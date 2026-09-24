using FluentAssertions;
using Scrapper.Models.DTOs;
using Scrapper.Services.Implementations;
using Xunit;

namespace Scrapper.Services.Tests;

public class CsvExportServiceTests
{
    private readonly CsvExportService _sut = new();

    [Fact]
    public void ExportToCsv_ProducesUtf8BomBytes()
    {
        var request = new ExportRequestDto
        {
            FieldNames = ["Name"],
            Records = [new ScrapedRecordDto { Fields = new() { ["Name"] = "Test" } }]
        };

        var bytes = _sut.ExportToCsv(request);

        bytes.Should().NotBeEmpty();
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        text.Should().Contain("Test");
    }
}
