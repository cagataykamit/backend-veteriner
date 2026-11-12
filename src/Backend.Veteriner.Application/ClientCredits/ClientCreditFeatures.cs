using System.Text.Json.Serialization;
using Backend.Veteriner.Application.CashRegisters;
using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Finance;
using Backend.Veteriner.Application.Payments.Access;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.CashRegisters;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ClientCredits;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ClientCredits;

public sealed record ClientCreditApplicationItemDto(
    Guid Id,
    Guid ChargeId,
    string ChargeDescription,
    decimal Amount,
    DateTime CreatedAtUtc,
    DateTime? ReversedAtUtc,
    string? ReverseReason);

/// <summary>
/// Müşteri kredisi (docs/CLIENT_CREDIT_API_CONTRACT.md §7). Devir alacağında (§13) <see cref="SourcePaymentId"/> boş,
/// <see cref="IsImported"/> doludur.
/// </summary>
public sealed record ClientCreditDto(
    Guid Id,
    Guid ClinicId,
    Guid ClientId,
    Guid? SourcePaymentId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ClientCreditSource Source,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ClientCreditStatus Status,
    decimal Amount,
    decimal UsedAmount,
    decimal RemainingAmount,
    string Currency,
    DateTime CreatedAtUtc,
    Guid CreatedByUserId,
    DateTime? RefundedAtUtc,
    decimal? RefundedAmount,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PaymentMethod? RefundMethod,
    string? RefundReason,
    DateTime? VoidedAtUtc,
    IReadOnlyList<ClientCreditApplicationItemDto> Applications,
    bool IsImported);

public sealed record ClientCreditApplyLineDto(Guid ChargeId, decimal Amount);

public sealed record ClientCreditApplyResultDto(
    Guid ClientId,
    Guid ClinicId,
    decimal AppliedAmount,
    decimal RemainingCreditBalance,
    IReadOnlyList<ClientCreditAppliedItemDto> Applications);

public sealed record ClientCreditAppliedItemDto(Guid Id, Guid CreditId, Guid ChargeId, decimal Amount, DateTime CreatedAtUtc);

/// <summary>Krediden ücretlere mahsup (FIN-005 K3/K4); <c>Payments.Create</c>.</summary>
public sealed record ApplyClientCreditCommand(Guid ClientId, Guid? ClinicId, Guid RequestId, IReadOnlyList<ClientCreditApplyLineDto> Allocations)
    : IRequest<Result<ClientCreditApplyResultDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "ClientCredit.Apply";
    public string? AuditTarget => $"ClientId={ClientId};ClinicId={ClinicId}";
}

/// <summary>Mahsubu geri alır (FIN-005 K5); <c>Payments.Correct</c>.</summary>
public sealed record ReverseClientCreditApplicationCommand(Guid ClientId, Guid ApplicationId, string? Reason)
    : IRequest<Result>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "ClientCredit.ReverseApplication";
    public string? AuditTarget => $"ApplicationId={ApplicationId}";
}

/// <summary>Kalan kredinin tamamını iade eder (FIN-005 K6); <c>Payments.Correct</c>. Nakit iadede kasa seçilebilir (TIER-008): verilmezse
/// Ana kasa; kart ve havalede kasa yok.</summary>
public sealed record RefundClientCreditCommand(Guid ClientId, Guid CreditId, string? Reason, PaymentMethod Method, Guid? CashRegisterId = null)
    : IRequest<Result<ClientCreditDto>>, IAuditableRequest
{
    public string AuditAction => "ClientCredit.Refund";
    public string? AuditTarget => $"CreditId={CreditId}";
}

/// <summary>Müşterinin kredileri; varsayılan yalnız açık olanlar.</summary>
public sealed record GetClientCreditsQuery(Guid ClientId, Guid? ClinicId, bool IncludeClosed) : IRequest<Result<IReadOnlyList<ClientCreditDto>>>;

public sealed class ApplyClientCreditCommandValidator : AbstractValidator<ApplyClientCreditCommand>
{
    public ApplyClientCreditCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Allocations).NotNull();
        RuleForEach(x => x.Allocations).ChildRules(l =>
        {
            l.RuleFor(x => x.ChargeId).NotEmpty();
            l.RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(Payment.MaxAmount);
        });
    }
}

public sealed class ReverseClientCreditApplicationCommandValidator : AbstractValidator<ReverseClientCreditApplicationCommand>
{
    public ReverseClientCreditApplicationCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.ApplicationId).NotEmpty();
    }
}

