using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 数据转发（源/目标变量映射）应用服务实现：负责数据转换规则的增删改查（CRUD）。
    /// 规则定义源设备变量到目标设备变量的读取/写入映射，实际转换由运行时引擎执行。
    /// </summary>
    public class DataConversionAppService : IDataConversionAppService
    {
        /// <summary>数据转换仓储，提供持久化能力。</summary>
        private readonly IDataConversionRepository _repository;
        /// <summary>设备仓储，用于保存期校验设备存在性及启用状态。</summary>
        private readonly IDeviceRepository _deviceRepository;
        /// <summary>设备变量应用服务，用于解析源/目标变量的实例配置（存在性/启用/读写模式）。</summary>
        private readonly IDataPointMappingAppService _dataPointMappingAppService;

        /// <summary>构造函数：注入数据转换仓储、设备仓储与设备变量应用服务。</summary>
        public DataConversionAppService(
            IDataConversionRepository repository,
            IDeviceRepository deviceRepository,
            IDataPointMappingAppService dataPointMappingAppService)
        {
            _repository = repository;
            _deviceRepository = deviceRepository;
            _dataPointMappingAppService = dataPointMappingAppService;
        }

        /// <summary>按主键获取数据转换规则，不存在时返回 null。</summary>
        public async Task<DataConversionDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new DataConversionDto
            {
                Id = entity.Id,
                Name = entity.Name,
                SourceDeviceId = entity.SourceDeviceId,
                SourceVariableKey = entity.SourceVariableKey,
                TargetDeviceId = entity.TargetDeviceId,
                TargetVariableKey = entity.TargetVariableKey,
                Active = entity.Active,
                OutOfRangePolicy = entity.OutOfRangePolicy
            };
        }

        /// <summary>获取全部数据转换规则列表。</summary>
        public async Task<List<DataConversionDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new DataConversionDto
            {
                Id = entity.Id,
                Name = entity.Name,
                SourceDeviceId = entity.SourceDeviceId,
                SourceVariableKey = entity.SourceVariableKey,
                TargetDeviceId = entity.TargetDeviceId,
                TargetVariableKey = entity.TargetVariableKey,
                Active = entity.Active,
                OutOfRangePolicy = entity.OutOfRangePolicy
            }).ToList();
        }

        /// <summary>新增数据转换规则，并将生成的主键写回 DTO。</summary>
        public async Task CreateAsync(DataConversionDto dto)
        {
            await ValidateAsync(dto);

            var entity = new DataConversion
            {
                Name = dto.Name,
                SourceDeviceId = dto.SourceDeviceId,
                SourceVariableKey = dto.SourceVariableKey,
                TargetDeviceId = dto.TargetDeviceId,
                TargetVariableKey = dto.TargetVariableKey,
                Active = dto.Active,
                OutOfRangePolicy = NormalizePolicy(dto.OutOfRangePolicy)
            };
            await _repository.InsertAsync(entity);
            // InsertAsync 内部 SaveChangesAsync 后自增 Id 已回填到实体，同步回写 DTO 供接口返回
            dto.Id = entity.Id;
        }

        /// <summary>更新数据转换规则；记录不存在时静默忽略。</summary>
        public async Task UpdateAsync(DataConversionDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                await ValidateAsync(dto);

                entity.Name = dto.Name;
                entity.SourceDeviceId = dto.SourceDeviceId;
                entity.SourceVariableKey = dto.SourceVariableKey;
                entity.TargetDeviceId = dto.TargetDeviceId;
                entity.TargetVariableKey = dto.TargetVariableKey;
                entity.Active = dto.Active;
                entity.OutOfRangePolicy = NormalizePolicy(dto.OutOfRangePolicy);
                await _repository.UpdateAsync(entity);
            }
        }

        /// <summary>删除数据转换规则；记录不存在时静默忽略。</summary>
        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
            }
        }

        /// <summary>归一化越限策略：仅接受 Clamp / Reject，其余（含 null/空）回退 Clamp。</summary>
        private static string NormalizePolicy(string? policy) =>
            string.Equals(policy, "Reject", StringComparison.OrdinalIgnoreCase) ? "Reject" : "Clamp";

        /// <summary>
        /// 保存期静态校验（根因 C）：在持久化前拦截"源/目标设备或变量缺失、禁用、目标只读"的非法规则，
        /// 使非法规则在保存时即被拒绝而非静默失效。仅校验静态配置合法性，不拦截设备运行态（连接/未运行），
        /// 该部分交由运行时 pending 机制（根因 B）处理。
        /// <para>目标变量可写性判定复用运行时 <see cref="IDataPointMappingAppService"/> 产出
        /// 的 EffectiveAccessMode 语义（实例覆盖优先、回退模板），保证保存期与运行期判定一致。</para>
        /// </summary>
        private async Task ValidateAsync(DataConversionDto dto)
        {
            // 源设备存在性 + 启用
            var sourceDevice = await _deviceRepository.GetByIdAsync(dto.SourceDeviceId)
                ?? throw new BusinessException($"源设备（ID {dto.SourceDeviceId}）不存在");
            if (!sourceDevice.IsEnabled)
            {
                throw new BusinessException($"源设备 '{sourceDevice.Name}' 已禁用，无法作为数据转换源");
            }

            // 目标设备存在性 + 启用
            var targetDevice = await _deviceRepository.GetByIdAsync(dto.TargetDeviceId)
                ?? throw new BusinessException($"目标设备（ID {dto.TargetDeviceId}）不存在");
            if (!targetDevice.IsEnabled)
            {
                throw new BusinessException($"目标设备 '{targetDevice.Name}' 已禁用，无法作为数据转换目标");
            }

            // 源变量存在性 + 启用
            var sourceVar = (await _dataPointMappingAppService.GetByDeviceAsync(dto.SourceDeviceId))
                .FirstOrDefault(v => v.Key == dto.SourceVariableKey);
            if (sourceVar == null)
            {
                throw new BusinessException($"源设备 '{sourceDevice.Name}' 下不存在变量 [{dto.SourceVariableKey}]");
            }
            if (!sourceVar.IsEnabled)
            {
                throw new BusinessException($"源变量 [{dto.SourceVariableKey}] 已禁用，无法作为数据转换源");
            }

            // 目标变量存在性 + 启用 + 可写
            var targetVar = (await _dataPointMappingAppService.GetByDeviceAsync(dto.TargetDeviceId))
                .FirstOrDefault(v => v.Key == dto.TargetVariableKey);
            if (targetVar == null)
            {
                throw new BusinessException($"目标设备 '{targetDevice.Name}' 下不存在变量 [{dto.TargetVariableKey}]");
            }
            if (!targetVar.IsEnabled)
            {
                throw new BusinessException($"目标变量 [{dto.TargetVariableKey}] 已禁用，无法作为数据转换目标");
            }
            if (targetVar.EffectiveAccessMode == "Read")
            {
                throw new BusinessException($"目标变量 [{dto.TargetVariableKey}] 为只读，禁止作为数据转换目标");
            }

            // 源/目标变量数据类型兼容性校验（类型兼容矩阵见 DataTypeCompatibility）：
            // 拦截"布尔↔数值、文本↔其他"等跨大类规则，避免运行期驱动转换失败或值语义错乱。
            if (!DataTypeCompatibility.IsCompatible(sourceVar.DataType, targetVar.DataType))
            {
                throw new BusinessException(
                    $"源变量 [{dto.SourceVariableKey}]({sourceVar.DataType}) 与目标变量 [{dto.TargetVariableKey}]({targetVar.DataType}) " +
                    $"数据类型不兼容（{DataTypeCompatibility.DescribeCategory(sourceVar.DataType)}↔{DataTypeCompatibility.DescribeCategory(targetVar.DataType)}），禁止建立转换规则");
            }
        }
    }
}

