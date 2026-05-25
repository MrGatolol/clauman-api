using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaumanAPI.Models;

namespace ClaumanAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TrasladosController : ControllerBase
    {
        private readonly string _conexion;

        public TrasladosController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/traslados
        [HttpGet]
        public IActionResult ObtenerTodos()
        {
            var lista = new List<Traslado>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(@"
                SELECT Id, Numero,
                       FORMAT(Fecha, 'dd/MM/yyyy') AS Fecha,
                       CONVERT(VARCHAR(8), Hora, 108) AS Hora,
                       BodegaOrigen, BodegaDest, Estado, Usuario
                FROM Traslados
                ORDER BY Id DESC", conexion);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new Traslado
                {
                    Id           = reader.GetInt32(0),
                    Numero       = reader.GetInt32(1),
                    Fecha        = reader.GetString(2),
                    Hora         = reader.GetString(3),
                    BodegaOrigen = reader.GetString(4),
                    BodegaDest   = reader.GetString(5),
                    Estado       = reader.GetString(6),
                    Usuario      = reader.GetString(7),
                });
            }

            return Ok(lista);
        }

        // GET /api/traslados/5
        [HttpGet("{id}")]
        public IActionResult ObtenerPorId(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmdCab = new SqlCommand(@"
                SELECT Id, Numero,
                       FORMAT(Fecha, 'dd/MM/yyyy'),
                       CONVERT(VARCHAR(8), Hora, 108),
                       BodegaOrigen, BodegaDest, Estado, Usuario
                FROM Traslados WHERE Id = @Id", conexion);
            cmdCab.Parameters.AddWithValue("@Id", id);

            using var reader = cmdCab.ExecuteReader();
            if (!reader.Read())
                return NotFound(new { mensaje = $"Traslado {id} no encontrado." });

            var traslado = new Traslado
            {
                Id           = reader.GetInt32(0),
                Numero       = reader.GetInt32(1),
                Fecha        = reader.GetString(2),
                Hora         = reader.GetString(3),
                BodegaOrigen = reader.GetString(4),
                BodegaDest   = reader.GetString(5),
                Estado       = reader.GetString(6),
                Usuario      = reader.GetString(7),
            };
            reader.Close();

            var cmdDet = new SqlCommand(@"
                SELECT Id, TrasladoId, ProductoId, Codigo, Descripcion, Cantidad
                FROM TrasladosDetalle WHERE TrasladoId = @Id", conexion);
            cmdDet.Parameters.AddWithValue("@Id", id);

            using var readerDet = cmdDet.ExecuteReader();
            while (readerDet.Read())
            {
                traslado.Detalle.Add(new TrasladoDetalle
                {
                    Id          = readerDet.GetInt32(0),
                    TrasladoId  = readerDet.GetInt32(1),
                    ProductoId  = readerDet.IsDBNull(2) ? null : readerDet.GetInt32(2),
                    Codigo      = readerDet.GetString(3),
                    Descripcion = readerDet.GetString(4),
                    Cantidad    = readerDet.GetInt32(5),
                });
            }

            return Ok(traslado);
        }

        // POST /api/traslados
        [HttpPost]
        public IActionResult Crear([FromBody] Traslado traslado)
        {
            if (traslado.Detalle.Count == 0)
                return BadRequest(new { mensaje = "El traslado debe tener al menos un producto." });

            if (traslado.BodegaOrigen == traslado.BodegaDest)
                return BadRequest(new { mensaje = "La bodega origen y destino no pueden ser iguales." });

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                var cmdNum = new SqlCommand(
                    "SELECT COALESCE(MAX(Numero), 8399) + 1 FROM Traslados",
                    conexion, tx);
                traslado.Numero = Convert.ToInt32(cmdNum.ExecuteScalar());

                var cmdCab = new SqlCommand(@"
                    INSERT INTO Traslados
                        (Numero, Fecha, Hora, BodegaOrigen, BodegaDest, Estado, Usuario)
                    VALUES
                        (@Numero, GETDATE(), CURRENT_TIME,
                         @BodegaOrigen, @BodegaDest, 'PENDIENTE', @Usuario)
                    ;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", conexion, tx);

                cmdCab.Parameters.AddWithValue("@Numero",       traslado.Numero);
                cmdCab.Parameters.AddWithValue("@BodegaOrigen", traslado.BodegaOrigen);
                cmdCab.Parameters.AddWithValue("@BodegaDest",   traslado.BodegaDest);
                cmdCab.Parameters.AddWithValue("@Usuario",      traslado.Usuario);

                traslado.Id = Convert.ToInt32(cmdCab.ExecuteScalar());

                foreach (var item in traslado.Detalle)
                {
                    var cmdDet = new SqlCommand(@"
                        INSERT INTO TrasladosDetalle
                            (TrasladoId, ProductoId, Codigo, Descripcion, Cantidad)
                        VALUES
                            (@TrasladoId, @ProductoId, @Codigo, @Descripcion, @Cantidad)",
                        conexion, tx);

                    cmdDet.Parameters.AddWithValue("@TrasladoId",  traslado.Id);
                    cmdDet.Parameters.AddWithValue("@ProductoId",  (object?)item.ProductoId ?? DBNull.Value);
                    cmdDet.Parameters.AddWithValue("@Codigo",      item.Codigo);
                    cmdDet.Parameters.AddWithValue("@Descripcion", item.Descripcion);
                    cmdDet.Parameters.AddWithValue("@Cantidad",    item.Cantidad);
                    cmdDet.ExecuteNonQuery();
                }

                tx.Commit();
                return CreatedAtAction(nameof(ObtenerPorId), new { id = traslado.Id }, traslado);
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al guardar el traslado.", detalle = ex.Message });
            }
        }

        // PUT /api/traslados/5/completar
        [HttpPut("{id}/completar")]
        public IActionResult Completar(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(
                "UPDATE Traslados SET Estado = 'COMPLETADO' WHERE Id = @Id AND Estado = 'PENDIENTE'",
                conexion);
            cmd.Parameters.AddWithValue("@Id", id);

            int filas = cmd.ExecuteNonQuery();
            if (filas == 0)
                return NotFound(new { mensaje = $"Traslado {id} no encontrado o ya no está pendiente." });

            return NoContent();
        }

        // PUT /api/traslados/5/anular
        [HttpPut("{id}/anular")]
        public IActionResult Anular(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(
                "UPDATE Traslados SET Estado = 'ANULADO' WHERE Id = @Id AND Estado = 'PENDIENTE'",
                conexion);
            cmd.Parameters.AddWithValue("@Id", id);

            int filas = cmd.ExecuteNonQuery();
            if (filas == 0)
                return NotFound(new { mensaje = $"Traslado {id} no encontrado o ya no está pendiente." });

            return NoContent();
        }
    }
}
