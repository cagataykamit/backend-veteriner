using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Commands.KvkkFormTemplate.UpdateClinicKvkkFormTemplate;
using Backend.Veteriner.Application.Clinics.KvkkFormTemplate;
using Backend.Veteriner.Application.Clinics.Queries.KvkkFormTemplate.GetClinicKvkkFormTemplate;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clinics.KvkkFormTemplate;

/// <summary>
/// ADR-021 / ADR-024: klinik KVKK şablonları (açık rıza formu, aydınlatma metni) okuma (varsayılan) ve kaydetme
/// (oluştur/güncelle, sürüm çakışması).
/// </summary>
public sealed class ClinicKvkkFormTemplateHandlerTests
{
    private const ClinicKvkkFormKind Consent = ClinicKvkkFormKind.Consent;
    private const ClinicKvkkFormKind Disclosure = ClinicKvkkFormKind.Disclosure;
    private static readonly string ValidRowVersion = Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IClinicAssignmentAccessGuard> _assignmentGuard = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<ClinicKvkkFormTemplate>> _templatesRead = new();
    private readonly Mock<IRepository<ClinicKvkkFormTemplate>> _templates = new();

    public ClinicKvkkFormTemplateHandlerTests()
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

    private void ReturnTemplate(ClinicKvkkFormTemplate? row)
    {
        _templatesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicKvkkFormTemplateByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        _templates.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicKvkkFormTemplateByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
    }

    private ClinicKvkkFormTemplate ExistingTemplate(ClinicKvkkFormKind kind = Consent)
        => ClinicKvkkFormTemplate.Create(_tenantId, _clinicId, kind, "Klinik metni", _userId).Value!;

    private GetClinicKvkkFormTemplateQueryHandler CreateQueryHandler()
        => new(_tenantContext.Object, _clientContext.Object, _assignmentGuard.Object, _userClinics.Object, _clinics.Object, _templatesRead.Object);

    private UpdateClinicKvkkFormTemplateCommandHandler CreateCommandHandler()
        => new(_tenantContext.Object, _clientContext.Object, _permissions.Object, _assignmentGuard.Object,
            _userClinics.Object, _clinics.Object, _templates.Object);

