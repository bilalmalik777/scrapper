using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;
using Scrapper.Services.Models;

namespace Scrapper.Services.Implementations.Strategies;

/// <summary>
/// Priority 1: an explicit CSS/XPath selector supplied by the user (Advanced Options).
/// Only "applies" when the field actually has a selector — otherwise it steps aside so the
/// automatic strategies run.
/// </summary>
public class SelectorExtractionStrategy : IFieldExtractionStrategy
{
    public ExtractionSource Source => ExtractionSource.Selector;

    public Task<ExtractionCandidate?> TryExtractAsync(
        ExtractionContext context, FieldDefinitionDto field, FieldKind resolvedKind, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(field.Selector))
        {
            return Task.FromResult<ExtractionCandidate?>(null);
        }

        var matches = HtmlExtractionUtils.SelectNodes(context.RecordNode, field.Selector, field.SelectorType);
        var target = matches.FirstOrDefault();
        if (target is null)
        {
            return Task.FromResult<ExtractionCandidate?>(null);
        }

        var value = HtmlExtractionUtils.ExtractValue(target, field.ExtractionType, field.Attribute);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult<ExtractionCandidate?>(null);
        }

        return Task.FromResult<ExtractionCandidate?>(new ExtractionCandidate(value, 99, Source));
    }
}
