namespace ClaumanAPI.Models
{
    public class Cliente
    {
        public int Id { get; set; }
        public string Rut { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Direccion { get; set; } = string.Empty;
        public string Comuna { get; set; } = string.Empty;
        public string Ciudad { get; set; } = string.Empty;
        public string Giro { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string EmailCorp { get; set; } = string.Empty;
        public string EmailCot { get; set; } = string.Empty;
    }
}