-- =============================================================
-- init_sqlserver.sql
-- Schema completo del sistema Clauman para SQL Server.
-- Ejecutar UNA VEZ contra una base recién creada (ej: ClaumanDB en
-- SQLEXPRESS local). Compatible con SQL Server 2016+.
--
-- Cómo crear la BD y ejecutar este script (en sqlcmd o SSMS):
--     CREATE DATABASE ClaumanDB;
--     GO
--     USE ClaumanDB;
--     GO
--     -- Pega el contenido de este archivo y ejecuta.
-- =============================================================

-- ============ MANTENEDORES ============

IF OBJECT_ID('Categorias','U') IS NULL
CREATE TABLE Categorias (
    Id     INT IDENTITY(1,1) PRIMARY KEY,
    Nombre NVARCHAR(100) NOT NULL UNIQUE
);

IF OBJECT_ID('Clientes','U') IS NULL
CREATE TABLE Clientes (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    Rut       NVARCHAR(20)  NOT NULL,
    Nombre    NVARCHAR(200) NOT NULL,
    Direccion NVARCHAR(200) NOT NULL DEFAULT '',
    Comuna    NVARCHAR(100) NOT NULL DEFAULT '',
    Ciudad    NVARCHAR(100) NOT NULL DEFAULT '',
    Giro      NVARCHAR(200) NOT NULL DEFAULT '',
    Telefono  NVARCHAR(50)  NOT NULL DEFAULT '',
    EmailCorp NVARCHAR(150) NOT NULL DEFAULT '',
    EmailCot  NVARCHAR(150) NOT NULL DEFAULT ''
);

IF OBJECT_ID('Proveedores','U') IS NULL
CREATE TABLE Proveedores (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Rut         NVARCHAR(20)  NOT NULL,
    RazonSocial NVARCHAR(200) NOT NULL,
    Direccion   NVARCHAR(200) NOT NULL DEFAULT '',
    Ciudad      NVARCHAR(100) NOT NULL DEFAULT '',
    Telefono    NVARCHAR(50)  NOT NULL DEFAULT '',
    EmailCorp   NVARCHAR(150) NOT NULL DEFAULT '',
    Ejecutivo   NVARCHAR(150) NOT NULL DEFAULT '',
    EmailCot    NVARCHAR(150) NOT NULL DEFAULT '',
    Banco       NVARCHAR(100) NOT NULL DEFAULT '',
    CtaCte      NVARCHAR(50)  NOT NULL DEFAULT '',
    RutTitular  NVARCHAR(20)  NOT NULL DEFAULT '',
    EmailPagos  NVARCHAR(150) NOT NULL DEFAULT ''
);

