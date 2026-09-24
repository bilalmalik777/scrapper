using HtmlAgilityPack;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;
using Scrapper.Services.Models;

namespace Scrapper.Services.Implementations;

/// <summary>See <see cref="IProfileFieldMerger"/>.</summary>
public class ProfileFieldMerger(
    IStructuredDataParser structuredDataParser,
    IFieldExtractionOrchestrator extractionOrchestrator) : IProfileFieldMerger
{
    public async Task MergeProfileFieldsAsync(
        ScrapedRecordDto record, HtmlNode profileDocumentNode, List<FieldDefinitionDto> fields, CancellationToken cancellationToken)
    {
        var profileStructuredData = structuredDataParser.Parse(profileDocumentNode);
        var profileContext = new ExtractionContext
        {
            RecordNode = profileDocumentNode,
            DocumentNode = profileDocumentNode,
            RecordIndex = 0,
            TotalRecords = 1,
            StructuredDataObjects = profileStructuredData,
        };

        foreach (var field in fields)
        {
            var profileResult = await extractionOrchestrator.ExtractFieldAsync(profileContext, field, cancellationToken);
            if (profileResult.IsMissing)
            {
                continue; // Keep whatever the listing page already found (or leave it missing).
            }

            // Some sites' "profile" links lead to a page that's mostly generic site chrome
            // (a WordPress author-archive template with no real per-person content, a broken
            // link that redirects to the homepage, ...) rather than an actual detail page. On
            // a page like that, extraction still "finds" *something* — a nav link, a site
            // tagline — just not the real field, and it comes back Low confidence precisely
            // because nothing genuinely matched. A weak guess like that must never clobber a
            // value the listing page had already found correctly; a High/Medium-confidence
            // profile match still overrides as normal, since that's what makes profile
            // crawling useful when the profile page IS the more detailed source.
            var listingAlreadyHadValue = !record.MissingFields.Contains(field.Name)
                && !string.IsNullOrWhiteSpace(record.Fields.GetValueOrDefault(field.Name));
            if (listingAlreadyHadValue && profileResult.ConfidenceLevel == ConfidenceLevel.Low)
            {
                continue;
            }

            record.Fields[field.Name] = profileResult.Value;
            record.FieldResults[field.Name] = profileResult;
            record.MissingFields.Remove(field.Name);
        }

        record.ProfileVisited = true;
    }
}
