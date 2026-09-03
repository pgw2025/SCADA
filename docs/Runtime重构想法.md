
可以。结合你现在这套 Area → Device → Controller → Connection → DataPoint → Mapping 的设计，我建议你的 DeviceRuntime 不要只做成一个“Dictionary<long, object> 存实时值”的简单缓存。

如果你的目标是以后做到 1000～10000 台设备，我更建议把 Runtime 设计成一个真正的设备运行时对象：

> 数据库负责“设备是什么、怎么连接、采集什么”；Runtime 负责“设备现在怎么样、现在有什么值、通信是否正常、发生了什么状态变化”。




---

一、先确定 DeviceRuntime 的定位

整个系统可以理解成：

数据库
                      │
          ┌───────────┴───────────┐
          │                       │
     Device 配置              DataPointMapping
          │                       │
          └───────────┬───────────┘
                      ↓
                DeviceWorker
                      ↓
                ProtocolDriver
                 ↓          ↓
                PLC        OPC UA
                      │
                      ↓
                DeviceRuntime
                      │
          ┌───────────┼───────────┐
          ↓           ↓           ↓
       前端实时     报警引擎     数据持久化
       SignalR

所以：

Device 是“设备实体”

例如：

输送机01

DeviceRuntime 是“输送机01当前运行状态”

例如：

输送机01

通信状态：Connected
运行状态：Running
速度：1.25 m/s
计数：15382
温度：42.3 ℃
报警：无

最后通信：
2026-09-03 18:13:42

数据质量：
Good


---

二、我建议 DeviceRuntime 分成 5 个区域

不要把所有东西全部扔进一个 Dictionary。

建议：

DeviceRuntime
│
├── Identity       设备身份
│
├── Connection     通信状态
│
├── Status         设备运行状态
│
├── Values         实时数据
│
└── Statistics     运行统计

进一步：

DeviceRuntime
│
├── DeviceId
├── Code
├── Name
│
├── ConnectionState
│   ├── State
│   ├── LastSuccessTime
│   ├── LastErrorTime
│   ├── LastError
│   ├── ConsecutiveFailures
│   └── ReconnectCount
│
├── Status
│   ├── DeviceStatus
│   ├── AlarmState
│   └── ...
│
├── Values
│   ├── DataPointId → RuntimeValue
│   ├── DataPointId → RuntimeValue
│   └── ...
│
└── Statistics
    ├── ReadCount
    ├── ErrorCount
    ├── LastReadDuration
    └── ...

这样以后扩展非常舒服。


---

三、第一版 DeviceRuntime 我建议这样写

例如：

public sealed class DeviceRuntime
{
    public long DeviceId { get; }

    public string Code { get; }

    public string Name { get; }

    /// <summary>
    /// 通信状态
    /// </summary>
    public ConnectionRuntime Connection { get; }

    /// <summary>
    /// 设备运行状态
    /// </summary>
    public DeviceStatusRuntime Status { get; }

    /// <summary>
    /// 实时数据
    /// </summary>
    public RuntimeValueStore Values { get; }

    /// <summary>
    /// 运行统计
    /// </summary>
    public RuntimeStatistics Statistics { get; }

    public DeviceRuntime(
        long deviceId,
        string code,
        string name)
    {
        DeviceId = deviceId;
        Code = code;
        Name = name;

        Connection = new ConnectionRuntime();
        Status = new DeviceStatusRuntime();
        Values = new RuntimeValueStore();
        Statistics = new RuntimeStatistics();
    }
}

这样以后不会出现这种情况：

runtime["xxx"]
runtime["yyy"]
runtime["zzz"]

最后整个 Runtime 变成一个“大杂烩”。


---

四、ConnectionRuntime 非常重要

工业系统里面：

> 设备状态 ≠ PLC通信状态



比如：

PLC连接正常
但机器停止

或者：

PLC连接断开
机器实际可能还在运行

所以一定要分开。

我建议：

public sealed class ConnectionRuntime
{
    public ConnectionState State { get; private set; }

    public DateTime? LastSuccessTime { get; private set; }

    public DateTime? LastErrorTime { get; private set; }

    public string? LastError { get; private set; }

    public int ConsecutiveFailures { get; private set; }

    public int ReconnectCount { get; private set; }

    public long ReadCount { get; private set; }

    public long ErrorCount { get; private set; }

    public void MarkSuccess()
    {
        State = ConnectionState.Connected;

        LastSuccessTime = DateTime.UtcNow;

        ConsecutiveFailures = 0;

        ReadCount++;
    }

    public void MarkError(string error)
    {
        State = ConnectionState.Faulted;

        LastErrorTime = DateTime.UtcNow;

        LastError = error;

        ConsecutiveFailures++;

        ErrorCount++;
    }

    public void MarkReconnecting()
    {
        State = ConnectionState.Reconnecting;

        ReconnectCount++;
    }
}

状态：

public enum ConnectionState
{
    Unknown = 0,

    Connecting = 1,

    Connected = 2,

    Reconnecting = 3,

    Faulted = 4,

    Disabled = 5
}

这样前端就可以直接显示：

🟢 PLC正常
🟡 正在重连
🔴 PLC通信故障
⚪ 设备已禁用


