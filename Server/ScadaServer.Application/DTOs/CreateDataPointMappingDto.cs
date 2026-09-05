using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ScadaServer.Domain.Enums;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 创建设备变量实例 DTO。
    /// 按"设备 + 变量模板"创建一条 DataPointMapping 实例，地址 / 位偏移 / 采集周期默认从模板回退，
    /// 其后的采集细节（地址、轮询等）可在设备实例层单独覆盖。
    /// </summary>
    public class CreateDataPointMappingDto
    {
        /// <summary>目标设备ID。</summary>
        [Required(ErrorMessage = "设备ID不能为空")]
        public int DeviceId { get; set; }

        /// <summary>变量模板ID（必须隶属于该设备所绑定的数据模型）。</summary>
        [Required(ErrorMessage = "变量模板ID不能为空")]
        public int DataPointId { get; set; }

        /// <summary>是否启用该设备实例变量，默认 true。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 变量更新方式（Polling=自主轮询 / Subscription=订阅推送）。Subscription 仅限支持订阅的协议（OPC UA）。
        /// <para>字符串序列化（"Polling"/"Subscription"）；前端不传该字段创建变量默认轮询。</para>
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public UpdateModeEnum UpdateMode { get; set; } = UpdateModeEnum.Polling;
    }
}