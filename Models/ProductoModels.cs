namespace ClaumanAPI.Models
{
    // Esta clase representa exactamente una fila de la tabla Productos en SQL Server.
    // Cada propiedad = una columna.
    // El front recibirá un JSON con estos mismos nombres (en camelCase automáticamente).
    public class Producto
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;

        // CategoriaId es el campo "fuente de verdad" — FK a la tabla Categorias.
        // El campo Categoria (string) se mantiene como conveniencia para la UI
        // (mostrar el nombre sin tener que joinear desde el front).
        // Al crear/editar:
        //   - Si viene CategoriaId  → se usa directamente (preferido)
        //   - Si viene solo Categoria (nombre) → se busca el Id; si no existe, error
        public int? CategoriaId { get; set; }
        public string Categoria { get; set; } = string.Empty;

        public string Descripcion { get; set; } = string.Empty;
        public string Ubicacion { get; set; } = string.Empty;
        public int StockVina { get; set; }
        public int StockVa { get; set; }
        public int PrecioMeson { get; set; }
        public int PrecioMayor { get; set; }
        public int PrecioWeb { get; set; }
        public int CostoNeto { get; set; }
        public int Utilidad { get; set; }
        public bool TieneImagen { get; set; }
        public string Observacion { get; set; } = string.Empty;
        public string Compatibilidad { get; set; } = string.Empty;
    }
}