using Microsoft.AspNetCore.Mvc;
using Npgsql;
using ClaumanAPI.Models;
using ClaumanAPI.Middleware;

namespace ClaumanAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ParametrosController : ControllerBase
    {
        private readonly string _conexion;

        public ParametrosController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/parametros  — devuelve dictionary clave→valor
        [HttpGet]
        public IActionResult ObtenerTodos()
        {
            var dict = new Dictionary<string, string>();

            using var conexion = new NpgsqlConnection(_conexion);
            conexion.Open();
            var cmd = new NpgsqlCommand("SELECT Clave, Valor FROM Parametros", conexion);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                dict[reader.GetString(0)] = reader.GetString(1);

            return Ok(dict);
        }

        // PUT /api/parametros  body: { clave1: 'valor1', clave2: 'valor2', ... }
        // Actualiza varios parámetros en una sola llamada — solo ADMIN
        [HttpPut]
        [RequireRol("ADMIN")]
        public IActionResult Actualizar([FromBody] Dictionary<string, string> cambios)
        {
            if (cambios.Count == 0)
                return BadRequest(new { mensaje = "No se enviaron cambios." });

            using var conexion = new NpgsqlConnection(_conexion);
            conexion.Open();
            using var tx = conexion.BeginTransaction();

            try
            {
                foreach (var kv in cambios)
                {
                    // UPSERT: si existe actualiza, si no inserta
                    var cmd = new NpgsqlCommand(@"
                        IF EXISTS (SELECT 1 FROM Parametros WHERE Clave = @Clave)
                            UPDATE Parametros SET Valor = @Valor WHERE Clave = @Clave
                        ELSE
                            INSERT INTO Parametros (Clave, Valor) VALUES (@Clave, @Valor);", conexion, tx);
                    cmd.Parameters.AddWithValue("@Clave", kv.Key);
                    cmd.Parameters.AddWithValue("@Valor", kv.Value ?? "");
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return NoContent();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new { mensaje = "Error al guardar parámetros.", detalle = ex.Message });
            }
        }
    }
}
