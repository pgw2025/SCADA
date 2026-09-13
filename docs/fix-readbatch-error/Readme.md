# P1 修复任务：解决批量读取失败后立即执行大量单变量读取导致的请求风暴

你现在需要对 SCADA 项目中的 `DeviceWorker.WorkerAsync` 进行一次**针对性 P1 修复**。

## 一、修复目标

解决当前逻辑中：

> `ReadBatchAsync(due)` 批量读取失败后，将 `batch` 设为 `null`，随后对 `due` 中每一个变量执行 `ReadAsync(vr)`，导致一次批量通信故障被放大为 N 次单变量通信请求的问题。

这个问题可能造成：

* PLC/OPC UA 通信故障期间产生大量无意义请求；
* 单次超时被放大为 N 次超时；
* Worker 故障恢复时间明显变长；
* PLC、Socket、OPC UA Session 承受额外压力；
* 多设备同时故障时形成请求风暴；
* 大量变量逐个失败导致日志刷屏；
* 延迟 `NeedsReconnect` / 重连机制生效。

**本次只修复这个问题，不要借此进行大规模重构。**

---

# 二、当前问题

当前逻辑类似：

```csharp
Dictionary<string, object>? batch = null;

try
{
    batch = await driver.ReadBatchAsync(due);
}
catch
{
    batch = null;
}
```

随后：

```csharp
foreach (var vr in due)
{
    if (batch != null &&
        batch.TryGetValue(vr.Key, out var value))
    {
        newValue = value;
    }
    else
    {
        newValue = await driver.ReadAsync(vr);
    }
}
```

这里存在严重问题：

```text
ReadBatchAsync(due)
        ↓
通信失败
        ↓
batch = null
        ↓
foreach(due)
        ↓
ReadAsync(v1)
ReadAsync(v2)
ReadAsync(v3)
...
ReadAsync(vN)
```

一次通信故障可能被放大成 N 次通信请求。

例如：

```text
100 个变量
Batch 超时 3 秒
        ↓
随后最多 100 次 Single Read
        ↓
每次又可能等待超时
```

这是必须修复的。

---

# 三、修复原则

必须遵循下面的核心原则：

## 原则 1：必须区分“通信异常”和“非通信异常”

`ReadBatchAsync()` 抛异常后，不能简单：

```csharp
catch
{
    batch = null;
}
```

必须保留异常信息，并判断异常类型。

### 通信类异常

包括但不限于：

* `TimeoutException`
* `SocketException`
* `IOException`
* PLC 连接断开
* TCP connection reset
* connection closed
* OPC UA Session 断开
* Driver 明确表示设备连接不可用的异常

如果项目已有统一的 Driver 通信异常类型，**优先使用现有类型**。

如果项目没有统一类型，可以先使用可靠的异常类型判断，不要大量依赖异常字符串。

---

# 四、通信异常时禁止 Single Read Fallback

这是本次修复最重要的要求。

如果：

```csharp
ReadBatchAsync(due)
```

抛出通信类异常：

```text
Batch Read
    ↓
通信异常
    ↓
立即停止本轮通信读取
    ↓
禁止 ReadAsync(vr)
    ↓
不要执行 N 次 Single Read
```

也就是说：

```csharp
foreach (var vr in due)
{
    await driver.ReadAsync(vr);
}
```

**不能在 Batch 通信失败后继续执行。**

---

# 五、通信异常后的正确处理方式

Batch 通信失败以后：

1. 记录一次明确的 Batch 通信失败日志；
2. 不再对本批变量逐个执行 `ReadAsync`；
3. 将本批变量视为本轮读取失败；
4. 保持现有的失败统计机制；
5. 正确递增 `ConsecutiveFailureCount`；
6. 按现有 `ReconnectAfterConsecutiveFailures` 判断是否触发重连；
7. 达到阈值后设置：

   ```csharp
   _runtime.NeedsReconnect = true;
   ```
8. 正确退出本轮或者触发当前已有的重连流程；
9. 不改变现有正常读取成功时的业务逻辑。

注意：

**不要通过伪造一个 `batch` 空字典来让后面的代码继续 Single Read。**

---

# 六、非通信异常可以允许降级，但必须受控

如果 Batch 失败的原因不是通信故障，例如：

* Batch 参数问题；
* Batch 数据解析问题；
* Batch 实现本身异常；
* 某种不支持的批量读取场景；

可以考虑继续执行：

```csharp
ReadAsync(vr)
```

作为降级方案。

但是必须满足：

```text
Batch 非通信异常
        ↓
判断变量数量
        ↓
数量合理
        ↓
允许 Single Read Fallback
```

不要无条件允许几百、几千个变量全部 Single Read。

增加一个合理的 fallback 上限，例如：

```csharp
private const int MaxIndividualFallbackVariables = 50;
```

具体数值请根据项目现有配置习惯决定。

如果：

```csharp
due.Count > MaxIndividualFallbackVariables
```

则不要执行大量 Single Read，应直接将本轮视为失败并记录日志。

