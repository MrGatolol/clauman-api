using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaumanAPI.Models;
using ClaumanAPI.Middleware;

namespace ClaumanAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InventarioController : ControllerBase
    {
        private readonly string _conexion;

        public InventarioController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/inventario
        [HttpGet]
        public IActionResult ObtenerTodos()
        {
            var lista = new List<Producto>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            // JOIN con Categorias para traer Id Y nombre — front necesita ambos
            var cmd = new SqlCommand(@"
                SELECT
                    i.Id, i.Codigo, i.CategoriaId, COALESCE(c.Nombre, '') AS Categoria,
                    i.Descripcion, COALESCE(i.Ubicacion, '') AS Ubicacion,
                    COALESCE(i.StockVina, 0), COALESCE(i.StockVa, 0),
                    COALESCE(i.PrecioMeson, 0), COALESCE(i.PrecioMayor, 0),
                    COALESCE(i.PrecioWeb, 0), COALESCE(i.CostoNeto, 0),
                    COALESCE(i.Utilidad, 0), ISNULL(i.TieneImagen, 0),
                    COALESCE(i.Observacion, ''), COALESCE(i.Compatibilidad, '')
                FROM Inventario i
                LEFT JOIN Categorias c ON i.CategoriaId = c.Id
                ORDER BY i.Descripcion", conexion);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new Producto
                {
                    Id            = reader.GetInt32(0),
                    Codigo        = reader.GetString(1),
                    CategoriaId   = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    Categoria     = reader.GetString(3),
                    Descripcion   = reader.GetString(4),
                    Ubicacion     = reader.GetString(5),
                    StockVina     = reader.GetInt32(6),
                    StockVa       = reader.GetInt32(7),
                    PrecioMeson   = reader.GetInt32(8),
                    PrecioMayor   = reader.GetInt32(9),
                    PrecioWeb     = reader.GetInt32(10),
                    CostoNeto     = reader.GetInt32(11),
                    Utilidad      = reader.GetInt32(12),
                    TieneImagen   = reader.GetBoolean(13),
                    Observacion   = reader.GetString(14),
                    Compatibilidad = reader.GetString(15),
                });
            }

            return Ok(lista);
        }

        // GET /api/inventario/paginado?page=1&size=50&q=filtro&categoriaId=2&soloConStock=true
        // Devuelve un wrapper { items, total, page, size, totalPages }.
        // Pensado para tablas grandes (>500 productos) — usa OFFSET/FETCH y filtra en BD.
        [HttpGet("paginado")]
        public IActionResult ObtenerPaginado(
            [FromQuery] int page = 1,
            [FromQuery] int size = 50,
            [FromQuery] string? q = null,
            [FromQuery] int? categoriaId = null,
            [FromQuery] bool soloConStock = false)
        {
            // Sanitizar: page mínimo 1, size entre 1 y 200 (evita abuso de payload)
            page = Math.Max(1, page);
            size = Math.Clamp(size, 1, 200);
            int offset = (page - 1) * size;

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            // Filtro WHERE compartido entre el COUNT y el SELECT (evita divergencia)
            string where = @"
                WHERE 1=1
                  AND (CAST(@Q AS NVARCHAR(MAX)) IS NULL OR i.Codigo LIKE CAST(@QLike AS NVARCHAR(MAX)) OR i.Descripcion LIKE CAST(@QLike AS NVARCHAR(MAX)))
                  AND (CAST(@CategoriaId AS INT) IS NULL OR i.CategoriaId = CAST(@CategoriaId AS INT))
                  AND (@SoloConStock = 0 OR (COALESCE(i.StockVina, 0) + COALESCE(i.StockVa, 0)) > 0)";

            // 1) Contar total con el mismo filtro (para que el front pueda calcular páginas)
            var cmdCount = new SqlCommand($@"
                SELECT COUNT(*)
                FROM Inventario i
                LEFT JOIN Categorias c ON c.Id = i.CategoriaId
                {where}", conexion);
            AgregarParametrosFiltro(cmdCount, q, categoriaId, soloConStock);
            int total = (int)cmdCount.ExecuteScalar();

            // 2) Traer página con ORDER BY estable + OFFSET/FETCH (SQL Server 2012+)
            var cmd = new SqlCommand($@"
                SELECT
                    i.Id, i.Codigo, i.CategoriaId, COALESCE(c.Nombre, '') AS Categoria,
                    i.Descripcion, COALESCE(i.Ubicacion, ''),
                    COALESCE(i.StockVina, 0), COALESCE(i.StockVa, 0),
                    COALESCE(i.PrecioMeson, 0), COALESCE(i.PrecioMayor, 0),
                    COALESCE(i.PrecioWeb, 0), COALESCE(i.CostoNeto, 0),
                    COALESCE(i.Utilidad, 0), ISNULL(i.TieneImagen, 0),
                    COALESCE(i.Observacion, ''), COALESCE(i.Compatibilidad, '')
                FROM Inventario i
                LEFT JOIN Categorias c ON c.Id = i.CategoriaId
                {where}
                ORDER BY i.Descripcion, i.Id   -- Id como tiebreaker para que el orden sea estable
                OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY", conexion);
            AgregarParametrosFiltro(cmd, q, categoriaId, soloConStock);
            cmd.Parameters.AddWithValue("@Offset", offset);
            cmd.Parameters.AddWithValue("@Size",   size);

            var items = new List<Producto>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new Producto
                {
                    Id            = reader.GetInt32(0),
                    Codigo        = reader.GetString(1),
                    CategoriaId   = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    Categoria     = reader.GetString(3),
                    Descripcion   = reader.GetString(4),
                    Ubicacion     = reader.GetString(5),
                    StockVina     = reader.GetInt32(6),
                    StockVa       = reader.GetInt32(7),
                    PrecioMeson   = reader.GetInt32(8),
                    PrecioMayor   = reader.GetInt32(9),
                    PrecioWeb     = reader.GetInt32(10),
                    CostoNeto     = reader.GetInt32(11),
                    Utilidad      = reader.GetInt32(12),
                    TieneImagen   = reader.GetBoolean(13),
                    Observacion   = reader.GetString(14),
                    Compatibilidad = reader.GetString(15),
                });
            }

            return Ok(new Paginado<Producto> {
                Items = items, Total = total, Page = page, Size = size,
            });
        }

        // Helper: agrega los 3 parámetros de filtro que comparten COUNT y SELECT
        private static void AgregarParametrosFiltro(SqlCommand cmd, string? q, int? categoriaId, bool soloConStock)
        {
            cmd.Parameters.AddWithValue("@Q",            (object?)q ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@QLike",        q == null ? DBNull.Value : (object)$"%{q}%");
            cmd.Parameters.AddWithValue("@CategoriaId",  (object?)categoriaId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SoloConStock", soloConStock ? 1 : 0);
        }

        // GET /api/inventario/5
        [HttpGet("{id}")]
        public IActionResult ObtenerPorId(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(@"
                SELECT
                    i.Id, i.Codigo, i.CategoriaId, COALESCE(c.Nombre, '') AS Categoria,
                    i.Descripcion, COALESCE(i.Ubicacion, ''),
                    COALESCE(i.StockVina, 0), COALESCE(i.StockVa, 0),
                    COALESCE(i.PrecioMeson, 0), COALESCE(i.PrecioMayor, 0),
                    COALESCE(i.PrecioWeb, 0), COALESCE(i.CostoNeto, 0),
                    COALESCE(i.Utilidad, 0), ISNULL(i.TieneImagen, 0),
                    COALESCE(i.Observacion, ''), COALESCE(i.Compatibilidad, '')
                FROM Inventario i
                LEFT JOIN Categorias c ON i.CategoriaId = c.Id
                WHERE i.Id = @Id", conexion);

            cmd.Parameters.AddWithValue("@Id", id);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return NotFound(new { mensaje = $"Producto con Id {id} no encontrado." });

            return Ok(new Producto
            {
                Id             = reader.GetInt32(0),
                Codigo         = reader.GetString(1),
                CategoriaId    = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Categoria      = reader.GetString(3),
                Descripcion    = reader.GetString(4),
                Ubicacion      = reader.GetString(5),
                StockVina      = reader.GetInt32(6),
                StockVa        = reader.GetInt32(7),
                PrecioMeson    = reader.GetInt32(8),
                PrecioMayor    = reader.GetInt32(9),
                PrecioWeb      = reader.GetInt32(10),
                CostoNeto      = reader.GetInt32(11),
                Utilidad       = reader.GetInt32(12),
                TieneImagen    = reader.GetBoolean(13),
                Observacion    = reader.GetString(14),
                Compatibilidad = reader.GetString(15),
            });
        }

        // POST /api/inventario
        [HttpPost]
        [RequirePermiso("productos.crear")]
        public IActionResult Crear([FromBody] Producto producto)
        {
            // ---- Validaciones de entrada (todos los campos clave) ----
            if (string.IsNullOrWhiteSpace(producto.Codigo))
                return BadRequest(new { mensaje = "El código es requerido." });
            if (string.IsNullOrWhiteSpace(producto.Descripcion))
                return BadRequest(new { mensaje = "La descripción es requerida." });
            if (producto.StockVina < 0 || producto.StockVa < 0)
                return BadRequest(new { mensaje = "El stock no puede ser negativo." });
            if (producto.PrecioMeson < 0 || producto.PrecioMayor < 0 || producto.CostoNeto < 0)
                return BadRequest(new { mensaje = "Los precios no pueden ser negativos." });

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            // Resolver CategoriaId: preferir el campo Id explícito; si no, buscar por nombre.
            // Si pasaron un nombre pero no existe en BD → error en lugar de NULL silencioso.
            int? categoriaId = ResolverCategoriaId(conexion, producto);
            if (categoriaId == null && !string.IsNullOrWhiteSpace(producto.Categoria))
                return BadRequest(new { mensaje = $"La categoría '{producto.Categoria}' no existe." });

            try
            {
                var cmd = new SqlCommand(@"
                    INSERT INTO Inventario
                        (Codigo, Descripcion, CategoriaId, Ubicacion,
                         StockVina, StockVa, PrecioMeson, PrecioMayor,
                         PrecioWeb, CostoNeto, Utilidad, TieneImagen,
                         Observacion, Compatibilidad)
                    VALUES
                        (@Codigo, @Descripcion, @CategoriaId, @Ubicacion,
                         @StockVina, @StockVa, @PrecioMeson, @PrecioMayor,
                         @PrecioWeb, @CostoNeto, @Utilidad, @TieneImagen,
                         @Observacion, @Compatibilidad)
                    ;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", conexion);

                AgregarParametros(cmd, producto, categoriaId);

                producto.Id = Convert.ToInt32(cmd.ExecuteScalar());
                producto.CategoriaId = categoriaId;
                return CreatedAtAction(nameof(ObtenerPorId), new { id = producto.Id }, producto);
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                return Conflict(new { mensaje = $"Ya existe un producto con código '{producto.Codigo}'." });
            }
        }

        // PUT /api/inventario/5
        [HttpPut("{id}")]
        public IActionResult Editar(int id, [FromBody] Producto producto)
        {
            if (string.IsNullOrWhiteSpace(producto.Codigo))
                return BadRequest(new { mensaje = "El código es requerido." });
            if (string.IsNullOrWhiteSpace(producto.Descripcion))
                return BadRequest(new { mensaje = "La descripción es requerida." });
            if (producto.StockVina < 0 || producto.StockVa < 0)
                return BadRequest(new { mensaje = "El stock no puede ser negativo." });
            if (producto.PrecioMeson < 0 || producto.PrecioMayor < 0 || producto.CostoNeto < 0)
                return BadRequest(new { mensaje = "Los precios no pueden ser negativos." });

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            int? categoriaId = ResolverCategoriaId(conexion, producto);
            if (categoriaId == null && !string.IsNullOrWhiteSpace(producto.Categoria))
                return BadRequest(new { mensaje = $"La categoría '{producto.Categoria}' no existe." });

            var cmd = new SqlCommand(@"
                UPDATE Inventario SET
                    Codigo        = @Codigo,
                    Descripcion   = @Descripcion,
                    CategoriaId   = @CategoriaId,
                    Ubicacion     = @Ubicacion,
                    StockVina     = @StockVina,
                    StockVa       = @StockVa,
                    PrecioMeson   = @PrecioMeson,
                    PrecioMayor   = @PrecioMayor,
                    PrecioWeb     = @PrecioWeb,
                    CostoNeto     = @CostoNeto,
                    Utilidad      = @Utilidad,
                    TieneImagen   = @TieneImagen,
                    Observacion   = @Observacion,
                    Compatibilidad = @Compatibilidad
                WHERE Id = @Id", conexion);

            AgregarParametros(cmd, producto, categoriaId);
            cmd.Parameters.AddWithValue("@Id", id);

            int filas = cmd.ExecuteNonQuery();
            if (filas == 0)
                return NotFound(new { mensaje = $"Producto con Id {id} no encontrado." });

            return NoContent();
        }

        // DELETE /api/inventario/5
        [HttpDelete("{id}")]
        [RequirePermiso("productos.eliminar")]
        public IActionResult Eliminar(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand("DELETE FROM Inventario WHERE Id = @Id", conexion);
            cmd.Parameters.AddWithValue("@Id", id);

            int filas = cmd.ExecuteNonQuery();
            if (filas == 0)
                return NotFound(new { mensaje = $"Producto con Id {id} no encontrado." });

            return NoContent();
        }

        // ---- Helpers privados ----

        // Resuelve qué CategoriaId usar siguiendo este orden:
        //   1. Si el cliente envió CategoriaId explícito → validar que exista y usarlo
        //   2. Si no, pero envió un nombre → buscar por nombre
        //   3. Si nada de eso → null (producto sin categoría)
        private int? ResolverCategoriaId(SqlConnection conexion, Producto p)
        {
            // Camino 1: id explícito (preferido)
            if (p.CategoriaId.HasValue)
            {
                var cmd = new SqlCommand("SELECT 1 FROM Categorias WHERE Id = @Id", conexion);
                cmd.Parameters.AddWithValue("@Id", p.CategoriaId.Value);
                return cmd.ExecuteScalar() != null ? p.CategoriaId.Value : null;
            }

            // Camino 2: buscar por nombre
            if (!string.IsNullOrWhiteSpace(p.Categoria))
            {
                var cmd = new SqlCommand("SELECT Id FROM Categorias WHERE Nombre = @Nombre", conexion);
                cmd.Parameters.AddWithValue("@Nombre", p.Categoria);
                var r = cmd.ExecuteScalar();
                return r != null ? Convert.ToInt32(r) : null;
            }

            return null;
        }

        // Centraliza los parámetros comunes entre INSERT y UPDATE
        private void AgregarParametros(SqlCommand cmd, Producto p, int? categoriaId)
        {
            cmd.Parameters.AddWithValue("@Codigo",         p.Codigo);
            cmd.Parameters.AddWithValue("@Descripcion",    p.Descripcion);
            cmd.Parameters.AddWithValue("@CategoriaId",    (object?)categoriaId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Ubicacion",      p.Ubicacion);
            cmd.Parameters.AddWithValue("@StockVina",      p.StockVina);
            cmd.Parameters.AddWithValue("@StockVa",        p.StockVa);
            cmd.Parameters.AddWithValue("@PrecioMeson",    p.PrecioMeson);
            cmd.Parameters.AddWithValue("@PrecioMayor",    p.PrecioMayor);
            cmd.Parameters.AddWithValue("@PrecioWeb",      p.PrecioWeb);
            cmd.Parameters.AddWithValue("@CostoNeto",      p.CostoNeto);
            cmd.Parameters.AddWithValue("@Utilidad",       p.Utilidad);
            cmd.Parameters.AddWithValue("@TieneImagen",    p.TieneImagen);
            cmd.Parameters.AddWithValue("@Observacion",    p.Observacion);
            cmd.Parameters.AddWithValue("@Compatibilidad", p.Compatibilidad);
        }
    }
}