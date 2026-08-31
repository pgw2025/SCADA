using Microsoft.Extensions.Logging.Abstractions;
using ScadaServer.Infrastructure.Communication;
using S7.Net;
using Xunit;

namespace ScadaServer.Infrastructure.Tests
{
    /// <summary>
    /// S7Driver.ParseAddress 地址解析健壮性测试：
    /// 合法地址 → 正确的 S7AddressInfo；任意非法地址（含超大数字溢出、非法 Bit、错误前缀、null/空白/乱串）→ null 且不抛异常。
    /// </summary>
    public class S7DriverAddressParsingTests
    {
        private static S7Driver CreateDriver() => new(NullLogger<S7Driver>.Instance);

        #region 合法地址 → 正确解析

        [Theory]
        [InlineData("DB1.DBX0.0", 1, 0, 0, 1)]   // DB 位地址
        [InlineData("DB1.DBX0.7", 1, 0, 7, 1)]   // DB 位地址（bit=7 边界）
        [InlineData("DB2.DBB10", 2, 10, 0, 1)]   // DB 字节
        [InlineData("DB1.DBW10", 1, 10, 0, 2)]   // DB 字
        [InlineData("DB1.DBD20", 1, 20, 0, 4)]   // DB 双字
        [InlineData("DB1.DBR30", 1, 30, 0, 4)]   // DB REAL 写法（宽度 4）
        public void ParseAddress_ValidDbAddress_ReturnsInfo(string address, int expectedDb, int expectedOffset, int expectedBit, int expectedLength)
        {
            var info = CreateDriver().ParseAddress(address);

            Assert.NotNull(info);
            Assert.Equal(DataType.DataBlock, info.S7Area);
            Assert.Equal(expectedDb, info.DbNumber);
            Assert.Equal(expectedOffset, info.ByteOffset);
            Assert.Equal(expectedBit, info.BitOffset);
            Assert.Equal(expectedLength, info.ByteLength);
            // 位地址（DBX）HasBit=true；字节/字/双字地址 HasBit=false
            Assert.Equal(address.Contains("DBX"), info.HasBit);
        }

        [Theory]
        [InlineData("I0.0", 0, 1)]
        [InlineData("IB0", 0, 1)]
        [InlineData("IW2", 2, 2)]
        [InlineData("ID4", 4, 4)]
        public void ParseAddress_ValidInputAddress_ReturnsInfo(string address, int expectedOffset, int expectedLength)
        {
            var info = CreateDriver().ParseAddress(address);

            Assert.NotNull(info);
            Assert.Equal(DataType.Input, info.S7Area);
            Assert.Equal(0, info.DbNumber);
            Assert.Equal(expectedOffset, info.ByteOffset);
            Assert.Equal(expectedLength, info.ByteLength);
            Assert.Equal(address.StartsWith("I0."), info.HasBit); // 仅 "I0.0" 为位地址
        }

        [Theory]
        [InlineData("Q0.0", 0, 1)]
        [InlineData("QB0", 0, 1)]
        [InlineData("QW0", 0, 2)]
        [InlineData("QD0", 0, 4)]
        public void ParseAddress_ValidOutputAddress_ReturnsInfo(string address, int expectedOffset, int expectedLength)
        {
            var info = CreateDriver().ParseAddress(address);

            Assert.NotNull(info);
            Assert.Equal(DataType.Output, info.S7Area);
            Assert.Equal(0, info.DbNumber);
            Assert.Equal(expectedOffset, info.ByteOffset);
            Assert.Equal(expectedLength, info.ByteLength);
            Assert.Equal(address.StartsWith("Q0."), info.HasBit);
        }