public sealed class RefundClientCreditCommandValidator : AbstractValidator<RefundClientCreditCommand>
{
    public RefundClientCreditCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.CreditId).NotEmpty();
        RuleFor(x => x.Method).IsInEnum();
    }
}

internal static class ClientCreditMapping
{
    public static ClientCreditDto ToDto(
        ClientCredit c,
        IEnumerable<ClientCreditApplication> applications,
        IReadOnlyDictionary<Guid, string> chargeDescriptions)
        => new(
            c.Id, c.ClinicId, c.ClientId, c.SourcePaymentId, c.Source, c.Status, c.Amount, c.UsedAmount, c.RemainingAmount, c.Currency,
            c.CreatedAtUtc, c.CreatedByUserId, c.RefundedAtUtc, c.RefundedAmount, c.RefundMethod, c.RefundReason, c.VoidedAtUtc,
            applications
                .Where(a => a.CreditId == c.Id)
                .Select(a => new ClientCreditApplicationItemDto(
                    a.Id, a.ChargeId, chargeDescriptions.GetValueOrDefault(a.ChargeId, string.Empty), a.Amount,
                    a.CreatedAtUtc, a.ReversedAtUtc, a.ReverseReason))
                .ToList(),
            c.IsImported);

    public static Result Conflict()
        => Result.Failure("ClientCredits.ConcurrencyConflict", "Kredi ya da ücret eşzamanlı olarak güncellendi; yeniden okunup işlem tekrarlanmalı.");
}

public sealed class ApplyClientCreditCommandHandler : IRequestHandler<ApplyClientCreditCommand, Result<ClientCreditApplyResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Client> _clients;
    private readonly IRepository<ClientCredit> _credits;
    private readonly IRepository<ClientCreditApplication> _applications;
    private readonly IRepository<Charge> _charges;
    private readonly TimeProvider _timeProvider;

    public ApplyClientCreditCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Client> clients,
        IRepository<ClientCredit> credits,
        IRepository<ClientCreditApplication> applications,
        IRepository<Charge> charges,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _clients = clients;
        _credits = credits;
        _applications = applications;
        _charges = charges;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClientCreditApplyResultDto>> Handle(ApplyClientCreditCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result<ClientCreditApplyResultDto>.Failure(actor.Error);
        var (tenantId, userId) = actor.Value;

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
            return Result<ClientCreditApplyResultDto>.Failure(ClinicContextRule.Mismatch("Payments"));

        if ((_clinicContext.ClinicId ?? request.ClinicId) is not { } clinicId)
            return Result<ClientCreditApplyResultDto>.Failure("ClientCredits.Validation", "Klinik bağlamı ya da clinicId zorunludur.");

        var access = await PaymentClinicWriteScope.EnsureWriteAccessAsync(_clinicScopeResolver, tenantId, clinicId, ct);
        if (!access.IsSuccess)
            return Result<ClientCreditApplyResultDto>.Failure(access.Error);

        if (await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.ClientId), ct) is null)
            return Result<ClientCreditApplyResultDto>.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        // Tekrar isteği: aynı anahtarla yazılmış mahsuplar döner, ikinci kez yazılmaz.
        var replay = await _applications.ListAsync(new ClientCreditApplicationsByRequestIdSpec(tenantId, request.RequestId), ct);
        if (replay.Count > 0)
            return Result<ClientCreditApplyResultDto>.Success(await BuildResultAsync(tenantId, request.ClientId, clinicId, replay, ct));

        if (request.Allocations is not { Count: > 0 })
            return Result<ClientCreditApplyResultDto>.Failure("ClientCredits.AllocationsRequired", "En az bir ücret seçilmelidir.");

        var chargeIds = request.Allocations.Select(a => a.ChargeId).Distinct().ToArray();
        var charges = (await _charges.ListAsync(new ChargesByIdsForUpdateSpec(tenantId, chargeIds), ct)).ToDictionary(c => c.Id);
        if (charges.Count != chargeIds.Length)
            return Result<ClientCreditApplyResultDto>.Failure("Charges.NotFound", "Ücret kaydı bulunamadı veya kiracıya ait değil.");

        var credits = await _credits.ListAsync(new OpenClientCreditsForUpdateSpec(tenantId, request.ClientId, clinicId), ct);
        var applied = ClientCreditLedger.Apply(
            credits,
            request.Allocations.Select(a => (charges[a.ChargeId], a.Amount)).ToList(),
            userId,
            request.RequestId,
            _timeProvider.GetUtcNow().UtcDateTime);
        if (!applied.IsSuccess)
            return Result<ClientCreditApplyResultDto>.Failure(applied.Error);

        try
        {
            await _applications.AddRangeAsync(applied.Value!, ct);
            await _applications.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ClientCreditApplyResultDto>.Failure(ClientCreditMapping.Conflict().Error);
        }

        return Result<ClientCreditApplyResultDto>.Success(new ClientCreditApplyResultDto(
            request.ClientId,
            clinicId,
            applied.Value!.Sum(a => a.Amount),
            credits.Sum(c => c.RemainingAmount),
            applied.Value!.Select(a => new ClientCreditAppliedItemDto(a.Id, a.CreditId, a.ChargeId, a.Amount, a.CreatedAtUtc)).ToList()));
    }

    private async Task<ClientCreditApplyResultDto> BuildResultAsync(
        Guid tenantId, Guid clientId, Guid clinicId, IReadOnlyList<ClientCreditApplication> rows, CancellationToken ct)
    {
        var open = await _credits.ListAsync(new OpenClientCreditsForUpdateSpec(tenantId, clientId, clinicId), ct);
        return new ClientCreditApplyResultDto(
            clientId,
            clinicId,
            rows.Where(a => !a.IsReversed).Sum(a => a.Amount),
            open.Sum(c => c.RemainingAmount),
            rows.Select(a => new ClientCreditAppliedItemDto(a.Id, a.CreditId, a.ChargeId, a.Amount, a.CreatedAtUtc)).ToList());
    }
}

