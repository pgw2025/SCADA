namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 变量类型枚举
    /// </summary>
    public enum VariableType
    {
        /// <summary>
        /// 模拟量（浮点数）
        /// </summary>
        Analog,

        /// <summary>
        /// 数字量（布尔值）
        /// </summary>
        Digital
    }

    /// <summary>
    /// 数据类型枚举
    /// </summary>
    public enum DataTypeEnum
    {
        /// <summary>
        /// 16位整数
        /// </summary>
        INT,

        /// <summary>
        /// 32位浮点数
        /// </summary>
        REAL,

        /// <summary>
        /// 布尔值
        /// </summary>
        BOOL,

        /// <summary>
        /// 32位整数
        /// </summary>
        DINT,

        /// <summary>
        /// 8位字节
        /// </summary>
        BYTE,

        /// <summary>
        /// 位（bit）
        /// </summary>
        BIT
    }
}
