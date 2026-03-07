// ─────────────────────────────────────────────────────────────────
//  IndoorNav Emergency Server  –  ASP.NET Core Minimal API
//
//  Endpoints:
//    GET  /emergency/status                → { activeBuildingIds: [...] }
//    POST /emergency/activate/{buildingId}  → 200 OK
//    POST /emergency/deactivate/{buildingId}→ 200 OK
//    POST /emergency/deactivate-all         → 200 OK
//    GET  /health                           → "ok"
//
//  Run:  dotnet run --project Tools/EmergencyServer/EmergencyServer.csproj
//  URL:  http://localhost:5180  (set ServerBaseUrl in AppConfig.cs)
// ─────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p =>
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.UseCors();

// ── In-memory state ───────────────────────────────────────────────
var activeBuildings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var stateLock       = new object();

// ── Endpoints ─────────────────────────────────────────────────────

app.MapGet("/health", () => "ok");

app.MapGet("/emergency/status", () =>
{
    string[] ids;
    lock (stateLock) ids = [.. activeBuildings];
    return Results.Ok(new { activeBuildingIds = ids });
});

app.MapPost("/emergency/activate/{buildingId}", (string buildingId) =>
{
    lock (stateLock) activeBuildings.Add(buildingId);
    Console.WriteLine($"[ЧС] Активировано: {buildingId}");
    return Results.Ok();
});

app.MapPost("/emergency/deactivate/{buildingId}", (string buildingId) =>
{
    lock (stateLock) activeBuildings.Remove(buildingId);
    Console.WriteLine($"[ЧС] Снято: {buildingId}");
    return Results.Ok();
});

app.MapPost("/emergency/deactivate-all", () =>
{
    lock (stateLock) activeBuildings.Clear();
    Console.WriteLine("[ЧС] Снято со всех зданий");
    return Results.Ok();
});

app.Run("http://0.0.0.0:5180");