        [Theory]
        [InlineData("M0.0", 0, 1)]
        [InlineData("MB0", 0, 1)]
        [InlineData("MW10", 10, 2)]
        [InlineData("MD20", 20, 4)]
        public void ParseAddress_ValidMemoryAddress_ReturnsInfo(string address, int expectedOffset, int expectedLength)
        {
            var info = CreateDriver().ParseAddress(address);

            Assert.NotNull(info);
            Assert.Equal(DataType.Memory, info.S7Area);
            Assert.Equal(0, info.DbNumber);
            Assert.Equal(expectedOffset, info.ByteOffset);
            Assert.Equal(expectedLength, info.ByteLength);
            Assert.Equal(address.StartsWith("M0."), info.HasBit);
        }

        [Fact]
        public void ParseAddress_IsCaseInsensitive()
        {
            // 大小写不敏感（历史兼容行为）
            Assert.NotNull(CreateDriver().ParseAddress("db1.dbx0.1"));
            Assert.NotNull(CreateDriver().ParseAddress("Mw10"));
        }

        [Fact]
        public void ParseAddress_TrimsSurroundingWhitespace()
        {
            var info = CreateDriver().ParseAddress("  DB1.DBW10  ");

            Assert.NotNull(info);
            Assert.Equal(10, info.ByteOffset);
        }

        #endregion

        #region 非法地址 → null（不抛异常）

        [Theory]
        [InlineData(null)]                              // null
        [InlineData("")]                                // 空字符串
        [InlineData("   ")]                             // 空格
        [InlineData("hello world")]                     // 随机字符串
        [InlineData("DB1 DBW10")]                       // 格式错误
        [InlineData("DB0.DBW0")]                        // DB0 保留不可用
        [InlineData("DBW0")]                            // 缺少 DB 前缀
        [InlineData("DB1.MW0")]                         // 非 DB 区域误配 DB 前缀
        [InlineData("DB1.DBW0.1")]                      // 非位类型携带 bit 后缀
        [InlineData("DB1.DBX0.8")]                      // Bit 超出 0~7
        [InlineData("DB1.DBX0.9")]                      // Bit 超出 0~7
        [InlineData("DB1.DBX0.12")]                     // 多位数字 Bit 超界
        [InlineData("M0")]                              // 位类型缺失 bit 后缀
        [InlineData("I0")]                              // 位类型缺失 bit 后缀
        [InlineData("Q0")]                              // 位类型缺失 bit 后缀
        [InlineData("M0.8")]                            // 非 DB 位地址 Bit 超界
        [InlineData("DB999999999999999999999.DBW0")]    // DB 号超大数字（原 OverflowException 场景）
        [InlineData("M999999999999999999999")]          // 偏移超大数字（原 OverflowException 场景）
        [InlineData("DB1.DBW999999999999999999999")]    // DB 内偏移超大数字
        [InlineData("DB1.DBW-10")]                      // 负数偏移
        [InlineData("2147483648.ID0")]                  // 偏移恰超 int.MaxValue
        [InlineData("DB.")]                             // 残缺地址
        [InlineData("I0.0.1")]                          // 多余段
        public void ParseAddress_InvalidAddress_ReturnsNullWithoutThrowing(string? address)
        {
            // 任意非法输入只允许返回 null，绝不允许抛出异常
            // （不依赖 ReadAsync/ReadBatchAsync 外层 catch 兜底）
            var info = CreateDriver().ParseAddress(address!);
            Assert.Null(info);
        }

        [Fact]
        public void ParseAddress_AllInvalidInputs_NeverThrows()
        {
            // 聚合防御性验证：一批典型非法输入全部安全返回 null
            string[] inputs =
            {
                "DB0.DBW0", "DBW0", "DB1.MW0", "DB1.DBW0.1", "DB1.DBX0.8", "M0", "I0", "Q0", "M0.8",
                "DB999999999999999999999.DBW0", "M999999999999999999999", "", "   ", "xyz", "DB1..DBW0"
            };

            var driver = CreateDriver();
            foreach (var input in inputs)
            {
                Assert.Null(driver.ParseAddress(input));
            }
        }

        #endregion
    }
}
