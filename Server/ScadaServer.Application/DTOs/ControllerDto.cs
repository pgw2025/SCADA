using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 控制器创建/更新请求体（阶段 2，控制器/PLC 资产台账）。
    /// 类型（PLC/OPCUA Server）通过 <see cref="ProtocolId"/> 指定。
    /// </summary>
    public class CreateControllerDto
    {
        /// <summary>控制器编码（业务键，全局唯一）。</summary>
        [Required(ErrorMessage = "控制器编码不能为空")]
        [StringLength(50, ErrorMessage = "控制器编码不能超过50个字符")]
        public string Code { get; set; } = string.Empty;

        /// <summary>控制器名称。</summary>
        [Required(ErrorMessage = "控制器名称不能为空")]
        [StringLength(100, ErrorMessage = "控制器名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>控制器类型/所用协议 ID（FK → Protocols）。</summary>
        public int ProtocolId { get; set; }

        /// <summary>厂商（Siemens/Kepware...）。</summary>
        [StringLength(100, ErrorMessage = "厂商不能超过100个字符")]
        public string? Manufacturer { get; set; }

        /// <summary>型号（S7-1500/KEPServerEX...）。</summary>
        [StringLength(100, ErrorMessage = "型号不能超过100个字符")]
        public string? Model { get; set; }

        /// <summary>描述。</summary>
        [StringLength(500, ErrorMessage = "描述不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>是否启用（禁用后不可被后续连接引用）。</summary>
        public bool IsEnabled { get; set; } = true;
    }

    /// <summary>
    /// 控制器 DTO。对应 <see cref="ScadaServer.Domain.Entities.Controller"/> 实体。
    /// </summary>
    public class ControllerDto : CreateControllerDto
    {
        /// <summary>控制器ID（主键，创建时由服务端生成）。</summary>
        public int Id { get; set; }

        /// <summary>协议名称（派生展示字段，来自 Protocol 导航）。</summary>
        public string ProtocolName { get; set; } = string.Empty;

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>最近更新时间</summary>
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// 控制器分页查询条件。
    /// </summary>
    public class ControllerQueryDto
    {
        /// <summary>按协议过滤。</summary>
        public int? ProtocolId { get; set; }

        /// <summary>关键字（编码/名称/厂商/型号模糊匹配）。</summary>
        public string? Keyword { get; set; }

        /// <summary>页码（从 1 开始）。</summary>
        public int PageIndex { get; set; } = 1;

        /// <summary>每页条数。</summary>
        public int PageSize { get; set; } = 20;
    }

    /// <summary>
    /// 控制器分页结果。
    /// </summary>
    public class ControllerPagedResultDto
    {
        /// <summary>总条数。</summary>
        public int Total { get; set; }

        /// <summary>当前页数据。</summary>
        public List<ControllerDto> Items { get; set; } = new();
    }

    /// <summary>
    /// 控制器下拉选项（/api/controllers/options 数据源：Id+Code+Name+Protocol）。
    /// </summary>
    public class ControllerOptionDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int ProtocolId { get; set; }
        public string ProtocolName { get; set; } = string.Empty;
    }
}
