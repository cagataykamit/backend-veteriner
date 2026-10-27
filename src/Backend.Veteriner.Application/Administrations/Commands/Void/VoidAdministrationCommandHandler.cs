using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Administrations.Specs;
using Backend.Veteriner.Application.ChargeSuggestions.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.StockBatches.Specs;
using Backend.Veteriner.Application.StockMovements.Commands.Reverse;
using Backend.Veteriner.Application.StockMovements.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Administrations.Commands.Void;

/// <summary>
/// Uygulamayı iptal eder. Stoğa iade seçilirse stoğu mevcut <see cref="ReverseStockMovementCommand"/> ile aynı transaction'da geri verir
/// (ters hareket kuralları tek yerde yaşar; pasif ürün de dahil) ve stok adımı başarısız olursa iptal geri alınır.
/// Zarar/imha seçilirse stok hareketi yazılmaz; orijinal çıkış hareketi geçerli kalır.
/// Uygulama bir aşı kaydına bağlıysa aşı kaydı da aynı transaction'da iptal edilir; bekleyen ücret kalemi de düşer.
/// </summary>
public sealed class VoidAdministrationCommandHandler
    : IRequestHandler<VoidAdministrationCommand, Result<AdministrationDto>>
{
    private const string StockReferenceType = Administration.StockReferenceType;

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Product> _products;
    private readonly IReadRepository<StockMovement> _stockMovements;
    private readonly IReadRepository<StockBatch> _batches;
    private readonly IRepository<Administration> _administrations;
    private readonly IRepository<Vaccination> _vaccinations;
    private readonly IRepository<ChargeSuggestion> _chargeSuggestions;
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;

    public VoidAdministrationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Product> products,
        IReadRepository<StockMovement> stockMovements,
        IReadRepository<StockBatch> batches,
        IRepository<Administration> administrations,
        IRepository<Vaccination> vaccinations,
        IRepository<ChargeSuggestion> chargeSuggestions,
        ISender sender,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _products = products;
        _stockMovements = stockMovements;
        _batches = batches;
        _administrations = administrations;
        _vaccinations = vaccinations;
        _chargeSuggestions = chargeSuggestions;
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AdministrationDto>> Handle(VoidAdministrationCommand request, CancellationToken ct)
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
            return Fail("Tenants.TenantInactive", "Pasif kiracı için uygulama iptal edilemez.");

        var administration = await _administrations.FirstOrDefaultAsync(
            new AdministrationForUpdateSpec(tenantId, request.Id), ct);
        if (administration is null
            || (_clinicContext.ClinicId is { } contextClinicId && administration.ClinicId != contextClinicId))
        {
            return Fail("Administrations.NotFound", "Uygulama kaydı bulunamadı.");
        }

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, administration.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<AdministrationDto>.Failure(clinicAccess.Error);

        var product = await _products.FirstOrDefaultAsync(new ProductByIdSpec(tenantId, administration.ProductId), ct);
        if (product is null)
            return Fail("Products.NotFound", "Ürün bulunamadı veya kiracıya ait değil.");

        var batch = administration.BatchId is { } batchId
            ? await _batches.FirstOrDefaultAsync(new StockBatchByIdSpec(tenantId, batchId), ct)
            : null;

        var stockOutcome = AdministrationVoidStockOutcome.StockReturned;
        if (!string.IsNullOrWhiteSpace(request.StockOutcome)
            && (!Enum.TryParse(request.StockOutcome, ignoreCase: true, out stockOutcome) || !Enum.IsDefined(stockOutcome)))
        {
            return Fail("Administrations.Validation", "stockOutcome StockReturned veya WrittenOff olmalıdır.");
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var voided = administration.Void(userId, request.Reason, stockOutcome, utcNow);
        if (!voided.IsSuccess)
            return Result<AdministrationDto>.Failure(voided.Error);

        var stockStep = stockOutcome == AdministrationVoidStockOutcome.WrittenOff
            ? await SaveWrittenOffAsync(administration, ct)
            : await ReturnStockAsync(administration, tenantId, ct);
        if (!stockStep.IsSuccess)
            return Result<AdministrationDto>.Failure(stockStep.Error);

        var dropped = await DropChargeSuggestionAsync(administration, userId, utcNow, ct);
        if (!dropped.IsSuccess)
            return Result<AdministrationDto>.Failure(dropped.Error);

        await CancelLinkedVaccinationAsync(administration, utcNow, ct);
        return Result<AdministrationDto>.Success(administration.ToDto(product, batch));
    }

    private async Task<Result> ReturnStockAsync(Administration administration, Guid tenantId, CancellationToken ct)
    {
        var outMovement = await _stockMovements.FirstOrDefaultAsync(
            new StockMovementByReferenceSpec(
                tenantId,
                administration.ClinicId,
                administration.ProductId,
                StockReferenceType,
                administration.Id,
                StockMovementType.Out),
            ct);
        if (outMovement is null)
            return Result.Failure("Administrations.StockMovementNotFound", "Uygulamanın stok hareketi bulunamadı.");

        // Ters hareket yazımı (SaveChanges) izlenen iptal alanlarını da aynı transaction'da kalıcılaştırır.
        var reversal = await _sender.Send(
            new ReverseStockMovementCommand(outMovement.Id, administration.VoidReason!, Notes: null, RequestId: null),
            ct);
        if (!reversal.IsSuccess)
        {
            // Eşzamanlı iki iptal: kaybeden ters hareket benzersiz kuralına çarpar.
            return reversal.Error.Code == "StockMovements.AlreadyReversed"
                ? Result.Failure("Administrations.AlreadyVoided", "Uygulama zaten iptal edilmiş.")
                : Result.Failure(reversal.Error);
        }

        return Result.Success();
    }

    private async Task<Result> SaveWrittenOffAsync(Administration administration, CancellationToken ct)
    {
        try
        {
            await _administrations.UpdateAsync(administration, ct);
            await _administrations.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Eşzamanlı iptal kazandı (RowVersion): hiçbir şey yazılmadı.
            return Result.Failure("Administrations.AlreadyVoided", "Uygulama zaten iptal edilmiş.");
        }

        return Result.Success();
    }

    // Bekleyen "ücret bekliyor" kalemi düşer (ADR-018). Ücreti kesilmiş/ücretsiz sayılmış kalem değişmez; kesilmiş ücret yönetici düzeltmesidir.
    // Eşzamanlı ücrete çevirme kazanırsa (RowVersion) iptal geri alınır; kullanıcı yeniden dener ve kalem artık çevrilmiş olur.
    private async Task<Result> DropChargeSuggestionAsync(Administration administration, Guid userId, DateTime utcNow, CancellationToken ct)
    {
        var suggestion = await _chargeSuggestions.FirstOrDefaultAsync(
            new ChargeSuggestionBySourceForUpdateSpec(administration.TenantId, ChargeSuggestion.AdministrationSource, administration.Id),
            ct);
        if (suggestion is null || !suggestion.CancelForVoidedSource(userId, utcNow))
            return Result.Success();

        try
        {
            await _chargeSuggestions.UpdateAsync(suggestion, ct);
            await _chargeSuggestions.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                "Administrations.ConcurrencyConflict",
                "Uygulamanın ücret kalemi aynı anda değişti; tekrar deneyin.");
        }

        return Result.Success();
    }

    // Bağlı aşı kaydı uygulamanın parçasıdır: uygulama iptal edilince aşı kaydı da iptal olur (kesinleşmiş muayene kilidi uygulamaz, D3).
    private async Task CancelLinkedVaccinationAsync(Administration administration, DateTime utcNow, CancellationToken ct)
    {
        if (administration.VaccinationId is not { } vaccinationId)
            return;

        var vaccination = await _vaccinations.FirstOrDefaultAsync(
            new VaccinationByIdSpec(administration.TenantId, vaccinationId), ct);
        if (vaccination is null)
            return;

        vaccination.CancelForVoidedAdministration(utcNow);
        await _vaccinations.UpdateAsync(vaccination, ct);
        await _vaccinations.SaveChangesAsync(ct);
    }

    private static Result<AdministrationDto> Fail(string code, string message)
        => Result<AdministrationDto>.Failure(code, message);
}
