using System.Text;
using Jint;
using Jint.Native;
using Jint.Native.Object;

namespace ScadaServer.Runtime.Scripting
{
    /// <summary>
    /// JS 受限沙箱（基于 Jint）：仅向脚本暴露白名单 API（read/write/getQuality/log），
    /// 支持超时、递归深度限制与严格模式；一条 sandbox 实例对应一段已解析的脚本代码。
    /// <para>Jint 无宿主进程/网络/文件能力，注入面仅为我们声明的桥接函数，满足“受限子集”约束。</para>
    /// <para>读写授权：read/getQuality 需设备在 ScopeRead；write 需 "设备键.变量键" 在 ScopeWrite；空授权 = 拒绝全部。</para>
    /// </summary>
    public sealed class ScriptSandbox
    {
        private readonly Engine _engine;
        private readonly StringBuilder _output = new();
        private readonly ScriptRuntimeAccess _access;
        private readonly bool _dryRun;
        private readonly string? _scopeRead;
        private readonly string? _scopeWrite;

        /// <summary>
        /// 触发时传入 onChange 的事件对象；为空表示本次是 run 钩子（非 OnChange）。
        /// </summary>
        public class TriggerPayload
        {
            public string DeviceKey { get; set; } = string.Empty;
            public string VariableKey { get; set; } = string.Empty;
            public object? Value { get; set; }
            public object? PreviousValue { get; set; }
            public string Quality { get; set; } = "Good";
        }

        /// <summary>
        /// 解析并准备脚本。语法错误会在此抛出。
        /// <para><paramref name="scopeRead"/>/<paramref name="scopeWrite"/> 为授权串（';' 分隔），空 = 默认拒绝全部。</para>
        /// </summary>
        public ScriptSandbox(string code, int timeoutMs, ScriptRuntimeAccess access, bool dryRun,
            string? scopeRead, string? scopeWrite)
        {
            _access = access;
            _dryRun = dryRun;
            _scopeRead = scopeRead;
            _scopeWrite = scopeWrite;

            _engine = new Engine(opts =>
            {
                opts.LimitRecursion(100);
                opts.TimeoutInterval(TimeSpan.FromMilliseconds(timeoutMs));
                opts.Strict(true);
            });

            RegisterApi();
            _engine.Execute(code);
        }

        private void RegisterApi()
        {
            // log(...)：变长参数统一 append 到输出缓冲。
            _engine.SetValue("log", new Action<JsValue[]>(args =>
            {
                var parts = args.Select(FormatValue);
                _output.AppendLine(string.Join(" ", parts));
            }));

            _engine.SetValue("read", new Func<string, string, object?>((devKey, varKey) =>
            {
                if (!ScriptRuntimeAccess.IsReadAllowed(_scopeRead, devKey))
                {
                    _output.AppendLine($"[DENIED] read {devKey}.{varKey}：设备 [{devKey}] 不在读授权列表");
                    return null;
                }
                return _access.Read(devKey, varKey);
            }));

            _engine.SetValue("getQuality", new Func<string, string, string>((devKey, varKey) =>
            {
                if (!ScriptRuntimeAccess.IsReadAllowed(_scopeRead, devKey))
                {
                    _output.AppendLine($"[DENIED] getQuality {devKey}.{varKey}：设备 [{devKey}] 不在读授权列表");
                    return "Unknown";
                }
                return _access.GetQuality(devKey, varKey);
            }));

            // write： dry-run（试运行）时只记录“将要写入”，不真实改运行时，避免试运行副作用。
            _engine.SetValue("write", new Func<string, string, JsValue, bool>((devKey, varKey, value) =>
            {
                if (!ScriptRuntimeAccess.IsWriteAllowed(_scopeWrite, devKey, varKey))
                {
                    _output.AppendLine($"[DENIED] write {devKey}.{varKey}：不在写授权列表（需精确到 设备键.变量键）");
                    return false;
                }
                var raw = Unwrap(value);
                if (_dryRun)
                {
                    _output.AppendLine($"[DRY-RUN] 写入 {devKey}.{varKey} = {raw}");
                    return true;
                }

                if (!_access.Write(devKey, varKey, raw!, out var err))
                {
                    _output.AppendLine($"[WRITE-FAIL] {devKey}.{varKey}: {err}");
                    return false;
                }
                return true;
            }));
        }

        /// <summary>
        /// 执行 run 钩子（手动/周期/Cron 触发）。返回脚本输出文本。
        /// </summary>
        public string Run()
        {
            _output.Clear();
            if (!HasFunction("run"))
            {
                _output.AppendLine("[SKIP] 未声明 run() 钩子");
                return _output.ToString();
            }
            _engine.Invoke("run");
            return _output.ToString();
        }

        /// <summary>
        /// 执行 onChange 钩子（变量变化触发）。返回脚本输出文本。
        /// </summary>
        public string OnChange(TriggerPayload payload)
        {
            _output.Clear();
            if (!HasFunction("onChange"))
            {
                _output.AppendLine("[SKIP] 未声明 onChange(ev) 钩子");
                return _output.ToString();
            }

            var ev = new JsObject(_engine);
            ev.Set("deviceKey", JsValue.FromObject(_engine, payload.DeviceKey));
            ev.Set("variableKey", JsValue.FromObject(_engine, payload.VariableKey));
            ev.Set("value", payload.Value == null ? JsValue.Null : JsValue.FromObject(_engine, payload.Value));
            ev.Set("previous", payload.PreviousValue == null ? JsValue.Null : JsValue.FromObject(_engine, payload.PreviousValue));
            ev.Set("quality", JsValue.FromObject(_engine, payload.Quality));

            _engine.Invoke("onChange", ev);
            return _output.ToString();
        }

        /// <summary>
        /// 判断脚本是否声明了指定顶层函数。
        /// </summary>
        private bool HasFunction(string name) => !_engine.GetValue(name).IsUndefined();

        private static string FormatValue(JsValue v) => v.IsUndefined() ? "undefined" : (v.ToString() ?? string.Empty);

        private object? Unwrap(JsValue v)
        {
            if (v.IsNull() || v.IsUndefined())
            {
                return null;
            }
            if (v.IsNumber())
            {
                return v.AsNumber();
            }
            if (v.IsBoolean())
            {
                return v.AsBoolean();
            }
            if (v.IsString())
            {
                return v.AsString();
            }
            return v.ToObject();
        }
    }
}