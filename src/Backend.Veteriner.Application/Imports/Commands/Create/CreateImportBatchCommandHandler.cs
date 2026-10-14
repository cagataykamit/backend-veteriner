using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;
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
    private readonly TimeProvider _timeProvider;

    public CreateImportBatchCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<ImportBatch> batchesRead,
        IRepository<ImportBatch> batchesWrite,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _batchesRead = batchesRead;
        _batchesWrite = batchesWrite;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ImportBatchCreateResultDto>> Handle(CreateImportBatchCommand request, CancellationToken ct)
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

        var existing = await _batchesRead.FirstOrDefaultAsync(new ImportBatchByRequestIdSpec(tenantId, request.RequestId), ct);
        if (existing is not null)
            return Replay(existing, request);

        var openDrafts = await _batchesRead.CountAsync(new OpenImportDraftsSpec(tenantId), ct);
        if (openDrafts >= ImportBatch.MaxOpenDraftsPerTenant)
        {
            return Fail(
                "Imports.TooManyOpenDrafts",
                $"Kiracıda en fazla {ImportBatch.MaxOpenDraftsPerTenant} açık taslak parti olabilir; önce birini tamamlayın veya silin.");
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
        try
        {
            // Repository AddAsync kaydı hemen kalıcılaştırır (SaveChanges içerir).
            await _batchesWrite.AddAsync(batch, ct);
        }
        catch (DbUpdateException)
        {
            // Eşzamanlı çift istek: (TenantId, RequestId) benzersiz indeksi kaybedene çarptı; kazanan parti döner.
            var winner = await _batchesRead.FirstOrDefaultAsync(
                new ImportBatchByRequestIdSpec(tenantId, request.RequestId), ct);
            if (winner is null)
                throw;

            return Replay(winner, request);
        }

        return Result<ImportBatchCreateResultDto>.Success(new ImportBatchCreateResultDto(true, batch.ToDto()));
    }

    private static Result<ImportBatchCreateResultDto> Replay(ImportBatch existing, CreateImportBatchCommand request)
    {
        if (!existing.MatchesRequest(request.ClinicId, request.Name))
            return Fail("Imports.RequestIdConflict", "Bu requestId başka içerikli bir parti için kullanılmış.");

        return Result<ImportBatchCreateResultDto>.Success(new ImportBatchCreateResultDto(false, existing.ToDto()));
    }

    private static Result<ImportBatchCreateResultDto> Fail(string code, string message)
        => Result<ImportBatchCreateResultDto>.Failure(code, message);
}
