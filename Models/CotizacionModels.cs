namespace ClaumanAPI.Models
{
    public class Cotizacion
    {
        public int Id { get; set; }
        public int Numero { get; set; }
        public string Fecha { get; set; } = string.Empty;
        public string Hora { get; set; } = string.Empty;
        public int? ClienteId { get; set; }
        public string ClienteRef { get; set; } = string.Empty;
        public string CondVenta { get; set; } = "MESON";
        public int DescGlobal { get; set; }
        public int Total { get; set; }
        public string Estado { get; set; } = "VIGENTE";
        public string Usuario { get; set; } = string.Empty;
        public string? Vencimiento { get; set; }

        public List<CotizacionDetalle> Detalle { get; set; } = new();
    }

    public class CotizacionDetalle
    {
        public int Id { get; set; }
        public int CotizacionId { get; set; }
        public int? ProductoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public int PrecioUnitario { get; set; }
        public int Subtotal { get; set; }
    }
}