---

五、DeviceStatusRuntime 专门负责“机器状态”

这个和通信状态分开。

例如：

public sealed class DeviceStatusRuntime
{
    public DeviceRunState RunState { get; private set; }

    public bool HasAlarm { get; private set; }

    public DateTime? StateChangedAt { get; private set; }

    public void SetRunState(DeviceRunState state)
    {
        if (RunState == state)
            return;

        RunState = state;

        StateChangedAt = DateTime.UtcNow;
    }

    public void SetAlarm(bool hasAlarm)
    {
        HasAlarm = hasAlarm;
    }
}

例如：

public enum DeviceRunState
{
    Unknown = 0,

    Stopped = 1,

    Running = 2,

    Paused = 3,

    Fault = 4,

    Maintenance = 5
}

于是：

通信状态：
Connected

设备状态：
Running

是两个完全不同的东西。


---

六、RuntimeValue 是整个 Runtime 最核心的部分

之前我们设计的是：

public class RuntimeValue
{
    public object? Value { get; set; }

    public DataQuality Quality { get; set; }

    public DateTime Timestamp { get; set; }

    public DateTime? SourceTimestamp { get; set; }

    public bool Changed { get; set; }
}

我建议再完善一点。

public sealed class RuntimeValue
{
    /// <summary>
    /// DataPoint ID
    /// </summary>
    public long DataPointId { get; init; }

    /// <summary>
    /// 当前值
    /// </summary>
    public object? Value { get; private set; }

    /// <summary>
    /// 上一次值
    /// </summary>
    public object? PreviousValue { get; private set; }

    /// <summary>
    /// 数据质量
    /// </summary>
    public DataQuality Quality { get; private set; }

    /// <summary>
    /// 系统接收到数据的时间
    /// </summary>
    public DateTime Timestamp { get; private set; }

    /// <summary>
    /// PLC/OPC UA原始时间
    /// </summary>
    public DateTime? SourceTimestamp { get; private set; }

    /// <summary>
    /// 是否发生变化
    /// </summary>
    public bool Changed { get; private set; }

    /// <summary>
    /// 更新次数
    /// </summary>
    public long UpdateCount { get; private set; }

    public void Update(
        object? value,
        DataQuality quality,
        DateTime timestamp,
        DateTime? sourceTimestamp = null)
    {
        PreviousValue = Value;

        Changed = !Equals(Value, value);

        Value = value;

        Quality = quality;

        Timestamp = timestamp;

        SourceTimestamp = sourceTimestamp;

        UpdateCount++;
    }
}


---

七、DataQuality 建议做成枚举

例如：

public enum DataQuality
{
    Unknown = 0,

    Good = 1,

    Bad = 2,

    Uncertain = 3
}

以后可以继续细化：

Good
Bad
Uncertain
NotConnected
Timeout
ConfigError
DataTypeError

例如：

Speed = 1.25
Quality = Good

而 PLC 断开：

Speed = 1.25
Quality = Bad

注意：

> PLC断开以后，不要简单把 Speed 改成 0。



因为：

PLC断开

和：

PLC告诉你 Speed = 0

完全是两个不同的含义。

这是工业 SCADA 系统里面非常重要的一点。


---

八、不要直接使用 Dictionary<long, RuntimeValue>

我更推荐封装一层：

public sealed class RuntimeValueStore
{
    private readonly ConcurrentDictionary<long, RuntimeValue> _values = new();

    public RuntimeValue? Get(long dataPointId)
    {
        return _values.TryGetValue(dataPointId, out var value)
            ? value
            : null;
    }

    public void Update(
        long dataPointId,
        object? value,
        DataQuality quality,
        DateTime timestamp,
        DateTime? sourceTimestamp = null)
    {
        var runtimeValue = _values.GetOrAdd(
            dataPointId,
            id => new RuntimeValue
            {
                DataPointId = id
            });

        runtimeValue.Update(
            value,
            quality,
            timestamp,
            sourceTimestamp);
    }

    public IReadOnlyCollection<RuntimeValue> GetAll()
    {
        return _values.Values.ToList();
    }
}

这样以后如果你需要：

批量更新
删除
获取发生变化的数据
获取某个数据点
获取所有数据
统计

都可以在这里实现。


---

九、但是这里有一个很重要的问题：线程安全

你的系统未来很可能是：

DeviceWorker
     ↓
读取PLC
     ↓
更新 Runtime
        ↘
         SignalR
        ↘
         AlarmEngine
        ↘
         HistoryWriter

多个线程可能同时访问：

DeviceRuntime

所以不能简单：

Dictionary<long, RuntimeValue>

然后大家随便读写。

建议：

RuntimeManager
       │
       ├── DeviceRuntime 1
       ├── DeviceRuntime 2
       ├── DeviceRuntime 3
       ├── DeviceRuntime 4
       └── ...

使用：

ConcurrentDictionary<long, DeviceRuntime>

而 DeviceRuntime 内部的 ValueStore 再自己负责线程安全。


---

十、RuntimeManager 是整个 Runtime 的“大管家”

推荐：

public sealed class RuntimeManager
{
    private readonly ConcurrentDictionary<long, DeviceRuntime> _runtimes = new();

