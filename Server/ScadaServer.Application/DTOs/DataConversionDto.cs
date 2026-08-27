namespace ScadaServer.Application.DTOs
{
    public class DataConversionDto
    {
        public int Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)]
        public string Name { get; set; }

        public int SourceDeviceId { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        public string SourceVariableKey { get; set; }

        public int TargetDeviceId { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        public string TargetVariableKey { get; set; }

        public bool Active { get; set; }
    }
}
