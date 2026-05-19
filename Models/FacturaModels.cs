namespace ClaumanAPI.Models
{
    public class Factura
    {
        public int Id { get; set; }
        public int Numero { get; set; }
        public int Folio { get; set; }
        public string Fecha { get; set; } = string.Empty;
        public string Hora { get; set; } = string.Empty;
        public int ClienteId { get; set; }
        public string CondVenta { get; set; } = "CONTADO";    // CONTADO | CREDITO
        public string OrdenCompra { get; set; } = string.Empty;
        public int DescGlobal { get; set; }
        public int TotalNeto { get; set; }
        public int Iva { get; set; }
        public int Total { get; set; }
        public string Estado { get; set; } = "VIGENTE";       // VIGENTE | PAGADA | ANULADA | VENCIDA
        public string Usuario { get; set; } = string.Empty;
        public string? Vencimiento { get; set; }
        public string Bodega { get; set; } = "VINA";

        public List<FacturaDetalle> Detalle { get; set; } = new();
    }

    public class FacturaDetalle
    {
        public int Id { get; set; }
        public int FacturaId { get; set; }
        public int? ProductoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public int PrecioUnitario { get; set; }
        public int Subtotal { get; set; }
    }
}
