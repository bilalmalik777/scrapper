using HtmlAgilityPack;

namespace Scrapper.Services.Interfaces;

public interface IStructuredDataParser
{
    /// <summary>
    /// Parses every JSON-LD block on the page into a flat list of entity dictionaries
    /// (e.g. one per Person/Product in an ItemList or @graph), in document order.
    /// </summary>
    List<Dictionary<string, object?>> Parse(HtmlNode documentNode);
}