    public DeviceRuntime GetOrCreate(
        long deviceId,
        string code,
        string name)
    {
        return _runtimes.GetOrAdd(
            deviceId,
            _ => new DeviceRuntime(
                deviceId,
                code,
                name));
    }

    public bool TryGet(
        long deviceId,
        out DeviceRuntime? runtime)
    {
        return _runtimes.TryGetValue(
            deviceId,
            out runtime);
    }

    public bool Remove(long deviceId)
    {
        return _runtimes.TryRemove(
            deviceId,
            out _);
    }

    public IReadOnlyCollection<DeviceRuntime> GetAll()
    {
        return _runtimes.Values.ToList();
    }
}


---

十一、但是我更推荐再增加 Runtime 生命周期

你的系统不是：

程序启动
↓
加载10000台设备
↓
永远运行

而应该是：

设备配置
 ↓
创建Runtime
 ↓
启动Worker
 ↓
连接PLC
 ↓
采集
 ↓
运行
 ↓
设备禁用
 ↓
停止Worker
 ↓
释放Runtime

所以可以设计：

DeviceRuntime
       │
       ├── Created
       ├── Starting
       ├── Running
       ├── Stopping
       └── Stopped

例如：

public enum RuntimeState
{
    Created = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Stopped = 4
}


---

十二、我建议最终形成这样的 Runtime

这是我比较推荐你现在就采用的版本：

DeviceRuntime
│
├── Identity
│   ├── DeviceId
│   ├── Code
│   └── Name
│
├── RuntimeState
│   ├── State
│   └── StartedAt
│
├── Connection
│   ├── State
│   ├── LastSuccessTime
│   ├── LastErrorTime
│   ├── LastError
│   ├── ConsecutiveFailures
│   └── ReconnectCount
│
├── Status
│   ├── RunState
│   ├── HasAlarm
│   └── StateChangedAt
│
├── Values
│   ├── RunStatus
│   ├── Speed
│   ├── Count
│   ├── Temperature
│   └── ...
│
└── Statistics
    ├── ReadCount
    ├── ErrorCount
    ├── LastReadTime
    └── LastReadDuration


---

十三、DeviceWorker 怎么和 Runtime 配合？

这是非常关键的一点。

例如：

public class DeviceWorker
{
    private readonly DeviceRuntime _runtime;
    private readonly IProtocolDriver _driver;

    public DeviceWorker(
        DeviceRuntime runtime,
        IProtocolDriver driver)
    {
        _runtime = runtime;
        _driver = driver;
    }

    public async Task ExecuteAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var values = await _driver.ReadAsync(
                    cancellationToken);

                foreach (var value in values)
                {
                    _runtime.Values.Update(
                        value.DataPointId,
                        value.Value,
                        value.Quality,
                        DateTime.UtcNow,
                        value.SourceTimestamp);
                }

                _runtime.Connection.MarkSuccess();
            }
            catch (Exception ex)
            {
                _runtime.Connection.MarkError(
                    ex.Message);
            }

            await Task.Delay(
                500,
                cancellationToken);
        }
    }
}

但是这里还有一个重要升级：

不要让 Worker 自己决定所有 DataPoint 的采集周期。

因为你的 DataPointMapping 已经有：

ScanInterval

所以最终应该是：

DataPointMapping
       ↓
采集调度器
       ↓
500ms → RunStatus
500ms → Speed
1000ms → Count
5000ms → Temperature

这就和你之前设计的：

DeviceScheduler

联系起来了。


---

十四、最终的数据流我建议这样设计

这是你整个系统最重要的一张逻辑图：

EF Core
                      │
        ┌─────────────┼─────────────┐
        ↓             ↓             ↓
      Device       Connection    Mapping
        │             │             │
        └─────────────┼─────────────┘
                      ↓
                DeviceScheduler
                      ↓
                 DeviceWorker
                      ↓
                IProtocolDriver
                 ↓          ↓
                S7         OPC UA
                 ↓          ↓
                    PLC
                     │
                     ↓
                  原始数据
                     │
                     ↓
                 数据转换
                     │
                     ↓
                DeviceRuntime
                     │
       ┌─────────────┼──────────────┐
       ↓             ↓              ↓
   SignalR       AlarmEngine    HistoryWriter
       ↓             ↓              ↓
      Vue           Alarm           EF Core


---

十五、特别建议：Runtime 不要依赖 EF Core Entity

例如不要这样：

public class DeviceRuntime
{
    public Device Device { get; set; }

    public ICollection<DataPointMapping> Mappings { get; set; }
}

这样会导致 Runtime 和 EF Core 强耦合。

应该：

EF Core Entity
       ↓
RuntimeConfig
       ↓
DeviceRuntime

例如：

public sealed class DeviceRuntimeConfig
{
    public long DeviceId { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public long ConnectionId { get; init; }

    public string Protocol { get; init; } = string.Empty;

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; }

    public IReadOnlyList<DataPointRuntimeConfig> DataPoints
        { get; init; }
        = [];
}

启动的时候：

数据库
 ↓
读取 Device
 ↓
读取 Connection
 ↓
读取 Mapping
 ↓
