namespace ScadaServer.Application.DTOs
{
    public class CreateUserDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "Operator";
        public string Status { get; set; } = "Active";
    }
}
