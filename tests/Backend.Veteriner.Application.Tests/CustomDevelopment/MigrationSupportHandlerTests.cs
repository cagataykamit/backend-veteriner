using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.CustomDevelopment;
using Backend.Veteriner.Application.Tests.Tenants;
using Backend.Veteriner.Domain.CustomDevelopment;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.CustomDevelopment;

/// <summary>
/// TIER-021: geçiş desteği talebi her pakette açılır; ücret bilgisi pakete göre (Kurumsal ücretsiz, diğerleri ücretli), açık talep sınırı
/// özel geliştirme sınırından ayrıdır.
/// </summary>
public sealed class MigrationSupportHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClientContext> _client = new();
    private readonly Mock<IRepository<CustomDevelopmentRequest>> _requests = new();
    private readonly Mock<IReadRepository<User>> _users = new();

    public MigrationSupportHandlerTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(TenantId);
        _client.SetupGet(c => c.UserId).Returns(UserId);
        _users.Setup(r => r.ListAsync(It.IsAny<ISpecification<User, CustomDevelopmentUser>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CustomDevelopmentUser(UserId, "Selin", "selin@example.com")]);
    }

    private CreateMigrationSupportRequestCommandHandler Handler(SubscriptionPlanCode plan, bool trialing = false)
        => new(_tenant.Object, _client.Object, PlanFeatureTestFactory.ForPlan(plan, trialing: trialing), _requests.Object, _users.Object,
            TimeProvider.System);

    private void OpenMigrationCount(int count)
        => _requests.Setup(r => r.CountAsync(It.IsAny<OpenMigrationSupportRequestsByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(count);

    private static CreateMigrationSupportRequestCommand Command(
        ImportSourceProfile program = ImportSourceProfile.EVet, string? otherName = null)
        => new(program, otherName, MigrationRecordVolume.UpTo5000, MigrationMeetingDays.Tuesday, MigrationMeetingTimes.Afternoon, "Not");

    [Theory]
    [InlineData(SubscriptionPlanCode.Basic, false, MigrationSupportFee.Quoted)]
    [InlineData(SubscriptionPlanCode.Pro, false, MigrationSupportFee.Quoted)]
    [InlineData(SubscriptionPlanCode.Premium, false, MigrationSupportFee.Free)]
    [InlineData(SubscriptionPlanCode.Premium, true, MigrationSupportFee.Free)]
    [InlineData(SubscriptionPlanCode.Basic, true, MigrationSupportFee.Quoted)]
    public async Task Create_Should_Open_In_Every_Plan_With_Plan_Fee(SubscriptionPlanCode plan, bool trialing, MigrationSupportFee fee)
    {
        OpenMigrationCount(0);
        CustomDevelopmentRequest? saved = null;
        _requests.Setup(r => r.AddAsync(It.IsAny<CustomDevelopmentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CustomDevelopmentRequest, CancellationToken>((r, _) => saved = r)
            .ReturnsAsync((CustomDevelopmentRequest r, CancellationToken _) => r);

        var result = await Handler(plan, trialing).Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        saved!.TenantId.Should().Be(TenantId);
        saved.CreatedByUserId.Should().Be(UserId);
        saved.MigrationFee.Should().Be(fee);
        saved.RequestTarget.Should().Be("E-vet");
        result.Value!.TargetKind.Should().Be(CustomDevelopmentTargetKind.Migration);
        result.Value.Migration.Should().Be(new MigrationSupportDetailsDto(
            ImportSourceProfile.EVet, MigrationRecordVolume.UpTo5000, MigrationMeetingDays.Tuesday, MigrationMeetingTimes.Afternoon, fee,
            CustomDevelopmentRequest.MeetingDurationMinutes));
        _requests.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_Reject_Second_Open_Migration_Request()
    {
        OpenMigrationCount(CustomDevelopmentRequest.MaxOpenMigrationPerTenant);

        var result = await Handler(SubscriptionPlanCode.Premium).Handle(Command(), CancellationToken.None);

        result.Error.Code.Should().Be("CustomDevelopment.MigrationAlreadyOpen");
        _requests.Verify(r => r.AddAsync(It.IsAny<CustomDevelopmentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ImportSourceProfile.Generic, "  VetSoft  ", "VetSoft")]
    [InlineData(ImportSourceProfile.Generic, null, "Genel Excel / CSV")]
    [InlineData(ImportSourceProfile.Kolayvet, "yok sayılır", "Kolayvet")]
    public void Program_Name_Should_Come_From_Profile_Or_Clinic(ImportSourceProfile program, string? other, string expected)
        => MigrationSupportRules.ProgramName(program, other).Should().Be(expected);

    [Fact]
    public async Task Overview_Should_Return_Fee_Duration_And_Only_Tenant_Migration_Requests()
    {
        var mine = CustomDevelopmentRequest.CreateMigrationSupport(
            TenantId, ImportSourceProfile.Kolayvet, "Kolayvet", MigrationRecordVolume.Unknown, MigrationMeetingDays.Monday,
            MigrationMeetingTimes.Morning, null, MigrationSupportFee.Quoted, UserId, DateTime.UtcNow).Value!;
        var read = new Mock<IReadRepository<CustomDevelopmentRequest>>();
        MigrationSupportRequestsByTenantSpec? usedSpec = null;
        read.Setup(r => r.ListAsync(It.IsAny<ISpecification<CustomDevelopmentRequest>>(), It.IsAny<CancellationToken>()))
            .Callback<ISpecification<CustomDevelopmentRequest>, CancellationToken>((s, _) => usedSpec = s as MigrationSupportRequestsByTenantSpec)
            .ReturnsAsync([mine]);
        var handler = new GetMigrationSupportQueryHandler(
            _tenant.Object, PlanFeatureTestFactory.ForPlan(SubscriptionPlanCode.Pro), read.Object, _users.Object);

        var result = await handler.Handle(new GetMigrationSupportQuery(), CancellationToken.None);

        result.Value!.Fee.Should().Be(MigrationSupportFee.Quoted);
        result.Value.MeetingDurationMinutes.Should().Be(60);
        result.Value.OpenCount.Should().Be(1);
        result.Value.MaxOpen.Should().Be(CustomDevelopmentRequest.MaxOpenMigrationPerTenant);
        usedSpec.Should().NotBeNull();
    }

    [Fact]
    public void Migration_Specs_Should_Filter_By_Tenant_And_Kind()
    {
        var otherTenant = CustomDevelopmentRequest.CreateMigrationSupport(
            Guid.NewGuid(), ImportSourceProfile.EVet, "E-vet", MigrationRecordVolume.Unknown, MigrationMeetingDays.Monday,
            MigrationMeetingTimes.Morning, null, MigrationSupportFee.Quoted, UserId, DateTime.UtcNow).Value!;
        var mine = CustomDevelopmentRequest.CreateMigrationSupport(
            TenantId, ImportSourceProfile.EVet, "E-vet", MigrationRecordVolume.Unknown, MigrationMeetingDays.Monday,
            MigrationMeetingTimes.Morning, null, MigrationSupportFee.Quoted, UserId, DateTime.UtcNow).Value!;
        var customDevelopment = CustomDevelopmentRequest.Create(
            TenantId, "Başlık", "Metin", CustomDevelopmentTargetKind.Report, "Rapor", CustomDevelopmentPriority.Normal, UserId, DateTime.UtcNow).Value!;
        var all = new[] { otherTenant, mine, customDevelopment };

        new MigrationSupportRequestsByTenantSpec(TenantId).Evaluate(all).Should().Equal(mine);
        new OpenMigrationSupportRequestsByTenantSpec(TenantId).Evaluate(all).Should().Equal(mine);
        // Özel geliştirme listesi ve sınırı geçiş talebini saymaz.
        new CustomDevelopmentRequestsByTenantSpec(TenantId).Evaluate(all).Should().Equal(customDevelopment);
        new OpenCustomDevelopmentRequestsByTenantSpec(TenantId).Evaluate(all).Should().Equal(customDevelopment);
    }
}