组装 RuntimeConfig
 ↓
创建 DeviceRuntime

这样 Runtime 就是一个纯运行时对象。


---

十六、再往后，你的 Runtime 可以支持“快照”

这个对你的 Vue 前端特别有用。

例如：

public sealed class DeviceRuntimeSnapshot
{
    public long DeviceId { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public RuntimeState RuntimeState { get; init; }

    public ConnectionState ConnectionState { get; init; }

    public DeviceRunState RunState { get; init; }

    public bool HasAlarm { get; init; }

    public DateTime? LastSuccessTime { get; init; }

    public IReadOnlyList<RuntimeValueDto> Values { get; init; }
        = [];
}

前端：

GET /api/devices/10001/runtime

得到：

{
  "deviceId": 10001,
  "code": "CONVEYOR001",
  "name": "输送机01",
  "connectionState": "Connected",
  "runState": "Running",
  "hasAlarm": false,
  "values": [
    {
      "dataPointId": 1,
      "value": true,
      "quality": "Good"
    },
    {
      "dataPointId": 2,
      "value": 1.25,
      "quality": "Good"
    }
  ]
}

而真正实时的时候：

DeviceRuntime
      ↓
SignalR
      ↓
Vue

这样前端就不需要不停地：

GET /api/devices/1/runtime
GET /api/devices/1/runtime
GET /api/devices/1/runtime
GET /api/devices/1/runtime
...


---

十七、我尤其建议你把“变化”作为 Runtime 的核心能力

比如 PLC：

Speed

1.20
1.21
1.21
1.21
1.25 ← Changed
1.25
1.25
1.30 ← Changed

Runtime 可以判断：

Changed = true;

然后：

Runtime
  ↓
Changed
  ├── SignalR 推送
  ├── HistoryWriter
  └── AlarmEngine

这样就形成：

> 采集一次，多路消费。



而不是：

SignalR自己读PLC
Alarm自己读PLC
History自己读PLC
API自己读PLC

这是架构上非常重要的一点。


---

十八、最终我建议你的 Runtime 层结构

如果让我现在帮你定项目结构，我会这么做：

Infrastructure
│
├── Drivers
│   ├── S7
│   └── OPCUA
│
└── Persistence
    └── ScadaDbContext


Application
│
├── Devices
│
├── Runtime
│   ├── DeviceRuntime.cs
│   ├── RuntimeManager.cs
│   ├── RuntimeValue.cs
│   ├── RuntimeValueStore.cs
│   ├── RuntimeSnapshot.cs
│   ├── ConnectionRuntime.cs
│   ├── DeviceStatusRuntime.cs
│   └── RuntimeStatistics.cs
│
├── Collection
│   ├── DeviceWorker.cs
│   ├── DeviceScheduler.cs
│   └── CollectionManager.cs
│
├── Alarm
│   └── AlarmEngine.cs
│
└── Realtime
    └── RuntimeHub.cs

整个系统最终就是：

┌──────────────┐
                 │   EF Core    │
                 │   数据库      │
                 └──────┬───────┘
                        │
                  RuntimeConfig
                        │
                        ↓
               ┌─────────────────┐
               │ RuntimeManager  │
               └────────┬────────┘
                        │
           ┌────────────┼────────────┐
           ↓            ↓            ↓
      DeviceRuntime  DeviceRuntime DeviceRuntime
           │
     ┌─────┼──────┬────────┐
     ↓     ↓      ↓        ↓
 Connection Status Values Statistics
                     │
                     ↓
                DeviceWorker
                     │
                     ↓
               ProtocolDriver
                     │
                     ↓
                    PLC

这一版我认为非常适合你现在的项目。

而且它不会把架构做得过度复杂：你现在先实现 DeviceRuntime + RuntimeManager + DeviceWorker 就可以跑起来，等后面增加 1000～10000 台设备、SignalR、报警、历史数据、多个 PLC/机器人控制器 时，再逐步扩展即可。

如果继续往下做，我建议下一步直接把 `DeviceRuntime + RuntimeManager + DeviceWorker + DeviceScheduler + IProtocolDriver` 五个类的完整 C# 实现定下来，因为这五个类一旦设计正确，你后面的 S7、OPC UA、SignalR 和 EF Core 基本就能顺着这个骨架往下搭。















可以。为了让你真正理解 Runtime 到底在干什么，我不直接上 10000 台设备的复杂代码，而是先做一个非常小、但架构和你最终项目一致的 Demo。

我们假设现场只有：

PLC001
└── 输送机01
    ├── RunStatus   Bool
    ├── Speed       Real
    ├── Count       Int
    └── Temperature Real

我们用一个假的 PLC Driver模拟 PLC，这样你不需要真的连接 S7-1500，也能完整看到：

> PLC → Driver → DeviceWorker → DeviceRuntime → Alarm → SignalR/API




---

一、先看完整运行过程

你先记住这一张图：

数据库
                      │
              Device + Mapping
                      │
                      ↓
             DeviceRuntimeConfig
                      │
                      ↓
               DeviceWorker
                      │
                      ↓
              IProtocolDriver
                      │
                 FakePLC
                      │
                      ↓
               PLC数据读取
                      │
                      ↓
                DeviceRuntime
                 │    │    │
        ┌────────┘    │    └─────────┐
        ↓             ↓              ↓
      前端          报警引擎       历史记录
     SignalR                        EF Core

这里最重要的是：

DeviceWorker 不保存设备状态。

它只是：

> “负责不停地从 PLC 读取数据，然后把数据交给 Runtime。”



真正保存当前状态的是：

DeviceRuntime


---

二、第一步：定义 PLC 返回的数据

先定义一个非常简单的数据结构：

public sealed class DriverValue
{
    public long DataPointId { get; init; }

