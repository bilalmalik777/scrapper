namespace Scrapper.Models.Enums;

public enum SelectorType
{
    Css,
    XPath
}

public enum ExtractionType
{
    Text,
    Attribute,
    Html,
    Link
}

/// <summary>
/// The semantic kind of a field, used to drive automatic inference, extraction
/// strategy selection, and value validation when no explicit selector is supplied.
/// </summary>
public enum FieldKind
{
    /// <summary>Kind was not supplied and could not be inferred confidently; treated as free text.</summary>
    Auto,
    Text,
    Name,
    Location,
    Currency,
    Email,
    Phone,
    Url,
    Rating,
    Number,

    /// <summary>Long-form free text — a biography/about section, not a short label value.</summary>
    Biography
}

/// <summary>
/// Which extraction strategy produced a field's value, surfaced to the client for transparency.
/// </summary>
public enum ExtractionSource
{
    None,
    Selector,
    StructuredData,
    SemanticHtml,
    Pattern,
    Ai
}

public enum ConfidenceLevel
{
    None,
    Low,
    Medium,
    High
}

/// <summary>Lifecycle status of a long-running, checkpointed paged crawl job.</summary>
public enum CrawlJobStatus
{
    NotStarted,
    Running,

    /// <summary>Stopped mid-crawl without an error (shutdown, cancellation) — safe to resume.</summary>
    Paused,

    /// <summary>The target site explicitly denied access (HTTP 403) or a page kept failing after retries — safe to resume/retry-failed once the block lifts.</summary>
    Blocked,
    Completed,
    Failed
}
