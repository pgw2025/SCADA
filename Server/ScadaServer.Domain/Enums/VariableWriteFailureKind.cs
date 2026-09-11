namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 变量写入失败分类（根因 A）。由 <c>RuntimeManager.WriteVariableAsync</c> 在失败路径返回，
    /// 供变量绑定引擎区分「瞬时可重试」与「确定性不可重试」，实现有限重试与离线补写。
    /// </summary>
    public enum VariableWriteFailureKind
    {
        /// <summary>写入成功（无失败）。</summary>
        None = 0,

        /// <summary>目标设备不在运行中（未注册/已过期）。瞬时类：可等待就绪后重试。</summary>
        DeviceNotRunning = 1,

        /// <summary>目标变量不存在（设备下无该业务键）。确定性：应由保存期校验拦截。</summary>
        VariableNotExist = 2,

        /// <summary>目标变量已禁用。确定性配置问题。</summary>
        VariableDisabled = 3,

        /// <summary>目标变量为只读，禁止写入。确定性配置问题。</summary>
        VariableReadOnly = 4,

        /// <summary>设备驱动未就绪（Driver 为空）。瞬时类。</summary>
        DriverNotReady = 5,

        /// <summary>设备连接态非 Connected（未连接/连接中）。瞬时类。</summary>
        DeviceNotConnected = 6,

        /// <summary>写入值越界（低于下限或高于上限）。确定性（Reject 策略）或可被 Clamp 消除。</summary>
        OutOfRange = 7,

        /// <summary>驱动写入超时。瞬时类（底层写入可能迟到落地）。</summary>
        Timeout = 8,

        /// <summary>驱动写入抛出异常（网络/协议错误等）。瞬时类。</summary>
        DriverError = 9,

        /// <summary>未归类失败。</summary>
        Unknown = 10
    }
}