using HtmlAgilityPack;
using Scrapper.Models.DTOs;

namespace Scrapper.Services.Interfaces;

/// <summary>
/// The reusable "given one listing page's HTML, produce its records" step — the exact same
/// record-detection and field-extraction pipeline <c>ScraperEngine</c> uses for the single
/// synchronous scrape, factored out so a paged/checkpointed crawl can call it too without
/// duplicating (or diverging from) any selector, parsing, or extraction logic.
/// </summary>
public interface IListingPageExtractor
{
    Task<List<ScrapedRecordDto>> ExtractAsync(
        HtmlNode documentNode,
        Uri pageUri,
        string? recordSelector,
        Scrapper.Models.Enums.SelectorType recordSelectorType,
        List<FieldDefinitionDto> fields,
        CancellationToken cancellationToken);
}
