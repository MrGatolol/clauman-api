using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using ClaumanAPI.Models;
using ClaumanAPI.Middleware;

namespace ClaumanAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [RequireRol("ADMIN")]   // Todo el CRUD de usuarios + bitácora es solo para ADMIN
    public class UsuariosController : ControllerBase
    {
        private readonly string _conexion;

        public UsuariosController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/usuarios
        [HttpGet]
        public IActionResult ObtenerTodos()
        {
            var lista = new List<Usuario>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(@"
                SELECT Id, Rut, Nombre, Username, Rol, Activo, Permisos
                FROM Usuarios
                ORDER BY Nombre", conexion);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new Usuario
                {
                    Id       = reader.GetInt32(0),
                    Rut      = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Nombre   = reader.GetString(2),
                    Username = reader.GetString(3),
                    Rol      = reader.GetString(4),
                    Activo   = reader.GetBoolean(5),
                    Permisos = reader.IsDBNull(6) ? null : reader.GetString(6),
                });
            }
            return Ok(lista);
        }

        // POST /api/usuarios
        [HttpPost]
        public IActionResult Crear([FromBody] CrearUsuarioRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest(new { mensaje = "Usuario y contraseña son requeridos." });
            if (string.IsNullOrWhiteSpace(req.Nombre))
                return BadRequest(new { mensaje = "El nombre es requerido." });
            if (req.Password.Length < 6)
                return BadRequest(new { mensaje = "La contraseña debe tener al menos 6 caracteres." });

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            try
            {
                var cmd = new SqlCommand(@"
                    INSERT INTO Usuarios (Rut, Nombre, Username, PasswordHash, Rol, Permisos, Activo)
                    VALUES (@Rut, @Nombre, @Username, @Hash, @Rol, @Permisos, 1)
                    ;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", conexion);

                cmd.Parameters.AddWithValue("@Rut",      (object?)req.Rut ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Nombre",   req.Nombre);
                cmd.Parameters.AddWithValue("@Username", req.Username);
                cmd.Parameters.AddWithValue("@Hash",     HashSha256(req.Password));
                cmd.Parameters.AddWithValue("@Rol",      req.Rol);
                cmd.Parameters.AddWithValue("@Permisos", (object?)req.Permisos ?? DBNull.Value);

                int id = Convert.ToInt32(cmd.ExecuteScalar());
                return CreatedAtAction(nameof(ObtenerTodos), new Usuario
                {
                    Id = id, Rut = req.Rut, Nombre = req.Nombre, Username = req.Username,
                    Rol = req.Rol, Activo = true, Permisos = req.Permisos,
                });
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                return Conflict(new { mensaje = $"El usuario '{req.Username}' ya existe." });
            }
        }

        // PUT /api/usuarios/5  — editar nombre y rol (no password)
        [HttpPut("{id}")]
        public IActionResult Editar(int id, [FromBody] Usuario u)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(@"
                UPDATE Usuarios SET
                    Rut      = @Rut,
                    Nombre   = @Nombre,
                    Rol      = @Rol,
                    Activo   = @Activo,
                    Permisos = @Permisos
                WHERE Id = @Id", conexion);
            cmd.Parameters.AddWithValue("@Rut",      (object?)u.Rut ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Nombre",   u.Nombre);
            cmd.Parameters.AddWithValue("@Rol",      u.Rol);
            cmd.Parameters.AddWithValue("@Activo",   u.Activo);
            cmd.Parameters.AddWithValue("@Permisos", (object?)u.Permisos ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Id",       id);

            if (cmd.ExecuteNonQuery() == 0)
                return NotFound(new { mensaje = $"Usuario {id} no encontrado." });
            return NoContent();
        }

        // DELETE /api/usuarios/5
        [HttpDelete("{id}")]
        public IActionResult Eliminar(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            var cmd = new SqlCommand("DELETE FROM Usuarios WHERE Id = @Id", conexion);
            cmd.Parameters.AddWithValue("@Id", id);

            if (cmd.ExecuteNonQuery() == 0)
                return NotFound(new { mensaje = $"Usuario {id} no encontrado." });
            return NoContent();
        }

        // GET /api/usuarios/accesos  — últimos 50 logins (exitosos y fallidos)
        [HttpGet("accesos")]
        public IActionResult ObtenerAccesos()
        {
            var lista = new List<Dictionary<string, object?>>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            // Postgres usa || para concatenar (no +). Y LIMIT en vez de TOP.
            var cmd = new SqlCommand(@"
                SELECT Id, Username,
                       FORMAT(Fecha, 'dd/MM/yyyy HH:mm:ss') AS Fecha,
                       Exito,
                       COALESCE(Local, '') AS Local
                FROM AccesosLog
                ORDER BY Fecha DESC
                OFFSET 0 ROWS FETCH NEXT 50 ROWS ONLY", conexion);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new Dictionary<string, object?>
                {
                    ["id"]       = reader.GetInt32(0),
                    ["username"] = reader.GetString(1),
                    ["fecha"]    = reader.GetString(2),
                    ["exito"]    = reader.GetBoolean(3),
                    ["local"]    = reader.GetString(4),
                });
            }
            return Ok(lista);
        }

        private static string HashSha256(string texto)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(texto));
            var sb = new StringBuilder();
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
