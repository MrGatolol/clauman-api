namespace ClaumanAPI.Models
{
    public class Traslado
    {
        public int Id { get; set; }
        public int Numero { get; set; }
        public string Fecha { get; set; } = string.Empty;
        public string Hora { get; set; } = string.Empty;
        public string BodegaOrigen { get; set; } = string.Empty;
        public string BodegaDest { get; set; } = string.Empty;
        public string Estado { get; set; } = "PENDIENTE";
        public string Usuario { get; set; } = string.Empty;

        public List<TrasladoDetalle> Detalle { get; set; } = new();
    }

    public class TrasladoDetalle
    {
        public int Id { get; set; }
        public int TrasladoId { get; set; }
        public int? ProductoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int Cantidad { get; set; }
    }
}
