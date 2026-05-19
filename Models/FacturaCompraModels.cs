namespace ClaumanAPI.Models
{
    public class FacturaCompra
    {
        public int Id { get; set; }
        public string Fecha { get; set; } = string.Empty;
        public string TipoDoc { get; set; } = "FACTURA";       // FACTURA | BOLETA | GUIA
        public string NumeroDoc { get; set; } = string.Empty;
        public int ProveedorId { get; set; }
        public string OrdenCompra { get; set; } = string.Empty;
        public string CondVenta { get; set; } = "CONTADO";     // CONTADO | CREDITO
        public int TotalNeto { get; set; }
        public int Iva { get; set; }
        public int Total { get; set; }
        public string Estado { get; set; } = "VIGENTE";        // VIGENTE | RECEPCIONADA | PAGADA | ANULADA
        public string? FechaRecepcion { get; set; }
        public string? Vencimiento { get; set; }
        public string Usuario { get; set; } = string.Empty;

        public List<FacturaCompraDetalle> Detalle { get; set; } = new();
    }

    public class FacturaCompraDetalle
    {
        public int Id { get; set; }
        public int FacturaCompraId { get; set; }
        public int? ProductoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public int PrecioNeto { get; set; }
        public int PrecioMeson { get; set; }
        public int PrecioMayor { get; set; }
        public int Subtotal { get; set; }
    }
}