---

# 七、不要吞掉 Batch 异常

禁止：

```csharp
catch
{
    batch = null;
}
```

至少改成：

```csharp
Exception? batchException = null;

try
{
    batch = await driver.ReadBatchAsync(
        due,
        cancellationToken);
}
catch (OperationCanceledException)
    when (cancellationToken.IsCancellationRequested)
{
    throw;
}
catch (Exception ex)
{
    batchException = ex;

    _logger.LogWarning(
        ex,
        "Device {DeviceKey} batch read failed. VariableCount={VariableCount}",
        _runtime.Device.Key,
        due.Count);
}
```

然后根据：

```csharp
batchException
```

判断是否属于通信异常。

---

# 八、CancellationToken 必须正确传递

如果当前 `IProtocolDriver` 已支持：

```csharp
CancellationToken
```

必须将：

```csharp
cancellationToken
```

传递给：

```csharp
ReadBatchAsync(...)
ReadAsync(...)
```

如果当前接口还不支持 CancellationToken：

可以在本次修复中做**最小范围的接口调整**，但必须同步修改所有实现和调用方，确保项目能够正常编译。

禁止只修改接口而不修改实现。

同时：

```csharp
catch (OperationCanceledException)
    when (cancellationToken.IsCancellationRequested)
{
    throw;
}
```

不要把正常的 Worker 停止误判成通信故障。

---

# 九、推荐的异常分类方式

优先检查项目中是否已经存在类似：

```csharp
DriverCommunicationException
ProtocolCommunicationException
```

之类的统一异常。

如果存在：

```csharp
catch (DriverCommunicationException ex)
{
    // 通信失败
}
```

优先使用。

如果不存在，可以暂时实现类似：

```csharp
private static bool IsCommunicationFailure(Exception ex)
{
    return ex is TimeoutException
        || ex is SocketException
        || ex is IOException
        || ex.InnerException is SocketException;
}
```

必要时结合项目已有 Driver 异常类型进行补充。

**不要简单通过：**

```csharp
ex.Message.Contains("disconnect")
```

作为主要判断依据。

如果需要新增统一异常类型，请保持改动最小，不要重构整个异常体系。

---

# 十、推荐的核心逻辑

最终逻辑应该接近：

```csharp
IDictionary<string, object>? batch = null;
Exception? batchException = null;

try
{
    batch = await driver.ReadBatchAsync(
        due,
        cancellationToken);
}
catch (OperationCanceledException)
    when (cancellationToken.IsCancellationRequested)
{
    throw;
}
catch (Exception ex)
{
    batchException = ex;

    _logger.LogWarning(
        ex,
        "Device {DeviceKey} batch read failed. VariableCount={VariableCount}",
        _runtime.Device.Key,
        due.Count);
}

if (batchException != null)
{
    if (IsCommunicationFailure(batchException))
    {
        // 关键：
        // 通信失败时禁止执行 N 次 ReadAsync。

        await HandleCommunicationFailureAsync(
            due,
            batchException,
            now,
            cancellationToken);

        continue;
    }

    // 非通信异常才允许有限度的 Single Read Fallback。

    if (due.Count > MaxIndividualFallbackVariables)
    {
        _logger.LogError(
            "Device {DeviceKey} batch read failed and " +
            "variable count {Count} exceeds individual fallback limit {Limit}. " +
            "Skip individual fallback.",
            _runtime.Device.Key,
            due.Count,
            MaxIndividualFallbackVariables);

        await HandleBatchFailureAsync(
            due,
            batchException,
            now,
            cancellationToken);

        continue;
    }
}
```

然后只有：

```text
Batch 成功
```

或者：

```text
Batch 非通信异常 + 变量数量未超过限制
```

才能进入：

```csharp
ReadAsync(vr)
```

---

# 十一、不要破坏现有的变量处理逻辑

本次修复不是重写 `WorkerAsync`。

以下逻辑原则上保持不变：

* `Subscription` 变量跳过；
* Disabled 变量跳过；
* `NextPollTime` 更新；
* `_processor.ApplyPolledAsync(...)`；
* 成功/失败计数；
* `LastCommunicationTime`；
* `ConnectionState`；
* `NeedsReconnect`；
* `ReconnectAfterConsecutiveFailures`；
* Watchdog；
* PollRoundCount；
* AverageResponseTime。

只修改：

> **Batch 失败后的异常分类、Fallback 决策以及通信失败短路行为。**

---

# 十二、特别注意“本轮失败变量”的处理

通信失败以后，不要为了避免 Single Read 而什么都不处理。

应该让这些变量正常进入本轮失败处理流程。

例如项目当前已经有：

```csharp
await _processor.ApplyPolledAsync(
    _runtime,
    vr,
    null,
    now);
```

那么可以复用现有机制。

但是不要在通信异常处理中再次执行真实的 Driver Read。

正确模型：

```text
Batch 通信失败
       ↓
不再访问 Driver
       ↓
变量本轮值 = null / failed
       ↓
交给现有处理逻辑
       ↓
更新失败状态
```

