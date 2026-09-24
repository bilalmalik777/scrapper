using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Models;

namespace Scrapper.Services.Interfaces;

/// <summary>
/// One extraction strategy in the automatic (no-selector) pipeline. Strategies are tried in
/// priority order by <see cref="IFieldExtractionOrchestrator"/> until one produces a value
/// that passes validation for the field's kind.
/// </summary>
public interface IFieldExtractionStrategy
{
    ExtractionSource Source { get; }

    Task<ExtractionCandidate?> TryExtractAsync(
        ExtractionContext context,
        FieldDefinitionDto field,
        FieldKind resolvedKind,
        CancellationToken cancellationToken);
}
