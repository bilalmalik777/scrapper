using Scrapper.Models.DTOs;

namespace Scrapper.Services.Interfaces;

public interface IScraperEngine
{
    Task<ScrapeResultDto> ScrapeAsync(ScraperConfigDto config, CancellationToken cancellationToken = default);
}
