using Scrapper.Models.DTOs;

namespace Scrapper.Services.Interfaces;

public interface ICsvExportService
{
    byte[] ExportToCsv(ExportRequestDto request);

    /// <summary>Builds one CSV per listing page, packaged as a single .zip (see <see cref="ExportRequestDto.GroupByPage"/>).</summary>
    byte[] ExportToZipByPage(ExportRequestDto request);
}
