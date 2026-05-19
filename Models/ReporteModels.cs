namespace ClaumanAPI.Models
{
    // Item del reporte de Inventario Valorizado
    public class ReporteInventarioItem
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int Stock { get; set; }
        public int CostoNeto { get; set; }
        public int Subtotal { get; set; }
    }

    // Item del Ranking de Productos Vendidos
    public class ReporteRankingItem
    {
        public int Ranking { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int CantidadVendida { get; set; }
        public int PrecioPromedio { get; set; }
    }

    // Item de Ventas por Categoría
    public class ReporteVentaCategoria
    {
        public string Categoria { get; set; } = string.Empty;
        public int Subtotal { get; set; }
    }

    // Item del Resumen Anual de Boletas
    public class ReporteResumenMensual
    {
        public int Mes { get; set; }
        public string Periodo { get; set; } = string.Empty;
        public int Emitidas { get; set; }
        public int Neto { get; set; }
        public int Iva { get; set; }
        public int Total { get; set; }
    }

    // Item del reporte de Bajo Stock
    public class ReporteBajoStockItem
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int StockVina { get; set; }
        public int StockVa { get; set; }
        public int StockTotal { get; set; }
        public string Ubicacion { get; set; } = string.Empty;
    }

    // Item del registro de ventas (bitácora — UNION boletas + notas de venta)
    public class ReporteBitacoraItem
    {
        public string Tipo { get; set; } = string.Empty;        // BOLETA | N.VENTA
        public int Numero { get; set; }
        public string Fecha { get; set; } = string.Empty;
        public string Hora { get; set; } = string.Empty;
        public string MedioPago { get; set; } = string.Empty;
        public int Total { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
    }

    // Item del reporte "Productos Comprados a Proveedores"
    public class ReporteProductoCompradoItem
    {
        public string Fecha { get; set; } = string.Empty;
        public string TipoDoc { get; set; } = string.Empty;
        public string NumeroDoc { get; set; } = string.Empty;
        public string Proveedor { get; set; } = string.Empty;
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public int PrecioNeto { get; set; }
        public int Subtotal { get; set; }
        public string Estado { get; set; } = string.Empty;
    }

    // KPIs del día — usado por el Panel de Ventas como home del cajero
    public class ReporteResumenDia
    {
        public string Fecha { get; set; } = string.Empty;
        public int VentasTotalHoy { get; set; }
        public int CantidadBoletas { get; set; }
        public int CantidadNotasVenta { get; set; }
        public int CantidadFacturas { get; set; }
        public int CantidadCotizaciones { get; set; }
    }

    // Reporte de Caja Diaria — agregado de ventas por período
    public class ReporteCajaDiaria
    {
        public string Desde { get; set; } = string.Empty;
        public string Hasta { get; set; } = string.Empty;
        public int TotalDocumentos { get; set; }
        public int TotalVentas { get; set; }
        public List<CajaDiariaMedioPago> PorMedioPago { get; set; } = new();
        public List<CajaDiariaTipoDoc> PorTipoDoc { get; set; } = new();
    }

    public class CajaDiariaMedioPago
    {
        public string MedioPago { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public int Total { get; set; }
    }

    public class CajaDiariaTipoDoc
    {
        public string Tipo { get; set; } = string.Empty;  // BOLETA | N.VENTA | FACTURA
        public int Cantidad { get; set; }
        public int Total { get; set; }
    }

    // Item del reporte de Facturas Impagas
    public class ReporteFacturaImpagaItem
    {
        public int Id { get; set; }
        public int Folio { get; set; }
        public string Fecha { get; set; } = string.Empty;
        public string Vencimiento { get; set; } = string.Empty;
        public int DiasVencido { get; set; }     // negativo = aún no vence, positivo = vencida
        public string ClienteRut { get; set; } = string.Empty;
        public string ClienteNombre { get; set; } = string.Empty;
        public int Total { get; set; }
        public string Estado { get; set; } = string.Empty;
    }

    // Item de movimientos de productos (ingresos/egresos por traslado)
    public class ReporteMovimientoItem
    {
        public int Numero { get; set; }
        public string Fecha { get; set; } = string.Empty;
        public string BodegaOrigen { get; set; } = string.Empty;
        public string BodegaDest { get; set; } = string.Empty;
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
    }
}
