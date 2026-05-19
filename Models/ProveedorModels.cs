namespace ClaumanAPI.Models
{
    public class Proveedor
    {
        public int Id { get; set; }
        public string Rut { get; set; } = string.Empty;
        public string RazonSocial { get; set; } = string.Empty;
        public string Direccion { get; set; } = string.Empty;
        public string Ciudad { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string EmailCorp { get; set; } = string.Empty;
        public string Ejecutivo { get; set; } = string.Empty;
        public string EmailCot { get; set; } = string.Empty;
        public string Banco { get; set; } = string.Empty;
        public string CtaCte { get; set; } = string.Empty;
        public string RutTitular { get; set; } = string.Empty;
        public string EmailPagos { get; set; } = string.Empty;
    }
}
