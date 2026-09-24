using FluentAssertions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging.Abstractions;
using Scrapper.Services.Implementations;
using Xunit;

namespace Scrapper.Services.Tests;

public class StructuredDataParserTests
{
    private readonly StructuredDataParser _sut = new(NullLogger<StructuredDataParser>.Instance);

    [Fact]
    public void Parse_SinglePersonObject_ReturnsOneEntity()
    {
        const string html = """
            <html><head><script type="application/ld+json">
            { "@type": "Person", "name": "Dr John Smith", "address": { "addressLocality": "Manchester" } }
            </script></head><body></body></html>
            """;
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var entities = _sut.Parse(doc.DocumentNode);

        entities.Should().HaveCount(1);
        entities[0]["name"].Should().Be("Dr John Smith");
    }

    [Fact]
    public void Parse_ItemListWrapper_FlattensToIndividualItems()
    {
        const string html = """
            <html><head><script type="application/ld+json">
            {
              "@type": "ItemList",
              "itemListElement": [
                { "item": { "@type": "Person", "name": "Dr John Smith" } },
                { "item": { "@type": "Person", "name": "Dr Sarah Jones" } }
              ]
            }
            </script></head><body></body></html>
            """;
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var entities = _sut.Parse(doc.DocumentNode);

        entities.Should().HaveCount(2);
        entities[0]["name"].Should().Be("Dr John Smith");
        entities[1]["name"].Should().Be("Dr Sarah Jones");
    }

    [Fact]
    public void Parse_MalformedJson_SkipsWithoutThrowing()
    {
        const string html = """
            <html><head><script type="application/ld+json">{ not valid json </script></head><body></body></html>
            """;
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var act = () => _sut.Parse(doc.DocumentNode);

        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Fact]
    public void Parse_NoJsonLdOnPage_ReturnsEmpty()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<html><body><p>No structured data here</p></body></html>");

        _sut.Parse(doc.DocumentNode).Should().BeEmpty();
    }
}