如果 `_processor.ApplyPolledAsync(..., null, ...)` 本身存在业务语义问题，请保持本次修改范围最小，并明确指出，不要顺手重构。

---

# 十三、日志要求

避免：

```text
Batch失败
Single v1失败
Single v2失败
Single v3失败
...
Single v100失败
```

通信故障情况下应该主要产生：

```text
Device xxx batch read failed.
VariableCount=100
Reason=TimeoutException
```

以及现有的设备通信失败/重连日志。

要求：

* 通信 Batch 失败只记录一次主要错误；
* 不因为禁止 Single Read 而产生 100 条重复错误日志；
* 日志必须包含 DeviceKey；
* 包含变量数量；
* 包含异常；
* 不要打印变量值等敏感/大量数据。

---

# 十四、必须检查所有 Driver 实现

修改 `IProtocolDriver` 或相关接口时，不要只修改 `DeviceWorker`。

检查项目中所有：

```csharp
IProtocolDriver
ReadBatchAsync
ReadAsync
```

的：

* 接口定义；
* S7Driver；
* OpcUaDriver；
* 其他 Driver；
* Mock/Test Driver；
* 所有调用方。

确保：

```text
接口
 ↓
实现
 ↓
调用
```

完全一致。

特别不要出现：

```text
Worker 使用 CancellationToken
但 Driver 实现没有正确传递 Token
```

这种“表面修复”。

---

# 十五、禁止事项

本次任务禁止：

1. 不要重写整个 `WorkerAsync`；
2. 不要重新设计 Runtime 架构；
3. 不要修改 Device/Variable 数据模型；
4. 不要修改数据库；
5. 不要修改 EF Core；
6. 不要修改 MQTT；
7. 不要修改 OPC UA 订阅架构；
8. 不要修改 S7 地址解析；
9. 不要引入新的第三方 NuGet 包；
10. 不要进行与本问题无关的代码格式化；
11. 不要顺手解决其他 P1/P2 问题；
12. 不要把 Batch 失败简单改成“永远不允许 Single Read”；
13. 不要使用无限并发 `Task.WhenAll` 来替代单变量读取；
14. 不要为了提高速度而同时发起大量 PLC 请求。

---

# 十六、完成后必须验证

修改完成后执行：

```bash
dotnet build
```

如果项目存在测试：

```bash
dotnet test
```

至少验证以下场景：

### 场景 1：Batch 成功

```text
Batch成功
→ 不执行额外 Single Read
→ 正常处理变量
```

### 场景 2：Batch Timeout

```text
Batch Timeout
→ 不执行任何 Single Read
→ 记录通信失败
→ ConsecutiveFailureCount 正确增加
→ 按现有阈值触发重连
```

### 场景 3：Batch SocketException

```text
Batch SocketException
→ 不执行任何 Single Read
→ 进入通信失败处理
```

### 场景 4：Batch 非通信异常

```text
Batch业务/参数异常
→ 变量数量 <= fallback 上限
→ 可以 Single Read Fallback
```

### 场景 5：Batch 非通信异常 + 大量变量

```text
Batch异常
→ due.Count > fallback 上限
→ 不执行几百/几千次 Single Read
→ 本轮失败
```

### 场景 6：Worker 正常停止

```text
CancellationToken 触发
→ OperationCanceledException
→ 不被计入通信失败
→ 正常退出 Worker
```

---

# 十七、最终输出要求

修改完成后，请不要只告诉我“已经修复”。

请明确输出：

### 1. 修改了哪些文件

例如：

```text
DeviceWorker.cs
IProtocolDriver.cs
S7Driver.cs
OpcUaDriver.cs
```

### 2. 每个文件修改了什么

### 3. Batch 通信异常时是否保证 0 次 Single Read

必须明确回答：

```text
是 / 否
```

### 4. 哪些异常被判定为通信异常

列出实际代码中的判断。

### 5. Single Read Fallback 的最大数量是多少

例如：

```text
MaxIndividualFallbackVariables = 50
```

### 6. 是否修改了 Driver 接口

如果修改，列出所有实现是否同步修改。

### 7. 编译结果

提供：

```text
dotnet build
```

的最终结果。

### 8. 测试结果

如果有测试，提供：

```text
dotnet test
```

结果。

---

## 最终验收标准

本次修复最核心的验收标准只有一个：

> **当 `ReadBatchAsync()` 因 PLC、TCP、Socket、OPC UA Session 等通信原因失败时，本轮绝对不能继续对 `due` 中的每个变量调用 `ReadAsync()`。**

也就是说：

```text
Batch Communication Failure
        ↓
NO N × Single Read
        ↓
Failure State
        ↓
Reconnect Logic
```

必须成立。

如果修改过程中发现当前项目的 Driver 接口、异常体系或 Worker 状态管理无法安全支持上述方案，**先停止大规模修改，指出具体冲突位置和原因，不要自行进行架构级重构。**
