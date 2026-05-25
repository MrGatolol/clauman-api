using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClaumanAPI.Tests
{
    /// <summary>
    /// Tests de integración que pegan al API REAL corriendo en localhost:5296.
    /// Antes de correrlos, asegúrate de tener el API levantada con `dotnet run`.
    ///
    /// Estos tests cubren los caminos críticos:
    ///   - Login devuelve token válido
    ///   - Rechaza credenciales malas
    ///   - Endpoints protegidos rechazan sin token
    ///   - Permisos por rol funcionan (CAJERO vs ADMIN)
    ///   - El stock baja al vender y sube al anular
    ///   - Stock insuficiente rechaza con 400
    /// </summary>
    public class IntegrationTests : IClassFixture<HttpClientFixture>
    {
        private readonly HttpClient _http;

        public IntegrationTests(HttpClientFixture fix)
        {
            _http = fix.Http;
        }

        // ===================== AUTH =====================

        [Fact]
        public async Task Login_ConCredencialesCorrectas_DevuelveToken()
        {
            var res = await _http.PostAsJsonAsync("/api/auth/login",
                new { username = "admin", password = "admin123" });

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty("token").GetString()!.Length > 10);
            Assert.Equal("ADMIN", body.GetProperty("rol").GetString());
        }

        [Fact]
        public async Task Login_ConPasswordIncorrecta_Devuelve401()
        {
            var res = await _http.PostAsJsonAsync("/api/auth/login",
                new { username = "admin", password = "incorrecta" });

            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }

        [Fact]
        public async Task Login_ConUsuarioInexistente_Devuelve401()
        {
            var res = await _http.PostAsJsonAsync("/api/auth/login",
                new { username = "fantasma_x", password = "x" });

            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }

        // ===================== PROTECCIÓN POR TOKEN =====================

        [Fact]
        public async Task SinToken_GetCategorias_Devuelve401()
        {
            var http = new HttpClient { BaseAddress = new Uri("http://localhost:5296") };
            var res = await http.GetAsync("/api/categorias");
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }

        [Fact]
        public async Task ConToken_GetCategorias_Devuelve200()
        {
            var token = await LoginAsync("admin", "admin123");
            _http.DefaultRequestHeaders.Authorization = new("Bearer", token);

            var res = await _http.GetAsync("/api/categorias");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        // ===================== PERMISOS POR ROL =====================

        [Fact]
        public async Task Cajero_AccedeAUsuarios_Devuelve403()
        {
            var token = await LoginAsync("cajero1", "cajero123");
            _http.DefaultRequestHeaders.Authorization = new("Bearer", token);

            var res = await _http.GetAsync("/api/usuarios");
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        [Fact]
        public async Task Admin_AccedeAUsuarios_Devuelve200()
        {
            var token = await LoginAsync("admin", "admin123");
            _http.DefaultRequestHeaders.Authorization = new("Bearer", token);

            var res = await _http.GetAsync("/api/usuarios");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        // ===================== VALIDACIONES =====================

        [Fact]
        public async Task CrearBoleta_ConCantidadCero_Devuelve400()
        {
            var token = await LoginAsync("admin", "admin123");
            _http.DefaultRequestHeaders.Authorization = new("Bearer", token);

            var prods = await _http.GetFromJsonAsync<JsonElement>("/api/inventario");
            var primerProd = prods[0];

            var body = new {
                clienteId = (int?)null, medioPago = "EFECTIVO", descGlobal = 0,
                totalNeto = 0, iva = 0, total = 0,
                usuario = "admin", bodega = "VINA",
                detalle = new[] {
                    new {
                        productoId = primerProd.GetProperty("id").GetInt32(),
                        codigo = "X", descripcion = "Y",
                        cantidad = 0, precioUnitario = 100,
                    }
                }
            };

            var res = await _http.PostAsJsonAsync("/api/boletas", body);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        [Fact]
        public async Task CrearBoleta_ConStockInsuficiente_Devuelve400()
        {
            var token = await LoginAsync("admin", "admin123");
            _http.DefaultRequestHeaders.Authorization = new("Bearer", token);

            var prods = await _http.GetFromJsonAsync<JsonElement>("/api/inventario");
            var primerProd = prods[0];

            var body = new {
                clienteId = (int?)null, medioPago = "EFECTIVO", descGlobal = 0,
                totalNeto = 100, iva = 19, total = 119,
                usuario = "admin", bodega = "VINA",
                detalle = new[] {
                    new {
                        productoId = primerProd.GetProperty("id").GetInt32(),
                        codigo = "X", descripcion = "Y",
                        cantidad = 999999, precioUnitario = 100,
                    }
                }
            };

            var res = await _http.PostAsJsonAsync("/api/boletas", body);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        // ===================== STOCK (caso end-to-end) =====================

        [Fact]
        public async Task VenderYAnular_DejaStockIgualAlInicio()
        {
            var token = await LoginAsync("admin", "admin123");
            _http.DefaultRequestHeaders.Authorization = new("Bearer", token);

            // 1) Stock inicial
            var prods = await _http.GetFromJsonAsync<JsonElement>("/api/inventario");
            // Buscar uno con al menos 5 de stock en Viña para tener margen
            JsonElement prod = default;
            foreach (var p in prods.EnumerateArray())
            {
                if (p.GetProperty("stockVina").GetInt32() >= 5) { prod = p; break; }
            }
            Assert.NotEqual(default, prod);

            int idProd = prod.GetProperty("id").GetInt32();
            int stockInicial = prod.GetProperty("stockVina").GetInt32();

            // 2) Vender 1 unidad
            var body = new {
                clienteId = (int?)null, medioPago = "EFECTIVO", descGlobal = 0,
                totalNeto = 100, iva = 19, total = 119,
                usuario = "admin", bodega = "VINA",
                detalle = new[] {
                    new {
                        productoId = idProd,
                        codigo = prod.GetProperty("codigo").GetString(),
                        descripcion = prod.GetProperty("descripcion").GetString(),
                        cantidad = 1, precioUnitario = 119,
                    }
                }
            };
            var resVenta = await _http.PostAsJsonAsync("/api/boletas", body);
            Assert.True(resVenta.IsSuccessStatusCode,
                $"Falló la venta: {await resVenta.Content.ReadAsStringAsync()}");
            var nuevaBoleta = await resVenta.Content.ReadFromJsonAsync<JsonElement>();
            int boletaId = nuevaBoleta.GetProperty("id").GetInt32();

            // 3) Verificar que el stock bajó 1
            var prodDespues = await _http.GetFromJsonAsync<JsonElement>($"/api/inventario/{idProd}");
            int stockDespues = prodDespues.GetProperty("stockVina").GetInt32();
            Assert.Equal(stockInicial - 1, stockDespues);

            // 4) Anular y verificar que vuelve al stock inicial
            var resAnular = await _http.PutAsync($"/api/boletas/{boletaId}/anular", null);
            Assert.True(resAnular.IsSuccessStatusCode);

            var prodFinal = await _http.GetFromJsonAsync<JsonElement>($"/api/inventario/{idProd}");
            int stockFinal = prodFinal.GetProperty("stockVina").GetInt32();
            Assert.Equal(stockInicial, stockFinal);
        }

        // ===================== Helper: login y devolver token =====================
        private async Task<string> LoginAsync(string user, string pwd)
        {
            // Limpiar header anterior si lo hay
            _http.DefaultRequestHeaders.Authorization = null;
            var r = await _http.PostAsJsonAsync("/api/auth/login", new { username = user, password = pwd });
            r.EnsureSuccessStatusCode();
            var body = await r.Content.ReadFromJsonAsync<JsonElement>();
            return body.GetProperty("token").GetString()!;
        }
    }

    /// <summary>
    /// Fixture compartido: una sola instancia de HttpClient para todos los tests
    /// (más rápido que abrir conexión nueva por cada Fact).
    /// </summary>
    public class HttpClientFixture : IDisposable
    {
        public HttpClient Http { get; }
        public HttpClientFixture()
        {
            Http = new HttpClient { BaseAddress = new Uri("http://localhost:5296") };
        }
        public void Dispose() => Http.Dispose();
    }
}
