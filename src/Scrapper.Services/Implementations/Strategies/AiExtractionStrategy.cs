using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;
using Scrapper.Services.Models;

namespace Scrapper.Services.Implementations.Strategies;

/// <summary>
/// Priority 5 (last resort): hands the record's text to a pluggable AI provider and asks
/// for the named field back. Skipped entirely when no provider is configured
/// (<see cref="NullAiExtractionProvider"/> by default) — this is a controlled,
/// single-field-at-a-time extraction call, not open-ended page interpretation.
/// </summary>
public class AiExtractionStrategy(IAiExtractionProvider aiProvider) : IFieldExtractionStrategy
{
    public ExtractionSource Source => ExtractionSource.Ai;

    public async Task<ExtractionCandidate?> TryExtractAsync(
        ExtractionContext context, FieldDefinitionDto field, FieldKind resolvedKind, CancellationToken cancellationToken)
    {
        if (!aiProvider.IsConfigured)
        {
            return null;
        }

        var recordText = HtmlExtractionUtils.GetVisibleText(context.RecordNode);
        if (string.IsNullOrWhiteSpace(recordText))
        {
            return null;
        }

        var results = await aiProvider.ExtractFieldsAsync(recordText, [field.Name], cancellationToken);
        if (results is null || !results.TryGetValue(field.Name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // AI output is unverified against the page structure, so it starts at a moderate
        // confidence — validation and the orchestrator's downgrade-on-failure still apply.
        return new ExtractionCandidate(value, 60, Source);
    }
}
