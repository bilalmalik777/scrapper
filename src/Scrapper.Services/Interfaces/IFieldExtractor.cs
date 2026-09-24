using Scrapper.Models.DTOs;

namespace Scrapper.Services.Interfaces;

public interface IFieldExtractor
{
    List<ScrapedRecordDto> ExtractRecords(string html, ScraperConfigDto config, out List<string> warnings);

    (int MatchCount, List<string> Previews) TestSelector(
        string html,
        string? recordSelector,
        Scrapper.Models.Enums.SelectorType recordSelectorType,
        string selector,
        Scrapper.Models.Enums.SelectorType selectorType,
        Scrapper.Models.Enums.ExtractionType extractionType,
        string? attribute);
}
