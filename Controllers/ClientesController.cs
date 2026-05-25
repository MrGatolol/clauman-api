using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaumanAPI.Models;

namespace ClaumanAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClientesController : ControllerBase
    {
        private readonly string _conexion;

        public ClientesController(IConfiguration config)
        {
            _conexion = config.GetConnectionString("ClaumanDB")!;
        }

        // GET /api/clientes
        [HttpGet]
        public IActionResult ObtenerTodos()
        {
            var lista = new List<Cliente>();

            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(
                "SELECT Id, Rut, Nombre, Direccion, Comuna, Ciudad, Giro, Telefono, EmailCorp, EmailCot FROM Clientes ORDER BY Nombre",
                conexion
            );
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                lista.Add(new Cliente
                {
                    Id        = reader.GetInt32(0),
                    Rut       = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Nombre    = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Direccion = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Comuna    = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Ciudad    = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Giro      = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    Telefono  = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    EmailCorp = reader.IsDBNull(8) ? "" : reader.GetString(8),
                    EmailCot  = reader.IsDBNull(9) ? "" : reader.GetString(9),
                });
            }

            return Ok(lista);
        }

        // GET /api/clientes/5  — un cliente específico
        [HttpGet("{id}")]
        public IActionResult ObtenerPorId(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(
                "SELECT Id, Rut, Nombre, Direccion, Comuna, Ciudad, Giro, Telefono, EmailCorp, EmailCot FROM Clientes WHERE Id = @Id",
                conexion
            );
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return NotFound(new { mensaje = $"Cliente {id} no encontrado." });

            return Ok(new Cliente
            {
                Id        = reader.GetInt32(0),
                Rut       = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Nombre    = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Direccion = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Comuna    = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Ciudad    = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Giro      = reader.IsDBNull(6) ? "" : reader.GetString(6),
                Telefono  = reader.IsDBNull(7) ? "" : reader.GetString(7),
                EmailCorp = reader.IsDBNull(8) ? "" : reader.GetString(8),
                EmailCot  = reader.IsDBNull(9) ? "" : reader.GetString(9),
            });
        }

        // POST /api/clientes
        [HttpPost]
        public IActionResult Crear([FromBody] Cliente cliente)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(
                @"INSERT INTO Clientes (Rut, Nombre, Direccion, Comuna, Ciudad, Giro, Telefono, EmailCorp, EmailCot)
                  VALUES (@Rut, @Nombre, @Direccion, @Comuna, @Ciudad, @Giro, @Telefono, @EmailCorp, @EmailCot);
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                conexion
            );

            cmd.Parameters.AddWithValue("@Rut",       cliente.Rut);
            cmd.Parameters.AddWithValue("@Nombre",    cliente.Nombre.ToUpper());
            cmd.Parameters.AddWithValue("@Direccion", cliente.Direccion);
            cmd.Parameters.AddWithValue("@Comuna",    cliente.Comuna);
            cmd.Parameters.AddWithValue("@Ciudad",    cliente.Ciudad);
            cmd.Parameters.AddWithValue("@Giro",      cliente.Giro);
            cmd.Parameters.AddWithValue("@Telefono",  cliente.Telefono);
            cmd.Parameters.AddWithValue("@EmailCorp", cliente.EmailCorp);
            cmd.Parameters.AddWithValue("@EmailCot",  cliente.EmailCot);

            cliente.Id = Convert.ToInt32(cmd.ExecuteScalar());
            return CreatedAtAction(nameof(ObtenerTodos), cliente);
        }

        // PUT /api/clientes/5
        [HttpPut("{id}")]
        public IActionResult Editar(int id, [FromBody] Cliente cliente)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            var cmd = new SqlCommand(
                @"UPDATE Clientes
                  SET Rut=@Rut, Nombre=@Nombre, Direccion=@Direccion, Comuna=@Comuna,
                      Ciudad=@Ciudad, Giro=@Giro, Telefono=@Telefono, EmailCorp=@EmailCorp, EmailCot=@EmailCot
                  WHERE Id = @Id",
                conexion
            );

            cmd.Parameters.AddWithValue("@Id",        id);
            cmd.Parameters.AddWithValue("@Rut",       cliente.Rut);
            cmd.Parameters.AddWithValue("@Nombre",    cliente.Nombre.ToUpper());
            cmd.Parameters.AddWithValue("@Direccion", cliente.Direccion);
            cmd.Parameters.AddWithValue("@Comuna",    cliente.Comuna);
            cmd.Parameters.AddWithValue("@Ciudad",    cliente.Ciudad);
            cmd.Parameters.AddWithValue("@Giro",      cliente.Giro);
            cmd.Parameters.AddWithValue("@Telefono",  cliente.Telefono);
            cmd.Parameters.AddWithValue("@EmailCorp", cliente.EmailCorp);
            cmd.Parameters.AddWithValue("@EmailCot",  cliente.EmailCot);

            int filas = cmd.ExecuteNonQuery();
            if (filas == 0)
                return NotFound(new { mensaje = $"Cliente {id} no encontrado." });

            return NoContent();
        }

        // DELETE /api/clientes/5
        [HttpDelete("{id}")]
        public IActionResult Eliminar(int id)
        {
            using var conexion = new SqlConnection(_conexion);
            conexion.Open();

            try
            {
                var cmd = new SqlCommand("DELETE FROM Clientes WHERE Id = @Id", conexion);
                cmd.Parameters.AddWithValue("@Id", id);

                int filas = cmd.ExecuteNonQuery();
                if (filas == 0)
                    return NotFound(new { mensaje = $"Cliente {id} no encontrado." });

                return NoContent();
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                return Conflict(new {
                    mensaje = $"No se puede eliminar el cliente {id} porque tiene boletas/facturas/cotizaciones asociadas. " +
                              "Conservar el historial requiere mantener el cliente."
                });
            }
        }
    }
}