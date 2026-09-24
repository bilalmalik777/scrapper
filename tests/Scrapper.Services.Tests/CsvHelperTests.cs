using System.Text;
using FluentAssertions;
using Scrapper.Models.DTOs;
using Scrapper.Utils.Helpers;
using Xunit;

namespace Scrapper.Services.Tests;

public class CsvHelperTests
{
    [Fact]
    public void BuildCsv_NormalData_ProducesExpectedRows()
    {
        var fields = new List<string> { "Name", "Location" };
        var records = new List<ScrapedRecordDto>
        {
            new() { Fields = new() { ["Name"] = "John Smith", ["Location"] = "London" } }
        };

        var csv = CsvHelper.BuildCsv(fields, records);

        csv.Should().Contain("Name,Location");
        csv.Should().Contain("John Smith,London");
    }

    [Fact]
    public void BuildCsv_MissingField_ProducesEmptyCell()
    {
        var fields = new List<string> { "Name", "Location" };
        var records = new List<ScrapedRecordDto>
        {
            new() { Fields = new() { ["Name"] = "John Smith" } }
        };

        var csv = CsvHelper.BuildCsv(fields, records);

        csv.Should().Contain("John Smith,");
    }

    [Fact]
    public void BuildCsv_ValueWithComma_IsQuoted()
    {
        var fields = new List<string> { "Name" };
        var records = new List<ScrapedRecordDto> { new() { Fields = new() { ["Name"] = "Smith, John" } } };

        var csv = CsvHelper.BuildCsv(fields, records);

        csv.Should().Contain("\"Smith, John\"");
    }

    [Fact]
    public void BuildCsv_ValueWithQuotes_IsEscaped()
    {
        var fields = new List<string> { "Name" };
        var records = new List<ScrapedRecordDto> { new() { Fields = new() { ["Name"] = "The \"Best\" Clinic" } } };

        var csv = CsvHelper.BuildCsv(fields, records);

        csv.Should().Contain("\"The \"\"Best\"\" Clinic\"");
    }

    [Fact]
    public void BuildCsv_ValueWithNewline_IsQuoted()
    {
        var fields = new List<string> { "Notes" };
        var records = new List<ScrapedRecordDto> { new() { Fields = new() { ["Notes"] = "Line1\nLine2" } } };

        var csv = CsvHelper.BuildCsv(fields, records);

        csv.Should().Contain("\"Line1\nLine2\"");
    }

    [Fact]
    public void BuildCsv_UnicodeValue_IsPreserved()
    {
        var fields = new List<string> { "Name" };
        var records = new List<ScrapedRecordDto> { new() { Fields = new() { ["Name"] = "Jöhn Śmith 中文" } } };

        var csv = CsvHelper.BuildCsv(fields, records);

        csv.Should().Contain("Jöhn Śmith 中文");
    }

    [Fact]
    public void BuildCsv_EmptyDataset_ProducesHeaderOnly()
    {
        var fields = new List<string> { "Name", "Location" };
        var records = new List<ScrapedRecordDto>();

        var csv = CsvHelper.BuildCsv(fields, records);

        csv.Trim().Should().Be("Name,Location");
    }

    [Fact]
    public void BuildCsvBytesWithBom_StartsWithUtf8Bom()
    {
        var bytes = CsvHelper.BuildCsvBytesWithBom(["Name"], []);
        var bom = Encoding.UTF8.GetPreamble();

        bytes.Take(bom.Length).Should().Equal(bom);
    }
}
