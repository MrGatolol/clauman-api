using Microsoft.AspNetCore.Mvc;
using Npgsql;
using ClaumanAPI.Models;
using ClaumanAPI.Middleware;

namespace ClaumanAPI.Controllers
{
    [Route("api/cambio-precios")]
    [ApiController]
    public class CambioPreciosController : ControllerBase
    {
        private readonly string _conexion;

        public CambioPreciosController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // POST /api/cambio-precios/preview
        // Calcula los precios nuevos sin aplicarlos — devuelve un preview para que el usuario confirme.
        [HttpPost("preview")]
        public IActionResult Preview([FromBody] CambioPreciosRequest req)
        {
            var lista = new List<PreviewCambioPrecio>();
            decimal factor = 1 + (req.Porcentaje / 100m);

            using var conexion = new NpgsqlConnection(_conexion);
            conexion.Open();

            var sql = @"
                SELECT i.Id,
                       COALESCE(i.Codigo, '')      AS Codigo,
                       COALESCE(c.Nombre, '')      AS Categoria,
                       COALESCE(i.Descripcion, '') AS Descripcion,
                       COALESCE(i.PrecioMeson, 0)  AS PrecioMeson,
                       COALESCE(i.PrecioMayor, 0)  AS PrecioMayor
                FROM Inventario i
                LEFT JOIN Categorias c ON c.Id = i.CategoriaId
                WHERE (@CategoriaId::int IS NULL OR i.CategoriaId = @CategoriaId::int)
                ORDER BY i.Descripcion";

            var cmd = new NpgsqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@CategoriaId", (object?)req.CategoriaId ?? DBNull.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int pMeson = reader.GetInt32(4);
                int pMayor = reader.GetInt32(5);

                bool afectaMeson = req.TipoPrecio == "MESON"     || req.TipoPrecio == "AMBOS";
                bool afectaMayor = req.TipoPrecio == "MAYORISTA" || req.TipoPrecio == "AMBOS";

                lista.Add(new PreviewCambioPrecio
                {
                    Id                = reader.GetInt32(0),
                    Codigo            = reader.GetString(1),
                    Categoria         = reader.GetString(2),
                    Descripcion       = reader.GetString(3),
                    PrecioMesonActual = pMeson,
                    PrecioMesonNuevo  = afectaMeson ? (int)Math.Round(pMeson * factor) : pMeson,
                    PrecioMayorActual = pMayor,
                    PrecioMayorNuevo  = afectaMayor ? (int)Math.Round(pMayor * factor) : pMayor,
                });
            }
            return Ok(lista);
        }

        // POST /api/cambio-precios/aplicar
        // Aplica el cambio masivo de precios a la BD — solo ADMIN
        [HttpPost("aplicar")]
        [RequireRol("ADMIN")]
        public IActionResult Aplicar([FromBody] CambioPreciosRequest req)
        {
            if (req.Porcentaje == 0)
                return BadRequest(new { mensaje = "El porcentaje no puede ser 0." });

            decimal factor = 1 + (req.Porcentaje / 100m);

            using var conexion = new NpgsqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                string set = req.TipoPrecio switch
                {
                    "MESON"     => "PrecioMeson = CAST(COALESCE(PrecioMeson, 0) * @Factor AS INT)",
                    "MAYORISTA" => "PrecioMayor = CAST(COALESCE(PrecioMayor, 0) * @Factor AS INT)",
                    _           => "PrecioMeson = CAST(COALESCE(PrecioMeson, 0) * @Factor AS INT), " +
                                   "PrecioMayor = CAST(COALESCE(PrecioMayor, 0) * @Factor AS INT)",
                };

                var sql = $@"
                    UPDATE Inventario
                    SET {set}
                    WHERE (@CategoriaId::int IS NULL OR CategoriaId = @CategoriaId::int)";

                var cmd = new NpgsqlCommand(sql, conexion, tx);
                cmd.Parameters.AddWithValue("@Factor",      factor);
                cmd.Parameters.AddWithValue("@CategoriaId", (object?)req.CategoriaId ?? DBNull.Value);
                int afectados = cmd.ExecuteNonQuery();

                tx.Commit();
                return Ok(new { mensaje = $"Se actualizaron {afectados} producto(s).", afectados });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al aplicar el cambio.", detalle = ex.Message });
            }
        }
    }
}
