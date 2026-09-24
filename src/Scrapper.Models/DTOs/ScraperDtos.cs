using Scrapper.Models.Enums;

namespace Scrapper.Models.DTOs;

/// <summary>
/// A field the user wants extracted. Only <see cref="Name"/> is required — everything else
/// is an optional escalation: <see cref="Type"/> narrows automatic inference/validation,
/// and <see cref="Selector"/> (Advanced Options in the UI) forces a specific, explicit
/// extraction path instead of the automatic multi-strategy engine.
/// </summary>
public class FieldDefinitionDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional. When omitted, the field's kind is inferred from its name.</summary>
    public FieldKind? Type { get; set; }

    public bool Required { get; set; }

    // Advanced/manual fallback — all optional. When Selector is supplied, it takes
    // priority over automatic extraction (see ExtractionPriority in the engine docs).
    public string? Selector { get; set; }
    public SelectorType SelectorType { get; set; } = SelectorType.Css;
    public ExtractionType ExtractionType { get; set; } = ExtractionType.Text;
    public string? Attribute { get; set; }
}

public class ScraperConfigDto
{
    public string Url { get; set; } = string.Empty;

    /// <summary>Optional. When omitted, the engine auto-detects the repeated record/card structure.</summary>
    public string? RecordSelector { get; set; }
    public SelectorType RecordSelectorType { get; set; } = SelectorType.Css;

    public List<FieldDefinitionDto> Fields { get; set; } = [];
    public int MaxRecords { get; set; } = 200;
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// When the listing page only shows a summary per record, automatically discover and
    /// visit each record's own detail/profile page for the fields that need it. Defaults to
    /// on — most directory listings need this for anything beyond name/location/rating.
    /// </summary>
    public bool EnableProfileCrawl { get; set; } = true;

    /// <summary>Maximum number of individual profile pages to visit in one scrape.</summary>
    public int MaxProfiles { get; set; } = 50;

    /// <summary>Maximum number of listing pages to follow via pagination (1 = no pagination).</summary>
    public int MaxPages { get; set; } = 1;

    /// <summary>
    /// Renders each page (listing, pagination, and profile pages) in a headless browser before
    /// extracting fields, so content that only exists after client-side JavaScript runs — a
    /// search-results widget, a "find a doctor" map — can be seen at all. Off by default:
    /// slower and heavier than the plain HTTP fetch, and unnecessary for the large majority of
    /// sites whose content is present in the initial server-rendered HTML.
    /// </summary>
    public bool EnableJavaScriptRendering { get; set; } = false;
}

public class ValidateUrlRequestDto
{
    public string Url { get; set; } = string.Empty;
}

public class ValidateUrlResponseDto
{
    public bool IsValid { get; set; }
    public string? Reason { get; set; }
    public string? NormalizedUrl { get; set; }
}

public class TestSelectorRequestDto
{
    public string Url { get; set; } = string.Empty;
    public SelectorType SelectorType { get; set; } = SelectorType.Css;
    public string Selector { get; set; } = string.Empty;
    public ExtractionType ExtractionType { get; set; } = ExtractionType.Text;
    public string? Attribute { get; set; }
    public string? RecordSelector { get; set; }
    public SelectorType RecordSelectorType { get; set; } = SelectorType.Css;
}

public class TestSelectorResponseDto
{
    public int MatchCount { get; set; }
    public List<string> SamplePreviews { get; set; } = [];
    public string? Warning { get; set; }
}

/// <summary>Per-field metadata about how a value was (or wasn't) found, for one record.</summary>
public class FieldResultDto
{
    public string? Value { get; set; }
    public FieldKind ResolvedType { get; set; } = FieldKind.Text;
    public ExtractionSource Source { get; set; } = ExtractionSource.None;
    public int Confidence { get; set; }
    public ConfidenceLevel ConfidenceLevel { get; set; } = ConfidenceLevel.None;
    public bool IsMissing { get; set; }
}

public class ScrapedRecordDto
{
    public Dictionary<string, string?> Fields { get; set; } = [];
    public List<string> MissingFields { get; set; } = [];

    /// <summary>Extraction detail per field name — how confident the engine is and which strategy produced it.</summary>
    public Dictionary<string, FieldResultDto> FieldResults { get; set; } = [];

    /// <summary>The individual detail/profile page discovered and (if enabled) visited for this record, if any.</summary>
    public string? ProfileUrl { get; set; }

    /// <summary>True once this record's profile page was successfully fetched and merged in.</summary>
    public bool ProfileVisited { get; set; }

    /// <summary>Which listing page (1-based) this record was found on — lets a multi-page scrape be exported split by page.</summary>
    public int PageNumber { get; set; } = 1;
}

public class ScrapeResultDto
{
    public int TotalRecords { get; set; }
    public List<ScrapedRecordDto> Records { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public List<string> FieldNames { get; set; } = [];

    /// <summary>True when the record structure was auto-detected rather than supplied by the user.</summary>
    public bool RecordSelectorAutoDetected { get; set; }

    /// <summary>Number of listing pages fetched (1 unless pagination was followed).</summary>
    public int PagesProcessed { get; set; } = 1;

    /// <summary>Number of individual profile pages successfully visited and merged in.</summary>
    public int ProfilesVisited { get; set; }
}

public class ExportRequestDto
{
    public List<string> FieldNames { get; set; } = [];
    public List<ScrapedRecordDto> Records { get; set; } = [];

    /// <summary>
    /// When true, exports one CSV per listing page instead of a single combined file, packaged
    /// together as one .zip — each entry named by its page number (e.g. "3.csv").
    /// </summary>
    public bool GroupByPage { get; set; } = false;
}