    public object? Value { get; init; }

    public DataQuality Quality { get; init; }

    public DateTime SourceTimestamp { get; init; }
}

例如 PLC 返回：

RunStatus = true
Speed = 1.25
Count = 1538
Temperature = 42.3

实际上 Driver 返回的是：

[
    new DriverValue
    {
        DataPointId = 1,
        Value = true
    },

    new DriverValue
    {
        DataPointId = 2,
        Value = 1.25
    },

    new DriverValue
    {
        DataPointId = 3,
        Value = 1538
    },

    new DriverValue
    {
        DataPointId = 4,
        Value = 42.3
    }
]

注意：

这里已经开始体现你的设计思想：

DataPointId = 2

并不关心：

DB10.DBD4

它只知道：

> “这是 Speed 这个数据点。”



地址是 DataPointMapping 管理的。


---

三、第二步：定义 DataQuality

public enum DataQuality
{
    Unknown = 0,

    Good = 1,

    Bad = 2,

    Uncertain = 3
}

例如：

PLC正常：

Speed = 1.25
Quality = Good

PLC断开：

Speed = 1.25
Quality = Bad

注意：

不要把 PLC 断开直接变成 Speed = 0。

因为：

Speed = 0

可能代表机器真的停止。

而：

Speed = 1.25
Quality = Bad

代表：

> 最后一次知道速度是 1.25，但现在通信已经不可靠。




---

四、第三步：RuntimeValue

现在开始进入 Runtime 核心。

public sealed class RuntimeValue
{
    public long DataPointId { get; init; }

    public object? Value { get; private set; }

    public object? PreviousValue { get; private set; }

    public DataQuality Quality { get; private set; }

    public DateTime Timestamp { get; private set; }

    public DateTime? SourceTimestamp { get; private set; }

    public bool Changed { get; private set; }

    public long UpdateCount { get; private set; }

    public void Update(
        object? value,
        DataQuality quality,
        DateTime timestamp,
        DateTime? sourceTimestamp = null)
    {
        PreviousValue = Value;

        Changed = !Equals(Value, value);

        Value = value;

        Quality = quality;

        Timestamp = timestamp;

        SourceTimestamp = sourceTimestamp;

        UpdateCount++;
    }
}

假设第一次 PLC 返回：

Speed = 1.20

Runtime：

Value = 1.20
PreviousValue = null
Changed = true

第二次：

Speed = 1.20

变成：

Value = 1.20
PreviousValue = 1.20
Changed = false

第三次：

Speed = 1.50

变成：

Value = 1.50
PreviousValue = 1.20
Changed = true

这就是 Runtime 的价值。


---

五、第四步：RuntimeValueStore

一个设备有很多 DataPoint。

所以：

输送机01
│
├── RunStatus
├── Speed
├── Count
└── Temperature

我们用：

ConcurrentDictionary<long, RuntimeValue>

保存。

using System.Collections.Concurrent;

public sealed class RuntimeValueStore
{
    private readonly ConcurrentDictionary<long, RuntimeValue> _values = new();

    public RuntimeValue? Get(long dataPointId)
    {
        return _values.TryGetValue(
            dataPointId,
            out var value)
            ? value
            : null;
    }

    public void Update(
        long dataPointId,
        object? value,
        DataQuality quality,
        DateTime timestamp,
        DateTime? sourceTimestamp = null)
    {
        var runtimeValue = _values.GetOrAdd(
            dataPointId,
            id => new RuntimeValue
            {
                DataPointId = id
            });

        runtimeValue.Update(
            value,
            quality,
            timestamp,
            sourceTimestamp);
    }

    public IReadOnlyCollection<RuntimeValue> GetAll()
    {
        return _values.Values.ToList();
    }
}

现在：

RuntimeValueStore
│
├── 1 → RunStatus
├── 2 → Speed
├── 3 → Count
└── 4 → Temperature


---

六、第五步：通信状态

现在解决另一个问题：

> PLC 到底有没有连接？



定义：

public enum ConnectionState
{
    Unknown = 0,

    Connecting = 1,

    Connected = 2,

    Reconnecting = 3,

    Faulted = 4,

    Disabled = 5
}

然后：

public sealed class ConnectionRuntime
{
    public ConnectionState State { get; private set; }

    public DateTime? LastSuccessTime { get; private set; }

    public DateTime? LastErrorTime { get; private set; }

    public string? LastError { get; private set; }

    public int ConsecutiveFailures { get; private set; }

    public int ReconnectCount { get; private set; }

    public long ReadCount { get; private set; }

    public long ErrorCount { get; private set; }

