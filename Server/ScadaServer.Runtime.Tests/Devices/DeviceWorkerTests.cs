using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Runtime.Devices;
using Xunit;

namespace ScadaServer.Runtime.Tests.Devices
{
    /// <summary>
    /// DeviceWorker 空转等待计算单元测试（CPU 忙循环缺陷回归防护）。
    /// <para>
    /// 缺陷背景：空转分支的 soonest 计算未排除订阅变量，而订阅变量的 NextPollTime
    /// 停留在注册时刻（不再推进），把空转等待钳成 0 —— 循环体无任何 await，
    /// 退化为打满单个 CPU 核心的忙循环（纯订阅设备永久忙等、混合设备在轮询间隙忙等），
    /// 表现为"添加（启用）一个 OPC UA 订阅设备后 CPU 飙升至 ~60%"。
    /// </para>
    /// <para>
    /// 覆盖 <see cref="DeviceWorker.ComputeIdleWaitMs"/>（internal，经 InternalsVisibleTo 断言）
    /// 与 <see cref="VariableRuntime.PollingIntervalMs"/> 的运行时安全下限。
    /// </para>
    /// </summary>
    public class DeviceWorkerTests
    {
        // ===================== 核心回归：订阅变量不得参与空转调度 =====================

        [Fact]
        public void ComputeIdleWait_PureSubscriptionDevice_FallsBackToDevicePollingInterval()
        {
            // 纯订阅设备：唯一变量是订阅模式，NextPollTime 停留在注册时刻（过去）。
            // 修复前：soonest 被订阅变量的过去时刻拉低 → waitMs = 0 → 忙循环；
            // 修复后：订阅变量被排除 → soonest 保持 MaxValue → 回退设备级 PollingInterval。
            var runtime = CreateRuntime(pollingInterval: 1000);
            var subscribed = CreateVariable(UpdateModeEnum.Subscription);
            subscribed.NextPollTime = DateTime.UtcNow.AddHours(-1); // 注册时刻残留（过去）
            runtime.Variables[subscribed.Instance!.Id] = subscribed;

            var waitMs = ComputeIdleWaitMsViaCollect(runtime, DateTime.UtcNow);

            Assert.Equal(1000, waitMs); // 正常休眠 1s，而不是 0（忙循环）
        }

        [Fact]
        public void ComputeIdleWait_MixedDevice_WaitsUntilNextPollingVariable()
        {
            // 混合设备：订阅变量 NextPollTime 在过去 + 轮询变量 500ms 后到期。
            // 修复前：soonest 取订阅变量的过去时刻 → waitMs = 0（轮询间隙忙等）；
            // 修复后：soonest 只看轮询变量 → 精确休眠到下次轮询。
            var runtime = CreateRuntime(pollingInterval: 5000);
            var now = DateTime.UtcNow;

            var subscribed = CreateVariable(UpdateModeEnum.Subscription, id: 10);
            subscribed.NextPollTime = now.AddHours(-1); // 过去时刻残留
            runtime.Variables[subscribed.Instance!.Id] = subscribed;

            var polled = CreateVariable(UpdateModeEnum.Polling, id: 11);
            polled.NextPollTime = now.AddMilliseconds(500); // 500ms 后到期
            runtime.Variables[polled.Instance!.Id] = polled;

            var waitMs = ComputeIdleWaitMsViaCollect(runtime, now);

            Assert.Equal(500, waitMs); // 轮询变量的真实到期间隔，不被订阅变量钳成 0
        }

        // ===================== 既有语义保持（无回归） =====================

        [Fact]
        public void ComputeIdleWait_EmptyDevice_UsesDevicePollingInterval()
        {
            // 无任何变量（新设备未配变量）：回退设备级 PollingInterval（修复前后语义一致）。
            var runtime = CreateRuntime(pollingInterval: 1000);

            Assert.Equal(1000, ComputeIdleWaitMsViaCollect(runtime, DateTime.UtcNow));
        }

        [Fact]
        public void ComputeIdleWait_AllVariablesDisabled_UsesDevicePollingInterval()
        {
            // 全部变量被禁用（含 NextPollTime 残留在过去的禁用轮询变量）：不参与调度，回退设备级间隔。
            var runtime = CreateRuntime(pollingInterval: 1500);
            var now = DateTime.UtcNow;

            var disabled = CreateVariable(UpdateModeEnum.Polling, id: 20, enabled: false);
            disabled.NextPollTime = now.AddHours(-1);
            runtime.Variables[disabled.Instance!.Id] = disabled;

            Assert.Equal(1500, ComputeIdleWaitMsViaCollect(runtime, now));
        }

        [Fact]
        public void ComputeIdleWait_FarFutureNextPoll_ClampedToCeiling()
        {
            // 下次轮询在 5s 后：休眠收敛到上限 2000ms，保证取消/配置变更的响应性（既有语义）。
            var runtime = CreateRuntime(pollingInterval: 5000);
            var now = DateTime.UtcNow;

            var polled = CreateVariable(UpdateModeEnum.Polling);
            polled.NextPollTime = now.AddSeconds(5);
            runtime.Variables[polled.Instance!.Id] = polled;

            Assert.Equal(2000, ComputeIdleWaitMsViaCollect(runtime, now));
        }

