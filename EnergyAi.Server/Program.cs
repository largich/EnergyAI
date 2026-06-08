using Anthropic.SDK;
using EnergyAi.Server.Data;
using EnergyAi.Server.Endpoints;
using EnergyAi.Server.Plugins;
using EnergyAi.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;

// Bootstrap logger captures startup errors before DI/config is ready.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        var dbSection  = ctx.Configuration.GetSection("SerilogDb");
        var connString = ctx.Configuration.GetConnectionString("EnergyDb")!;
        var tableName  = dbSection["TableName"]  ?? "AppLog";
        var schemaName = dbSection["SchemaName"] ?? "logs";
        var minLevel   = Enum.TryParse<LogEventLevel>(dbSection["MinimumLevel"], out var lvl)
                            ? lvl : LogEventLevel.Warning;

        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .WriteTo.MSSqlServer(
               connectionString: connString,
               sinkOptions: new MSSqlServerSinkOptions
               {
                   TableName        = tableName,
                   SchemaName       = schemaName,
                   AutoCreateSqlTable = true,
               },
               restrictedToMinimumLevel: minLevel);
    });

    builder.AddServiceDefaults();
    builder.Services.AddProblemDetails();
    builder.Services.AddOpenApi();

    // ---- Energy DB (read-only) ----
    var energyConn = builder.Configuration.GetConnectionString("EnergyDb")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:EnergyDb is not configured.");

    builder.Services.AddDbContext<EnergyDbContext>(opt =>
        opt.UseSqlServer(energyConn, sql => sql.CommandTimeout(60)));

    // ---- Chat DB (chat schema — new tables) ----
    builder.Services.AddDbContext<ChatDbContext>(opt =>
        opt.UseSqlServer(energyConn, sql => sql.CommandTimeout(30)));

    // ---- Energy services ----
    builder.Services.AddScoped<IConsumptionService, ConsumptionService>();
    builder.Services.AddScoped<IAnomalyService,     AnomalyService>();
    builder.Services.AddScoped<IReportService,      ReportService>();
    builder.Services.AddScoped<IHeatmapService,     HeatmapService>();

    // ---- Chat services ----
    builder.Services.AddScoped<EnergyPlugin>();
    builder.Services.AddScoped<IChatHistoryService, ChatHistoryService>();

    // ---- Anthropic IChatClient (scoped so EnergyPlugin injection works cleanly) ----
    var anthropicApiKey = builder.Configuration["Anthropic:ApiKey"]
        ?? throw new InvalidOperationException("Anthropic:ApiKey is not configured.");
    var anthropicModel  = builder.Configuration["Anthropic:ModelId"] ?? "claude-sonnet-4-6";

    builder.Services.AddScoped<IChatClient>(sp =>
    {
        var client = new AnthropicClient(anthropicApiKey);
        return client.Messages
            .AsBuilder()
            .UseFunctionInvocation()
            .UseLogging(sp.GetRequiredService<ILoggerFactory>())
            .Build();
    });

    builder.Services.AddScoped<IAgentService>(sp =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        var plugin     = sp.GetRequiredService<EnergyPlugin>();
        var logger     = sp.GetRequiredService<ILogger<AgentService>>();
        return new AgentService(chatClient, plugin, logger, anthropicModel);
    });

    // ---- App ----
    var app = builder.Build();

    // Auto-apply chat schema migrations on startup
    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<ChatDbContext>()
            .Database.MigrateAsync();
    }

    app.UseExceptionHandler();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
        app.MapOpenApi();

    var api = app.MapGroup("/api");
    api.MapEnergyEndpoints();
    api.MapChatEndpoints();

    string[] summaries = ["Freezing","Bracing","Chilly","Cool","Mild","Warm","Balmy","Hot","Sweltering","Scorching"];
    api.MapGet("weatherforecast", () =>
        Enumerable.Range(1, 5).Select(i =>
            new WeatherForecast(DateOnly.FromDateTime(DateTime.Now.AddDays(i)),
                Random.Shared.Next(-20, 55),
                summaries[Random.Shared.Next(summaries.Length)])).ToArray())
        .WithName("GetWeatherForecast");

    app.MapDefaultEndpoints();
    app.UseFileServer();
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
