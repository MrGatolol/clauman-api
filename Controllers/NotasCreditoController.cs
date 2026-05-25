using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaumanAPI.Models;
using ClaumanAPI.Middleware;

namespace ClaumanAPI.Controllers
{
    [Route("api/notas-credito")]
    [ApiController]
    public class NotasCreditoController : ControllerBase
    {
        private readonly string _conexion;

        public NotasCreditoController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/notas-credito
        [HttpGet]
        public IActionResult ObtenerTodas()
        {
            var lista = new List<NotaCredito>();
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(@"
                SELECT Id, Numero,
                       FORMAT(Fecha, 'dd/MM/yyyy') AS Fecha,
                       CONVERT(VARCHAR(8), Hora, 108) AS Hora,
                       TipoDocOrigen, DocOrigenId, DocOrigenNumero,
                       ClienteId, Total, Motivo, Usuario, Bodega, Anulada
                FROM NotasCredito
                ORDER BY Id DESC", conexion);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                lista.Add(new NotaCredito
                {
                    Id              = rd.GetInt32(0),
                    Numero          = rd.GetInt32(1),
                    Fecha           = rd.GetString(2),
                    Hora            = rd.GetString(3),
                    TipoDocOrigen   = rd.GetString(4),
                    DocOrigenId     = rd.GetInt32(5),
                    DocOrigenNumero = rd.GetInt32(6),
                    ClienteId       = rd.IsDBNull(7) ? null : rd.GetInt32(7),
                    Total           = rd.GetInt32(8),
                    Motivo          = rd.GetString(9),
                    Usuario         = rd.GetString(10),
                    Bodega          = rd.GetString(11),
                    Anulada         = rd.GetBoolean(12),
                });
            }
            return Ok(lista);
        }

