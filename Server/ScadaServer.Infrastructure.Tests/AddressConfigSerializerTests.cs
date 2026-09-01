using ScadaServer.Domain.Addresses;
using Xunit;

namespace ScadaServer.Infrastructure.Tests
{
    /// <summary>
    /// AddressConfigSerializer 测试：JSON 序列化往返、展示串生成（与 S7Driver 地址语法对齐）、
    /// 历史展示串回填解析，以及非法输入安全返回（不抛异常）。
    /// </summary>
    public class AddressConfigSerializerTests
    {
        [Theory]
        [InlineData("DB", 1, 10, "BYTE", -1, "DB1.DBB10")]  // DB 字节
        [InlineData("DB", 2, 20, "WORD", -1, "DB2.DBW20")]  // DB 字
        [InlineData("DB", 1, 30, "DWORD", -1, "DB1.DBD30")] // DB 双字
        [InlineData("DB", 1, 0, "BIT", 0, "DB1.DBX0.0")]    // DB 位
        [InlineData("I", 0, 5, "BYTE", -1, "IB5")]           // 输入字节
        [InlineData("I", 0, 2, "BIT", 3, "I2.3")]            // 输入位
        [InlineData("Q", 0, 8, "WORD", -1, "QW8")]           // 输出字
        [InlineData("M", 0, 40, "BIT", 7, "M40.7")]          // 存储位
        public void ToDisplay_S7_MatchesDriverSyntax(string area, int db, int offset, string width, int bit, string expected)
        {
            var cfg = new AddressConfig { Protocol = "S7", Area = area, DbNumber = db, ByteOffset = offset, Width = width, BitOffset = bit };
            Assert.Equal(expected, AddressConfigSerializer.ToDisplay(cfg));
        }

        [Theory]
        [InlineData("DB1.DBB10", "DB", 1, 10, "BYTE", -1)]
        [InlineData("DB2.DBW20", "DB", 2, 20, "WORD", -1)]
        [InlineData("DB1.DBD30", "DB", 1, 30, "DWORD", -1)]
        [InlineData("DB1.DBX0.0", "DB", 1, 0, "BIT", 0)]
        [InlineData("IB5", "I", 0, 5, "BYTE", -1)]
        [InlineData("I2.3", "I", 0, 2, "BIT", 3)]
        [InlineData("QW8", "Q", 0, 8, "WORD", -1)]
        [InlineData("M40.7", "M", 0, 40, "BIT", 7)]
        public void BuildFromDisplay_S7_RoundTrips(string display, string area, int db, int offset, string width, int bit)
        {
            var cfg = AddressConfigSerializer.BuildFromDisplay(display, "S7");
            Assert.NotNull(cfg);
            Assert.Equal("S7", cfg!.Protocol);
            Assert.Equal(area, cfg.Area);
            Assert.Equal(db, cfg.DbNumber);
            Assert.Equal(offset, cfg.ByteOffset);
            Assert.Equal(width, cfg.Width);
            Assert.Equal(bit, cfg.BitOffset);
        }

        [Fact]
        public void Serialize_Deserialize_RoundTrips()
        {
            var cfg = new AddressConfig { Protocol = "S7", Area = "DB", DbNumber = 1, ByteOffset = 100, Width = "DWORD", BitOffset = -1 };
            var json = AddressConfigSerializer.Serialize(cfg);
            Assert.False(string.IsNullOrWhiteSpace(json));

            var back = AddressConfigSerializer.Deserialize(json!);
            Assert.NotNull(back);
            Assert.Equal("S7", back!.Protocol);
            Assert.Equal("DB", back.Area);
            Assert.Equal(1, back.DbNumber);
            Assert.Equal(100, back.ByteOffset);
            Assert.Equal("DWORD", back.Width);
        }

        [Theory]
        [InlineData("DB0.DBW10")]  // DB 号 < 1
        [InlineData("MW10.9")]       // 位偏移越界
        [InlineData("DB1.MW10")]     // 区域/前缀误配
        [InlineData("abc")]          // 乱串
        [InlineData("")]             // 空
        [InlineData(null)]
        public void BuildFromDisplay_S7_Invalid_ReturnsNull(string? display)
        {
            Assert.Null(AddressConfigSerializer.BuildFromDisplay(display, "S7"));
        }

        [Fact]
        public void ToDisplay_InvalidConfig_ReturnsNull()
        {
            // 未知协议 / 结构不合法时应返回 null 而不抛异常
            Assert.Null(AddressConfigSerializer.ToDisplay(new AddressConfig { Protocol = "UNKNOWN" }));
            Assert.Null(AddressConfigSerializer.ToDisplay(null));
        }

        [Fact]
        public void Serialize_Null_ReturnsNull()
        {
            Assert.Null(AddressConfigSerializer.Serialize(null));
        }
    }
}