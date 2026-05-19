namespace ClaumanAPI.Models
{
    public class Usuario
    {
        public int Id { get; set; }
        public string? Rut { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Rol { get; set; } = "CAJERO";    // ADMIN | VENDEDOR | CAJERO
        public bool Activo { get; set; } = true;
        public string? Permisos { get; set; }           // JSON string con la matriz de permisos
        // PasswordHash NUNCA se devuelve al cliente
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class LoginResponse
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Rol { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;   // simple identifier, no JWT por ahora
    }

    public class CrearUsuarioRequest
    {
        public string? Rut { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Rol { get; set; } = "CAJERO";
        public string? Permisos { get; set; }
    }
}
