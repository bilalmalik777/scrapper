using HtmlAgilityPack;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;
using Scrapper.Services.Models;

namespace Scrapper.Services.Implementations;

/// <summary>
/// See <see cref="IListingPageExtractor"/>. Calls the exact same
/// <see cref="IRecordDetector"/>/<see cref="IStructuredDataParser"/>/
/// <see cref="IFieldExtractionOrchestrator"/>/<see cref="IProfileLinkDetector"/> services
/// <c>ScraperEngine</c> uses — none of their selector/parsing/extraction logic is touched or
/// duplicated here, only the glue that sequences the calls for one page.
/// </summary>
public class ListingPageExtractor(
    IRecordDetector recordDetector,
    IStructuredDataParser structuredDataParser,
    IFieldExtractionOrchestrator extractionOrchestrator,
    IProfileLinkDetector profileLinkDetector) : IListingPageExtractor
{
    public async Task<List<ScrapedRecordDto>> ExtractAsync(
        HtmlNode documentNode,
        Uri pageUri,
        string? recordSelector,
        SelectorType recordSelectorType,
        List<FieldDefinitionDto> fields,
        CancellationToken cancellationToken)
    {
        List<HtmlNode> recordNodes;

        if (!string.IsNullOrWhiteSpace(recordSelector))
        {
            recordNodes = HtmlExtractionUtils.SelectNodes(documentNode, recordSelector, recordSelectorType);
        }
        else
        {
            recordNodes = recordDetector.DetectRecords(documentNode, fields) ?? [documentNode];
        }

        var structuredDataObjects = structuredDataParser.Parse(documentNode);
        var records = new List<ScrapedRecordDto>();
        var index = 0;

        foreach (var recordNode in recordNodes)
        {
            var context = new ExtractionContext
            {
                RecordNode = recordNode,
                DocumentNode = documentNode,
                RecordIndex = index,
                TotalRecords = recordNodes.Count,
                StructuredDataObjects = structuredDataObjects,
            };

            var record = new ScrapedRecordDto();

            foreach (var field in fields)
            {
                var fieldResult = await extractionOrchestrator.ExtractFieldAsync(context, field, cancellationToken);
                record.Fields[field.Name] = fieldResult.Value;
                record.FieldResults[field.Name] = fieldResult;

                if (fieldResult.IsMissing)
                {
                    record.MissingFields.Add(field.Name);
                }
            }

            if (!ReferenceEquals(recordNode, documentNode))
            {
                record.ProfileUrl = profileLinkDetector.DetectProfileUrl(recordNode, pageUri);
            }

            records.Add(record);
            index++;
        }

        return records;
    }
}
