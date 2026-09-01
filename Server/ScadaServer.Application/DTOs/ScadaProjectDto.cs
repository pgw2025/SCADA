namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 组态工程 DTO（描述一个可视化组态工程的基本信息）。
    /// </summary>
    public class ScadaProjectDto
    {
        /// <summary>工程ID（主键，创建时由服务端生成）</summary>
        public int Id { get; set; }

        /// <summary>工程名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>工程描述（可空）</summary>
        public string? Description { get; set; }

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; }
    }
}
