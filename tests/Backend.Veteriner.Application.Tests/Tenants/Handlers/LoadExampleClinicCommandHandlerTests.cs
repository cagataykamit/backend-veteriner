using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Commands.LoadExampleClinic;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.ExampleClinic;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

/// <summary>
/// Örnek klinik yükleme kuralları (ADR-002): yetki, kiracı eşleşmesi (IDOR), yalnız deneme aboneliği, yalnız boş klinik;
/// başarıda yükleyici kiracının ilk aktif kliniği ve oturum kullanıcısıyla çağrılır.
/// </summary>
public sealed class LoadExampleClinicCommandHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<TenantSubscription>> _subscriptions = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IExampleClinicLoader> _loader = new();

    private LoadExampleClinicCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clientContext.Object,
            _permissions.Object,
            _tenants.Object,
            _subscriptions.Object,
            _clinics.Object,
            _clients.Object,
            _pets.Object,
            _loader.Object);

    private LoadExampleClinicCommand Command => new(_tenantId);

    [Fact]
    public async Task Handle_Should_ReturnPermissionDenied_When_SubscriptionsManageMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Manage)).Returns(false);

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
        VerifyLoaderNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_ReturnContextMissing_When_JwtTenantAbsent()
    {
        SetupAuthorized(jwtTenantId: null);

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        VerifyLoaderNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_ReturnAccessDenied_When_RouteTenantDiffersFromJwt()
    {
        SetupAuthorized(jwtTenantId: Guid.NewGuid());

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
        _tenants.Verify(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyLoaderNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_ReturnTenantNotFound_When_TenantMissing()
    {
        SetupAuthorized(_tenantId);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.NotFound");
        VerifyLoaderNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_ReturnTrialRequired_When_SubscriptionIsNotTrialing()
    {
        SetupAuthorized(_tenantId);
        SetupTenant();
        var active = TenantSubscription.StartTrial(_tenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);
        active.ActivatePaidPlan(SubscriptionPlanCode.Pro, DateTime.UtcNow);
        SetupSubscription(active);
        SetupClinic();

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ExampleClinicTrialRequired");
        VerifyLoaderNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_ReturnTrialRequired_When_SubscriptionMissing()
    {
        SetupAuthorized(_tenantId);
        SetupTenant();
        SetupSubscription(null);

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ExampleClinicTrialRequired");
        VerifyLoaderNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_ReturnClinicNotFound_When_TenantHasNoActiveClinic()
    {
        SetupAuthorized(_tenantId);
        SetupTenant();
        SetupSubscription(Trialing());
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ActiveClinicsByTenantTakeSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
        VerifyLoaderNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_ReturnAlreadyLoaded_When_TenantHasClients()
    {
        SetupAuthorized(_tenantId);
        SetupTenant();
        SetupSubscription(Trialing());
        SetupClinic();
        SetupEmptyClinic(hasClients: true, hasPets: false);

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ExampleClinicAlreadyLoaded");
        VerifyLoaderNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_ReturnAlreadyLoaded_When_TenantHasPetsOnly()
    {
        SetupAuthorized(_tenantId);
        SetupTenant();
        SetupSubscription(Trialing());
        SetupClinic();
        SetupEmptyClinic(hasClients: false, hasPets: true);

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ExampleClinicAlreadyLoaded");
        VerifyLoaderNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_LoadIntoFirstClinic_And_ReturnSummary_When_TrialingAndEmpty()
    {
        SetupAuthorized(_tenantId);
        SetupTenant();
        SetupSubscription(Trialing());
        var clinic = SetupClinic();
        SetupEmptyClinic(hasClients: false, hasPets: false);
        var summary = new ExampleClinicLoadSummaryDto(_tenantId, clinic.Id, 12, 20, 40, 25, 15, 8, 6, 60, 40, 10, 30);
        _loader.Setup(x => x.LoadAsync(_tenantId, clinic.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsSuccess ? null : result.Error.Message);
        result.Value.Should().Be(summary);
        _loader.Verify(x => x.LoadAsync(_tenantId, clinic.Id, _userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetupAuthorized(Guid? jwtTenantId)
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Manage)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(jwtTenantId);
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
    }

    private void SetupTenant()
    {
        var tenant = new Tenant("Örnek Kiracı");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
    }

    private TenantSubscription Trialing()
        => TenantSubscription.StartTrial(_tenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);

    private void SetupSubscription(TenantSubscription? subscription)
        => _subscriptions.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

    private Clinic SetupClinic()
    {
        var clinic = new Clinic(_tenantId, "Örnek Klinik", "İstanbul");
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ActiveClinicsByTenantTakeSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        return clinic;
    }

    private void SetupEmptyClinic(bool hasClients, bool hasPets)
    {
        _clients.Setup(x => x.AnyAsync(It.IsAny<ClientsByTenantCountSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(hasClients);
        _pets.Setup(x => x.AnyAsync(It.IsAny<PetsByTenantCountSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(hasPets);
    }

    private void VerifyLoaderNeverCalled()
        => _loader.Verify(
            x => x.LoadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
}
