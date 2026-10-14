using System.Text.Json;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Queries.GetRows;

public sealed class GetImportBatchRowsQueryHandler
    : IRequestHandler<GetImportBatchRowsQuery, Result<PagedResult<ImportRowDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<ImportBatch> _batches;
    private readonly IReadRepository<ImportBatchRow> _rows;

    public GetImportBatchRowsQueryHandler(
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

    public async Task<Result<PagedResult<ImportRowDto>>> Handle(GetImportBatchRowsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<ImportRowDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id), ct);
        if (batch is null || !await ImportBatchVisibility.IsVisibleAsync(batch, _clinicContext, _clinicScopeResolver, ct))
            return Result<PagedResult<ImportRowDto>>.Failure(ImportBatchVisibility.NotFound());

        var access = ImportPayloadAccess.Check(batch);
        if (!access.IsSuccess)
            return Result<PagedResult<ImportRowDto>>.Failure(access.Error);

        ImportTable? table = ImportCanonicalNames.TryParseKey(request.Table, out var parsedTable) ? parsedTable : null;
        ImportRowStatusFilter.TryParse(request.Status, out var validation, out var apply);
        var codeFragment = request.Code is null ? null : ImportIssueJson.CodeFragment(request.Code);

        var total = await _rows.CountAsync(
            new ImportBatchRowsFilteredSpec(batch.Id, table, validation, apply, codeFragment), ct);
        var rows = await _rows.ListAsync(
            new ImportBatchRowsFilteredSpec(batch.Id, table, validation, apply, codeFragment, request.Page, request.PageSize), ct);

        return Result<PagedResult<ImportRowDto>>.Success(
            PagedResult<ImportRowDto>.Create(rows.Select(ToDto).ToList(), total, request.Page, request.PageSize));
    }

    private static ImportRowDto ToDto(ImportBatchRow row)
    {
        JsonElement? values = null;
        string? action = null;
        if (!string.IsNullOrWhiteSpace(row.NormalizedJson))
        {
            using var doc = JsonDocument.Parse(row.NormalizedJson);
            values = doc.RootElement.Clone();
            if (doc.RootElement.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String)
                action = a.GetString();
        }

        return new ImportRowDto(
            ImportCanonicalNames.Key(row.Table),
            row.RowNumber,
            new ImportRowValidationDto(row.ValidationStatus.ToString(), action),
            new ImportRowApplyDto(row.ApplyStatus.ToString(), row.TargetEntityId),
            ImportIssueJson.Parse(row.IssuesJson).Select(i => i.ToDto()).ToList(),
            values,
            ImportSourceJson.Parse(row.SourceJson));
    }
}
