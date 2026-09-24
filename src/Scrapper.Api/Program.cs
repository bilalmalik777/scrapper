using Scrapper.Api.Extensions;
using Scrapper.Api.Middleware;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/scrapper-.log", rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 5 * 1024 * 1024, rollOnFileSizeLimit: true));

    builder.Services.AddScrapperServices();
    builder.Services.AddScrapperInfrastructure(builder.Configuration);

    var app = builder.Build();

    // Graceful shutdown: signal every currently-running paged crawl job to stop so it saves
    // its checkpoint (page, records, status) before the process exits, rather than being torn
    // down mid-request with no chance to persist anything.
    app.Lifetime.ApplicationStopping.Register(() =>
        app.Services.GetRequiredService<Scrapper.Services.Interfaces.ICrawlJobRunner>().StopAll());

    app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseSerilogRequestLogging();

    app.UseCors("LocalDevelopment");

    app.UseHttpsRedirection();

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Scrapper.Api terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
