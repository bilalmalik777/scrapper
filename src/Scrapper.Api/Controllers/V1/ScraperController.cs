using Microsoft.AspNetCore.Mvc;
using Scrapper.Models.DTOs;
using Scrapper.Services.Interfaces;

namespace Scrapper.Api.Controllers.V1;

[Route("api/v1/scraper")]
public class ScraperController(
    IUrlValidationService urlValidationService,
    IFieldExtractor fieldExtractor,
    IHtmlFetcher htmlFetcher,
    IScraperEngine scraperEngine,
    ICsvExportService csvExportService,
    ILogger<ScraperController> logger) : BaseApiController
{
    [HttpPost("validate-url")]
    public async Task<ActionResult<Models.Common.ApiResponse<ValidateUrlResponseDto>>> ValidateUrl(
        [FromBody] ValidateUrlRequestDto request, CancellationToken cancellationToken)
    {
        var result = await urlValidationService.ValidateAsync(request.Url, cancellationToken);
        return Ok(result);
    }

    [HttpPost("test-selector")]
    public async Task<ActionResult<Models.Common.ApiResponse<TestSelectorResponseDto>>> TestSelector(
        [FromBody] TestSelectorRequestDto request, CancellationToken cancellationToken)
    {
        var validation = await urlValidationService.ValidateAsync(request.Url, cancellationToken);
        if (!validation.IsValid)
        {
            return Ok(new TestSelectorResponseDto { MatchCount = 0, Warning = validation.Reason });
        }

        string html;
        try
        {
            html = await htmlFetcher.FetchAsync(validation.NormalizedUrl ?? request.Url, 20, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch URL while testing selector");
            return Ok(new TestSelectorResponseDto { MatchCount = 0, Warning = "Unable to access the URL." });
        }

        var (matchCount, previews) = fieldExtractor.TestSelector(
            html, request.RecordSelector, request.RecordSelectorType,
            request.Selector, request.SelectorType, request.ExtractionType, request.Attribute);

        var response = new TestSelectorResponseDto
        {
            MatchCount = matchCount,
            SamplePreviews = previews,
            Warning = matchCount == 0 ? "The selector did not match any elements." : null
        };

        return Ok(response);
    }

    [HttpPost("scrape")]
    public async Task<ActionResult<Models.Common.ApiResponse<ScrapeResultDto>>> Scrape(
        [FromBody] ScraperConfigDto request, CancellationToken cancellationToken)
    {
        var result = await scraperEngine.ScrapeAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("export")]
    public ActionResult Export([FromBody] ExportRequestDto request)
    {
        if (request.GroupByPage)
        {
            var zipBytes = csvExportService.ExportToZipByPage(request);
            return File(zipBytes, "application/zip", $"scrape-export-by-page-{DateTime.UtcNow:yyyyMMddHHmmss}.zip");
        }

        var csvBytes = csvExportService.ExportToCsv(request);
        return File(csvBytes, "text/csv", $"scrape-export-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }
}
