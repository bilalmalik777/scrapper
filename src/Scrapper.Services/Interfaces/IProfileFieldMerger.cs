using HtmlAgilityPack;
using Scrapper.Models.DTOs;

namespace Scrapper.Services.Interfaces;

/// <summary>
/// Extracts fields from an already-fetched profile/detail page and merges them into a listing
/// record — the exact same "visit each record's own page for fields the listing doesn't show"
/// step <c>ScraperEngine</c> uses for the single synchronous scrape, factored out so a paged
/// crawl job can do the same profile crawling without duplicating the merge-precedence rules
/// (profile data overrides listing data, except a weak/Low-confidence profile guess never
/// clobbers a value the listing page already found).
/// </summary>
public interface IProfileFieldMerger
{
    Task MergeProfileFieldsAsync(
        ScrapedRecordDto record, HtmlNode profileDocumentNode, List<FieldDefinitionDto> fields, CancellationToken cancellationToken);
}
