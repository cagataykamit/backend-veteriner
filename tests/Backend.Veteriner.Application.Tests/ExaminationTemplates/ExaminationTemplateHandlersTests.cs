using Backend.Veteriner.Application.Administrations.Commands.Create;
using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.ExaminationTemplates.Commands.Apply;
using Backend.Veteriner.Application.ExaminationTemplates.Commands.Create;
using Backend.Veteriner.Application.ExaminationTemplates.Commands.SetActive;
using Backend.Veteriner.Application.ExaminationTemplates.Commands.Update;
using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Application.ExaminationTemplates.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests.Examinations;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.ExaminationTemplates;

public sealed class ExaminationTemplateHandlersTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] TemplateRowVersion = [0, 0, 0, 0, 0, 0, 0, 7];
    private static readonly string TemplateRowVersionBase64 = Convert.ToBase64String(TemplateRowVersion);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Examination>> _examinationsRead = new();
    private readonly Mock<IRepository<Examination>> _examinationsWrite = new();
    private readonly Mock<IRepository<ExaminationTemplate>> _templates = new();
    private readonly Mock<IReadRepository<ExaminationTemplate>> _templatesRead = new();
    private readonly Mock<IReadRepository<Product>> _products = new();
    private readonly Mock<IRepository<ChargeSuggestion>> _chargeSuggestions = new();
    private readonly Mock<ISender> _sender = new();

    private readonly List<Product> _catalog = new();
    private readonly List<CreateAdministrationCommand> _sentAdministrations = new();
    private readonly List<ChargeSuggestion> _addedSuggestions = new();

    public ExaminationTemplateHandlersTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _templates.Setup(r => r.AnyAsync(It.IsAny<ExaminationTemplateNameTakenSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _templates.Setup(r => r.AddAsync(It.IsAny<ExaminationTemplate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExaminationTemplate t, CancellationToken _) => t);
        _templates.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _products.Setup(r => r.ListAsync(It.IsAny<ProductsByIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _catalog.ToList());
        _examinationsWrite.Setup(r => r.UpdateAsync(It.IsAny<Examination>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _examinationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _chargeSuggestions.Setup(r => r.AddAsync(It.IsAny<ChargeSuggestion>(), It.IsAny<CancellationToken>()))
            .Callback<ChargeSuggestion, CancellationToken>((s, _) => _addedSuggestions.Add(s))
            .ReturnsAsync((ChargeSuggestion s, CancellationToken _) => s);
        _sender.Setup(s => s.Send(It.IsAny<CreateAdministrationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<AdministrationCreateResultDto>>, CancellationToken>((c, _) => _sentAdministrations.Add((CreateAdministrationCommand)c))
            .ReturnsAsync((IRequest<Result<AdministrationCreateResultDto>> c, CancellationToken _) => AdministrationOk((CreateAdministrationCommand)c));
    }

    private Product AddProduct(ProductKind kind, decimal price = 100m)
    {
        var product = new Product(_tenantId, $"Urun-{_catalog.Count}", "Adet", price, "TRY", kind: kind);
        _catalog.Add(product);
        return product;
    }

    private ExaminationTemplate Existing(ExaminationTemplate template)
    {
        typeof(ExaminationTemplate).GetProperty(nameof(ExaminationTemplate.RowVersion))!.SetValue(template, TemplateRowVersion);
        _templates.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationTemplateVisibleByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _templatesRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationTemplateVisibleByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        return template;
    }

    private ExaminationTemplate TenantTemplate(params ExaminationTemplateLineDraft[] lines)
        => Existing(ExaminationTemplate.CreateForTenant(_tenantId, "Kulak paketi", "Kulak kaşıntısı", "Otoskopi: ", null, lines).Value!);

    private Examination Draft(string visitReason = "Kontrol", string findings = "Genel durum iyi")
    {
        var e = new Examination(_tenantId, _clinicId, Guid.NewGuid(), null, Now.AddHours(-1), visitReason, findings, null, null);
        ExaminationTestSupport.SetRowVersion(e);
        _examinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(e);
        return e;
    }

    private static Result<AdministrationCreateResultDto> AdministrationOk(CreateAdministrationCommand c)
        => Result<AdministrationCreateResultDto>.Success(new AdministrationCreateResultDto(
            true,
            new AdministrationDto(
                Guid.NewGuid(), c.ClinicId!.Value, c.PetId, c.ExaminationId, null, c.ProductId, "Urun", null, "Adet", c.Quantity,
                Now, null, "Active", null, null, null, null, false, null, Guid.NewGuid(), Now, null),
            new AdministrationStockDto(10m, 0m, false, 10m, 0m)));

    private CreateExaminationTemplateCommandHandler CreateHandler() => new(_tenantContext.Object, _templates.Object, _products.Object);
    private UpdateExaminationTemplateCommandHandler UpdateHandler() => new(_tenantContext.Object, _templates.Object, _products.Object);
    private SetExaminationTemplateActiveCommandHandler SetActiveHandler() => new(_tenantContext.Object, _templates.Object);

    private ApplyExaminationTemplateCommandHandler ApplyHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _tenants.Object,
            _examinationsRead.Object,
            _examinationsWrite.Object,
            _templatesRead.Object,
            _products.Object,
            _chargeSuggestions.Object,
            _sender.Object,
            new FixedClock(Now));

    private static ApplyExaminationTemplateCommand ApplyCmd(Examination e, ExaminationTemplate t)
        => new(e.Id, t.Id, ExaminationTestSupport.SampleRowVersionBase64);

    [Fact]
    public async Task Create_Should_Return_Lines_With_Current_Product_Card()
    {
        var drug = AddProduct(ProductKind.Medicine, 45m);
        var service = AddProduct(ProductKind.Service, 300m);

        var result = await CreateHandler().Handle(
            new CreateExaminationTemplateCommand(
                " Kulak  paketi ", "Kulak kaşıntısı", null, null,
                [new ExaminationTemplateLineRequest(service.Id, 1m), new ExaminationTemplateLineRequest(drug.Id, 2.5m)]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Kulak paketi");
        result.Value.IsSystem.Should().BeFalse();
        result.Value.Lines.Select(l => l.ProductId).Should().Equal(service.Id, drug.Id);
        result.Value.Lines[0].ProductKind.Should().Be(ProductKind.Service);
        result.Value.Lines[1].CurrentUnitPrice.Should().Be(45m);
        _templates.Verify(r => r.AddAsync(It.IsAny<ExaminationTemplate>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_Reject_Vaccine_And_Unknown_Product_With_ProductId()
    {
        var vaccine = AddProduct(ProductKind.Vaccine);

        var vaccineResult = await CreateHandler().Handle(
            new CreateExaminationTemplateCommand("Aşı paketi", null, null, null, [new ExaminationTemplateLineRequest(vaccine.Id, 1m)]),
            CancellationToken.None);
        vaccineResult.Error.Code.Should().Be("ExaminationTemplates.VaccineNotAllowed");
        vaccineResult.Error.Extensions!["productId"].Should().Be(vaccine.Id);

        var unknownId = Guid.NewGuid();
        var unknownResult = await CreateHandler().Handle(
            new CreateExaminationTemplateCommand("Paket", null, null, null, [new ExaminationTemplateLineRequest(unknownId, 1m)]),
            CancellationToken.None);
        unknownResult.Error.Code.Should().Be("Products.NotFound");
        unknownResult.Error.Extensions!["productId"].Should().Be(unknownId);

        _templates.Verify(r => r.AddAsync(It.IsAny<ExaminationTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Return_Duplicate_When_Name_Taken()
    {
        _templates.Setup(r => r.AnyAsync(It.IsAny<ExaminationTemplateNameTakenSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(
            new CreateExaminationTemplateCommand("RUTİN MUAYENE", "Kontrol", null, null, null), CancellationToken.None);

        result.Error.Code.Should().Be("ExaminationTemplates.DuplicateName");
    }

    [Fact]
    public async Task Update_Should_Replace_Lines_With_Expected_RowVersion()
    {
        var oldProduct = AddProduct(ProductKind.Medicine);
        var newProduct = AddProduct(ProductKind.Service);
        var template = TenantTemplate(new ExaminationTemplateLineDraft(oldProduct.Id, 1m));

        var result = await UpdateHandler().Handle(
            new UpdateExaminationTemplateCommand(
                template.Id, "Kulak paketi 2", null, "Otoskopi: ", "Kontrol: ",
                [new ExaminationTemplateLineRequest(newProduct.Id, 3m)], TemplateRowVersionBase64),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Kulak paketi 2");
        result.Value.Lines.Should().ContainSingle().Which.ProductId.Should().Be(newProduct.Id);
        _templates.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_Return_Conflict_For_Stale_RowVersion_And_On_Save_Race()
    {
        var template = TenantTemplate();

        var stale = await UpdateHandler().Handle(
            new UpdateExaminationTemplateCommand(
                template.Id, "Yeni ad", "Şikâyet", null, null, [], Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 })),
            CancellationToken.None);
        stale.Error.Code.Should().Be("ExaminationTemplates.ConcurrencyConflict");
        _templates.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        _templates.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateConcurrencyException());
        var race = await UpdateHandler().Handle(
            new UpdateExaminationTemplateCommand(template.Id, "Yeni ad", "Şikâyet", null, null, [], TemplateRowVersionBase64),
            CancellationToken.None);
        race.Error.Code.Should().Be("ExaminationTemplates.ConcurrencyConflict");
    }

    [Fact]
    public async Task Update_And_SetActive_Should_Reject_System_Template()
    {
        var system = Existing(ExaminationTemplate.CreateSystem("Rutin muayene", "Genel kontrol", null, null));

        var update = await UpdateHandler().Handle(
            new UpdateExaminationTemplateCommand(system.Id, "Rutin", "Kontrol", null, null, [], TemplateRowVersionBase64),
            CancellationToken.None);
        update.Error.Code.Should().Be("ExaminationTemplates.SystemTemplateReadOnly");

        var deactivate = await SetActiveHandler().Handle(new SetExaminationTemplateActiveCommand(system.Id, false), CancellationToken.None);
        deactivate.Error.Code.Should().Be("ExaminationTemplates.SystemTemplateReadOnly");
        _templates.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetActive_Should_Deactivate_Tenant_Template()
    {
        var template = TenantTemplate();

        var result = await SetActiveHandler().Handle(new SetExaminationTemplateActiveCommand(template.Id, false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        template.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Apply_Should_Merge_Text_Send_Administrations_And_Create_Service_Items()
    {
        var drug = AddProduct(ProductKind.Medicine, 45m);
        var service = AddProduct(ProductKind.Service, 300m);
        var freeService = AddProduct(ProductKind.Service, 0m);
        var template = TenantTemplate(
            new ExaminationTemplateLineDraft(service.Id, 1m),
            new ExaminationTemplateLineDraft(drug.Id, 2m),
            new ExaminationTemplateLineDraft(freeService.Id, 1m));
        var exam = Draft();

        var result = await ApplyHandler().Handle(ApplyCmd(exam, template), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        exam.VisitReason.Should().Be("Kontrol, Kulak kaşıntısı");
        exam.Findings.Should().Be("Genel durum iyi\nOtoskopi:");
        result.Value!.UpdatedFields.Should().Equal("visitReason", "findings");
        exam.RowVersion.Should().Equal(ExaminationTestSupport.SampleRowVersionBytes);

        var sent = _sentAdministrations.Should().ContainSingle().Subject;
        sent.ProductId.Should().Be(drug.Id);
        sent.Quantity.Should().Be(2m);
        sent.ExaminationId.Should().Be(exam.Id);
        sent.ClinicId.Should().Be(_clinicId);
        sent.PetId.Should().Be(exam.PetId);
        result.Value.Administrations.Should().ContainSingle();

        var suggestion = _addedSuggestions.Should().ContainSingle().Subject;
        suggestion.SourceType.Should().Be(ChargeSuggestion.ExaminationServiceSource);
        suggestion.ProductId.Should().Be(service.Id);
        suggestion.SuggestedUnitPrice.Should().Be(300m);
        suggestion.ExaminationId.Should().Be(exam.Id);
        result.Value.Services.Should().HaveCount(2);
        result.Value.Services[0].ChargeSuggestionId.Should().Be(suggestion.Id);
        result.Value.Services[1].ChargeSuggestionId.Should().BeNull();

        _examinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Apply_Should_Reject_Finalized_Examination_Without_Writes()
    {
        var drug = AddProduct(ProductKind.Medicine);
        var template = TenantTemplate(new ExaminationTemplateLineDraft(drug.Id, 1m));
        var exam = Draft();
        exam.Finalize(Guid.NewGuid(), Now).IsSuccess.Should().BeTrue();

        var result = await ApplyHandler().Handle(ApplyCmd(exam, template), CancellationToken.None);

        result.Error.Code.Should().Be("Examinations.Finalized");
        _sentAdministrations.Should().BeEmpty();
        _examinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Apply_Should_Reject_Inactive_Template_And_Missing_RowVersion()
    {
        var template = TenantTemplate();
        template.Deactivate();
        var exam = Draft();

        var inactive = await ApplyHandler().Handle(ApplyCmd(exam, template), CancellationToken.None);
        inactive.Error.Code.Should().Be("ExaminationTemplates.Inactive");

        var noVersion = await ApplyHandler().Handle(new ApplyExaminationTemplateCommand(exam.Id, template.Id, null), CancellationToken.None);
        noVersion.Error.Code.Should().Be("Examinations.Validation");
        _examinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Apply_Should_Return_Conflict_Before_Stock_On_Stale_Examination()
    {
        var drug = AddProduct(ProductKind.Medicine);
        var template = TenantTemplate(new ExaminationTemplateLineDraft(drug.Id, 1m));
        var exam = Draft();
        _examinationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await ApplyHandler().Handle(ApplyCmd(exam, template), CancellationToken.None);

        result.Error.Code.Should().Be("Examinations.ConcurrencyConflict");
        _sentAdministrations.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_Should_Fail_With_ProductId_When_Administration_Rejected()
    {
        var drug = AddProduct(ProductKind.Medicine);
        var template = TenantTemplate(new ExaminationTemplateLineDraft(drug.Id, 5m));
        var exam = Draft();
        _sender.Setup(s => s.Send(It.IsAny<CreateAdministrationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AdministrationCreateResultDto>.Failure("Administrations.InsufficientStock", "Stok yetersiz."));

        var result = await ApplyHandler().Handle(ApplyCmd(exam, template), CancellationToken.None);

        result.Error.Code.Should().Be("Administrations.InsufficientStock");
        result.Error.Extensions!["productId"].Should().Be(drug.Id);
    }

    [Fact]
    public async Task Apply_Should_Revalidate_Lines_At_Apply_Time()
    {
        var drug = AddProduct(ProductKind.Medicine);
        var template = TenantTemplate(new ExaminationTemplateLineDraft(drug.Id, 1m));
        var exam = Draft();
        _catalog.Clear();

        var result = await ApplyHandler().Handle(ApplyCmd(exam, template), CancellationToken.None);

        result.Error.Code.Should().Be("Products.NotFound");
        _examinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
