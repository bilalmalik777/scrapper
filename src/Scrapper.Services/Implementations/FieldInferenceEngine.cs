using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;
using Scrapper.Utils.Constants;

namespace Scrapper.Services.Implementations;

/// <summary>
/// Maps a free-typed field name to a <see cref="FieldKind"/> using the configurable
/// synonym tables in <see cref="FieldSynonymCatalog"/> — never hard-codes field names.
/// </summary>
public class FieldInferenceEngine : IFieldInferenceEngine
{
    public FieldKind Infer(string fieldName)
    {
        var normalized = fieldName.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return FieldKind.Text;
        }

        FieldKind? bestKind = null;
        var bestTokenLength = 0;

        foreach (var (kind, synonyms) in FieldSynonymCatalog.Synonyms)
        {
            foreach (var synonym in synonyms)
            {
                var token = synonym.ToLowerInvariant();

                // Exact match wins outright.
                if (normalized == token)
                {
                    return kind;
                }

                // Otherwise prefer the longest synonym token contained in (or containing) the
                // field name, so e.g. "Consultation Fee" matches "consultation fee" over a
                // shorter, coincidental substring match.
                var isMatch = normalized.Contains(token) || token.Contains(normalized);
                if (isMatch && token.Length > bestTokenLength)
                {
                    bestKind = kind;
                    bestTokenLength = token.Length;
                }
            }
        }

        return bestKind ?? FieldKind.Text;
    }
}
