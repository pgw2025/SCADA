using System.Text.Json;
using ScadaServer.Application.Converters;
using Xunit;

namespace ScadaServer.Infrastructure.Tests
{
    /// <summary>
    /// NullableNumericJsonConverterFactory 测试：前端清空数字输入提交空串（""）时，
    /// 可空数值属性应归一化为 null（而非反序列化 400），合法数字串/数字令牌正常解析。
    /// </summary>
    public class NullableNumericJsonConverterTests
    {
        private static readonly JsonSerializerOptions Options = BuildOptions();

        [Theory]
        [InlineData("\"\"", null)]       // int?  空串 → null（前端清空 v-model.number 的典型产物）
        [InlineData("\"   \"", null)]    // int?  空白串 → null
        [InlineData("\"123\"", 123)]     // int?  数字串 → 123（NumberRange 校验通过）
        [InlineData("123", 123)]         // int?  数字令牌 → 123
        [InlineData("null", null)]       // int?  null 令牌 → null
        public void Read_IntNullable(string value, int? expected)
        {
            var dto = Deserialize<Payload>("pollingIntervalMs", value);
            Assert.Equal(expected, dto.PollingIntervalMs);
        }

        [Theory]
        [InlineData("\"\"", null)]       // double?  空串 → null
        [InlineData("\"0.1\"", 0.1)]     // double?  数字串 → 0.1
        [InlineData("0.1", 0.1)]         // double?  数字令牌 → 0.1
        [InlineData("null", null)]       // double?  null → null
        public void Read_DoubleNullable(string value, double? expected)
        {
            var dto = Deserialize<Payload>("deadBandOverride", value);
            Assert.Equal(expected, dto.DeadBandOverride);
        }

        [Fact]
        public void Write_HasValue_SerializesAsNumber()
        {
            var dto = new Payload { DeadBandOverride = 0.5 };
            var json = JsonSerializer.Serialize(dto, Options);
            Assert.Contains("\"deadBandOverride\":0.5", json);
        }

        [Fact]
        public void Write_Null_SerializesAsNull()
        {
            var dto = new Payload { PollingIntervalMs = null };
            var json = JsonSerializer.Serialize(dto, Options);
            Assert.Contains("\"pollingIntervalMs\":null", json);
        }

        private static JsonSerializerOptions BuildOptions()
        {
            var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            o.Converters.Add(new NullableNumericJsonConverterFactory());
            return o;
        }

        private static T Deserialize<T>(string prop, string value)
            => JsonSerializer.Deserialize<T>($"{{\"{prop}\":{value}}}", Options)!;

        private class Payload
        {
            public int? PollingIntervalMs { get; set; }
            public double? DeadBandOverride { get; set; }
        }
    }
}