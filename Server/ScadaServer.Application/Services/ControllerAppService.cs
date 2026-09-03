using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 控制器应用服务（阶段 2，控制器/PLC 资产台账；阶段 3 完善引用语义）。
    /// 仅资产登记（CRUD/列表/下拉），不产生任何采集行为。
    /// 阶段 3：返回连接数量（ConnectionCount）；删除时校验"存在连接或设备引用则拒绝"。
    /// </summary>
    public class ControllerAppService : IControllerAppService
    {
        private readonly IControllerRepository _repository;
        private readonly IProtocolRepository _protocolRepository;
        /// <summary>设备连接仓储（阶段 3：统计连接数 + 删除引用校验）。</summary>
        private readonly IDeviceConnectionRepository _connectionRepository;
        /// <summary>设备仓储（阶段 3：删除引用校验——设备可经 ControllerId 直接引用控制器）。</summary>
        private readonly IDeviceRepository _deviceRepository;

        public ControllerAppService(
            IControllerRepository repository,
            IProtocolRepository protocolRepository,
            IDeviceConnectionRepository connectionRepository,
            IDeviceRepository deviceRepository)
        {
            _repository = repository;
            _protocolRepository = protocolRepository;
            _connectionRepository = connectionRepository;
            _deviceRepository = deviceRepository;
        }

        /// <summary>按主键获取控制器（含连接数），不存在时返回 null。</summary>
        public async Task<ControllerDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;

            var dto = MapToDto(entity);
            dto.ConnectionCount = await _connectionRepository.CountAsync(c => c.ControllerId == id);
            return dto;
        }

        /// <summary>获取全部控制器列表（含各控制器连接数）。</summary>
        public async Task<List<ControllerDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            var counts = await _connectionRepository.GetCountsByControllerAsync();
            return list
                .Select(entity =>
                {
                    var dto = MapToDto(entity);
                    dto.ConnectionCount = counts.GetValueOrDefault(entity.Id);
                    return dto;
                })
                .ToList();
        }

        /// <summary>按协议/关键字过滤 + 分页查询控制器（含各控制器连接数）。</summary>
        public async Task<ControllerPagedResultDto> QueryAsync(ControllerQueryDto query)
        {
            var pageIndex = Math.Max(1, query.PageIndex);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);

            var (total, items) = await _repository.QueryAsync(
                query.ProtocolId,
                query.Keyword,
                pageIndex,
                pageSize);

            // 单次 GroupBy 查询取全部控制器连接数分布，内存填充本页 DTO，避免每条记录一次 Count 查询。
            var counts = await _connectionRepository.GetCountsByControllerAsync();

            return new ControllerPagedResultDto
            {
                Total = total,
                Items = items
                    .Select(entity =>
                    {
                        var dto = MapToDto(entity);
                        dto.ConnectionCount = counts.GetValueOrDefault(entity.Id);
                        return dto;
                    })
                    .ToList()
            };
        }

        /// <summary>下拉数据源（Id+Code+Name+Protocol），用于前端下拉选择。</summary>
        public async Task<List<ControllerOptionDto>> GetOptionsAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(c => new ControllerOptionDto
            {
                Id = c.Id,
                Code = c.Code,
                Name = c.Name,
                ProtocolId = c.ProtocolId,
                ProtocolName = c.Protocol?.Name ?? string.Empty
            }).ToList();
        }

        /// <summary>新增控制器：校验编码唯一性与协议存在性后写入，返回最新 DTO。</summary>
        public async Task<ControllerDto> CreateAsync(CreateControllerDto dto)
        {
            // 0. 规范化（[Required] 已在控制器校验非空，此处防御性兜底为空串）
            var code = dto.Code?.Trim() ?? string.Empty;
            var name = dto.Name?.Trim() ?? string.Empty;

            // 1. 编码唯一性校验
            if (await _repository.AnyAsync(c => c.Code == code))
            {
                throw new BusinessException($"控制器编码 '{code}' 已存在");
            }

            // 2. 协议存在性校验（类型即协议；FK Restrict 前给出友好提示）
            await EnsureProtocolExistsAsync(dto.ProtocolId);

            var entity = new Controller
            {
                Code = code,
                Name = name,
                ProtocolId = dto.ProtocolId,
                Manufacturer = dto.Manufacturer?.Trim(),
                Model = dto.Model?.Trim(),
                Description = dto.Description?.Trim(),
                IsEnabled = dto.IsEnabled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _repository.InsertAsync(entity);
            return MapToDto(entity);
        }

        /// <summary>更新控制器：校验存在性、编码唯一性（排除自身）与协议存在性后写入，返回最新 DTO。</summary>
        public async Task<ControllerDto> UpdateAsync(int id, CreateControllerDto dto)
        {
            var entity = await _repository.GetByIdForUpdateAsync(id);
            if (entity == null)
            {
                throw new BusinessException($"ID 为 {id} 的控制器不存在");
            }

            var code = dto.Code?.Trim() ?? string.Empty;
            var name = dto.Name?.Trim() ?? string.Empty;

            if (await _repository.AnyAsync(c => c.Code == code && c.Id != id))
            {
                throw new BusinessException($"控制器编码 '{code}' 已存在");
            }

            await EnsureProtocolExistsAsync(dto.ProtocolId);

            entity.Code = code;
            entity.Name = name;
            entity.ProtocolId = dto.ProtocolId;
            entity.Manufacturer = dto.Manufacturer?.Trim();
            entity.Model = dto.Model?.Trim();
            entity.Description = dto.Description?.Trim();
            entity.IsEnabled = dto.IsEnabled;
            entity.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(entity);

            return MapToDto(entity);
        }

        /// <summary>删除控制器：记录不存在时静默忽略；存在连接或设备引用时拒绝（阶段 3 引用校验）。</summary>
        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdForUpdateAsync(id);
            if (entity == null) return;

            // 阶段 3 引用校验：控制器下挂有连接、或有设备经 ControllerId 引用时禁止删除。
            // （Devices.ControllerId / DeviceConnections.ControllerId 均为 Restrict 外键，提前给出友好提示。）
            var hasConnections = await _connectionRepository.AnyAsync(c => c.ControllerId == id);
            if (hasConnections)
            {
                throw new BusinessException($"无法删除控制器 '{entity.Name}'：控制器下仍存在连接，请先删除其连接");
            }

            var hasDeviceReference = await _deviceRepository.AnyAsync(d => d.ControllerId == id);
            if (hasDeviceReference)
            {
                throw new BusinessException($"无法删除控制器 '{entity.Name}'：仍有设备经 ControllerId 引用该控制器，请先在设备管理页解除引用");
            }

            await _repository.DeleteAsync(entity);
        }

        /// <summary>协议存在性校验（类型即协议；协议被禁用时仍允许登记，仅校验存在）。</summary>
        private async Task EnsureProtocolExistsAsync(int protocolId)
        {
            var exists = await _protocolRepository.AnyAsync(p => p.Id == protocolId);
            if (!exists)
            {
                throw new BusinessException($"所选协议（ID={protocolId}）不存在，请重新选择");
            }
        }

        /// <summary>将控制器实体映射为 DTO。</summary>
        private static ControllerDto MapToDto(Controller entity) => new()
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            ProtocolId = entity.ProtocolId,
            ProtocolName = entity.Protocol?.Name ?? string.Empty,
            Manufacturer = entity.Manufacturer,
            Model = entity.Model,
            Description = entity.Description,
            IsEnabled = entity.IsEnabled,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
