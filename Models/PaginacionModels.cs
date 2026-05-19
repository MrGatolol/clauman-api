namespace ClaumanAPI.Models
{
    /// <summary>
    /// Wrapper estándar para respuestas paginadas. El frontend lo recibe igual
    /// para todos los listados grandes (inventario, clientes, boletas, etc.).
    /// </summary>
    public class Paginado<T>
    {
        public List<T> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int Size { get; set; }
        public int TotalPages => Size > 0 ? (int)Math.Ceiling((double)Total / Size) : 0;
    }
}
