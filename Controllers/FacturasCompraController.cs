using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaumanAPI.Models;
using ClaumanAPI.Middleware;

namespace ClaumanAPI.Controllers
{
    [Route("api/facturas-compra")]
    [ApiController]
    public class FacturasCompraController : ControllerBase
    {
        private readonly string _conexion;

        public FacturasCompraController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/facturas-compra
        [HttpGet]
        public IActionResult ObtenerTodas()
        {
            var lista = new List<FacturaCompra>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(@"
                SELECT Id,
                       FORMAT(Fecha, 'dd/MM/yyyy') AS Fecha,
                       TipoDoc, NumeroDoc, ProveedorId, OrdenCompra, CondVenta,
                       TotalNeto, Iva, Total, Estado,
                       FORMAT(FechaRecepcion, 'dd/MM/yyyy') AS FechaRecepcion,
                       FORMAT(Vencimiento, 'dd/MM/yyyy') AS Vencimiento,
                       Usuario
                FROM FacturasCompra
                ORDER BY Id DESC", conexion);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new FacturaCompra
                {
                    Id             = reader.GetInt32(0),
                    Fecha          = reader.GetString(1),
                    TipoDoc        = reader.GetString(2),
                    NumeroDoc      = reader.GetString(3),
                    ProveedorId    = reader.GetInt32(4),
                    OrdenCompra    = reader.GetString(5),
                    CondVenta      = reader.GetString(6),
                    TotalNeto      = reader.GetInt32(7),
                    Iva            = reader.GetInt32(8),
                    Total          = reader.GetInt32(9),
                    Estado         = reader.GetString(10),
                    FechaRecepcion = reader.IsDBNull(11) ? null : reader.GetString(11),
                    Vencimiento    = reader.IsDBNull(12) ? null : reader.GetString(12),
                    Usuario        = reader.GetString(13),
                });
            }
            return Ok(lista);
        }