        // GET /api/notas-credito/5 con detalle
        [HttpGet("{id}")]
        public IActionResult ObtenerPorId(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmdCab = new SqlCommand(@"
                SELECT Id, Numero,
                       FORMAT(Fecha, 'dd/MM/yyyy'),
                       CONVERT(VARCHAR(8), Hora, 108),
                       TipoDocOrigen, DocOrigenId, DocOrigenNumero,
                       ClienteId, Total, Motivo, Usuario, Bodega, Anulada
                FROM NotasCredito WHERE Id = @Id", conexion);
            cmdCab.Parameters.AddWithValue("@Id", id);

            using var rd = cmdCab.ExecuteReader();
            if (!rd.Read()) return NotFound(new { mensaje = $"Nota de Crédito {id} no encontrada." });

            var nc = new NotaCredito
            {
                Id              = rd.GetInt32(0),
                Numero          = rd.GetInt32(1),
                Fecha           = rd.GetString(2),
                Hora            = rd.GetString(3),
                TipoDocOrigen   = rd.GetString(4),
                DocOrigenId     = rd.GetInt32(5),
                DocOrigenNumero = rd.GetInt32(6),
                ClienteId       = rd.IsDBNull(7) ? null : rd.GetInt32(7),
                Total           = rd.GetInt32(8),
                Motivo          = rd.GetString(9),
                Usuario         = rd.GetString(10),
                Bodega          = rd.GetString(11),
                Anulada         = rd.GetBoolean(12),
            };
            rd.Close();

            var cmdDet = new SqlCommand(@"
                SELECT Id, NotaCreditoId, ProductoId, Codigo, Descripcion,
                       Cantidad, PrecioUnitario, (Cantidad * PrecioUnitario) AS Subtotal
                FROM NotasCreditoDetalle WHERE NotaCreditoId = @Id", conexion);
            cmdDet.Parameters.AddWithValue("@Id", id);
            using var rdDet = cmdDet.ExecuteReader();
            while (rdDet.Read())
            {
                nc.Detalle.Add(new NotaCreditoDetalle
                {
                    Id             = rdDet.GetInt32(0),
                    NotaCreditoId  = rdDet.GetInt32(1),
                    ProductoId     = rdDet.IsDBNull(2) ? null : rdDet.GetInt32(2),
                    Codigo         = rdDet.GetString(3),
                    Descripcion    = rdDet.GetString(4),
                    Cantidad       = rdDet.GetInt32(5),
                    PrecioUnitario = rdDet.GetInt32(6),
                    Subtotal       = rdDet.GetInt32(7),
                });
            }
            return Ok(nc);
        }

        // POST /api/notas-credito
        // Crea la NC, devuelve el stock al inventario, y marca el doc origen como ANULADA.
        // Por ahora solo soporta reversión TOTAL (devuelve todos los items del doc original).
        // Para parcial habría que pedir cantidades específicas por item.
        [HttpPost]
        [RequirePermiso("ventas.crearBoleta")]   // mismo permiso que vender — quien vende puede reversar
        public IActionResult Crear([FromBody] NotaCredito nc)
        {
            if (string.IsNullOrWhiteSpace(nc.Motivo))
                return BadRequest(new { mensaje = "El motivo es requerido." });
            if (nc.DocOrigenId <= 0)
                return BadRequest(new { mensaje = "Se requiere el documento origen." });

            var tipoOk = nc.TipoDocOrigen is "BOLETA" or "FACTURA" or "NOTA_VENTA";
            if (!tipoOk)
                return BadRequest(new { mensaje = "Tipo de documento origen inválido." });

            // Mapeo: tabla de cabecera, tabla detalle, columna FK, columna estado
            var (tablaDoc, tablaDet, colFk, columnaEstado, valorAnulada) = nc.TipoDocOrigen switch
            {
                "BOLETA"     => ("Boletas",    "BoletasDetalle",    "BoletaId",    "Anulada", "1"),
                "FACTURA"    => ("Facturas",   "FacturasDetalle",   "FacturaId",   "Estado",  "'ANULADA'"),
                _            => ("NotasVenta", "NotasVentaDetalle", "NotaVentaId", "Estado",  "'ANULADA'"),
            };

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                // 1. Verificar que el doc origen exista y NO esté ya anulado
                string condicionVigente = nc.TipoDocOrigen == "BOLETA"
                    ? "Anulada = 0"
                    : "Estado <> 'ANULADA'";
                var cmdCheck = new SqlCommand(
                    $"SELECT Numero, COALESCE(Bodega, 'VINA') FROM {tablaDoc} WHERE Id = @Id AND {condicionVigente}",
                    conexion, tx);
                cmdCheck.Parameters.AddWithValue("@Id", nc.DocOrigenId);
                int docNumero;
                string bodega;
                using (var r = cmdCheck.ExecuteReader())
                {
                    if (!r.Read())
                        return BadRequest(new { mensaje = $"El {nc.TipoDocOrigen} {nc.DocOrigenId} no existe o ya está anulado." });
                    docNumero = r.GetInt32(0);
                    bodega = r.GetString(1);
                }
                nc.DocOrigenNumero = docNumero;
                nc.Bodega = bodega;
                string colStock = bodega.Equals("VALEMANA", StringComparison.OrdinalIgnoreCase) ? "StockVa" : "StockVina";

                // 2. Cargar los items del doc origen (son los que devolveremos)
                var items = new List<NotaCreditoDetalle>();
                int totalReversado = 0;
                var cmdItems = new SqlCommand(
                    $"SELECT ProductoId, Codigo, Descripcion, Cantidad, PrecioUnitario FROM {tablaDet} WHERE {colFk} = @Id",
                    conexion, tx);
                cmdItems.Parameters.AddWithValue("@Id", nc.DocOrigenId);
                using (var r = cmdItems.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var item = new NotaCreditoDetalle
                        {
                            ProductoId     = r.IsDBNull(0) ? null : r.GetInt32(0),
                            Codigo         = r.GetString(1),
                            Descripcion    = r.GetString(2),
                            Cantidad       = r.GetInt32(3),
                            PrecioUnitario = r.GetInt32(4),
                        };
                        totalReversado += item.Cantidad * item.PrecioUnitario;
                        items.Add(item);
                    }
                }
                if (items.Count == 0)
                    return BadRequest(new { mensaje = "El documento origen no tiene items para reversar." });

                // 3. Reservar número correlativo de NC con bloqueo
                var cmdNum = new SqlCommand(
                    "SELECT COALESCE(MAX(Numero), 999) + 1 FROM NotasCredito WITH (TABLOCKX, HOLDLOCK)",
                    conexion, tx);
                nc.Numero = Convert.ToInt32(cmdNum.ExecuteScalar());

                // 4. Insertar cabecera de NC
                var cmdCab = new SqlCommand(@"
                    INSERT INTO NotasCredito
                        (Numero, Fecha, Hora, TipoDocOrigen, DocOrigenId, DocOrigenNumero,
                         ClienteId, Total, Motivo, Usuario, Bodega, Anulada)
                    VALUES
                        (@Numero, GETDATE(), CAST(GETDATE() AS TIME), @TipoDocOrigen, @DocOrigenId, @DocOrigenNumero,
                         @ClienteId, @Total, @Motivo, @Usuario, @Bodega, 0)
                    ;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", conexion, tx);

                cmdCab.Parameters.AddWithValue("@Numero",         nc.Numero);
                cmdCab.Parameters.AddWithValue("@TipoDocOrigen",  nc.TipoDocOrigen);
                cmdCab.Parameters.AddWithValue("@DocOrigenId",    nc.DocOrigenId);
                cmdCab.Parameters.AddWithValue("@DocOrigenNumero",nc.DocOrigenNumero);
                cmdCab.Parameters.AddWithValue("@ClienteId",      (object?)nc.ClienteId ?? DBNull.Value);
                cmdCab.Parameters.AddWithValue("@Total",          totalReversado);
                cmdCab.Parameters.AddWithValue("@Motivo",         nc.Motivo);
                cmdCab.Parameters.AddWithValue("@Usuario",        nc.Usuario);
                cmdCab.Parameters.AddWithValue("@Bodega",         nc.Bodega);

                nc.Id = Convert.ToInt32(cmdCab.ExecuteScalar());
                nc.Total = totalReversado;
                nc.Detalle = items;

                // 5. Insertar detalle + devolver stock por cada item
                foreach (var item in items)
                {
                    var cmdDet = new SqlCommand(@"
                        INSERT INTO NotasCreditoDetalle
                            (NotaCreditoId, ProductoId, Codigo, Descripcion, Cantidad, PrecioUnitario)
                        VALUES
                            (@NCId, @ProductoId, @Codigo, @Descripcion, @Cantidad, @PrecioUnitario)",
                        conexion, tx);
                    cmdDet.Parameters.AddWithValue("@NCId",           nc.Id);
                    cmdDet.Parameters.AddWithValue("@ProductoId",     (object?)item.ProductoId ?? DBNull.Value);
                    cmdDet.Parameters.AddWithValue("@Codigo",         item.Codigo);
                    cmdDet.Parameters.AddWithValue("@Descripcion",    item.Descripcion);
                    cmdDet.Parameters.AddWithValue("@Cantidad",       item.Cantidad);
                    cmdDet.Parameters.AddWithValue("@PrecioUnitario", item.PrecioUnitario);
                    cmdDet.ExecuteNonQuery();

                    if (item.ProductoId != null)
                    {
                        var cmdStock = new SqlCommand(
                            $"UPDATE Inventario SET {colStock} = COALESCE({colStock}, 0) + @Cant WHERE Id = @Id",
                            conexion, tx);
                        cmdStock.Parameters.AddWithValue("@Cant", item.Cantidad);
                        cmdStock.Parameters.AddWithValue("@Id",   item.ProductoId.Value);
                        cmdStock.ExecuteNonQuery();
                    }
                }

                // 6. Marcar el doc origen como ANULADA
                var cmdAnular = new SqlCommand(
                    $"UPDATE {tablaDoc} SET {columnaEstado} = {valorAnulada} WHERE Id = @Id",
                    conexion, tx);
                cmdAnular.Parameters.AddWithValue("@Id", nc.DocOrigenId);
                cmdAnular.ExecuteNonQuery();

                tx.Commit();
                return CreatedAtAction(nameof(ObtenerPorId), new { id = nc.Id }, nc);
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al crear la nota de crédito.", detalle = ex.Message });
            }
        }
    }
}
