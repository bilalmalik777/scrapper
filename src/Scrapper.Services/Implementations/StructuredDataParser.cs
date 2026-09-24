using System.Text.Json;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Scrapper.Services.Interfaces;

namespace Scrapper.Services.Implementations;

/// <summary>
/// Extracts JSON-LD (&lt;script type="application/ld+json"&gt;) structured data from a page
/// and flattens it into one entity per record — unwrapping @graph arrays and ItemList/
/// itemListElement wrappers, which is how most directory/listing sites (schema.org
/// Person/Product/LocalBusiness lists) publish their data.
/// </summary>
public class StructuredDataParser(ILogger<StructuredDataParser> logger) : IStructuredDataParser
{
    public List<Dictionary<string, object?>> Parse(HtmlNode documentNode)
    {
        var entities = new List<Dictionary<string, object?>>();

        var scripts = documentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts is null)
        {
            return entities;
        }

        foreach (var script in scripts)
        {
            var json = HtmlExtractionUtils.CleanText(script.InnerText);
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                entities.AddRange(FlattenRoot(doc.RootElement));
            }
            catch (JsonException ex)
            {
                logger.LogDebug(ex, "Skipping malformed JSON-LD block");
            }
        }

        return entities;
    }

    private static IEnumerable<Dictionary<string, object?>> FlattenRoot(JsonElement root)
    {
        var roots = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToList()
            : [root];

        foreach (var item in roots)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var dict = (Dictionary<string, object?>)ConvertElement(item)!;

            if (dict.TryGetValue("@graph", out var graph) && graph is List<object?> graphList)
            {
                foreach (var entry in graphList.OfType<Dictionary<string, object?>>())
                {
                    yield return entry;
                }

                continue;
            }

            var type = dict.GetValueOrDefault("@type")?.ToString();
            if (string.Equals(type, "ItemList", StringComparison.OrdinalIgnoreCase)
                && dict.TryGetValue("itemListElement", out var elements) && elements is List<object?> elementList)
            {
                foreach (var entry in elementList.OfType<Dictionary<string, object?>>())
                {
                    yield return entry.TryGetValue("item", out var inner) && inner is Dictionary<string, object?> innerDict
                        ? innerDict
                        : entry;
                }

                continue;
            }

            yield return dict;
        }
    }

    private static object? ConvertElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    dict[property.Name] = ConvertElement(property.Value);
                }
                return dict;
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(ConvertElement).ToList();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.GetRawText();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }
}
