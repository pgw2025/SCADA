namespace ScadaServer.Application.DTOs
{
    public class ScadaPageDto
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public string Name { get; set; }
        public bool IsHome { get; set; }

        /// <summary>
        /// 画布宽度（像素）
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 画布高度（像素）
        /// </summary>
        public int Height { get; set; }
    }
}
