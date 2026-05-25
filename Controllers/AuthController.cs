using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using ClaumanAPI.Models;

namespace ClaumanAPI.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly string _conexion;

        public AuthController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // POST /api/auth/login  body: { username, password }
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest(new { mensaje = "Usuario y contraseña son requeridos." });

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            // Buscar usuario activo
            var cmd = new SqlCommand(@"
                SELECT Id, Nombre, Username, PasswordHash, Rol, Activo
                FROM Usuarios
                WHERE Username = @Username", conexion);
            cmd.Parameters.AddWithValue("@Username", req.Username);

            int      id        = 0;
            string   nombre    = "";
            string   username  = "";
            string   hashBd    = "";
            string   rol       = "";
            bool     activo    = false;
            bool     encontrado = false;

            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    id        = reader.GetInt32(0);
                    nombre    = reader.GetString(1);
                    username  = reader.GetString(2);
                    hashBd    = reader.GetString(3);
                    rol       = reader.GetString(4);
                    activo    = reader.GetBoolean(5);
                    encontrado = true;
                }
            }

            // Hashear el password recibido y comparar (timing-safe)
            string hashEntrada = HashSha256(req.Password);
            bool credencialesOk = encontrado && activo && TimingSafeEquals(hashBd, hashEntrada);

            // Registrar en bitácora SIEMPRE (exitoso o no)
            var cmdLog = new SqlCommand(@"
                INSERT INTO AccesosLog (UsuarioId, Username, Fecha, Exito)
                VALUES (@UsuarioId, @Username, GETDATE(), @Exito)", conexion);
            cmdLog.Parameters.AddWithValue("@UsuarioId", encontrado ? (object)id : DBNull.Value);
            cmdLog.Parameters.AddWithValue("@Username",  req.Username);
            cmdLog.Parameters.AddWithValue("@Exito",     credencialesOk);
            cmdLog.ExecuteNonQuery();

            if (!credencialesOk)
                return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });

            // Generamos el token y lo persistimos en SesionTokens con expiración de 8 horas.
            // Cada request siguiente debe traer este token en el header Authorization, y
            // el AuthMiddleware lo valida contra la BD antes de dejar pasar.
            var token = Guid.NewGuid().ToString("N");
            var expira = DateTime.UtcNow.AddHours(8);
            var cmdToken = new SqlCommand(@"
                INSERT INTO SesionTokens (Token, UsuarioId, ExpiraEn)
                VALUES (@Token, @UsuarioId, @ExpiraEn)", conexion);
            cmdToken.Parameters.AddWithValue("@Token", token);
            cmdToken.Parameters.AddWithValue("@UsuarioId", id);
            cmdToken.Parameters.AddWithValue("@ExpiraEn", expira);
            cmdToken.ExecuteNonQuery();

            return Ok(new LoginResponse
            {
                Id       = id,
                Nombre   = nombre,
                Username = username,
                Rol      = rol,
                Token    = token,
            });
        }

        // POST /api/auth/logout — invalida el token actual (lo borra de SesionTokens).
        // No requiere body, se identifica por el header Authorization.
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var auth)) return NoContent();
            var token = auth.ToString().Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(token)) return NoContent();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();
            var cmd = new SqlCommand("DELETE FROM SesionTokens WHERE Token = @Token", conexion);
            cmd.Parameters.AddWithValue("@Token", token);
            cmd.ExecuteNonQuery();
            return NoContent();
        }

        // POST /api/auth/cambiar-password  body: { username, passwordActual, passwordNueva }
        [HttpPost("cambiar-password")]
        public IActionResult CambiarPassword([FromBody] Dictionary<string, string> body)
        {
            if (!body.TryGetValue("username", out var username) ||
                !body.TryGetValue("passwordActual", out var actual) ||
                !body.TryGetValue("passwordNueva", out var nueva))
                return BadRequest(new { mensaje = "Faltan campos requeridos." });

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand("SELECT PasswordHash FROM Usuarios WHERE Username = @U", conexion);
            cmd.Parameters.AddWithValue("@U", username);
            var hashActual = cmd.ExecuteScalar() as string;

            if (hashActual == null || !TimingSafeEquals(hashActual, HashSha256(actual)))
                return Unauthorized(new { mensaje = "Contraseña actual incorrecta." });

            if (nueva.Length < 6)
                return BadRequest(new { mensaje = "La nueva contraseña debe tener al menos 6 caracteres." });
            if (nueva == actual)
                return BadRequest(new { mensaje = "La nueva contraseña debe ser distinta de la actual." });

            // Actualizar password + invalidar TODOS los tokens del usuario
            // (si alguien tenía la contraseña vieja con un token abierto, queda fuera)
            var cmdUpd = new SqlCommand(
                "UPDATE Usuarios SET PasswordHash = @H WHERE Username = @U", conexion);
            cmdUpd.Parameters.AddWithValue("@H", HashSha256(nueva));
            cmdUpd.Parameters.AddWithValue("@U", username);
            cmdUpd.ExecuteNonQuery();

            var cmdDelTokens = new SqlCommand(@"
                DELETE FROM SesionTokens
                WHERE UsuarioId IN (SELECT Id FROM Usuarios WHERE Username = @U)", conexion);
            cmdDelTokens.Parameters.AddWithValue("@U", username);
            cmdDelTokens.ExecuteNonQuery();

            return NoContent();
        }

        // ---- Helpers ----
        private static string HashSha256(string texto)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(texto));
            var sb = new StringBuilder();
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        // Comparación constante para evitar timing attacks
        private static bool TimingSafeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
