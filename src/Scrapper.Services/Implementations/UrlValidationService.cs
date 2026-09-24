using Microsoft.Extensions.Logging;
using Scrapper.Models.DTOs;
using Scrapper.Services.Interfaces;
using Scrapper.Utils.Helpers;

namespace Scrapper.Services.Implementations;

public class UrlValidationService(ILogger<UrlValidationService> logger) : IUrlValidationService
{
    public async Task<ValidateUrlResponseDto> ValidateAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!UrlSecurityHelper.TryParseAllowedUrl(url, out var uri, out var reason))
        {
            return new ValidateUrlResponseDto { IsValid = false, Reason = reason };
        }

        var blocked = await UrlSecurityHelper.ResolvesToBlockedAddressAsync(uri!.Host, cancellationToken);
        if (blocked)
        {
            logger.LogWarning("Rejected URL resolving to a blocked address: {Host}", uri.Host);
            return new ValidateUrlResponseDto
            {
                IsValid = false,
                Reason = "Access to internal or private network addresses is not allowed."
            };
        }

        return new ValidateUrlResponseDto { IsValid = true, NormalizedUrl = uri.ToString() };
    }
}
