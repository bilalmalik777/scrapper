using Scrapper.Services.Interfaces;

namespace Scrapper.Services.Implementations;

/// <summary>
/// Default AI provider — always "not configured". Keeps the AI extraction strategy safely
/// inert until an operator registers a real provider (see <see cref="IAiExtractionProvider"/>).
/// </summary>
public class NullAiExtractionProvider : IAiExtractionProvider
{
    public bool IsConfigured => false;

    public Task<IReadOnlyDictionary<string, string?>?> ExtractFieldsAsync(
        string recordText, IReadOnlyList<string> fieldNames, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, string?>?>(null);
}
