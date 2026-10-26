using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Visits;
using Mediator;

namespace Backend.Veteriner.Application.Clients.Queries.GetBalance;

public sealed class GetClientBalanceQueryHandler
    : IRequestHandler<GetClientBalanceQuery, Result<ClientBalanceDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<Pet> _pets;
    private const int UnallocatedItemsTake = 50;

    private readonly IReadRepository<Visit> _visits;

    public GetClientBalanceQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Client> clients,
        IReadRepository<Charge> charges,
        IReadRepository<Payment> payments,
        IReadRepository<Pet> pets,
        IReadRepository<Visit> visits)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _clients = clients;
        _charges = charges;
        _payments = payments;
        _pets = pets;
        _visits = visits;
    }

    public async ValueTask<Result<ClientBalanceDto>> Handle(GetClientBalanceQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClientBalanceDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.Id), ct);
        if (client is null)
            return Result<ClientBalanceDto>.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, _clinicContext.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<ClientBalanceDto>.Failure(scopeResult.Error);

        var singleClinicId = scopeResult.Value!.SingleClinicId;
        var accessibleClinicIds = scopeResult.Value.AccessibleClinicIds;

        var charges = await _charges.ListAsync(
            new ChargesForClientBalanceSpec(tenantId, request.Id, singleClinicId, accessibleClinicIds), ct);
        var unallocated = await _payments.ListAsync(
            new UnallocatedPaymentsForClientSpec(tenantId, request.Id, singleClinicId, accessibleClinicIds), ct);

        var open = charges.Where(c => c.OutstandingAmount > 0).ToList();

        var petIds = open.Where(c => c.PetId.HasValue).Select(c => c.PetId!.Value).Distinct().ToArray();
        var petNames = petIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _pets.ListAsync(new PetsByTenantIdsSpec(tenantId, petIds), ct)).ToDictionary(p => p.Id, p => p.Name);

        var visitIds = open.Where(c => c.VisitId.HasValue).Select(c => c.VisitId!.Value).Distinct().ToArray();
        var visitArrivals = visitIds.Length == 0
            ? new Dictionary<Guid, DateTime>()
            : (await _visits.ListAsync(new VisitsByIdsSpec(tenantId, visitIds), ct)).ToDictionary(v => v.Id, v => v.ArrivedAtUtc);

        return Result<ClientBalanceDto>.Success(Build(client, singleClinicId, charges, open, unallocated, petNames, visitArrivals));
    }

    private static ClientBalanceDto Build(
        Client client,
        Guid? clinicId,
        IReadOnlyList<Charge> charges,
        IReadOnlyList<Charge> open,
        IReadOnlyList<Payment> unallocated,
        IReadOnlyDictionary<Guid, string> petNames,
        IReadOnlyDictionary<Guid, DateTime> visitArrivals)
    {
        var pets = open
            .GroupBy(c => c.PetId)
            .Select(petGroup => new ClientBalancePetDto(
                petGroup.Key,
                petGroup.Key is { } petId ? petNames.GetValueOrDefault(petId) : null,
                petGroup.Sum(c => c.OutstandingAmount),
                petGroup
                    .GroupBy(c => c.VisitId)
                    .Select(visitGroup => new ClientBalanceVisitDto(
                        visitGroup.Key,
                        visitGroup.Key is { } visitId && visitArrivals.TryGetValue(visitId, out var arrived) ? arrived : null,
                        visitGroup.Sum(c => c.OutstandingAmount),
                        visitGroup
                            .OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
                            .Select(c => new ClientBalanceChargeDto(
                                c.Id, c.ClinicId, c.ExaminationId, c.Description,
                                c.Amount, c.AllocatedAmount, c.OutstandingAmount, c.CreatedAtUtc))
                            .ToList()))
                    .OrderBy(v => v.VisitId is null)
                    .ThenByDescending(v => v.VisitArrivedAtUtc)
                    .ThenBy(v => v.VisitId)
                    .ToList()))
            .OrderByDescending(p => p.OutstandingAmount)
            .ThenBy(p => p.PetName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.PetId)
            .ToList();

        var totals = ClientBalanceTotals.From(charges, unallocated);

        return new ClientBalanceDto(
            client.Id,
            client.FullName,
            clinicId,
            Charge.DefaultCurrency,
            totals.ChargedAmount,
            totals.AllocatedAmount,
            totals.OutstandingAmount,
            open.Count,
            pets,
            new ClientBalanceUnallocatedPaymentsDto(
                totals.UnallocatedPaymentsAmount,
                totals.UnallocatedPaymentsCount,
                totals.UnallocatedPaymentsCount != unallocated.Count,
                unallocated
                    .Take(UnallocatedItemsTake)
                    .Select(p => new ClientBalanceUnallocatedPaymentDto(
                        p.Id, p.ClinicId, p.PaidAtUtc, p.Method, p.Amount, p.UnallocatedAmount, p.Currency, p.Notes))
                    .ToList()));
    }
}
