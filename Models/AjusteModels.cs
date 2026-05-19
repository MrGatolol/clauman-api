namespace ClaumanAPI.Models
{
    public class Ajuste
    {
        public int Id { get; set; }
        public string Fecha { get; set; } = string.Empty;   // formato 'dd/MM/yyyy HH:mm'
        public string Local { get; set; } = "VINA";          // VINA | VALEMANA | TODAS
        public string Tipo { get; set; } = "AJUSTE INVENTARIO";
        public int? ProductoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Motivo { get; set; } = string.Empty;
        public int AjusteCantidad { get; set; }              // + suma, - resta, 0 = sin afectar
        public string Usuario { get; set; } = string.Empty;
    }
}
