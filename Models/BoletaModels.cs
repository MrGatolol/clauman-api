namespace ClaumanAPI.Models
{
    // Cabecera de la boleta
    public class Boleta
    {
        public int Id { get; set; }
        public int Numero { get; set; }
        public string Fecha { get; set; } = string.Empty;
        public string Hora { get; set; } = string.Empty;
        public int? ClienteId { get; set; }
        public string MedioPago { get; set; } = string.Empty;
        public int DescGlobal { get; set; }
        public int TotalNeto { get; set; }
        public int Iva { get; set; }
        public int Total { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public bool Anulada { get; set; }
        public string Bodega { get; set; } = "VINA";    // VINA | VALEMANA — desde dónde sale el stock

        // Items del carrito — se envían juntos al crear
        public List<BoletaDetalle> Detalle { get; set; } = new();
    }

    // Un item del carrito
    public class BoletaDetalle
    {
        public int Id { get; set; }
        public int BoletaId { get; set; }
        public int? ProductoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public int PrecioUnitario { get; set; }
        public int Subtotal { get; set; }
    }
}