namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 联动动作类型（替代原 string ActionType）
    /// </summary>
    public enum LinkageActionEnum
    {
        /// <summary>赋值（将目标变量设为 LinkageValue）</summary>
        SetValue,
        /// <summary>取反/翻转</summary>
        Toggle,
        /// <summary>执行脚本</summary>
        ExecuteScript
    }
}
