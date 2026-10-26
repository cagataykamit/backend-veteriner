using Backend.Veteriner.Application.Charges;
using Backend.Veteriner.Application.Charges.Commands.Create;
using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.ChargeSuggestions.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ChargeSuggestions.Commands.Convert;

/// <summary>
/// Kalemi ücrete çevirir: ücret mevcut <see cref="CreateChargeCommand"/> ile yazılır (ücret kuralları tek yerde), ardından kalem aynı
/// transaction'da ücrete bağlanır. Çift ücret üç katmanda engellenir: kalem durumu (yalnız Pending çevrilir), kalemin RowVersion'ı
/// (eşzamanlı iki karardan biri kazanır, kaybedenin ücreti geri alınır) ve ücretin requestId benzersizliği (tekrar istek ilk ücreti döndürür).
/// </summary>
public sealed class ConvertChargeSuggestionCommandHandler
    : IRequestHandler<ConvertChargeSuggestionCommand, Result<ChargeCreateResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IRepository<ChargeSuggestion> _suggestions;
    private readonly IReadRepository<ChargeSuggestion> _suggestionsRead;
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Product> _products;
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;

    public ConvertChargeSuggestionCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IRepository<ChargeSuggestion> suggestions,
        IReadRepository<ChargeSuggestion> suggestionsRead,
        IReadRepository<Charge> charges,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IReadRepository<Examination> examinations,
        IReadRepository<Product> products,
        ISender sender,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _suggestions = suggestions;
        _suggestionsRead = suggestionsRead;
        _charges = charges;
        _pets = pets;
        _clients = clients;
        _examinations = examinations;
        _products = products;
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ChargeCreateResultDto>> Handle(ConvertChargeSuggestionCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
            return Fail("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var suggestion = await _suggestions.FirstOrDefaultAsync(new ChargeSuggestionForUpdateSpec(tenantId, request.Id), ct);
        if (suggestion is null
            || (_clinicContext.ClinicId is { } contextClinicId && suggestion.ClinicId != contextClinicId))
        {
            return Fail("ChargeSuggestions.NotFound", "Ücret bekleyen kalem bulunamadı.");
        }

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, suggestion.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<ChargeCreateResultDto>.Failure(clinicAccess.Error);

        // Tekrar istek: bu kalemi çeviren ücret döner; requestId başka bir ücrete aitse çakışmadır.
        if (request.RequestId is { } requestId)
        {
            var existing = await _charges.FirstOrDefaultAsync(new ChargeByRequestIdSpec(tenantId, requestId), ct);
            if (existing is not null)
                return await ReplayAsync(existing, suggestion.ChargeId, ct);
        }

        var pending = suggestion.EnsurePending();
        if (!pending.IsSuccess)
            return Result<ChargeCreateResultDto>.Failure(pending.Error);

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, suggestion.PetId), ct);
        if (pet is null)
            return Fail("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var unitPrice = request.UnitPrice ?? suggestion.SuggestedUnitPrice;
        if (unitPrice is null)
            return Fail("ChargeSuggestions.Validation", "Kalemin katalog fiyatı yok; birim fiyat girilmelidir.");

        // Ücret muayenenin güncel geliş kaydına bağlanır (ücret uçlarıyla aynı bağ).
        var visitId = suggestion.VisitId;
        if (suggestion.ExaminationId is { } examinationId)
        {
            var exam = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, examinationId), ct);
            visitId = exam?.VisitId;
        }

        // Satır türü kalemin katalog ürününden türer (ADR-036); KDV oranı boş = klinik varsayılanı.
        var product = await _products.FirstOrDefaultAsync(new ProductByIdSpec(tenantId, suggestion.ProductId), ct);
        var tax = product is null
            ? null
            : new ChargeTaxDto(ChargeLineKinds.FromProductKind(product.Kind), null);

        var charged = await _sender.Send(
            new CreateChargeCommand(
                suggestion.ClinicId,
                pet.ClientId,
                pet.Id,
                visitId,
                suggestion.ExaminationId,
                request.Description ?? suggestion.Description,
                request.Quantity ?? suggestion.Quantity,
                unitPrice.Value,
                request.RequestId,
                tax),
            ct);
        if (!charged.IsSuccess)
            return charged;

        var charge = charged.Value!.Charge;
        if (!charged.Value.Created)
        {
            // Aynı requestId ile eşzamanlı istek: kazanan ücret döndü; kalemi o istek bağlamış olmalı.
            var current = await _suggestionsRead.FirstOrDefaultAsync(new ChargeSuggestionCurrentSpec(tenantId, suggestion.Id), ct);
            return current?.ChargeId == charge.Id
                ? charged
                : Fail("ChargeSuggestions.DuplicateRequestId", "Bu requestId başka bir ücret isteği için kullanılmış.");
        }

        var converted = suggestion.MarkConverted(charge.Id, userId, _timeProvider.GetUtcNow().UtcDateTime);
        if (!converted.IsSuccess)
            return Result<ChargeCreateResultDto>.Failure(converted.Error);

        try
        {
            await _suggestions.UpdateAsync(suggestion, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Eşzamanlı karar (başka çevirme, ücretsiz sayma veya uygulama iptali) kazandı; başarısız sonuç bu ücreti geri alır.
            var current = await _suggestionsRead.FirstOrDefaultAsync(new ChargeSuggestionCurrentSpec(tenantId, suggestion.Id), ct);
            var state = current?.EnsurePending() ?? Result.Failure("ChargeSuggestions.NotFound", "Ücret bekleyen kalem bulunamadı.");
            return state.IsSuccess
                ? Fail("ChargeSuggestions.ConcurrencyConflict", "Kalem aynı anda değişti; listeyi yenileyip tekrar deneyin.")
                : Result<ChargeCreateResultDto>.Failure(state.Error);
        }

        return charged;
    }

    private async Task<Result<ChargeCreateResultDto>> ReplayAsync(Charge existing, Guid? suggestionChargeId, CancellationToken ct)
    {
        if (existing.Id != suggestionChargeId)
            return Fail("ChargeSuggestions.DuplicateRequestId", "Bu requestId başka bir ücret isteği için kullanılmış.");

        if (existing.IsVoided)
            return Fail("Charges.RequestIdVoided", "Bu requestId ile kaydedilen ücret iptal edilmiş; kalem yeniden ücretlendirilmez.");

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(existing.TenantId, existing.ClientId), ct);
        var petName = existing.PetId is { } petId
            ? (await _pets.FirstOrDefaultAsync(new PetByIdSpec(existing.TenantId, petId), ct))?.Name
            : null;

        return Result<ChargeCreateResultDto>.Success(
            new ChargeCreateResultDto(false, existing.ToDto(client?.FullName ?? string.Empty, petName)));
    }

    private static Result<ChargeCreateResultDto> Fail(string code, string message)
        => Result<ChargeCreateResultDto>.Failure(code, message);
}
