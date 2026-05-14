using EnergyAi.Server.Data;
using EnergyAi.Server.Endpoints;
using EnergyAi.Server.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// EF Core — connection string lives in appsettings*.json as "EnergyDb".
var conn = builder.Configuration.GetConnectionString("EnergyDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:EnergyDb is not configured. " +
        "Set it in appsettings.Development.json or as env var ConnectionStrings__EnergyDb.");

builder.Services.AddDbContext<EnergyDbContext>(opt =>
    opt.UseSqlServer(conn, sql => sql.CommandTimeout(60)));

builder.Services.AddScoped<IConsumptionService, ConsumptionService>();
builder.Services.AddScoped<IAnomalyService, AnomalyService>();
builder.Services.AddScoped<IReportService, ReportService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var api = app.MapGroup("/api");

// Energy management endpoints (consumption, comparison, anomalies, reports, metadata).
api.MapEnergyEndpoints();

// Kept from template; harmless, useful for smoke-testing connectivity from the SPA.
string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];
api.MapGet("weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapDefaultEndpoints();

app.UseFileServer();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
