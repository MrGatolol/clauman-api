using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaumanAPI.Models;
using ClaumanAPI.Middleware;

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

        // Convierte el nombre de bodega ("VINA"/"VALEMANA") a la columna de stock correspondiente.
        // Devuelve null si la bodega no es válida.
        private static string? BodegaACol(string? bodega)
        {
            if (string.IsNullOrWhiteSpace(bodega)) return null;
            var b = bodega.Trim().ToUpperInvariant();
            return b switch
            {
                "VINA" => "StockVina",
                "VALEMANA" => "StockVa",
                _ => null
            };
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
        // Modelo de stock en tránsito: al crear, el stock SALE de la bodega origen
        // (productos físicamente despachados). Llegan a la bodega destino al COMPLETAR.
        // Si se ANULA mientras está PENDIENTE → devuelve a origen.
        [HttpPost]
        public IActionResult Crear([FromBody] Traslado traslado)
        {
            if (traslado.Detalle.Count == 0)
                return BadRequest(new { mensaje = "El traslado debe tener al menos un producto." });
            if (string.IsNullOrWhiteSpace(traslado.Usuario))
                return BadRequest(new { mensaje = "Se requiere usuario." });

            var colOrigen = BodegaACol(traslado.BodegaOrigen);
            var colDest   = BodegaACol(traslado.BodegaDest);
            if (colOrigen == null || colDest == null)
                return BadRequest(new { mensaje = "Bodega inválida. Debe ser VINA o VALEMANA." });
            if (colOrigen == colDest)
                return BadRequest(new { mensaje = "La bodega origen y destino no pueden ser iguales." });

            foreach (var item in traslado.Detalle)
            {
                if (item.Cantidad <= 0)
                    return BadRequest(new { mensaje = $"La cantidad de '{item.Codigo}' debe ser mayor a 0." });
                if (string.IsNullOrWhiteSpace(item.Codigo))
                    return BadRequest(new { mensaje = "Hay items sin código." });
            }

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                // 1) Validar stock disponible en bodega origen (con UPDLOCK para evitar
                //    que otra transacción concurrente consuma el mismo stock entre
                //    nuestra lectura y nuestro UPDATE)
                foreach (var item in traslado.Detalle)
                {
                    if (item.ProductoId == null) continue;
                    var cmdStock = new SqlCommand(
                        $"SELECT COALESCE({colOrigen}, 0) FROM Inventario WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id",
                        conexion, tx);
                    cmdStock.Parameters.AddWithValue("@Id", item.ProductoId.Value);
                    var disponible = Convert.ToInt32(cmdStock.ExecuteScalar() ?? 0);
                    if (disponible < item.Cantidad)
                    {
                        return BadRequest(new {
                            mensaje = $"Stock insuficiente en {traslado.BodegaOrigen} para '{item.Codigo}'. " +
                                      $"Disponible: {disponible}, requerido: {item.Cantidad}."
                        });
                    }
                }

                // 2) Reservar número correlativo
                var cmdNum = new SqlCommand(
                    "SELECT COALESCE(MAX(Numero), 8399) + 1 FROM Traslados WITH (TABLOCKX, HOLDLOCK)",
                    conexion, tx);
                traslado.Numero = Convert.ToInt32(cmdNum.ExecuteScalar());

                // 3) Insertar cabecera
                var cmdCab = new SqlCommand(@"
                    INSERT INTO Traslados
                        (Numero, Fecha, Hora, BodegaOrigen, BodegaDest, Estado, Usuario)
                    OUTPUT INSERTED.Id,
                           FORMAT(INSERTED.Fecha, 'dd/MM/yyyy') AS Fecha,
                           CONVERT(VARCHAR(8), INSERTED.Hora, 108) AS Hora
                    VALUES
                        (@Numero, GETDATE(), CAST(GETDATE() AS TIME),
                         @BodegaOrigen, @BodegaDest, 'PENDIENTE', @Usuario)", conexion, tx);

                cmdCab.Parameters.AddWithValue("@Numero",       traslado.Numero);
                cmdCab.Parameters.AddWithValue("@BodegaOrigen", traslado.BodegaOrigen);
                cmdCab.Parameters.AddWithValue("@BodegaDest",   traslado.BodegaDest);
                cmdCab.Parameters.AddWithValue("@Usuario",      traslado.Usuario);

                using (var rd = cmdCab.ExecuteReader())
                {
                    rd.Read();
                    traslado.Id    = rd.GetInt32(0);
                    traslado.Fecha = rd.GetString(1);
                    traslado.Hora  = rd.GetString(2);
                }

                // 4) Insertar detalle + DESCONTAR stock de la bodega origen
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

                    if (item.ProductoId != null)
                    {
                        var cmdSal = new SqlCommand(
                            $"UPDATE Inventario SET {colOrigen} = COALESCE({colOrigen}, 0) - @Cant WHERE Id = @Id",
                            conexion, tx);
                        cmdSal.Parameters.AddWithValue("@Cant", item.Cantidad);
                        cmdSal.Parameters.AddWithValue("@Id",   item.ProductoId.Value);
                        cmdSal.ExecuteNonQuery();
                    }
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
        // Marca el traslado como COMPLETADO y SUMA el stock en la bodega destino.
        // Solo permitido si está en PENDIENTE.
        [HttpPut("{id}/completar")]
        public IActionResult Completar(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();
            try
            {
                // Obtener estado + bodega destino
                var cmdGet = new SqlCommand(
                    "SELECT Estado, BodegaDest FROM Traslados WHERE Id = @Id",
                    conexion, tx);
                cmdGet.Parameters.AddWithValue("@Id", id);
                string? estado = null, bodegaDest = null;
                using (var rd = cmdGet.ExecuteReader())
                {
                    if (!rd.Read()) return NotFound(new { mensaje = $"Traslado {id} no encontrado." });
                    estado     = rd.GetString(0);
                    bodegaDest = rd.GetString(1);
                }
                if (estado != "PENDIENTE")
                    return BadRequest(new { mensaje = $"El traslado ya está en estado '{estado}'." });

                var colDest = BodegaACol(bodegaDest);
                if (colDest == null)
                    return StatusCode(500, new { mensaje = $"Bodega destino inválida ('{bodegaDest}'). Datos inconsistentes." });

                // Sumar stock en la bodega destino por cada item con productoId
                var cmdSumar = new SqlCommand($@"
                    UPDATE i
                    SET i.{colDest} = COALESCE(i.{colDest}, 0) + d.Cantidad
                    FROM Inventario i
                    INNER JOIN TrasladosDetalle d ON d.ProductoId = i.Id
                    WHERE d.TrasladoId = @Id AND d.ProductoId IS NOT NULL",
                    conexion, tx);
                cmdSumar.Parameters.AddWithValue("@Id", id);
                int afectados = cmdSumar.ExecuteNonQuery();

                var cmdUpd = new SqlCommand(
                    "UPDATE Traslados SET Estado = 'COMPLETADO' WHERE Id = @Id",
                    conexion, tx);
                cmdUpd.Parameters.AddWithValue("@Id", id);
                cmdUpd.ExecuteNonQuery();

                tx.Commit();
                return Ok(new { mensaje = $"Traslado completado. Se sumó stock de {afectados} producto(s) a {bodegaDest}." });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al completar el traslado.", detalle = ex.Message });
            }
        }

        // PUT /api/traslados/5/anular
        // Solo permitido si está PENDIENTE. Devuelve el stock a la bodega origen.
        [HttpPut("{id}/anular")]
        [RequireRol("ADMIN")]
        public IActionResult Anular(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();
            try
            {
                var cmdGet = new SqlCommand(
                    "SELECT Estado, BodegaOrigen FROM Traslados WHERE Id = @Id",
                    conexion, tx);
                cmdGet.Parameters.AddWithValue("@Id", id);
                string? estado = null, bodegaOrigen = null;
                using (var rd = cmdGet.ExecuteReader())
                {
                    if (!rd.Read()) return NotFound(new { mensaje = $"Traslado {id} no encontrado." });
                    estado       = rd.GetString(0);
                    bodegaOrigen = rd.GetString(1);
                }
                if (estado != "PENDIENTE")
                    return BadRequest(new { mensaje = $"Solo se pueden anular traslados PENDIENTE (actual: '{estado}')." });

                var colOrigen = BodegaACol(bodegaOrigen);
                if (colOrigen == null)
                    return StatusCode(500, new { mensaje = $"Bodega origen inválida ('{bodegaOrigen}'). Datos inconsistentes." });

                // Devolver stock a la bodega origen
                var cmdDevolver = new SqlCommand($@"
                    UPDATE i
                    SET i.{colOrigen} = COALESCE(i.{colOrigen}, 0) + d.Cantidad
                    FROM Inventario i
                    INNER JOIN TrasladosDetalle d ON d.ProductoId = i.Id
                    WHERE d.TrasladoId = @Id AND d.ProductoId IS NOT NULL",
                    conexion, tx);
                cmdDevolver.Parameters.AddWithValue("@Id", id);
                int afectados = cmdDevolver.ExecuteNonQuery();

                var cmdUpd = new SqlCommand(
                    "UPDATE Traslados SET Estado = 'ANULADO' WHERE Id = @Id",
                    conexion, tx);
                cmdUpd.Parameters.AddWithValue("@Id", id);
                cmdUpd.ExecuteNonQuery();

                tx.Commit();
                return Ok(new { mensaje = $"Traslado anulado. Se devolvió stock de {afectados} producto(s) a {bodegaOrigen}." });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al anular el traslado.", detalle = ex.Message });
            }
        }
    }
}