public sealed class ReverseClientCreditApplicationCommandHandler : IRequestHandler<ReverseClientCreditApplicationCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<ClientCredit> _credits;
    private readonly IRepository<ClientCreditApplication> _applications;
    private readonly IRepository<Charge> _charges;
    private readonly TimeProvider _timeProvider;

    public ReverseClientCreditApplicationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IRepository<ClientCredit> credits,
        IRepository<ClientCreditApplication> applications,
        IRepository<Charge> charges,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _credits = credits;
        _applications = applications;
        _charges = charges;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(ReverseClientCreditApplicationCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result.Failure(actor.Error);
        var (tenantId, userId) = actor.Value;

        var application = await _applications.FirstOrDefaultAsync(new ClientCreditApplicationByIdForUpdateSpec(tenantId, request.ApplicationId), ct);
        var credit = application is null
            ? null
            : await _credits.FirstOrDefaultAsync(new ClientCreditByIdForUpdateSpec(tenantId, request.ClientId, application.CreditId), ct);
        if (application is null || credit is null
            || (_clinicContext.ClinicId is { } contextClinicId && credit.ClinicId != contextClinicId))
        {
            return Result.Failure("ClientCredits.ApplicationNotFound", "Mahsup kaydı bulunamadı.");
        }

        var access = await PaymentClinicWriteScope.EnsureWriteAccessAsync(_clinicScopeResolver, tenantId, credit.ClinicId, ct);
        if (!access.IsSuccess)
            return access;

        var charge = (await _charges.ListAsync(new ChargesByIdsForUpdateSpec(tenantId, [application.ChargeId]), ct)).FirstOrDefault();
        if (charge is null)
            return Result.Failure("Charges.NotFound", "Mahsubun ücreti bulunamadı.");

        var reversed = credit.ReverseApplication(application, charge, userId, request.Reason, _timeProvider.GetUtcNow().UtcDateTime);
        if (!reversed.IsSuccess)
            return reversed;

        try
        {
            await _credits.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ClientCreditMapping.Conflict();
        }

        return Result.Success();
    }
}