    [Fact]
    public async Task Get_Should_Return_Draft_Consent_Default_When_No_Row()
    {
        ReturnTemplate(null);

        var result = await CreateQueryHandler().Handle(new GetClinicKvkkFormTemplateQuery(_clinicId, Consent), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsDefault.Should().BeTrue();
        result.Value.RowVersion.Should().BeNull();
        result.Value.Text.Should().Be(ClinicKvkkFormTemplateDefaults.Text);
        result.Value.Text.Should().StartWith("Bu metin hukuk danışmanınızca onaylanmış açık rıza metniyle");
        result.Value.Text.Should().Contain("{{clinicName}} — KİŞİSEL VERİLERİN İŞLENMESİNE İLİŞKİN AÇIK RIZA BEYANI");
        result.Value.Placeholders.Should().Equal("{{clinicName}}", "{{clientName}}", "{{date}}");
        result.Value.MaxLength.Should().Be(ClinicKvkkFormTemplate.MaxTextLength);
    }

    [Fact]
    public async Task Get_Should_Return_Draft_Disclosure_Default_When_No_Row()
    {
        ReturnTemplate(null);

        var result = await CreateQueryHandler().Handle(new GetClinicKvkkFormTemplateQuery(_clinicId, Disclosure), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsDefault.Should().BeTrue();
        result.Value.RowVersion.Should().BeNull();
        result.Value.Text.Should().Be(ClinicKvkkFormTemplateDefaults.DisclosureText);
        result.Value.Text.Should().StartWith("Bu metin hukuk danışmanınızca onaylanmış aydınlatma metniyle");
        result.Value.Text.Should().Contain("{{clinicName}} — KİŞİSEL VERİLERİN İŞLENMESİNE İLİŞKİN AYDINLATMA METNİ")
            .And.Contain("{{date}}").And.Contain("{{clientName}}").And.NotContain("**");
        result.Value.Text.Length.Should().BeLessThan(ClinicKvkkFormTemplate.MaxTextLength);
        result.Value.Placeholders.Should().Equal("{{clinicName}}", "{{clientName}}", "{{date}}");
        _templatesRead.Verify(x => x.FirstOrDefaultAsync(
            It.Is<ClinicKvkkFormTemplateByClinicSpec>(s => s.IsSatisfiedBy(ExistingTemplate(Disclosure))
                && !s.IsSatisfiedBy(ExistingTemplate(Consent))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_Should_Return_Saved_Text()
    {
        ReturnTemplate(ExistingTemplate());

        var result = await CreateQueryHandler().Handle(new GetClinicKvkkFormTemplateQuery(_clinicId, Consent), CancellationToken.None);

        result.Value!.IsDefault.Should().BeFalse();
        result.Value.Text.Should().Be("Klinik metni");
    }

    [Fact]
    public async Task Get_Should_Deny_Unassigned_Clinic()
    {
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _userClinics.Setup(x => x.ExistsAsync(_userId, _clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateQueryHandler().Handle(new GetClinicKvkkFormTemplateQuery(_clinicId, Disclosure), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Theory]
    [InlineData(ClinicKvkkFormKind.Consent)]
    [InlineData(ClinicKvkkFormKind.Disclosure)]
    public async Task Update_Should_Create_When_No_Row_And_No_RowVersion(ClinicKvkkFormKind kind)
    {
        ReturnTemplate(null);

        var result = await CreateCommandHandler().Handle(
            new UpdateClinicKvkkFormTemplateCommand(_clinicId, kind, "Yeni metin {{clientName}}", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsDefault.Should().BeFalse();
        result.Value.Text.Should().Be("Yeni metin {{clientName}}");
        _templates.Verify(x => x.AddAsync(It.Is<ClinicKvkkFormTemplate>(t => t.Kind == kind), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_Modify_Existing_With_RowVersion()
    {
        var existing = ExistingTemplate(Disclosure);
        ReturnTemplate(existing);

        var result = await CreateCommandHandler().Handle(
            new UpdateClinicKvkkFormTemplateCommand(_clinicId, Disclosure, "Güncel metin", ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.Text.Should().Be("Güncel metin");
        existing.Kind.Should().Be(Disclosure);
        _templates.Verify(x => x.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _templates.Verify(x => x.AddAsync(It.IsAny<ClinicKvkkFormTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Conflict_When_Row_Exists_But_Client_Saw_Default()
    {
        ReturnTemplate(ExistingTemplate());

        var result = await CreateCommandHandler().Handle(
            new UpdateClinicKvkkFormTemplateCommand(_clinicId, Consent, "Metin", null), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.KvkkFormTemplate.ConcurrencyConflict");
        _templates.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Conflict_When_No_Row_But_RowVersion_Sent()
    {
        ReturnTemplate(null);

        var result = await CreateCommandHandler().Handle(
            new UpdateClinicKvkkFormTemplateCommand(_clinicId, Consent, "Metin", ValidRowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.KvkkFormTemplate.ConcurrencyConflict");
    }

    [Fact]
    public async Task Update_Should_Map_Stale_RowVersion_To_Conflict()
    {
        ReturnTemplate(ExistingTemplate());
        _templates.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateCommandHandler().Handle(
            new UpdateClinicKvkkFormTemplateCommand(_clinicId, Consent, "Metin", ValidRowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.KvkkFormTemplate.ConcurrencyConflict");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Update_Should_Reject_Empty_Text(string text)
    {
        ReturnTemplate(null);

        var result = await CreateCommandHandler().Handle(
            new UpdateClinicKvkkFormTemplateCommand(_clinicId, Disclosure, text, null), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.KvkkFormTemplate.Validation");
        _templates.Verify(x => x.AddAsync(It.IsAny<ClinicKvkkFormTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Require_Clinics_Update()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Clinics.Update)).Returns(false);

        var result = await CreateCommandHandler().Handle(
            new UpdateClinicKvkkFormTemplateCommand(_clinicId, Disclosure, "Metin", null), CancellationToken.None);

        result.Error.Code.Should().Be("Auth.PermissionDenied");
    }

    [Fact]
    public void Update_Audit_Action_Should_Distinguish_Kind()
    {
        new UpdateClinicKvkkFormTemplateCommand(_clinicId, Consent, "M", null).AuditAction
            .Should().Be("Clinic.KvkkFormTemplate.Update");
        new UpdateClinicKvkkFormTemplateCommand(_clinicId, Disclosure, "M", null).AuditAction
            .Should().Be("Clinic.KvkkDisclosureTemplate.Update");
    }

    [Fact]
    public void Validators_Should_Reject_Undefined_Kind()
    {
        new GetClinicKvkkFormTemplateQueryValidator()
            .Validate(new GetClinicKvkkFormTemplateQuery(_clinicId, (ClinicKvkkFormKind)99)).IsValid.Should().BeFalse();
        new UpdateClinicKvkkFormTemplateCommandValidator()
            .Validate(new UpdateClinicKvkkFormTemplateCommand(_clinicId, (ClinicKvkkFormKind)99, "M", null)).IsValid.Should().BeFalse();
        new UpdateClinicKvkkFormTemplateCommandValidator()
            .Validate(new UpdateClinicKvkkFormTemplateCommand(_clinicId, Disclosure, "M", null)).IsValid.Should().BeTrue();
    }
}
