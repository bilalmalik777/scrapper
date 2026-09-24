using Scrapper.Models.DTOs;

namespace Scrapper.Services.Interfaces;

public interface IUrlValidationService
{
    Task<ValidateUrlResponseDto> ValidateAsync(string url, CancellationToken cancellationToken = default);
}
