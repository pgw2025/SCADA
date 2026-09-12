using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// SCADA 画面文件夹应用服务：管理画面列表文件夹的分类、移动、删除与排序。
    /// 适用于 Desktop/Mobile/Popup 三端（Popup 为弹窗画面归属端）。
    /// 删除文件夹默认 reparent（内容上提一级），可选 cascade（连同子夹与画面删除）。
    /// </summary>
    public class ScadaPageFolderAppService : IScadaPageFolderAppService
    {
        private const string ReparentMode = "reparent";
        private const string CascadeMode = "cascade";

        private readonly IScadaPageFolderRepository _folderRepository;
        private readonly IScadaPageRepository _pageRepository;
        private readonly IHmiComponentRepository _componentRepository;
        private readonly IUnitOfWork _uow;

        public ScadaPageFolderAppService(
            IScadaPageFolderRepository folderRepository,
            IScadaPageRepository pageRepository,
            IHmiComponentRepository componentRepository,
            IUnitOfWork uow)
        {
            _folderRepository = folderRepository;
            _pageRepository = pageRepository;
            _componentRepository = componentRepository;
            _uow = uow;
        }

        public async Task<List<ScadaPageFolderDto>> GetByProjectAsync(int projectId)
        {
            var list = await _folderRepository.GetListAsync(f => f.ProjectId == projectId);
            return list
                .OrderBy(f => f.ParentFolderId)
                .ThenBy(f => f.SortOrder)
                .ThenBy(f => f.Id)
                .Select(MapToDto).ToList();
        }

        public async Task<int> CreateAsync(ScadaPageFolderDto dto)
        {
            ValidatePlatform(dto.Platform);
            await ValidateParentAsync(dto.ProjectId, dto.Platform, dto.ParentFolderId, excludeFolderId: null);
            await EnsureNameUniqueAsync(dto.ProjectId, dto.Platform, dto.ParentFolderId, dto.Name, excludeFolderId: null);

            var entity = new ScadaPageFolder
            {
                ProjectId = dto.ProjectId,
                ParentFolderId = dto.ParentFolderId,
                Platform = NormalizePlatform(dto.Platform),
                Name = dto.Name.Trim(),
                SortOrder = dto.SortOrder > 0
                    ? dto.SortOrder
                    : await GetNextFolderSortOrderAsync(dto.ProjectId, dto.Platform, dto.ParentFolderId),
                CreatedAt = DateTime.UtcNow
            };
            await _folderRepository.InsertAsync(entity);
            return entity.Id;
        }

        public async Task<bool> UpdateAsync(ScadaPageFolderDto dto)
        {
            var entity = await _folderRepository.GetByIdAsync(dto.Id);
            if (entity == null) return false;

            var platform = NormalizePlatform(dto.Platform);
            ValidatePlatform(platform);

            if (dto.ParentFolderId != entity.ParentFolderId)
            {
                // 移动：不能移入自身或自身后代（循环）
                if (dto.ParentFolderId.HasValue && await IsSelfOrDescendantAsync(dto.ParentFolderId.Value, entity.Id))
                    throw new ArgumentException("不能将文件夹移动到自身或其子文件夹内");

                await ValidateParentAsync(entity.ProjectId, platform, dto.ParentFolderId, entity.Id);
            }

            var name = dto.Name.Trim();
            if (!string.Equals(name, entity.Name, StringComparison.Ordinal))
                await EnsureNameUniqueAsync(entity.ProjectId, platform, dto.ParentFolderId ?? entity.ParentFolderId, name, entity.Id);

            var parentChanged = dto.ParentFolderId != entity.ParentFolderId;
            entity.Platform = platform;
            entity.Name = name;
            if (parentChanged)
            {
                entity.ParentFolderId = dto.ParentFolderId;
                // 落到新父级「文件夹段」末尾
                entity.SortOrder = await GetNextFolderSortOrderAsync(entity.ProjectId, platform, dto.ParentFolderId);
            }

            await _folderRepository.UpdateAsync(entity);
            return true;
        }

        public async Task DeleteAsync(int id, string? mode)
        {
            var normalizedMode = string.Equals(mode, CascadeMode, StringComparison.OrdinalIgnoreCase)
                ? CascadeMode
                : ReparentMode;

            await _uow.ExecuteInTransactionAsync(async _ =>
            {
                var folder = await _folderRepository.GetByIdAsync(id);
                if (folder == null) return true;

                if (normalizedMode == CascadeMode)
                    await DeleteCascadeAsync(folder);
                else
                    await DeleteReparentAsync(folder);

                return true;
            });
        }

        public async Task ReorderAsync(FolderReorderDto dto)
        {
            var platform = NormalizePlatform(dto.Platform);
            ValidatePlatform(platform);

            var folderIds = dto.Folders.Select(f => f.Id).ToList();
            var pageIds = dto.Pages.Select(p => p.Id).ToList();

            var folders = folderIds.Count == 0
                ? new List<ScadaPageFolder>()
                : await _folderRepository.GetListAsync(f => folderIds.Contains(f.Id));
            var pages = pageIds.Count == 0
                ? new List<ScadaPage>()
                : await _pageRepository.GetListAsync(p => pageIds.Contains(p.Id));

            // 校验：所有项都属于该父级且平台一致，避免跨父级/跨端错排
            foreach (var f in folders)
            {
                if (!string.Equals(f.Platform, platform, StringComparison.OrdinalIgnoreCase)
                    || f.ParentFolderId != dto.ParentFolderId)
                    throw new ArgumentException("排序项与目标层级/端不一致（文件夹）");
            }
            foreach (var p in pages)
            {
                if (!string.Equals(p.Platform, platform, StringComparison.OrdinalIgnoreCase)
                    || p.FolderId != dto.ParentFolderId)
                    throw new ArgumentException("排序项与目标层级/端不一致（画面）");
            }

            var folderOrder = new Dictionary<int, int>();
            foreach (var item in dto.Folders) folderOrder[item.Id] = item.SortOrder;
            foreach (var f in folders) if (folderOrder.TryGetValue(f.Id, out var so)) f.SortOrder = so;

            var pageOrder = new Dictionary<int, int>();
            foreach (var item in dto.Pages) pageOrder[item.Id] = item.SortOrder;
            foreach (var p in pages) if (pageOrder.TryGetValue(p.Id, out var so)) p.SortOrder = so;

            await _uow.ExecuteInTransactionAsync(async _ =>
            {
                if (folders.Count > 0) await _folderRepository.UpdateRangeAsync(folders);
                if (pages.Count > 0) await _pageRepository.UpdateRangeAsync(pages);
                return true;
            });
        }

        #region 私有实现

        /// <summary>删除文件夹（reparent）：直接子夹与直接画面上提一级，孙层内部结构不动；随后父级重排。</summary>
        private async Task DeleteReparentAsync(ScadaPageFolder folder)
        {
            var parentId = folder.ParentFolderId;
            var platform = folder.Platform;

            var subFolders = (await _folderRepository.GetListAsync(f => f.ParentFolderId == folder.Id))
                .OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToList();
            foreach (var sub in subFolders)
            {
                sub.ParentFolderId = parentId;
                await _folderRepository.UpdateAsync(sub);
            }

            var pages = (await _pageRepository.GetListAsync(p => p.FolderId == folder.Id))
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToList();
            foreach (var page in pages)
            {
                page.FolderId = parentId;
                await _pageRepository.UpdateAsync(page);
            }

            await _folderRepository.DeleteAsync(folder);
            await RenormalizeOrderAsync(folder.ProjectId, parentId, platform);
        }

        /// <summary>删除文件夹（cascade）：同一事务内先删画面（含组件），再逆深度删子夹，最后删根夹。</summary>
        private async Task DeleteCascadeAsync(ScadaPageFolder folder)
        {
            var subtree = await GetSubtreeAsync(folder.ProjectId, folder.Id); // (Id, Depth)，含根

            var subtreeIds = subtree.Select(s => s.Id).ToHashSet();
            var pages = await _pageRepository.GetListAsync(p => p.FolderId.HasValue && subtreeIds.Contains(p.FolderId.Value));

            // 守卫：删除后工程仍至少保留一个画面（后端兜底，前端已拦截）
            var projectPageTotal = await _pageRepository.CountAsync(p => p.ProjectId == folder.ProjectId);
            if (projectPageTotal - pages.Count < 1)
                throw new ArgumentException("删除该文件夹会导致工程没有任何画面，已取消删除");

            // 先删画面与组件
            foreach (var page in pages)
            {
                await _componentRepository.DeleteRangeAsync(c => c.PageId == page.Id);
                await _pageRepository.DeleteAsync(page);
            }

            // 逆深度删文件夹（先叶子后根，避免带引用删父）
            foreach (var (fid, _) in subtree.OrderByDescending(s => s.Depth))
            {
                await _folderRepository.DeleteAsync(fid);
            }
        }

        /// <summary>一次查询构建该工程文件夹的父子映射，从 rootId 出发 DFS 收集子树（含根），并计算每夹深度。</summary>
        private async Task<List<(int Id, int Depth)>> GetSubtreeAsync(int projectId, int rootId)
        {
            var all = await _folderRepository.GetListAsync(f => f.ProjectId == projectId);
            var byParent = all.Where(f => f.ParentFolderId.HasValue)
                .GroupBy(f => f.ParentFolderId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = new List<(int Id, int Depth)>();
            void Dfs(int fid, int depth)
            {
                result.Add((fid, depth));
                if (byParent.TryGetValue(fid, out var children))
                    foreach (var c in children.OrderBy(c => c.SortOrder).ThenBy(c => c.Id))
                        Dfs(c.Id, depth + 1);
            }
            Dfs(rootId, 0);
            return result;
        }

        /// <summary>同父级逐点重排：文件夹段 + 画面段各自按 (SortOrder, Id) 从 1 连续编号。</summary>
        private async Task RenormalizeOrderAsync(int projectId, int? parentFolderId, string platform)
        {
            var folders = (await _folderRepository.GetListAsync(f => f.ProjectId == projectId
                                                                    && f.Platform == platform
                                                                    && f.ParentFolderId == parentFolderId))
                .OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToList();
            for (int i = 0; i < folders.Count; i++)
                if (folders[i].SortOrder != i + 1) { folders[i].SortOrder = i + 1; await _folderRepository.UpdateAsync(folders[i]); }

            var pages = (await _pageRepository.GetListAsync(p => p.ProjectId == projectId
                                                                 && p.FolderId == parentFolderId
                                                                 && p.Platform == platform))
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToList();
            for (int i = 0; i < pages.Count; i++)
                if (pages[i].SortOrder != i + 1) { pages[i].SortOrder = i + 1; await _pageRepository.UpdateAsync(pages[i]); }
        }

        private async Task<int> GetNextFolderSortOrderAsync(int projectId, string platform, int? parentFolderId)
        {
            var folders = await _folderRepository.GetListAsync(f => f.ProjectId == projectId
                                                                    && f.Platform == platform
                                                                    && f.ParentFolderId == parentFolderId);
            return folders.Count == 0 ? 1 : folders.Max(f => f.SortOrder) + 1;
        }

        /// <summary>校验父文件夹：存在、同工程、同端；且父文件夹非自身后代。</summary>
        private async Task ValidateParentAsync(int projectId, string platform, int? parentFolderId, int? excludeFolderId)
        {
            if (parentFolderId == null) return;
            var parent = await _folderRepository.GetByIdAsync(parentFolderId.Value);
            if (parent == null)
                throw new ArgumentException("父文件夹不存在");
            if (parent.ProjectId != projectId)
                throw new ArgumentException("父文件夹与目标工程不一致");
            if (!string.Equals(parent.Platform, platform, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("父文件夹与目标端不一致，不能跨端移动");
            if (excludeFolderId.HasValue && await IsSelfOrDescendantAsync(parentFolderId.Value, excludeFolderId.Value))
                throw new ArgumentException("不能将文件夹移动到自身或其子文件夹内");
        }

        /// <summary>校验同一 (ProjectId, Platform, ParentFolderId) 下文件夹名唯一。</summary>
        private async Task EnsureNameUniqueAsync(int projectId, string platform, int? parentFolderId, string name, int? excludeFolderId)
        {
            var dup = await _folderRepository.AnyAsync(f =>
                f.ProjectId == projectId && f.Platform == platform && f.ParentFolderId == parentFolderId
                && f.Name == name
                && (excludeFolderId == null || f.Id != excludeFolderId.Value));
            if (dup)
                throw new ArgumentException("同层级下已存在同名文件夹");
        }

        /// <summary>判断 candidateId 是否等于或在 ancestorId 之下（含自身循环检测）。</summary>
        private async Task<bool> IsSelfOrDescendantAsync(int candidateId, int ancestorId)
        {
            if (candidateId == ancestorId) return true;
            var current = candidateId;
            while (true)
            {
                var f = await _folderRepository.GetByIdAsync(current);
                if (f?.ParentFolderId == null) return false;
                current = f.ParentFolderId.Value;
                if (current == ancestorId) return true;
                if (current <= 0) return false; // 环防御：无递增指针则终止
            }
        }

        private static string NormalizePlatform(string? platform)
        {
            var p = platform?.Trim();
            if (string.Equals(p, "Popup", StringComparison.OrdinalIgnoreCase)) return "Popup";
            if (string.Equals(p, "Mobile", StringComparison.OrdinalIgnoreCase)) return "Mobile";
            return "Desktop";
        }

        private static void ValidatePlatform(string? platform)
        {
            var p = platform?.Trim();
            if (!string.Equals(p, "Desktop", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(p, "Mobile", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(p, "Popup", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("未知的归属端，仅 Desktop/Mobile/Popup 可建文件夹");
        }

        private static ScadaPageFolderDto MapToDto(ScadaPageFolder e) => new()
        {
            Id = e.Id,
            ProjectId = e.ProjectId,
            ParentFolderId = e.ParentFolderId,
            Platform = e.Platform,
            Name = e.Name,
            SortOrder = e.SortOrder,
            CreatedAt = e.CreatedAt
        };

        #endregion
    }
}