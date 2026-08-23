using SqlSugar;

namespace ScadaServer.Domain.Entities
{
    /// <summary>
    /// 传感器实体
    /// </summary>
    [SugarTable("Sensors")]
    public class Sensor : EntityBase
    {
        /// <summary>
        /// 关联的设备ID
        /// </summary>
        public int DeviceId { get; set; }

        /// <summary>
        /// 关联的设备
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(DeviceId))]
        public Device Device { get; set; }

        /// <summary>
        /// 变量键
        /// </summary>
        public string VariableKey { get; set; }

        /// <summary>
        /// 传感器名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 单位
        /// </summary>
        public string Unit { get; set; }

        /// <summary>
        /// 最后采集值
        /// </summary>
        public double LastValue { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdateTime { get; set; }
    }
}