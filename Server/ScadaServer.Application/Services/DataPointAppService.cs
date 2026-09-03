using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.ImportExport;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System.Text.RegularExpressions;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 变量模型（DataPoint，模型内变量模板）应用服务：负责模板的增删改查、批量导入/导出。
    /// 模板变更会影响引用它的设备实例，故增删改后需热加载相关设备运行时；
    /// 删除模板时会一并级联清理各设备上的实例化及关联脚本。
    /// </summary>
    public class DataPointAppService : IDataPointAppService
    {
        // 与实体/DbContext 唯一索引 ix_modelvariable_model_key 对应的键格式约束。
        // 用于兜底捕获并发/直写库触发的唯一索引冲突；应用层预检用于友好提示。
        private const string ModelKeyUniqueIndexName = "ix_modelvariable_model_key";
        private static readonly Regex KeyFormatRegex = new("^[a-zA-Z0-9_]+$", RegexOptions.Compiled);

        /// <summary>读写模式合法值（与前端枚举/列定义一致）。</summary>
        private static readonly HashSet<string> ValidAccessModes = new(StringComparer.Ordinal) { "Read", "Write", "ReadWrite" };

        /// <summary>模型变量仓储，提供持久化能力。</summary>
        private readonly IDataPointRepository _repository;
        /// <summary>数据模型仓储，用于校验变量所属模型存在。</summary>
        private readonly IDataModelRepository _modelRepository;
        /// <summary>设备变量仓储，用于级联清理及定位受影响设备。</summary>
        private readonly IDataPointMappingRepository _dataPointMappingRepository;
        /// <summary>设备仓储，用于脚本联动清理时解析设备键。</summary>
        private readonly IDeviceRepository _deviceRepository;
        /// <summary>系统脚本仓储，用于联动清理引用被删变量的脚本。</summary>
        private readonly ISystemScriptRepository _systemScriptRepository;
        /// <summary>运行时设备管理器，用于模板变更后热加载设备采集。</summary>
        private readonly IRuntimeDeviceManager _runtimeDeviceManager;
        /// <summary>工作单元，用于删除模板与实例化变量伴随的原子操作。</summary>
        private readonly IUnitOfWork _uow;
        /// <summary>导入解析器，用于解析变量导入文件。</summary>
        private readonly IVariableImportParser _importParser;
        /// <summary>导出服务，用于将变量模板导出为 CSV/XLSX。</summary>
        private readonly VariableExportService _exportService;

        /// <summary>构造函数：注入变量、模型、设备、脚本仓储及运行时、导入导出等服务。</summary>
        public DataPointAppService(
            IDataPointRepository repository,
            IDataModelRepository modelRepository,
            IDataPointMappingRepository dataPointMappingRepository,
            IDeviceRepository deviceRepository,
            ISystemScriptRepository systemScriptRepository,
            IRuntimeDeviceManager runtimeDeviceManager,
            IUnitOfWork uow,
            IVariableImportParser importParser,
            VariableExportService exportService)
        {
            _repository = repository;
            _modelRepository = modelRepository;
            _dataPointMappingRepository = dataPointMappingRepository;
            _deviceRepository = deviceRepository;
            _systemScriptRepository = systemScriptRepository;
            _runtimeDeviceManager = runtimeDeviceManager;
            _uow = uow;
            _importParser = importParser;
            _exportService = exportService;
        }

        public async Task<DataPointDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return DataPointMapper.ToDto(entity);
        }

        public async Task<List<DataPointDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(DataPointMapper.ToDto).ToList();
        }

        public async Task<List<DataPointDto>> GetByModelIdAsync(int modelId)
        {
            var list = await _repository.GetListAsync(mv => mv.ModelId == modelId);
            return list.Select(DataPointMapper.ToDto).ToList();
        }

        public async Task<DataPointDto> CreateAsync(DataPointDto dto)
        {
            // 0. 规范化
            dto.Key = dto.Key.Trim();
            dto.Name = dto.Name.Trim();

            // 1. 存在性检查：模型必须存在
            var model = await _modelRepository.GetByIdAsync(dto.ModelId);
            if (model == null)
            {
                throw new BusinessException($"ID 为 {dto.ModelId} 的数据模型不存在");
            }

            // 2. 深度业务校验
            ValidateVariableLogic(dto);

            // 3. 业务校验：在同一个模型下 Key 唯一
            var keyExists = await _repository.AnyAsync(v => v.ModelId == dto.ModelId && v.Key == dto.Key);
            if (keyExists)
            {
                throw new BusinessException($"模型内已存在标识为 '{dto.Key}' 的变量");
            }

            var entity = MapToEntity(dto);
            try
            {
                await _repository.InsertAsync(entity);
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex, ModelKeyUniqueIndexName))
            {
                // 并发竞态兜底：预检通过但落库时撞唯一索引
                throw new BusinessException($"模型内已存在标识为 '{dto.Key}' 的变量");
            }

            dto.Id = entity.Id;
            // 新建模板尚无设备实例，无需热加载运行时（新设备创建时会自动实例化）。
            return dto;
        }

        public async Task<DataPointDto> UpdateAsync(DataPointDto dto)
        {
            // 0. 规范化
            dto.Key = dto.Key.Trim();
            dto.Name = dto.Name.Trim();

            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null)
            {
                throw new BusinessException($"ID 为 {dto.Id} 的变量定义不存在");
            }

            // 1. 模型归属结构性不可变：禁止将变量"移动"到其它模型（迁移会破坏既有设备变量引用关系）。
            if (dto.ModelId != entity.ModelId)
            {
                throw new BusinessException($"不支持变更变量所属模型。如需迁移，请先删除原变量再到目标模型下重新创建。");
            }

            // 2. 获取模型以确认存在
            var model = await _modelRepository.GetByIdAsync(dto.ModelId);
            if (model == null)
            {
                throw new BusinessException($"ID 为 {dto.ModelId} 的数据模型不存在");
            }

            // 3. 深度业务校验
            ValidateVariableLogic(dto);

            // 4. Key 查重（排除自身）
            var keyExists = await _repository.AnyAsync(v => v.ModelId == dto.ModelId && v.Key == dto.Key && v.Id != dto.Id);
            if (keyExists)
            {
                throw new BusinessException($"模型内已存在标识为 '{dto.Key}' 的变量");
            }

            MapToEntity(dto, entity);
            try
            {
                await _repository.UpdateAsync(entity);
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex, ModelKeyUniqueIndexName))
            {
                throw new BusinessException($"模型内已存在标识为 '{dto.Key}' 的变量");
            }

            // 5. 变量模板配置（存储模式/周期/缩放/死区/只读）变更影响运行中的设备变量，热加载这些设备。
            await ReloadDevicesOfVariableAsync(dto.Id);

            return dto;
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return;

            List<int> affectedDeviceIds = new();
            await _uow.ExecuteInTransactionAsync(async _ =>
            {
                // 级联清理：删除所有设备上对该模板的实例化（含脚本联动），再删模板本身。
                // 数据库端 DataPoint → DataPointMapping 外键为 Restrict，必须在此显式先行删除，杜绝静默级联。
                var dataPointMappings = await _dataPointMappingRepository.GetListAsync(dv => dv.DataPointId == id);
                affectedDeviceIds = dataPointMappings.Select(dv => dv.DeviceId).Distinct().ToList();

                foreach (var dv in dataPointMappings)
                {
                    await ScriptVariableCleanupHelper.CleanupScriptsByVariableAsync(dv, _deviceRepository, _repository, _systemScriptRepository);
                }

                if (dataPointMappings.Count > 0)
                {
                    await _dataPointMappingRepository.DeleteRangeAsync(dv => dv.DataPointId == id);
                }

                await _repository.DeleteAsync(entity);

                return true;
            });

            // 事务提交成功后，受影响的设备热重载（运行时重建 Worker 与变量集合）。
            await ReloadDevicesAsync(affectedDeviceIds);
        }

        public async Task<VariableImportPreviewDto> ImportPreviewAsync(int modelId, Stream fileStream, string fileName)
        {
            await EnsureModelAsync(modelId);

            var rows = await _importParser.ParseAsync(fileStream, fileName);
            return await BuildPreviewAsync(modelId, rows);
        }

        public async Task<VariableImportResultDto> ImportAsync(int modelId, Stream fileStream, string fileName, ConflictStrategy strategy)
        {
            await EnsureModelAsync(modelId);

            // 解析文件（与预览同源，不信任前端回传的预览数据，防并发变更）
            var rows = await _importParser.ParseAsync(fileStream, fileName);
            // 复用与预览一致的冲突/去重标记逻辑
            var preview = await BuildPreviewAsync(modelId, rows);

            // Abort 策略：存在任一库内冲突即整体失败
            if (strategy == ConflictStrategy.Abort && rows.Any(r => !r.HasError && r.IsConflict))
            {
                var dup = rows.First(r => !r.HasError && r.IsConflict);
                throw new BusinessException($"模型内已存在变量 '{dup.Key}'，Abort 策略中断导入");
            }

            var affectedDeviceIds = new HashSet<int>();
            var result = await _uow.ExecuteInTransactionAsync<VariableImportResultDto>(async _ =>
            {
                var result = new VariableImportResultDto();
                var existingList = await _repository.GetListAsync(v => v.ModelId == modelId);
                var byKey = existingList.ToDictionary(v => v.Key, v => v, StringComparer.OrdinalIgnoreCase);

                var toInsert = new List<DataPoint>();
                var toUpdate = new List<DataPoint>();

                foreach (var row in preview.Rows)
                {
                    if (row.HasError)
                    {
                        result.Skipped++;
                        result.Failed++;
                        result.FailedRows.Add(row);
                        continue;
                    }

                    if (row.IsConflict)
                    {
                        if (strategy == ConflictStrategy.Skip)
                        {
                            result.Skipped++;
                            continue;
                        }
                        // Overwrite：更新已有变量
                        var existing = byKey[row.Key];
                        ApplyRowToEntity(row, existing);
                        toUpdate.Add(existing);
                        result.Updated++;
                        continue;
                    }

                    var entity = MapRowToEntity(modelId, row);
                    toInsert.Add(entity);
                    result.Inserted++;
                }

                try
                {
                    if (toInsert.Count > 0) await _repository.InsertRangeAsync(toInsert);
                    if (toUpdate.Count > 0) await _repository.UpdateRangeAsync(toUpdate);
                }
                catch (DbUpdateException ex) when (IsDuplicateKey(ex, ModelKeyUniqueIndexName))
                {
                    throw new BusinessException("导入失败：模型内变量键与现有数据冲突或文件内键重复，请刷新后重试");
                }

                // 收集被 Overwrite 更新变量的引用设备，事务提交后热重载。
                if (toUpdate.Count > 0)
                {
                    var ids = toUpdate.Select(v => v.Id).ToList();
                    var affectedVars = await _dataPointMappingRepository.GetListAsync(dv => ids.Contains(dv.DataPointId));
                    foreach (var dv in affectedVars) affectedDeviceIds.Add(dv.DeviceId);
                }

                return result;
            });

            // 事务提交成功后，热重载被覆盖更新变量的引用设备（模板存储/换算配置变更需重建 Worker）。
            if (affectedDeviceIds.Count > 0)
            {
                await ReloadDevicesAsync(affectedDeviceIds.ToList());
            }

            return result;
        }

        public async Task<byte[]> ExportAsync(int modelId, string format)
        {
            await EnsureModelAsync(modelId);

            var list = await _repository.GetListAsync(v => v.ModelId == modelId);
            var dtos = list.Select(DataPointMapper.ToDto).ToList();

            return format?.ToLowerInvariant() switch
            {
                "csv" => _exportService.ExportCsv(dtos),
                _ => _exportService.ExportXlsx(dtos)
            };
        }

        /// <summary>
        /// 查询引用该模板变量的所有设备并热重载其运行时（模板配置变更需重建设备 Worker）。
        /// </summary>
        private async Task ReloadDevicesOfVariableAsync(int dataPointId)
        {
            var dataPointMappings = await _dataPointMappingRepository.GetListAsync(dv => dv.DataPointId == dataPointId);
            await ReloadDevicesAsync(dataPointMappings.Select(dv => dv.DeviceId).Distinct().ToList());
        }

        private Task ReloadDevicesAsync(List<int> deviceIds)
        {
            // 运行时重载内部对异常做吞并记录，不会向上抛出，避免业务写操作失败被误判。
            foreach (var deviceId in deviceIds)
            {
                _ = _runtimeDeviceManager.ReloadDeviceAsync(deviceId);
            }
            return Task.CompletedTask;
        }

        private async Task<DataModel> EnsureModelAsync(int modelId)
        {
            var model = await _modelRepository.GetByIdAsync(modelId);
            if (model == null)
                throw new BusinessException($"ID 为 {modelId} 的数据模型不存在");
            return model;
        }

        /// <summary>
        /// 对解析行做模型内冲突比对与文件内重复标记，并聚合统计。不查库不做写入。
        /// Key 判定遵循数据库 collation（MySQL 默认 ci），统一不区分大小写。
        /// </summary>
        private async Task<VariableImportPreviewDto> BuildPreviewAsync(int modelId, List<VariableImportRow> rows)
        {
            var existingList = await _repository.GetListAsync(v => v.ModelId == modelId);
            var existingKeys = existingList.Select(v => v.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (row.HasError) continue;

                if (row.Key.Length > 50)
                {
                    row.SetError($"变量标识 '{row.Key}' 超过50个字符");
                    continue;
                }
                if (!KeyFormatRegex.IsMatch(row.Key))
                {
                    row.SetError($"变量标识 '{row.Key}' 只能包含字母、数字和下划线");
                    continue;
                }

                // 文件内 Key 重复（不区分大小写）：首个保留，后续标记为错误，避免唯一冲突
                if (!seen.Add(row.Key))
                {
                    row.SetError($"文件内存在重复变量 '{row.Key}'");
                    continue;
                }

                row.IsConflict = existingKeys.Contains(row.Key);
            }

            return new VariableImportPreviewDto
            {
                ModelId = modelId,
                TotalRows = rows.Count,
                ErrorRows = rows.Count(r => r.HasError),
                ConflictRows = rows.Count(r => !r.HasError && r.IsConflict),
                ValidRows = rows.Count(r => !r.HasError && !r.IsConflict),
                Rows = rows
            };
        }

        /// <summary>
        /// 将导入行映射为新建实体并应用各字段默认值（复用 MapToEntity 的默认值逻辑）。
        /// </summary>
        private static DataPoint MapRowToEntity(int modelId, VariableImportRow row)
        {
            var varName = string.IsNullOrWhiteSpace(row.Name) ? row.Key : row.Name;
            var dto = new DataPointDto
            {
                ModelId = modelId,
                Key = row.Key,
                Name = varName,
                DataType = row.DataType,
                Unit = row.Unit,
                Min = row.Min,
                Max = row.Max,
                Description = row.Description,
                IsStored = row.StoreMode == null || row.StoreMode != StoreModeEnum.None,
                StoreMode = row.StoreMode ?? StoreModeEnum.Change,
                StoreIntervalMs = row.StoreIntervalMs is > 0 ? row.StoreIntervalMs.Value : 300000,
                UpdateMode = row.UpdateMode ?? UpdateMode.Polling,
                ScaleExpression = row.ScaleExpression,
                DeadBand = row.DeadBand,
                // 读写模式：AccessMode 列优先（导出模板会带出）；缺列时按 IsReadOnly 旧列推导（兼容旧文件）
                AccessMode = row.AccessMode,
                IsReadOnly = row.IsReadOnly ?? true,
                IsRequired = row.IsRequired ?? false,
                Sort = row.Sort ?? 0,
                IsEnabled = row.IsEnabled ?? true,
                ExtensionData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            // TIA 逻辑地址存入 ExtensionData，供设备实例化时自动带出
            if (!string.IsNullOrWhiteSpace(row.Address))
            {
                dto.ExtensionData["address"] = row.Address;
            }

            return MapToEntity(dto);
        }

        /// <summary>
        /// 将导入行应用到已有实体（Overwrite 策略），仅更新文件提供的字段，未提供字段保持原值。
        /// </summary>
        private static void ApplyRowToEntity(VariableImportRow row, DataPoint entity)
        {
            if (!string.IsNullOrWhiteSpace(row.Name)) entity.Name = row.Name;
            entity.DataType = row.DataType;
            if (row.Unit is not null) entity.Unit = row.Unit;
            if (row.Min is not null) entity.Min = row.Min;
            if (row.Max is not null) entity.Max = row.Max;
            if (row.Description is not null) entity.Description = row.Description;
            if (row.StoreMode is not null && row.StoreMode.Value != StoreModeEnum.None)
            {
                // 仅当显式指定且非 None 时覆盖存储周期；None 时周期无意义，保持原值
                if (row.StoreIntervalMs is > 0) entity.StoreIntervalMs = row.StoreIntervalMs.Value;
            }
            entity.StoreMode = row.StoreMode ?? entity.StoreMode;
            if (row.StoreIntervalMs is > 0) entity.StoreIntervalMs = row.StoreIntervalMs.Value;
            if (row.UpdateMode is not null) entity.UpdateMode = row.UpdateMode.Value;
            if (row.ScaleExpression is not null) entity.ScaleExpression = row.ScaleExpression;
            if (row.DeadBand is not null) entity.DeadBand = row.DeadBand;
            if (row.IsReadOnly is not null) entity.IsReadOnly = row.IsReadOnly.Value;
            // 阶段 4：AccessMode 权威（Overwrite 仅覆盖显式提供的字段）；与 IsReadOnly 同步单点。
            if (row.AccessMode is not null && (row.AccessMode == "Read" || row.AccessMode == "Write" || row.AccessMode == "ReadWrite"))
            {
                entity.AccessMode = row.AccessMode;
                entity.IsReadOnly = row.AccessMode == "Read";
            }
            if (row.IsRequired is not null) entity.IsRequired = row.IsRequired.Value;
            if (row.Sort is not null) entity.Sort = row.Sort.Value;
            if (row.IsEnabled is not null) entity.IsEnabled = row.IsEnabled.Value;

            // 地址：仅在新数据非空时覆盖（避免覆盖历史已有地址为空数据）
            if (!string.IsNullOrWhiteSpace(row.Address))
            {
                entity.ExtensionData ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                entity.ExtensionData["address"] = row.Address;
            }
        }

        private void ValidateVariableLogic(DataPointDto dto)
        {
            // A. 历史存储检查
            if (dto.StoreMode == StoreModeEnum.None && dto.IsStored)
            {
                throw new BusinessException("已勾选\"存储历史\"但存储模式为 None，请选择 Change/Cycle 等具体模式");
            }

            // B. 存储周期下限校验（避免误配为极小值导致海量写入）；None 模式周期无意义不校验
            if (dto.StoreMode != StoreModeEnum.None && dto.StoreIntervalMs < 1000)
            {
                throw new BusinessException("历史存储周期不能小于 1000ms（1 秒）");
            }

            // C. 量程校验：最小值不能大于最大值
            if (dto.Min.HasValue && dto.Max.HasValue && dto.Min.Value > dto.Max.Value)
            {
                throw new BusinessException($"变量 '{dto.Name}' 的最小值（{dto.Min}）不能大于最大值（{dto.Max}）");
            }

            // D. 工程换算表达式校验：长度、字符/函数白名单、语法可解析（校验阶段不执行表达式）。
            var scaleError = ScaleExpressionValidator.Validate(dto.ScaleExpression);
            if (scaleError != null)
            {
                throw new BusinessException($"变量 '{dto.Name}' 的换算表达式非法：{scaleError}");
            }

            // E. 读写模式校验：仅当显式传入且非法时拒绝（可空 = 交给 IsReadOnly 旧列推导，兼容旧客户端）
            var accessMode = dto.AccessMode?.Trim();
            if (!string.IsNullOrEmpty(accessMode) && !ValidAccessModes.Contains(accessMode))
            {
                throw new BusinessException($"变量 '{dto.Name}' 的读写模式非法：'{accessMode}'（可选 Read / Write / ReadWrite）");
            }

            // 数据类型合法性由枚举 + DTO JsonConverter 保证；信号类型由 DataType 派生，无需额外运行时校验。
        }

        /// <summary>
        /// 归一化读写模式（阶段 4 权威解析，唯一入口）：
        /// AccessMode 显式合法 → 取之；否则按旧列 IsReadOnly 推导（true=Read，false=ReadWrite，缺省 Read）。
        /// </summary>
        private static string ResolveAccessMode(string? accessMode, bool legacyIsReadOnly)
        {
            var mode = accessMode?.Trim();
            if (!string.IsNullOrEmpty(mode) && ValidAccessModes.Contains(mode))
            {
                return mode;
            }
            return legacyIsReadOnly ? "Read" : "ReadWrite";
        }

        private static DataPoint MapToEntity(DataPointDto dto, DataPoint? entity = null)
        {
            entity ??= new DataPoint();
            entity.ModelId = dto.ModelId;
            entity.Key = dto.Key;
            entity.Name = dto.Name;
            entity.DataType = dto.DataType;
            entity.Unit = dto.Unit;
            entity.Min = dto.Min;
            entity.Max = dto.Max;
            entity.Description = dto.Description;
            entity.StoreMode = dto.StoreMode;
            entity.StoreIntervalMs = dto.StoreIntervalMs;
            entity.UpdateMode = dto.UpdateMode;
            entity.ScaleExpression = dto.ScaleExpression;
            entity.DeadBand = dto.DeadBand;
            // 阶段 4 权限同步单点：AccessMode 为权威列，IsReadOnly 兼容列随之同步（两列永不矛盾）。
            entity.AccessMode = ResolveAccessMode(dto.AccessMode, dto.IsReadOnly);
            entity.IsReadOnly = entity.AccessMode == "Read";
            entity.IsRequired = dto.IsRequired;
            entity.Sort = dto.Sort;
            entity.IsEnabled = dto.IsEnabled;
            entity.ExtensionData = dto.ExtensionData ?? new Dictionary<string, string>();
            return entity;
        }

        /// <summary>
        /// 判断 EF 保存异常是否为指定唯一索引名下的重复键冲突（MySql 错误码 1062）。
        /// </summary>
        private static bool IsDuplicateKey(DbUpdateException ex, string indexName)
        {
            var inner = ex.GetBaseException();
            if (inner is MySqlException mySql && mySql.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
            {
                return mySql.Message.Contains(indexName, StringComparison.Ordinal)
                    || mySql.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase);
            }
            // 兜底：DB 无关的错误码时按消息判定
            return inner is MySqlException m2 && m2.Number == 1062;
        }
    }
}