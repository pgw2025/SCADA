using System.ComponentModel.DataAnnotations;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 传感器 DTO（映射设备的某个采集变量及其当前实时值）。
    /// </summary>
    public class SensorDto
    {
        /// <summary>传感器ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>所属设备ID</summary>
        [Range(1, int.MaxValue, ErrorMessage = "请选择所属设备")]
        public int DeviceId { get; set; }

        /// <summary>关联的变量业务键</summary>
        [Required(ErrorMessage = "变量业务键不能为空")]
        [StringLength(100, ErrorMessage = "变量业务键不能超过100个字符")]
        public string VariableKey { get; set; } = string.Empty;

        /// <summary>传感器名称</summary>
        [Required(ErrorMessage = "传感器名称不能为空")]
        [StringLength(100, ErrorMessage = "传感器名称不能超过100个字符")]
        public string Name { get; set; } = string.Empty;

        /// <summary>单位</summary>
        [StringLength(20, ErrorMessage = "单位不能超过20个字符")]
        public string Unit { get; set; } = string.Empty;

        /// <summary>最后一次采集到的数值</summary>
        public double LastValue { get; set; }

        /// <summary>最后一次值更新时间</summary>
        public DateTime LastUpdateTime { get; set; }
    }
}