    public void MarkSuccess()
    {
        State = ConnectionState.Connected;

        LastSuccessTime = DateTime.UtcNow;

        ConsecutiveFailures = 0;

        ReadCount++;
    }

    public void MarkError(string error)
    {
        State = ConnectionState.Faulted;

        LastErrorTime = DateTime.UtcNow;

        LastError = error;

        ConsecutiveFailures++;

        ErrorCount++;
    }
}


---

七、第六步：设备运行状态

通信状态和设备状态一定要分开。

例如：

PLC通信：Connected

机器状态：Stopped

这是完全正常的。

定义：

public enum DeviceRunState
{
    Unknown = 0,

    Stopped = 1,

    Running = 2,

    Paused = 3,

    Fault = 4,

    Maintenance = 5
}

然后：

public sealed class DeviceStatusRuntime
{
    public DeviceRunState RunState { get; private set; }

    public bool HasAlarm { get; private set; }

    public DateTime? StateChangedAt { get; private set; }

    public void SetRunState(DeviceRunState state)
    {
        if (RunState == state)
            return;

        RunState = state;

        StateChangedAt = DateTime.UtcNow;
    }

    public void SetAlarm(bool hasAlarm)
    {
        HasAlarm = hasAlarm;
    }
}


---

八、第七步：真正的 DeviceRuntime

现在把刚才几个东西组合起来：

public sealed class DeviceRuntime
{
    public long DeviceId { get; }

    public string Code { get; }

    public string Name { get; }

    public ConnectionRuntime Connection { get; }

    public DeviceStatusRuntime Status { get; }

    public RuntimeValueStore Values { get; }

    public DeviceRuntime(
        long deviceId,
        string code,
        string name)
    {
        DeviceId = deviceId;
        Code = code;
        Name = name;

        Connection = new ConnectionRuntime();

        Status = new DeviceStatusRuntime();

        Values = new RuntimeValueStore();
    }
}

现在一个 Runtime 就非常清晰：

DeviceRuntime
│
├── DeviceId = 10001
├── Code = CONVEYOR001
├── Name = 输送机01
│
├── Connection
│   ├── Connected
│   ├── LastSuccessTime
│   └── ErrorCount
│
├── Status
│   ├── Running
│   └── HasAlarm
│
└── Values
    ├── RunStatus = true
    ├── Speed = 1.25
    ├── Count = 1538
    └── Temperature = 42.3

这时候你应该开始理解 Runtime 是什么了。


---

九、第八步：RuntimeManager

假设系统里面有：

10000台设备

不能：

DeviceRuntime runtime;

只保存一台。

我们需要：

RuntimeManager
│
├── Device 1 → Runtime
├── Device 2 → Runtime
├── Device 3 → Runtime
├── ...
└── Device 10000 → Runtime

代码：

using System.Collections.Concurrent;

public sealed class RuntimeManager
{
    private readonly ConcurrentDictionary<long, DeviceRuntime>
        _runtimes = new();

    public DeviceRuntime GetOrCreate(
        long deviceId,
        string code,
        string name)
    {
        return _runtimes.GetOrAdd(
            deviceId,
            _ => new DeviceRuntime(
                deviceId,
                code,
                name));
    }

    public bool TryGet(
        long deviceId,
        out DeviceRuntime? runtime)
    {
        return _runtimes.TryGetValue(
            deviceId,
            out runtime);
    }

    public bool Remove(long deviceId)
    {
        return _runtimes.TryRemove(
            deviceId,
            out _);
    }
}


---

十、现在模拟一个 PLC

我们暂时不接西门子 PLC。

写一个假的：

public interface IProtocolDriver
{
    Task<IReadOnlyList<DriverValue>> ReadAsync(
        CancellationToken cancellationToken);
}

然后：

public sealed class FakePlcDriver : IProtocolDriver
{
    private readonly Random _random = new();

    private int _count;

    public Task<IReadOnlyList<DriverValue>> ReadAsync(
        CancellationToken cancellationToken)
    {
        _count++;

        var speed = 1.0 + _random.NextDouble();

        var temperature = 40 + _random.NextDouble() * 5;

        var values = new List<DriverValue>
        {
            new()
            {
                DataPointId = 1,
                Value = true,
                Quality = DataQuality.Good,
                SourceTimestamp = DateTime.UtcNow
            },

            new()
            {
                DataPointId = 2,
                Value = speed,
                Quality = DataQuality.Good,
                SourceTimestamp = DateTime.UtcNow
            },

            new()
            {
                DataPointId = 3,
                Value = _count,
                Quality = DataQuality.Good,
                SourceTimestamp = DateTime.UtcNow
            },

            new()
            {
                DataPointId = 4,
                Value = temperature,
                Quality = DataQuality.Good,
                SourceTimestamp = DateTime.UtcNow
            }
        };

        return Task.FromResult<IReadOnlyList<DriverValue>>(values);
    }
}

这个 Driver 就相当于：

S7Driver

只不过现在是：

FakePlcDriver


---

十一、现在才轮到 DeviceWorker

这是最关键的一步。

public sealed class DeviceWorker
{
    private readonly DeviceRuntime _runtime;

    private readonly IProtocolDriver _driver;

