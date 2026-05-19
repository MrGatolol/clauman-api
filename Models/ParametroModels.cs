namespace ClaumanAPI.Models
{
    public class Parametro
    {
        public string Clave { get; set; } = string.Empty;
        public string Valor { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
    }

    // Request para cambio masivo de precios
    public class CambioPreciosRequest
    {
        public int? CategoriaId { get; set; }       // null = todas
        public string TipoPrecio { get; set; } = "MESON";   // MESON | MAYORISTA | AMBOS
        public decimal Porcentaje { get; set; }     // 10 = +10%, -5 = bajada 5%
    }

    // Item del preview de cambio de precios (antes de aplicar)
    public class PreviewCambioPrecio
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int PrecioMesonActual { get; set; }
        public int PrecioMesonNuevo { get; set; }
        public int PrecioMayorActual { get; set; }
        public int PrecioMayorNuevo { get; set; }
    }

}
