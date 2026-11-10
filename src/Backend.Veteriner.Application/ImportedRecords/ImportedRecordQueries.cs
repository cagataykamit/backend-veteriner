using Ardalis.Specification;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.ImportedRecords;

// Eski program kayıtlarının okuma uçları (ADR-048 T1/T4, IMPORT_API_CONTRACT.md §12.8, §12.11). Salt okunur; kayıtlar
// yalnız çağıranın erişebildiği kliniklerden gelir. Hiçbir defter, rapor, finans veya portal sorgusu bu tabloları okumaz.

public sealed record ImportedClinicalRecordDto(
    Guid Id,
    DateOnly RecordedOn,
    string? RecordType,
    string? Complaint,
    string? History,
    string? Findings,
    string? Diagnosis,
    string? Treatment,
    string? Prescription,
    string? LabResults,
    string? Notes,
    string? VeterinarianName,
    decimal? WeightKg,
    decimal? TemperatureC,
    Guid ClinicId,
    Guid ImportBatchId,
    DateTime CreatedAtUtc);

public sealed record ImportedAccountEntryDto(
    Guid Id,
    DateOnly OccurredOn,
    string Kind,
    decimal Amount,
    string Currency,
    string? Description,
    string? PaymentMethod,
    string? DocumentNumber,
    string? PetName,
    Guid ClinicId,
    Guid ImportBatchId);

/// <summary>Borç ve tahsilat toplamları; yalnız bilgi (bakiye değildir, bakiyenin tek kaynağı devir ücretidir).</summary>
public sealed record ImportedAccountEntryTotalsDto(decimal Debit, decimal Credit);

public sealed record ImportedAccountEntriesPageDto(
    IReadOnlyList<ImportedAccountEntryDto> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    ImportedAccountEntryTotalsDto Totals);

public static class ImportedRecordPaging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}

public sealed record GetPetImportedClinicalRecordsQuery(Guid PetId, int Page = 1, int PageSize = ImportedRecordPaging.DefaultPageSize)
    : IRequest<Result<PagedResult<ImportedClinicalRecordDto>>>;

public sealed record GetClientImportedAccountEntriesQuery(Guid ClientId, int Page = 1, int PageSize = ImportedRecordPaging.DefaultPageSize)
    : IRequest<Result<ImportedAccountEntriesPageDto>>;

public sealed class GetPetImportedClinicalRecordsQueryValidator : AbstractValidator<GetPetImportedClinicalRecordsQuery>
{
    public GetPetImportedClinicalRecordsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, ImportedRecordPaging.MaxPageSize);
    }
}

public sealed class GetClientImportedAccountEntriesQueryValidator : AbstractValidator<GetClientImportedAccountEntriesQuery>
{
    public GetClientImportedAccountEntriesQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, ImportedRecordPaging.MaxPageSize);
    }
}

/// <summary>Hastanın eski program kayıtları (klinik kapsamında); <paramref name="page"/> null ise sayım için tümü.</summary>
public sealed class ImportedClinicalRecordsForPetSpec : Specification<ImportedClinicalRecord>
{
    public ImportedClinicalRecordsForPetSpec(
        Guid tenantId, Guid petId, Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds, int? page = null, int pageSize = 0)
    {
        Query.AsNoTracking();
        Query.Where(r => r.TenantId == tenantId && r.PetId == petId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (page is { } p)
        {
            Query.OrderByDescending(r => r.RecordedOn).ThenByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id);
            Query.Skip((p - 1) * pageSize).Take(pageSize);
        }
    }
}

/// <summary>Müşterinin eski program hareketleri (klinik kapsamında); <paramref name="page"/> null ise toplam/sayım için tümü.</summary>
public sealed class ImportedAccountEntriesForClientSpec : Specification<ImportedAccountEntry>
{
    public ImportedAccountEntriesForClientSpec(
        Guid tenantId, Guid clientId, Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds, int? page = null, int pageSize = 0)
    {
        Query.AsNoTracking();
        Query.Where(e => e.TenantId == tenantId && e.ClientId == clientId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (page is { } p)
        {
            Query.OrderByDescending(e => e.OccurredOn).ThenByDescending(e => e.CreatedAtUtc).ThenBy(e => e.Id);
            Query.Skip((p - 1) * pageSize).Take(pageSize);
        }
    }
}

/// <summary>Toplamlar için yalnız tür ve tutar (tek sorgu, satır verisi taşımaz).</summary>
public sealed record ImportedAccountEntryAmount(ImportedAccountEntryKind Kind, decimal Amount);

public sealed class ImportedAccountEntryAmountsSpec : Specification<ImportedAccountEntry, ImportedAccountEntryAmount>
{
    public ImportedAccountEntryAmountsSpec(Guid tenantId, Guid clientId, Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.AsNoTracking();
        Query.Where(e => e.TenantId == tenantId && e.ClientId == clientId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.Select(e => new ImportedAccountEntryAmount(e.Kind, e.Amount));
    }
}

public sealed class GetPetImportedClinicalRecordsQueryHandler
    : IRequestHandler<GetPetImportedClinicalRecordsQuery, Result<PagedResult<ImportedClinicalRecordDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<ImportedClinicalRecord> _records;

    public GetPetImportedClinicalRecordsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Pet> pets,
        IReadRepository<ImportedClinicalRecord> records)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _pets = pets;
        _records = records;
    }

    public async ValueTask<Result<PagedResult<ImportedClinicalRecordDto>>> Handle(GetPetImportedClinicalRecordsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<PagedResult<ImportedClinicalRecordDto>>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        if (await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct) is null)
            return Result<PagedResult<ImportedClinicalRecordDto>>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, _clinicContext.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<PagedResult<ImportedClinicalRecordDto>>.Failure(scope.Error);

        var (clinicId, accessible) = (scope.Value!.SingleClinicId, scope.Value.AccessibleClinicIds);
        var total = await _records.CountAsync(new ImportedClinicalRecordsForPetSpec(tenantId, request.PetId, clinicId, accessible), ct);
        var rows = await _records.ListAsync(
            new ImportedClinicalRecordsForPetSpec(tenantId, request.PetId, clinicId, accessible, request.Page, request.PageSize), ct);

        var items = rows
            .Select(r => new ImportedClinicalRecordDto(
                r.Id, r.RecordedOn, r.RecordType, r.Complaint, r.History, r.Findings, r.Diagnosis, r.Treatment, r.Prescription,
                r.LabResults, r.Notes, r.VeterinarianName, r.WeightKg, r.TemperatureC, r.ClinicId, r.ImportBatchId, r.CreatedAtUtc))
            .ToList();
        return Result<PagedResult<ImportedClinicalRecordDto>>.Success(
            PagedResult<ImportedClinicalRecordDto>.Create(items, total, request.Page, request.PageSize));
    }
}

public sealed class GetClientImportedAccountEntriesQueryHandler
    : IRequestHandler<GetClientImportedAccountEntriesQuery, Result<ImportedAccountEntriesPageDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<ImportedAccountEntry> _entries;

    public GetClientImportedAccountEntriesQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Client> clients,
        IReadRepository<ImportedAccountEntry> entries)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _clients = clients;
        _entries = entries;
    }

    public async ValueTask<Result<ImportedAccountEntriesPageDto>> Handle(GetClientImportedAccountEntriesQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ImportedAccountEntriesPageDto>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        if (await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.ClientId), ct) is null)
            return Result<ImportedAccountEntriesPageDto>.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, _clinicContext.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<ImportedAccountEntriesPageDto>.Failure(scope.Error);

        var (clinicId, accessible) = (scope.Value!.SingleClinicId, scope.Value.AccessibleClinicIds);
        var amounts = await _entries.ListAsync(new ImportedAccountEntryAmountsSpec(tenantId, request.ClientId, clinicId, accessible), ct);
        var rows = await _entries.ListAsync(
            new ImportedAccountEntriesForClientSpec(tenantId, request.ClientId, clinicId, accessible, request.Page, request.PageSize), ct);

        var items = rows
            .Select(e => new ImportedAccountEntryDto(
                e.Id, e.OccurredOn, e.Kind.ToString(), e.Amount, e.Currency, e.Description, e.PaymentMethod, e.DocumentNumber,
                e.PetName, e.ClinicId, e.ImportBatchId))
            .ToList();
        var totals = new ImportedAccountEntryTotalsDto(
            amounts.Where(a => a.Kind == ImportedAccountEntryKind.Debit).Sum(a => a.Amount),
            amounts.Where(a => a.Kind == ImportedAccountEntryKind.Credit).Sum(a => a.Amount));
        var page = PagedResult<ImportedAccountEntryDto>.Create(items, amounts.Count, request.Page, request.PageSize);
        return Result<ImportedAccountEntriesPageDto>.Success(
            new ImportedAccountEntriesPageDto(page.Items, page.Page, page.PageSize, page.TotalItems, page.TotalPages, totals));
    }
}

/// <summary>Müşteri veri kopyası (ADR-032): müşterinin bugünkü hastalarına ait eski program kayıtları, yeniden eskiye, en çok <paramref name="take"/>.</summary>
public sealed class ImportedClinicalRecordsForClientDataExportSpec : Specification<ImportedClinicalRecord>
{
    public ImportedClinicalRecordsForClientDataExportSpec(
        Guid tenantId, Guid clientId, Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds, int take)
    {
        Query.AsNoTracking();
        Query.Where(r => r.TenantId == tenantId && r.ClientId == clientId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderByDescending(r => r.RecordedOn).ThenBy(r => r.Id).Take(take);
    }
}
