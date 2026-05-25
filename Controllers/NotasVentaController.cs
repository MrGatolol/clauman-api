using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaumanAPI.Models;
using ClaumanAPI.Middleware;

namespace ClaumanAPI.Controllers
{
    [Route("api/notas-venta")]
    [ApiController]
    public class NotasVentaController : ControllerBase
    {
        private readonly string _conexion;

        public NotasVentaController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/notas-venta
        [HttpGet]
        public IActionResult ObtenerTodas()
        {
            var lista = new List<NotaVenta>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(@"
                SELECT Id, Numero,
                       FORMAT(Fecha, 'dd/MM/yyyy') AS Fecha,
                       CONVERT(VARCHAR(8), Hora, 108)  AS Hora,
                       ClienteId, ClienteRef, CondVenta, MedioPago,
                       DescGlobal, TotalNeto, Iva, Total, Estado, Usuario
                FROM NotasVenta
                ORDER BY Id DESC", conexion);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new NotaVenta
                {
                    Id         = reader.GetInt32(0),
                    Numero     = reader.GetInt32(1),
                    Fecha      = reader.GetString(2),
                    Hora       = reader.GetString(3),
                    ClienteId  = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    ClienteRef = reader.GetString(5),
                    CondVenta  = reader.GetString(6),
                    MedioPago  = reader.GetString(7),
                    DescGlobal = reader.GetInt32(8),
                    TotalNeto  = reader.GetInt32(9),
                    Iva        = reader.GetInt32(10),
                    Total      = reader.GetInt32(11),
                    Estado     = reader.GetString(12),
                    Usuario    = reader.GetString(13),
                });
            }
            return Ok(lista);
        }

