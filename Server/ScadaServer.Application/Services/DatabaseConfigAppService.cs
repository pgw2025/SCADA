using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 数据库配置应用服务。
    /// <para>
    /// 统一以 <c>DatabaseConfigs</c> 表为事实源（替代原 databases.json 双轨）。
    /// 处理：字段映射、密码/令牌掩码回显与“掩码不改密”、同 Type 生效唯一性。
    /// </para>
    /// <para>
    /// 生效历史库配置的写路径（新增/更新/删除）即时联动 <see cref="IInfluxStore"/>，
    /// 使配置变更无需重启/手动迁移即可生效或停用（阶段1 P1-1）。
    /// </para>
    /// </summary>
    public class DatabaseConfigAppService : IDatabaseConfigAppService
    {
        /// <summary>敏感字段回显占位符，用于「掩码回显、掩码不改密」。</summary>
        private const string SecretMask = "******";

        /// <summary>历史库类型标识。</summary>
        private const string HistoricalType = "Historical";

        /// <summary>InfluxDB 后端类型标识。</summary>
        private const string InfluxBackendType = "InfluxDB";

        /// <summary>数据库配置仓储，提供持久化能力。</summary>
        private readonly IDatabaseConfigRepository _repository;

        /// <summary>InfluxDB 时序库客户端（生效历史库配置联动用）。</summary>
        private readonly IInfluxStore _influxStore;

        /// <summary>构造函数：注入数据库配置仓储与时序库客户端。</summary>
        public DatabaseConfigAppService(
            IDatabaseConfigRepository repository,
            IInfluxStore influxStore)
        {
            _repository = repository;
            _influxStore = influxStore;
        }

        /// <summary>按主键获取数据库配置，不存在时返回 null。</summary>
        public async Task<DatabaseConfigDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            return entity == null ? null : ToDto(entity);
        }

        /// <summary>获取全部数据库配置列表。</summary>
        public async Task<List<DatabaseConfigDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(ToDto).ToList();
        }

        /// <summary>新增数据库配置：校验后写入，创建即生效时同 Type 其它生效配置降级为备用。</summary>
        public async Task CreateAsync(DatabaseConfigDto dto)
        {
            Validate(dto);

            var entity = FromDto(new DatabaseConfig(), dto);

            // 创建即启用时，同 Type 其它生效配置降级为备用
            if (entity.IsActive)
            {
                await DeactivateOthersAsync(entity.Type, excludeId: null);
            }

            await _repository.InsertAsync(entity);

            // 创建即生效历史库配置：联动 InfluxStore 即时生效（阶段1 P1-1）
            if (IsActiveHistoricalInflux(entity))
            {
                _influxStore.Rebuild(entity);
            }
        }

        /// <summary>更新数据库配置：校验后应用修改；密码/令牌传掩码或空则保留原值；由备用切换生效时降级同 Type 其它配置。</summary>
        public async Task UpdateAsync(DatabaseConfigDto dto)
        {
            Validate(dto);

            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null)
            {
                return;
            }

            // 密码/令牌传掩码或空 => 视为“保持原值不修改”
            if (string.IsNullOrEmpty(dto.Password) || dto.Password == SecretMask)
            {
                dto.Password = entity.Password;
            }
            if (string.IsNullOrEmpty(dto.Token) || dto.Token == SecretMask)
            {
                dto.Token = entity.Token;
            }

            var wasActive = entity.IsActive;
            var wasHistoricalInflux = IsHistoricalInflux(entity);
            FromDto(entity, dto);

            // 由备用切换为生效时，同 Type 其它生效配置降级
            if (entity.IsActive && !wasActive)
            {
                await DeactivateOthersAsync(entity.Type, entity.Id);
            }

            await _repository.UpdateAsync(entity);

            // 生效历史库配置变更联动（阶段1 P1-1）
            if (IsActiveHistoricalInflux(entity))
            {
                // 更新后仍为生效历史库：按新配置重建客户端（热切换）。
                _influxStore.Rebuild(entity);
            }
            else if (wasActive && wasHistoricalInflux)
            {
                // 原为生效历史库，更新后不再是生效状态（停用/改类型/改后端）：停用客户端回退 MySQL。
                _influxStore.Reset();
            }
        }

        /// <summary>删除数据库配置；记录不存在时静默忽略。</summary>
        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                var wasActiveHistoricalInflux = IsActiveHistoricalInflux(entity);
                await _repository.DeleteAsync(entity);

                // 删除的是当前生效历史库：停用客户端回退 MySQL（阶段1 P1-1）
                if (wasActiveHistoricalInflux)
                {
                    _influxStore.Reset();
                }
            }
        }

        /// <summary>判定实体是否为「生效的 InfluxDB 历史库配置」（Type=Historical 且 BackendType=InfluxDB 且 IsActive）。</summary>
        private static bool IsActiveHistoricalInflux(DatabaseConfig e) =>
            e.IsActive && IsHistoricalInflux(e);

        /// <summary>判定实体是否为「InfluxDB 历史库配置」（忽略 IsActive）。</summary>
        private static bool IsHistoricalInflux(DatabaseConfig e) =>
            string.Equals(e.Type, HistoricalType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.BackendType, InfluxBackendType, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 将同一 Type 下其它生效配置置为非生效，保证同 Type 仅一条 <see cref="DatabaseConfig.IsActive"/>。
        /// </summary>
        private async Task DeactivateOthersAsync(string type, int? excludeId)
        {
            var actives = await _repository.GetListAsync(c => c.Type == type && c.IsActive);
            foreach (var item in actives)
            {
                if (excludeId.HasValue && item.Id == excludeId.Value)
                {
                    continue;
                }
                item.IsActive = false;
                await _repository.UpdateAsync(item);
            }
        }

        /// <summary>
        /// 基础校验：名称/后端类型必填，端口合法；InfluxDB 场景要求 Bucket（或 DatabaseName 兜底）。
        /// </summary>
        private static void Validate(DatabaseConfigDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
            {
                throw new Domain.Exceptions.BusinessException("配置名称不能为空。");
            }
            if (string.IsNullOrWhiteSpace(dto.BackendType))
            {
                throw new Domain.Exceptions.BusinessException("后端类型不能为空。");
            }
            if (dto.Port <= 0)
            {
                throw new Domain.Exceptions.BusinessException("端口号必须为正整数。");
            }
        }

        /// <summary>
        /// 实体 → DTO（敏感字段回显为掩码）。
        /// </summary>
        private static DatabaseConfigDto ToDto(DatabaseConfig e) => new()
        {
            Id = e.Id,
            Name = e.Name,
            Type = e.Type,
            BackendType = e.BackendType,
            Host = e.Host,
            Port = e.Port,
            Username = e.Username,
            Password = string.IsNullOrEmpty(e.Password) ? null : SecretMask,
            HasPassword = !string.IsNullOrEmpty(e.Password),
            DatabaseName = e.DatabaseName,
            Token = string.IsNullOrEmpty(e.Token) ? null : SecretMask,
            HasToken = !string.IsNullOrEmpty(e.Token),
            Org = e.Org,
            Bucket = e.Bucket,
            IsActive = e.IsActive,
            LastStatus = e.LastStatus,
            LastCheckedAt = e.LastCheckedAt
        };

        /// <summary>
        /// DTO → 实体（仅覆盖可写字段；敏感字段由调用方预处理“掩码不改密”）。
        /// </summary>
        private static DatabaseConfig FromDto(DatabaseConfig e, DatabaseConfigDto dto)
        {
            e.Name = dto.Name;
            e.Type = dto.Type;
            e.BackendType = dto.BackendType;
            e.Host = dto.Host;
            e.Port = dto.Port;
            e.Username = dto.Username;
            e.Password = dto.Password ?? string.Empty;
            e.DatabaseName = dto.DatabaseName;
            e.Token = dto.Token;
            e.Org = dto.Org;
            e.Bucket = dto.Bucket;
            e.IsActive = dto.IsActive;
            return e;
        }
    }
}
