using System.IO.Compression;
using System.Text;
using Scrapper.Models.DTOs;

namespace Scrapper.Utils.Helpers;

public static class CsvHelper
{
    public static string BuildCsv(IReadOnlyList<string> fieldNames, IReadOnlyList<ScrapedRecordDto> records)
    {
        var sb = new StringBuilder();

        sb.AppendLine(string.Join(',', fieldNames.Select(Escape)));

        foreach (var record in records)
        {
            var values = fieldNames.Select(name =>
                record.Fields.TryGetValue(name, out var value) ? Escape(value) : string.Empty);
            sb.AppendLine(string.Join(',', values));
        }

        return sb.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        var escaped = value.Replace("\"", "\"\"");
        return needsQuoting ? $"\"{escaped}\"" : escaped;
    }

    public static byte[] BuildCsvBytesWithBom(IReadOnlyList<string> fieldNames, IReadOnlyList<ScrapedRecordDto> records)
    {
        var csv = BuildCsv(fieldNames, records);
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(csv);
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    /// <summary>
    /// Groups records by <see cref="ScrapedRecordDto.PageNumber"/> and builds one CSV per
    /// group, packaged together as a single .zip — each entry named "{pageNumber}.csv" so a
    /// multi-page scrape can be exported as one file per listing page instead of one combined
    /// file. Records are grouped in ascending page-number order regardless of input order.
    /// </summary>
    public static byte[] BuildPerPageZipBytes(IReadOnlyList<string> fieldNames, IReadOnlyList<ScrapedRecordDto> records)
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var byPage = records
                .GroupBy(r => r.PageNumber)
                .OrderBy(g => g.Key);

            foreach (var page in byPage)
            {
                var entry = archive.CreateEntry($"{page.Key}.csv", CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                var bytes = BuildCsvBytesWithBom(fieldNames, page.ToList());
                entryStream.Write(bytes, 0, bytes.Length);
            }
        }

        return zipStream.ToArray();
    }
}
