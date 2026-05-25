using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaumanAPI.Models;
using System.Globalization;

namespace ClaumanAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportesController : ControllerBase
    {
        private readonly string _conexion;
        private static readonly string[] MESES = {
            "", "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
            "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
        };

        public ReportesController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/reportes/inventario-valorizado?categoria=CHEVROLET
        // Productos con stock > 0 y su valorización (stock × costo neto)
        [HttpGet("inventario-valorizado")]
        public IActionResult InventarioValorizado([FromQuery] string? categoria = null)
        {
            var lista = new List<ReporteInventarioItem>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var sql = @"
                SELECT i.Id,
                       COALESCE(i.Codigo, '') AS Codigo,
                       COALESCE(c.Nombre, '') AS Categoria,
                       COALESCE(i.Descripcion, '') AS Descripcion,
                       (COALESCE(i.StockVina, 0) + COALESCE(i.StockVa, 0)) AS Stock,
                       COALESCE(i.CostoNeto, 0) AS CostoNeto,
                       (COALESCE(i.StockVina, 0) + COALESCE(i.StockVa, 0)) * COALESCE(i.CostoNeto, 0) AS Subtotal
                FROM Inventario i
                LEFT JOIN Categorias c ON c.Id = i.CategoriaId
                WHERE (COALESCE(i.StockVina, 0) + COALESCE(i.StockVa, 0)) > 0
                  AND (CAST(@Categoria AS NVARCHAR(MAX)) IS NULL OR c.Nombre = CAST(@Categoria AS NVARCHAR(MAX)))
                ORDER BY i.Descripcion";

            var cmd = new SqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@Categoria",
                string.IsNullOrWhiteSpace(categoria) ? DBNull.Value : (object)categoria);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new ReporteInventarioItem
                {
                    Id          = reader.GetInt32(0),
                    Codigo      = reader.GetString(1),
                    Categoria   = reader.GetString(2),
                    Descripcion = reader.GetString(3),
                    Stock       = reader.GetInt32(4),
                    CostoNeto   = reader.GetInt32(5),
                    Subtotal    = reader.GetInt32(6),
                });
            }

            return Ok(lista);
        }

        // GET /api/reportes/ranking-productos?desde=2026-01-01&hasta=2026-12-31
        // TOP 50 productos más vendidos en boletas vigentes
        [HttpGet("ranking-productos")]
        public IActionResult RankingProductos(
            [FromQuery] DateTime? desde = null,
            [FromQuery] DateTime? hasta = null)
        {
            var lista = new List<ReporteRankingItem>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var sql = @"
                SELECT d.Codigo,
                       MAX(d.Descripcion) AS Descripcion,
                       SUM(d.Cantidad) AS CantidadVendida,
                       CAST(AVG(CAST(d.PrecioUnitario AS DECIMAL(12,2))) AS INT) AS PrecioPromedio
                FROM BoletasDetalle d
                INNER JOIN Boletas b ON b.Id = d.BoletaId
                WHERE b.Anulada = 0
                  AND (CAST(@Desde AS DATETIME2) IS NULL OR b.Fecha >= @Desde)
                  AND (CAST(@Hasta AS DATETIME2) IS NULL OR b.Fecha <= @Hasta)
                GROUP BY d.Codigo
                ORDER BY SUM(d.Cantidad) DESC
                OFFSET 0 ROWS FETCH NEXT 50 ROWS ONLY";

            var cmd = new SqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@Desde", (object?)desde ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Hasta", (object?)hasta ?? DBNull.Value);

            using var reader = cmd.ExecuteReader();
            int rank = 1;
            while (reader.Read())
            {
                lista.Add(new ReporteRankingItem
                {
                    Ranking         = rank++,
                    Codigo          = reader.GetString(0),
                    Descripcion     = reader.GetString(1),
                    CantidadVendida = reader.GetInt32(2),
                    PrecioPromedio  = reader.GetInt32(3),
                });
            }

            return Ok(lista);
        }

        // GET /api/reportes/ventas-categorias?desde=2026-01-01&hasta=2026-12-31
        // Suma de ventas agrupadas por categoría de producto
        [HttpGet("ventas-categorias")]
        public IActionResult VentasPorCategoria(
            [FromQuery] DateTime? desde = null,
            [FromQuery] DateTime? hasta = null)
        {
            var lista = new List<ReporteVentaCategoria>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var sql = @"
                SELECT COALESCE(c.Nombre, 'SIN CATEGORÍA') AS Categoria,
                       SUM(d.Subtotal) AS Subtotal
                FROM BoletasDetalle d
                INNER JOIN Boletas b ON b.Id = d.BoletaId
                LEFT JOIN Inventario i ON i.Id = d.ProductoId
                LEFT JOIN Categorias c ON c.Id = i.CategoriaId
                WHERE b.Anulada = 0
                  AND (CAST(@Desde AS DATETIME2) IS NULL OR b.Fecha >= @Desde)
                  AND (CAST(@Hasta AS DATETIME2) IS NULL OR b.Fecha <= @Hasta)
                GROUP BY c.Nombre
                ORDER BY SUM(d.Subtotal) DESC";

            var cmd = new SqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@Desde", (object?)desde ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Hasta", (object?)hasta ?? DBNull.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new ReporteVentaCategoria
                {
                    Categoria = reader.GetString(0),
                    Subtotal  = reader.GetInt32(1),
                });
            }

            return Ok(lista);
        }

        // GET /api/reportes/bajo-stock?umbral=10
        // Productos cuyo stock total (Viña + V.Alemana) está por debajo del umbral
        [HttpGet("bajo-stock")]
        public IActionResult BajoStock([FromQuery] int umbral = 10)
        {
            var lista = new List<ReporteBajoStockItem>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var sql = @"
                SELECT i.Id,
                       COALESCE(i.Codigo, '') AS Codigo,
                       COALESCE(c.Nombre, '') AS Categoria,
                       COALESCE(i.Descripcion, '') AS Descripcion,
                       COALESCE(i.StockVina, 0) AS StockVina,
                       COALESCE(i.StockVa, 0) AS StockVa,
                       (COALESCE(i.StockVina, 0) + COALESCE(i.StockVa, 0)) AS StockTotal,
                       COALESCE(i.Ubicacion, '') AS Ubicacion
                FROM Inventario i
                LEFT JOIN Categorias c ON c.Id = i.CategoriaId
                WHERE (COALESCE(i.StockVina, 0) + COALESCE(i.StockVa, 0)) < @Umbral
                ORDER BY (COALESCE(i.StockVina, 0) + COALESCE(i.StockVa, 0)) ASC, i.Descripcion";

            var cmd = new SqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@Umbral", umbral);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new ReporteBajoStockItem
                {
                    Id          = reader.GetInt32(0),
                    Codigo      = reader.GetString(1),
                    Categoria   = reader.GetString(2),
                    Descripcion = reader.GetString(3),
                    StockVina   = reader.GetInt32(4),
                    StockVa     = reader.GetInt32(5),
                    StockTotal  = reader.GetInt32(6),
                    Ubicacion   = reader.GetString(7),
                });
            }

            return Ok(lista);
        }

        // GET /api/reportes/ranking-cotizados?desde&hasta
        // TOP 50 productos más cotizados (suma de cantidades en cotizaciones no rechazadas)
        [HttpGet("ranking-cotizados")]
        public IActionResult RankingCotizados([FromQuery] DateTime? desde = null, [FromQuery] DateTime? hasta = null)
        {
            var lista = new List<ReporteRankingItem>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var sql = @"
                SELECT d.Codigo,
                       MAX(d.Descripcion) AS Descripcion,
                       SUM(d.Cantidad) AS CantidadCotizada,
                       CAST(AVG(CAST(d.PrecioUnitario AS DECIMAL(12,2))) AS INT) AS PrecioPromedio
                FROM CotizacionesDetalle d
                INNER JOIN Cotizaciones c ON c.Id = d.CotizacionId
                WHERE c.Estado <> 'RECHAZADA'
                  AND (CAST(@Desde AS DATETIME2) IS NULL OR c.Fecha >= @Desde)
                  AND (CAST(@Hasta AS DATETIME2) IS NULL OR c.Fecha <= @Hasta)
                GROUP BY d.Codigo
                ORDER BY SUM(d.Cantidad) DESC
                OFFSET 0 ROWS FETCH NEXT 50 ROWS ONLY";

            var cmd = new SqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@Desde", (object?)desde ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Hasta", (object?)hasta ?? DBNull.Value);

            using var reader = cmd.ExecuteReader();
            int rank = 1;
            while (reader.Read())
            {
                lista.Add(new ReporteRankingItem
                {
                    Ranking         = rank++,
                    Codigo          = reader.GetString(0),
                    Descripcion     = reader.GetString(1),
                    CantidadVendida = reader.GetInt32(2),
                    PrecioPromedio  = reader.GetInt32(3),
                });
            }

            return Ok(lista);
        }

        // GET /api/reportes/bitacora-ventas?desde&hasta
        // Registro detallado de TODAS las ventas (boletas + notas de venta), unidas y ordenadas por fecha
        [HttpGet("bitacora-ventas")]
        public IActionResult BitacoraVentas([FromQuery] DateTime? desde = null, [FromQuery] DateTime? hasta = null)
        {
            var lista = new List<ReporteBitacoraItem>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var sql = @"
                SELECT 'BOLETA' AS Tipo, b.Numero,
                       FORMAT(b.Fecha, 'dd/MM/yyyy') AS Fecha,
                       CONVERT(VARCHAR(8), b.Hora, 108)  AS Hora,
                       b.MedioPago, b.Total, b.Usuario,
                       CASE WHEN b.Anulada = 1 THEN 'ANULADA' ELSE 'VIGENTE' END AS Estado
                FROM Boletas b
                WHERE (CAST(@Desde AS DATETIME2) IS NULL OR b.Fecha >= @Desde)
                  AND (CAST(@Hasta AS DATETIME2) IS NULL OR b.Fecha <= @Hasta)

                UNION ALL

                SELECT 'N.VENTA' AS Tipo, n.Numero,
                       FORMAT(n.Fecha, 'dd/MM/yyyy') AS Fecha,
                       CONVERT(VARCHAR(8), n.Hora, 108)  AS Hora,
                       n.MedioPago, n.Total, n.Usuario, n.Estado
                FROM NotasVenta n
                WHERE (CAST(@Desde AS DATETIME2) IS NULL OR n.Fecha >= @Desde)
                  AND (CAST(@Hasta AS DATETIME2) IS NULL OR n.Fecha <= @Hasta)

                ORDER BY Fecha DESC, Hora DESC";

            var cmd = new SqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@Desde", (object?)desde ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Hasta", (object?)hasta ?? DBNull.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new ReporteBitacoraItem
                {
                    Tipo      = reader.GetString(0),
                    Numero    = reader.GetInt32(1),
                    Fecha     = reader.GetString(2),
                    Hora      = reader.GetString(3),
                    MedioPago = reader.GetString(4),
                    Total     = reader.GetInt32(5),
                    Usuario   = reader.GetString(6),
                    Estado    = reader.GetString(7),
                });
            }

            return Ok(lista);
        }

        // GET /api/reportes/movimientos?direccion=ingreso|egreso&bodega=VIÑA&desde&hasta
        // Items que entraron/salieron de una bodega vía traslados completados
        [HttpGet("movimientos")]
        public IActionResult Movimientos(
            [FromQuery] string direccion = "ingreso",
            [FromQuery] string? bodega = null,
            [FromQuery] DateTime? desde = null,
            [FromQuery] DateTime? hasta = null)
        {
            var lista = new List<ReporteMovimientoItem>();
            var colBodega = direccion.Equals("egreso", StringComparison.OrdinalIgnoreCase)
                ? "BodegaOrigen" : "BodegaDest";

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var sql = $@"
                SELECT t.Numero,
                       FORMAT(t.Fecha, 'dd/MM/yyyy') AS Fecha,
                       t.BodegaOrigen, t.BodegaDest,
                       d.Codigo, d.Descripcion, d.Cantidad,
                       t.Usuario, t.Estado
                FROM TrasladosDetalle d
                INNER JOIN Traslados t ON t.Id = d.TrasladoId
                WHERE t.Estado = 'COMPLETADO'
                  AND (CAST(@Bodega AS NVARCHAR(MAX)) IS NULL OR t.{colBodega} = CAST(@Bodega AS NVARCHAR(MAX)))
                  AND (CAST(@Desde AS DATETIME2) IS NULL OR t.Fecha >= CAST(@Desde AS DATETIME2))
                  AND (CAST(@Hasta AS DATETIME2) IS NULL OR t.Fecha <= CAST(@Hasta AS DATETIME2))
                ORDER BY t.Fecha DESC, t.Numero DESC";

            var cmd = new SqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@Bodega", (object?)bodega ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Desde",  (object?)desde  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Hasta",  (object?)hasta  ?? DBNull.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new ReporteMovimientoItem
                {
                    Numero       = reader.GetInt32(0),
                    Fecha        = reader.GetString(1),
                    BodegaOrigen = reader.GetString(2),
                    BodegaDest   = reader.GetString(3),
                    Codigo       = reader.GetString(4),
                    Descripcion  = reader.GetString(5),
                    Cantidad     = reader.GetInt32(6),
                    Usuario      = reader.GetString(7),
                    Estado       = reader.GetString(8),
                });
            }

            return Ok(lista);
        }

        // GET /api/reportes/resumen-dia
        // KPIs del día actual — pensado para el Panel de Ventas
        [HttpGet("resumen-dia")]
        public IActionResult ResumenDia()
        {
            var resumen = new ReporteResumenDia
            {
                Fecha = DateTime.Today.ToString("dd/MM/yyyy"),
            };

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            // Una sola query con UNION ALL para obtener los 4 contadores y los 3 totales
            var sql = @"
                SELECT 'BOLETAS' AS Tipo, COUNT(*) AS Cant, COALESCE(SUM(Total), 0) AS Tot
                FROM Boletas
                WHERE CAST(Fecha AS DATE) = CAST(GETDATE() AS DATE) AND Anulada = 0

                UNION ALL

                SELECT 'NOTAS', COUNT(*), COALESCE(SUM(Total), 0)
                FROM NotasVenta
                WHERE CAST(Fecha AS DATE) = CAST(GETDATE() AS DATE) AND Estado <> 'ANULADA'

                UNION ALL

                SELECT 'FACTURAS', COUNT(*), COALESCE(SUM(Total), 0)
                FROM Facturas
                WHERE CAST(Fecha AS DATE) = CAST(GETDATE() AS DATE) AND Estado <> 'ANULADA'

                UNION ALL

                SELECT 'COTIZACIONES', COUNT(*), 0
                FROM Cotizaciones
                WHERE CAST(Fecha AS DATE) = CAST(GETDATE() AS DATE)";

            var cmd = new SqlCommand(sql, conexion);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var tipo = reader.GetString(0);
                var cant = reader.GetInt32(1);
                var tot  = reader.GetInt32(2);
                switch (tipo)
                {
                    case "BOLETAS":      resumen.CantidadBoletas      = cant; resumen.VentasTotalHoy += tot; break;
                    case "NOTAS":        resumen.CantidadNotasVenta   = cant; resumen.VentasTotalHoy += tot; break;
                    case "FACTURAS":     resumen.CantidadFacturas     = cant; resumen.VentasTotalHoy += tot; break;
                    case "COTIZACIONES": resumen.CantidadCotizaciones = cant; break;
                }
            }
            return Ok(resumen);
        }

        // GET /api/reportes/productos-comprados?desde&hasta&proveedorId
        // Detalle de productos comprados a proveedores vía facturas de compra
        [HttpGet("productos-comprados")]
        public IActionResult ProductosComprados(
            [FromQuery] DateTime? desde = null,
            [FromQuery] DateTime? hasta = null,
            [FromQuery] int? proveedorId = null)
        {
            var lista = new List<ReporteProductoCompradoItem>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var sql = @"
                SELECT FORMAT(f.Fecha, 'dd/MM/yyyy') AS Fecha,
                       f.TipoDoc, f.NumeroDoc,
                       p.RazonSocial AS Proveedor,
                       d.Codigo, d.Descripcion, d.Cantidad, d.PrecioNeto,
                       (d.Cantidad * d.PrecioNeto) AS Subtotal,
                       f.Estado
                FROM FacturasCompraDetalle d
                INNER JOIN FacturasCompra  f ON f.Id = d.FacturaCompraId
                INNER JOIN Proveedores     p ON p.Id = f.ProveedorId
                WHERE f.Estado <> 'ANULADA'
                  AND (CAST(@Desde AS DATETIME2) IS NULL OR f.Fecha >= CAST(@Desde AS DATETIME2))
                  AND (CAST(@Hasta AS DATETIME2) IS NULL OR f.Fecha <= CAST(@Hasta AS DATETIME2))
                  AND (CAST(@ProveedorId AS INT) IS NULL OR f.ProveedorId = CAST(@ProveedorId AS INT))
                ORDER BY f.Fecha DESC, f.Id DESC";

            var cmd = new SqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@Desde",       (object?)desde       ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Hasta",       (object?)hasta       ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ProveedorId", (object?)proveedorId ?? DBNull.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new ReporteProductoCompradoItem
                {
                    Fecha       = reader.GetString(0),
                    TipoDoc     = reader.GetString(1),
                    NumeroDoc   = reader.GetString(2),
                    Proveedor   = reader.GetString(3),
                    Codigo      = reader.GetString(4),
                    Descripcion = reader.GetString(5),
                    Cantidad    = reader.GetInt32(6),
                    PrecioNeto  = reader.GetInt32(7),
                    Subtotal    = reader.GetInt32(8),
                    Estado      = reader.GetString(9),
                });
            }
            return Ok(lista);
        }

        // GET /api/reportes/caja-diaria?desde=YYYY-MM-DD&hasta=YYYY-MM-DD
        // Cierre de caja del período: agrega Boletas + Notas de Venta + Facturas no anuladas
        [HttpGet("caja-diaria")]
        public IActionResult CajaDiaria(
            [FromQuery] DateTime? desde = null,
            [FromQuery] DateTime? hasta = null)
        {
            var fechaDesde = desde ?? DateTime.Today;
            var fechaHasta = hasta ?? DateTime.Today;

            var resultado = new ReporteCajaDiaria
            {
                Desde = fechaDesde.ToString("dd/MM/yyyy"),
                Hasta = fechaHasta.ToString("dd/MM/yyyy"),
            };

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            // 1) Totales por medio de pago — unimos boletas + notas + facturas
            var sqlMedios = @"
                SELECT MedioPago, COUNT(*) AS Cantidad, SUM(Total) AS Total
                FROM (
                    SELECT MedioPago, Total
                    FROM Boletas
                    WHERE Anulada = 0 AND Fecha BETWEEN @Desde AND @Hasta

                    UNION ALL

                    SELECT MedioPago, Total
                    FROM NotasVenta
                    WHERE Estado <> 'ANULADA' AND Fecha BETWEEN @Desde AND @Hasta

                    UNION ALL

                    SELECT CondVenta AS MedioPago, Total
                    FROM Facturas
                    WHERE Estado <> 'ANULADA' AND Fecha BETWEEN @Desde AND @Hasta
                ) t
                GROUP BY MedioPago
                ORDER BY SUM(Total) DESC";

            var cmd1 = new SqlCommand(sqlMedios, conexion);
            cmd1.Parameters.AddWithValue("@Desde", fechaDesde);
            cmd1.Parameters.AddWithValue("@Hasta", fechaHasta);
            using (var reader = cmd1.ExecuteReader())
            {
                while (reader.Read())
                {
                    resultado.PorMedioPago.Add(new CajaDiariaMedioPago
                    {
                        MedioPago = reader.GetString(0),
                        Cantidad  = reader.GetInt32(1),
                        Total     = reader.GetInt32(2),
                    });
                }
            }

            // 2) Totales por tipo de documento
            var sqlTipos = @"
                SELECT 'BOLETA' AS Tipo, COUNT(*) AS Cantidad, COALESCE(SUM(Total), 0) AS Total
                FROM Boletas
                WHERE Anulada = 0 AND Fecha BETWEEN @Desde AND @Hasta

                UNION ALL

                SELECT 'N.VENTA', COUNT(*), COALESCE(SUM(Total), 0)
                FROM NotasVenta
                WHERE Estado <> 'ANULADA' AND Fecha BETWEEN @Desde AND @Hasta

                UNION ALL

                SELECT 'FACTURA', COUNT(*), COALESCE(SUM(Total), 0)
                FROM Facturas
                WHERE Estado <> 'ANULADA' AND Fecha BETWEEN @Desde AND @Hasta";

            var cmd2 = new SqlCommand(sqlTipos, conexion);
            cmd2.Parameters.AddWithValue("@Desde", fechaDesde);
            cmd2.Parameters.AddWithValue("@Hasta", fechaHasta);
            using (var reader = cmd2.ExecuteReader())
            {
                while (reader.Read())
                {
                    resultado.PorTipoDoc.Add(new CajaDiariaTipoDoc
                    {
                        Tipo     = reader.GetString(0),
                        Cantidad = reader.GetInt32(1),
                        Total    = reader.GetInt32(2),
                    });
                }
            }

            // Totales generales (sumando lo que ya tenemos)
            resultado.TotalDocumentos = resultado.PorTipoDoc.Sum(t => t.Cantidad);
            resultado.TotalVentas     = resultado.PorTipoDoc.Sum(t => t.Total);

            return Ok(resultado);
        }

        // GET /api/reportes/facturas-impagas
        // Facturas a crédito vigentes (VIGENTE o VENCIDA), con cálculo de días vencidos
        [HttpGet("facturas-impagas")]
        public IActionResult FacturasImpagas()
        {
            var lista = new List<ReporteFacturaImpagaItem>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var sql = @"
                SELECT f.Id, f.Folio,
                       FORMAT(f.Fecha, 'dd/MM/yyyy') AS Fecha,
                       COALESCE(FORMAT(f.Vencimiento, 'dd/MM/yyyy'), '') AS Vencimiento,
                       DATEDIFF(DAY, f.Vencimiento, CAST(GETDATE() AS DATE)) AS DiasVencido,
                       c.Rut AS ClienteRut, c.Nombre AS ClienteNombre,
                       f.Total, f.Estado
                FROM Facturas f
                INNER JOIN Clientes c ON c.Id = f.ClienteId
                WHERE f.Estado IN ('VIGENTE', 'VENCIDA')
                  AND f.CondVenta = 'CREDITO'
                ORDER BY f.Vencimiento ASC";

            var cmd = new SqlCommand(sql, conexion);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new ReporteFacturaImpagaItem
                {
                    Id            = reader.GetInt32(0),
                    Folio         = reader.GetInt32(1),
                    Fecha         = reader.GetString(2),
                    Vencimiento   = reader.GetString(3),
                    DiasVencido   = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    ClienteRut    = reader.GetString(5),
                    ClienteNombre = reader.GetString(6),
                    Total         = reader.GetInt32(7),
                    Estado        = reader.GetString(8),
                });
            }

            return Ok(lista);
        }

        // GET /api/reportes/resumen-anual-boletas?anio=2026
        // Totales mensuales de boletas vigentes para un año
        [HttpGet("resumen-anual-boletas")]
        public IActionResult ResumenAnualBoletas([FromQuery] int? anio = null)
        {
            int anioFinal = anio ?? DateTime.Now.Year;
            var lista = new List<ReporteResumenMensual>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var sql = @"
                SELECT MONTH(Fecha) AS Mes,
                       COUNT(*) AS Emitidas,
                       SUM(TotalNeto) AS Neto,
                       SUM(Iva) AS Iva,
                       SUM(Total) AS Total
                FROM Boletas
                WHERE YEAR(Fecha) = @Anio
                  AND Anulada = 0
                GROUP BY MONTH(Fecha)
                ORDER BY MONTH(Fecha)";

            var cmd = new SqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@Anio", anioFinal);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int mes = reader.GetInt32(0);
                lista.Add(new ReporteResumenMensual
                {
                    Mes      = mes,
                    Periodo  = $"{MESES[mes]} {anioFinal}",
                    Emitidas = reader.GetInt32(1),
                    Neto     = reader.GetInt32(2),
                    Iva      = reader.GetInt32(3),
                    Total    = reader.GetInt32(4),
                });
            }

            return Ok(lista);
        }
    }
}