        // GET /api/facturas-compra/5
        [HttpGet("{id}")]
        public IActionResult ObtenerPorId(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmdCab = new SqlCommand(@"
                SELECT Id,
                       FORMAT(Fecha, 'dd/MM/yyyy'),
                       TipoDoc, NumeroDoc, ProveedorId, OrdenCompra, CondVenta,
                       TotalNeto, Iva, Total, Estado,
                       FORMAT(FechaRecepcion, 'dd/MM/yyyy'),
                       FORMAT(Vencimiento, 'dd/MM/yyyy'),
                       Usuario
                FROM FacturasCompra WHERE Id = @Id", conexion);
            cmdCab.Parameters.AddWithValue("@Id", id);

            using var reader = cmdCab.ExecuteReader();
            if (!reader.Read())
                return NotFound(new { mensaje = $"Factura de compra {id} no encontrada." });

            var f = new FacturaCompra
            {
                Id             = reader.GetInt32(0),
                Fecha          = reader.GetString(1),
                TipoDoc        = reader.GetString(2),
                NumeroDoc      = reader.GetString(3),
                ProveedorId    = reader.GetInt32(4),
                OrdenCompra    = reader.GetString(5),
                CondVenta      = reader.GetString(6),
                TotalNeto      = reader.GetInt32(7),
                Iva            = reader.GetInt32(8),
                Total          = reader.GetInt32(9),
                Estado         = reader.GetString(10),
                FechaRecepcion = reader.IsDBNull(11) ? null : reader.GetString(11),
                Vencimiento    = reader.IsDBNull(12) ? null : reader.GetString(12),
                Usuario        = reader.GetString(13),
            };
            reader.Close();

            var cmdDet = new SqlCommand(@"
                SELECT Id, FacturaCompraId, ProductoId, Codigo, Descripcion,
                       Cantidad, PrecioNeto, PrecioMeson, PrecioMayor,
                       (Cantidad * PrecioNeto) AS Subtotal
                FROM FacturasCompraDetalle WHERE FacturaCompraId = @Id", conexion);
            cmdDet.Parameters.AddWithValue("@Id", id);

            using var rd = cmdDet.ExecuteReader();
            while (rd.Read())
            {
                f.Detalle.Add(new FacturaCompraDetalle
                {
                    Id              = rd.GetInt32(0),
                    FacturaCompraId = rd.GetInt32(1),
                    ProductoId      = rd.IsDBNull(2) ? null : rd.GetInt32(2),
                    Codigo          = rd.GetString(3),
                    Descripcion     = rd.GetString(4),
                    Cantidad        = rd.GetInt32(5),
                    PrecioNeto      = rd.GetInt32(6),
                    PrecioMeson     = rd.GetInt32(7),
                    PrecioMayor     = rd.GetInt32(8),
                    Subtotal        = rd.GetInt32(9),
                });
            }
            return Ok(f);
        }

        // POST /api/facturas-compra
        [HttpPost]
        public IActionResult Crear([FromBody] FacturaCompra f)
        {
            if (f.Detalle.Count == 0)
                return BadRequest(new { mensaje = "La factura debe tener al menos un producto." });
            if (f.ProveedorId <= 0)
                return BadRequest(new { mensaje = "Se requiere proveedor." });
            if (string.IsNullOrWhiteSpace(f.NumeroDoc))
                return BadRequest(new { mensaje = "Se requiere número de documento." });
            if (f.CondVenta == "CREDITO" && string.IsNullOrWhiteSpace(f.Vencimiento))
                return BadRequest(new { mensaje = "Las compras a crédito requieren fecha de vencimiento." });

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                // Validar proveedor
                var cmdProv = new SqlCommand("SELECT COUNT(*) FROM Proveedores WHERE Id = @Id", conexion, tx);
                cmdProv.Parameters.AddWithValue("@Id", f.ProveedorId);
                if ((int)cmdProv.ExecuteScalar() == 0)
                    throw new InvalidOperationException($"Proveedor {f.ProveedorId} no existe.");

                var cmdCab = new SqlCommand(@"
                    INSERT INTO FacturasCompra
                        (Fecha, TipoDoc, NumeroDoc, ProveedorId, OrdenCompra, CondVenta,
                         TotalNeto, Iva, Total, Estado, Vencimiento, Usuario)
                    VALUES
                        (GETDATE(), @TipoDoc, @NumeroDoc, @ProveedorId, @OrdenCompra, @CondVenta,
                         @TotalNeto, @Iva, @Total, 'VIGENTE', @Vencimiento, @Usuario)
                    ;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", conexion, tx);

                cmdCab.Parameters.AddWithValue("@TipoDoc",     f.TipoDoc);
                cmdCab.Parameters.AddWithValue("@NumeroDoc",   f.NumeroDoc);
                cmdCab.Parameters.AddWithValue("@ProveedorId", f.ProveedorId);
                cmdCab.Parameters.AddWithValue("@OrdenCompra", f.OrdenCompra ?? "");
                cmdCab.Parameters.AddWithValue("@CondVenta",   f.CondVenta);
                cmdCab.Parameters.AddWithValue("@TotalNeto",   f.TotalNeto);
                cmdCab.Parameters.AddWithValue("@Iva",         f.Iva);
                cmdCab.Parameters.AddWithValue("@Total",       f.Total);
                cmdCab.Parameters.AddWithValue("@Usuario",     f.Usuario);
                cmdCab.Parameters.AddWithValue("@Vencimiento",
                    string.IsNullOrWhiteSpace(f.Vencimiento) ? DBNull.Value : (object)DateTime.Parse(f.Vencimiento));

                f.Id = Convert.ToInt32(cmdCab.ExecuteScalar());

                foreach (var item in f.Detalle)
                {
                    var cmdDet = new SqlCommand(@"
                        INSERT INTO FacturasCompraDetalle
                            (FacturaCompraId, ProductoId, Codigo, Descripcion, Cantidad, PrecioNeto, PrecioMeson, PrecioMayor)
                        VALUES
                            (@FacturaCompraId, @ProductoId, @Codigo, @Descripcion, @Cantidad, @PrecioNeto, @PrecioMeson, @PrecioMayor)",
                        conexion, tx);

                    cmdDet.Parameters.AddWithValue("@FacturaCompraId", f.Id);
                    cmdDet.Parameters.AddWithValue("@ProductoId",      (object?)item.ProductoId ?? DBNull.Value);
                    cmdDet.Parameters.AddWithValue("@Codigo",          item.Codigo);
                    cmdDet.Parameters.AddWithValue("@Descripcion",     item.Descripcion);
                    cmdDet.Parameters.AddWithValue("@Cantidad",        item.Cantidad);
                    cmdDet.Parameters.AddWithValue("@PrecioNeto",      item.PrecioNeto);
                    cmdDet.Parameters.AddWithValue("@PrecioMeson",     item.PrecioMeson);
                    cmdDet.Parameters.AddWithValue("@PrecioMayor",     item.PrecioMayor);
                    cmdDet.ExecuteNonQuery();
                }

                tx.Commit();
                return CreatedAtAction(nameof(ObtenerPorId), new { id = f.Id }, f);
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al guardar la factura de compra.", detalle = ex.Message });
            }
        }

        // PUT /api/facturas-compra/5/recepcionar?bodega=VINA
        // SUMA el stock al inventario y opcionalmente actualiza precios sugeridos.
        // Esta es la operación clave del módulo: la compra entra a inventario.
        [HttpPut("{id}/recepcionar")]
        [RequireRol("ADMIN")]
        public IActionResult Recepcionar(int id, [FromQuery] string bodega = "VINA")
        {
            var bodegaCol = bodega.Equals("VALEMANA", StringComparison.OrdinalIgnoreCase) ? "StockVa" : "StockVina";

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                // Verificar estado
                var cmdEstado = new SqlCommand(
                    "SELECT Estado FROM FacturasCompra WHERE Id = @Id", conexion, tx);
                cmdEstado.Parameters.AddWithValue("@Id", id);
                var estado = cmdEstado.ExecuteScalar() as string;
                if (estado == null)
                    return NotFound(new { mensaje = $"Factura {id} no encontrada." });
                if (estado != "VIGENTE")
                    return BadRequest(new { mensaje = $"La factura ya está en estado '{estado}'." });

                // Iterar cada item del detalle y sumar stock + actualizar precios
                var cmdItems = new SqlCommand(
                    @"SELECT ProductoId, Cantidad, PrecioNeto, PrecioMeson, PrecioMayor
                      FROM FacturasCompraDetalle WHERE FacturaCompraId = @Id",
                    conexion, tx);
                cmdItems.Parameters.AddWithValue("@Id", id);

                var items = new List<(int? prodId, int cant, int pNeto, int pMeson, int pMayor)>();
                using (var rd = cmdItems.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        items.Add((
                            rd.IsDBNull(0) ? null : rd.GetInt32(0),
                            rd.GetInt32(1), rd.GetInt32(2), rd.GetInt32(3), rd.GetInt32(4)
                        ));
                    }
                }

                int actualizados = 0;
                foreach (var (prodId, cant, pNeto, pMeson, pMayor) in items)
                {
                    if (prodId == null) continue;  // si no tiene producto vinculado, no se puede sumar stock

                    // CASE WHEN > 0: si la factura no trae el precio/costo, NO lo pisamos
                    // (preservamos el valor existente del inventario)
                    var cmdStock = new SqlCommand(
                        $@"UPDATE Inventario SET
                              {bodegaCol} = COALESCE({bodegaCol}, 0) + @Cant,
                              CostoNeto   = CASE WHEN @CostoNeto > 0 THEN @CostoNeto ELSE CostoNeto END,
                              PrecioMeson = CASE WHEN @PMeson > 0 THEN @PMeson ELSE PrecioMeson END,
                              PrecioMayor = CASE WHEN @PMayor > 0 THEN @PMayor ELSE PrecioMayor END
                           WHERE Id = @Id",
                        conexion, tx);
                    cmdStock.Parameters.AddWithValue("@Cant",      cant);
                    cmdStock.Parameters.AddWithValue("@CostoNeto", pNeto);
                    cmdStock.Parameters.AddWithValue("@PMeson",    pMeson);
                    cmdStock.Parameters.AddWithValue("@PMayor",    pMayor);
                    cmdStock.Parameters.AddWithValue("@Id",        prodId);
                    actualizados += cmdStock.ExecuteNonQuery();
                }

                // Marcar la factura como recepcionada y GUARDAR la bodega
                // (sin esto, anular después no sabría dónde reversar)
                var cmdUpd = new SqlCommand(@"
                    UPDATE FacturasCompra
                    SET Estado = 'RECEPCIONADA',
                        FechaRecepcion = CAST(GETDATE() AS DATE),
                        BodegaRecepcion = @Bodega
                    WHERE Id = @Id", conexion, tx);
                cmdUpd.Parameters.AddWithValue("@Id",     id);
                cmdUpd.Parameters.AddWithValue("@Bodega", bodega.ToUpperInvariant());
                cmdUpd.ExecuteNonQuery();

                tx.Commit();
                return Ok(new { mensaje = $"Factura recepcionada. Se actualizó stock de {actualizados} producto(s).", productosActualizados = actualizados });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al recepcionar.", detalle = ex.Message });
            }
        }

        // PUT /api/facturas-compra/5/pagar
        [HttpPut("{id}/pagar")]
        [RequireRol("ADMIN")]
        public IActionResult Pagar(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            var cmd = new SqlCommand(
                "UPDATE FacturasCompra SET Estado = 'PAGADA' WHERE Id = @Id AND Estado <> 'ANULADA'",
                conexion);
            cmd.Parameters.AddWithValue("@Id", id);
            if (cmd.ExecuteNonQuery() == 0)
                return NotFound(new { mensaje = $"Factura {id} no encontrada o ya está anulada." });
            return NoContent();
        }

        // PUT /api/facturas-compra/5/anular[?bodega=VINA]
        // Si la factura ya fue RECEPCIONADA, reversa el stock automáticamente
        // usando la BodegaRecepcion guardada al recepcionar. El parámetro `bodega`
        // queda como override por si una factura recepcionada antes de esta versión
        // tiene BodegaRecepcion en NULL (legacy data).
        [HttpPut("{id}/anular")]
        [RequireRol("ADMIN")]
        public IActionResult Anular(int id, [FromQuery] string? bodega = null)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                var cmdGet = new SqlCommand(
                    "SELECT Estado, BodegaRecepcion FROM FacturasCompra WHERE Id = @Id",
                    conexion, tx);
                cmdGet.Parameters.AddWithValue("@Id", id);
                string? estado = null, bodegaRec = null;
                using (var rd = cmdGet.ExecuteReader())
                {
                    if (!rd.Read())
                        return NotFound(new { mensaje = $"Factura {id} no encontrada." });
                    estado    = rd.GetString(0);
                    bodegaRec = rd.IsDBNull(1) ? null : rd.GetString(1);
                }
                if (estado == "ANULADA")
                    return BadRequest(new { mensaje = "La factura ya estaba anulada." });

                int revertidos = 0;
                string? bodegaUsada = null;

                // Si está RECEPCIONADA, hay stock que devolver
                if (estado == "RECEPCIONADA")
                {
                    // Prioridad: bodega del query (override) > BodegaRecepcion guardada
                    var bodegaFinal = (bodega ?? bodegaRec)?.ToUpperInvariant();
                    var bodegaCol = bodegaFinal switch
                    {
                        "VINA"     => "StockVina",
                        "VALEMANA" => "StockVa",
                        _          => null
                    };
                    if (bodegaCol == null)
                        return BadRequest(new {
                            mensaje = "Esta factura fue recepcionada pero no tiene bodega registrada (data legacy). " +
                                      "Indica la bodega manualmente: ?bodega=VINA o ?bodega=VALEMANA."
                        });
                    bodegaUsada = bodegaFinal;

                    var cmdRev = new SqlCommand($@"
                        UPDATE i
                        SET i.{bodegaCol} = COALESCE(i.{bodegaCol}, 0) - d.Cantidad
                        FROM Inventario i
                        INNER JOIN FacturasCompraDetalle d ON d.ProductoId = i.Id
                        WHERE d.FacturaCompraId = @Id AND d.ProductoId IS NOT NULL",
                        conexion, tx);
                    cmdRev.Parameters.AddWithValue("@Id", id);
                    revertidos = cmdRev.ExecuteNonQuery();
                }

                var cmdUpd = new SqlCommand(
                    "UPDATE FacturasCompra SET Estado = 'ANULADA' WHERE Id = @Id",
                    conexion, tx);
                cmdUpd.Parameters.AddWithValue("@Id", id);
                cmdUpd.ExecuteNonQuery();

                tx.Commit();
                return Ok(new {
                    mensaje = revertidos > 0
                        ? $"Factura anulada. Se reversó stock de {revertidos} producto(s) de {bodegaUsada}."
                        : "Factura anulada (no había stock que reversar)."
                });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al anular la factura.", detalle = ex.Message });
            }
        }
    }
}
