namespace Scrapper.Services.Interfaces;

/// <summary>
/// Pluggable AI-assisted extraction backend. Deliberately provider-agnostic — nothing in the
/// engine references OpenAI/Anthropic/etc. by name. To enable AI-assisted extraction, register
/// a real implementation of this interface in DI (replacing <c>NullAiExtractionProvider</c>);
/// nothing else in the extraction pipeline needs to change.
/// </summary>
public interface IAiExtractionProvider
{
    /// <summary>False when no provider is configured — the AI strategy is skipped entirely.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Given a single record's text content and the field names requested, returns a
    /// best-effort value per field name (or null for fields it can't find). Implementations
    /// must not fabricate values with no basis in the supplied content.
    /// </summary>
    Task<IReadOnlyDictionary<string, string?>?> ExtractFieldsAsync(
        string recordText,
        IReadOnlyList<string> fieldNames,
        CancellationToken cancellationToken);
}
