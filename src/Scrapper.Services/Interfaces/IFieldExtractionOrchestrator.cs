using Scrapper.Models.DTOs;
using Scrapper.Services.Models;

namespace Scrapper.Services.Interfaces;

/// <summary>
/// Resolves a field's kind (explicit or inferred) and runs the extraction strategies in
/// priority order, validating and scoring whatever they find.
/// </summary>
public interface IFieldExtractionOrchestrator
{
    Task<FieldResultDto> ExtractFieldAsync(
        ExtractionContext context, FieldDefinitionDto field, CancellationToken cancellationToken);
}
