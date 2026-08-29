namespace ScadaServer.Application.DTOs
{
    public class DataConversionDto
    {
        public int Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public int SourceDeviceId { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        public string SourceVariableKey { get; set; } = string.Empty;

        public int TargetDeviceId { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        public string TargetVariableKey { get; set; } = string.Empty;

        public bool Active { get; set; }
    }
}
