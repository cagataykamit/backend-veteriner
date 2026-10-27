using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Reading;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Imports.Commands.Create;

public sealed class CreateImportBatchCommandHandler
    : IRequestHandler<CreateImportBatchCommand, Result<ImportBatchCreateResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<ImportBatch> _batchesRead;
    private readonly IRepository<ImportBatch> _batchesWrite;
    private readonly IRepository<ImportBatchRow> _rowsWrite;
    private readonly ImportUploadReader _reader;
    private readonly ImportTableInfoLoader _tableInfo;
    private readonly TimeProvider _timeProvider;
    private readonly ITrialLimitPolicy _trialLimitPolicy;

    public CreateImportBatchCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<ImportBatch> batchesRead,
        IRepository<ImportBatch> batchesWrite,
        IRepository<ImportBatchRow> rowsWrite,
        ImportUploadReader reader,
        ImportTableInfoLoader tableInfo,
        TimeProvider timeProvider,
        ITrialLimitPolicy trialLimitPolicy)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _batchesRead = batchesRead;
        _batchesWrite = batchesWrite;
        _rowsWrite = rowsWrite;
        _reader = reader;
        _tableInfo = tableInfo;
        _timeProvider = timeProvider;
        _trialLimitPolicy = trialLimitPolicy;
    }

    public async ValueTask<Result<ImportBatchCreateResultDto>> Handle(CreateImportBatchCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
            return Fail("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Fail("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Fail("Tenants.TenantInactive", "Pasif kiracı için içe aktarma partisi oluşturulamaz.");

        // Klinik yalnız aşı satırları için gerekir; bağlamdan varsayılmaz, açıkça seçilir.
        if (request.ClinicId is { } clinicId)
        {
            if (_clinicContext.ClinicId is { } contextClinicId && contextClinicId != clinicId)
            {
                return Fail(
                    "Imports.ClinicContextMismatch",
                    "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
            }

            var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, clinicId, ct);
            if (!clinicAccess.IsSuccess)
                return Result<ImportBatchCreateResultDto>.Failure(clinicAccess.Error);
        }

        // Ucuz denetimler (uzantı, imza, boyut, özet) ayrıştırmadan önce: tekrar ve mükerrer dosya kararları buna dayanır.
        var prepared = _reader.Prepare(request.Files, request.Encoding, request.Delimiter);
        if (!prepared.IsSuccess)
            return Result<ImportBatchCreateResultDto>.Failure(prepared.Error);

        var upload = prepared.Value!;

        var existing = await _batchesRead.FirstOrDefaultAsync(new ImportBatchByRequestIdSpec(tenantId, request.RequestId), ct);
        if (existing is not null)
            return await ReplayAsync(existing, request, upload.ContentHash, ct);

        var duplicate = await FindDuplicateFileAsync(tenantId, upload.ContentHash, ct);
        if (duplicate is not null)
            return duplicate.Value;

        var openDrafts = await _batchesRead.CountAsync(new OpenImportDraftsSpec(tenantId), ct);
        if (openDrafts >= ImportBatch.MaxOpenDraftsPerTenant)
        {
            return Fail(
                "Imports.TooManyOpenDrafts",
                $"Kiracıda en fazla {ImportBatch.MaxOpenDraftsPerTenant} açık taslak parti olabilir; önce birini tamamlayın veya silin.");
        }

        // Deneme kiracısında tablo başına satır tavanı düşüktür (TRIAL-004); aşım Trial.ImportLimitExceeded (422).
        var read = _reader.Read(upload, trialTenant: await _trialLimitPolicy.IsTrialAsync(tenantId, ct));
        if (!read.IsSuccess)
            return Result<ImportBatchCreateResultDto>.Failure(read.Error);

        var tables = read.Value!.Tables;
        if (tables.Any(t => t.Table == ImportTable.Vaccinations) && request.ClinicId is null)
        {
            return Fail(
                "Imports.ClinicRequired",
                "Aşı tablosu içeren partide clinicId zorunludur; aşılar bu kliniğe yazılır.");
        }

        var created = ImportBatch.Create(
            tenantId,
            request.ClinicId,
            request.RequestId,
            request.Name,
            userId,
            _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<ImportBatchCreateResultDto>.Failure(created.Error);

        var batch = created.Value!;
        var attached = batch.AttachFile(
            upload.ContentHash,
            RowCount(tables, ImportTable.Customers),
            RowCount(tables, ImportTable.Pets),
            RowCount(tables, ImportTable.Vaccinations));
        if (!attached.IsSuccess)
            return Result<ImportBatchCreateResultDto>.Failure(attached.Error);

        try
        {
            // Parti hemen kaydedilir (benzersizlik burada elenir); komut işlem içindedir (ITransactionalRequest).
            await _batchesWrite.AddAsync(batch, ct);
            await _batchesWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Eşzamanlı çift istek: benzersiz indeks (requestId veya içerik özeti) kaybedene çarptı.
            var winner = await _batchesRead.FirstOrDefaultAsync(
                new ImportBatchByRequestIdSpec(tenantId, request.RequestId), ct);
            if (winner is not null)
                return await ReplayAsync(winner, request, upload.ContentHash, ct);

            var duplicateAfterRace = await FindDuplicateFileAsync(tenantId, upload.ContentHash, ct);
            if (duplicateAfterRace is not null)
                return duplicateAfterRace.Value;

            throw;
        }

        var rows = tables.SelectMany(t => t.Rows.Select(r =>
            ImportBatchRow.Create(
                batch.Id,
                t.Table,
                r.RowNumber,
                ImportSourceRowJson.Serialize(t.Headers, r.Cells),
                normalizedJson: null)));
        await _rowsWrite.AddRangeAsync(rows, ct);
        await _rowsWrite.SaveChangesAsync(ct);

        var headers = tables.ToDictionary(t => t.Table, t => t.Headers);
        var warnings = read.Value.Warnings
            .Select(w => new ImportWarningDto(
                w.Code,
                w.Table is { } table ? ImportCanonicalNames.Key(table) : null,
                w.Message))
            .ToList();

        return Result<ImportBatchCreateResultDto>.Success(
            new ImportBatchCreateResultDto(true, batch.ToDto(ImportTableInfoLoader.Build(batch, headers), warnings)));
    }

    private async Task<Result<ImportBatchCreateResultDto>> ReplayAsync(
        ImportBatch existing,
        CreateImportBatchCommand request,
        string contentHash,
        CancellationToken ct)
    {
        if (!existing.MatchesRequest(request.ClinicId, request.Name, contentHash))
            return Fail("Imports.RequestIdConflict", "Bu requestId başka içerikli bir parti için kullanılmış.");

        var tables = await _tableInfo.LoadAsync(existing, ct);
        return Result<ImportBatchCreateResultDto>.Success(
            new ImportBatchCreateResultDto(false, existing.ToDto(tables)));
    }

    /// <summary>
    /// Aynı özetli aktif parti varsa <c>Imports.DuplicateFile</c>; parti kimliği yalnız çağıranın görebildiği partiyse
    /// (klinik bağlı parti başka kliniğe sızdırılmaz) hata ayrıntısına eklenir.
    /// </summary>
    private async Task<Result<ImportBatchCreateResultDto>?> FindDuplicateFileAsync(
        Guid tenantId,
        string contentHash,
        CancellationToken ct)
    {
        var holder = await _batchesRead.FirstOrDefaultAsync(new ImportBatchByContentHashSpec(tenantId, contentHash), ct);
        if (holder is null)
            return null;

        IReadOnlyDictionary<string, object?>? extensions = null;
        if (await ImportBatchVisibility.IsVisibleAsync(holder, _clinicContext, _clinicScopeResolver, ct))
            extensions = new Dictionary<string, object?> { ["batchId"] = holder.Id };

        return Result<ImportBatchCreateResultDto>.Failure(new Error(
            "Imports.DuplicateFile",
            "Bu dosya zaten yüklenmiş ve henüz geri alınmamış/silinmemiş bir partide duruyor.",
            extensions));
    }

    private static int RowCount(IReadOnlyList<ImportSourceTable> tables, ImportTable table)
        => tables.FirstOrDefault(t => t.Table == table)?.Rows.Count ?? 0;

    private static Result<ImportBatchCreateResultDto> Fail(string code, string message)
        => Result<ImportBatchCreateResultDto>.Failure(code, message);
}
