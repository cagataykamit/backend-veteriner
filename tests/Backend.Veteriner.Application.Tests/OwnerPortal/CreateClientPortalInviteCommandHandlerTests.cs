using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Commands.CreateClientPortalInvite;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.OwnerPortal;

/// <summary>ADR-031 K3/K4: klinik paneli daveti — uygunluk kuralı, e-posta, token gizliliği, eski davetin iptali.</summary>
public sealed class CreateClientPortalInviteCommandHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly OwnerPortalTestFixture _f = new();
    private readonly Tenant _tenant = new("Kiracı");
    private readonly Clinic _clinic;
    private readonly Client _client;
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<OwnerAccountClientLink>> _links = new();
    private readonly Mock<IRepository<OwnerPortalInvite>> _invites = new();
    private readonly Mock<IClinicAssignmentAccessGuard> _assignmentGuard = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly List<OwnerPortalInvite> _pending = [];
    private readonly List<OwnerPortalInvite> _added = [];
    private OwnerAccountClientLink? _activeLink;

    public CreateClientPortalInviteCommandHandlerTests()
    {
        _clinic = new Clinic(_tenant.Id, "Merkez Klinik", "İzmir");
        _client = new Client(_tenant.Id, "Ayşe Yılmaz", email: "Ayse@Example.com");

        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenant.Id);
        _clientContext.SetupGet(c => c.UserId).Returns(UserId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(_clinic.Id);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Tenant>>(), It.IsAny<CancellationToken>())).ReturnsAsync(_tenant);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Client>>(), It.IsAny<CancellationToken>())).ReturnsAsync(_client);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>())).ReturnsAsync(_clinic);
        _links.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<OwnerAccountClientLink>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _activeLink);
        _invites.Setup(r => r.ListAsync(It.IsAny<ISpecification<OwnerPortalInvite>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _pending.ToList());
        _invites.Setup(r => r.AddAsync(It.IsAny<OwnerPortalInvite>(), It.IsAny<CancellationToken>()))
            .Callback<OwnerPortalInvite, CancellationToken>((i, _) => _added.Add(i))
            .ReturnsAsync((OwnerPortalInvite i, CancellationToken _) => i);
        _assignmentGuard.Setup(g => g.MustApplyAssignedClinicScopeAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
    }

    private CreateClientPortalInviteCommandHandler Handler() => new(
        new OwnerPortalStaffAccess(
            _tenantContext.Object, _clientContext.Object, _tenants.Object, _clients.Object, _assignmentGuard.Object, _userClinics.Object),
        _clinicContext.Object,
        _clinics.Object,
        _links.Object,
        _invites.Object,
        _f.Secrets.Object,
        _f.Hash.Object,
        _f.Email.Object,
        Options.Create(new OwnerPortalOptions { PortalBaseUrl = "https://portal.example.com/" }),
        Options.Create(new AppOptions()),
        _f.Clock);

    private void VerifyNoEmail() =>
        _f.Email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);

    [Fact]
    public async Task Should_Email_Link_To_Client_Email_And_Store_Only_Token_Hash()
    {
        _f.NextToken = "secret-invite-token";

        var result = await Handler().Handle(new CreateClientPortalInviteCommand(_client.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Pending");
        result.Value.Email.Should().Be("ayse@example.com");
        result.Value.ExpiresAtUtc.Should().Be(OwnerPortalTestFixture.Now.AddDays(7));

        var invite = _added.Should().ContainSingle().Subject;
        invite.TokenHash.Should().Be("h:secret-invite-token");
        invite.ClinicId.Should().Be(_clinic.Id);
        invite.CreatedByUserId.Should().Be(UserId);
        _f.Email.Verify(e => e.SendAsync(
            "ayse@example.com",
            It.Is<string>(s => s.Contains("Merkez Klinik")),
            It.Is<string>(b => b.Contains("https://portal.example.com/invite?token=secret-invite-token")),
            It.IsAny<CancellationToken>(),
            false), Times.Once);
    }

    [Fact]
    public async Task Should_Not_Invite_Kvkk_Declined_Client()
    {
        _client.RecordKvkkConsent(KvkkConsentStatus.Declined, null, new DateOnly(2026, 10, 16), UserId, null, null);

        var result = await Handler().Handle(new CreateClientPortalInviteCommand(_client.Id, null), CancellationToken.None);

        result.Error.Code.Should().Be("OwnerPortal.ClientOptedOut");
        _added.Should().BeEmpty();
        VerifyNoEmail();
    }

    [Fact]
    public async Task Should_Not_Invite_Client_Who_Opted_Out_Of_Reminders()
    {
        _client.ApplyContactPreferences(null, remindersOptedOut: true);

        var result = await Handler().Handle(new CreateClientPortalInviteCommand(_client.Id, null), CancellationToken.None);

        result.Error.Code.Should().Be("OwnerPortal.ClientOptedOut");
        VerifyNoEmail();
    }

    [Fact]
    public async Task Should_Fail_When_Client_Already_Linked()
    {
        _activeLink = OwnerAccountClientLink.Create(Guid.NewGuid(), _tenant.Id, _clinic.Id, _client.Id, UserId, OwnerPortalTestFixture.Now);

        var result = await Handler().Handle(new CreateClientPortalInviteCommand(_client.Id, null), CancellationToken.None);

        result.Error.Code.Should().Be("OwnerPortal.AlreadyLinked");
        VerifyNoEmail();
    }

    [Fact]
    public async Task Should_Revoke_Previous_Pending_Invite()
    {
        var previous = OwnerPortalInvite.CreatePending(
            _tenant.Id, _clinic.Id, _client.Id, "ayse@example.com", "h:old", UserId, OwnerPortalTestFixture.Now.AddDays(3), OwnerPortalTestFixture.Now);
        _pending.Add(previous);

        await Handler().Handle(new CreateClientPortalInviteCommand(_client.Id, null), CancellationToken.None);

        previous.Status.Should().Be(OwnerPortalInviteStatus.Revoked);
        _added.Should().ContainSingle();
    }

    [Fact]
    public async Task Should_Reject_Body_Clinic_That_Differs_From_Context()
    {
        var result = await Handler().Handle(new CreateClientPortalInviteCommand(_client.Id, Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("OwnerPortal.ClinicContextMismatch");
    }

    [Fact]
    public async Task Should_Reject_Unassigned_Clinic_For_Clinic_Scoped_User()
    {
        _assignmentGuard.Setup(g => g.MustApplyAssignedClinicScopeAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _userClinics.Setup(u => u.ExistsAsync(UserId, _clinic.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Handler().Handle(new CreateClientPortalInviteCommand(_client.Id, null), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
        VerifyNoEmail();
    }
}
