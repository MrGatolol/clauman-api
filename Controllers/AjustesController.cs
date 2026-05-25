using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaumanAPI.Models;
using ClaumanAPI.Middleware;

namespace ClaumanAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AjustesController : ControllerBase
    {
        private readonly string _conexion;

        public AjustesController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/ajustes?desde&hasta
        [HttpGet]
        public IActionResult ObtenerTodos(
            [FromQuery] DateTime? desde = null,
            [FromQuery] DateTime? hasta = null)
        {
            var lista = new List<Ajuste>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var sql = @"
                SELECT Id,
                       FORMAT(Fecha, 'dd/MM/yyyy HH:mm:ss') AS Fecha,
                       Local, Tipo, ProductoId, Codigo, Descripcion, Motivo, Ajuste, Usuario
                FROM Ajustes
                WHERE (CAST(@Desde AS DATE) IS NULL OR CAST(Fecha AS DATE) >= CAST(@Desde AS DATE))
                  AND (CAST(@Hasta AS DATE) IS NULL OR CAST(Fecha AS DATE) <= CAST(@Hasta AS DATE))
                ORDER BY Fecha DESC";

            var cmd = new SqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@Desde", (object?)desde ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Hasta", (object?)hasta ?? DBNull.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new Ajuste
                {
                    Id             = reader.GetInt32(0),
                    Fecha          = reader.GetString(1),
                    Local          = reader.GetString(2),
                    Tipo           = reader.GetString(3),
                    ProductoId     = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    Codigo         = reader.GetString(5),
                    Descripcion    = reader.GetString(6),
                    Motivo         = reader.GetString(7),
                    AjusteCantidad = reader.GetInt32(8),
                    Usuario        = reader.GetString(9),
                });
            }
            return Ok(lista);
        }

        // POST /api/ajustes
        // Crea un ajuste y, si tiene cantidad != 0 y ProductoId no nulo, modifica el stock.
        // Atómico: si falla la BD, ni el ajuste ni el cambio de stock quedan persistidos.
        [HttpPost]
        [RequirePermiso("inventario.ajustes")]
        public IActionResult Crear([FromBody] Ajuste a)
        {
            if (string.IsNullOrWhiteSpace(a.Motivo))
                return BadRequest(new { mensaje = "Se requiere indicar un motivo del ajuste." });
            if (string.IsNullOrWhiteSpace(a.Usuario))
                return BadRequest(new { mensaje = "Se requiere usuario." });

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                // 1) Insertar el ajuste
                // OUTPUT INSERTED nos devuelve Id + Fecha en una sola llamada — así
                // el front puede mostrar el ajuste recién creado sin tener que refetchear.
                var cmd = new SqlCommand(@"
                    INSERT INTO Ajustes (Fecha, Local, Tipo, ProductoId, Codigo, Descripcion, Motivo, Ajuste, Usuario)
                    OUTPUT INSERTED.Id, FORMAT(INSERTED.Fecha, 'dd/MM/yyyy HH:mm:ss') AS Fecha
                    VALUES (GETDATE(), @Local, @Tipo, @ProductoId, @Codigo, @Descripcion, @Motivo, @Ajuste, @Usuario)", conexion, tx);

                cmd.Parameters.AddWithValue("@Local",       a.Local);
                cmd.Parameters.AddWithValue("@Tipo",        a.Tipo);
                cmd.Parameters.AddWithValue("@ProductoId",  (object?)a.ProductoId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Codigo",      a.Codigo ?? "");
                cmd.Parameters.AddWithValue("@Descripcion", a.Descripcion ?? "");
                cmd.Parameters.AddWithValue("@Motivo",      a.Motivo);
                cmd.Parameters.AddWithValue("@Ajuste",      a.AjusteCantidad);
                cmd.Parameters.AddWithValue("@Usuario",     a.Usuario);

                using (var rd = cmd.ExecuteReader())
                {
                    rd.Read();
                    a.Id    = rd.GetInt32(0);
                    a.Fecha = rd.GetString(1);
                }

                // 2) Si tiene producto + cantidad != 0, actualiza el stock
                if (a.ProductoId.HasValue && a.AjusteCantidad != 0)
                {
                    // Local determina qué columna(s) afectar
                    string updateCols;
                    if (a.Local.Equals("VINA", StringComparison.OrdinalIgnoreCase))
                        updateCols = "StockVina = COALESCE(StockVina, 0) + @Ajuste";
                    else if (a.Local.Equals("VALEMANA", StringComparison.OrdinalIgnoreCase))
                        updateCols = "StockVa = COALESCE(StockVa, 0) + @Ajuste";
                    else // TODAS — repartir mitad y mitad (simplificación)
                        updateCols = "StockVina = COALESCE(StockVina, 0) + @Ajuste, StockVa = COALESCE(StockVa, 0) + @Ajuste";

                    var cmdStock = new SqlCommand(
                        $"UPDATE Inventario SET {updateCols} WHERE Id = @Id", conexion, tx);
                    cmdStock.Parameters.AddWithValue("@Ajuste", a.AjusteCantidad);
                    cmdStock.Parameters.AddWithValue("@Id",     a.ProductoId.Value);
                    cmdStock.ExecuteNonQuery();
                }

                tx.Commit();
                return CreatedAtAction(nameof(ObtenerTodos), a);
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al crear el ajuste.", detalle = ex.Message });
            }
        }
    }
}
