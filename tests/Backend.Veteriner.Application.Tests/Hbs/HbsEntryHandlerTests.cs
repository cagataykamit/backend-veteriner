using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hbs;
using Backend.Veteriner.Application.Prescriptions.Commands.UpdateHbs;
using Backend.Veteriner.Application.Prescriptions.Queries.GetHbsData;
using Backend.Veteriner.Application.Prescriptions.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Vaccinations.Commands.UpdateHbs;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Hbs;

/// <summary>
/// ADR-036 Dilim 2: HBS geri kaydı (PATCH) ve hazır veri (GET) handler'ları — kayıt, klinik kapsamı, aşı koşulu, kimlik no izni.
/// Kesinleşmiş muayene kilidi bu yola hiç girmez (muayene deposu enjekte edilmez); uçtan uca kanıt entegrasyon testindedir.
/// </summary>
public sealed class HbsEntryHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scope = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Prescription>> _prescriptionsRead = new();
    private readonly Mock<IRepository<Prescription>> _prescriptionsWrite = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinationsRead = new();
    private readonly Mock<IRepository<Vaccination>> _vaccinationsWrite = new();
    private readonly Mock<IClinicVeterinarianReader> _names = new();

    public HbsEntryHandlerTests()
    {
        var tenant = new Tenant("Klinik");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clientContext.SetupGet(c => c.UserId).Returns(_userId);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _scope.Setup(s => s.ResolveAsync(_tenantId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Success(new ClinicReadScope(_clinicId, null)));
        _names.Setup(n => n.GetNamesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string?> { [_userId] = "Dr. Ayşe" });
    }

    private Prescription SetupPrescription()
    {
        var p = new Prescription(_tenantId, _clinicId, Guid.NewGuid(), Guid.NewGuid(), null, DateTime.UtcNow, "Reçete", "İçerik", null, null);
        _prescriptionsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(p);
        return p;
    }

    private UpdatePrescriptionHbsCommandHandler PrescriptionHandler()
        => new(_tenantContext.Object, _clinicContext.Object, _clientContext.Object, _scope.Object, _tenants.Object,
            _prescriptionsRead.Object, _prescriptionsWrite.Object, _names.Object);

    private UpdateVaccinationHbsCommandHandler VaccinationHandler()
        => new(_tenantContext.Object, _clinicContext.Object, _clientContext.Object, _scope.Object, _tenants.Object,
            _vaccinationsRead.Object, _vaccinationsWrite.Object, _names.Object);

    [Fact]
    public async Task Prescription_Patch_Should_Save_Number_And_Return_Marker_Name()
    {
        var p = SetupPrescription();

        var result = await PrescriptionHandler().Handle(
            new UpdatePrescriptionHbsCommand(p.Id, HbsEntryStatus.Entered, " 2RX4K9 ", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ReferenceNumber.Should().Be("2RX4K9");
        result.Value.MarkedByName.Should().Be("Dr. Ayşe");
        result.Value.IsTracked.Should().BeTrue();
        p.HbsMarkedByUserId.Should().Be(_userId);
        _prescriptionsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Prescription_Patch_Validation_Failure_Should_Not_Save()
    {
        var p = SetupPrescription();

        var result = await PrescriptionHandler().Handle(
            new UpdatePrescriptionHbsCommand(p.Id, HbsEntryStatus.NotRequired, null, "kısa"), CancellationToken.None);

        result.Error.Code.Should().Be("Prescriptions.HbsValidation");
        _prescriptionsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Prescription_Patch_In_Other_Clinic_Context_Should_Be_NotFound()
    {
        var p = SetupPrescription();
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        var result = await PrescriptionHandler().Handle(
            new UpdatePrescriptionHbsCommand(p.Id, HbsEntryStatus.Entered, "X", null), CancellationToken.None);

        result.Error.Code.Should().Be("Prescriptions.NotFound");
    }

    [Fact]
    public async Task Prescription_Patch_Without_Clinic_Assignment_Should_Be_Denied()
    {
        var p = SetupPrescription();
        _scope.Setup(s => s.ResolveAsync(_tenantId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Failure("Clinics.AccessDenied", "Klinik erişimi yok."));

        var result = await PrescriptionHandler().Handle(
            new UpdatePrescriptionHbsCommand(p.Id, HbsEntryStatus.Entered, "X", null), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
        p.HbsStatus.Should().Be(HbsEntryStatus.NotEntered);
    }

    [Fact]
    public async Task Vaccination_Patch_On_Scheduled_Vaccination_Should_Fail()
    {
        var v = new Vaccination(_tenantId, Guid.NewGuid(), _clinicId, null, Guid.NewGuid(), "Kuduz", VaccinationStatus.Scheduled,
            null, DateTime.UtcNow.AddDays(5), null);
        _vaccinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccinationByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(v);

        var result = await VaccinationHandler().Handle(
            new UpdateVaccinationHbsCommand(v.Id, HbsEntryStatus.Entered, "ATS-1", null), CancellationToken.None);

        result.Error.Code.Should().Be("Vaccinations.HbsNotApplicable");
        _vaccinationsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Vaccination_Patch_On_Applied_Vaccination_Should_Save_Ats_Number()
    {
        var v = new Vaccination(_tenantId, Guid.NewGuid(), _clinicId, null, Guid.NewGuid(), "Kuduz", VaccinationStatus.Applied,
            DateTime.UtcNow.AddHours(-1), null, null);
        _vaccinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccinationByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(v);

        var result = await VaccinationHandler().Handle(
            new UpdateVaccinationHbsCommand(v.Id, HbsEntryStatus.Entered, "ATS-1", null), CancellationToken.None);

        result.Value!.ReferenceNumber.Should().Be("ATS-1");
        result.Value.IsTracked.Should().BeTrue();
        _vaccinationsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true, "10000000146", false)]
    [InlineData(false, "100******46", true)]
    public async Task HbsData_Should_Reveal_Owner_Identity_Only_With_Clients_Update(bool canUpdateClients, string expected, bool masked)
    {
        var sources = new Mock<IHbsSourceReader>();
        var source = new HbsPrescriptionSource(
            Guid.NewGuid(), _clinicId, DateTime.UtcNow, "İçerik", [],
            new HbsAnimalSource("Pamuk", "Kedi", null, null, null),
            new HbsOwnerSource("Ayşe", null, ClientPersonType.Individual, "10000000146", null),
            null,
            new HbsEntrySource(HbsEntryStatus.NotEntered, null, null, null, null, null));
        sources.Setup(s => s.GetPrescriptionAsync(_tenantId, source.Id, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        var claims = new Mock<IUserOperationClaimRepository>();
        claims.Setup(c => c.GetOperationClaimNamesByUserIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());
        var userClinics = new Mock<IUserClinicRepository>();
        userClinics.Setup(u => u.ExistsAsync(_userId, _clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var permissions = new Mock<ICurrentUserPermissionChecker>();
        permissions.Setup(p => p.HasPermission(PermissionCatalog.Clients.Update)).Returns(canUpdateClients);

        var result = await new GetPrescriptionHbsDataQueryHandler(
                _tenantContext.Object, _clinicContext.Object, _clientContext.Object, claims.Object, userClinics.Object, permissions.Object, sources.Object)
            .Handle(new GetPrescriptionHbsDataQuery(source.Id), CancellationToken.None);

        result.Value!.Sections.SelectMany(s => s.Fields).Single(f => f.Key == "owner.identityNumber").Value.Should().Be(expected);
        result.Value.OwnerIdentityMasked.Should().Be(masked);
    }

    [Fact]
    public async Task HbsData_For_Unassigned_Clinic_Should_Be_NotFound()
    {
        var sources = new Mock<IHbsSourceReader>();
        var source = new HbsPrescriptionSource(
            Guid.NewGuid(), _clinicId, DateTime.UtcNow, "İçerik", [],
            new HbsAnimalSource("Pamuk", "Kedi", null, null, null),
            new HbsOwnerSource("Ayşe", null, null, null, null),
            null,
            new HbsEntrySource(HbsEntryStatus.NotEntered, null, null, null, null, null));
        sources.Setup(s => s.GetPrescriptionAsync(_tenantId, source.Id, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        var claims = new Mock<IUserOperationClaimRepository>();
        claims.Setup(c => c.GetOperationClaimNamesByUserIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());

        var result = await new GetPrescriptionHbsDataQueryHandler(
                _tenantContext.Object, _clinicContext.Object, _clientContext.Object, claims.Object, new Mock<IUserClinicRepository>().Object,
                new Mock<ICurrentUserPermissionChecker>().Object, sources.Object)
            .Handle(new GetPrescriptionHbsDataQuery(source.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Prescriptions.NotFound");
    }
}
