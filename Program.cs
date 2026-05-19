var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
        policy.WithOrigins(origenes)
              .AllowAnyHeader()
              .AllowAnyMethod();
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
app.UseMiddleware<ClaumanAPI.Middleware.AuthMiddleware>();
app.UseAuthorization();
app.MapControllers();

// Endpoint para que Render verifique que el servicio está vivo.
// Render le pega cada 30s — si responde 200 mantiene la app despierta.
app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));

app.Run();