public sealed class RefundClientCreditCommandHandler : IRequestHandler<RefundClientCreditCommand, Result<ClientCreditDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<ClientCredit> _credits;
    private readonly IReadRepository<ClientCreditApplication> _applications;
    private readonly IReadRepository<Charge> _charges;
    private readonly TimeProvider _timeProvider;
    private readonly IReadRepository<CashRegister> _cashRegisters;
    private readonly TenantPlanFeatureEvaluator _planFeatures;

    public RefundClientCreditCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IRepository<ClientCredit> credits,
        IReadRepository<ClientCreditApplication> applications,
        IReadRepository<Charge> charges,
        TimeProvider timeProvider,
        IReadRepository<CashRegister> cashRegisters,
        TenantPlanFeatureEvaluator planFeatures)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _credits = credits;
        _applications = applications;
        _charges = charges;
        _timeProvider = timeProvider;
        _cashRegisters = cashRegisters;
        _planFeatures = planFeatures;
    }

    public async ValueTask<Result<ClientCreditDto>> Handle(RefundClientCreditCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result<ClientCreditDto>.Failure(actor.Error);
        var (tenantId, userId) = actor.Value;

        var credit = await _credits.FirstOrDefaultAsync(new ClientCreditByIdForUpdateSpec(tenantId, request.ClientId, request.CreditId), ct);
        if (credit is null || (_clinicContext.ClinicId is { } contextClinicId && credit.ClinicId != contextClinicId))
            return Result<ClientCreditDto>.Failure("ClientCredits.NotFound", "Kredi kaydı bulunamadı.");

        var access = await PaymentClinicWriteScope.EnsureWriteAccessAsync(_clinicScopeResolver, tenantId, credit.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClientCreditDto>.Failure(access.Error);

        Guid? cashRegisterId = null;
        if (request.Method == PaymentMethod.Cash)
        {
            var register = await CashRegisterRules.ResolveForCashAsync(
                _cashRegisters, _planFeatures, tenantId, credit.ClinicId, request.CashRegisterId, ct);
            if (!register.IsSuccess)
                return Result<ClientCreditDto>.Failure(register.Error);
            cashRegisterId = register.Value;
        }
        else
        {
            cashRegisterId = request.CashRegisterId;
        }

        var refunded = credit.Refund(userId, request.Reason, request.Method, _timeProvider.GetUtcNow().UtcDateTime, cashRegisterId);
        if (!refunded.IsSuccess)
            return Result<ClientCreditDto>.Failure(refunded.Error);

        try
        {
            await _credits.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ClientCreditDto>.Failure(ClientCreditMapping.Conflict().Error);
        }

        var applications = await _applications.ListAsync(new ClientCreditApplicationsByCreditIdsSpec(tenantId, [credit.Id]), ct);
        var chargeIds = applications.Select(a => a.ChargeId).Distinct().ToArray();
        var descriptions = chargeIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _charges.ListAsync(new ChargesByIdsSpec(tenantId, chargeIds), ct)).ToDictionary(c => c.Id, c => c.Description);
        return Result<ClientCreditDto>.Success(ClientCreditMapping.ToDto(credit, applications, descriptions));
    }
}

public sealed class GetClientCreditsQueryHandler : IRequestHandler<GetClientCreditsQuery, Result<IReadOnlyList<ClientCreditDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<ClientCredit> _credits;
    private readonly IReadRepository<ClientCreditApplication> _applications;
    private readonly IReadRepository<Charge> _charges;

    public GetClientCreditsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Client> clients,
        IReadRepository<ClientCredit> credits,
        IReadRepository<ClientCreditApplication> applications,
        IReadRepository<Charge> charges)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _clients = clients;
        _credits = credits;
        _applications = applications;
        _charges = charges;
    }

    public async ValueTask<Result<IReadOnlyList<ClientCreditDto>>> Handle(GetClientCreditsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<IReadOnlyList<ClientCreditDto>>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        if (await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.ClientId), ct) is null)
            return Result<IReadOnlyList<ClientCreditDto>>.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
            return Result<IReadOnlyList<ClientCreditDto>>.Failure(ClinicContextRule.Mismatch("Payments"));

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, _clinicContext.ClinicId ?? request.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<IReadOnlyList<ClientCreditDto>>.Failure(scope.Error);

        var credits = await _credits.ListAsync(
            new ClientCreditsForClientSpec(tenantId, request.ClientId, scope.Value!.SingleClinicId, scope.Value.AccessibleClinicIds, !request.IncludeClosed),
            ct);
        if (credits.Count == 0)
            return Result<IReadOnlyList<ClientCreditDto>>.Success([]);

        var applications = await _applications.ListAsync(
            new ClientCreditApplicationsByCreditIdsSpec(tenantId, credits.Select(c => c.Id).ToArray()), ct);
        var chargeIds = applications.Select(a => a.ChargeId).Distinct().ToArray();
        var descriptions = chargeIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _charges.ListAsync(new ChargesByIdsSpec(tenantId, chargeIds), ct)).ToDictionary(c => c.Id, c => c.Description);

        return Result<IReadOnlyList<ClientCreditDto>>.Success(
            credits.Select(c => ClientCreditMapping.ToDto(c, applications, descriptions)).ToList());
    }
}
