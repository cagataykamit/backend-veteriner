using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Commands.TaxpayerSettings.UpdateClinicTaxpayerSettings;
using Backend.Veteriner.Application.Clinics.Queries.TaxpayerSettings.GetClinicTaxpayerSettings;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clinics.TaxpayerSettings;

/// <summary>ADR-036 Dilim 1: klinik mükellef ayarları okuma (varsayılan) ve kaydetme (izin, oluştur/güncelle, doğrulama).</summary>
public sealed class ClinicTaxpayerSettingsHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IClinicAssignmentAccessGuard> _assignmentGuard = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<ClinicTaxpayerSettings>> _settingsRead = new();
    private readonly Mock<IRepository<ClinicTaxpayerSettings>> _settings = new();

    public ClinicTaxpayerSettingsHandlerTests()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(_tenantId);
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Clinics.Update)).Returns(true);
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var clinic = new Clinic(_tenantId, "K", "Istanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, _clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
    }

    private void ReturnSettings(ClinicTaxpayerSettings? row)
    {
        _settingsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicTaxpayerSettingsByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        _settings.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicTaxpayerSettingsByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
    }

    private GetClinicTaxpayerSettingsQueryHandler QueryHandler()
        => new(_tenantContext.Object, _clientContext.Object, _assignmentGuard.Object, _userClinics.Object, _clinics.Object, _settingsRead.Object);

    private UpdateClinicTaxpayerSettingsCommandHandler CommandHandler()
        => new(_tenantContext.Object, _clientContext.Object, _permissions.Object, _assignmentGuard.Object,
            _userClinics.Object, _clinics.Object, _settings.Object, TimeProvider.System);

    private UpdateClinicTaxpayerSettingsCommand Command(string? identity = "1234567890", decimal? vat = 20m, decimal? withholding = null)
        => new(_clinicId, ClinicTaxpayerType.Company, identity, "Kadıköy VD", "Örnek Klinik Ltd.", "İstanbul", "Kadıköy",
            "Moda Cad. 1", vat, withholding);

    [Fact]
    public async Task Get_Should_Return_Default_With_Twenty_Percent_Vat_When_No_Row()
    {
        ReturnSettings(null);

        var result = await QueryHandler().Handle(new GetClinicTaxpayerSettingsQuery(_clinicId), CancellationToken.None);

        result.Value!.IsConfigured.Should().BeFalse();
        result.Value.DefaultVatRate.Should().Be(20m);
        result.Value.WithholdingRate.Should().BeNull();
        result.Value.TaxIdentityNumber.Should().BeNull();
    }

    [Fact]
    public async Task Get_Should_Deny_Unassigned_Clinic()
    {
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _userClinics.Setup(x => x.ExistsAsync(_userId, _clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await QueryHandler().Handle(new GetClinicTaxpayerSettingsQuery(_clinicId), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Update_Should_Create_Row_When_None()
    {
        ReturnSettings(null);

        var result = await CommandHandler().Handle(Command(withholding: 20m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsConfigured.Should().BeTrue();
        result.Value.TaxpayerType.Should().Be(ClinicTaxpayerType.Company);
        result.Value.WithholdingRate.Should().Be(20m);
        _settings.Verify(x => x.AddAsync(It.IsAny<ClinicTaxpayerSettings>(), It.IsAny<CancellationToken>()), Times.Once);
        _settings.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_Change_Existing_Row()
    {
        var existing = ClinicTaxpayerSettings.Create(_tenantId, _clinicId, null, null, null, null, null, null, null, 20m, null, _userId, DateTime.UtcNow).Value!;
        ReturnSettings(existing);

        var result = await CommandHandler().Handle(Command(identity: "10000000146", vat: 10m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.TaxIdentityNumber.Should().Be("10000000146");
        existing.DefaultVatRate.Should().Be(10m);
        _settings.Verify(x => x.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_Require_Clinics_Update()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Clinics.Update)).Returns(false);

        var result = await CommandHandler().Handle(Command(), CancellationToken.None);

        result.Error.Code.Should().Be("Auth.PermissionDenied");
        _settings.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Reject_Invalid_Identity_Without_Saving()
    {
        ReturnSettings(null);

        var result = await CommandHandler().Handle(Command(identity: "1234567891"), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.TaxpayerSettings.Validation");
        _settings.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("1234567891", 20.0, null, "TaxIdentityNumber")]
    [InlineData("1234567890", 120.0, null, "DefaultVatRate")]
    [InlineData("1234567890", 20.0, 20.555, "WithholdingRate")]
    public void Validator_Should_Report_Field(string identity, double vat, double? withholding, string property)
    {
        var errors = new UpdateClinicTaxpayerSettingsCommandValidator()
            .Validate(Command(identity, (decimal)vat, (decimal?)withholding)).Errors;

        errors.Should().ContainSingle().Which.PropertyName.Should().Be(property);
    }

    [Fact]
    public void Validator_Should_Require_Default_Vat()
        => new UpdateClinicTaxpayerSettingsCommandValidator().Validate(Command(vat: null))
            .Errors.Should().ContainSingle(e => e.PropertyName == "DefaultVatRate");
}
