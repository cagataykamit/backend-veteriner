using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.Commands.OwnerSignup;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Legal;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.SalesPartners;
using Backend.Veteriner.Application.SalesPartners.Specs;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.ExampleClinic;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using AwesomeAssertions;
using Moq;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Tests.Public.Handlers;

public sealed class PublicOwnerSignupCommandHandlerTests
{
    private readonly Mock<IReadRepository<User>> _usersRead = new();
    private readonly Mock<IUserRepository> _usersWrite = new();
    private readonly Mock<IReadRepository<Tenant>> _tenantsRead = new();
    private readonly Mock<IRepository<Tenant>> _tenantsWrite = new();
    private readonly Mock<IRepository<Clinic>> _clinicsWrite = new();
    private readonly Mock<IRepository<UserTenant>> _userTenantsWrite = new();
    private readonly Mock<IRepository<UserClinic>> _userClinicsWrite = new();
    private readonly Mock<IReadRepository<OperationClaim>> _operationClaimsRead = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IRepository<TenantSubscription>> _subscriptionsWrite = new();
    private readonly Mock<IRepository<LegalAcceptance>> _legalAcceptancesWrite = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ILogger<PublicOwnerSignupCommandHandler>> _logger = new();
    private readonly Mock<IRepository<Backend.Veteriner.Domain.CashRegisters.CashRegister>> _cashRegistersWrite = new();
    private readonly Mock<IRepository<Backend.Veteriner.Domain.CashRegisters.CashMovementCategory>> _cashCategoriesWrite = new();
    private readonly Mock<IReadRepository<DiscountCode>> _codes = new();
    private readonly Mock<IReadRepository<SalesPartner>> _partners = new();
    private readonly Mock<IReadRepository<DiscountRedemption>> _redemptionsRead = new();
    private readonly Mock<IRepository<DiscountRedemption>> _redemptionsWrite = new();
    private readonly Mock<IExampleClinicLoader> _exampleClinic = new();
    private readonly Mock<IRepository<TenantBillingProfile>> _billingProfilesWrite = new();
    private readonly TrialSignupTestSupport _trial = new();

    public PublicOwnerSignupCommandHandlerTests() => _trial.AddVerified(DateTime.UtcNow);