        // ===================== 防御下限：异常配置不得退化为忙循环 =====================

        [Fact]
        public void ComputeIdleWait_ZeroDeviceInterval_ClampedToFloor()
        {
            // 设备级 PollingInterval 被配成 0（DTO 无范围校验，可落库）：
            // 修复前空设备/纯订阅设备 waitMs = 0 → 忙循环；修复后收敛到下限 100ms。
            var runtime = CreateRuntime(pollingInterval: 0);

            Assert.Equal(100, ComputeIdleWaitMsViaCollect(runtime, DateTime.UtcNow));
        }

        [Fact]
        public void ComputeIdleWait_NegativeDeviceInterval_ClampedToFloor()
        {
            // 负值同型防御。
            var runtime = CreateRuntime(pollingInterval: -1000);

            Assert.Equal(100, ComputeIdleWaitMsViaCollect(runtime, DateTime.UtcNow));
        }

        [Fact]
        public void ComputeIdleWait_DuePollingVariable_ClampedToFloor()
        {
            // 轮询变量已到期（NextPollTime = now）：实际会先进 due 分支不进空转，
            // 此处断言即使因时序边界进入空转分支，等待也有下限兜底（不忙循环）。
            var runtime = CreateRuntime(pollingInterval: 5000);
            var now = DateTime.UtcNow;

            var polled = CreateVariable(UpdateModeEnum.Polling);
            polled.NextPollTime = now; // 恰好到期
            runtime.Variables[polled.Instance!.Id] = polled;

            Assert.Equal(100, ComputeIdleWaitMsViaCollect(runtime, now));
        }

        // ===================== 变量级轮询间隔的运行时安全下限 =====================

        [Fact]
        public void PollingIntervalMs_ExplicitZeroOrNegative_ClampedToTenMs()
        {
            // 显式配置 0/负值（清空表单被序列化为 0 等）：修复前返回 0 →
            // NextPollTime 推进 0ms → 每轮都到期 → 高频忙循环；修复后收敛到 10ms。
            Assert.Equal(10, CreateVariable(UpdateModeEnum.Polling, intervalMs: 0).PollingIntervalMs);
            Assert.Equal(10, CreateVariable(UpdateModeEnum.Polling, intervalMs: -5).PollingIntervalMs);
        }

        [Fact]
        public void PollingIntervalMs_NullFallsBackToDefault_AndNormalValueKept()
        {
            // 未配置（null）回退 1000ms；正常配置原样生效（运行时收敛不改变合法语义）。
            Assert.Equal(1000, CreateVariable(UpdateModeEnum.Polling, intervalMs: null).PollingIntervalMs);
            Assert.Equal(250, CreateVariable(UpdateModeEnum.Polling, intervalMs: 250).PollingIntervalMs);
        }

        // ===================== 测试工具 =====================

        /// <summary>
        /// 组合调用 <see cref="DeviceWorker.CollectDueAndSoonest"/> 与
        /// <see cref="DeviceWorker.ComputeIdleWaitMs"/>，等价于改造前的单方法入口，
        /// 使既有用例断言无需改动即可继续回归（排除订阅变量 / clamp 上下限）。
        /// </summary>
        private static int ComputeIdleWaitMsViaCollect(DeviceRuntime runtime, DateTime now)
            => DeviceWorker.ComputeIdleWaitMs(
                DeviceWorker.CollectDueAndSoonest(runtime.Variables, now).Soonest,
                runtime.Device.PollingInterval,
                now);

        private static DeviceRuntime CreateRuntime(int pollingInterval)
            => new(new Device { Id = 1, Key = "D1", PollingInterval = pollingInterval });

        /// <summary>
        /// 构造运行时变量。默认：启用、PollingIntervalMs=null（运行时回退 1000ms）。
        /// id 为 DataPointMapping.Id（runtime.Variables 的键），缺省 1。
        /// </summary>
        private static VariableRuntime CreateVariable(
            UpdateModeEnum updateMode,
            int id = 1,
            bool enabled = true,
            int? intervalMs = null)
        {
            var definition = new DataPoint
            {
                Id = 100,
                Key = "VAR-" + id,
                Name = "变量" + id,
                DataType = DataTypeEnum.REAL,
                StoreMode = StoreModeEnum.None,
                StoreIntervalMs = 300000
            };

            return new VariableRuntime
            {
                DeviceId = 1,
                Definition = definition,
                Instance = new DataPointMapping
                {
                    Id = id,
                    DeviceId = 1,
                    DataPointId = definition.Id,
                    IsEnabled = enabled,
                    PollingIntervalMs = intervalMs,
                    UpdateMode = updateMode
                }
            };
        }
    }
}
