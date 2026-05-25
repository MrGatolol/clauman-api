using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaumanAPI.Models;

namespace ClaumanAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProveedoresController : ControllerBase
    {
        private readonly string _conexion;

        public ProveedoresController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/proveedores
        [HttpGet]
        public IActionResult ObtenerTodos()
        {
            var lista = new List<Proveedor>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(@"
                SELECT Id, Rut, RazonSocial, Direccion, Ciudad, Telefono,
                       EmailCorp, Ejecutivo, EmailCot,
                       Banco, CtaCte, RutTitular, EmailPagos
                FROM Proveedores
                ORDER BY RazonSocial", conexion);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lista.Add(new Proveedor
                {
                    Id          = reader.GetInt32(0),
                    Rut         = reader.GetString(1),
                    RazonSocial = reader.GetString(2),
                    Direccion   = reader.GetString(3),
                    Ciudad      = reader.GetString(4),
                    Telefono    = reader.GetString(5),
                    EmailCorp   = reader.GetString(6),
                    Ejecutivo   = reader.GetString(7),
                    EmailCot    = reader.GetString(8),
                    Banco       = reader.GetString(9),
                    CtaCte      = reader.GetString(10),
                    RutTitular  = reader.GetString(11),
                    EmailPagos  = reader.GetString(12),
                });
            }

            return Ok(lista);
        }

        // POST /api/proveedores
        [HttpPost]
        public IActionResult Crear([FromBody] Proveedor prov)
        {
            if (string.IsNullOrWhiteSpace(prov.Rut) || string.IsNullOrWhiteSpace(prov.RazonSocial))
                return BadRequest(new { mensaje = "RUT y Razón Social son requeridos." });

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            try
            {
                var cmd = new SqlCommand(@"
                    INSERT INTO Proveedores
                        (Rut, RazonSocial, Direccion, Ciudad, Telefono, EmailCorp,
                         Ejecutivo, EmailCot, Banco, CtaCte, RutTitular, EmailPagos)
                    VALUES
                        (@Rut, @RazonSocial, @Direccion, @Ciudad, @Telefono, @EmailCorp,
                         @Ejecutivo, @EmailCot, @Banco, @CtaCte, @RutTitular, @EmailPagos)
                    ;
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", conexion);

                AgregarParametros(cmd, prov);
                prov.Id = Convert.ToInt32(cmd.ExecuteScalar());

                return CreatedAtAction(nameof(ObtenerTodos), new { id = prov.Id }, prov);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = "Error al crear el proveedor.", detalle = ex.Message });
            }
        }

        // PUT /api/proveedores/5
        [HttpPut("{id}")]
        public IActionResult Editar(int id, [FromBody] Proveedor prov)
        {
            if (string.IsNullOrWhiteSpace(prov.Rut) || string.IsNullOrWhiteSpace(prov.RazonSocial))
                return BadRequest(new { mensaje = "RUT y Razón Social son requeridos." });

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(@"
                UPDATE Proveedores SET
                    Rut         = @Rut,
                    RazonSocial = @RazonSocial,
                    Direccion   = @Direccion,
                    Ciudad      = @Ciudad,
                    Telefono    = @Telefono,
                    EmailCorp   = @EmailCorp,
                    Ejecutivo   = @Ejecutivo,
                    EmailCot    = @EmailCot,
                    Banco       = @Banco,
                    CtaCte      = @CtaCte,
                    RutTitular  = @RutTitular,
                    EmailPagos  = @EmailPagos
                WHERE Id = @Id", conexion);

            AgregarParametros(cmd, prov);
            cmd.Parameters.AddWithValue("@Id", id);

            int filas = cmd.ExecuteNonQuery();
            if (filas == 0)
                return NotFound(new { mensaje = $"Proveedor {id} no encontrado." });

            return NoContent();
        }

        // DELETE /api/proveedores/5
        [HttpDelete("{id}")]
        public IActionResult Eliminar(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            try
            {
                var cmd = new SqlCommand("DELETE FROM Proveedores WHERE Id = @Id", conexion);
                cmd.Parameters.AddWithValue("@Id", id);

                int filas = cmd.ExecuteNonQuery();
                if (filas == 0)
                    return NotFound(new { mensaje = $"Proveedor {id} no encontrado." });

                return NoContent();
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                return Conflict(new {
                    mensaje = $"No se puede eliminar el proveedor {id} porque tiene facturas de compra asociadas."
                });
            }
        }

        // Helper: agrega los 12 parámetros a un SqlCommand de INSERT/UPDATE
        private static void AgregarParametros(SqlCommand cmd, Proveedor p)
        {
            cmd.Parameters.AddWithValue("@Rut",         p.Rut);
            cmd.Parameters.AddWithValue("@RazonSocial", p.RazonSocial);
            cmd.Parameters.AddWithValue("@Direccion",   p.Direccion ?? "");
            cmd.Parameters.AddWithValue("@Ciudad",      p.Ciudad    ?? "");
            cmd.Parameters.AddWithValue("@Telefono",    p.Telefono  ?? "");
            cmd.Parameters.AddWithValue("@EmailCorp",   p.EmailCorp ?? "");
            cmd.Parameters.AddWithValue("@Ejecutivo",   p.Ejecutivo ?? "");
            cmd.Parameters.AddWithValue("@EmailCot",    p.EmailCot  ?? "");
            cmd.Parameters.AddWithValue("@Banco",       p.Banco     ?? "");
            cmd.Parameters.AddWithValue("@CtaCte",      p.CtaCte    ?? "");
            cmd.Parameters.AddWithValue("@RutTitular",  p.RutTitular?? "");
            cmd.Parameters.AddWithValue("@EmailPagos",  p.EmailPagos?? "");
        }
    }
}
