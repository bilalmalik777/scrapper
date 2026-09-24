using HtmlAgilityPack;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;
using Scrapper.Utils.Constants;

namespace Scrapper.Services.Implementations;

/// <summary>
/// Explicit-selector extraction — the "advanced/manual mode" path, and the engine behind
/// the Test Selector feature. Only used for fields where the user supplied a Selector;
/// automatic (no-selector) extraction is handled by <see cref="IFieldExtractionOrchestrator"/>.
/// </summary>
public class FieldExtractor : IFieldExtractor
{
    public List<ScrapedRecordDto> ExtractRecords(string html, ScraperConfigDto config, out List<string> warnings)
    {
        warnings = [];
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var recordNodes = GetRecordNodes(doc, config.RecordSelector, config.RecordSelectorType);

        if (recordNodes.Count == 0)
        {
            warnings.Add(string.IsNullOrWhiteSpace(config.RecordSelector)
                ? "No content was found on the page."
                : "The record selector did not match any elements.");
            return [];
        }

        var records = new List<ScrapedRecordDto>();
        var maxRecords = Math.Min(config.MaxRecords, ScrapingLimits.MaxRecordsHardCap);

        foreach (var node in recordNodes.Take(maxRecords))
        {
            var record = new ScrapedRecordDto();

            foreach (var field in config.Fields)
            {
                var value = ExtractFieldValue(node, field);
                record.Fields[field.Name] = value;

                if (string.IsNullOrWhiteSpace(value))
                {
                    record.MissingFields.Add(field.Name);
                    if (field.Required)
                    {
                        warnings.Add($"Required field '{field.Name}' could not be extracted for one or more records.");
                    }
                }
            }

            records.Add(record);
        }

        if (recordNodes.Count > maxRecords)
        {
            warnings.Add($"Only the first {maxRecords} records were processed out of {recordNodes.Count} found.");
        }

        return records;
    }

    public (int MatchCount, List<string> Previews) TestSelector(
        string html,
        string? recordSelector,
        SelectorType recordSelectorType,
        string selector,
        SelectorType selectorType,
        ExtractionType extractionType,
        string? attribute)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var scopeNodes = GetRecordNodes(doc, recordSelector, recordSelectorType);
        if (scopeNodes.Count == 0 && !string.IsNullOrWhiteSpace(recordSelector))
        {
            return (0, []);
        }

        if (scopeNodes.Count == 0)
        {
            scopeNodes = [doc.DocumentNode];
        }

        var previews = new List<string>();
        var totalMatches = 0;

        foreach (var scope in scopeNodes)
        {
            var matches = HtmlExtractionUtils.SelectNodes(scope, selector, selectorType);
            totalMatches += matches.Count;

            foreach (var match in matches)
            {
                if (previews.Count < ScrapingLimits.TestSelectorPreviewCount)
                {
                    previews.Add(HtmlExtractionUtils.ExtractValue(match, extractionType, attribute) ?? string.Empty);
                }
            }

            if (totalMatches > ScrapingLimits.TestSelectorMaxMatches)
            {
                break;
            }
        }

        return (totalMatches, previews);
    }

    private static List<HtmlNode> GetRecordNodes(HtmlDocument doc, string? recordSelector, SelectorType selectorType)
    {
        if (string.IsNullOrWhiteSpace(recordSelector))
        {
            return [doc.DocumentNode];
        }

        return HtmlExtractionUtils.SelectNodes(doc.DocumentNode, recordSelector, selectorType);
    }

    private static string? ExtractFieldValue(HtmlNode recordNode, FieldDefinitionDto field)
    {
        var matches = HtmlExtractionUtils.SelectNodes(recordNode, field.Selector, field.SelectorType);
        var target = matches.FirstOrDefault();
        return target is null ? null : HtmlExtractionUtils.ExtractValue(target, field.ExtractionType, field.Attribute);
    }
}
