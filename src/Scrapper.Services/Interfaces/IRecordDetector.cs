using HtmlAgilityPack;
using Scrapper.Models.DTOs;

namespace Scrapper.Services.Interfaces;

public interface IRecordDetector
{
    /// <summary>
    /// Finds the repeated "card"/"row" structure on a listing page (e.g. one node per
    /// specialist in a directory). Returns null when no clear repeated structure is found,
    /// in which case the caller should fall back to treating the whole page as one record.
    /// </summary>
    /// <param name="fields">
    /// The fields the caller actually wants. When supplied, candidate groups are preferred
    /// by how well they actually contain these fields, not just by structural repetition —
    /// this is what stops an incidental repeated widget (e.g. skill/rating bars on a detail
    /// page) from being mistaken for "the records" just because it repeats more often than
    /// the page's one genuine content section.
    /// </param>
    List<HtmlNode>? DetectRecords(HtmlNode documentNode, IReadOnlyList<FieldDefinitionDto>? fields = null);
}
