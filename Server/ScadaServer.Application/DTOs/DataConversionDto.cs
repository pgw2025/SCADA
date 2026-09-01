namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 数据转换（值映射）规则 DTO：将源设备变量的值转换后写入目标设备变量。
    /// </summary>
    public class DataConversionDto
    {
        /// <summary>转换规则ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>规则名称；必填，最长 100 字符（校验特性）</summary>
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>源设备ID（被读取的变量所在设备）</summary>
        public int SourceDeviceId { get; set; }

        /// <summary>源设备变量业务键；必填（校验特性）</summary>
        [System.ComponentModel.DataAnnotations.Required]
        public string SourceVariableKey { get; set; } = string.Empty;

        /// <summary>目标设备ID（被写入的变量所在设备）</summary>
        public int TargetDeviceId { get; set; }

        /// <summary>目标设备变量业务键；必填（校验特性）</summary>
        [System.ComponentModel.DataAnnotations.Required]
        public string TargetVariableKey { get; set; } = string.Empty;

        /// <summary>是否启用该转换规则</summary>
        public bool Active { get; set; }
    }
}
