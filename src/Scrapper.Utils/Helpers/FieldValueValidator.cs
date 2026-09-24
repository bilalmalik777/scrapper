using Scrapper.Models.Enums;
using Scrapper.Utils.Constants;

namespace Scrapper.Utils.Helpers;

/// <summary>
/// Checks whether a candidate extracted value plausibly matches the field kind it was
/// extracted for, e.g. rejects "Manchester" as a Currency value. Used to downgrade
/// confidence or discard a candidate outright rather than silently accepting noise.
/// </summary>
public static class FieldValueValidator
{
    public static bool IsPlausible(FieldKind kind, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();

        return kind switch
        {
            FieldKind.Currency => FieldSynonymCatalog.Patterns[FieldKind.Currency].Any(r => r.IsMatch(value)),
            FieldKind.Email => FieldSynonymCatalog.Patterns[FieldKind.Email].Any(r => r.IsMatch(value)),
            FieldKind.Phone => FieldSynonymCatalog.Patterns[FieldKind.Phone].Any(r => r.IsMatch(value))
                               && value.Any(char.IsDigit),
            FieldKind.Url => FieldSynonymCatalog.Patterns[FieldKind.Url].Any(r => r.IsMatch(value))
                             || value.Contains('/'),
            FieldKind.Rating => FieldSynonymCatalog.Patterns[FieldKind.Rating].Any(r => r.IsMatch(value)),
            // A Number field (years of experience, a review/endorsement count, ...) is always
            // a small human-scale figure — never a phone number or ID that merely happens to
            // contain digits. Capping at 4 digits (0-9999) comfortably covers any believable
            // count while firmly excluding phone numbers, which run 7+ raw digits.
            FieldKind.Number => value.Count(char.IsDigit) is >= 1 and <= 4,
            // Allows a longer value when multiple locations are joined into one field
            // (see PatternExtractionStrategy — a specialist with several practice addresses).
            // Also rejects a phone-number-shaped value — some sites reuse one generic class
            // for a whole block of contact-detail rows (address/phone/website alike), so a
            // labelled-element match can land on the wrong sibling row.
            FieldKind.Location => value.Length is >= 2 and <= 1000
                                   && !FieldSynonymCatalog.Patterns[FieldKind.Currency].Any(r => r.IsMatch(value))
                                   && !FieldSynonymCatalog.Patterns[FieldKind.Email].Any(r => r.IsMatch(value))
                                   && !FieldSynonymCatalog.Patterns[FieldKind.Phone].Any(r => r.IsMatch(value)),
            FieldKind.Name => value.Length is >= 2 and <= 150,
            FieldKind.Biography => value.Length is >= 20 and <= 8000,
            // List/section-style free text (Qualifications, Insurers, Languages spoken, ...).
            _ => value.Length <= 3000,
        };
    }
}
