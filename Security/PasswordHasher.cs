using System.Security.Cryptography;
using System.Text;

namespace ClaumanAPI.Security
{
    /// <summary>
    /// Hashing y verificación de contraseñas con migración transparente
    /// SHA256 (legacy) → BCrypt (nuevo).
    ///
    /// Cómo identificamos cuál algoritmo se usó:
    ///   - BCrypt: el hash empieza con "$2a$", "$2b$" o "$2y$"
    ///   - SHA256 legacy: 64 caracteres hex (32 bytes en hex)
    ///
    /// Flujo de migración:
    ///   1. Usuarios existentes tienen hash SHA256. Siguen pudiendo loguear
    ///      mientras Verify() acepte ambos formatos.
    ///   2. En cada login exitoso con hash SHA256, devolvemos NeedsRehash=true.
    ///      AuthController re-guarda el hash usando BCrypt.
    ///   3. Eventualmente todos los hashes se migran solos, sin pedirle al
    ///      usuario que cambie la contraseña.
    ///
    /// BCrypt work factor: 11 (~150ms en hardware moderno — defensa contra
    /// brute force sin frustrar al usuario en el login).
    /// </summary>
    public static class PasswordHasher
    {
        private const int BCryptWorkFactor = 11;

        /// <summary>Hashea una password con BCrypt. Genera salt aleatorio internamente.</summary>
        public static string Hash(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("password no puede ser vacío", nameof(password));
            return BCrypt.Net.BCrypt.HashPassword(password, BCryptWorkFactor);
        }

        /// <summary>
        /// Verifica una password contra un hash almacenado. Acepta tanto BCrypt como SHA256 legacy.
        /// </summary>
        /// <param name="passwordPlano">la password que el usuario tipeó</param>
        /// <param name="hashAlmacenado">el hash que está en la BD</param>
        /// <param name="needsRehash">
        /// true si el hash almacenado es SHA256 legacy — el caller debería re-hashear
        /// y guardar usando <see cref="Hash"/> para migrar a BCrypt transparente.
        /// </param>
        public static bool Verify(string passwordPlano, string hashAlmacenado, out bool needsRehash)
        {
            needsRehash = false;
            if (string.IsNullOrEmpty(hashAlmacenado) || string.IsNullOrEmpty(passwordPlano))
                return false;

            // Hashes BCrypt empiezan con "$2a$", "$2b$" o "$2y$"
            if (hashAlmacenado.StartsWith("$2"))
            {
                try { return BCrypt.Net.BCrypt.Verify(passwordPlano, hashAlmacenado); }
                catch { return false; }
            }

            // SHA256 legacy: hex de 64 caracteres (32 bytes)
            if (hashAlmacenado.Length == 64 && IsHex(hashAlmacenado))
            {
                var hashEntrada = Sha256Hex(passwordPlano);
                bool ok = TimingSafeEquals(hashAlmacenado, hashEntrada);
                if (ok) needsRehash = true;   // migrar a BCrypt en el próximo guardado
                return ok;
            }

            return false;
        }

        // ---- Helpers privados ----

        private static string Sha256Hex(string texto)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(texto));
            var sb = new StringBuilder(64);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        // Comparación constant-time para evitar timing attacks.
        private static bool TimingSafeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static bool IsHex(string s)
        {
            foreach (var c in s)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }
    }
}