    public DeviceWorker(
        DeviceRuntime runtime,
        IProtocolDriver driver)
    {
        _runtime = runtime;

        _driver = driver;
    }

    public async Task ExecuteAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var values =
                    await _driver.ReadAsync(
                        cancellationToken);

                foreach (var value in values)
                {
                    _runtime.Values.Update(
                        value.DataPointId,
                        value.Value,
                        value.Quality,
                        DateTime.UtcNow,
                        value.SourceTimestamp);
                }

                _runtime.Connection.MarkSuccess();

                Console.WriteLine(
                    $"[{_runtime.Name}] PLC读取成功");
            }
            catch (Exception ex)
            {
                _runtime.Connection.MarkError(
                    ex.Message);

                Console.WriteLine(
                    $"[{_runtime.Name}] PLC读取失败：{ex.Message}");
            }

            await Task.Delay(
                1000,
                cancellationToken);
        }
    }
}

现在整个过程就是：

DeviceWorker
     │
     │ ReadAsync()
     ↓
FakePlcDriver
     │
     │ PLC数据
     ↓
DeviceWorker
     │
     │ Update()
     ↓
DeviceRuntime


---

十二、运行起来看看

写一个 Console Demo：

var runtimeManager = new RuntimeManager();

var runtime = runtimeManager.GetOrCreate(
    10001,
    "CONVEYOR001",
    "输送机01");

var driver = new FakePlcDriver();

var worker = new DeviceWorker(
    runtime,
    driver);

using var cts = new CancellationTokenSource();

var task = worker.ExecuteAsync(
    cts.Token);

while (true)
{
    await Task.Delay(2000);

    Console.Clear();

    Console.WriteLine("========== 设备状态 ==========");

    Console.WriteLine(
        $"设备：{runtime.Name}");

    Console.WriteLine(
        $"通信：{runtime.Connection.State}");

    Console.WriteLine();

    foreach (var value in runtime.Values.GetAll())
    {
        Console.WriteLine(
            $"DataPoint={value.DataPointId}, " +
            $"Value={value.Value}, " +
            $"Quality={value.Quality}, " +
            $"Changed={value.Changed}");
    }
}

运行之后你可能看到：

========== 设备状态 ==========

设备：输送机01
通信：Connected

DataPoint=1, Value=True, Quality=Good, Changed=False
DataPoint=2, Value=1.532, Quality=Good, Changed=True
DataPoint=3, Value=8, Quality=Good, Changed=True
DataPoint=4, Value=42.81, Quality=Good, Changed=True

过两秒：

========== 设备状态 ==========

设备：输送机01
通信：Connected

DataPoint=1, Value=True, Quality=Good, Changed=False
DataPoint=2, Value=1.143, Quality=Good, Changed=True
DataPoint=3, Value=10, Quality=Good, Changed=True
DataPoint=4, Value=41.92, Quality=Good, Changed=True


---

十三、这个时候你一定要理解一个问题

数据库里面可能是：

DeviceDataValue

DeviceId    DataPointId    Value
10001       1              true
10001       2              1.143
10001       3              10
10001       4              41.92

但是：

Runtime 不是数据库。

Runtime 是：

内存

例如：

RAM

DeviceRuntime[10001]
       │
       ├── Speed = 1.143
       ├── Count = 10
       ├── Temperature = 41.92
       └── RunStatus = true

所以前端实时访问：

Vue
 ↓
ASP.NET Core
 ↓
RuntimeManager
 ↓
DeviceRuntime

根本不需要查数据库。


---

十四、那么数据库什么时候写？

例如：

PLC
 ↓
DeviceWorker
 ↓
Runtime

每秒采集。

但是不要：

每次采集
 ↓
INSERT DataHistory

否则：

10000台设备
×
100个点
×
1秒

就是：

1,000,000 次/秒

数据库直接崩。

所以后面应该增加：

Runtime
   │
   ↓
HistoryWriter
   │
   ↓
批量写数据库

例如：

Runtime
 ↓
内存队列
 ↓
1000条
 ↓
Batch Insert
 ↓
DataHistory


---

十五、Runtime 还能做一个非常重要的事情：报警

例如：

Temperature > 80℃

PLC：

Temperature = 82

进入：

DeviceRuntime
       ↓
AlarmEngine
       ↓
产生 AlarmRecord

例如：

var temperature =
    runtime.Values.Get(4);

if (temperature?.Quality == DataQuality.Good &&
    temperature.Value is double value &&
    value > 80)
{
    runtime.Status.SetAlarm(true);

    // 创建报警事件
}

注意：

AlarmEngine 不应该再去 PLC 读取温度。

它直接：

Runtime
 ↓
Temperature
 ↓
判断


---

十六、SignalR 也是这样

假设：

Speed
1.20
↓
1.30

Runtime：

Changed = true

于是：

DeviceRuntime
       ↓
RuntimeChanged Event
       ↓
SignalR
       ↓
Vue

Vue：

输送机01

状态：运行 🟢

速度：1.30 m/s
计数：1538
温度：42.3 ℃

整个过程中：

Vue 没有访问 PLC。

Vue 没有频繁查询数据库。

