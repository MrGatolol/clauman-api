using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaumanAPI.Models;

namespace ClaumanAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoriasController : ControllerBase
    {
        private readonly string _conexion;

        public CategoriasController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/categorias
        [HttpGet]
        public IActionResult ObtenerTodas()
        {
            var lista = new List<Categoria>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand("SELECT Id, Nombre FROM Categorias ORDER BY Nombre", conexion);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
                lista.Add(new Categoria { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });

            return Ok(lista);
        }

        // POST /api/categorias
        [HttpPost]
        public IActionResult Crear([FromBody] Categoria categoria)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            // Postgres usa RETURNING en vez de SELECT SCOPE_IDENTITY()
            var cmd = new SqlCommand(
                "INSERT INTO Categorias (Nombre) VALUES (@Nombre); SELECT CAST(SCOPE_IDENTITY() AS INT);",
                conexion
            );
            cmd.Parameters.AddWithValue("@Nombre", categoria.Nombre.ToUpper());

            categoria.Id = Convert.ToInt32(cmd.ExecuteScalar());
            return CreatedAtAction(nameof(ObtenerTodas), categoria);
        }

        // DELETE /api/categorias/5
        [HttpDelete("{id}")]
        public IActionResult Eliminar(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand("DELETE FROM Categorias WHERE Id = @Id", conexion);
            cmd.Parameters.AddWithValue("@Id", id);

            int filas = cmd.ExecuteNonQuery();
            if (filas == 0)
                return NotFound(new { mensaje = $"Categoría {id} no encontrada." });

            return NoContent();
        }
    }
}