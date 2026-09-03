using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 区域 DTO（设备分区的名称与描述，含树形结构字段）。
    /// </summary>
    public class AreaDto
    {
        /// <summary>区域ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>父区域ID（NULL 表示根区域）</summary>
        public int? ParentId { get; set; }

        /// <summary>区域名称；必填，最长 50 字符（校验特性）</summary>
        [Required(ErrorMessage = "区域名称不能为空")]
        [StringLength(50, ErrorMessage = "区域名称不能超过50个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>区域编码（稳定短码，用于设备编号自动生成前缀）；库级唯一，NULL 可多条共存</summary>
        [StringLength(50, ErrorMessage = "区域编码不能超过50个字符")]
        public string? Code { get; set; }

        /// <summary>区域类型（AreaTypeEnum：Factory=1/Workshop=2/ProductionLine=3/Area=4/Warehouse=5），默认 Area</summary>
        public int AreaType { get; set; } = 4;

        /// <summary>区域描述；可空，最长 200 字符（校验特性）</summary>
        [StringLength(200, ErrorMessage = "描述不能超过200个字符")]
        public string Description { get; set; } = string.Empty;

        /// <summary>排序（同级内展示顺序）</summary>
        public int Sort { get; set; }

        /// <summary>是否启用</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>创建时间（UTC）</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>更新时间（UTC）</summary>
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// 区域树节点 DTO（树形接口返回；含子节点与直接挂载设备数）。
    /// </summary>
    public class AreaTreeNodeDto
    {
        /// <summary>区域ID</summary>
        public int Id { get; set; }

        /// <summary>父区域ID（NULL 表示根区域）</summary>
        public int? ParentId { get; set; }

        /// <summary>区域名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>区域编码</summary>
        public string? Code { get; set; }

        /// <summary>区域类型（AreaTypeEnum）</summary>
        public int AreaType { get; set; }

        /// <summary>区域描述</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>排序</summary>
        public int Sort { get; set; }

        /// <summary>是否启用</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>该区域下直接挂载的设备数量</summary>
        public int DeviceCount { get; set; }

        /// <summary>子区域节点</summary>
        public List<AreaTreeNodeDto> Children { get; set; } = new();
    }
}
