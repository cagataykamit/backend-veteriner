using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Reporting;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Queries.GetErrors;

public sealed class GetImportBatchErrorsQueryHandler : IRequestHandler<GetImportBatchErrorsQuery, Result<ImportTemplateFileDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<ImportBatch> _batches;
    private readonly IReadRepository<ImportBatchRow> _rows;

    public GetImportBatchErrorsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<ImportBatch> batches,
        IReadRepository<ImportBatchRow> rows)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _batches = batches;
        _rows = rows;
    }

    public async ValueTask<Result<ImportTemplateFileDto>> Handle(GetImportBatchErrorsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ImportTemplateFileDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id), ct);
        if (batch is null || !await ImportBatchVisibility.IsVisibleAsync(batch, _clinicContext, _clinicScopeResolver, ct))
            return Result<ImportTemplateFileDto>.Failure(ImportBatchVisibility.NotFound());

        var access = ImportPayloadAccess.Check(batch);
        if (!access.IsSuccess)
            return Result<ImportTemplateFileDto>.Failure(access.Error);

        var rows = (await _rows.ListAsync(new ImportErrorRowsSpec(batch.Id), ct))
            .Select(r => new ImportErrorReportRow(
                r.Table,
                r.RowNumber,
                ImportSourceJson.Parse(r.SourceJson),
                ImportIssueJson.Parse(r.IssuesJson).Where(i => i.Severity == ImportIssueSeverity.Error).ToList()))
            .ToList();

        var file = request.Format == GetImportBatchErrorsQuery.Csv
            ? new ImportTemplateFileDto(ImportErrorReportWriter.BuildCsv(rows), ImportErrorReportWriter.CsvContentType, ImportErrorReportWriter.CsvFileName)
            : new ImportTemplateFileDto(ImportErrorReportWriter.BuildXlsx(rows), ImportErrorReportWriter.XlsxContentType, ImportErrorReportWriter.XlsxFileName);
        return Result<ImportTemplateFileDto>.Success(file);
    }
}
