using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Finance;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Payments.Commands.Void;

public sealed class VoidPaymentCommandHandler : IRequestHandler<VoidPaymentCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<Payment> _payments;
    private readonly IRepository<PaymentAllocation> _allocations;
    private readonly IRepository<Charge> _charges;
    private readonly IPaymentIntegrationEventOutbox _eventOutbox;
    private readonly TimeProvider _timeProvider;

    public VoidPaymentCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IRepository<Payment> payments,
        IRepository<PaymentAllocation> allocations,
        IRepository<Charge> charges,
        IPaymentIntegrationEventOutbox eventOutbox,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _payments = payments;
        _allocations = allocations;
        _charges = charges;
        _eventOutbox = eventOutbox;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(VoidPaymentCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result.Failure(actor.Error);

        var (tenantId, userId) = actor.Value;

        var payment = await _payments.FirstOrDefaultAsync(new PaymentForUpdateSpec(tenantId, request.Id), ct);
        if (payment is null
            || (_clinicContext.ClinicId is { } contextClinicId && payment.ClinicId != contextClinicId))
        {
            return Result.Failure("Payments.NotFound", "Ödeme kaydı bulunamadı.");
        }

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, payment.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result.Failure(clinicAccess.Error);

        var allocations = await _allocations.ListAsync(new ActivePaymentAllocationsForUpdateSpec(tenantId, payment.Id), ct);
        var chargeIds = allocations.Select(a => a.ChargeId).Distinct().ToList();
        var charges = chargeIds.Count == 0
            ? []
            : await _charges.ListAsync(new ChargesByIdsForUpdateSpec(tenantId, chargeIds), ct);
        var chargesById = charges.ToDictionary(c => c.Id);

        if (allocations.Any(a => !chargesById.ContainsKey(a.ChargeId)))
        {
            return Result.Failure("Payments.Validation", "Ödemenin tahsis edildiği ücret bulunamadı; iptal yapılamadı.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var voided = payment.Void(
            allocations.Select(a => (a, chargesById[a.ChargeId])).ToList(),
            userId,
            request.Reason,
            nowUtc);
        if (!voided.IsSuccess)
            return voided;


        try
        {
            // Outbox emission, ödeme/tahsis/ücret güncellemeleriyle aynı SaveChanges'te kalıcı olur (tek işlem).
            await _eventOutbox.EnqueueAsync(
                PaymentIntegrationEventTypes.Voided,
                new PaymentVoidedIntegrationEvent(
                    Guid.NewGuid(),
                    nowUtc,
                    PaymentProjectionSnapshotFactory.Create(payment)),
                ct);

            await _payments.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                "Payments.ConcurrencyConflict",
                "Ödeme veya ilgili ücret eşzamanlı olarak güncellendi; yeniden okunup işlem tekrarlanmalı.");
        }

        return Result.Success();
    }
}
