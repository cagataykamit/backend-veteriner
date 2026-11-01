using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ExaminationTemplates.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Visits.Commands.SetPreparation;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Visits;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Visits;

/// <summary>CHECKIN-001/002/003: geliş hazırlığı (geliş nedeni + hazırlanan şablon/paket).</summary>
public sealed class SetVisitPreparationCommandHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Visit>> _visitsRead = new();
    private readonly Mock<IRepository<Visit>> _visitsWrite = new();
    private readonly Mock<IReadRepository<ExaminationTemplate>> _templates = new();

    public SetVisitPreparationCommandHandlerTests()
        => _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);

    private SetVisitPreparationCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _visitsRead.Object,
            _visitsWrite.Object,
            _templates.Object,
            VeterinarianReaderMock.Create().Object);

    private Visit ReturnVisit()
    {
        var visit = VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId);
        _visitsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VisitByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(visit);
        return visit;
    }

    private ExaminationTemplate ReturnTemplate(bool active = true)
    {
        var template = ExaminationTemplate.CreateForTenant(_tenantId, "Kulak paketi", "Kulak kaşıntısı", null, null, [], DateTime.UtcNow).Value!;
        if (!active)
            template.Deactivate(DateTime.UtcNow);
        _templates.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationTemplateVisibleByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        return template;
    }

    [Fact]
    public async Task Handle_Should_Set_Reason_And_Template_And_Save()
    {
        var visit = ReturnVisit();
        var template = ReturnTemplate();

        var result = await CreateHandler().Handle(
            new SetVisitPreparationCommand(visit.Id, "Kulak kaşıntısı", template.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ArrivalReason.Should().Be("Kulak kaşıntısı");
        result.Value.PreparedExaminationTemplateId.Should().Be(template.Id);
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Be_Idempotent_And_Clear_With_Nulls()
    {
        var visit = ReturnVisit();
        var template = ReturnTemplate();
        await CreateHandler().Handle(new SetVisitPreparationCommand(visit.Id, "Aşı", template.Id), CancellationToken.None);

        await CreateHandler().Handle(new SetVisitPreparationCommand(visit.Id, "Aşı", template.Id), CancellationToken.None);
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        var cleared = await CreateHandler().Handle(new SetVisitPreparationCommand(visit.Id, null, null), CancellationToken.None);
        cleared.Value!.ArrivalReason.Should().BeNull();
        cleared.Value.PreparedExaminationTemplateId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_Reject_Inactive_New_Template_But_Keep_Already_Prepared_One()
    {
        var visit = ReturnVisit();
        var template = ReturnTemplate();
        await CreateHandler().Handle(new SetVisitPreparationCommand(visit.Id, null, template.Id), CancellationToken.None);

        template.Deactivate(DateTime.UtcNow);

        // Aynı (artık pasif) şablon kalır, yalnız neden değişir.
        var keep = await CreateHandler().Handle(new SetVisitPreparationCommand(visit.Id, "Kontrol", template.Id), CancellationToken.None);
        keep.IsSuccess.Should().BeTrue();

        var other = ReturnTemplate(active: false);
        var reject = await CreateHandler().Handle(new SetVisitPreparationCommand(visit.Id, "Kontrol", other.Id), CancellationToken.None);
        reject.Error.Code.Should().Be("Visits.Validation");
    }

    [Fact]
    public async Task Handle_Should_Return_NotOpen_For_Completed_Visit()
    {
        var visit = ReturnVisit();
        visit.Start(DateTime.UtcNow);
        visit.Complete(DateTime.UtcNow);

        var result = await CreateHandler().Handle(new SetVisitPreparationCommand(visit.Id, "Geç not", null), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.NotOpen");
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Return_NotFound_For_Other_Clinic_Context()
    {
        var visit = ReturnVisit();
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(new SetVisitPreparationCommand(visit.Id, "Aşı", null), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.NotFound");
    }
}
