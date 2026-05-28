-- =============================================================
-- seed_demo.sql — Datos de DEMOSTRACIÓN para poblar gráficos/KPIs.
--
-- Todas las boletas generadas llevan Usuario = 'DEMO' y Numero >= 50000,
-- así no se confunden con datos reales (la numeración real va ~44668) y
-- se pueden borrar de un golpe.
--
-- PARA REVERTIR (borrar todos los datos demo):
--   DELETE FROM BoletasDetalle WHERE BoletaId IN (SELECT Id FROM Boletas WHERE Usuario = 'DEMO');
--   DELETE FROM Boletas WHERE Usuario = 'DEMO';
--
-- Es idempotente: si ya hay datos DEMO, no inserta de nuevo.
-- =============================================================

SET NOCOUNT ON;
-- Requerido para INSERT en tablas con columnas computadas PERSISTED (Subtotal)
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

IF EXISTS (SELECT 1 FROM Boletas WHERE Usuario = 'DEMO')
BEGIN
    PRINT 'Ya existen datos DEMO. No se inserta nada. Para regenerar, bórralos primero.';
    RETURN;
END

-- Productos reales para el detalle (id, codigo, descripcion, precio meson)
DECLARE @prods TABLE (idx INT IDENTITY(0,1), pid INT, codigo NVARCHAR(50), descripcion NVARCHAR(200), precio INT);
INSERT INTO @prods (pid, codigo, descripcion, precio) VALUES
  (1, 'FIL-001', 'Filtro de Aceite',            8500),
  (5, 'ACE-505', 'Aceite Motor 15W40 4LT',     16500),
  (6, 'BAT-220', 'Bateria 12V 75AH Bosch',     89000),
  (7, 'FIL-088', 'Filtro Petroleo Isuzu DMax', 13900),
  (8, 'CHE-450', 'Optico LH DMAX',            142000);

DECLARE @medios TABLE (idx INT, nombre NVARCHAR(30));
INSERT INTO @medios VALUES (0,'EFECTIVO'),(1,'TARJETA'),(2,'TRANSFERENCIA'),(3,'DEBITO');

DECLARE @i INT = 0;
DECLARE @totalBoletas INT = 38;

WHILE @i < @totalBoletas
BEGIN
    -- Fecha: las últimas 6 boletas son de HOY (para que los KPIs del día muestren actividad);
    -- el resto se distribuye en los meses 1..5 de 2026 para llenar el gráfico anual.
    DECLARE @fecha DATE;
    IF @i >= (@totalBoletas - 6)
        SET @fecha = CAST(GETDATE() AS DATE);
    ELSE
    BEGIN
        DECLARE @mes INT = (@i % 5) + 1;
        DECLARE @dia INT = ((@i * 7) % 27) + 1;
        SET @fecha = DATEFROMPARTS(2026, @mes, @dia);
    END

    DECLARE @medio NVARCHAR(30) = (SELECT nombre FROM @medios WHERE idx = @i % 4);
    DECLARE @numItems INT = (@i % 3) + 1;          -- 1 a 3 items
    DECLARE @numero INT = 50000 + @i;

    -- 1) Cabecera con totales en 0 (se actualizan tras el detalle)
    INSERT INTO Boletas (Numero, Fecha, Hora, ClienteId, MedioPago, DescGlobal, TotalNeto, Iva, Total, Usuario, Anulada, Bodega)
    VALUES (@numero, @fecha, CAST(GETDATE() AS TIME), NULL, @medio, 0, 0, 0, 0, 'DEMO', 0, 'VINA');

    DECLARE @bid INT = SCOPE_IDENTITY();

    -- 2) Detalle: @numItems productos distintos, cantidad 1..4
    DECLARE @j INT = 0;
    WHILE @j < @numItems
    BEGIN
        DECLARE @pidx INT = (@i + @j) % 5;
        DECLARE @cant INT = ((@i + @j) % 4) + 1;
        DECLARE @pid INT, @cod NVARCHAR(50), @desc NVARCHAR(200), @precio INT;
        SELECT @pid = pid, @cod = codigo, @desc = descripcion, @precio = precio FROM @prods WHERE idx = @pidx;

        INSERT INTO BoletasDetalle (BoletaId, ProductoId, Codigo, Descripcion, Cantidad, PrecioUnitario)
        VALUES (@bid, @pid, @cod, @desc, @cant, @precio);

        SET @j = @j + 1;
    END

    -- 3) Recalcular totales desde el detalle (IVA 19%)
    DECLARE @sumTotal INT = (SELECT SUM(Subtotal) FROM BoletasDetalle WHERE BoletaId = @bid);
    DECLARE @neto INT = CAST(ROUND(@sumTotal / 1.19, 0) AS INT);
    DECLARE @iva  INT = @sumTotal - @neto;

    UPDATE Boletas SET TotalNeto = @neto, Iva = @iva, Total = @sumTotal WHERE Id = @bid;

    SET @i = @i + 1;
END

PRINT CONCAT('Insertadas ', @totalBoletas, ' boletas DEMO (Numero 50000..', 50000 + @totalBoletas - 1, ').');

-- Resumen rápido
SELECT
    (SELECT COUNT(*) FROM Boletas WHERE Usuario='DEMO') AS BoletasDemo,
    (SELECT COUNT(*) FROM Boletas WHERE Usuario='DEMO' AND Fecha = CAST(GETDATE() AS DATE)) AS BoletasHoy,
    (SELECT SUM(Total) FROM Boletas WHERE Usuario='DEMO') AS MontoTotal;
