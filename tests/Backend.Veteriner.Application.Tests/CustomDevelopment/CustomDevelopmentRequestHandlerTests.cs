using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.CustomDevelopment;
using Backend.Veteriner.Application.Tests.Tenants;
using Backend.Veteriner.Domain.CustomDevelopment;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.CustomDevelopment;

/// <summary>TIER-010: yeni talep Kurumsal ister, açık talep sınırı dolunca açık hata kodu döner, talep açan kullanıcıya bağlanır.</summary>
public sealed class CustomDevelopmentRequestHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClientContext> _client = new();
    private readonly Mock<IRepository<CustomDevelopmentRequest>> _requests = new();
    private readonly Mock<IReadRepository<User>> _users = new();

    public CustomDevelopmentRequestHandlerTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(TenantId);
        _client.SetupGet(c => c.UserId).Returns(UserId);
        _users.Setup(r => r.ListAsync(It.IsAny<ISpecification<User, CustomDevelopmentUser>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CustomDevelopmentUser(UserId, "Selin", "selin@example.com")]);
    }

    private CreateCustomDevelopmentRequestCommandHandler Handler(SubscriptionPlanCode plan)
        => new(_tenant.Object, _client.Object, PlanFeatureTestFactory.ForPlan(plan), _requests.Object, _users.Object, TimeProvider.System);

    private void OpenCount(int count)
        => _requests.Setup(r => r.CountAsync(It.IsAny<OpenCustomDevelopmentRequestsByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(count);

    private static CreateCustomDevelopmentRequestCommand Command()
        => new("Gün sonu raporuna sütun", "Hekim adı sütunu eklensin.", CustomDevelopmentTargetKind.Report, "Gün sonu raporu",
            CustomDevelopmentPriority.Normal);

    [Theory]
    [InlineData(SubscriptionPlanCode.Basic)]
    [InlineData(SubscriptionPlanCode.Pro)]
    public async Task Create_Should_Be_Forbidden_Below_Premium(SubscriptionPlanCode plan)
    {
        OpenCount(0);

        var result = await Handler(plan).Handle(Command(), CancellationToken.None);

        result.Error.Code.Should().Be("Subscriptions.FeatureNotInPlan");
        _requests.Verify(r => r.AddAsync(It.IsAny<CustomDevelopmentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Reject_When_Open_Limit_Reached()
    {
        OpenCount(CustomDevelopmentRequest.MaxOpenPerTenant);

        var result = await Handler(SubscriptionPlanCode.Premium).Handle(Command(), CancellationToken.None);

        result.Error.Code.Should().Be("CustomDevelopment.TooManyOpen");
        _requests.Verify(r => r.AddAsync(It.IsAny<CustomDevelopmentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Save_Received_Request_For_Tenant_And_User()
    {
        OpenCount(CustomDevelopmentRequest.MaxOpenPerTenant - 1);
        CustomDevelopmentRequest? saved = null;
        _requests.Setup(r => r.AddAsync(It.IsAny<CustomDevelopmentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CustomDevelopmentRequest, CancellationToken>((r, _) => saved = r)
            .ReturnsAsync((CustomDevelopmentRequest r, CancellationToken _) => r);

        var result = await Handler(SubscriptionPlanCode.Premium).Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        saved!.TenantId.Should().Be(TenantId);
        saved.CreatedByUserId.Should().Be(UserId);
        result.Value!.Status.Should().Be(CustomDevelopmentRequestStatus.Received);
        result.Value.CreatedByName.Should().Be("Selin");
        _requests.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
