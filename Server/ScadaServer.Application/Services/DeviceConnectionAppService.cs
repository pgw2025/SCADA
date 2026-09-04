using System.Text.Json;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 设备连接应用服务实现（阶段 3：连接/控制器管理 API）。
    /// <para>
    /// 负责 DeviceConnection 资产的管理：按控制器查询、CRUD。连接配置遵循 P3-B：
    /// <see cref="CreateDeviceConnectionDto.ConfigJson"/> 保存驱动完整配置原文（真相源），
    /// Host/Port/TimeoutMs 为按协议从 ConfigJson 提取的冗余列，由本服务统一重算，
    /// 与 <see cref="DeviceConnectionProfile"/>（双写/回填共用）算法一致，保证展示列始终与配置原文吻合。
    /// </para>
    /// <para>
    /// 引用语义（与 DeviceDeletionService 对称）：
    /// 连接被设备引用（Device.ConnectionId 指向）后，其生命周期移交设备接口管理（改参/删除走设备页，
    /// Connection.ConfigJson 即连接配置唯一真相源，阶段 6 起不再与 Device.JsonConfig 双写）；
    /// 故对"被引用连接"的更新与删除直接拒绝，避免绕过设备接口破坏端点唯一性与共享语义。
    /// 删除未被引用的连接后，若其控制器无其它连接且无设备引用，则一并清理。
    /// </para>
    /// </summary>
    public class DeviceConnectionAppService : IDeviceConnectionAppService
    {
        private readonly IDeviceConnectionRepository _repository;
        private readonly IControllerRepository _controllerRepository;
        private readonly IProtocolRepository _protocolRepository;
        private readonly IDeviceRepository _deviceRepository;
        private readonly IRuntimeDeviceManager _runtimeDeviceManager;

        public DeviceConnectionAppService(
            IDeviceConnectionRepository repository,
            IControllerRepository controllerRepository,
            IProtocolRepository protocolRepository,
            IDeviceRepository deviceRepository,
            IRuntimeDeviceManager runtimeDeviceManager)
        {
            _repository = repository;
            _controllerRepository = controllerRepository;
            _protocolRepository = protocolRepository;
            _deviceRepository = deviceRepository;
            _runtimeDeviceManager = runtimeDeviceManager;
        }

        /// <summary>按主键获取连接（含控制器/协议导航），不存在时返回 null。</summary>
        public async Task<DeviceConnectionDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;

            var dto = MapToDto(entity);
            dto.InUseByDevice = await _deviceRepository.AnyAsync(d => d.ConnectionId == id);
            return dto;
        }

        /// <summary>获取连接列表（含控制器/协议导航）；controllerId 非空时仅返回该控制器下的连接。</summary>
        public async Task<List<DeviceConnectionDto>> GetListAsync(int? controllerId = null)
        {
            var list = await _repository.GetListAsync();
            if (controllerId.HasValue)
            {
                list = list.Where(c => c.ControllerId == controllerId.Value).ToList();
            }

            // 批量取被设备引用的连接 ID，避免逐条 N+1 查询。
            var referenced = new HashSet<int>(
                (await _deviceRepository.GetListAsync(d => d.ConnectionId.HasValue))
                .Select(d => d.ConnectionId!.Value));

            var dtos = new List<DeviceConnectionDto>(list.Count);
            foreach (var entity in list)
            {
                var dto = MapToDto(entity);
                dto.InUseByDevice = referenced.Contains(entity.Id);
                dtos.Add(dto);
            }
            return dtos;
        }

        /// <summary>新增连接：校验控制器存在且启用、协议存在与配置 JSON 合法，按配置原文重算冗余列后写入。</summary>
        public async Task<DeviceConnectionDto> CreateAsync(CreateDeviceConnectionDto dto)
        {
            var name = dto.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                throw new BusinessException("连接名称不能为空");
            }

            // 1. 控制器存在性 + 启用性校验（阶段 2 语义：禁用控制器不可被新连接引用）
            var controller = await _controllerRepository.GetByIdAsync(dto.ControllerId);
            if (controller == null)
            {
                throw new BusinessException($"ID 为 {dto.ControllerId} 的控制器不存在");
            }
            if (!controller.IsEnabled)
            {
                throw new BusinessException($"控制器 '{controller.Name}' 已禁用，不可新建连接引用");
            }

            // 2. 协议存在性校验（FK Restrict 前给出友好提示）
            var protocol = await _protocolRepository.GetByIdAsync(dto.ProtocolId);
            if (protocol == null)
            {
                throw new BusinessException($"所选协议（ID={dto.ProtocolId}）不存在，请重新选择");
            }

            // 3. 配置原文：空值以 "{}" 兜底（与 Device.JsonConfig 空值语义一致），并校验 JSON 可解析
            var configJson = string.IsNullOrWhiteSpace(dto.ConfigJson) ? "{}" : dto.ConfigJson!;
            EnsureConfigJsonParsable(configJson);

            var now = DateTime.UtcNow;
            var summary = DeviceConnectionProfile.ParseConnectionSummary(protocol.Key, configJson);
            var entity = new DeviceConnection
            {
                ControllerId = dto.ControllerId,
                Name = DeviceConnectionProfile.Truncate(name, 100) ?? string.Empty,
                ProtocolId = dto.ProtocolId,
                Host = summary.Host,
                Port = summary.Port,
                ConfigJson = configJson,
                TimeoutMs = summary.TimeoutMs,
                ReconnectIntervalMs = dto.ReconnectIntervalMs,
                IsEnabled = dto.IsEnabled,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _repository.InsertAsync(entity);

            return await GetByIdAsync(entity.Id)
                ?? throw new BusinessException($"创建连接后无法读取 ID 为 {entity.Id} 的连接记录");
        }

        /// <summary>更新连接：被设备引用时拒绝（双写一致由设备接口单点维护）；ConfigJson 留空 = 保留原配置。</summary>
        public async Task<DeviceConnectionDto> UpdateAsync(int id, CreateDeviceConnectionDto dto)
        {
            var entity = await _repository.GetByIdForUpdateAsync(id);
            if (entity == null)
            {
                throw new BusinessException($"ID 为 {id} 的连接不存在");
            }

            // 已被设备引用 → 允许编辑参数字段（Name/IsEnabled/ReconnectIntervalMs/ConfigJson 及其派生的
            // Host/Port/TimeoutMs），但冻结结构性绑定（ControllerId/ProtocolId）：改它们会改变设备协议/控制器
            // 归属，必须走设备页维护。阶段 6 起 Connection.ConfigJson 即连接配置唯一真相源，此处直接改配置安全。
            var inUseByDevice = await _deviceRepository.AnyAsync(d => d.ConnectionId == id);
            if (inUseByDevice
                && (dto.ControllerId != entity.ControllerId || dto.ProtocolId != entity.ProtocolId))
            {
                throw new BusinessException("该连接正被设备使用，所属控制器/协议不可在此变更，请在设备管理页调整设备归属");
            }

            var name = dto.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                throw new BusinessException("连接名称不能为空");
            }

            // 控制器存在性校验；仅当更换控制器时才检查"禁用不可引用"（同控制器下编辑已有连接不算新增引用）。
            var controller = await _controllerRepository.GetByIdAsync(dto.ControllerId);
            if (controller == null)
            {
                throw new BusinessException($"ID 为 {dto.ControllerId} 的控制器不存在");
            }
            if (dto.ControllerId != entity.ControllerId && !controller.IsEnabled)
            {
                throw new BusinessException($"控制器 '{controller.Name}' 已禁用，不可将连接改挂到该控制器");
            }

            var protocol = await _protocolRepository.GetByIdAsync(dto.ProtocolId);
            if (protocol == null)
            {
                throw new BusinessException($"所选协议（ID={dto.ProtocolId}）不存在，请重新选择");
            }

            var now = DateTime.UtcNow;
            // 记录连接配置真相源变更前快照，用于判定是否需要触发会话热更新（仅参数/启停变更才重建，改名不重建）。
            var prevConfigJson = entity.ConfigJson;
            var prevReconnectIntervalMs = entity.ReconnectIntervalMs;
            var prevIsEnabled = entity.IsEnabled;

            entity.ControllerId = dto.ControllerId;
            entity.Name = DeviceConnectionProfile.Truncate(name, 100) ?? string.Empty;
            entity.ProtocolId = dto.ProtocolId;
            entity.ReconnectIntervalMs = dto.ReconnectIntervalMs;
            entity.IsEnabled = dto.IsEnabled;

            // ConfigJson：非空 = 提交新配置原文，重算冗余列；留空 = 保留原配置（与设备 PUT 语义一致）。
            if (!string.IsNullOrWhiteSpace(dto.ConfigJson))
            {
                EnsureConfigJsonParsable(dto.ConfigJson!);
                var summary = DeviceConnectionProfile.ParseConnectionSummary(protocol.Key, dto.ConfigJson!);
                entity.ConfigJson = dto.ConfigJson;
                entity.Host = summary.Host;
                entity.Port = summary.Port;
                entity.TimeoutMs = summary.TimeoutMs;
            }

            entity.UpdatedAt = now;
            await _repository.UpdateAsync(entity);

            // 连接配置热更新接线（P4.2）：配置真相源（ConfigJson）/重连间隔/启用状态任一变化即通知运行时
            // 重建/销毁/创建会话，使变更无需重启服务即时生效。仅改名称不触发。
            if (entity.ConfigJson != prevConfigJson
                || entity.ReconnectIntervalMs != prevReconnectIntervalMs
                || entity.IsEnabled != prevIsEnabled)
            {
                await _runtimeDeviceManager.ReloadConnectionAsync(id);
            }

            return await GetByIdAsync(id)
                ?? throw new BusinessException($"更新连接后无法读取 ID 为 {id} 的连接记录");
        }

        /// <summary>删除连接：被设备引用时拒绝；删除后清理因此产生的无引用独占控制器（与设备删除口径一致）。</summary>
        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdForUpdateAsync(id);
            if (entity == null) return;

            var inUseByDevice = await _deviceRepository.AnyAsync(d => d.ConnectionId == id);
            if (inUseByDevice)
            {
                throw new BusinessException($"无法删除连接 '{entity.Name}'：该连接正被设备使用，请先在设备管理页解除引用");
            }

            // 用 DeleteRangeAsync（跟踪查询）删除，避免 AsNoTracking 图 Remove 引发的重复跟踪冲突。
            await _repository.DeleteRangeAsync(c => c.Id == id);

            // 清理无引用控制器：删除的连接是其控制器下最后一条连接、且无任何设备经 ControllerId 引用时，
            // 控制器不再有存在价值，一并清理（与 DeviceDeletionService.CleanupExclusiveControllerAndConnectionAsync 引用口径一致）。
            var controllerId = entity.ControllerId;
            var deviceRef = await _deviceRepository.AnyAsync(d => d.ControllerId == controllerId);
            var hasOtherConnections = await _repository.AnyAsync(c => c.ControllerId == controllerId);
            if (!deviceRef && !hasOtherConnections)
            {
                await _controllerRepository.DeleteRangeAsync(c => c.Id == controllerId);
            }
        }

        /// <summary>校验配置原文为可解析的 JSON；非法时抛出友好业务异常。</summary>
        private static void EnsureConfigJsonParsable(string configJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(configJson);
            }
            catch (JsonException ex)
            {
                throw new BusinessException($"连接配置 JSON 格式无效: {ex.Message}");
            }
        }

        /// <summary>将连接实体映射为 DTO（控制器/协议导航为空时名称字段留空）。</summary>
        private static DeviceConnectionDto MapToDto(DeviceConnection entity) => new()
        {
            Id = entity.Id,
            ControllerId = entity.ControllerId,
            ControllerCode = entity.Controller?.Code,
            ControllerName = entity.Controller?.Name,
            Name = entity.Name,
            ProtocolId = entity.ProtocolId,
            ProtocolName = entity.Protocol?.Name,
            ConfigJson = entity.ConfigJson,
            Host = entity.Host,
            Port = entity.Port,
            TimeoutMs = entity.TimeoutMs,
            ReconnectIntervalMs = entity.ReconnectIntervalMs,
            IsEnabled = entity.IsEnabled,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
