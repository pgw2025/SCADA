using ScadaServer.Application.Common;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// HMI 组件模板应用服务：组件库模板元数据的增删改查与导入导出。
    /// 统一校验分类 / 图标形态 / 渲染轨白名单、JSON 合法性、模板键唯一；
    /// SVG 轨入库前经 <see cref="SvgSanitizer"/> 清洗（后端第一道防线）。
    /// </summary>
    public class HmiWidgetTemplateAppService : IHmiWidgetTemplateAppService
    {
        /// <summary>分类白名单（与前端组件库分类一致）</summary>
        private static readonly HashSet<string> Categories = new() { "equipment", "sensors", "structures", "headers" };

        /// <summary>图标形态白名单</summary>
        private static readonly HashSet<string> IconKinds = new() { "lucide", "div", "svg", "emoji" };

        /// <summary>渲染轨白名单</summary>
        private static readonly HashSet<string> RenderKinds = new() { "builtin", "svg" };

        /// <summary>模板仓储，提供持久化能力。</summary>
        private readonly IHmiWidgetTemplateRepository _repository;

        /// <summary>构造函数：注入模板仓储。</summary>
        public HmiWidgetTemplateAppService(IHmiWidgetTemplateRepository repository)
            => _repository = repository;

        /// <summary>查询全部模板，按 SortOrder 升序、同序按 Id。</summary>
        public async Task<List<HmiWidgetTemplateDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
                .Select(MapToDto).ToList();
        }

        /// <summary>按主键查询模板；不存在返回 null。</summary>
        public async Task<HmiWidgetTemplateDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            return entity == null ? null : MapToDto(entity);
        }

        /// <summary>创建模板：校验后写入，返回生成的主键。</summary>
        public async Task<int> CreateAsync(HmiWidgetTemplateDto dto)
        {
            await ValidateAsync(dto);
            var entity = MapToEntity(dto);
            entity.CreatedAtUtc = entity.UpdatedAtUtc = DateTime.UtcNow;
            await _repository.InsertAsync(entity);
            return entity.Id;
        }

        /// <summary>更新模板：校验后全量覆盖字段，成功返回 true，记录不存在时返回 false。</summary>
        public async Task<bool> UpdateAsync(HmiWidgetTemplateDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null) return false;

            await ValidateAsync(dto);

            entity.TemplateKey = dto.TemplateKey;
            entity.RenderType = dto.RenderType;
            entity.Name = dto.Name;
            entity.Category = dto.Category;
            entity.Description = dto.Description;
            entity.DefaultWidth = dto.DefaultWidth;
            entity.DefaultHeight = dto.DefaultHeight;
            entity.IconKind = dto.IconKind;
            entity.IconKey = dto.IconKey;
            entity.IconColor = dto.IconColor;
            entity.RenderKind = dto.RenderKind;
            entity.SvgTemplate = dto.RenderKind == "svg" ? SvgSanitizer.Sanitize(dto.SvgTemplate ?? "") : null;
            entity.DefaultPropsJson = dto.DefaultPropsJson;
            entity.PropSchemaJson = dto.PropSchemaJson;
            entity.SortOrder = dto.SortOrder;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            await _repository.UpdateAsync(entity);
            return true;
        }

        /// <summary>删除模板；系统内置模板（IsSystem）拒绝删除（审查 A9：显式报错而非静默）。</summary>
        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id)
                ?? throw new BusinessException("模板不存在");
            if (entity.IsSystem)
                throw new BusinessException("系统内置模板不可删除");
            await _repository.DeleteAsync(entity);
        }

        /// <summary>导入模板（单条）：格式校验后按键冲突策略覆盖或改名另存。</summary>
        public async Task<ImportResult> ImportAsync(WidgetTemplateImportDto import)
        {
            if (!string.Equals(import.Format, "scada-widget-template", StringComparison.OrdinalIgnoreCase))
                throw new BusinessException("不支持的模板文件格式");

            var dto = import.Template;
            var existing = await _repository.GetByKeyAsync(dto.TemplateKey);

            if (existing != null && import.ConflictMode == "overwrite")
            {
                dto.Id = existing.Id;
                dto.IsSystem = existing.IsSystem;      // 覆盖不改变系统标记
                await UpdateAsync(dto);
                return new ImportResult { Ok = true, Id = existing.Id, Mode = "overwrite" };
            }

            if (existing != null)
            {
                // 审查 A10：8 位短随机后缀，先截 key 保证后缀完整（TemplateKey 列宽 64）
                var suffix = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                    .Replace("+", "").Replace("/", "")[..8].ToLowerInvariant();
                var baseKey = dto.TemplateKey.Length > 55 ? dto.TemplateKey[..55] : dto.TemplateKey;
                dto.TemplateKey = $"{baseKey}-{suffix}";
                var created = await CreateAsync(dto);
                return new ImportResult { Ok = true, Id = created, Mode = "renamed", NewKey = dto.TemplateKey };
            }

            var id = await CreateAsync(dto);
            return new ImportResult { Ok = true, Id = id, Mode = "create" };
        }

        /// <summary>导出模板（单条）；不存在抛业务异常。</summary>
        public async Task<WidgetTemplateExportDto> ExportAsync(int id)
        {
            var dto = await GetByIdAsync(id) ?? throw new BusinessException("模板不存在");
            return new WidgetTemplateExportDto { Template = dto };
        }

        /// <summary>批量导出模板（多模板打一个文件）；跳过不存在的 Id。</summary>
        public async Task<WidgetTemplateBundleDto> ExportBundleAsync(IEnumerable<int> ids)
        {
            var templates = new List<HmiWidgetTemplateDto>();
            foreach (var id in ids.Distinct())
            {
                var dto = await GetByIdAsync(id);
                if (dto != null) templates.Add(dto);
            }
            return new WidgetTemplateBundleDto { Templates = templates };
        }

        #region 私有方法

        /// <summary>
        /// 统一校验（审查 A13）：分类 / 图标形态 / 渲染轨白名单、SVG 轨约束、
        /// JSON 合法性、模板键唯一（排除自身）。
        /// </summary>
        private async Task ValidateAsync(HmiWidgetTemplateDto dto)
        {
            if (!Categories.Contains(dto.Category))
                throw new BusinessException($"非法分类：{dto.Category}");
            if (!IconKinds.Contains(dto.IconKind))
                throw new BusinessException($"非法图标形态：{dto.IconKind}");
            if (!RenderKinds.Contains(dto.RenderKind))
                throw new BusinessException($"非法渲染轨：{dto.RenderKind}");
            if (dto.RenderKind == "svg" && string.IsNullOrWhiteSpace(dto.SvgTemplate))
                throw new BusinessException("SVG 渲染轨必须提供 SVG 模板源码");
            if (dto.RenderKind == "svg" && !dto.RenderType.Equals(dto.TemplateKey, StringComparison.Ordinal))
                throw new BusinessException("SVG 模板 RenderType 必须与 TemplateKey 一致（D10）");

            ValidateJson(dto.DefaultPropsJson, "DefaultPropsJson");
            ValidateJson(dto.PropSchemaJson, "PropSchemaJson");

            var dup = await _repository.GetByKeyAsync(dto.TemplateKey);
            if (dup != null && dup.Id != dto.Id)
                throw new BusinessException($"模板键已存在：{dto.TemplateKey}");
        }

        /// <summary>校验字段为合法 JSON（空串按 {} / [] 的约定场景容错）。</summary>
        private static void ValidateJson(string json, string field)
        {
            try
            {
                using var _ = System.Text.Json.JsonDocument.Parse(
                    string.IsNullOrWhiteSpace(json) ? "{}" : json);
            }
            catch (System.Text.Json.JsonException)
            {
                throw new BusinessException($"{field} 不是合法 JSON");
            }
        }

        /// <summary>将模板实体映射为 DTO。</summary>
        private static HmiWidgetTemplateDto MapToDto(HmiWidgetTemplate entity) => new()
        {
            Id = entity.Id,
            TemplateKey = entity.TemplateKey,
            RenderType = entity.RenderType,
            Name = entity.Name,
            Category = entity.Category,
            Description = entity.Description,
            DefaultWidth = entity.DefaultWidth,
            DefaultHeight = entity.DefaultHeight,
            IconKind = entity.IconKind,
            IconKey = entity.IconKey,
            IconColor = entity.IconColor,
            RenderKind = entity.RenderKind,
            SvgTemplate = entity.SvgTemplate,
            DefaultPropsJson = entity.DefaultPropsJson,
            PropSchemaJson = entity.PropSchemaJson,
            IsSystem = entity.IsSystem,
            SortOrder = entity.SortOrder
        };

        /// <summary>将模板 DTO 映射为实体（SVG 轨经清洗后入库）。</summary>
        private static HmiWidgetTemplate MapToEntity(HmiWidgetTemplateDto dto) => new()
        {
            TemplateKey = dto.TemplateKey,
            RenderType = dto.RenderType,
            Name = dto.Name,
            Category = dto.Category,
            Description = dto.Description,
            DefaultWidth = dto.DefaultWidth,
            DefaultHeight = dto.DefaultHeight,
            IconKind = dto.IconKind,
            IconKey = dto.IconKey,
            IconColor = dto.IconColor,
            RenderKind = dto.RenderKind,
            SvgTemplate = dto.RenderKind == "svg" ? SvgSanitizer.Sanitize(dto.SvgTemplate ?? "") : null,
            DefaultPropsJson = dto.DefaultPropsJson,
            PropSchemaJson = dto.PropSchemaJson,
            IsSystem = dto.IsSystem,
            SortOrder = dto.SortOrder
        };

        #endregion
    }
}
