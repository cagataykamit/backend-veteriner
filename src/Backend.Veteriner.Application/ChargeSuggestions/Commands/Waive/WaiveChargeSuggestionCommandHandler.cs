using Backend.Veteriner.Application.ChargeSuggestions.Contracts.Dtos;
using Backend.Veteriner.Application.ChargeSuggestions.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ChargeSuggestions.Commands.Waive;

public sealed class WaiveChargeSuggestionCommandHandler
    : IRequestHandler<WaiveChargeSuggestionCommand, Result<ChargeSuggestionDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IRepository<ChargeSuggestion> _suggestions;
    private readonly IReadRepository<ChargeSuggestion> _suggestionsRead;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly TimeProvider _timeProvider;

    public WaiveChargeSuggestionCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IRepository<ChargeSuggestion> suggestions,
        IReadRepository<ChargeSuggestion> suggestionsRead,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _suggestions = suggestions;
        _suggestionsRead = suggestionsRead;
        _pets = pets;
        _clients = clients;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ChargeSuggestionDto>> Handle(WaiveChargeSuggestionCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ChargeSuggestionDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
            return Result<ChargeSuggestionDto>.Failure("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var suggestion = await _suggestions.FirstOrDefaultAsync(new ChargeSuggestionForUpdateSpec(tenantId, request.Id), ct);
        if (suggestion is null
            || (_clinicContext.ClinicId is { } contextClinicId && suggestion.ClinicId != contextClinicId))
        {
            return Result<ChargeSuggestionDto>.Failure("ChargeSuggestions.NotFound", "Ücret bekleyen kalem bulunamadı.");
        }

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, suggestion.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<ChargeSuggestionDto>.Failure(clinicAccess.Error);

        var waived = suggestion.Waive(userId, request.Reason, _timeProvider.GetUtcNow().UtcDateTime);
        if (!waived.IsSuccess)
            return Result<ChargeSuggestionDto>.Failure(waived.Error);

        try
        {
            await _suggestions.UpdateAsync(suggestion, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Eşzamanlı karar kazandı (RowVersion): hiçbir şey yazılmadı, güncel durum bildirilir.
            var current = await _suggestionsRead.FirstOrDefaultAsync(new ChargeSuggestionCurrentSpec(tenantId, suggestion.Id), ct);
            var state = current?.EnsurePending() ?? Result.Failure("ChargeSuggestions.NotFound", "Ücret bekleyen kalem bulunamadı.");
            return state.IsSuccess
                ? Result<ChargeSuggestionDto>.Failure("ChargeSuggestions.ConcurrencyConflict", "Kalem aynı anda değişti; listeyi yenileyip tekrar deneyin.")
                : Result<ChargeSuggestionDto>.Failure(state.Error);
        }

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, suggestion.PetId), ct);
        var client = pet is null ? null : await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, pet.ClientId), ct);

        return Result<ChargeSuggestionDto>.Success(
            suggestion.ToDto(pet?.ClientId ?? Guid.Empty, client?.FullName ?? string.Empty, pet?.Name ?? string.Empty));
    }
}