SignalR 也没有访问 PLC。

只有：

DeviceWorker → PLC

负责采集。


---

十七、再把你的真实项目代入进去

刚才 Demo：

FakePlcDriver

换成你的：

S7Driver

就变成：

S7-1500
192.168.1.10
      ↓
S7Driver
      ↓
DeviceWorker
      ↓
DeviceRuntime

如果以后增加 OPC UA：

OPC UA Server
      ↓
OpcUaDriver
      ↓
DeviceWorker
      ↓
DeviceRuntime

Modbus：

Modbus TCP
      ↓
ModbusDriver
      ↓
DeviceWorker
      ↓
DeviceRuntime

而：

DeviceRuntime

完全不需要知道下面到底是：

S7
OPC UA
Modbus
EtherNet/IP

这就是 IProtocolDriver 的价值。


---

十八、再对应回你的数据库

你之前设计的表：

Area
Device
Controller
DeviceConnection
DataModel
DataPoint
DataPointMapping
DeviceDataModel
DeviceDataValue
DataHistory
AlarmRecord

和 Runtime 的关系是：

数据库
                       │
              ┌────────┴────────┐
              │                 │
           Device          Mapping
              │                 │
              └────────┬────────┘
                       ↓
                 RuntimeConfig
                       ↓
                 DeviceWorker
                       ↓
                 ProtocolDriver
                       ↓
                      PLC
                       │
                       ↓
                ┌──────────────┐
                │ DeviceRuntime│
                └──────┬───────┘
                       │
          ┌────────────┼─────────────┐
          ↓            ↓             ↓
       SignalR     AlarmEngine   HistoryWriter
          ↓            ↓             ↓
         Vue       AlarmRecord    DataHistory

这里有一个非常重要的思想：

> 数据库描述“应该怎么运行”，Runtime 描述“现在正在怎么运行”。




---

十九、你可以用一个现实中的例子彻底理解

假设数据库里面：

Device

Id = 10001
Code = CONVEYOR001
Name = 输送机01

Mapping：

RunStatus → DB10.DBX0.0
Speed     → DB10.DBD4
Count     → DB10.DBD8

Worker：

每500ms读取一次

PLC：

DB10.DBX0.0 = 1
DB10.DBD4   = 125
DB10.DBD8   = 15382

转换：

RunStatus = true
Speed = 12.5
Count = 15382

然后：

DeviceRuntime
                         │
            ┌────────────┼────────────┐
            ↓            ↓            ↓
       RunStatus      Speed         Count
          true         12.5         15382

这时候前端打开：

输送机01

API：

GET /api/devices/10001/runtime

直接：

RuntimeManager
       ↓
DeviceRuntime[10001]

得到：

{
  "deviceId": 10001,
  "name": "输送机01",
  "connectionState": "Connected",
  "runState": "Running",
  "values": {
    "RunStatus": true,
    "Speed": 12.5,
    "Count": 15382
  }
}

完全不需要访问 PLC。


---

二十、最后给你一个最重要的理解方式

以后你写代码的时候，可以把整个系统想成下面这样：

┌───────────────────────────────┐
│           数据库              │
│                               │
│  “设备应该是什么样”           │
│                               │
│ Device                        │
│ Connection                    │
│ DataPoint                     │
│ Mapping                       │
└───────────────┬───────────────┘
                │
                │ 加载配置
                ↓
┌───────────────────────────────┐
│       DeviceRuntime           │
│                               │
│  “设备现在是什么样”            │
│                               │
│ Connected                     │
│ Running                       │
│ Speed = 12.5                  │
│ Count = 15382                 │
│ Temperature = 42.3            │
└───────┬──────────┬────────────┘
        │          │
        ↓          ↓
      SignalR    Alarm
        │          │
        ↓          ↓
       Vue      AlarmRecord

而 DeviceWorker 就像一个工人：

DeviceWorker：

“我每隔500ms去PLC看一下。”

“PLC说 Speed = 12.5。”

“好，我把 Speed 更新到 Runtime。”

“PLC说 Count = 15382。”

“好，我把 Count 更新到 Runtime。”

“PLC断了。”

“好，我把 Connection 标记成 Faulted。”

所以你可以牢牢记住这句话：

> Worker 负责采集，Runtime 负责保存当前状态，Alarm 负责判断，History 负责记录过去，SignalR 负责告诉前端。



这五个职责不要混在一起。


---

如果按照你现在这个项目继续往下实现，我下一步建议直接做一个可运行的 ASP.NET Core Demo，把上面的 Console Demo 改成真实项目结构：

Scada.Api
Scada.Application
 ├── Runtime
 ├── Collection
 ├── Devices
 └── Alarm
Scada.Domain
 ├── Entities
 └── Runtime
Scada.Infrastructure
 ├── EF Core
 └── Drivers
      ├── S7Driver
      └── FakeDriver

然后用 Fake PLC + EF Core + BackgroundService + RuntimeManager + SignalR + Vue 3，模拟 3 台 PLC、10 个设备、几十个 DataPoint 的完整运行过程。这样你会非常直观地看到 DeviceWorker、RuntimeManager、DeviceRuntime、DeviceScheduler 到底应该怎样协作。