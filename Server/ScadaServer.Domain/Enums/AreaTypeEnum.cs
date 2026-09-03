namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 区域类型枚举：定义组织树中每个区域的层级类型。
    /// <para>
    /// 与目标设计一致（Factory/Workshop/ProductionLine/Area/Warehouse），
    /// 前端可根据类型做差异化展示（图标、操作等）。
    /// </para>
    /// </summary>
    public enum AreaTypeEnum
    {
        /// <summary>
        /// 工厂/公司（组织树顶层）
        /// </summary>
        Factory = 1,

        /// <summary>
        /// 车间
        /// </summary>
        Workshop = 2,

        /// <summary>
        /// 生产线
        /// </summary>
        ProductionLine = 3,

        /// <summary>
        /// 区域（默认类型，设备直接挂载的组织单元）
        /// </summary>
        Area = 4,

        /// <summary>
        /// 仓库
        /// </summary>
        Warehouse = 5
    }
}
