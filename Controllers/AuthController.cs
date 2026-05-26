using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using ClaumanAPI.Models;
using ClaumanAPI.Security;

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
        // Limitado a 10 intentos por minuto por IP (ver Program.cs).
        [HttpPost("login")]
        [EnableRateLimiting("login")]
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

            // Verificación con PasswordHasher: acepta BCrypt y SHA256 legacy.
            // Si era SHA256 y verificó OK, marcamos needsRehash para migrar a BCrypt.
            bool needsRehash = false;
            bool credencialesOk = encontrado && activo
                                  && PasswordHasher.Verify(req.Password, hashBd, out needsRehash);

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

            // Migración transparente SHA256 → BCrypt: si el hash era legacy,
            // re-hasheamos con BCrypt y guardamos. El usuario nunca se entera.
            if (needsRehash)
            {
                var cmdRehash = new SqlCommand(
                    "UPDATE Usuarios SET PasswordHash = @H WHERE Id = @Id", conexion);
                cmdRehash.Parameters.AddWithValue("@H",  PasswordHasher.Hash(req.Password));
                cmdRehash.Parameters.AddWithValue("@Id", id);
                cmdRehash.ExecuteNonQuery();
            }

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

        // POST /api/auth/cambiar-password  body: { passwordActual, passwordNueva }
        // El usuario se identifica via el token (HttpContext.Items["UsuarioId"]).
        // Ya no se acepta `username` en el body — eso permitía que un usuario
        // intentara cambiar la pass de otro adivinando la pass actual.
        [HttpPost("cambiar-password")]
        public IActionResult CambiarPassword([FromBody] Dictionary<string, string> body)
        {
            if (!body.TryGetValue("passwordActual", out var actual) ||
                !body.TryGetValue("passwordNueva", out var nueva))
                return BadRequest(new { mensaje = "Faltan campos requeridos (passwordActual, passwordNueva)." });

            // El AuthMiddleware ya validó el token y dejó UsuarioId en HttpContext.Items
            if (!HttpContext.Items.TryGetValue("UsuarioId", out var idObj) || idObj is not int usuarioId)
                return Unauthorized(new { mensaje = "Sesión no válida." });

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand("SELECT PasswordHash FROM Usuarios WHERE Id = @Id", conexion);
            cmd.Parameters.AddWithValue("@Id", usuarioId);
            var hashActual = cmd.ExecuteScalar() as string;

            if (hashActual == null || !PasswordHasher.Verify(actual, hashActual, out _))
                return Unauthorized(new { mensaje = "Contraseña actual incorrecta." });

            if (nueva.Length < 6)
                return BadRequest(new { mensaje = "La nueva contraseña debe tener al menos 6 caracteres." });
            if (nueva == actual)
                return BadRequest(new { mensaje = "La nueva contraseña debe ser distinta de la actual." });

            // Actualizar password (con BCrypt) + invalidar TODOS los tokens del usuario
            // (si alguien tenía la contraseña vieja con un token abierto, queda fuera)
            var cmdUpd = new SqlCommand(
                "UPDATE Usuarios SET PasswordHash = @H WHERE Id = @Id", conexion);
            cmdUpd.Parameters.AddWithValue("@H",  PasswordHasher.Hash(nueva));
            cmdUpd.Parameters.AddWithValue("@Id", usuarioId);
            cmdUpd.ExecuteNonQuery();

            var cmdDelTokens = new SqlCommand(
                "DELETE FROM SesionTokens WHERE UsuarioId = @Id", conexion);
            cmdDelTokens.Parameters.AddWithValue("@Id", usuarioId);
            cmdDelTokens.ExecuteNonQuery();

            return NoContent();
        }
    }
}
