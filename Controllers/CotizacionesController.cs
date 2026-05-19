using Microsoft.AspNetCore.Mvc;
using Npgsql;
using ClaumanAPI.Models;
using ClaumanAPI.Middleware;

namespace ClaumanAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CotizacionesController : ControllerBase
    {
        private readonly string _conexion;

        public CotizacionesController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/cotizaciones
        [HttpGet]
        public IActionResult ObtenerTodas()
        {
            var lista = new List<Cotizacion>();

            using var conexion = new NpgsqlConnection(_conexion);
            conexion.Open();

            var cmd = new NpgsqlCommand(@"
                SELECT Id, Numero,
                       TO_CHAR(Fecha, 'DD/MM/YYYY') AS Fecha,
                       TO_CHAR(Hora, 'HH24:MI:SS')  AS Hora,
                       ClienteId, ClienteRef, CondVenta,
                       DescGlobal, Total, Estado, Usuario,
                       TO_CHAR(Vencimiento, 'DD/MM/YYYY') AS Vencimiento
                FROM Cotizaciones
                ORDER BY Id DESC", conexion);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new Cotizacion
                {
                    Id          = reader.GetInt32(0),
                    Numero      = reader.GetInt32(1),
                    Fecha       = reader.GetString(2),
                    Hora        = reader.GetString(3),
                    ClienteId   = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    ClienteRef  = reader.GetString(5),
                    CondVenta   = reader.GetString(6),
                    DescGlobal  = reader.GetInt32(7),
                    Total       = reader.GetInt32(8),
                    Estado      = reader.GetString(9),
                    Usuario     = reader.GetString(10),
                    Vencimiento = reader.IsDBNull(11) ? null : reader.GetString(11),
                });
            }
            return Ok(lista);
        }

        // GET /api/cotizaciones/5  — con detalle
        [HttpGet("{id}")]
        public IActionResult ObtenerPorId(int id)
        {
            using var conexion = new NpgsqlConnection(_conexion);
            conexion.Open();

            var cmdCab = new NpgsqlCommand(@"
                SELECT Id, Numero,
                       TO_CHAR(Fecha, 'DD/MM/YYYY'),
                       TO_CHAR(Hora, 'HH24:MI:SS'),
                       ClienteId, ClienteRef, CondVenta,
                       DescGlobal, Total, Estado, Usuario,
                       TO_CHAR(Vencimiento, 'DD/MM/YYYY')
                FROM Cotizaciones WHERE Id = @Id", conexion);
            cmdCab.Parameters.AddWithValue("@Id", id);

            using var reader = cmdCab.ExecuteReader();
            if (!reader.Read())
                return NotFound(new { mensaje = $"Cotización {id} no encontrada." });

            var cot = new Cotizacion
            {
                Id          = reader.GetInt32(0),
                Numero      = reader.GetInt32(1),
                Fecha       = reader.GetString(2),
                Hora        = reader.GetString(3),
                ClienteId   = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                ClienteRef  = reader.GetString(5),
                CondVenta   = reader.GetString(6),
                DescGlobal  = reader.GetInt32(7),
                Total       = reader.GetInt32(8),
                Estado      = reader.GetString(9),
                Usuario     = reader.GetString(10),
                Vencimiento = reader.IsDBNull(11) ? null : reader.GetString(11),
            };
            reader.Close();

            var cmdDet = new NpgsqlCommand(@"
                SELECT Id, CotizacionId, ProductoId, Codigo, Descripcion,
                       Cantidad, PrecioUnitario, (Cantidad * PrecioUnitario) AS Subtotal
                FROM CotizacionesDetalle WHERE CotizacionId = @Id", conexion);
            cmdDet.Parameters.AddWithValue("@Id", id);

            using var rd = cmdDet.ExecuteReader();
            while (rd.Read())
            {
                cot.Detalle.Add(new CotizacionDetalle
                {
                    Id             = rd.GetInt32(0),
                    CotizacionId   = rd.GetInt32(1),
                    ProductoId     = rd.IsDBNull(2) ? null : rd.GetInt32(2),
                    Codigo         = rd.GetString(3),
                    Descripcion    = rd.GetString(4),
                    Cantidad       = rd.GetInt32(5),
                    PrecioUnitario = rd.GetInt32(6),
                    Subtotal       = rd.GetInt32(7),
                });
            }
            return Ok(cot);
        }

        // POST /api/cotizaciones  — crea cabecera + detalle en transacción
        [HttpPost]
        [RequirePermiso("ventas.crearCotizacion")]
        public IActionResult Crear([FromBody] Cotizacion cot)
        {
            if (cot.Detalle.Count == 0)
                return BadRequest(new { mensaje = "La cotización debe tener al menos un producto." });

            using var conexion = new NpgsqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                // Próximo número con bloqueo exclusivo (evita race conditions con varios cajeros)
                var cmdNum = new NpgsqlCommand(
                    "SELECT COALESCE(MAX(Numero), 32559) + 1 FROM Cotizaciones ",
                    conexion, tx);
                cot.Numero = Convert.ToInt32(cmdNum.ExecuteScalar());

                var cmdCab = new NpgsqlCommand(@"
                    INSERT INTO Cotizaciones
                        (Numero, Fecha, Hora, ClienteId, ClienteRef, CondVenta,
                         DescGlobal, Total, Estado, Usuario, Vencimiento)
                    VALUES
                        (@Numero, NOW(), CURRENT_TIME, @ClienteId, @ClienteRef, @CondVenta,
                         @DescGlobal, @Total, 'VIGENTE', @Usuario, @Vencimiento)
                    RETURNING Id;", conexion, tx);

                cmdCab.Parameters.AddWithValue("@Numero",      cot.Numero);
                cmdCab.Parameters.AddWithValue("@ClienteId",   (object?)cot.ClienteId ?? DBNull.Value);
                cmdCab.Parameters.AddWithValue("@ClienteRef",  cot.ClienteRef ?? "");
                cmdCab.Parameters.AddWithValue("@CondVenta",   cot.CondVenta);
                cmdCab.Parameters.AddWithValue("@DescGlobal",  cot.DescGlobal);
                cmdCab.Parameters.AddWithValue("@Total",       cot.Total);
                cmdCab.Parameters.AddWithValue("@Usuario",     cot.Usuario);
                cmdCab.Parameters.AddWithValue("@Vencimiento",
                    string.IsNullOrWhiteSpace(cot.Vencimiento) ? DBNull.Value : (object)DateTime.Parse(cot.Vencimiento));

                cot.Id = Convert.ToInt32(cmdCab.ExecuteScalar());

                foreach (var item in cot.Detalle)
                {
                    var cmdDet = new NpgsqlCommand(@"
                        INSERT INTO CotizacionesDetalle
                            (CotizacionId, ProductoId, Codigo, Descripcion, Cantidad, PrecioUnitario)
                        VALUES
                            (@CotizacionId, @ProductoId, @Codigo, @Descripcion, @Cantidad, @PrecioUnitario)",
                        conexion, tx);

                    cmdDet.Parameters.AddWithValue("@CotizacionId",   cot.Id);
                    cmdDet.Parameters.AddWithValue("@ProductoId",     (object?)item.ProductoId ?? DBNull.Value);
                    cmdDet.Parameters.AddWithValue("@Codigo",         item.Codigo);
                    cmdDet.Parameters.AddWithValue("@Descripcion",    item.Descripcion);
                    cmdDet.Parameters.AddWithValue("@Cantidad",       item.Cantidad);
                    cmdDet.Parameters.AddWithValue("@PrecioUnitario", item.PrecioUnitario);
                    cmdDet.ExecuteNonQuery();
                }

                tx.Commit();
                return CreatedAtAction(nameof(ObtenerPorId), new { id = cot.Id }, cot);
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al guardar la cotización.", detalle = ex.Message });
            }
        }

        // PUT /api/cotizaciones/5/estado  — cambia el estado (ACEPTADA / RECHAZADA / VENCIDA)
        [HttpPut("{id}/estado")]
        public IActionResult CambiarEstado(int id, [FromBody] Dictionary<string, string> body)
        {
            if (!body.TryGetValue("estado", out var nuevoEstado))
                return BadRequest(new { mensaje = "Falta el campo 'estado'." });

            var validos = new[] { "VIGENTE", "ACEPTADA", "RECHAZADA", "VENCIDA" };
            if (!validos.Contains(nuevoEstado))
                return BadRequest(new { mensaje = $"Estado inválido. Debe ser uno de: {string.Join(", ", validos)}." });

            using var conexion = new NpgsqlConnection(_conexion);
            conexion.Open();
            var cmd = new NpgsqlCommand("UPDATE Cotizaciones SET Estado = @Estado WHERE Id = @Id", conexion);
            cmd.Parameters.AddWithValue("@Estado", nuevoEstado);
            cmd.Parameters.AddWithValue("@Id", id);

            if (cmd.ExecuteNonQuery() == 0)
                return NotFound(new { mensaje = $"Cotización {id} no encontrada." });

            return NoContent();
        }
    }
}
