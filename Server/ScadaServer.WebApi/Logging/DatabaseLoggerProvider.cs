using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScadaServer.Application.Options;
using ScadaServer.WebApi.HostedServices;

namespace ScadaServer.WebApi.Logging
{
    /// <summary>
    /// 将 ILogger 运行日志写入数据库的 Logger Provider。
    /// <para>
    /// 以单例方式注册（services.AddSingleton&lt;ILoggerProvider&gt;），由 LoggerFactory 延迟解析。
    /// 为避免 DI 构造期循环依赖（LoggerFactory → Provider → SystemLogRecorder → ILogger → LoggerFactory），
    /// 不在构造函数直接注入 <see cref="SystemLogRecorder"/>，而是注入 IServiceProvider，
    /// 在首次 WriteLog 时再惰性解析并缓存（Lazy 保证线程安全）。
    /// </para>
    /// <para>
    /// 过滤规则（可配置）：低于 MinLevel 丢弃；类别命中前缀/全名黑名单丢弃（含防递归）；
    /// 内容按 MaxContentLength 截断；通过后交给 SystemLogRecorder.RecordRuntime 异步落库。
    /// </para>
    /// </summary>
    public class DatabaseLoggerProvider : ILoggerProvider
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly SystemLogOptions _options;
        private readonly Lazy<SystemLogRecorder> _recorder;

        public DatabaseLoggerProvider(IServiceProvider serviceProvider, IOptions<SystemLogOptions> options)
        {
            _serviceProvider = serviceProvider;
            _options = options.Value;
            _recorder = new Lazy<SystemLogRecorder>(() => _serviceProvider.GetRequiredService<SystemLogRecorder>());
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new DatabaseLogger(categoryName, this);
        }

        /// <summary>
        /// 供 DatabaseLogger 回调：按过滤规则决定是否落库。
        /// </summary>
        internal void WriteLog(LogLevel logLevel, string categoryName, string message)
        {
            if (!ShouldRecord(categoryName, logLevel))
                return;

            var level = logLevel.ToString();
            var source = categoryName;
            var content = message;

            if (content.Length > _options.MaxContentLength)
            {
                content = content[.._options.MaxContentLength];
            }

            _recorder.Value.RecordRuntime(level, source, content);
        }

        private bool ShouldRecord(string categoryName, LogLevel logLevel)
        {
            if (logLevel < ParseLevel(_options.MinLevel))
                return false;

            // 前缀黑名单（高频框架噪音等）
            foreach (var prefix in _options.IgnoreCategories)
            {
                if (!string.IsNullOrWhiteSpace(prefix) && categoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // 全名黑名单（防递归：Recorder/Provider 自身日志）
            foreach (var exact in _options.IgnoreExactCategories)
            {
                if (string.Equals(categoryName, exact, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private static LogLevel ParseLevel(string value)
        {
            return Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level)
                ? level
                : LogLevel.Information;
        }

        public void Dispose()
        {
        }

        /// <summary>
        /// 自定义 ILogger 实现：标准 formatter 输出消息，交由 Provider 统一过滤落库。
        /// </summary>
        private sealed class DatabaseLogger : ILogger
        {
            private readonly string _categoryName;
            private readonly DatabaseLoggerProvider _provider;

            public DatabaseLogger(string categoryName, DatabaseLoggerProvider provider)
            {
                _categoryName = categoryName;
                _provider = provider;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;

                var message = formatter(state, exception);
                _provider.WriteLog(logLevel, _categoryName, message);
            }
        }
    }
}
