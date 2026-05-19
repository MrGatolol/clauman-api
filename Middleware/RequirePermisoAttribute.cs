using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;
using System.Text.Json;

namespace ClaumanAPI.Middleware
{
    /// <summary>
    /// Atributo que protege un endpoint contra un permiso específico de la matriz.
    /// El AuthMiddleware ya validó el token y dejó UsuarioId en HttpContext.Items.
    /// Este filtro carga el rol y la matriz de permisos del usuario, y verifica
    /// si tiene el permiso requerido. ADMIN siempre pasa.
    ///
    /// Uso:
    ///   [RequirePermiso("ventas.crearBoleta")]
    ///   public IActionResult Crear(...) { ... }
    ///
    /// Los nombres de permiso siguen el path del JSON de la matriz:
    ///   "ventas.crearBoleta", "inventario.ajustes", "productos.eliminar", etc.
    /// </summary>
    /// <summary>
    /// Variante para endpoints que requieren un rol específico (ej: solo ADMIN puede
    /// crear/eliminar usuarios). Más simple y directo que RequirePermiso para casos
    /// donde la matriz no aplica.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class RequireRolAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string[] _rolesPermitidos;

        public RequireRolAttribute(params string[] rolesPermitidos)
        {
            _rolesPermitidos = rolesPermitidos;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
        {
            if (!ctx.HttpContext.Items.TryGetValue("UsuarioId", out var userIdObj) || userIdObj is not int usuarioId)
            {
                ctx.Result = new ObjectResult(new { mensaje = "Sin contexto de usuario." }) { StatusCode = 500 };
                return;
            }

            var config = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            await using var conexion = new NpgsqlConnection(config.GetConnectionString("ClaumanDB")!);
            await conexion.OpenAsync();

            var cmd = new NpgsqlCommand("SELECT Rol FROM Usuarios WHERE Id = @Id", conexion);
            cmd.Parameters.AddWithValue("@Id", usuarioId);
            var rol = (await cmd.ExecuteScalarAsync()) as string ?? "";

            if (!_rolesPermitidos.Contains(rol, StringComparer.OrdinalIgnoreCase))
            {
                ctx.Result = new ObjectResult(new {
                    mensaje = $"Tu rol ({rol}) no tiene permiso para esta acción. Requiere: {string.Join(", ", _rolesPermitidos)}."
                }) { StatusCode = 403 };
                return;
            }

            await next();
        }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class RequirePermisoAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _permiso;

        public RequirePermisoAttribute(string permiso)
        {
            _permiso = permiso;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
        {
            // El AuthMiddleware ya dejó UsuarioId. Si no hay, es bug — 500.
            if (!ctx.HttpContext.Items.TryGetValue("UsuarioId", out var userIdObj) || userIdObj is not int usuarioId)
            {
                ctx.Result = new ObjectResult(new { mensaje = "Sin contexto de usuario." }) { StatusCode = 500 };
                return;
            }

            // Resolver connection string desde DI
            var config = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var conexion = new NpgsqlConnection(config.GetConnectionString("ClaumanDB")!);
            await conexion.OpenAsync();

            // Leer rol + permisos
            var cmd = new NpgsqlCommand(
                "SELECT Rol, ISNULL(Permisos, '') FROM Usuarios WHERE Id = @Id", conexion);
            cmd.Parameters.AddWithValue("@Id", usuarioId);

            string rol = "";
            string permisosJson = "";
            using (var rd = await cmd.ExecuteReaderAsync())
            {
                if (!await rd.ReadAsync())
                {
                    ctx.Result = new ObjectResult(new { mensaje = "Usuario no existe." }) { StatusCode = 401 };
                    return;
                }
                rol = rd.GetString(0);
                permisosJson = rd.GetString(1);
            }
            await conexion.CloseAsync();

            // ADMIN tiene acceso total — sin chequear matriz
            if (rol.Equals("ADMIN", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            // Si el usuario no tiene matriz definida, no tiene NINGÚN permiso (deny by default)
            if (string.IsNullOrWhiteSpace(permisosJson))
            {
                ctx.Result = new ObjectResult(new {
                    mensaje = $"Tu rol ({rol}) no tiene permiso para '{_permiso}'."
                }) { StatusCode = 403 };
                return;
            }

            // Parsear el JSON y navegar el path: "ventas.crearBoleta" → permisos.ventas.crearBoleta
            try
            {
                using var doc = JsonDocument.Parse(permisosJson);
                JsonElement actual = doc.RootElement;
                foreach (var parte in _permiso.Split('.'))
                {
                    if (!actual.TryGetProperty(parte, out actual))
                    {
                        ctx.Result = new ObjectResult(new {
                            mensaje = $"Permiso '{_permiso}' no encontrado en tu matriz."
                        }) { StatusCode = 403 };
                        return;
                    }
                }

                // El valor final debe ser true (boolean) para permitir
                bool tienePermiso = actual.ValueKind == JsonValueKind.True;
                if (!tienePermiso)
                {
                    ctx.Result = new ObjectResult(new {
                        mensaje = $"Tu rol ({rol}) no tiene permiso para '{_permiso}'."
                    }) { StatusCode = 403 };
                    return;
                }
            }
            catch (JsonException)
            {
                ctx.Result = new ObjectResult(new {
                    mensaje = "Matriz de permisos corrupta. Pide a un admin que te la regenere."
                }) { StatusCode = 500 };
                return;
            }

            await next();
        }
    }
}
