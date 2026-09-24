using Scrapper.Models.DTOs;
using Scrapper.Services.Interfaces;
using Scrapper.Utils.Helpers;

namespace Scrapper.Services.Implementations;

public class CsvExportService : ICsvExportService
{
    public byte[] ExportToCsv(ExportRequestDto request) =>
        CsvHelper.BuildCsvBytesWithBom(request.FieldNames, request.Records);

    public byte[] ExportToZipByPage(ExportRequestDto request) =>
        CsvHelper.BuildPerPageZipBytes(request.FieldNames, request.Records);
}
