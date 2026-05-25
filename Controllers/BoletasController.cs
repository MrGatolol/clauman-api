using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaumanAPI.Models;
using ClaumanAPI.Middleware;

namespace ClaumanAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BoletasController : ControllerBase
    {
        private readonly string _conexion;

        public BoletasController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/boletas  — lista todas las boletas
        [HttpGet]
        public IActionResult ObtenerTodas()
        {
            var lista = new List<Boleta>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(@"
                SELECT b.Id, b.Numero, FORMAT(b.Fecha, 'dd/MM/yyyy') AS Fecha,
                       CONVERT(VARCHAR(8), b.Hora, 108) AS Hora,
                       b.ClienteId, b.MedioPago, b.DescGlobal,
                       b.TotalNeto, b.Iva, b.Total, b.Usuario, b.Anulada,
                       COALESCE(b.Bodega, 'VINA') AS Bodega
                FROM Boletas b
                ORDER BY b.Id DESC", conexion);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new Boleta
                {
                    Id        = reader.GetInt32(0),
                    Numero    = reader.GetInt32(1),
                    Fecha     = reader.GetString(2),
                    Hora      = reader.GetString(3),
                    ClienteId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    MedioPago = reader.GetString(5),
                    DescGlobal= reader.GetInt32(6),
                    TotalNeto = reader.GetInt32(7),
                    Iva       = reader.GetInt32(8),
                    Total     = reader.GetInt32(9),
                    Usuario   = reader.GetString(10),
                    Anulada   = reader.GetBoolean(11),
                    Bodega    = reader.GetString(12),
                });
            }

            return Ok(lista);
        }

        // GET /api/boletas/5  — detalle con items
        [HttpGet("{id}")]
        public IActionResult ObtenerPorId(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmdCab = new SqlCommand(@"
                SELECT b.Id, b.Numero, FORMAT(b.Fecha, 'dd/MM/yyyy'),
                       CONVERT(VARCHAR(8), b.Hora, 108),
                       b.ClienteId, b.MedioPago, b.DescGlobal,
                       b.TotalNeto, b.Iva, b.Total, b.Usuario, b.Anulada,
                       COALESCE(b.Bodega, 'VINA')
                FROM Boletas b WHERE b.Id = @Id", conexion);
            cmdCab.Parameters.AddWithValue("@Id", id);

            using var reader = cmdCab.ExecuteReader();
            if (!reader.Read())
                return NotFound(new { mensaje = $"Boleta {id} no encontrada." });

            var boleta = new Boleta
            {
                Id        = reader.GetInt32(0),
                Numero    = reader.GetInt32(1),
                Fecha     = reader.GetString(2),
                Hora      = reader.GetString(3),
                ClienteId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                MedioPago = reader.GetString(5),
                DescGlobal= reader.GetInt32(6),
                TotalNeto = reader.GetInt32(7),
                Iva       = reader.GetInt32(8),
                Total     = reader.GetInt32(9),
                Usuario   = reader.GetString(10),
                Anulada   = reader.GetBoolean(11),
                Bodega    = reader.GetString(12),
            };
            reader.Close();

            var cmdDet = new SqlCommand(@"
                SELECT Id, BoletaId, ProductoId, Codigo,
                       Descripcion, Cantidad, PrecioUnitario, Subtotal
                FROM BoletasDetalle WHERE BoletaId = @Id", conexion);
            cmdDet.Parameters.AddWithValue("@Id", id);

            using var readerDet = cmdDet.ExecuteReader();
            while (readerDet.Read())
            {
                boleta.Detalle.Add(new BoletaDetalle
                {
                    Id             = readerDet.GetInt32(0),
                    BoletaId       = readerDet.GetInt32(1),
                    ProductoId     = readerDet.IsDBNull(2) ? null : readerDet.GetInt32(2),
                    Codigo         = readerDet.GetString(3),
                    Descripcion    = readerDet.GetString(4),
                    Cantidad       = readerDet.GetInt32(5),
                    PrecioUnitario = readerDet.GetInt32(6),
                    Subtotal       = readerDet.GetInt32(7),
                });
            }

            return Ok(boleta);
        }

        // POST /api/boletas
        // Esta función hace 4 cosas en una sola transacción atómica:
        //   1. Reserva un número de boleta correlativo (con TABLOCKX para evitar race conditions)
        //   2. Valida que haya stock disponible para todos los items con productoId
        //   3. Inserta cabecera + detalle
        //   4. Descuenta el stock vendido del inventario de la bodega correspondiente
        [HttpPost]
        [RequirePermiso("ventas.crearBoleta")]
        public IActionResult Crear([FromBody] Boleta boleta)
        {
            if (boleta.Detalle.Count == 0)
                return BadRequest(new { mensaje = "La boleta debe tener al menos un producto." });

            // Validar cada item del detalle: cantidad > 0, precio >= 0, código no vacío
            foreach (var item in boleta.Detalle)
            {
                if (item.Cantidad <= 0)
                    return BadRequest(new { mensaje = $"La cantidad de '{item.Codigo}' debe ser mayor a 0." });
                if (item.PrecioUnitario < 0)
                    return BadRequest(new { mensaje = $"El precio de '{item.Codigo}' no puede ser negativo." });
                if (string.IsNullOrWhiteSpace(item.Codigo))
                    return BadRequest(new { mensaje = "Hay items sin código." });
            }
            if (boleta.Total < 0)
                return BadRequest(new { mensaje = "El total no puede ser negativo." });

            var bodega = boleta.Bodega?.ToUpper() ?? "VINA";
            if (bodega != "VINA" && bodega != "VALEMANA")
                return BadRequest(new { mensaje = "Bodega inválida. Debe ser VINA o VALEMANA." });

            // Determina qué columna de Inventario actualizar según la bodega
            string colStock = bodega == "VALEMANA" ? "StockVa" : "StockVina";

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                // ---- 1) Validar stock disponible ANTES de cualquier insert ----
                // Solo validamos los items con productoId vinculado (los "manuales" pasan)
                foreach (var item in boleta.Detalle)
                {
                    if (item.ProductoId == null) continue;
                    var cmdStockActual = new SqlCommand(
                        $"SELECT COALESCE({colStock}, 0) FROM Inventario WHERE Id = @Id", conexion, tx);
                    cmdStockActual.Parameters.AddWithValue("@Id", item.ProductoId.Value);
                    var disponible = (int)(cmdStockActual.ExecuteScalar() ?? 0);
                    if (disponible < item.Cantidad)
                    {
                        return BadRequest(new {
                            mensaje = $"Stock insuficiente en {bodega} para '{item.Codigo}'. " +
                                      $"Disponible: {disponible}, requerido: {item.Cantidad}."
                        });
                    }
                }

                // ---- 2) Obtener próximo número con bloqueo exclusivo ----
                // TABLOCKX evita que otra transacción concurrente lea el mismo MAX
                var cmdNum = new SqlCommand(
                    "SELECT COALESCE(MAX(Numero), 44660) + 1 FROM Boletas WITH (TABLOCKX, HOLDLOCK)",
                    conexion, tx);
                boleta.Numero = Convert.ToInt32(cmdNum.ExecuteScalar());

                // ---- 3) Insertar cabecera ----
                var cmdCab = new SqlCommand(@"
                    INSERT INTO Boletas
                        (Numero, Fecha, Hora, ClienteId, MedioPago,
                         DescGlobal, TotalNeto, Iva, Total, Usuario, Anulada, Bodega)
                    VALUES
                        (@Numero, GETDATE(), CAST(GETDATE() AS TIME), @ClienteId, @MedioPago,
                         @DescGlobal, @TotalNeto, @Iva, @Total, @Usuario, 0, @Bodega)
                    ;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", conexion, tx);

                cmdCab.Parameters.AddWithValue("@Numero",     boleta.Numero);
                cmdCab.Parameters.AddWithValue("@ClienteId",  (object?)boleta.ClienteId ?? DBNull.Value);
                cmdCab.Parameters.AddWithValue("@MedioPago",  boleta.MedioPago);
                cmdCab.Parameters.AddWithValue("@DescGlobal", boleta.DescGlobal);
                cmdCab.Parameters.AddWithValue("@TotalNeto",  boleta.TotalNeto);
                cmdCab.Parameters.AddWithValue("@Iva",        boleta.Iva);
                cmdCab.Parameters.AddWithValue("@Total",      boleta.Total);
                cmdCab.Parameters.AddWithValue("@Usuario",    boleta.Usuario);
                cmdCab.Parameters.AddWithValue("@Bodega",     bodega);

                boleta.Id = Convert.ToInt32(cmdCab.ExecuteScalar());

                // ---- 4) Insertar detalle + descontar stock por cada item ----
                foreach (var item in boleta.Detalle)
                {
                    var cmdDet = new SqlCommand(@"
                        INSERT INTO BoletasDetalle
                            (BoletaId, ProductoId, Codigo, Descripcion, Cantidad, PrecioUnitario)
                        VALUES
                            (@BoletaId, @ProductoId, @Codigo, @Descripcion, @Cantidad, @PrecioUnitario)",
                        conexion, tx);

                    cmdDet.Parameters.AddWithValue("@BoletaId",       boleta.Id);
                    cmdDet.Parameters.AddWithValue("@ProductoId",     (object?)item.ProductoId ?? DBNull.Value);
                    cmdDet.Parameters.AddWithValue("@Codigo",         item.Codigo);
                    cmdDet.Parameters.AddWithValue("@Descripcion",    item.Descripcion);
                    cmdDet.Parameters.AddWithValue("@Cantidad",       item.Cantidad);
                    cmdDet.Parameters.AddWithValue("@PrecioUnitario", item.PrecioUnitario);
                    cmdDet.ExecuteNonQuery();

                    // Descontar stock si el item está vinculado a un producto del inventario
                    if (item.ProductoId != null)
                    {
                        var cmdStock = new SqlCommand(
                            $"UPDATE Inventario SET {colStock} = COALESCE({colStock}, 0) - @Cant WHERE Id = @Id",
                            conexion, tx);
                        cmdStock.Parameters.AddWithValue("@Cant", item.Cantidad);
                        cmdStock.Parameters.AddWithValue("@Id",   item.ProductoId.Value);
                        cmdStock.ExecuteNonQuery();
                    }
                }

                tx.Commit();
                return CreatedAtAction(nameof(ObtenerPorId), new { id = boleta.Id }, boleta);
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al guardar la boleta.", detalle = ex.Message });
            }
        }

        // PUT /api/boletas/5/anular
        // Marca la boleta como anulada y DEVUELVE el stock al inventario de la misma bodega.
        // Si ya está anulada, no hace nada.
        [HttpPut("{id}/anular")]
        public IActionResult Anular(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                // Verificar estado y obtener bodega
                var cmdGet = new SqlCommand(
                    "SELECT Anulada, COALESCE(Bodega, 'VINA') FROM Boletas WHERE Id = @Id",
                    conexion, tx);
                cmdGet.Parameters.AddWithValue("@Id", id);

                bool ya;
                string bodega;
                using (var rd = cmdGet.ExecuteReader())
                {
                    if (!rd.Read()) return NotFound(new { mensaje = $"Boleta {id} no encontrada." });
                    ya = rd.GetBoolean(0);
                    bodega = rd.GetString(1);
                }

                if (ya) return BadRequest(new { mensaje = "La boleta ya estaba anulada." });

                string colStock = bodega.Equals("VALEMANA", StringComparison.OrdinalIgnoreCase) ? "StockVa" : "StockVina";

                // Devolver stock por cada item con productoId
                var cmdDevolver = new SqlCommand($@"
                    UPDATE i
                    SET i.{colStock} = COALESCE(i.{colStock}, 0) + d.Cantidad
                    FROM Inventario i
                    INNER JOIN BoletasDetalle d ON d.ProductoId = i.Id
                    WHERE d.BoletaId = @Id AND d.ProductoId IS NOT NULL", conexion, tx);
                cmdDevolver.Parameters.AddWithValue("@Id", id);
                int devueltos = cmdDevolver.ExecuteNonQuery();

                // Marcar como anulada
                var cmdAnular = new SqlCommand(
                    "UPDATE Boletas SET Anulada = 1 WHERE Id = @Id", conexion, tx);
                cmdAnular.Parameters.AddWithValue("@Id", id);
                cmdAnular.ExecuteNonQuery();

                tx.Commit();
                return Ok(new { mensaje = $"Boleta anulada. Se devolvió stock de {devueltos} producto(s) a {bodega}." });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al anular la boleta.", detalle = ex.Message });
            }
        }
    }
}
