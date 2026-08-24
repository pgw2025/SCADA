using System.Threading;

namespace ScadaServer.WebApi.HostedServices;

/// <summary>
/// 数据库初始化就绪协调服务（单例）。
/// <para>
/// 用于解决 <see cref="RuntimeHostedService"/> 与 <see cref="StartupHostedService"/>
/// 之间的启动竞态：Runtime 在查询数据库前必须等待数据库迁移与种子数据完成。
/// </para>
/// <para>
/// 设计要点：
/// 1. 不使用 <see cref="Thread.Sleep"/> 轮询，而是基于 <see cref="TaskCompletionSource"/> 阻塞等待。
/// 2. 即使等待方已取消（应用关闭），也不会抛二次异常——取消时直接返回 false，由调用方决定如何降级。
/// 3. 提供 <see cref="MarkFailed"/> 以支持数据库初始化失败时让 Runtime 立即得知并跳过启动，
///    避免 Runtime 在残缺数据库上盲目重试。
/// </para>
/// </summary>
public sealed class DatabaseInitializationStatus
{
    private readonly TaskCompletionSource<InitializationResult> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly object _stateLock = new();
    private InitializationState _state = InitializationState.Pending;

    /// <summary>
    /// 数据库初始化是否已完成（成功或失败）。用于快速判断，避免重复 await。
    /// </summary>
    public bool IsCompleted
    {
        get
        {
            lock (_stateLock)
            {
                return _state is InitializationState.Succeeded or InitializationState.Failed;
            }
        }
    }

    /// <summary>
    /// 标记数据库初始化成功。幂等：多次调用只有第一次生效。
    /// </summary>
    public void MarkSucceeded()
    {
        lock (_stateLock)
        {
            if (_state != InitializationState.Pending)
                return;
            _state = InitializationState.Succeeded;
        }

        _tcs.TrySetResult(InitializationResult.Success);
    }

    /// <summary>
    /// 标记数据库初始化失败。Runtime 据此跳过启动并避免盲目重试。
    /// 幂等：多次调用只有第一次生效。
    /// </summary>
    /// <param name="error">失败原因，用于日志诊断。</param>
    public void MarkFailed(Exception? error)
    {
        lock (_stateLock)
        {
            if (_state != InitializationState.Pending)
                return;
            _state = InitializationState.Failed;
        }

        _tcs.TrySetResult(InitializationResult.Failure(error));
    }

    /// <summary>
    /// 异步等待数据库初始化完成。
    /// <para>
    /// 不抛异常：取消或初始化失败时返回 <see cref="InitializationResult"/>，由调用方决策。
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">应用关闭令牌；取消时立即返回失败结果，不抛异常。</param>
    public async Task<InitializationResult> WaitAsync(CancellationToken cancellationToken)
    {
        // 已直接完成则无需 await。
        if (IsCompleted)
            return await _tcs.Task.ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var completedTask = await Task.WhenAny(_tcs.Task, Task.Delay(Timeout.Infinite, cts.Token))
            .ConfigureAwait(false);

        if (completedTask != _tcs.Task)
        {
            // 链接令牌已取消（应用关闭）：返回取消结果，不抛 OperationCanceledException。
            return InitializationResult.Cancelled();
        }

        return await _tcs.Task.ConfigureAwait(false);
    }
}

/// <summary>
/// 数据库初始化结果。失败时携带异常供上层诊断；不通过异常传递，避免二次异常。
/// </summary>
public sealed class InitializationResult
{
    public bool Succeeded { get; init; }
    public bool IsCancelled { get; init; }
    public Exception? Error { get;  init; }

    public static readonly InitializationResult Success = new() { Succeeded = true };

    public static InitializationResult Failure(Exception? error) => new() { Succeeded = false, Error = error };

    public static InitializationResult Cancelled() => new() { Succeeded = false, IsCancelled = true };
}

internal enum InitializationState
{
    Pending,
    Succeeded,
    Failed
}
