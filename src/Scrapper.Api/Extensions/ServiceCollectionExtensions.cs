using System.Net;
using FluentValidation;
using FluentValidation.AspNetCore;
using Scrapper.Services.Implementations;
using Scrapper.Services.Implementations.Strategies;
using Scrapper.Services.Interfaces;

namespace Scrapper.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScrapperServices(this IServiceCollection services)
    {
        // Some sites (behind Cloudflare or similar) gzip/br-compress their response regardless
        // of whether the request even sends an Accept-Encoding header — without automatic
        // decompression here, HttpClient hands back the raw compressed bytes as if they were
        // text, which HtmlAgilityPack then parses as garbage (no real tags/text at all), so
        // every field silently comes back empty even though the page genuinely lists records.
        services.AddHttpClient(nameof(HtmlFetcher))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
            });

        services.AddScoped<IUrlValidationService, UrlValidationService>();
        services.AddScoped<IHtmlFetcher, HtmlFetcher>();

        // Shared across the app's lifetime — launching a headless browser per request would be
        // far too slow/heavy. Only used by the opt-in JavaScript-rendering fetch path.
        services.AddSingleton<IBrowserProvider, PlaywrightBrowserProvider>();
        services.AddScoped<IFieldExtractor, FieldExtractor>();
        services.AddScoped<IScraperEngine, ScraperEngine>();
        services.AddScoped<ICsvExportService, CsvExportService>();

        // Automatic (no-selector) extraction pipeline.
        services.AddScoped<IRecordDetector, RecordDetector>();
        services.AddScoped<IStructuredDataParser, StructuredDataParser>();
        services.AddScoped<IFieldInferenceEngine, FieldInferenceEngine>();
        services.AddScoped<IFieldExtractionOrchestrator, FieldExtractionOrchestrator>();
        services.AddScoped<IProfileLinkDetector, ProfileLinkDetector>();
        services.AddScoped<IProfileFieldMerger, ProfileFieldMerger>();

        // Paged, checkpointed crawl jobs (see CrawlJobsController) — separate lifecycle from
        // the single synchronous scrape above, but reuses the same extraction pipeline via
        // IListingPageExtractor.
        services.AddSingleton<ICrawlStateStore>(_ => new JsonFileCrawlStateStore());
        services.AddSingleton<ICrawlHttpSessionFactory, CrawlHttpSessionFactory>();
        services.AddScoped<IListingPageExtractor, ListingPageExtractor>();
        services.AddScoped<IPagedCrawlService, PagedCrawlService>();
        services.AddSingleton<ICrawlJobRunner, CrawlJobRunner>();

        // AI-assisted extraction is the last-resort strategy and is disabled by default.
        // Swap NullAiExtractionProvider for a real IAiExtractionProvider implementation
        // to enable it — nothing else in the pipeline needs to change.
        services.AddScoped<IAiExtractionProvider, NullAiExtractionProvider>();

        // Extraction strategies, registered in priority order (see FieldExtractionOrchestrator).
        services.AddScoped<IFieldExtractionStrategy, SelectorExtractionStrategy>();
        services.AddScoped<IFieldExtractionStrategy, StructuredDataExtractionStrategy>();
        services.AddScoped<IFieldExtractionStrategy, NextDataExtractionStrategy>();
        services.AddScoped<IFieldExtractionStrategy, SemanticHtmlExtractionStrategy>();
        services.AddScoped<IFieldExtractionStrategy, PatternExtractionStrategy>();
        services.AddScoped<IFieldExtractionStrategy, AiExtractionStrategy>();

        return services;
    }

    public static IServiceCollection AddScrapperInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatorsFromAssemblyContaining<Program>();
        services.AddFluentValidationAutoValidation();

        services.AddControllers()
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options =>
        {
            options.AddPolicy("LocalDevelopment", policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
                }
                else
                {
                    policy.SetIsOriginAllowed(origin => new Uri(origin).IsLoopback)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                }
            });
        });

        services.AddOpenApi();

        return services;
    }
}
