namespace ClaumanAPI.Models
{
    public class NotaVenta
    {
        public int Id { get; set; }
        public int Numero { get; set; }
        public string Fecha { get; set; } = string.Empty;
        public string Hora { get; set; } = string.Empty;
        public int? ClienteId { get; set; }
        public string ClienteRef { get; set; } = string.Empty;
        public string CondVenta { get; set; } = "MESON";
        public string MedioPago { get; set; } = "EFECTIVO";
        public int DescGlobal { get; set; }
        public int TotalNeto { get; set; }
        public int Iva { get; set; }
        public int Total { get; set; }
        public string Estado { get; set; } = "VIGENTE";
        public string Usuario { get; set; } = string.Empty;
        public string Bodega { get; set; } = "VINA";

        public List<NotaVentaDetalle> Detalle { get; set; } = new();
    }

    public class NotaVentaDetalle
    {
        public int Id { get; set; }
        public int NotaVentaId { get; set; }
        public int? ProductoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public int PrecioUnitario { get; set; }
        public int Subtotal { get; set; }
    }
}
