using Scrapper.Models.Enums;

namespace Scrapper.Services.Models;

/// <summary>A single candidate value produced by one extraction strategy, before validation/scoring.</summary>
public record ExtractionCandidate(string Value, int Confidence, ExtractionSource Source);
