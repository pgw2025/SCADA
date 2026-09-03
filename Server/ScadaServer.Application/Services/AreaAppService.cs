using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 区域应用服务实现：负责区域（Area）的增删改查与树形组织。
    /// <para>
    /// 树形语义：<see cref="Area.ParentId"/> 为自引用外键，NULL 表示根区域；
    /// 删除前校验该区域下是否仍存在子区域或设备，存在则禁止删除以保护数据完整；
    /// 更新父区域时做防环校验（父不能是自己或其子孙）。
    /// </para>
    /// </summary>
    public class AreaAppService : IAreaAppService
    {
        /// <summary>区域仓储，提供增删改查能力。</summary>
        private readonly IAreaRepository _repository;
        /// <summary>设备仓储，用于删除区域前校验其下是否还有设备、区域树节点设备计数。</summary>
        private readonly IDeviceRepository _deviceRepository;
        /// <summary>工作单元（当前服务未使用，为接口约定预留）。</summary>
        private readonly IUnitOfWork _uow;

        /// <summary>构造函数：注入区域、设备仓储及工作单元。</summary>
        public AreaAppService(IAreaRepository repository, IDeviceRepository deviceRepository, IUnitOfWork uow)
        {
            _repository = repository;
            _deviceRepository = deviceRepository;
            _uow = uow;
        }

        /// <summary>按主键获取区域，不存在时返回 null。</summary>
        public async Task<AreaDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return MapToDto(entity);
        }

        /// <summary>获取全部区域列表（平级，含树形字段）。</summary>
        public async Task<List<AreaDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.OrderBy(a => a.Sort).ThenBy(a => a.Id).Select(MapToDto).ToList();
        }

        /// <summary>获取区域树：根节点列表，含子区域与各节点直接挂载的设备数。</summary>
        public async Task<List<AreaTreeNodeDto>> GetTreeAsync()
        {
            var all = await _repository.GetListAsync();
            var deviceCounts = await _deviceRepository.GetCountByAreaAsync();

            var nodes = all.ToDictionary(
                a => a.Id,
                a => new AreaTreeNodeDto
                {
                    Id = a.Id,
                    ParentId = a.ParentId,
                    Name = a.Name,
                    Code = a.Code,
                    AreaType = (int)a.AreaType,
                    Description = a.Description,
                    Sort = a.Sort,
                    IsEnabled = a.IsEnabled,
                    DeviceCount = deviceCounts.TryGetValue(a.Id, out var c) ? c : 0
                });

            var roots = new List<AreaTreeNodeDto>();
            foreach (var node in nodes.Values.OrderBy(n => n.Sort).ThenBy(n => n.Id))
            {
                if (node.ParentId.HasValue && nodes.TryGetValue(node.ParentId.Value, out var parent))
                {
                    parent.Children.Add(node);
                }
                else
                {
                    roots.Add(node);
                }
            }

            SortTree(roots);
            return roots;
        }

        /// <summary>新增区域：名称需全局唯一，成功后将生成的主键写回 DTO 返回。</summary>
        public async Task<AreaDto> CreateAsync(AreaDto dto)
        {
            // 业务校验：名称不能重复
            var existing = await _repository.GetListAsync(a => a.Name == dto.Name);
            if (existing.Any())
            {
                throw new BusinessException($"区域名称 '{dto.Name}' 已存在");
            }

            // 父区域存在性校验
            if (dto.ParentId.HasValue)
            {
                var parent = await _repository.GetByIdAsync(dto.ParentId.Value);
                if (parent == null)
                {
                    throw new BusinessException($"父区域 ID {dto.ParentId} 不存在");
                }
            }

            var entity = new Area
            {
                ParentId = dto.ParentId,
                Name = dto.Name,
                Code = NormalizeCode(dto.Code),
                AreaType = (AreaTypeEnum)(dto.AreaType is >= 1 and <= 5 ? dto.AreaType : (int)AreaTypeEnum.Area),
                Description = dto.Description ?? string.Empty,
                Sort = dto.Sort,
                IsEnabled = dto.IsEnabled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _repository.InsertAsync(entity);

            // 返回包含生成 ID 的 DTO
            dto.Id = entity.Id;
            dto.CreatedAt = entity.CreatedAt;
            dto.UpdatedAt = entity.UpdatedAt;
            return dto;
        }

        /// <summary>更新区域信息：校验存在性、名称唯一（排除自身）、父区域防环，最后回读最新数据返回。</summary>
        public async Task<AreaDto> UpdateAsync(AreaDto dto)
        {
            // 1. 检查记录是否存在
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null)
            {
                throw new BusinessException($"ID 为 {dto.Id} 的区域不存在");
            }

            // 2. 业务校验：名称不能与其他区域重复
            var existing = await _repository.GetListAsync(a => a.Name == dto.Name && a.Id != dto.Id);
            if (existing.Any())
            {
                throw new BusinessException($"区域名称 '{dto.Name}' 已存在");
            }

            // 3. 父区域校验：存在性 + 防环（父不能是自己或其子孙）
            if (dto.ParentId.HasValue)
            {
                if (dto.ParentId.Value == dto.Id)
                {
                    throw new BusinessException("父区域不能是区域自身");
                }
                var parent = await _repository.GetByIdAsync(dto.ParentId.Value);
                if (parent == null)
                {
                    throw new BusinessException($"父区域 ID {dto.ParentId} 不存在");
                }
                if (await IsDescendantAsync(dto.ParentId.Value, dto.Id))
                {
                    throw new BusinessException("父区域不能是当前区域的子孙区域（会造成循环层级）");
                }
            }

            // 4. 更新字段
            entity.ParentId = dto.ParentId;
            entity.Name = dto.Name;
            entity.Code = NormalizeCode(dto.Code);
            entity.AreaType = (AreaTypeEnum)(dto.AreaType is >= 1 and <= 5 ? dto.AreaType : (int)entity.AreaType);
            entity.Description = dto.Description ?? string.Empty;
            entity.Sort = dto.Sort;
            entity.IsEnabled = dto.IsEnabled;
            entity.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(entity);

            // 5. 返回最新的 DTO
            return MapToDto(entity);
        }

        /// <summary>删除区域：校验存在性及其下子区域/设备数量，存在时抛异常禁止删除。</summary>
        public async Task DeleteAsync(int id)
        {
            // 1. 检查区域是否存在
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return;

            // 2. 安全检查：存在子区域禁止删除
            if (await _repository.AnyAsync(a => a.ParentId == id))
            {
                throw new BusinessException($"无法删除区域 '{entity.Name}'，因为该区域下仍有子区域。请先移除或删除相关子区域。");
            }

            // 3. 安全检查：该区域下还有设备，禁止删除
            var deviceCount = await _deviceRepository.CountAsync(d => d.AreaId == id);
            if (deviceCount > 0)
            {
                throw new BusinessException($"无法删除区域 '{entity.Name}'，因为该区域下尚有 {deviceCount} 台设备。请先移除或删除相关设备。");
            }

            // 4. 执行删除
            await _repository.DeleteAsync(entity);
        }

        /// <summary>获取指定区域（含子孙区域）下直接挂载的设备 ID 列表。</summary>
        public async Task<List<int>> GetDeviceIdsInSubtreeAsync(int areaId)
        {
            var all = await _repository.GetListAsync();
            var subtreeIds = CollectSubtreeIds(all, areaId);
            if (subtreeIds.Count == 0)
            {
                return new List<int>();
            }

            var devices = await _deviceRepository.GetListAsync(d => subtreeIds.Contains(d.AreaId));
            return devices.Select(d => d.Id).Distinct().ToList();
        }

        /// <summary>判断 candidateId 是否为 rootId 的子孙（用于防环校验）。</summary>
        private async Task<bool> IsDescendantAsync(int candidateId, int rootId)
        {
            var all = await _repository.GetListAsync();
            var byParent = all.ToLookup(a => a.ParentId);
            var stack = new Stack<int>();
            stack.Push(rootId);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var child in byParent[current])
                {
                    if (child.Id == candidateId) return true;
                    stack.Push(child.Id);
                }
            }
            return false;
        }

        /// <summary>收集指定区域及其所有子孙区域的 ID。</summary>
        private static List<int> CollectSubtreeIds(List<Area> all, int rootId)
        {
            var byParent = all.ToLookup(a => a.ParentId);
            var result = new List<int>();
            var stack = new Stack<int>();
            stack.Push(rootId);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                result.Add(current);
                foreach (var child in byParent[current])
                {
                    stack.Push(child.Id);
                }
            }
            return result;
        }

        /// <summary>递归排序树节点（按 Sort 升序、Id 升序）。</summary>
        private static void SortTree(List<AreaTreeNodeDto> nodes)
        {
            nodes.Sort((x, y) =>
            {
                var c = x.Sort.CompareTo(y.Sort);
                return c != 0 ? c : x.Id.CompareTo(y.Id);
            });
            foreach (var node in nodes)
            {
                SortTree(node.Children);
            }
        }

        /// <summary>空字符串编码归一化为 NULL（与库唯一索引语义一致）。</summary>
        private static string? NormalizeCode(string? code)
            => string.IsNullOrWhiteSpace(code) ? null : code.Trim();

        /// <summary>实体 → 平级 DTO 映射。</summary>
        private static AreaDto MapToDto(Area entity) => new()
        {
            Id = entity.Id,
            ParentId = entity.ParentId,
            Name = entity.Name,
            Code = entity.Code,
            AreaType = (int)entity.AreaType,
            Description = entity.Description,
            Sort = entity.Sort,
            IsEnabled = entity.IsEnabled,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
