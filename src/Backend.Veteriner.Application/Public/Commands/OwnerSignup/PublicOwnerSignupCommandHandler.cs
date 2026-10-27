using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Application.SalesPartners;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.ExampleClinic;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Legal;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Public.Commands.OwnerSignup;

public sealed class PublicOwnerSignupCommandHandler
    : IRequestHandler<PublicOwnerSignupCommand, Result<PublicOwnerSignupResultDto>>
{
    private readonly IReadRepository<User> _usersRead;
    private readonly IUserRepository _usersWrite;
    private readonly IReadRepository<Tenant> _tenantsRead;
    private readonly IRepository<Tenant> _tenantsWrite;
    private readonly ITenantScope _tenantScope;
    private readonly IRepository<Clinic> _clinicsWrite;
    private readonly IRepository<UserTenant> _userTenantsWrite;
    private readonly IRepository<UserClinic> _userClinicsWrite;
    private readonly IReadRepository<OperationClaim> _operationClaimsRead;
    private readonly IUserOperationClaimRepository _userOperationClaims;
    private readonly IRepository<TenantSubscription> _subscriptionsWrite;
    private readonly IRepository<LegalAcceptance> _legalAcceptancesWrite;
    private readonly DiscountCodeRedemptionService _discountCodes;
    private readonly IRepository<DiscountRedemption> _redemptionsWrite;
    private readonly IPasswordHasher _hasher;
    private readonly IUnitOfWork _uow;
    private readonly IExampleClinicLoader _exampleClinic;
    private readonly ILogger<PublicOwnerSignupCommandHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public PublicOwnerSignupCommandHandler(
        IReadRepository<User> usersRead,
        IUserRepository usersWrite,
        IReadRepository<Tenant> tenantsRead,
        IRepository<Tenant> tenantsWrite,
        ITenantScope tenantScope,
        IRepository<Clinic> clinicsWrite,
        IRepository<UserTenant> userTenantsWrite,
        IRepository<UserClinic> userClinicsWrite,
        IReadRepository<OperationClaim> operationClaimsRead,
        IUserOperationClaimRepository userOperationClaims,
        IRepository<TenantSubscription> subscriptionsWrite,
        IRepository<LegalAcceptance> legalAcceptancesWrite,
        DiscountCodeRedemptionService discountCodes,
        IRepository<DiscountRedemption> redemptionsWrite,
        IPasswordHasher hasher,
        IUnitOfWork uow,
        IExampleClinicLoader exampleClinic,
        TimeProvider timeProvider,
        ILogger<PublicOwnerSignupCommandHandler>? logger = null)
    {
        _usersRead = usersRead;
        _usersWrite = usersWrite;
        _tenantsRead = tenantsRead;
        _tenantsWrite = tenantsWrite;
        _tenantScope = tenantScope;
        _clinicsWrite = clinicsWrite;
        _userTenantsWrite = userTenantsWrite;
        _userClinicsWrite = userClinicsWrite;
        _operationClaimsRead = operationClaimsRead;
        _userOperationClaims = userOperationClaims;
        _subscriptionsWrite = subscriptionsWrite;
        _legalAcceptancesWrite = legalAcceptancesWrite;
        _discountCodes = discountCodes;
        _redemptionsWrite = redemptionsWrite;
        _hasher = hasher;
        _uow = uow;
        _exampleClinic = exampleClinic;
        _logger = logger ?? NullLogger<PublicOwnerSignupCommandHandler>.Instance;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PublicOwnerSignupResultDto>> Handle(PublicOwnerSignupCommand request, CancellationToken ct)
    {
        var totalSw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();
        var querySteps = 0;
        var slowestStep = string.Empty;
        long slowestMs = 0;

        void MarkStep(string name)
        {
            querySteps++;
            var elapsed = stepSw.ElapsedMilliseconds;
            if (elapsed > slowestMs)
            {
                slowestMs = elapsed;
                slowestStep = name;
            }

            stepSw.Restart();
        }

        var email = request.Email.Trim();
        var tenantName = request.TenantName.Trim();
        var clinicName = request.ClinicName.Trim();
        var clinicCity = request.ClinicCity.Trim();

        if (!SubscriptionPlanCatalog.TryParseApiCode(request.PlanCode, out var planCode))
        {
            return Result<PublicOwnerSignupResultDto>.Failure(
                "Subscriptions.PlanCodeInvalid",
                "Geçersiz planCode. Desteklenen planlar: Basic, Pro, Premium.");
        }

        var userExists = await _usersRead.AnyAsync(new UserExistsByEmailSpec(email), ct);
        MarkStep("userExistsByEmail");
        if (userExists)
            return Result<PublicOwnerSignupResultDto>.Failure("Users.DuplicateEmail", "Bu e-posta adresi zaten kayıtlı.");

        var tenantNameKey = tenantName.ToLowerInvariant();
        var tenantExists = await _tenantsRead.AnyAsync(new TenantByNameCaseInsensitiveSpec(tenantNameKey), ct);
        MarkStep("tenantExistsByName");
        if (tenantExists)
        {
            return Result<PublicOwnerSignupResultDto>.Failure(
                "Tenants.DuplicateName",
                "Aynı ada sahip bir kiracı zaten var (büyük/küçük harf ayrımı yapılmaz).");
        }

        var adminClaim = await _operationClaimsRead.FirstOrDefaultAsync(new OperationClaimByNameSpec("admin"), ct);
        MarkStep("adminClaimLookup");
        if (adminClaim is null)
        {
            return Result<PublicOwnerSignupResultDto>.Failure(
                "Auth.AdminClaimMissing",
                "Sistem rol konfigürasyonu eksik. Lütfen yöneticinizle iletişime geçin.");
        }

        // Sunucu da doğrular (ADR-029 madde 4): kayıt olan klinik yöneticisi gereken belgelerin güncel sürümünü kabul etmiş olmalı.
        var requiredLegalDocuments = LegalDocumentCatalog.RequiredFor([adminClaim.Name]);
        var legalCheck = LegalDocumentCatalog.EnsureAcceptsCurrent(requiredLegalDocuments, request.AcceptedLegalDocuments);
        if (!legalCheck.IsSuccess)
            return Result<PublicOwnerSignupResultDto>.Failure(legalCheck.Error);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var user = new User(email, _hasher.Hash(request.Password), utcNow);
        MarkStep("hashAndCreateUser");
        var roleResult = user.AddRole("Admin", utcNow);
        if (!roleResult.IsSuccess)
            return Result<PublicOwnerSignupResultDto>.Failure(roleResult.Error);

        var tenant = new Tenant(tenantName, utcNow);

        // Kayıt anonimdir; yeni kiracının kayıtları onun kapsamında yazılır (ADR-040).
        using var tenantScope = _tenantScope.UseTenant(tenant.Id);
        var clinic = new Clinic(tenant.Id, clinicName, clinicCity);

        var subscription = TenantSubscription.StartTrial(
            tenant.Id,
            planCode,
            utcNow,
            SubscriptionTrialDefaults.TrialDays);

        // Satış ortağı kodu (ADR-037): sunucuda yeniden doğrulanır; geçersizse (kendi kodu dahil, tek tip) kayıt yapılmaz.
        DiscountRedemption? redemption = null;
        if (!string.IsNullOrWhiteSpace(request.DiscountCode))
        {
            var redeemed = await _discountCodes.RedeemAsync(tenant.Id, email, request.DiscountCode, utcNow, ct);
            MarkStep("discountCode");
            if (!redeemed.IsSuccess)
                return Result<PublicOwnerSignupResultDto>.Failure(redeemed.Error);
            redemption = redeemed.Value!;
        }

        await _usersWrite.AddAsync(user, ct);
        await _tenantsWrite.AddAsync(tenant, ct);
        await _subscriptionsWrite.AddAsync(subscription, ct);
        await _clinicsWrite.AddAsync(clinic, ct);
        await _userTenantsWrite.AddAsync(new UserTenant(user.Id, tenant.Id, utcNow), ct);
        await _userClinicsWrite.AddAsync(new UserClinic(user.Id, clinic.Id, utcNow), ct);
        await _userOperationClaims.AddAsync(new UserOperationClaim(user.Id, adminClaim.Id), ct);
        await _legalAcceptancesWrite.AddRangeAsync(
            LegalDocumentCatalog.CreateAcceptances(user.Id, tenant.Id, requiredLegalDocuments, utcNow), ct);
        if (redemption is not null)
            await _redemptionsWrite.AddAsync(redemption, ct);
        MarkStep("stageEntities");

        await _uow.SaveChangesAsync(ct);
        MarkStep("saveChanges");

        // Örnek klinik (ADR-002): aynı işlemde; yeni kiracı deneme sürecinde ve boş olduğu için ayrıca kural kontrolü gerekmez.
        ExampleClinicLoadSummaryDto? exampleClinic = null;
        if (request.StartWithExampleClinic)
        {
            exampleClinic = await _exampleClinic.LoadAsync(tenant.Id, clinic.Id, user.Id, ct);
            MarkStep("exampleClinic");
        }

        var response = new PublicOwnerSignupResultDto(
            tenant.Id,
            clinic.Id,
            user.Id,
            SubscriptionPlanCatalog.ToApiCode(planCode),
            subscription.TrialStartsAtUtc ?? utcNow,
            subscription.TrialEndsAtUtc ?? utcNow,
            true,
            "login",
            redemption is null ? null : new DiscountCodeSummaryDto(redemption.Code, redemption.DiscountPercent, DiscountRedemption.WindowMonths),
            exampleClinic);

        _logger.LogInformation(
            "Public owner signup succeeded. TenantId={TenantId} UserId={UserId} QuerySteps={QuerySteps} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} TotalElapsedMs={TotalElapsedMs}",
            tenant.Id,
            user.Id,
            querySteps,
            slowestStep,
            slowestMs,
            totalSw.ElapsedMilliseconds);

        return Result<PublicOwnerSignupResultDto>.Success(response);
    }
}
