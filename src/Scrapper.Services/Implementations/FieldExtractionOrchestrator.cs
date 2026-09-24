using System.Text.RegularExpressions;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;
using Scrapper.Services.Models;
using Scrapper.Utils.Constants;
using Scrapper.Utils.Helpers;

namespace Scrapper.Services.Implementations;

/// <summary>
/// Resolves each field's kind and runs the registered <see cref="IFieldExtractionStrategy"/>
/// implementations in priority order (DI registration order — see
/// ServiceCollectionExtensions), stopping at the first candidate that validates for that
/// kind. If nothing validates but *something* was found, it's surfaced at reduced/low
/// confidence rather than discarded — the UI decides whether to show or flag it, but the
/// engine never silently returns a value it has no basis for.
/// </summary>
public partial class FieldExtractionOrchestrator(
    IEnumerable<IFieldExtractionStrategy> strategies,
    IFieldInferenceEngine inferenceEngine) : IFieldExtractionOrchestrator
{
    public async Task<FieldResultDto> ExtractFieldAsync(
        ExtractionContext context, FieldDefinitionDto field, CancellationToken cancellationToken)
    {
        var resolvedKind = field.Type ?? inferenceEngine.Infer(field.Name);
        ExtractionCandidate? bestInvalidCandidate = null;

        foreach (var strategy in strategies)
        {
            var candidate = await strategy.TryExtractAsync(context, field, resolvedKind, cancellationToken);
            if (candidate is null)
            {
                continue;
            }

            // A Number field (years of experience, a review count, ...) must never surface
            // more than the number itself — a labelled section a strategy matched on can
            // easily carry unrelated prose alongside the real figure (e.g. a "24 years of
            // experience" line followed by a job-history list in the same container). Pull
            // out just the number/years phrase if one exists in the text; if the whole value
            // isn't a clean number and no such phrase can be found in it, discard the
            // candidate outright rather than let it reach the invalid-candidate fallback
            // below and get surfaced anyway at reduced confidence.
            if (resolvedKind == FieldKind.Number)
            {
                candidate = NormalizeNumberCandidate(candidate);
                if (candidate is null)
                {
                    continue;
                }
            }

            // A Fees/Currency field is prone to the same failure as Number: a labelled section
            // ("Fees" heading, "consultation-fee" class, ...) can just as easily wrap an
            // unrelated article blurb or a whole sentence as it can the actual price — e.g. a
            // "Related articles" link block sharing a container class with the real fee row.
            // Pull out just the currency amount(s) if any are present anywhere in the text
            // (joining several, since one specialist can list several fees); if none can be
            // found at all, discard the candidate rather than let it reach the
            // invalid-candidate fallback below and get surfaced anyway at reduced confidence.
            if (resolvedKind == FieldKind.Currency)
            {
                candidate = NormalizeCurrencyCandidate(candidate);
                if (candidate is null)
                {
                    continue;
                }
            }

            if (FieldValueValidator.IsPlausible(resolvedKind, candidate.Value))
            {
                return BuildResult(candidate.Value, resolvedKind, candidate.Source, candidate.Confidence);
            }

            if (bestInvalidCandidate is null || candidate.Confidence > bestInvalidCandidate.Confidence)
            {
                bestInvalidCandidate = candidate;
            }
        }

        if (bestInvalidCandidate is not null)
        {
            var downgraded = Math.Max(1, bestInvalidCandidate.Confidence / 2);
            return BuildResult(bestInvalidCandidate.Value, resolvedKind, bestInvalidCandidate.Source, downgraded);
        }

        return new FieldResultDto
        {
            Value = null,
            ResolvedType = resolvedKind,
            Source = ExtractionSource.None,
            Confidence = 0,
            ConfidenceLevel = ConfidenceLevel.None,
            IsMissing = true,
        };
    }

    private static FieldResultDto BuildResult(string value, FieldKind kind, ExtractionSource source, int confidence) =>
        new()
        {
            Value = value,
            ResolvedType = kind,
            Source = source,
            Confidence = confidence,
            ConfidenceLevel = LevelFor(confidence),
            IsMissing = false,
        };

    private static ConfidenceLevel LevelFor(int confidence) => confidence switch
    {
        >= ScrapingLimits.ConfidenceHighThreshold => ConfidenceLevel.High,
        >= ScrapingLimits.ConfidenceMediumThreshold => ConfidenceLevel.Medium,
        > 0 => ConfidenceLevel.Low,
        _ => ConfidenceLevel.None,
    };

    private static ExtractionCandidate? NormalizeNumberCandidate(ExtractionCandidate candidate)
    {
        var trimmed = candidate.Value.Trim();

        // Already a clean bare number (e.g. a review count) — nothing to clean up.
        if (BareNumberRegex().IsMatch(trimmed))
        {
            return candidate;
        }

        if (FieldSynonymCatalog.Patterns.TryGetValue(FieldKind.Number, out var patterns) && patterns.Length > 0)
        {
            var match = patterns[0].Match(trimmed);
            if (match.Success)
            {
                var cleaned = match.Value.Trim();
                return cleaned == trimmed ? candidate : candidate with { Value = cleaned };
            }
        }

        return null;
    }

    [GeneratedRegex(@"^[\d,]+$")]
    private static partial Regex BareNumberRegex();

    private static ExtractionCandidate? NormalizeCurrencyCandidate(ExtractionCandidate candidate)
    {
        var trimmed = candidate.Value.Trim();

        if (!FieldSynonymCatalog.Patterns.TryGetValue(FieldKind.Currency, out var patterns) || patterns.Length == 0)
        {
            return candidate;
        }

        // Already a clean value (e.g. "From £150; £200") — nothing to clean up.
        if (patterns.Any(p => p.IsMatch(trimmed)) && trimmed.Length <= 200)
        {
            return candidate;
        }

        var matches = patterns
            .SelectMany(p => p.Matches(trimmed).Select(m => m.Value.Trim()))
            .Distinct()
            .ToList();

        if (matches.Count > 0)
        {
            var cleaned = string.Join("; ", matches);
            return cleaned == trimmed ? candidate : candidate with { Value = cleaned };
        }

        // No currency amount anywhere in the text. A short value (e.g. "Manchester" from a
        // mismatched label) is still worth surfacing at reduced confidence — it might be a
        // genuine field the site just didn't format as expected. A long value is a different
        // situation: it's not a wrong guess at a price, it's prose from an unrelated block
        // (an article teaser, a paragraph) that happened to share a container/class with the
        // real fee row — showing that to the user is actively misleading, so discard it.
        return trimmed.Length > 60 ? null : candidate;
    }
}
