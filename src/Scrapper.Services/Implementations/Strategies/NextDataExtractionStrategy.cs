using System.Text.Json;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;
using Scrapper.Services.Models;

namespace Scrapper.Services.Implementations.Strategies;

/// <summary>
/// Priority 2b: many modern sites (anything built with Next.js and similar frameworks) embed
/// the page's full server-rendered data as JSON in a well-known script tag
/// (&lt;script id="__NEXT_DATA__" type="application/json"&gt;) — often the ONLY place a real
/// contact email/phone exists at all, when the visible page only shows a generic "Email"/
/// "Call" button gated behind a client-side reveal action that never touches the raw HTML a
/// non-JS fetch receives. Deliberately scoped to a whole-page single-record context (e.g. a
/// profile page): on a multi-record listing page, correlating one key inside this page-wide
/// payload back to "which card it belongs to" isn't reliable, so this stays out of the way
/// there and leaves listing-page extraction to the other strategies.
/// </summary>
public class NextDataExtractionStrategy : IFieldExtractionStrategy
{
    private const int MaxDepth = 40;

    private static readonly Dictionary<FieldKind, string[]> KeyNames = new()
    {
        [FieldKind.Email] = ["email", "emails"],
        [FieldKind.Phone] = ["phone", "phones", "telephone", "tel"],
    };

    public ExtractionSource Source => ExtractionSource.StructuredData;

    public Task<ExtractionCandidate?> TryExtractAsync(
        ExtractionContext context, FieldDefinitionDto field, FieldKind resolvedKind, CancellationToken cancellationToken)
    {
        if (context.TotalRecords != 1 || !KeyNames.TryGetValue(resolvedKind, out var keyNames))
        {
            return Task.FromResult<ExtractionCandidate?>(null);
        }

        var script = context.DocumentNode.SelectSingleNode("//script[@id='__NEXT_DATA__']");
        if (script is null)
        {
            return Task.FromResult<ExtractionCandidate?>(null);
        }

        var json = HtmlExtractionUtils.CleanText(script.InnerText);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Task.FromResult<ExtractionCandidate?>(null);
        }

        string? value;
        try
        {
            using var doc = JsonDocument.Parse(json);
            value = FindFirstMatch(doc.RootElement, keyNames, 0);
        }
        catch (JsonException)
        {
            return Task.FromResult<ExtractionCandidate?>(null);
        }

        return Task.FromResult(value is null ? null : new ExtractionCandidate(value, 85, Source));
    }

    private static string? FindFirstMatch(JsonElement element, string[] keyNames, int depth)
    {
        if (depth > MaxDepth)
        {
            return null;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (keyNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        var direct = ExtractValue(property.Value);
                        if (direct is not null)
                        {
                            return direct;
                        }
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    var nested = FindFirstMatch(property.Value, keyNames, depth + 1);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                return null;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindFirstMatch(item, keyNames, depth + 1);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static string? ExtractValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString(),
        JsonValueKind.Array => value.EnumerateArray()
            .Select(ExtractValue)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
        _ => null,
    };
}
