namespace ScadaServer.Application.DTOs
{
    public class HmiComponentDto
    {
        public int Id { get; set; }
        public int PageId { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int ZIndex { get; set; }
        public string BindField { get; set; }
        public string PropsJson { get; set; }
    }
}
