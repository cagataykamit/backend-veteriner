using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Administrations.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.ProductStocks.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.StockMovements.Commands.Create;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Treatments;
using Backend.Veteriner.Application.Treatments.Specs;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Administrations.Commands.Create;

/// <summary>
/// Uygulama kaydını yazar, ardından stok düşümünü mevcut <see cref="CreateStockMovementCommand"/> ile aynı transaction'da yapar
/// (stok kuralları tek yerde yaşar). Atomiklik, komutun <see cref="ITransactionalRollbackOnFailureRequest"/> işaretiyle
/// <c>TransactionBehavior</c> tarafından sağlanır: stok adımı başarısız olursa uygulama kaydı geri alınır.
/// </summary>
public sealed class CreateAdministrationCommandHandler
    : IRequestHandler<CreateAdministrationCommand, Result<AdministrationCreateResultDto>>
{
    private const string StockReferenceType = "Administration";

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Product> _products;
    private readonly IReadRepository<Treatment> _treatments;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<ProductStock> _stocks;
    private readonly IReadRepository<Administration> _administrationsRead;
    private readonly IRepository<Administration> _administrationsWrite;
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;

    public CreateAdministrationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Pet> pets,
        IReadRepository<Product> products,
        IReadRepository<Treatment> treatments,
        IReadRepository<Examination> examinations,
        IReadRepository<ProductStock> stocks,
        IReadRepository<Administration> administrationsRead,
        IRepository<Administration> administrationsWrite,
        ISender sender,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _pets = pets;
        _products = products;
        _treatments = treatments;
        _examinations = examinations;
        _stocks = stocks;
        _administrationsRead = administrationsRead;
        _administrationsWrite = administrationsWrite;
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async Task<Result<AdministrationCreateResultDto>> Handle(
        CreateAdministrationCommand request, CancellationToken ct)
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
            return Fail("Tenants.TenantInactive", "Pasif kiracı için uygulama kaydı oluşturulamaz.");

        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue
            && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Fail(
                "Administrations.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        if ((_clinicContext.ClinicId ?? request.ClinicId) is not { } clinicId)
            return Fail("Administrations.Validation", "ClinicId zorunludur (aktif klinik bağlamı yok).");

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, clinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<AdministrationCreateResultDto>.Failure(clinicAccess.Error);

        var product = await _products.FirstOrDefaultAsync(new ProductByIdSpec(tenantId, request.ProductId), ct);
        if (product is null)
            return Fail("Products.NotFound", "Ürün bulunamadı veya kiracıya ait değil.");

        // Tekrar istek: aynı RequestId ilk kaydı döndürür, stok değişmez.
        if (request.RequestId is { } requestId && requestId != Guid.Empty)
        {
            var existing = await _administrationsRead.FirstOrDefaultAsync(
                new AdministrationByRequestIdSpec(tenantId, requestId), ct);
            if (existing is not null)
                return await ExistingAsync(existing, request, clinicId, product, ct);
        }

        if (!product.IsActive)
            return Fail("Products.Inactive", "Pasif ürün için uygulama kaydı oluşturulamaz.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Fail("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var examinationId = request.ExaminationId;
        var addedAfterFinalization = false;
        if (request.TreatmentId is { } treatmentId)
        {
            var treatment = await _treatments.FirstOrDefaultAsync(new TreatmentByIdSpec(tenantId, treatmentId), ct);
            if (treatment is null)
                return Fail("Treatments.NotFound", "Tedavi bulunamadı veya kiracıya ait değil.");

            if (treatment.ClinicId != clinicId)
                return Fail("Administrations.TreatmentClinicMismatch", "Tedavi ile klinik bilgisi uyuşmuyor.");

            if (treatment.PetId != request.PetId)
                return Fail("Administrations.TreatmentPetMismatch", "Tedavi ile seçilen hayvan uyuşmuyor.");

            if (treatment.ExaminationId is { } treatmentExaminationId)
            {
                if (examinationId.HasValue && examinationId.Value != treatmentExaminationId)
                    return Fail("Administrations.TreatmentExaminationMismatch", "Tedavi başka bir muayeneye bağlı.");

                examinationId = treatmentExaminationId;
            }
        }

        if (examinationId is { } linkedExaminationId)
        {
            var exam = await _examinations.FirstOrDefaultAsync(
                new ExaminationByIdSpec(tenantId, linkedExaminationId), ct);
            if (exam is null)
                return Fail("Examinations.NotFound", "Muayene bulunamadı veya kiracıya ait değil.");

            if (exam.ClinicId != clinicId)
                return Fail("Administrations.ExaminationClinicMismatch", "Muayene ile klinik bilgisi uyuşmuyor.");

            if (exam.PetId != request.PetId)
                return Fail("Administrations.ExaminationPetMismatch", "Muayene ile seçilen hayvan uyuşmuyor.");

            // Kesinleşmiş muayene uygulamayı reddetmez (ADR-013): sonradan ekleme olarak gerekçeyle kaydedilir.
            addedAfterFinalization = exam.IsFinalized;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var administeredAtUtc = request.AdministeredAtUtc is { } at ? TreatmentDateUtcWindow.ToUtc(at) : nowUtc;
        var window = TreatmentDateUtcWindow.Validate(administeredAtUtc);
        if (!window.IsSuccess)
            return Result<AdministrationCreateResultDto>.Failure(window.Error);

        var created = Administration.Create(
            tenantId,
            clinicId,
            request.PetId,
            examinationId,
            request.TreatmentId,
            request.ProductId,
            request.Quantity,
            administeredAtUtc,
            request.Notes,
            addedAfterFinalization,
            request.LateEntryReason,
            userId,
            nowUtc,
            request.RequestId);
        if (!created.IsSuccess)
            return Result<AdministrationCreateResultDto>.Failure(created.Error);

        var administration = created.Value!;
        try
        {
            // Repository AddAsync kaydı hemen kalıcılaştırır (SaveChanges içerir); requestId benzersizliği stoktan önce elenir.
            await _administrationsWrite.AddAsync(administration, ct);
        }
        catch (DbUpdateException) when (request.RequestId.HasValue)
        {
            // Eşzamanlı çift istek: filtreli benzersiz indeks kaybedene çarptı; hiçbir şey yazılmadı, kazanan döner.
            var winner = await _administrationsRead.FirstOrDefaultAsync(
                new AdministrationByRequestIdSpec(tenantId, request.RequestId.Value), ct);
            if (winner is null)
                throw;

            return await ExistingAsync(winner, request, clinicId, product, ct);
        }

        var stockMovement = await _sender.Send(
            new CreateStockMovementCommand(
                clinicId,
                request.ProductId,
                StockMovementType.Out,
                request.Quantity,
                UnitCost: null,
                Reason: "Uygulama",
                ReferenceType: StockReferenceType,
                ReferenceId: administration.Id,
                OccurredAtUtc: administeredAtUtc,
                Notes: null),
            ct);
        if (!stockMovement.IsSuccess)
            return Result<AdministrationCreateResultDto>.Failure(MapStockError(stockMovement.Error));

        var stock = await _stocks.FirstOrDefaultAsync(
            new ProductStockForUpdateSpec(tenantId, clinicId, request.ProductId), ct);

        return Result<AdministrationCreateResultDto>.Success(
            new AdministrationCreateResultDto(true, administration.ToDto(product), stock!.ToDto()));
    }

    private async Task<Result<AdministrationCreateResultDto>> ExistingAsync(
        Administration existing,
        CreateAdministrationCommand request,
        Guid clinicId,
        Product product,
        CancellationToken ct)
    {
        if (existing.IsVoided)
        {
            return Fail(
                "Administrations.RequestIdVoided",
                "Bu requestId ile kaydedilen uygulama iptal edilmiş; yeni bir requestId kullanılmalı.");
        }

        if (existing.ClinicId != clinicId
            || existing.PetId != request.PetId
            || existing.ProductId != request.ProductId
            || existing.Quantity != request.Quantity)
        {
            return Fail(
                "Administrations.DuplicateRequestId",
                "Bu requestId başka bir uygulama isteği için kullanılmış.");
        }

        var stock = await _stocks.FirstOrDefaultAsync(
            new ProductStockForUpdateSpec(existing.TenantId, existing.ClinicId, existing.ProductId), ct);

        return Result<AdministrationCreateResultDto>.Success(
            new AdministrationCreateResultDto(false, existing.ToDto(product), stock!.ToDto()));
    }

    private static Error MapStockError(Error error)
        => error.Code == "StockMovements.InsufficientStock"
            ? new Error("Administrations.InsufficientStock", "Uygulama için yeterli stok yok.")
            : error;

    private static Result<AdministrationCreateResultDto> Fail(string code, string message)
        => Result<AdministrationCreateResultDto>.Failure(code, message);
}