        // GET /api/notas-venta/5  — con detalle
        [HttpGet("{id}")]
        public IActionResult ObtenerPorId(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmdCab = new SqlCommand(@"
                SELECT Id, Numero,
                       FORMAT(Fecha, 'dd/MM/yyyy'),
                       CONVERT(VARCHAR(8), Hora, 108),
                       ClienteId, ClienteRef, CondVenta, MedioPago,
                       DescGlobal, TotalNeto, Iva, Total, Estado, Usuario
                FROM NotasVenta WHERE Id = @Id", conexion);
            cmdCab.Parameters.AddWithValue("@Id", id);

            using var reader = cmdCab.ExecuteReader();
            if (!reader.Read())
                return NotFound(new { mensaje = $"Nota de Venta {id} no encontrada." });

            var nv = new NotaVenta
            {
                Id         = reader.GetInt32(0),
                Numero     = reader.GetInt32(1),
                Fecha      = reader.GetString(2),
                Hora       = reader.GetString(3),
                ClienteId  = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                ClienteRef = reader.GetString(5),
                CondVenta  = reader.GetString(6),
                MedioPago  = reader.GetString(7),
                DescGlobal = reader.GetInt32(8),
                TotalNeto  = reader.GetInt32(9),
                Iva        = reader.GetInt32(10),
                Total      = reader.GetInt32(11),
                Estado     = reader.GetString(12),
                Usuario    = reader.GetString(13),
            };
            reader.Close();

            var cmdDet = new SqlCommand(@"
                SELECT Id, NotaVentaId, ProductoId, Codigo, Descripcion,
                       Cantidad, PrecioUnitario, (Cantidad * PrecioUnitario) AS Subtotal
                FROM NotasVentaDetalle WHERE NotaVentaId = @Id", conexion);
            cmdDet.Parameters.AddWithValue("@Id", id);

            using var rd = cmdDet.ExecuteReader();
            while (rd.Read())
            {
                nv.Detalle.Add(new NotaVentaDetalle
                {
                    Id             = rd.GetInt32(0),
                    NotaVentaId    = rd.GetInt32(1),
                    ProductoId     = rd.IsDBNull(2) ? null : rd.GetInt32(2),
                    Codigo         = rd.GetString(3),
                    Descripcion    = rd.GetString(4),
                    Cantidad       = rd.GetInt32(5),
                    PrecioUnitario = rd.GetInt32(6),
                    Subtotal       = rd.GetInt32(7),
                });
            }
            return Ok(nv);
        }

        // POST /api/notas-venta
        // Igual que Boletas: valida stock, reserva número con TABLOCKX, descuenta del inventario.
        [HttpPost]
        [RequirePermiso("ventas.crearNVenta")]
        public IActionResult Crear([FromBody] NotaVenta nv)
        {
            if (nv.Detalle.Count == 0)
                return BadRequest(new { mensaje = "La nota de venta debe tener al menos un producto." });

            foreach (var item in nv.Detalle)
            {
                if (item.Cantidad <= 0)
                    return BadRequest(new { mensaje = $"La cantidad de '{item.Codigo}' debe ser mayor a 0." });
                if (item.PrecioUnitario < 0)
                    return BadRequest(new { mensaje = $"El precio de '{item.Codigo}' no puede ser negativo." });
                if (string.IsNullOrWhiteSpace(item.Codigo))
                    return BadRequest(new { mensaje = "Hay items sin código." });
            }
            if (nv.Total < 0 || nv.TotalNeto < 0 || nv.Iva < 0 || nv.DescGlobal < 0)
                return BadRequest(new { mensaje = "Los totales (Total, TotalNeto, Iva, DescGlobal) no pueden ser negativos." });

            var bodega = nv.Bodega?.ToUpper() ?? "VINA";
            if (bodega != "VINA" && bodega != "VALEMANA")
                return BadRequest(new { mensaje = "Bodega inválida. Debe ser VINA o VALEMANA." });

            string colStock = bodega == "VALEMANA" ? "StockVa" : "StockVina";

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                // Validar ClienteId si vino con valor
                if (nv.ClienteId.HasValue)
                {
                    var cmdCli = new SqlCommand(
                        "SELECT COUNT(*) FROM Clientes WHERE Id = @Id", conexion, tx);
                    cmdCli.Parameters.AddWithValue("@Id", nv.ClienteId.Value);
                    if ((int)cmdCli.ExecuteScalar() == 0)
                        return BadRequest(new { mensaje = $"El cliente {nv.ClienteId.Value} no existe." });
                }

                // Validar stock disponible
                foreach (var item in nv.Detalle)
                {
                    if (item.ProductoId == null) continue;
                    var cmdStockActual = new SqlCommand(
                        $"SELECT COALESCE({colStock}, 0) FROM Inventario WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id", conexion, tx);
                    cmdStockActual.Parameters.AddWithValue("@Id", item.ProductoId.Value);
                    var disponible = (int)(cmdStockActual.ExecuteScalar() ?? 0);
                    if (disponible < item.Cantidad)
                        return BadRequest(new {
                            mensaje = $"Stock insuficiente en {bodega} para '{item.Codigo}'. " +
                                      $"Disponible: {disponible}, requerido: {item.Cantidad}."
                        });
                }

                // Número correlativo con bloqueo exclusivo
                var cmdNum = new SqlCommand(
                    "SELECT COALESCE(MAX(Numero), 39599) + 1 FROM NotasVenta WITH (TABLOCKX, HOLDLOCK)",
                    conexion, tx);
                nv.Numero = Convert.ToInt32(cmdNum.ExecuteScalar());

                var cmdCab = new SqlCommand(@"
                    INSERT INTO NotasVenta
                        (Numero, Fecha, Hora, ClienteId, ClienteRef, CondVenta, MedioPago,
                         DescGlobal, TotalNeto, Iva, Total, Estado, Usuario, Bodega)
                    OUTPUT INSERTED.Id,
                           FORMAT(INSERTED.Fecha, 'dd/MM/yyyy') AS Fecha,
                           CONVERT(VARCHAR(8), INSERTED.Hora, 108) AS Hora
                    VALUES
                        (@Numero, GETDATE(), CAST(GETDATE() AS TIME), @ClienteId, @ClienteRef, @CondVenta, @MedioPago,
                         @DescGlobal, @TotalNeto, @Iva, @Total, 'VIGENTE', @Usuario, @Bodega)", conexion, tx);

                cmdCab.Parameters.AddWithValue("@Numero",     nv.Numero);
                cmdCab.Parameters.AddWithValue("@ClienteId",  (object?)nv.ClienteId ?? DBNull.Value);
                cmdCab.Parameters.AddWithValue("@ClienteRef", nv.ClienteRef ?? "");
                cmdCab.Parameters.AddWithValue("@CondVenta",  nv.CondVenta);
                cmdCab.Parameters.AddWithValue("@MedioPago",  nv.MedioPago);
                cmdCab.Parameters.AddWithValue("@DescGlobal", nv.DescGlobal);
                cmdCab.Parameters.AddWithValue("@TotalNeto",  nv.TotalNeto);
                cmdCab.Parameters.AddWithValue("@Iva",        nv.Iva);
                cmdCab.Parameters.AddWithValue("@Total",      nv.Total);
                cmdCab.Parameters.AddWithValue("@Usuario",    nv.Usuario);
                cmdCab.Parameters.AddWithValue("@Bodega",     bodega);

                using (var rd = cmdCab.ExecuteReader())
                {
                    rd.Read();
                    nv.Id    = rd.GetInt32(0);
                    nv.Fecha = rd.GetString(1);
                    nv.Hora  = rd.GetString(2);
                }

                foreach (var item in nv.Detalle)
                {
                    var cmdDet = new SqlCommand(@"
                        INSERT INTO NotasVentaDetalle
                            (NotaVentaId, ProductoId, Codigo, Descripcion, Cantidad, PrecioUnitario)
                        VALUES
                            (@NotaVentaId, @ProductoId, @Codigo, @Descripcion, @Cantidad, @PrecioUnitario)",
                        conexion, tx);

                    cmdDet.Parameters.AddWithValue("@NotaVentaId",    nv.Id);
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
                return CreatedAtAction(nameof(ObtenerPorId), new { id = nv.Id }, nv);
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al guardar la nota de venta.", detalle = ex.Message });
            }
        }

        // PUT /api/notas-venta/5/anular
        // Marca anulada + devuelve stock.
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
                    "SELECT Estado, COALESCE(Bodega, 'VINA') FROM NotasVenta WHERE Id = @Id",
                    conexion, tx);
                cmdGet.Parameters.AddWithValue("@Id", id);

                string estado, bodega;
                using (var rd = cmdGet.ExecuteReader())
                {
                    if (!rd.Read()) return NotFound(new { mensaje = $"Nota de venta {id} no encontrada." });
                    estado = rd.GetString(0);
                    bodega = rd.GetString(1);
                }
                if (estado != "VIGENTE")
                    return BadRequest(new { mensaje = $"La nota está en estado '{estado}', no se puede anular." });

                string colStock = bodega.Equals("VALEMANA", StringComparison.OrdinalIgnoreCase) ? "StockVa" : "StockVina";

                var cmdDevolver = new SqlCommand($@"
                    UPDATE i
                    SET i.{colStock} = COALESCE(i.{colStock}, 0) + d.Cantidad
                    FROM Inventario i
                    INNER JOIN NotasVentaDetalle d ON d.ProductoId = i.Id
                    WHERE d.NotaVentaId = @Id AND d.ProductoId IS NOT NULL", conexion, tx);
                cmdDevolver.Parameters.AddWithValue("@Id", id);
                int devueltos = cmdDevolver.ExecuteNonQuery();

                var cmdAnular = new SqlCommand(
                    "UPDATE NotasVenta SET Estado = 'ANULADA' WHERE Id = @Id", conexion, tx);
                cmdAnular.Parameters.AddWithValue("@Id", id);
                cmdAnular.ExecuteNonQuery();

                tx.Commit();
                return Ok(new { mensaje = $"Nota anulada. Stock devuelto a {bodega} para {devueltos} producto(s)." });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al anular.", detalle = ex.Message });
            }
        }

        // PUT /api/notas-venta/5/facturar
        // Genera una factura REAL a partir de la nota de venta:
        //  1. Copia cabecera (cliente, totales, bodega) y detalle a Facturas/FacturasDetalle.
        //  2. Reserva Numero + Folio de Factura con bloqueo correlativo.
        //  3. Marca la NV como 'FACTURADA' (NO mueve stock — ya se movió al crear la NV).
        // Requiere que la NV tenga ClienteId (las facturas no pueden ser sin cliente).
        [HttpPut("{id}/facturar")]
        [RequirePermiso("ventas.crearFactura")]
        public IActionResult Facturar(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();
            try
            {
                // 1) Leer cabecera de la NV
                var cmdNv = new SqlCommand(@"
                    SELECT Estado, ClienteId, ClienteRef, CondVenta, DescGlobal, TotalNeto, Iva, Total, Usuario, Bodega
                    FROM NotasVenta WHERE Id = @Id", conexion, tx);
                cmdNv.Parameters.AddWithValue("@Id", id);

                string? estado = null, condVenta = null, usuario = null, bodega = null, clienteRef = null;
                int? clienteId = null;
                int descGlobal = 0, totalNeto = 0, iva = 0, total = 0;

                using (var rd = cmdNv.ExecuteReader())
                {
                    if (!rd.Read())
                        return NotFound(new { mensaje = $"Nota de venta {id} no encontrada." });
                    estado     = rd.GetString(0);
                    clienteId  = rd.IsDBNull(1) ? null : rd.GetInt32(1);
                    clienteRef = rd.IsDBNull(2) ? "" : rd.GetString(2);
                    condVenta  = rd.GetString(3);
                    descGlobal = rd.GetInt32(4);
                    totalNeto  = rd.GetInt32(5);
                    iva        = rd.GetInt32(6);
                    total      = rd.GetInt32(7);
                    usuario    = rd.GetString(8);
                    bodega     = rd.GetString(9);
                }

                if (estado != "VIGENTE")
                    return BadRequest(new { mensaje = $"La nota ya está en estado '{estado}', no se puede facturar." });
                if (!clienteId.HasValue)
                    return BadRequest(new { mensaje = "La nota no tiene cliente asociado — una factura requiere cliente." });

                // 2) Reservar próximo Numero + Folio de Factura
                var cmdNum = new SqlCommand(
                    "SELECT COALESCE(MAX(Numero), 999) + 1, COALESCE(MAX(Folio), 1999999) + 1 FROM Facturas WITH (TABLOCKX, HOLDLOCK)",
                    conexion, tx);
                int numero, folio;
                using (var rd = cmdNum.ExecuteReader())
                {
                    rd.Read();
                    numero = rd.GetInt32(0);
                    folio  = rd.GetInt32(1);
                }

                // 3) Insertar cabecera de la factura
                var cmdCab = new SqlCommand(@"
                    INSERT INTO Facturas
                        (Numero, Folio, Fecha, Hora, ClienteId, CondVenta, OrdenCompra,
                         DescGlobal, TotalNeto, Iva, Total, Estado, Usuario, Vencimiento, Bodega)
                    VALUES
                        (@Numero, @Folio, GETDATE(), CAST(GETDATE() AS TIME), @ClienteId, @CondVenta, '',
                         @DescGlobal, @TotalNeto, @Iva, @Total, 'VIGENTE', @Usuario, NULL, @Bodega)
                    ;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", conexion, tx);
                cmdCab.Parameters.AddWithValue("@Numero",     numero);
                cmdCab.Parameters.AddWithValue("@Folio",      folio);
                cmdCab.Parameters.AddWithValue("@ClienteId",  clienteId.Value);
                cmdCab.Parameters.AddWithValue("@CondVenta",  condVenta);
                cmdCab.Parameters.AddWithValue("@DescGlobal", descGlobal);
                cmdCab.Parameters.AddWithValue("@TotalNeto",  totalNeto);
                cmdCab.Parameters.AddWithValue("@Iva",        iva);
                cmdCab.Parameters.AddWithValue("@Total",      total);
                cmdCab.Parameters.AddWithValue("@Usuario",    usuario);
                cmdCab.Parameters.AddWithValue("@Bodega",     bodega);
                int facturaId = Convert.ToInt32(cmdCab.ExecuteScalar());

                // 4) Copiar detalle (sin tocar stock — ya se movió al crear la NV)
                var cmdCopiarDet = new SqlCommand(@"
                    INSERT INTO FacturasDetalle (FacturaId, ProductoId, Codigo, Descripcion, Cantidad, PrecioUnitario)
                    SELECT @FacturaId, ProductoId, Codigo, Descripcion, Cantidad, PrecioUnitario
                    FROM NotasVentaDetalle
                    WHERE NotaVentaId = @NvId", conexion, tx);
                cmdCopiarDet.Parameters.AddWithValue("@FacturaId", facturaId);
                cmdCopiarDet.Parameters.AddWithValue("@NvId",      id);
                int items = cmdCopiarDet.ExecuteNonQuery();

                // 5) Marcar la NV como FACTURADA
                var cmdUpd = new SqlCommand(
                    "UPDATE NotasVenta SET Estado = 'FACTURADA' WHERE Id = @Id",
                    conexion, tx);
                cmdUpd.Parameters.AddWithValue("@Id", id);
                cmdUpd.ExecuteNonQuery();

                tx.Commit();
                return Ok(new {
                    mensaje    = $"Factura {numero} (folio {folio}) creada desde nota de venta {id}. {items} ítem(s) copiados.",
                    facturaId,
                    numero,
                    folio
                });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al facturar la nota.", detalle = ex.Message });
            }
        }
    }
}
