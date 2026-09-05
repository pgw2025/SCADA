using ScadaServer.Domain.Enums;

namespace ScadaServer.Domain.Interfaces
{
    /// <summary>
    /// 变量运行时只读视图（即"RuntimeVariable"的领域抽象）。
    /// <para>
    /// 目的：让协议驱动层只依赖本接口即可完成读取/订阅，而不感知具体运行时类型，
    /// 从而保证 <b>驱动不允许知道 DataModel / DataPoint</b>（地址等实现细节
    /// 已由运行时解析为 <see cref="Address"/> 暴露，来源为 DataPointMapping）。
    /// </para>
    /// <para>
    /// 本接口定义在 Domain 层而非 Runtime 层，是为了保持依赖方向单向：
    /// Domain ← Infrastructure(驱动/工厂) ← Runtime(运行时实现本接口)，
    /// 避免 Infrastructure 与 Runtime 形成循环引用。
    /// </para>
    /// </summary>
    public interface IRuntimeVariable
    {
        /// <summary>变量业务键（来自 DataPoint.Key）。</summary>
        string Key { get; }

        /// <summary>变量名称（来自 DataPoint.Name）。</summary>
        string Name { get; }

        /// <summary>数据类型（来自 DataPoint.DataType）。</summary>
        DataTypeEnum DataType { get; }

        /// <summary>单位（来自 DataPoint.Unit）。</summary>
        string? Unit { get; }

        /// <summary>最小值（来自 DataPoint.Min）。</summary>
        double? Min { get; }

        /// <summary>最大值（来自 DataPoint.Max）。</summary>
        double? Max { get; }

        /// <summary>
        /// 实际寄存器/节点地址。
        /// <para>权威来源：DataPointMapping.Address（设备实例配置）。驱动读取一律使用本属性，禁止回退到模型模板地址。</para>
        /// </summary>
        string Address { get; }

        /// <summary>位偏移（DataPointMapping.BitOffset 优先，空则回退模板）。</summary>
        int? BitOffset { get; }

        /// <summary>轮询间隔（毫秒）（DataPointMapping.PollingIntervalMs 优先，空则回退模板）。</summary>
        int PollingIntervalMs { get; }

        // 缩放已由"线性 Slope/Offset"改为"公式表达式"，且不再暴露给协议驱动：
        // 工程换算是 Runtime 层值转换职责（见 VariableScaling），驱动只认原始值/地址/数据类型。

        /// <summary>死区（DataPointMapping.DeadBandOverride 优先，空则回退模板）。</summary>
        double? DeadBand { get; }

        /// <summary>该变量在设备实例上是否启用（DataPointMapping.IsEnabled）。</summary>
        bool IsEnabled { get; }

        /// <summary>
        /// 有效读写模式（DataPointMapping.AccessModeOverride 优先，空则回退模板 AccessMode）。
        /// Read=只读 / Write=只写 / ReadWrite=读写。
        /// </summary>
        string EffectiveAccessMode { get; }

        /// <summary>是否只读（禁止外部写入）。仅当有效读写模式为 Read（只读）时为 true。</summary>
        bool IsReadOnly { get; }

        /// <summary>
        /// 设备变量实例 ID（DataPointMapping.Id），跨设备全局唯一，订阅项身份标识。
        /// <para>驱动订阅回调一律以本 ID 路由到目标设备运行时，禁止使用 <see cref="Key"/>
        /// （同名变量在不同设备上 Key 相同，会导致回调路由串台）。</para>
        /// </summary>
        int InstanceId { get; }

        /// <summary>变量更新方式（Polling=自主轮询 / Subscription=订阅推送）。</summary>
        UpdateModeEnum UpdateMode { get; }
    }
}
