using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Template;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Queries.GetTemplate;

public sealed class GetImportTemplateQueryHandler : IRequestHandler<GetImportTemplateQuery, Result<ImportTemplateFileDto>>
{
    public Task<Result<ImportTemplateFileDto>> Handle(GetImportTemplateQuery request, CancellationToken ct)
    {
        if (request.Format == GetImportTemplateQueryValidator.Xlsx)
        {
            return Task.FromResult(Result<ImportTemplateFileDto>.Success(new ImportTemplateFileDto(
                ImportTemplateBuilder.BuildXlsx(),
                ImportTemplateBuilder.XlsxContentType,
                ImportTemplateBuilder.XlsxFileName)));
        }

        // Biçim ve tablo anahtarı doğrulayıcıda denetlendi.
        ImportCanonicalNames.TryParseKey(request.Table, out var table);
        return Task.FromResult(Result<ImportTemplateFileDto>.Success(new ImportTemplateFileDto(
            ImportTemplateBuilder.BuildCsv(table),
            ImportTemplateBuilder.CsvContentType,
            ImportTemplateBuilder.CsvFileName(table))));
    }
}
