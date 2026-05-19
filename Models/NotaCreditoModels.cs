namespace ClaumanAPI.Models
{
    /// <summary>
    /// Nota de Crédito: documento que reversa total o parcialmente una venta.
    /// Cuando se emite:
    ///   1. Devuelve el stock de los items al inventario de la bodega original
    ///   2. Marca el documento origen como ANULADA (si la nota cubre todo) o queda VIGENTE (si es parcial)
    /// </summary>
    public class NotaCredito
    {
        public int Id { get; set; }
        public int Numero { get; set; }
        public string Fecha { get; set; } = string.Empty;
        public string Hora { get; set; } = string.Empty;

        // Doc que se está revirtiendo
        public string TipoDocOrigen { get; set; } = "BOLETA";  // BOLETA | FACTURA | NOTA_VENTA
        public int DocOrigenId { get; set; }
        public int DocOrigenNumero { get; set; }

        public int? ClienteId { get; set; }
        public int Total { get; set; }
        public string Motivo { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
        public string Bodega { get; set; } = "VINA";
        public bool Anulada { get; set; }

        public List<NotaCreditoDetalle> Detalle { get; set; } = new();
    }

    public class NotaCreditoDetalle
    {
        public int Id { get; set; }
        public int NotaCreditoId { get; set; }
        public int? ProductoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public int PrecioUnitario { get; set; }
        public int Subtotal { get; set; }
    }
}
