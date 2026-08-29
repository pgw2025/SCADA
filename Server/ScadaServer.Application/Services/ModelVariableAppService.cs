using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.ImportExport;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Enums;
using System.Text.RegularExpressions;

namespace ScadaServer.Application.Services
{
    public class ModelVariableAppService : IModelVariableAppService
    {
        private readonly IModelVariableRepository _repository;
        private readonly IDataModelRepository _modelRepository;
        private readonly IUnitOfWork _uow;
        private readonly IVariableImportParser _importParser;
        private readonly VariableExportService _exportService;

        public ModelVariableAppService(
            IModelVariableRepository repository,
            IDataModelRepository modelRepository,
            IUnitOfWork uow,
            IVariableImportParser importParser,
            VariableExportService exportService)
        {
            _repository = repository;
            _modelRepository = modelRepository;
            _uow = uow;
            _importParser = importParser;
            _exportService = exportService;
        }

        public async Task<ModelVariableDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return MapToDto(entity);
        }

        public async Task<List<ModelVariableDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task<List<ModelVariableDto>> GetByModelIdAsync(int modelId)
        {
            var list = await _repository.GetListAsync(mv => mv.ModelId == modelId);
            return list.Select(MapToDto).ToList();
        }

        public async Task<ModelVariableDto> CreateAsync(ModelVariableDto dto)
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

            // 2. 深度业务校验（协议真相源在 Device.Type，此处不再按协议校验地址格式）
            ValidateVariableLogic(dto);

            // 3. 业务校验：在同一个模型下 Key 和 Address 必须唯一
            var keyExists = await _repository.AnyAsync(v => v.ModelId == dto.ModelId && v.Key == dto.Key);
            if (keyExists)
            {
                throw new BusinessException($"模型内已存在标识为 '{dto.Key}' 的变量");
            }

            var entity = MapToEntity(dto);
            await _repository.InsertAsync(entity);
            
