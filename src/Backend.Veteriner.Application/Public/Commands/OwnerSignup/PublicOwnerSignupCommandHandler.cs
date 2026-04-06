using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using MediatR;

namespace Backend.Veteriner.Application.Public.Commands.OwnerSignup;

public sealed class PublicOwnerSignupCommandHandler
    : IRequestHandler<PublicOwnerSignupCommand, Result<PublicOwnerSignupResultDto>>
{
    private readonly IReadRepository<User> _usersRead;
    private readonly IUserRepository _usersWrite;
    private readonly IReadRepository<Tenant> _tenantsRead;
    private readonly IRepository<Tenant> _tenantsWrite;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IRepository<Clinic> _clinicsWrite;
    private readonly IRepository<UserTenant> _userTenantsWrite;
    private readonly IRepository<UserClinic> _userClinicsWrite;
    private readonly IReadRepository<OperationClaim> _operationClaimsRead;
    private readonly IUserOperationClaimRepository _userOperationClaims;
    private readonly IRepository<TenantSubscription> _subscriptionsWrite;
    private readonly IPasswordHasher _hasher;
    private readonly IUnitOfWork _uow;

    public PublicOwnerSignupCommandHandler(
        IReadRepository<User> usersRead,
        IUserRepository usersWrite,
        IReadRepository<Tenant> tenantsRead,
        IRepository<Tenant> tenantsWrite,
        IReadRepository<Clinic> clinicsRead,
        IRepository<Clinic> clinicsWrite,
        IRepository<UserTenant> userTenantsWrite,
        IRepository<UserClinic> userClinicsWrite,
        IReadRepository<OperationClaim> operationClaimsRead,
        IUserOperationClaimRepository userOperationClaims,
        IRepository<TenantSubscription> subscriptionsWrite,
        IPasswordHasher hasher,
        IUnitOfWork uow)
    {
        _usersRead = usersRead;
        _usersWrite = usersWrite;
        _tenantsRead = tenantsRead;
        _tenantsWrite = tenantsWrite;
        _clinicsRead = clinicsRead;
        _clinicsWrite = clinicsWrite;
        _userTenantsWrite = userTenantsWrite;
        _userClinicsWrite = userClinicsWrite;
        _operationClaimsRead = operationClaimsRead;
        _userOperationClaims = userOperationClaims;
        _subscriptionsWrite = subscriptionsWrite;
        _hasher = hasher;
        _uow = uow;
    }

    public async Task<Result<PublicOwnerSignupResultDto>> Handle(PublicOwnerSignupCommand request, CancellationToken ct)
    {
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

        var existingUser = await _usersRead.FirstOrDefaultAsync(new UserByEmailSpec(email), ct);
        if (existingUser is not null)
            return Result<PublicOwnerSignupResultDto>.Failure("Users.DuplicateEmail", "Bu e-posta adresi zaten kayıtlı.");

        var tenantNameKey = tenantName.ToLowerInvariant();
        var existingTenant = await _tenantsRead.FirstOrDefaultAsync(new TenantByNameCaseInsensitiveSpec(tenantNameKey), ct);
        if (existingTenant is not null)
        {
            return Result<PublicOwnerSignupResultDto>.Failure(
                "Tenants.DuplicateName",
                "Aynı ada sahip bir kiracı zaten var (büyük/küçük harf ayrımı yapılmaz).");
        }

        var adminClaim = await _operationClaimsRead.FirstOrDefaultAsync(new OperationClaimByNameSpec("admin"), ct);
        if (adminClaim is null)
        {
            return Result<PublicOwnerSignupResultDto>.Failure(
                "Auth.AdminClaimMissing",
                "Sistem rol konfigürasyonu eksik. Lütfen yöneticinizle iletişime geçin.");
        }

        var user = new User(email, _hasher.Hash(request.Password));
        var roleResult = user.AddRole("Admin");
        if (!roleResult.IsSuccess)
            return Result<PublicOwnerSignupResultDto>.Failure(roleResult.Error);

        var tenant = new Tenant(tenantName);
        var clinic = new Clinic(tenant.Id, clinicName, clinicCity);

        var clinicNameKey = clinicName.ToLowerInvariant();
        var duplicateClinic = await _clinicsRead.FirstOrDefaultAsync(
            new ClinicByTenantAndNameCaseInsensitiveSpec(tenant.Id, clinicNameKey), ct);
        if (duplicateClinic is not null)
        {
            return Result<PublicOwnerSignupResultDto>.Failure(
                "Clinics.DuplicateName",
                "Bu kiracı altında aynı isimde bir klinik zaten var.");
        }

        var utcNow = DateTime.UtcNow;
        var subscription = TenantSubscription.StartTrial(
            tenant.Id,
            planCode,
            utcNow,
            SubscriptionTrialDefaults.TrialDays);

        await _usersWrite.AddAsync(user, ct);
        await _tenantsWrite.AddAsync(tenant, ct);
        await _subscriptionsWrite.AddAsync(subscription, ct);
        await _clinicsWrite.AddAsync(clinic, ct);
        await _userTenantsWrite.AddAsync(new UserTenant(user.Id, tenant.Id), ct);
        await _userClinicsWrite.AddAsync(new UserClinic(user.Id, clinic.Id), ct);
        await _userOperationClaims.AddAsync(new UserOperationClaim(user.Id, adminClaim.Id), ct);

        await _uow.SaveChangesAsync(ct);

        var response = new PublicOwnerSignupResultDto(
            tenant.Id,
            clinic.Id,
            user.Id,
            SubscriptionPlanCatalog.ToApiCode(planCode),
            subscription.TrialStartsAtUtc ?? utcNow,
            subscription.TrialEndsAtUtc ?? utcNow,
            true,
            "login");

        return Result<PublicOwnerSignupResultDto>.Success(response);
    }
}