    private PublicOwnerSignupCommandHandler CreateHandler()
        => new(
            _usersRead.Object,
            _usersWrite.Object,
            _tenantsRead.Object,
            _tenantsWrite.Object,
            Mock.Of<ITenantScope>(),
            _clinicsWrite.Object,
            _userTenantsWrite.Object,
            _userClinicsWrite.Object,
            _operationClaimsRead.Object,
            _userOperationClaims.Object,
            _subscriptionsWrite.Object,
            _legalAcceptancesWrite.Object,
            new DiscountCodeRedemptionService(_codes.Object, _partners.Object, Mock.Of<ITenantScope>(), _redemptionsRead.Object),
            _redemptionsWrite.Object,
            _hasher.Object,
            _uow.Object,
            _exampleClinic.Object,
            TimeProvider.System,
            _cashRegistersWrite.Object,
            _cashCategoriesWrite.Object,
            _trial.CreateEmailService(),
            _trial.CreateIdentityService(),
            _billingProfilesWrite.Object,
            _logger.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PlanCodeInvalid()
    {
        var handler = CreateHandler();
        var command = Signup("gold", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "Abc12345!");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.PlanCodeInvalid");
        _usersRead.Verify(r => r.AnyAsync(It.IsAny<UserExistsByEmailSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateEmail()
    {
        var handler = CreateHandler();
        var command = Signup("Basic", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "Abc12345!");

        _usersRead.Setup(r => r.AnyAsync(It.IsAny<UserExistsByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Users.DuplicateEmail");
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_LoadExampleClinic_InSameFlow_When_StartWithExampleClinic()
    {
        SetupHappyPathLookups();
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");
        ExampleClinicLoadSummaryDto? summary = null;
        _exampleClinic.Setup(x => x.LoadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, Guid clinicId, Guid _, CancellationToken _) =>
                summary = new ExampleClinicLoadSummaryDto(tenantId, clinicId, 12, 20, 40, 25, 15, 8, 6, 60, 40, 10, 30));

        var command = Signup(
            "Basic", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "Abc12345!", AllCurrentDocuments(), null, StartWithExampleClinic: true);
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExampleClinic.Should().Be(summary);
        summary!.TenantId.Should().Be(result.Value.TenantId);
        summary.ClinicId.Should().Be(result.Value.ClinicId);
        _exampleClinic.Verify(
            x => x.LoadAsync(result.Value.TenantId, result.Value.ClinicId, result.Value.UserId, It.IsAny<CancellationToken>()),
            Times.Once);
        // Önce kayıt yazılır (SaveChanges), sonra aynı işlemde örnek veri; ikisi de TransactionBehavior altında.
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_NotTouchExampleClinic_When_StartWithExampleClinicFalse()
    {
        SetupHappyPathLookups();
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");

        var command = Signup("Basic", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "Abc12345!", AllCurrentDocuments());
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExampleClinic.Should().BeNull();
        _exampleClinic.Verify(
            x => x.LoadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_CreateOwnerTenantClinicMembershipsAndSubscription_When_Successful()
    {
        var handler = CreateHandler();
        var command = Signup("Pro", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "Abc12345!", AllCurrentDocuments());
        var adminClaim = new OperationClaim("Admin");

        _usersRead.Setup(r => r.AnyAsync(It.IsAny<UserExistsByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _tenantsRead.Setup(r => r.AnyAsync(It.IsAny<TenantByNameCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _operationClaimsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<OperationClaimByNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminClaim);
        _hasher.Setup(h => h.Hash(command.Password)).Returns("hashed");

        TenantSubscription? capturedSub = null;
        _subscriptionsWrite.Setup(r => r.AddAsync(It.IsAny<TenantSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<TenantSubscription, CancellationToken>((s, _) => capturedSub = s);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var response = result.Value!;
        response.CanLogin.Should().BeTrue();
        response.NextStep.Should().Be("login");
        response.PlanCode.Should().Be("Pro");
        capturedSub.Should().NotBeNull();
        capturedSub!.PlanCode.Should().Be(SubscriptionPlanCode.Pro);
        capturedSub.Status.Should().Be(TenantSubscriptionStatus.Trialing);

        _usersWrite.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
        _tenantsWrite.Verify(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()), Times.Once);
        _clinicsWrite.Verify(r => r.AddAsync(It.IsAny<Clinic>(), It.IsAny<CancellationToken>()), Times.Once);
        _userTenantsWrite.Verify(r => r.AddAsync(It.IsAny<UserTenant>(), It.IsAny<CancellationToken>()), Times.Once);
        _userClinicsWrite.Verify(r => r.AddAsync(It.IsAny<UserClinic>(), It.IsAny<CancellationToken>()), Times.Once);
        _userOperationClaims.Verify(r => r.AddAsync(It.IsAny<UserOperationClaim>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _legalAcceptancesWrite.Verify(r => r.AddRangeAsync(
            It.Is<IEnumerable<LegalAcceptance>>(rows => rows.Count() == 3
                && rows.All(a => a.Version == LegalDocumentCatalog.CurrentVersion(a.DocumentType))
                && rows.Select(a => a.DocumentType).Distinct().Count() == 3),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnLegalRequired_And_CreateNothing_When_DataProcessingNotAccepted()
    {
        SetupHappyPathLookups();
        var handler = CreateHandler();
        var documents = AllCurrentDocuments().Where(d => d.DocumentType != LegalDocumentType.DataProcessing).ToArray();
        var command = Signup("Pro", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "Abc12345!", documents);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LegalAcceptances.Required");
        _usersWrite.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _legalAcceptancesWrite.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<LegalAcceptance>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnVersionOutdated_When_SubmittedVersionIsNotCurrent()
    {
        SetupHappyPathLookups();
        var handler = CreateHandler();
        var documents = AllCurrentDocuments()
            .Select(d => d.DocumentType == LegalDocumentType.Terms ? d with { Version = "0.0" } : d)
            .ToArray();
        var command = Signup("Pro", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "Abc12345!", documents);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LegalAcceptances.VersionOutdated");
        _usersWrite.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Write_Redemption_And_Return_Discount_When_Code_Valid()
    {
        SetupHappyPathLookups();
        SetupCode(partnerEmail: "ortak@example.com");
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");
        DiscountRedemption? written = null;
        _redemptionsWrite.Setup(r => r.AddAsync(It.IsAny<DiscountRedemption>(), It.IsAny<CancellationToken>()))
            .Callback<DiscountRedemption, CancellationToken>((r, _) => written = r)
            .ReturnsAsync((DiscountRedemption r, CancellationToken _) => r);

        var command = Signup("Pro", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "Abc12345!", AllCurrentDocuments(), " ışık10 ");
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Discount!.Code.Should().Be("ISIK10");
        result.Value.Discount.DiscountPercent.Should().Be(10m);
        result.Value.Discount.DiscountMonths.Should().Be(12);
        written!.TenantId.Should().Be(result.Value.TenantId);
        written.CommissionPercent.Should().Be(20m);
    }

    [Theory]
    [InlineData("YOKKOD", "owner@a.com")]
    [InlineData("ISIK10", "ORTAK@example.com")]
    public async Task Handle_Should_Return_Uniform_Invalid_And_Create_Nothing_When_Code_Unknown_Or_Self_Use(string rawCode, string email)
    {
        SetupHappyPathLookups();
        SetupCode(partnerEmail: "ortak@example.com");
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");
        _trial.AddVerified(DateTime.UtcNow, email.ToLowerInvariant());

        var command = Signup("Pro", "Tenant A", "Clinic A", "Istanbul", email, "Abc12345!", AllCurrentDocuments(), rawCode);
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DiscountRedemption.InvalidCode);
        _usersWrite.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _tenantsWrite.Verify(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()), Times.Never);
        _redemptionsWrite.Verify(r => r.AddAsync(It.IsAny<DiscountRedemption>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SetupCode(string? partnerEmail)
    {
        var partner = SalesPartner.Create("Ortak", partnerEmail, null, null, DateTime.UtcNow);
        var code = DiscountCode.Create(partner.Id, "ISIK10", 10m, 20m, null, null, null, DateTime.UtcNow).Value!;
        _codes.Setup(r => r.FirstOrDefaultAsync(It.IsAny<DiscountCodeByNormalizedCodeSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DiscountCodeByNormalizedCodeSpec spec, CancellationToken _) =>
                spec.Evaluate([code]).FirstOrDefault());
        _partners.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SalesPartnerByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);
    }

    private void SetupHappyPathLookups()
    {
        _usersRead.Setup(r => r.AnyAsync(It.IsAny<UserExistsByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _tenantsRead.Setup(r => r.AnyAsync(It.IsAny<TenantByNameCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _operationClaimsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<OperationClaimByNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationClaim("Admin"));
    }

    [Fact]
    public async Task Handle_Should_Register_Trial_Identity_Billing_Profile_And_Confirm_Email()
    {
        SetupHappyPathLookups();
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");
        User? owner = null;
        _usersWrite.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => owner = u)
            .ReturnsAsync((User u, CancellationToken _) => u);
        TenantBillingProfile? profile = null;
        _billingProfilesWrite.Setup(r => r.AddAsync(It.IsAny<TenantBillingProfile>(), It.IsAny<CancellationToken>()))
            .Callback<TenantBillingProfile, CancellationToken>((p, _) => profile = p)
            .ReturnsAsync((TenantBillingProfile p, CancellationToken _) => p);

        var result = await CreateHandler().Handle(
            Signup("Basic", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "Abc12345!", AllCurrentDocuments()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        owner!.EmailConfirmed.Should().BeTrue();
        var identity = _trial.Identities.Single();
        identity.TrialTenantId.Should().Be(result.Value!.TenantId);
        identity.EmailKey.Should().Be("owner@a.com");
        identity.TaxIdentityNumber.Should().Be(TrialSignupTestSupport.ValidVkn);
        profile!.TaxNumber.Should().Be(TrialSignupTestSupport.ValidVkn);
        _trial.Verifications.Single().UsedAtUtc.Should().NotBeNull();
    }

    [Theory]
    [InlineData("baska-jeton")]
    [InlineData(null)]
    public async Task Handle_Should_Return_VerificationRequired_And_Create_Nothing_When_Email_Token_Does_Not_Match(string? token)
    {
        SetupHappyPathLookups();

        var command = Signup("Basic", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "Abc12345!", AllCurrentDocuments())
            with { EmailVerificationToken = token };
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.Error.Code.Should().Be("SignupEmail.VerificationRequired");
        _usersWrite.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _trial.Verifications.Single().UsedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_Reject_Second_Trial_With_Same_Tax_Number()
    {
        SetupHappyPathLookups();
        _trial.Identities.Add(TrialSignupIdentity.Create(Guid.NewGuid(), "ilk@a.com", TrialSignupTestSupport.ValidVkn, DateTime.UtcNow));

        var result = await CreateHandler().Handle(
            Signup("Basic", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "Abc12345!", AllCurrentDocuments()),
            CancellationToken.None);

        result.Error.Code.Should().Be("SignupIdentity.DuplicateTaxIdentity");
        _tenantsWrite.Verify(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Create_No_Billing_Profile_When_Tax_Number_Optional_And_Empty()
    {
        SetupHappyPathLookups();
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");
        _trial.Options = new() { RequireTaxIdentityNumber = false };

        var result = await CreateHandler().Handle(
            Signup("Basic", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "Abc12345!", AllCurrentDocuments()) with { TaxIdentityNumber = null },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _trial.Identities.Single().TaxIdentityNumber.Should().BeNull();
        _billingProfilesWrite.Verify(r => r.AddAsync(It.IsAny<TenantBillingProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Doğrulanmış e-posta jetonu ve geçerli VKN ile kayıt komutu (TRIAL-001); diğer alanlar kayıt komutuyla aynı.</summary>
    private static PublicOwnerSignupCommand Signup(
        string planCode,
        string tenantName,
        string clinicName,
        string clinicCity,
        string email,
        string password,
        IReadOnlyList<LegalDocumentAcceptanceInput>? AcceptedLegalDocuments = null,
        string? DiscountCode = null,
        bool StartWithExampleClinic = false)
        => new(planCode, tenantName, clinicName, clinicCity, email, password, AcceptedLegalDocuments, DiscountCode, StartWithExampleClinic,
            TrialSignupTestSupport.Token, TrialSignupTestSupport.ValidVkn);

    private static LegalDocumentAcceptanceInput[] AllCurrentDocuments() =>
        LegalDocumentCatalog.CurrentVersions
            .Select(kv => new LegalDocumentAcceptanceInput(kv.Key, kv.Value))
            .ToArray();
}
