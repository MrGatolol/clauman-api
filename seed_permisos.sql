SET QUOTED_IDENTIFIER ON;
GO

-- Matriz default para CAJERO
UPDATE Usuarios
SET Permisos = N'{"locales":{"vina":true,"valemana":true,"taller":false},"ventas":{"crearBoleta":true,"crearNVenta":true,"crearFactura":false,"crearCotizacion":true,"ocuparMayorista":false,"verHistorica":true},"inventario":{"crearCompra":false,"verStockMinimo":true,"ajustes":false,"verMovimientos":true},"compras":{"crearSinStock":false},"traslados":{"crear":false,"recibir":true},"productos":{"crear":false,"nivelEdicion":"sinAcceso","eliminar":false,"crearPromoPack":false},"clientesProvCat":{"accesoTotal":false},"resumenes":{"verNVentas":true,"verBoletas":true,"verFacturas":false}}'
WHERE Rol = 'CAJERO' AND (Permisos IS NULL OR Permisos = '');

-- Matriz default para VENDEDOR
UPDATE Usuarios
SET Permisos = N'{"locales":{"vina":true,"valemana":true,"taller":true},"ventas":{"crearBoleta":true,"crearNVenta":true,"crearFactura":true,"crearCotizacion":true,"ocuparMayorista":true,"verHistorica":true},"inventario":{"crearCompra":true,"verStockMinimo":true,"ajustes":true,"verMovimientos":true},"compras":{"crearSinStock":true},"traslados":{"crear":true,"recibir":true},"productos":{"crear":true,"nivelEdicion":"todo","eliminar":false,"crearPromoPack":true},"clientesProvCat":{"accesoTotal":true},"resumenes":{"verNVentas":true,"verBoletas":true,"verFacturas":true}}'
WHERE Rol = 'VENDEDOR' AND (Permisos IS NULL OR Permisos = '');

PRINT 'Matrices default cargadas para usuarios sin permisos definidos';
SELECT Username, Rol, LEN(ISNULL(Permisos, '')) AS Tamano FROM Usuarios;
