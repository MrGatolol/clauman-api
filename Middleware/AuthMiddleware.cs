using Microsoft.Data.SqlClient;

namespace ClaumanAPI.Middleware
{
    /// <summary>
    /// Middleware que valida el header Authorization en cada request.
    /// Si la ruta es pública (login, swagger), pasa sin validar.
    /// Si es protegida, busca el token en SesionTokens y verifica expiración.
    /// Si el token es válido, refresca UltimoUso y agrega UsuarioId al HttpContext.
    /// </summary>
    public class AuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _conexion;

        // Rutas que NO requieren autenticación
        private static readonly string[] RutasPublicas = new[]
        {
            "/api/auth/login",
            "/swagger",
            "/health",       // Render health check — debe ser público para que el ping funcione
        };

        public AuthMiddleware(RequestDelegate next, IConfiguration config)
        {
            _next = next;
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        public async Task InvokeAsync(HttpContext ctx)
        {
            var path = ctx.Request.Path.Value ?? "";

            // Si es una ruta pública o no es /api/*, pasa sin validar
            bool requiereAuth = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                                && !RutasPublicas.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase));

            if (!requiereAuth)
            {
                await _next(ctx);
                return;
            }

            // Leer token del header Authorization
            if (!ctx.Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                await ResponderNoAutorizado(ctx, "Falta header Authorization.");
                return;
            }

            var token = authHeader.ToString().Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(token))
            {
                await ResponderNoAutorizado(ctx, "Token vacío.");
                return;
            }

            // Validar contra BD
            using var conexion = new SqlConnection(_conexion);
            await conexion.OpenAsync();
            var cmd = new SqlCommand(@"
                SELECT UsuarioId, ExpiraEn FROM SesionTokens WHERE Token = @Token", conexion);
            cmd.Parameters.AddWithValue("@Token", token);

            int? usuarioId = null;
            DateTime? expira = null;
            using (var rd = await cmd.ExecuteReaderAsync())
            {
                if (await rd.ReadAsync())
                {
                    usuarioId = rd.GetInt32(0);
                    expira    = rd.GetDateTime(1);
                }
            }

            if (usuarioId == null)
            {
                await ResponderNoAutorizado(ctx, "Token inválido. Vuelve a iniciar sesión.");
                return;
            }

            if (expira < DateTime.UtcNow)
            {
                // Limpiar el token vencido
                var cmdDel = new SqlCommand("DELETE FROM SesionTokens WHERE Token = @T", conexion);
                cmdDel.Parameters.AddWithValue("@T", token);
                await cmdDel.ExecuteNonQueryAsync();
                await ResponderNoAutorizado(ctx, "Tu sesión expiró. Vuelve a iniciar sesión.");
                return;
            }

            // Refrescar UltimoUso (sliding session — extiende cada uso por 8h más)
            var cmdRefresh = new SqlCommand(
                "UPDATE SesionTokens SET UltimoUso = GETDATE(), ExpiraEn = @E WHERE Token = @T",
                conexion);
            cmdRefresh.Parameters.AddWithValue("@T", token);
            cmdRefresh.Parameters.AddWithValue("@E", DateTime.UtcNow.AddHours(8));
            await cmdRefresh.ExecuteNonQueryAsync();

            // Guardar usuarioId en el context para que los controllers puedan leerlo si quieren
            ctx.Items["UsuarioId"] = usuarioId.Value;

            await _next(ctx);
        }

        private static async Task ResponderNoAutorizado(HttpContext ctx, string mensaje)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync($"{{\"mensaje\":\"{mensaje}\"}}");
        }
    }
}