            dto.Id = entity.Id; 
            return dto;
        }

        public async Task<ModelVariableDto> UpdateAsync(ModelVariableDto dto)
        {
            // 0. 规范化
            dto.Key = dto.Key.Trim();
            dto.Name = dto.Name.Trim();

            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null)
            {
                throw new BusinessException($"ID 为 {dto.Id} 的变量定义不存在");
            }

            // 1. 获取模型以获知协议类型
            var model = await _modelRepository.GetByIdAsync(dto.ModelId);
            if (model == null)
            {
                throw new BusinessException($"ID 为 {dto.ModelId} 的数据模型不存在");
            }

            // 2. 深度业务校验（协议真相源在 Device.Type，此处不再按协议校验地址格式）
            ValidateVariableLogic(dto);

            // 3. 业务校验：Key 查重（排除自身）
            var keyExists = await _repository.AnyAsync(v => v.ModelId == dto.ModelId && v.Key == dto.Key && v.Id != dto.Id);
            if (keyExists)
            {
                throw new BusinessException($"模型内已存在标识为 '{dto.Key}' 的变量");
            }

            MapToEntity(dto, entity);
            await _repository.UpdateAsync(entity);
            
            return dto;
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return;

            await _repository.DeleteAsync(entity);
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

            return await _uow.ExecuteInTransactionAsync<VariableImportResultDto>(async scope =>
            {
                var result = new VariableImportResultDto();
                var existingList = await _repository.GetListAsync(v => v.ModelId == modelId);
                var byKey = existingList.ToDictionary(v => v.Key, v => v);

                var toInsert = new List<ModelVariable>();
                var toUpdate = new List<ModelVariable>();
                // 文件内重复：仅首个有效（重复已在 preview 中标记为错误并被排除）

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

                if (toInsert.Count > 0) await _repository.InsertRangeAsync(toInsert);
                if (toUpdate.Count > 0) await _repository.UpdateRangeAsync(toUpdate);

                return result;
            });
        }

        public async Task<byte[]> ExportAsync(int modelId, string format)
        {
            await EnsureModelAsync(modelId);

            var list = await _repository.GetListAsync(v => v.ModelId == modelId);
            var dtos = list.Select(MapToDto).ToList();

            return format?.ToLowerInvariant() switch
            {
                "csv" => _exportService.ExportCsv(dtos),
                _ => _exportService.ExportXlsx(dtos)
            };
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
        /// </summary>
        private async Task<VariableImportPreviewDto> BuildPreviewAsync(int modelId, List<VariableImportRow> rows)
        {
            var existingList = await _repository.GetListAsync(v => v.ModelId == modelId);
            var existingKeys = existingList.Select(v => v.Key).ToHashSet();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (row.HasError) continue;

                // 文件内 Key 重复：首个保留，后续标记为错误，避免主键冲突
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
        private static ModelVariable MapRowToEntity(int modelId, VariableImportRow row)
        {
            var varName = string.IsNullOrWhiteSpace(row.Name) ? row.Key : row.Name;
            var dto = new ModelVariableDto
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
                StoreIntervalMs = row.StoreIntervalMs is >= 0 ? row.StoreIntervalMs.Value : 300000,
                UpdateMode = row.UpdateMode ?? UpdateMode.Polling,
                ScaleSlope = row.ScaleSlope ?? 1.0,
                ScaleOffset = row.ScaleOffset ?? 0.0,
                DeadBand = row.DeadBand,
                IsReadOnly = row.IsReadOnly ?? true,
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
        private static void ApplyRowToEntity(VariableImportRow row, ModelVariable entity)
        {
            if (!string.IsNullOrWhiteSpace(row.Name)) entity.Name = row.Name;
            entity.DataType = row.DataType;
            if (row.Unit is not null) entity.Unit = row.Unit;
            if (row.Min is not null) entity.Min = row.Min;
            if (row.Max is not null) entity.Max = row.Max;
            if (row.Description is not null) entity.Description = row.Description;
            if (row.StoreMode is not null) entity.StoreMode = row.StoreMode.Value;
            if (row.StoreIntervalMs is not null) entity.StoreIntervalMs = row.StoreIntervalMs.Value;
            if (row.UpdateMode is not null) entity.UpdateMode = row.UpdateMode.Value;
            if (row.ScaleSlope is not null) entity.ScaleSlope = row.ScaleSlope.Value;
            if (row.ScaleOffset is not null) entity.ScaleOffset = row.ScaleOffset.Value;
            if (row.DeadBand is not null) entity.DeadBand = row.DeadBand;
            if (row.IsReadOnly is not null) entity.IsReadOnly = row.IsReadOnly.Value;

            // 地址：仅在新数据非空时覆盖（避免覆盖历史已有地址为空数据）
            if (!string.IsNullOrWhiteSpace(row.Address))
            {
                entity.ExtensionData ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                entity.ExtensionData["address"] = row.Address;
            }
        }

        private void ValidateVariableLogic(ModelVariableDto dto)
        {
            // A. 类型匹配校验（信号类型由 DataType 推导，此处直接校验 DataType 合法性）
            var signalType = (dto.DataType == DataTypeEnum.BIT || dto.DataType == DataTypeEnum.BOOL)
                ? VariableType.Digital
                : VariableType.Analog;
            if (signalType == VariableType.Digital && dto.DataType != DataTypeEnum.BOOL && dto.DataType != DataTypeEnum.BIT)
            {
                throw new BusinessException("数字量性质的变量，数据类型必须为 BOOL 或 BIT");
            }

            // B. 地址格式校验已下放到前端按协议拦截 + 运行时驱动；此处不再强制非空。
            //    虚拟/计算类变量本就无物理地址,允许空地址;地址唯一性由调用方在地址非空时校验。

            // C. 历史存储检查
            if (dto.StoreMode == StoreModeEnum.None && dto.IsStored)
            {
                throw new BusinessException("已勾选\"存储历史\"但存储模式为 None，请选择 Change/Cycle 等具体模式");
            }

            // D. 存储周期下限校验（避免误配为极小值导致海量写入）
            if (dto.StoreMode != StoreModeEnum.None && dto.StoreIntervalMs < 1000)
            {
                throw new BusinessException("历史存储周期不能小于 1000ms（1 秒）");
            }
        }

        private static ModelVariableDto MapToDto(ModelVariable entity) => new()
        {
            Id = entity.Id,
            ModelId = entity.ModelId,
            Key = entity.Key,
            Name = entity.Name,
            Type = entity.Type,
            DataType = entity.DataType,
            Unit = entity.Unit,
            Min = entity.Min,
            Max = entity.Max,
            Description = entity.Description,
            IsStored = entity.IsStored,
            StoreMode = entity.StoreMode,
            StoreIntervalMs = entity.StoreIntervalMs,
            UpdateMode = entity.UpdateMode,
            ScaleSlope = entity.ScaleSlope,
            ScaleOffset = entity.ScaleOffset,
            DeadBand = entity.DeadBand,
            IsReadOnly = entity.IsReadOnly,
            ExtensionData = entity.ExtensionData
        };

        private static ModelVariable MapToEntity(ModelVariableDto dto, ModelVariable? entity = null)
        {
            entity ??= new ModelVariable();
            entity.ModelId = dto.ModelId;
            entity.Key = dto.Key;
            entity.Name = dto.Name;
            entity.DataType = dto.DataType;
            entity.Unit = dto.Unit;
            entity.Min = dto.Min;
            entity.Max = dto.Max;
            // 注(P1-5)：Address / BitOffset / PollingIntervalMs 已迁移至 DeviceVariable，
            // 模板层不再写回；地址、采集周期等采集细节统一在设备实例层维护。
            entity.Description = dto.Description;
            entity.StoreMode = dto.StoreMode;
            entity.StoreIntervalMs = dto.StoreIntervalMs;
            entity.UpdateMode = dto.UpdateMode;
            entity.ScaleSlope = dto.ScaleSlope;
            entity.ScaleOffset = dto.ScaleOffset;
            entity.DeadBand = dto.DeadBand;
            entity.IsReadOnly = dto.IsReadOnly;
            entity.ExtensionData = dto.ExtensionData ?? new Dictionary<string, string>();
            return entity;
        }
    }
}

