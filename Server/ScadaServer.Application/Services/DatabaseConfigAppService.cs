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
    /// </summary>
    public class DatabaseConfigAppService : IDatabaseConfigAppService
    {
        /// <summary>敏感字段回显占位符，用于「掩码回显、掩码不改密」。</summary>
        private const string SecretMask = "******";

        /// <summary>数据库配置仓储，提供持久化能力。</summary>
        private readonly IDatabaseConfigRepository _repository;

        /// <summary>构造函数：注入数据库配置仓储。</summary>
        public DatabaseConfigAppService(IDatabaseConfigRepository repository)
        {
            _repository = repository;
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
            FromDto(entity, dto);

            // 由备用切换为生效时，同 Type 其它生效配置降级
            if (entity.IsActive && !wasActive)
            {
                await DeactivateOthersAsync(entity.Type, entity.Id);
            }

            await _repository.UpdateAsync(entity);
        }

        /// <summary>删除数据库配置；记录不存在时静默忽略。</summary>
        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
            }
        }

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
