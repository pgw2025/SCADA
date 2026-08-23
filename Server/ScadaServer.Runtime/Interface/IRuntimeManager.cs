namespace ScadaServer.Runtime.Interface;

/// <summary>
/// 运行时管理器接口
/// </summary>
public interface IRuntimeManager
{
    /// <summary>
    /// 初始化运行时
    /// </summary>
    Task InitializeAsync();
}