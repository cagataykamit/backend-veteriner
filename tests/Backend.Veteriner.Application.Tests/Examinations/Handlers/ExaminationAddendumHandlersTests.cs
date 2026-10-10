using System.Text.Json;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Commands.AddAddendum;
using Backend.Veteriner.Application.Examinations.Commands.Create;
using Backend.Veteriner.Application.Examinations.Commands.Update;
using Backend.Veteriner.Application.Examinations.Queries.GetAddenda;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Tests.Visits;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Examinations.Handlers;

public sealed class ExaminationAddendumHandlersTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IRepository<ExaminationAddendum>> _addendaWrite = new();
    private readonly Mock<IReadRepository<ExaminationAddendum>> _addendaRead = new();

    public ExaminationAddendumHandlersTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clientContext.SetupGet(c => c.UserId).Returns(_userId);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
    }

    private AddExaminationAddendumCommandHandler CreateAddHandler(
        IReadOnlyDictionary<Guid, string?>? names = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _scopeResolver.Object,
            _tenants.Object,
            _examinations.Object,
            _addendaWrite.Object,
            VeterinarianReaderMock.Create(names).Object,
            new FixedClock(Now));

    private GetExaminationAddendaQueryHandler CreateGetHandler(
        IReadOnlyDictionary<Guid, string?>? names = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _examinations.Object,
            _addendaRead.Object,
            VeterinarianReaderMock.Create(names).Object);

    private Examination ReturnExamination(bool finalized)
    {
        var e = new Examination(_tenantId, _clinicId, Guid.NewGuid(), null, Now.AddHours(-1), "Sikayet", "Bulgu", null, null);
        if (finalized)
            e.Finalize(_userId, Now.AddMinutes(-30));
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(e);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(e);
        return e;
    }

    [Fact]
    public async Task Add_Should_Persist_Addendum_With_User_And_Time_And_Return_Name()
    {
        var e = ReturnExamination(finalized: true);
        ExaminationAddendum? saved = null;
        _addendaWrite.Setup(r => r.AddAsync(It.IsAny<ExaminationAddendum>(), It.IsAny<CancellationToken>()))
            .Callback<ExaminationAddendum, CancellationToken>((a, _) => saved = a)
            .ReturnsAsync((ExaminationAddendum a, CancellationToken _) => a);

        var result = await CreateAddHandler(new Dictionary<Guid, string?> { [_userId] = "Dr. Ayşe" })
            .Handle(new AddExaminationAddendumCommand(e.Id, "Kilo hatalı girildi", "Doğru kilo 12,4 kg."), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.ExaminationId.Should().Be(e.Id);
        saved.CreatedByUserId.Should().Be(_userId);
        saved.CreatedAtUtc.Should().Be(Now);
        result.Value!.CreatedByName.Should().Be("Dr. Ayşe");
        result.Value.Text.Should().Be("Doğru kilo 12,4 kg.");
        _addendaWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Add_Should_Fail_NotFinalized_And_Not_Save_For_Draft()
    {
        var e = ReturnExamination(finalized: false);

        var result = await CreateAddHandler()
            .Handle(new AddExaminationAddendumCommand(e.Id, "Yeterli gerekçe", "Metin"), CancellationToken.None);

        result.Error.Code.Should().Be("Examinations.NotFinalized");
        _addendaWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("", "Metin")]
    [InlineData("kısa", "Metin")]
    [InlineData("Yeterli gerekçe", "  ")]
    public async Task Add_Should_Fail_Validation_When_Reason_Or_Text_Invalid(string reason, string text)
    {
        var e = ReturnExamination(finalized: true);

        var result = await CreateAddHandler()
            .Handle(new AddExaminationAddendumCommand(e.Id, reason, text), CancellationToken.None);

        result.Error.Code.Should().Be("Examinations.Validation");
        _addendaWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Add_Should_Fail_NotFound_When_Examination_Missing()
    {
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Examination?)null);

        var result = await CreateAddHandler()
            .Handle(new AddExaminationAddendumCommand(Guid.NewGuid(), "Yeterli gerekçe", "Metin"), CancellationToken.None);

        result.Error.Code.Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task Add_Should_Fail_When_Clinic_Not_Assigned_To_User()
    {
        var e = ReturnExamination(finalized: true);
        _scopeResolver.SetupAccessDenied();

        var result = await CreateAddHandler()
            .Handle(new AddExaminationAddendumCommand(e.Id, "Yeterli gerekçe", "Metin"), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _addendaWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_Should_Return_Addenda_With_Names()
    {
        var e = ReturnExamination(finalized: true);
        var first = e.AddAddendum(_userId, "Birinci gerekçe", "Birinci", Now).Value!;
        var second = e.AddAddendum(Guid.NewGuid(), "İkinci gerekçe", "İkinci", Now.AddMinutes(5)).Value!;
        _addendaRead.Setup(r => r.ListAsync(It.IsAny<ExaminationAddendaByExaminationIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);

        var result = await CreateGetHandler(new Dictionary<Guid, string?> { [_userId] = "Dr. Ayşe" })
            .Handle(new GetExaminationAddendaQuery(e.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(a => a.Text).Should().Equal("Birinci", "İkinci");
        result.Value![0].CreatedByName.Should().Be("Dr. Ayşe");
        result.Value[1].CreatedByName.Should().BeNull();
    }

    [Fact]
    public async Task Get_Should_Return_Empty_For_Draft_Without_Name_Lookup()
    {
        var e = ReturnExamination(finalized: false);
        _addendaRead.Setup(r => r.ListAsync(It.IsAny<ExaminationAddendaByExaminationIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateGetHandler().Handle(new GetExaminationAddendaQuery(e.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_Should_Fail_NotFound_When_Missing_Or_Other_Clinic_Context()
    {
        var e = ReturnExamination(finalized: true);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        var result = await CreateGetHandler().Handle(new GetExaminationAddendaQuery(e.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task Get_Should_Fail_When_Clinic_Not_Assigned_To_User()
    {
        var e = ReturnExamination(finalized: true);
        _scopeResolver.SetupAccessDenied();

        var result = await CreateGetHandler().Handle(new GetExaminationAddendaQuery(e.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public void Examination_Commands_Should_Be_Auditable_Without_Clinical_Text_In_Payload()
    {
        var id = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var add = new AddExaminationAddendumCommand(id, "GIZLI-GEREKCE", "GIZLI-EK-NOT");
        var create = new CreateExaminationCommand(
            Guid.NewGuid(), petId, null, Now, "GIZLI-SIKAYET", "GIZLI-BULGU", "GIZLI-DEGERLENDIRME", "GIZLI-NOT",
            "GIZLI-ANAMNEZ", "GIZLI-PLAN", WeightKg: 4.2m);
        var update = new UpdateExaminationCommand(
            id, null, null, null, Now, "GIZLI-SIKAYET", "GIZLI-BULGU", "GIZLI-DEGERLENDIRME", "GIZLI-NOT", "rv",
            "GIZLI-ANAMNEZ", "GIZLI-PLAN");

        add.AuditAction.Should().Be("Examination.AddAddendum");
        add.AuditTarget.Should().Be($"ExaminationId={id}");
        create.AuditAction.Should().Be("Examination.Create");
        create.AuditTarget.Should().Be($"PetId={petId}");
        update.AuditAction.Should().Be("Examination.Update");
        update.AuditTarget.Should().Be($"ExaminationId={id}");

        foreach (var payload in new[]
                 {
                     JsonSerializer.Serialize(add, options),
                     JsonSerializer.Serialize(create, options),
                     JsonSerializer.Serialize(update, options),
                 })
        {
            payload.Should().NotContain("GIZLI");
        }

        JsonSerializer.Serialize(create, options).Should().Contain("weightKg");
        JsonSerializer.Serialize(update, options).Should().Contain(id.ToString());
        add.Should().BeAssignableTo<IAuditableRequest>();
        create.Should().BeAssignableTo<IAuditableRequest>();
        update.Should().BeAssignableTo<IAuditableRequest>();
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
