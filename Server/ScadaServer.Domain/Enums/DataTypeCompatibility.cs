namespace ScadaServer.Domain.Enums
{
    /// <summary>
    /// 数据类型兼容判定（数据转换规则源/目标变量类型校验共用，前后端矩阵需保持一致）。
    /// <para>
    /// 规则：同一类型恒兼容；数值类（整型/浮点/字/字节）任意互转允许（驱动按目标类型转换，数值语义保持）；
    /// 布尔类（BOOL/BIT）互转允许；文本类（STRING/CHAR）仅同类型允许（防截断）；跨大类一律拒绝
    /// （数值↔布尔语义混叠、数值/布尔↔文本驱动字节语义不同）。
    /// </para>
    /// </summary>
    public static class DataTypeCompatibility
    {
        /// <summary>判定源类型 → 目标类型是否可建立数据转换。</summary>
        public static bool IsCompatible(DataTypeEnum source, DataTypeEnum target)
        {
            if (source == target)
            {
                return true;
            }
            return (IsNumeric(source) && IsNumeric(target))
                || (IsBool(source) && IsBool(target));
        }

        /// <summary>大类中文描述（校验错误文案用）。</summary>
        public static string DescribeCategory(DataTypeEnum type) =>
            IsNumeric(type) ? "数值类" : IsBool(type) ? "布尔类" : "文本类";

        private static bool IsNumeric(DataTypeEnum t) => t is DataTypeEnum.INT
            or DataTypeEnum.DINT or DataTypeEnum.REAL or DataTypeEnum.FLOAT or DataTypeEnum.DOUBLE
            or DataTypeEnum.UINT16 or DataTypeEnum.UINT32 or DataTypeEnum.INT64 or DataTypeEnum.UINT64
            or DataTypeEnum.WORD or DataTypeEnum.BYTE;

        private static bool IsBool(DataTypeEnum t) => t is DataTypeEnum.BOOL or DataTypeEnum.BIT;
    }
}
