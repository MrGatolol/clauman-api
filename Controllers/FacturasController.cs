using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaumanAPI.Models;
using ClaumanAPI.Middleware;

namespace ClaumanAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FacturasController : ControllerBase
    {
        private readonly string _conexion;

        public FacturasController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/facturas
        [HttpGet]
        public IActionResult ObtenerTodas()
        {
            var lista = new List<Factura>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(@"
                SELECT Id, Numero, Folio,
                       FORMAT(Fecha, 'dd/MM/yyyy') AS Fecha,
                       CONVERT(VARCHAR(8), Hora, 108)  AS Hora,
                       ClienteId, CondVenta, OrdenCompra,
                       DescGlobal, TotalNeto, Iva, Total, Estado, Usuario,
                       FORMAT(Vencimiento, 'dd/MM/yyyy') AS Vencimiento
                FROM Facturas
                ORDER BY Id DESC", conexion);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new Factura
                {
                    Id          = reader.GetInt32(0),
                    Numero      = reader.GetInt32(1),
                    Folio       = reader.GetInt32(2),
                    Fecha       = reader.GetString(3),
                    Hora        = reader.GetString(4),
                    ClienteId   = reader.GetInt32(5),
                    CondVenta   = reader.GetString(6),
                    OrdenCompra = reader.GetString(7),
                    DescGlobal  = reader.GetInt32(8),
                    TotalNeto   = reader.GetInt32(9),
                    Iva         = reader.GetInt32(10),
                    Total       = reader.GetInt32(11),
                    Estado      = reader.GetString(12),
                    Usuario     = reader.GetString(13),
                    Vencimiento = reader.IsDBNull(14) ? null : reader.GetString(14),
                });
            }
            return Ok(lista);
        }

        // GET /api/facturas/5
        [HttpGet("{id}")]
        public IActionResult ObtenerPorId(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmdCab = new SqlCommand(@"
                SELECT Id, Numero, Folio,
                       FORMAT(Fecha, 'dd/MM/yyyy'),
                       CONVERT(VARCHAR(8), Hora, 108),
                       ClienteId, CondVenta, OrdenCompra,
                       DescGlobal, TotalNeto, Iva, Total, Estado, Usuario,
                       FORMAT(Vencimiento, 'dd/MM/yyyy')
                FROM Facturas WHERE Id = @Id", conexion);
            cmdCab.Parameters.AddWithValue("@Id", id);

            using var reader = cmdCab.ExecuteReader();
            if (!reader.Read())
                return NotFound(new { mensaje = $"Factura {id} no encontrada." });

            var f = new Factura
            {
                Id          = reader.GetInt32(0),
                Numero      = reader.GetInt32(1),
                Folio       = reader.GetInt32(2),
                Fecha       = reader.GetString(3),
                Hora        = reader.GetString(4),
                ClienteId   = reader.GetInt32(5),
                CondVenta   = reader.GetString(6),
                OrdenCompra = reader.GetString(7),
                DescGlobal  = reader.GetInt32(8),
                TotalNeto   = reader.GetInt32(9),
                Iva         = reader.GetInt32(10),
                Total       = reader.GetInt32(11),
                Estado      = reader.GetString(12),
                Usuario     = reader.GetString(13),
                Vencimiento = reader.IsDBNull(14) ? null : reader.GetString(14),
            };
            reader.Close();

            var cmdDet = new SqlCommand(@"
                SELECT Id, FacturaId, ProductoId, Codigo, Descripcion,
                       Cantidad, PrecioUnitario, (Cantidad * PrecioUnitario) AS Subtotal
                FROM FacturasDetalle WHERE FacturaId = @Id", conexion);
            cmdDet.Parameters.AddWithValue("@Id", id);

            using var rd = cmdDet.ExecuteReader();
            while (rd.Read())
            {
                f.Detalle.Add(new FacturaDetalle
                {
                    Id             = rd.GetInt32(0),
                    FacturaId      = rd.GetInt32(1),
                    ProductoId     = rd.IsDBNull(2) ? null : rd.GetInt32(2),
                    Codigo         = rd.GetString(3),
                    Descripcion    = rd.GetString(4),
                    Cantidad       = rd.GetInt32(5),
                    PrecioUnitario = rd.GetInt32(6),
                    Subtotal       = rd.GetInt32(7),
                });
            }
            return Ok(f);
        }

        // POST /api/facturas
        [HttpPost]
        [RequirePermiso("ventas.crearFactura")]
        public IActionResult Crear([FromBody] Factura f)
        {
            if (f.Detalle.Count == 0)
                return BadRequest(new { mensaje = "La factura debe tener al menos un producto." });
            if (f.ClienteId <= 0)
                return BadRequest(new { mensaje = "Las facturas requieren un cliente con RUT." });
            if (f.CondVenta == "CREDITO" && string.IsNullOrWhiteSpace(f.Vencimiento))
                return BadRequest(new { mensaje = "Las facturas a crédito requieren fecha de vencimiento." });

            foreach (var item in f.Detalle)
            {
                if (item.Cantidad <= 0)
                    return BadRequest(new { mensaje = $"La cantidad de '{item.Codigo}' debe ser mayor a 0." });
                if (item.PrecioUnitario < 0)
                    return BadRequest(new { mensaje = $"El precio de '{item.Codigo}' no puede ser negativo." });
            }
            if (f.Total < 0)
                return BadRequest(new { mensaje = "El total no puede ser negativo." });

            var bodega = f.Bodega?.ToUpper() ?? "VINA";
            if (bodega != "VINA" && bodega != "VALEMANA")
                return BadRequest(new { mensaje = "Bodega inválida." });
            string colStock = bodega == "VALEMANA" ? "StockVa" : "StockVina";

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                // Verificar que el cliente existe
                var cmdCli = new SqlCommand("SELECT COUNT(*) FROM Clientes WHERE Id = @Id", conexion, tx);
                cmdCli.Parameters.AddWithValue("@Id", f.ClienteId);
                if ((int)cmdCli.ExecuteScalar() == 0)
                    throw new InvalidOperationException($"Cliente {f.ClienteId} no existe.");

                // Validar stock disponible
                foreach (var item in f.Detalle)
                {
                    if (item.ProductoId == null) continue;
                    var cmdStockActual = new SqlCommand(
                        $"SELECT COALESCE({colStock}, 0) FROM Inventario WHERE Id = @Id", conexion, tx);
                    cmdStockActual.Parameters.AddWithValue("@Id", item.ProductoId.Value);
                    var disponible = (int)(cmdStockActual.ExecuteScalar() ?? 0);
                    if (disponible < item.Cantidad)
                        return BadRequest(new {
                            mensaje = $"Stock insuficiente en {bodega} para '{item.Codigo}'. " +
                                      $"Disponible: {disponible}, requerido: {item.Cantidad}."
                        });
                }

                // Próximo número interno y folio con bloqueo exclusivo
                var cmdNum = new SqlCommand(
                    "SELECT COALESCE(MAX(Numero), 999) + 1, COALESCE(MAX(Folio), 1999999) + 1 FROM Facturas WITH (TABLOCKX, HOLDLOCK)",
                    conexion, tx);
                using (var rdr = cmdNum.ExecuteReader())
                {
                    rdr.Read();
                    f.Numero = rdr.GetInt32(0);
                    f.Folio  = rdr.GetInt32(1);
                }

                var cmdCab = new SqlCommand(@"
                    INSERT INTO Facturas
                        (Numero, Folio, Fecha, Hora, ClienteId, CondVenta, OrdenCompra,
                         DescGlobal, TotalNeto, Iva, Total, Estado, Usuario, Vencimiento, Bodega)
                    VALUES
                        (@Numero, @Folio, GETDATE(), CAST(GETDATE() AS TIME), @ClienteId, @CondVenta, @OrdenCompra,
                         @DescGlobal, @TotalNeto, @Iva, @Total, 'VIGENTE', @Usuario, @Vencimiento, @Bodega)
                    ;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", conexion, tx);

                cmdCab.Parameters.AddWithValue("@Numero",      f.Numero);
                cmdCab.Parameters.AddWithValue("@Folio",       f.Folio);
                cmdCab.Parameters.AddWithValue("@ClienteId",   f.ClienteId);
                cmdCab.Parameters.AddWithValue("@CondVenta",   f.CondVenta);
                cmdCab.Parameters.AddWithValue("@OrdenCompra", f.OrdenCompra ?? "");
                cmdCab.Parameters.AddWithValue("@DescGlobal",  f.DescGlobal);
                cmdCab.Parameters.AddWithValue("@TotalNeto",   f.TotalNeto);
                cmdCab.Parameters.AddWithValue("@Iva",         f.Iva);
                cmdCab.Parameters.AddWithValue("@Total",       f.Total);
                cmdCab.Parameters.AddWithValue("@Usuario",     f.Usuario);
                cmdCab.Parameters.AddWithValue("@Vencimiento",
                    string.IsNullOrWhiteSpace(f.Vencimiento) ? DBNull.Value : (object)DateTime.Parse(f.Vencimiento));
                cmdCab.Parameters.AddWithValue("@Bodega",      bodega);

                f.Id = Convert.ToInt32(cmdCab.ExecuteScalar());

                foreach (var item in f.Detalle)
                {
                    var cmdDet = new SqlCommand(@"
                        INSERT INTO FacturasDetalle
                            (FacturaId, ProductoId, Codigo, Descripcion, Cantidad, PrecioUnitario)
                        VALUES
                            (@FacturaId, @ProductoId, @Codigo, @Descripcion, @Cantidad, @PrecioUnitario)",
                        conexion, tx);

                    cmdDet.Parameters.AddWithValue("@FacturaId",      f.Id);
                    cmdDet.Parameters.AddWithValue("@ProductoId",     (object?)item.ProductoId ?? DBNull.Value);
                    cmdDet.Parameters.AddWithValue("@Codigo",         item.Codigo);
                    cmdDet.Parameters.AddWithValue("@Descripcion",    item.Descripcion);
                    cmdDet.Parameters.AddWithValue("@Cantidad",       item.Cantidad);
                    cmdDet.Parameters.AddWithValue("@PrecioUnitario", item.PrecioUnitario);
                    cmdDet.ExecuteNonQuery();

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
                return CreatedAtAction(nameof(ObtenerPorId), new { id = f.Id }, f);
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al guardar la factura.", detalle = ex.Message });
            }
        }

        // PUT /api/facturas/5/pagar  — marca como pagada
        [HttpPut("{id}/pagar")]
        public IActionResult Pagar(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            var cmd = new SqlCommand(
                "UPDATE Facturas SET Estado = 'PAGADA' WHERE Id = @Id AND Estado IN ('VIGENTE', 'VENCIDA')",
                conexion);
            cmd.Parameters.AddWithValue("@Id", id);
            if (cmd.ExecuteNonQuery() == 0)
                return NotFound(new { mensaje = $"Factura {id} no encontrada o ya está pagada/anulada." });
            return NoContent();
        }

        // PUT /api/facturas/5/anular
        // Marca anulada y devuelve el stock al inventario de la misma bodega.
        [HttpPut("{id}/anular")]
        public IActionResult Anular(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                var cmdGet = new SqlCommand(
                    "SELECT Estado, COALESCE(Bodega, 'VINA') FROM Facturas WHERE Id = @Id",
                    conexion, tx);
                cmdGet.Parameters.AddWithValue("@Id", id);

                string estado, bodega;
                using (var rd = cmdGet.ExecuteReader())
                {
                    if (!rd.Read()) return NotFound(new { mensaje = $"Factura {id} no encontrada." });
                    estado = rd.GetString(0);
                    bodega = rd.GetString(1);
                }
                if (estado == "PAGADA")
                    return BadRequest(new { mensaje = "No se puede anular una factura PAGADA." });
                if (estado == "ANULADA")
                    return BadRequest(new { mensaje = "La factura ya está anulada." });

                string colStock = bodega.Equals("VALEMANA", StringComparison.OrdinalIgnoreCase) ? "StockVa" : "StockVina";

                var cmdDevolver = new SqlCommand($@"
                    UPDATE i
                    SET i.{colStock} = COALESCE(i.{colStock}, 0) + d.Cantidad
                    FROM Inventario i
                    INNER JOIN FacturasDetalle d ON d.ProductoId = i.Id
                    WHERE d.FacturaId = @Id AND d.ProductoId IS NOT NULL", conexion, tx);
                cmdDevolver.Parameters.AddWithValue("@Id", id);
                int devueltos = cmdDevolver.ExecuteNonQuery();

                var cmdAnular = new SqlCommand(
                    "UPDATE Facturas SET Estado = 'ANULADA' WHERE Id = @Id", conexion, tx);
                cmdAnular.Parameters.AddWithValue("@Id", id);
                cmdAnular.ExecuteNonQuery();

                tx.Commit();
                return Ok(new { mensaje = $"Factura anulada. Stock devuelto a {bodega} para {devueltos} producto(s)." });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al anular.", detalle = ex.Message });
            }
        }

        // PUT /api/facturas/5/anular-legacy  (DEPRECATED)
        [HttpPut("{id}/anular-legacy")]
        public IActionResult AnularLegacy(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            var cmd = new SqlCommand(
                "UPDATE Facturas SET Estado = 'ANULADA' WHERE Id = @Id AND Estado <> 'PAGADA'",
                conexion);
            cmd.Parameters.AddWithValue("@Id", id);
            if (cmd.ExecuteNonQuery() == 0)
                return NotFound(new { mensaje = $"Factura {id} no encontrada o ya está pagada." });
            return NoContent();
        }
    }
}
