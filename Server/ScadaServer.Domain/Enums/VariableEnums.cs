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
    BIT,

    /// <summary>
    /// 单精度浮点数（对应前端 "Float"）
    /// </summary>
    FLOAT,

    /// <summary>
    /// 双精度浮点数（对应前端 "Double"）
    /// </summary>
    DOUBLE,

    /// <summary>
    /// 变长字符串（对应前端 "String"）
    /// </summary>
    STRING,

    /// <summary>
    /// 16位无符号整型（对应前端 "UInt16"）
    /// </summary>
    UINT16,

    /// <summary>
    /// 32位无符号整型（对应前端 "UInt32"）
    /// </summary>
    UINT32,

    /// <summary>
    /// 64位有符号整型（对应前端 "Int64"）
    /// </summary>
    INT64,

    /// <summary>
    /// 64位无符号整型（对应前端 "UInt64"）
    /// </summary>
    UINT64,

    /// <summary>
    /// 16位无符号字类型（对应前端 "Word"）
    /// </summary>
    WORD,

    /// <summary>
    /// 单字符字段（对应前端 "Char"）
    /// </summary>
    CHAR
}
}
