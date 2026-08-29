using System.Text.RegularExpressions;
using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    public class ExposedInterfaceAppService : IExposedInterfaceAppService
    {
        private const string OpenPrefix = "/open/";

        private static readonly string[] SupportedMethods = new[] { "GET", "POST" };
        private static readonly Regex RoutePattern = new(
            @"^/open/[a-zA-Z0-9\-_/.]+$",
            RegexOptions.Compiled);

        private readonly IExposedInterfaceRepository _repository;
        private readonly IDeviceRepository _deviceRepository;
        private readonly IModelVariableRepository _modelVariableRepository;
        private readonly IExposedApiRegistry _registry;

        public ExposedInterfaceAppService(
            IExposedInterfaceRepository repository,
            IDeviceRepository deviceRepository,
            IModelVariableRepository modelVariableRepository,
            IExposedApiRegistry registry)
        {
            _repository = repository;
            _deviceRepository = deviceRepository;
            _modelVariableRepository = modelVariableRepository;
            _registry = registry;
        }

        public async Task<ExposedInterfaceDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return ToDto(entity);
        }

        public async Task<List<ExposedInterfaceDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(ToDto).ToList();
        }

        public async Task CreateAsync(ExposedInterfaceDto dto)
        {
            await ValidateAsync(dto, excludeId: 0);
            var entity = new ExposedInterface
            {
                Name = dto.Name,
                RouteUrl = dto.RouteUrl,
                RequestMethod = dto.RequestMethod,
                DeviceId = dto.DeviceId,
                ExposedKey = dto.ExposedKey,
                Active = dto.Active
            };
            await _repository.InsertAsync(entity);
            await _registry.ReloadAsync();
        }

        public async Task UpdateAsync(ExposedInterfaceDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity != null)
            {
                await ValidateAsync(dto, excludeId: dto.Id);
                entity.Name = dto.Name;
                entity.RouteUrl = dto.RouteUrl;
                entity.RequestMethod = dto.RequestMethod;
                entity.DeviceId = dto.DeviceId;
                entity.ExposedKey = dto.ExposedKey;
                entity.Active = dto.Active;
                await _repository.UpdateAsync(entity);
                await _registry.ReloadAsync();
            }
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
                await _registry.ReloadAsync();
            }
        }

        public async Task SetActiveAsync(int id, bool active)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null && entity.Active != active)
            {
                entity.Active = active;
                await _repository.UpdateAsync(entity);
                await _registry.ReloadAsync();
            }
        }

        private static ExposedInterfaceDto ToDto(ExposedInterface e) => new()
        {
            Id = e.Id,
            Name = e.Name,
            RouteUrl = e.RouteUrl,
            RequestMethod = e.RequestMethod,
            DeviceId = e.DeviceId,
            ExposedKey = e.ExposedKey,
            Active = e.Active
        };

        /// <summary>
        /// 业务校验：名称/路由/方法/设备/变量归属/唯一性。任一违规抛出 <see cref="BusinessException"/>。
        /// </summary>
        private async Task ValidateAsync(ExposedInterfaceDto dto, int excludeId)
        {
            if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 100)
                throw new BusinessException("接口名称不能为空且长度不能超过 100 个字符。");

            var route = dto.RouteUrl?.Trim();
            if (string.IsNullOrWhiteSpace(route))
                throw new BusinessException("路由路径不能为空。");
            if (!route.StartsWith(OpenPrefix, StringComparison.OrdinalIgnoreCase))
                throw new BusinessException("路由路径必须以 /open/ 开头（开放接口统一挂在 /open 前缀下）。");
            if (route.Length <= OpenPrefix.Length)
                throw new BusinessException("路由路径不能是裸的 /open/，请提供具体的子路径。");
            if (!RoutePattern.IsMatch(route))
                throw new BusinessException("路由路径格式非法：仅允许字母、数字、中划线、下划线、点与斜杠。");
            if (route.Contains("//"))
                throw new BusinessException("路由路径不能包含连续双斜杠。");

            var method = dto.RequestMethod?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(method) || !SupportedMethods.Contains(method))
                throw new BusinessException("请求方法仅支持 GET、POST。");

            if (string.IsNullOrWhiteSpace(dto.ExposedKey))
                throw new BusinessException("映射变量 Key 不能为空。");

            var device = await _deviceRepository.GetByIdAsync(dto.DeviceId);
            if (device == null)
                throw new BusinessException($"指定的设备（Id={dto.DeviceId}）不存在。");

            var variableExists = await _modelVariableRepository.AnyAsync(
                mv => mv.ModelId == device.ModelId && mv.Key == dto.ExposedKey);
            if (!variableExists)
                throw new BusinessException($"设备 '{device.Name}' 所属的数据模型中不存在映射变量 '{dto.ExposedKey}'。");

            var duplicate = await _repository.AnyAsync(i =>
                i.RequestMethod.ToUpper() == method
                && i.RouteUrl.ToLower() == route.ToLower()
                && i.Id != excludeId);
            if (duplicate)
                throw new BusinessException($"已存在相同的接口路由 [{method} {route}]，请更换路由路径或请求方法。");
        }
    }
}