using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// =============================================================
// Rate-limiting — defensa contra brute-force en /api/auth/login.
// Política "login": máximo 10 intentos por IP cada 60 segundos.
// Si se excede, devolvemos 429 Too Many Requests.
//
// Por qué fixed window: simple, suficiente para login. Si quisieras algo
// más sofisticado (sliding window, token bucket) está disponible en la
// misma API de Microsoft.AspNetCore.RateLimiting.
// =============================================================
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit       = 10,
                Window            = TimeSpan.FromMinutes(1),
                QueueLimit        = 0,
                AutoReplenishment = true,
            }));
});

// =============================================================
// CORS — en desarrollo permite localhost:5173; en producción usa
// la lista del appsettings.Production.json (o variable de entorno
// Cors__OrigenesPermitidos__0=...).
// =============================================================
var origenes = builder.Configuration
    .GetSection("Cors:OrigenesPermitidos")
    .Get<string[]>()
    ?? new[] { "http://localhost:5173" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("ClaumanCors", policy =>
    {
        // En Development permitimos cualquier origen — esto habilita demos
        // por VS Code Port Forwarding / ngrok / cloudflare tunnel sin tener
        // que hardcodear la URL del túnel (cambia cada vez que se reinicia).
        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(_ => true)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(origenes)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

// =============================================================
// Render asigna el puerto vía la variable de entorno PORT.
// Si existe, escuchamos ahí; si no, dejamos el default (5296 en dev).
// =============================================================
var puertoRender = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(puertoRender))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{puertoRender}");
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("ClaumanCors");
app.UseRateLimiter();   // antes del middleware de auth para que el 429 dispare ya
app.UseMiddleware<ClaumanAPI.Middleware.AuthMiddleware>();
app.UseAuthorization();
app.MapControllers();

// Endpoint para que Render verifique que el servicio está vivo.
// Render le pega cada 30s — si responde 200 mantiene la app despierta.
app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));

app.Run();
