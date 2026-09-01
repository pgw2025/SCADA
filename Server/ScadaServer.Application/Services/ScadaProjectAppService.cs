using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 组态工程（ScadaProject）应用服务：负责工程 CRUD、工程/页面整树查询，
    /// 以及工程级/页面级导入导出（含绑定设备键的智能重映射）。
    /// 删除工程时级联清理其下所有页面与组件。
    /// </summary>
    public class ScadaProjectAppService : IScadaProjectAppService
    {
        /// <summary>组态工程仓储，提供持久化能力。</summary>
        private readonly IScadaProjectRepository _repository;
        /// <summary>组态页面仓储，用于工程下页面的读写。</summary>
        private readonly IScadaPageRepository _pageRepository;
        /// <summary>组件仓储，用于整树查询及级联清理。</summary>
        private readonly IHmiComponentRepository _componentRepository;
        /// <summary>设备仓储，用于导入导出时业务键与 Id 的映射。</summary>
        private readonly IDeviceRepository _deviceRepository;
        /// <summary>工作单元，用于删除工程及导入的原子操作。</summary>
        private readonly IUnitOfWork _uow;

        /// <summary>构造函数：注入工程、页面、组件、设备仓储及工作单元。</summary>
        public ScadaProjectAppService(
            IScadaProjectRepository repository,
            IScadaPageRepository pageRepository,
            IHmiComponentRepository componentRepository,
            IDeviceRepository deviceRepository,
            IUnitOfWork uow)
        {
            _repository = repository;
            _pageRepository = pageRepository;
            _componentRepository = componentRepository;
            _deviceRepository = deviceRepository;
            _uow = uow;
        }

        /// <summary>按主键获取工程，不存在时返回 null。</summary>
        public async Task<ScadaProjectDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new ScadaProjectDto { Id = entity.Id, Name = entity.Name, Description = entity.Description, CreatedAt = entity.CreatedAt };
        }

        /// <summary>获取全部工程列表。</summary>
        public async Task<List<ScadaProjectDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new ScadaProjectDto { Id = entity.Id, Name = entity.Name, Description = entity.Description, CreatedAt = entity.CreatedAt }).ToList();
        }

        /// <summary>新增工程，返回生成的主键。</summary>
        public async Task<int> CreateAsync(ScadaProjectDto dto)
        {
            var entity = new ScadaProject { Name = dto.Name, Description = dto.Description ?? string.Empty, CreatedAt = DateTime.UtcNow };
            await _repository.InsertAsync(entity);
            return entity.Id;
        }

        /// <summary>更新工程名称与描述，成功返回 true，记录不存在时返回 false。</summary>
        public async Task<bool> UpdateAsync(ScadaProjectDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null) return false;

            entity.Name = dto.Name;
            entity.Description = dto.Description ?? string.Empty;
            await _repository.UpdateAsync(entity);
            return true;
        }

        /// <summary>删除工程：同一事务内级联删除其下所有页面及页面组件后再删除工程。</summary>
        public async Task DeleteAsync(int id)
        {
            await _uow.ExecuteInTransactionAsync(async transaction =>
            {
                // 获取工程下所有页面
                var pages = await _pageRepository.GetListAsync();
                var projectPages = pages.Where(p => p.ProjectId == id).ToList();

                foreach (var page in projectPages)
                {
                    // 删除页面下所有组件
                    await _componentRepository.DeleteRangeAsync(c => c.PageId == page.Id);
                }

                // 删除所有页面
                await _pageRepository.DeleteRangeAsync(p => p.ProjectId == id);

                // 删除工程
                var entity = await _repository.GetByIdAsync(id);
                if (entity != null) await _repository.DeleteAsync(entity);

                return true;
            });
        }

        /// <summary>获取工程整树（工程 + 页面 + 各页面组件），工程不存在时返回 null。</summary>
        public async Task<ScadaProjectFullDto?> GetTreeAsync(int id)
        {
            var project = await _repository.GetByIdAsync(id);
            if (project == null) return null;

            var pages = await _pageRepository.GetListAsync(p => p.ProjectId == id);
            var pageIds = pages.Select(p => p.Id).ToList();

            // 阶段4 整树查询优化：仅拉取本工程页面下的组件（SQL 下推过滤），
            // 替代原先「全表拉取组件 → 内存逐页过滤」的 O(全量组件) 做法。
            var allComponents = pageIds.Count == 0
                ? new List<HmiComponent>()
                : await _componentRepository.GetListAsync(c => pageIds.Contains(c.PageId));

            var result = new ScadaProjectFullDto
            {
                Project = new ScadaProjectDto
                {
                    Id = project.Id,
                    Name = project.Name,
                    Description = project.Description,
                    CreatedAt = project.CreatedAt
                }
            };

            foreach (var page in pages)
            {
                var pageComponents = allComponents
                    .Where(c => c.PageId == page.Id)
                    .Select(c => new HmiComponentDto
                    {
                        Id = c.Id,
                        PageId = c.PageId,
                        Type = c.Type,
                        Name = c.Name,
                        X = c.X,
                        Y = c.Y,
                        Width = c.Width,
                        Height = c.Height,
                        ZIndex = c.ZIndex,
                        BindField = c.BindField,
                        Label = c.Label,
                        BindDeviceId = c.BindDeviceId,
                        BindVariableKey = c.BindVariableKey,
                        LayerId = c.LayerId,
                        PropsJson = c.PropsJson
                    })
                    .ToList();

                result.Pages.Add(new ScadaPageWithComponentsDto
                {
                    Id = page.Id,
                    ProjectId = page.ProjectId,
                    Name = page.Name,
                    IsHome = page.IsHome,
                    Platform = page.Platform,
                    Width = page.Width,
                    Height = page.Height,
                    BackgroundJson = page.BackgroundJson,
                    AdaptMode = page.AdaptMode,
                    LayersJson = page.LayersJson,
                    Components = pageComponents
                });
            }

            return result;
        }

        #region 导入导出

        /// <summary>导出工程为迁移包（含设备绑定键重映射），工程不存在时返回 null。</summary>
        public async Task<ScadaTransferPackageDto?> ExportAsync(int id)
        {
            var tree = await GetTreeAsync(id);
            if (tree == null) return null;

            // bindDeviceId -> 设备业务键：一次批量查询建映射（导出端设备可能已删除，删除则置 null）
            var deviceIds = tree.Pages
                .SelectMany(p => p.Components)
                .Where(c => c.BindDeviceId.HasValue)
                .Select(c => c.BindDeviceId!.Value)
                .Distinct()
                .ToList();
            var deviceKeys = deviceIds.Count == 0
                ? new Dictionary<int, string>()
                : (await _deviceRepository.GetListAsync(d => deviceIds.Contains(d.Id)))
                    .ToDictionary(d => d.Id, d => d.Key);

            return new ScadaTransferPackageDto
            {
                Format = ScadaTransferFormats.Project,
                Version = 1,
                ExportedAt = DateTime.UtcNow,
                Project = new ScadaProjectTransferDto
                {
                    Name = tree.Project.Name,
                    Description = tree.Project.Description ?? string.Empty
                },
                Pages = tree.Pages.Select(p => ToTransferPage(p, deviceKeys)).ToList()
            };
        }

        /// <summary>导入工程迁移包：重名自动加后缀、同端首页去重，在同一事务内写入工程/页面/组件。</summary>
        public async Task<ScadaImportResultDto> ImportAsync(ScadaTransferPackageDto package)
        {
            if (!string.Equals(package.Format, ScadaTransferFormats.Project, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("该文件不是工程导出文件（format 应为 scada-project）");
            if (package.Project == null)
                throw new ArgumentException("文件缺少工程信息（project）");
            if (package.Pages.Count == 0)
                throw new ArgumentException("文件不包含任何画面（pages 为空）");

            return await _uow.ExecuteInTransactionAsync(async _ =>
            {
                // 重名工程自动加后缀：名称(导入) / 名称(导入2) ...
                var existing = (await _repository.GetListAsync())
                    .Select(p => p.Name)
                    .ToHashSet(StringComparer.Ordinal);
                var name = MakeUniqueName(
                    string.IsNullOrWhiteSpace(package.Project.Name) ? "未命名工程" : package.Project.Name.Trim(),
                    n => !existing.Contains(n), "导入");

                var project = new ScadaProject
                {
                    Name = name,
                    Description = package.Project.Description ?? string.Empty,
                    CreatedAt = DateTime.UtcNow
                };
                await _repository.InsertAsync(project);

                var deviceMap = await BuildDeviceKeyMapAsync(package.Pages);
                var result = new ScadaImportResultDto { ProjectId = project.Id, ProjectName = name };

                // 工程内画面重名去重（导出文件自身可能含同名画面）+ 同端首页唯一
                var usedNames = new HashSet<string>(StringComparer.Ordinal);
                var homePlatforms = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in package.Pages)
                {
                    var pageName = MakeUniqueName(
                        string.IsNullOrWhiteSpace(p.Name) ? "未命名画面" : p.Name.Trim(),
                        n => !usedNames.Contains(n), "导入");
                    usedNames.Add(pageName);

                    var platform = NormalizePlatform(p.Platform);
                    var isHome = p.IsHome;
                    if (isHome && !homePlatforms.Add(platform))
                    {
                        isHome = false;
                        result.Warnings.Add($"画面「{p.Name}」与先前导入的同端画面重复标记首页，已降级为普通画面");
                    }

                    await InsertPageAsync(p, project.Id, pageName, isHome, deviceMap, result);
                }
                return result;
            });
        }

        /// <summary>导出单个页面为迁移包，页面不存在时返回 null。</summary>
        public async Task<ScadaTransferPackageDto?> ExportPageAsync(int pageId)
        {
            var page = await _pageRepository.GetByIdAsync(pageId);
            if (page == null) return null;
            var components = await _componentRepository.GetListAsync(c => c.PageId == pageId);

            var deviceIds = components
                .Where(c => c.BindDeviceId.HasValue)
                .Select(c => c.BindDeviceId!.Value)
                .Distinct()
                .ToList();
            var deviceKeys = deviceIds.Count == 0
                ? new Dictionary<int, string>()
                : (await _deviceRepository.GetListAsync(d => deviceIds.Contains(d.Id)))
                    .ToDictionary(d => d.Id, d => d.Key);

            return new ScadaTransferPackageDto
            {
                Format = ScadaTransferFormats.Page,
                Version = 1,
                ExportedAt = DateTime.UtcNow,
                Pages = new List<ScadaPageTransferDto>
                {
                    ToTransferPage(new ScadaPageWithComponentsDto
                    {
                        Name = page.Name,
                        IsHome = page.IsHome,
                        Platform = page.Platform,
                        Width = page.Width,
                        Height = page.Height,
                        BackgroundJson = page.BackgroundJson,
                        AdaptMode = page.AdaptMode,
                        LayersJson = page.LayersJson,
                        Components = components.Select(c => new HmiComponentDto
                        {
                            Type = c.Type, Name = c.Name, X = c.X, Y = c.Y,
                            Width = c.Width, Height = c.Height, ZIndex = c.ZIndex,
                            BindField = c.BindField, Label = c.Label,
                            BindDeviceId = c.BindDeviceId, BindVariableKey = c.BindVariableKey,
                            LayerId = c.LayerId,
                            PropsJson = c.PropsJson
                        }).ToList()
                    }, deviceKeys)
                }
            };
        }

        /// <summary>导入单页迁移包到指定工程：处理首页降级与重名，同一事务内写入页面与组件。</summary>
        public async Task<ScadaImportResultDto> ImportPageAsync(int projectId, ScadaTransferPackageDto package)
        {
            if (!string.Equals(package.Format, ScadaTransferFormats.Page, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("该文件不是画面导出文件（format 应为 scada-page）");
            if (package.Pages.Count == 0)
                throw new ArgumentException("文件不包含画面数据（pages 为空）");

            var project = await _repository.GetByIdAsync(projectId);
            if (project == null)
                throw new ArgumentException($"目标工程不存在（id={projectId}）");

            return await _uow.ExecuteInTransactionAsync(async _ =>
            {
                var resultWarnings = new List<string>();
                var source = package.Pages[0];
                var platform = NormalizePlatform(source.Platform);

                var targetPages = await _pageRepository.GetListAsync(p => p.ProjectId == projectId);

                // 目标工程同端已有首页且导入画面也标记首页 → 降级，不顶掉现有首页
                var isHome = source.IsHome;
                if (isHome && targetPages.Any(p => p.Platform == platform && p.IsHome))
                {
                    isHome = false;
                    resultWarnings.Add($"画面「{source.Name}」导入后已降级为普通画面（目标工程该端已存在首页）");
                }

                // 目标工程内重名自动加后缀
                var existing = targetPages.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
                var pageName = MakeUniqueName(
                    string.IsNullOrWhiteSpace(source.Name) ? "未命名画面" : source.Name.Trim(),
                    n => !existing.Contains(n), "导入");

                var deviceMap = await BuildDeviceKeyMapAsync(package.Pages);
                var result = new ScadaImportResultDto
                {
                    ProjectId = projectId,
                    ProjectName = project.Name,
                    Warnings = resultWarnings
                };

                var pageEntity = new ScadaPage
                {
                    ProjectId = projectId,
                    Name = pageName,
                    IsHome = isHome,
                    Platform = platform,
                    Width = source.Width > 0 ? source.Width : 1100,
                    Height = source.Height > 0 ? source.Height : 700,
                    BackgroundJson = NormalizeBackgroundJson(source.BackgroundJson),
                    AdaptMode = NormalizeAdaptMode(source.AdaptMode),
                    LayersJson = ScadaLayerJson.Normalize(source.LayersJson)
                };
                await _pageRepository.InsertAsync(pageEntity);
                result.ImportedPages = 1;
                result.PageId = pageEntity.Id;
                result.PageName = pageName;

                await InsertComponentsAsync(source.Components, pageEntity.Id, deviceMap, result);

                return result;
            });
        }

        // ===== 导入导出私有辅助 =====

        /// <summary>将页面及其组件转换为传输 DTO，把设备 Id 映射为业务键。</summary>
        private static ScadaPageTransferDto ToTransferPage(ScadaPageWithComponentsDto page, Dictionary<int, string> deviceKeys)
            => new()
            {
                Name = page.Name,
                IsHome = page.IsHome,
                Platform = page.Platform,
                Width = page.Width,
                Height = page.Height,
                BackgroundJson = page.BackgroundJson,
                AdaptMode = page.AdaptMode,
                LayersJson = page.LayersJson,
                Components = page.Components.Select(c => new ScadaComponentTransferDto
                {
                    Type = c.Type,
                    Name = c.Name,
                    X = c.X, Y = c.Y, Width = c.Width, Height = c.Height, ZIndex = c.ZIndex,
                    BindField = c.BindField,
                    Label = c.Label,
                    LayerId = c.LayerId,
                    BindDeviceKey = c.BindDeviceId.HasValue && deviceKeys.TryGetValue(c.BindDeviceId.Value, out var key)
                        ? key : null,
                    BindVariableKey = c.BindVariableKey,
                    PropsJson = string.IsNullOrWhiteSpace(c.PropsJson) ? "{}" : c.PropsJson
                }).ToList()
            };

        /// <summary>
        /// 收集迁移包中全部非空设备业务键，一次查询建 key -> id 映射；
        /// 匹配不到的键由 InsertComponentsAsync 统一记入 warnings。
        /// </summary>
        private async Task<Dictionary<string, int>> BuildDeviceKeyMapAsync(IEnumerable<ScadaPageTransferDto> pages)
        {
            var keys = pages.SelectMany(p => p.Components)
                .Where(c => !string.IsNullOrWhiteSpace(c.BindDeviceKey))
                .Select(c => c.BindDeviceKey!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (keys.Count == 0)
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var devices = await _deviceRepository.GetListAsync(d => keys.Contains(d.Key));
            return devices.ToDictionary(d => d.Key, d => d.Id, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>插入画面 + 其下全部组件（工程导入流程使用）</summary>
        private async Task InsertPageAsync(ScadaPageTransferDto page, int projectId, string name, bool isHome,
            Dictionary<string, int> deviceMap, ScadaImportResultDto result)
        {
            var entity = new ScadaPage
            {
                ProjectId = projectId,
                Name = name,
                IsHome = isHome,
                Platform = NormalizePlatform(page.Platform),
                Width = page.Width > 0 ? page.Width : 1100,
                Height = page.Height > 0 ? page.Height : 700,
                BackgroundJson = NormalizeBackgroundJson(page.BackgroundJson),
                AdaptMode = NormalizeAdaptMode(page.AdaptMode),
                LayersJson = ScadaLayerJson.Normalize(page.LayersJson)
            };
            await _pageRepository.InsertAsync(entity);
            result.ImportedPages++;
            await InsertComponentsAsync(page.Components, entity.Id, deviceMap, result);
        }

        /// <summary>
        /// 批量插入组件并做绑定智能匹配：bindDeviceKey 在本系统存在则映射为 bindDeviceId，
        /// 不存在则保留原值并记 warning（跨系统导入后用户可在编辑器重新绑定）。
        /// </summary>
        private async Task InsertComponentsAsync(List<ScadaComponentTransferDto> components, int pageId,
            Dictionary<string, int> deviceMap, ScadaImportResultDto result)
        {
            if (components.Count == 0) return;

            var missingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entities = new List<HmiComponent>(components.Count);
            foreach (var c in components)
            {
                int? bindDeviceId = null;
                if (!string.IsNullOrWhiteSpace(c.BindDeviceKey))
                {
                    var key = c.BindDeviceKey.Trim();
                    if (deviceMap.TryGetValue(key, out var id)) bindDeviceId = id;
                    else missingKeys.Add(key);
                }
                entities.Add(new HmiComponent
                {
                    PageId = pageId,
                    Type = c.Type,
                    Name = c.Name,
                    X = c.X, Y = c.Y,
                    Width = Math.Max(0, c.Width),
                    Height = Math.Max(0, c.Height),
                    ZIndex = c.ZIndex,
                    BindField = c.BindField ?? string.Empty,
                    Label = c.Label,
                    BindDeviceId = bindDeviceId,
                    BindVariableKey = string.IsNullOrWhiteSpace(c.BindVariableKey) ? null : c.BindVariableKey.Trim(),
                    LayerId = string.IsNullOrWhiteSpace(c.LayerId) ? null : c.LayerId.Trim(),
                    PropsJson = string.IsNullOrWhiteSpace(c.PropsJson) ? "{}" : c.PropsJson
                });
            }
            await _componentRepository.InsertRangeAsync(entities);
            result.ImportedComponents += entities.Count;

            foreach (var key in missingKeys)
                result.Warnings.Add($"组件绑定的设备「{key}」在本系统不存在，相关绑定已失效，请在编辑器中重新绑定");
        }

        /// <summary>
        /// 重名自动加后缀：原名可用直接用；否则 名称(后缀)、名称(后缀2)、名称(后缀3)…
        /// 超过 999 次碰撞时以 GUID 兜底，保证必然返回可用名。
        /// </summary>
        private static string MakeUniqueName(string baseName, Func<string, bool> isFree, string suffix)
        {
            if (isFree(baseName)) return baseName;
            var first = $"{baseName}({suffix})";
            if (isFree(first)) return first;
            for (var i = 2; i < 1000; i++)
            {
                var candidate = $"{baseName}({suffix}{i})";
                if (isFree(candidate)) return candidate;
            }
            return $"{baseName}-{Guid.NewGuid():N}";
        }

        /// <summary>归一化归属端：空/非法值一律回退 Desktop（与 ScadaPageAppService 行为一致）。</summary>
        private static string NormalizePlatform(string? platform)
            => string.Equals(platform, "Mobile", StringComparison.OrdinalIgnoreCase) ? "Mobile" : "Desktop";

        /// <summary>归一化背景 JSON：空白归 NULL；内容结构由前端负责，后端透传。</summary>
        private static string? NormalizeBackgroundJson(string? json)
            => string.IsNullOrWhiteSpace(json) ? null : json.Trim();

        /// <summary>归一化自适应模式：仅 FitScaleUp / Stretch，其余归 NULL。</summary>
        private static string? NormalizeAdaptMode(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return null;
            return mode.Trim() switch
            {
                "FitScaleUp" => "FitScaleUp",
                "Stretch" => "Stretch",
                _ => null
            };
        }

        #endregion
    }
}