IF OBJECT_ID('Inventario','U') IS NULL
CREATE TABLE Inventario (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    Codigo          NVARCHAR(50),
    Descripcion     NVARCHAR(200),
    CategoriaId     INT REFERENCES Categorias(Id),
    Ubicacion       NVARCHAR(100),
    StockVina       INT DEFAULT 0,
    StockVa         INT DEFAULT 0,
    PrecioMeson     INT DEFAULT 0,
    PrecioMayor     INT DEFAULT 0,
    PrecioWeb       INT DEFAULT 0,
    CostoNeto       INT DEFAULT 0,
    Utilidad        INT DEFAULT 0,
    TieneImagen     BIT DEFAULT 0,
    Observacion     NVARCHAR(500),
    Compatibilidad  NVARCHAR(500)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Inventario_Codigo')
CREATE UNIQUE INDEX IX_Inventario_Codigo ON Inventario(Codigo) WHERE Codigo IS NOT NULL;

-- ============ USUARIOS / AUTH ============

IF OBJECT_ID('Usuarios','U') IS NULL
CREATE TABLE Usuarios (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    Rut          NVARCHAR(20),
    Nombre       NVARCHAR(100) NOT NULL,
    Username     NVARCHAR(50)  NOT NULL UNIQUE,
    PasswordHash NVARCHAR(200) NOT NULL,
    Rol          NVARCHAR(20)  NOT NULL DEFAULT 'CAJERO',
    Activo       BIT           NOT NULL DEFAULT 1,
    CreadoEn     DATETIME2     NOT NULL DEFAULT GETDATE(),
    Permisos     NVARCHAR(MAX)
);

IF OBJECT_ID('AccesosLog','U') IS NULL
CREATE TABLE AccesosLog (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    UsuarioId INT,
    Username  NVARCHAR(50) NOT NULL,
    Fecha     DATETIME2 NOT NULL DEFAULT GETDATE(),
    Exito     BIT NOT NULL DEFAULT 0,
    Local     NVARCHAR(50)
);

IF OBJECT_ID('SesionTokens','U') IS NULL
CREATE TABLE SesionTokens (
    Token      NVARCHAR(64) PRIMARY KEY,
    UsuarioId  INT NOT NULL REFERENCES Usuarios(Id) ON DELETE CASCADE,
    CreadoEn   DATETIME2 NOT NULL DEFAULT GETDATE(),
    ExpiraEn   DATETIME2 NOT NULL,
    UltimoUso  DATETIME2 NOT NULL DEFAULT GETDATE()
);

-- ============ TRANSACCIONALES — VENTAS ============

IF OBJECT_ID('Boletas','U') IS NULL
CREATE TABLE Boletas (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    Numero     INT NOT NULL,
    Fecha      DATE NOT NULL DEFAULT CAST(GETDATE() AS DATE),
    Hora       TIME NOT NULL DEFAULT CAST(GETDATE() AS TIME),
    ClienteId  INT REFERENCES Clientes(Id),
    MedioPago  NVARCHAR(30) NOT NULL DEFAULT 'EFECTIVO',
    DescGlobal INT NOT NULL DEFAULT 0,
    TotalNeto  INT NOT NULL DEFAULT 0,
    Iva        INT NOT NULL DEFAULT 0,
    Total      INT NOT NULL DEFAULT 0,
    Usuario    NVARCHAR(50) NOT NULL,
    Anulada    BIT NOT NULL DEFAULT 0,
    Bodega     NVARCHAR(20) NOT NULL DEFAULT 'VINA'
);

IF OBJECT_ID('BoletasDetalle','U') IS NULL
CREATE TABLE BoletasDetalle (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    BoletaId       INT NOT NULL REFERENCES Boletas(Id),
    ProductoId     INT REFERENCES Inventario(Id),
    Codigo         NVARCHAR(50) NOT NULL,
    Descripcion    NVARCHAR(200) NOT NULL,
    Cantidad       INT NOT NULL,
    PrecioUnitario INT NOT NULL,
    Subtotal       AS (Cantidad * PrecioUnitario) PERSISTED
);

IF OBJECT_ID('NotasVenta','U') IS NULL
CREATE TABLE NotasVenta (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    Numero     INT NOT NULL,
    Fecha      DATE NOT NULL DEFAULT CAST(GETDATE() AS DATE),
    Hora       TIME NOT NULL DEFAULT CAST(GETDATE() AS TIME),
    ClienteId  INT REFERENCES Clientes(Id),
    ClienteRef NVARCHAR(200) NOT NULL DEFAULT '',
    CondVenta  NVARCHAR(20) NOT NULL DEFAULT 'MESON',
    MedioPago  NVARCHAR(30) NOT NULL DEFAULT 'EFECTIVO',
    DescGlobal INT NOT NULL DEFAULT 0,
    TotalNeto  INT NOT NULL DEFAULT 0,
    Iva        INT NOT NULL DEFAULT 0,
    Total      INT NOT NULL DEFAULT 0,
    Estado     NVARCHAR(20) NOT NULL DEFAULT 'VIGENTE',
    Usuario    NVARCHAR(50) NOT NULL,
    Bodega     NVARCHAR(20) NOT NULL DEFAULT 'VINA'
);

IF OBJECT_ID('NotasVentaDetalle','U') IS NULL
CREATE TABLE NotasVentaDetalle (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    NotaVentaId    INT NOT NULL REFERENCES NotasVenta(Id),
    ProductoId     INT REFERENCES Inventario(Id),
    Codigo         NVARCHAR(50) NOT NULL,
    Descripcion    NVARCHAR(200) NOT NULL,
    Cantidad       INT NOT NULL,
    PrecioUnitario INT NOT NULL,
    Subtotal       AS (Cantidad * PrecioUnitario) PERSISTED
);

IF OBJECT_ID('Cotizaciones','U') IS NULL
CREATE TABLE Cotizaciones (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Numero      INT NOT NULL,
    Fecha       DATE NOT NULL DEFAULT CAST(GETDATE() AS DATE),
    Hora        TIME NOT NULL DEFAULT CAST(GETDATE() AS TIME),
    ClienteId   INT REFERENCES Clientes(Id),
    ClienteRef  NVARCHAR(200) NOT NULL DEFAULT '',
    CondVenta   NVARCHAR(20) NOT NULL DEFAULT 'MESON',
    DescGlobal  INT NOT NULL DEFAULT 0,
    Total       INT NOT NULL DEFAULT 0,
    Estado      NVARCHAR(20) NOT NULL DEFAULT 'VIGENTE',
    Usuario     NVARCHAR(50) NOT NULL,
    Vencimiento DATE
);

IF OBJECT_ID('CotizacionesDetalle','U') IS NULL
CREATE TABLE CotizacionesDetalle (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    CotizacionId   INT NOT NULL REFERENCES Cotizaciones(Id),
    ProductoId     INT REFERENCES Inventario(Id),
    Codigo         NVARCHAR(50) NOT NULL,
    Descripcion    NVARCHAR(200) NOT NULL,
    Cantidad       INT NOT NULL,
    PrecioUnitario INT NOT NULL,
    Subtotal       AS (Cantidad * PrecioUnitario) PERSISTED
);

IF OBJECT_ID('Facturas','U') IS NULL
CREATE TABLE Facturas (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Numero      INT NOT NULL,
    Folio       INT NOT NULL,
    Fecha       DATE NOT NULL DEFAULT CAST(GETDATE() AS DATE),
    Hora        TIME NOT NULL DEFAULT CAST(GETDATE() AS TIME),
    ClienteId   INT NOT NULL REFERENCES Clientes(Id),
    CondVenta   NVARCHAR(20) NOT NULL DEFAULT 'CONTADO',
    OrdenCompra NVARCHAR(50) NOT NULL DEFAULT '',
    DescGlobal  INT NOT NULL DEFAULT 0,
    TotalNeto   INT NOT NULL DEFAULT 0,
    Iva         INT NOT NULL DEFAULT 0,
    Total       INT NOT NULL DEFAULT 0,
    Estado      NVARCHAR(20) NOT NULL DEFAULT 'VIGENTE',
    Usuario     NVARCHAR(50) NOT NULL,
    Vencimiento DATE,
    Bodega      NVARCHAR(20) NOT NULL DEFAULT 'VINA'
);

IF OBJECT_ID('FacturasDetalle','U') IS NULL
CREATE TABLE FacturasDetalle (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    FacturaId      INT NOT NULL REFERENCES Facturas(Id),
    ProductoId     INT REFERENCES Inventario(Id),
    Codigo         NVARCHAR(50) NOT NULL,
    Descripcion    NVARCHAR(200) NOT NULL,
    Cantidad       INT NOT NULL,
    PrecioUnitario INT NOT NULL,
    Subtotal       AS (Cantidad * PrecioUnitario) PERSISTED
);

IF OBJECT_ID('NotasCredito','U') IS NULL
CREATE TABLE NotasCredito (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    Numero          INT NOT NULL,
    Fecha           DATE NOT NULL DEFAULT CAST(GETDATE() AS DATE),
    Hora            TIME NOT NULL DEFAULT CAST(GETDATE() AS TIME),
    TipoDocOrigen   NVARCHAR(20) NOT NULL,
    DocOrigenId     INT NOT NULL,
    DocOrigenNumero INT NOT NULL,
    ClienteId       INT REFERENCES Clientes(Id),
    Total           INT NOT NULL DEFAULT 0,
    Motivo          NVARCHAR(300) NOT NULL DEFAULT '',
    Usuario         NVARCHAR(50) NOT NULL,
    Bodega          NVARCHAR(20) NOT NULL DEFAULT 'VINA',
    Anulada         BIT NOT NULL DEFAULT 0
);

IF OBJECT_ID('NotasCreditoDetalle','U') IS NULL
CREATE TABLE NotasCreditoDetalle (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    NotaCreditoId  INT NOT NULL REFERENCES NotasCredito(Id),
    ProductoId     INT REFERENCES Inventario(Id),
    Codigo         NVARCHAR(50) NOT NULL,
    Descripcion    NVARCHAR(200) NOT NULL,
    Cantidad       INT NOT NULL,
    PrecioUnitario INT NOT NULL
);

-- ============ TRASLADOS / COMPRAS ============

IF OBJECT_ID('Traslados','U') IS NULL
CREATE TABLE Traslados (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    Numero       INT NOT NULL,
    Fecha        DATE NOT NULL DEFAULT CAST(GETDATE() AS DATE),
    Hora         TIME NOT NULL DEFAULT CAST(GETDATE() AS TIME),
    BodegaOrigen NVARCHAR(50) NOT NULL,
    BodegaDest   NVARCHAR(50) NOT NULL,
    Estado       NVARCHAR(20) NOT NULL DEFAULT 'PENDIENTE',
    Usuario      NVARCHAR(50) NOT NULL
);

IF OBJECT_ID('TrasladosDetalle','U') IS NULL
CREATE TABLE TrasladosDetalle (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    TrasladoId  INT NOT NULL REFERENCES Traslados(Id),
    ProductoId  INT REFERENCES Inventario(Id),
    Codigo      NVARCHAR(50) NOT NULL,
    Descripcion NVARCHAR(200) NOT NULL,
    Cantidad    INT NOT NULL
);

IF OBJECT_ID('FacturasCompra','U') IS NULL
CREATE TABLE FacturasCompra (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    Fecha          DATE NOT NULL DEFAULT CAST(GETDATE() AS DATE),
    TipoDoc        NVARCHAR(20) NOT NULL DEFAULT 'FACTURA',
    NumeroDoc      NVARCHAR(50) NOT NULL,
    ProveedorId    INT NOT NULL REFERENCES Proveedores(Id),
    OrdenCompra    NVARCHAR(50) NOT NULL DEFAULT '',
    CondVenta      NVARCHAR(20) NOT NULL DEFAULT 'CONTADO',
    TotalNeto      INT NOT NULL DEFAULT 0,
    Iva            INT NOT NULL DEFAULT 0,
    Total          INT NOT NULL DEFAULT 0,
    Estado         NVARCHAR(20) NOT NULL DEFAULT 'VIGENTE',
    FechaRecepcion DATE,
    Vencimiento    DATE,
    Usuario        NVARCHAR(50) NOT NULL
);

IF OBJECT_ID('FacturasCompraDetalle','U') IS NULL
CREATE TABLE FacturasCompraDetalle (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    FacturaCompraId INT NOT NULL REFERENCES FacturasCompra(Id),
    ProductoId      INT REFERENCES Inventario(Id),
    Codigo          NVARCHAR(50) NOT NULL,
    Descripcion     NVARCHAR(200) NOT NULL,
    Cantidad        INT NOT NULL,
    PrecioNeto      INT NOT NULL,
    PrecioMeson     INT NOT NULL DEFAULT 0,
    PrecioMayor     INT NOT NULL DEFAULT 0
);

IF OBJECT_ID('Ajustes','U') IS NULL
CREATE TABLE Ajustes (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Fecha       DATETIME2 NOT NULL DEFAULT GETDATE(),
    Local       NVARCHAR(20) NOT NULL DEFAULT 'VINA',
    Tipo        NVARCHAR(50) NOT NULL DEFAULT 'AJUSTE INVENTARIO',
    ProductoId  INT REFERENCES Inventario(Id),
    Codigo      NVARCHAR(50) NOT NULL DEFAULT '',
    Descripcion NVARCHAR(200) NOT NULL DEFAULT '',
    Motivo      NVARCHAR(300) NOT NULL DEFAULT '',
    Ajuste      INT NOT NULL DEFAULT 0,
    Usuario     NVARCHAR(50) NOT NULL
);

-- ============ CONFIG ============

IF OBJECT_ID('Parametros','U') IS NULL
CREATE TABLE Parametros (
    Clave       NVARCHAR(50) PRIMARY KEY,
    Valor       NVARCHAR(500) NOT NULL,
    Descripcion NVARCHAR(200) NOT NULL DEFAULT ''
);

-- ============ SEEDS ============

-- Categorías base (solo si no existen)
INSERT INTO Categorias (Nombre)
SELECT v.Nombre FROM (VALUES
    ('ACCESORIO'),('ACEITE'),('BATERIA'),('BODEGA'),('CHEVROLET'),
    ('FILTRO'),('MITSUBISHI'),('MULTIMARCAS'),('PASTILLA'),('SSANGYONG'),
    ('SUZUKI'),('VARIOS')
) v(Nombre)
WHERE NOT EXISTS (SELECT 1 FROM Categorias c WHERE c.Nombre = v.Nombre);

-- Parámetros del sistema
INSERT INTO Parametros (Clave, Valor, Descripcion)
SELECT v.Clave, v.Valor, v.Descripcion FROM (VALUES
    ('descuento1',       '0',                                       'Descuento 1 por defecto (%)'),
    ('descuento2',       '0',                                       'Descuento 2 por defecto (%)'),
    ('iva',              '19',                                      'IVA aplicado en ventas (%)'),
    ('empresa_nombre',   'ClauMan SpA',                             'Razón social de la empresa'),
    ('empresa_rut',      '76.123.456-7',                            'RUT de la empresa'),
    ('empresa_giro',     'Comercio de repuestos automotrices',      'Giro comercial'),
    ('empresa_direccion','Av. Principal 1234, Viña del Mar',        'Dirección'),
    ('empresa_telefono', '+56 32 1234567',                          'Teléfono')
) v(Clave, Valor, Descripcion)
WHERE NOT EXISTS (SELECT 1 FROM Parametros p WHERE p.Clave = v.Clave);

-- Usuario admin inicial — contraseña: CAMBIAR_INMEDIATAMENTE_001!
-- DESPUÉS DEL PRIMER LOGIN: usa el modal "Cambiar contraseña" en la UI.
IF NOT EXISTS (SELECT 1 FROM Usuarios WHERE Username = 'admin')
INSERT INTO Usuarios (Rut, Nombre, Username, PasswordHash, Rol) VALUES
    ('11.111.111-1', 'Administrador', 'admin',
     '6eb4b988792bcd8227c7836c73a6adf202ce9db71930204be9cf2613663fdefc',
     'ADMIN');
