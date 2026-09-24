using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Scrapper.Models.Common;
using Scrapper.Models.DTOs;
using Xunit;

namespace Scrapper.Api.IntegrationTests;

public class ScraperControllerTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    // The API serializes enums as strings (JsonStringEnumConverter, see
    // ServiceCollectionExtensions.AddScrapperInfrastructure) — the test client needs the same
    // converter registered to read them back, since HttpClient's ReadFromJsonAsync defaults to
    // strict numeric enum parsing otherwise.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ValidateUrl_PrivateIp_ReturnsInvalidWithoutStackTrace()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/scraper/validate-url", new ValidateUrlRequestDto { Url = "http://192.168.1.1" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ValidateUrlResponseDto>>();
        body!.Data!.IsValid.Should().BeFalse();
        body.Data.Reason.Should().NotContain("Exception");
    }

    [Fact]
    public async Task ValidateUrl_MalformedUrl_ReturnsBadRequestFromValidator()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/scraper/validate-url", new ValidateUrlRequestDto { Url = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Scrape_MissingFields_ReturnsBadRequestValidation()
    {
        var config = new ScraperConfigDto { Url = "https://example.com", Fields = [] };

        var response = await _client.PostAsJsonAsync("/api/v1/scraper/scrape", config);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Scrape_BlockedUrl_ReturnsGracefulErrorNotException()
    {
        var config = new ScraperConfigDto
        {
            Url = "http://169.254.169.254/latest/meta-data",
            Fields = [new FieldDefinitionDto { Name = "Name", Selector = ".name" }]
        };

        var response = await _client.PostAsJsonAsync("/api/v1/scraper/scrape", config);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ScrapeResultDto>>();
        body!.Data!.Errors.Should().NotBeEmpty();
        body.Data.Errors.Should().NotContain(e => e.Contains("Exception") || e.Contains("StackTrace"));
    }

    [Fact]
    public async Task Scrape_FieldWithNameOnly_PassesValidationWithoutSelectorOrType()
    {
        var config = new ScraperConfigDto
        {
            Url = "http://169.254.169.254/latest/meta-data",
            Fields = [new FieldDefinitionDto { Name = "Name" }, new FieldDefinitionDto { Name = "Location" }]
        };

        var response = await _client.PostAsJsonAsync("/api/v1/scraper/scrape", config);

        // Should reach the engine (and fail on the SSRF-blocked URL) rather than being
        // rejected by validation for lacking a selector or type.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ScrapeResultDto>>();
        body!.Data!.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Scrape_DuplicateFieldNames_ReturnsBadRequestValidation()
    {
        var config = new ScraperConfigDto
        {
            Url = "https://example.com",
            Fields = [new FieldDefinitionDto { Name = "Name" }, new FieldDefinitionDto { Name = "name" }]
        };

        var response = await _client.PostAsJsonAsync("/api/v1/scraper/scrape", config);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Export_ProducesCsvFile()
    {
        var request = new ExportRequestDto
        {
            FieldNames = ["Name"],
            Records = [new ScrapedRecordDto { Fields = new() { ["Name"] = "Sample" } }]
        };

        var response = await _client.PostAsJsonAsync("/api/v1/scraper/export", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("Sample");
    }

    [Fact]
    public async Task Scrape_DoctifyListingWithDeepCrawl_VisitsProfilePagesAndMergesBio()
    {
        var config = new ScraperConfigDto
        {
            Url = "https://www.doctify.com/uk/find/prolotherapy/united-kingdom/specialists",
            MaxRecords = 3,
            MaxProfiles = 2,
            TimeoutSeconds = 20,
            Fields =
            [
                new FieldDefinitionDto { Name = "Name", Required = true },
                new FieldDefinitionDto { Name = "Bio", Type = Models.Enums.FieldKind.Biography },
            ]
        };

        var response = await _client.PostAsJsonAsync("/api/v1/scraper/scrape", config);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ScrapeResultDto>>(JsonOptions);
        var data = body!.Data!;

        data.TotalRecords.Should().BeGreaterThan(0);
        data.ProfilesVisited.Should().BeGreaterThan(0);
        data.Records.Should().Contain(r => r.ProfileVisited);
        data.Records.Should().Contain(r => r.Fields.ContainsKey("Profile URL") && r.Fields["Profile URL"]!.Contains("/uk/specialist/"));
        data.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task TestSelector_BlockedUrl_ReturnsWarningNotException()
    {
        var request = new TestSelectorRequestDto { Url = "http://10.0.0.1", Selector = ".name" };

        var response = await _client.PostAsJsonAsync("/api/v1/scraper/test-selector", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TestSelectorResponseDto>>();
        body!.Data!.MatchCount.Should().Be(0);
        body.Data.Warning.Should().NotBeNullOrEmpty();
    }
}
