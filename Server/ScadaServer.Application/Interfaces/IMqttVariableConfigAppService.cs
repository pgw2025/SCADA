using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.Interfaces
{
    /// <summary>
    /// MQTT 变量映射服务：管理某服务器下关联变量（别名/自定义主题/启用），
    /// 同一变量可关联多台服务器且别名各自独立。
    /// </summary>
    public interface IMqttVariableConfigAppService
    {
        /// <summary>
        /// 查询指定服务器下所有关联变量（含设备名、变量名、主题预览、实时值）。
        /// </summary>
        Task<List<MqttVariableConfigDto>> GetByServerAsync(int serverId);

        /// <summary>
        /// 新增关联变量（同一服务器下同一设备同一变量唯一）。
        /// </summary>
        Task<MqttVariableConfigDto> AddAsync(int serverId, MqttVariableConfigCreateDto dto);

        /// <summary>
        /// 更新关联变量（别名/自定义主题/启用开关）。
        /// </summary>
        Task<MqttVariableConfigDto?> UpdateAsync(int configId, MqttVariableConfigUpdateDto dto);

        /// <summary>
        /// 删除关联变量。
        /// </summary>
        Task DeleteAsync(int configId);
    }
}