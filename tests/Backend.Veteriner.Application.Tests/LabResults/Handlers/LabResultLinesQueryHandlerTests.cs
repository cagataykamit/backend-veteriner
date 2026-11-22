using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LabResults.Queries.GetById;
using Backend.Veteriner.Application.LabResults.Queries.GetParameterHistory;
using Backend.Veteriner.Application.LabResults.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.LabResults.Handlers;

/// <summary>REF-001 Dilim 3: detayda değer satırları ve önceki değer, parametre geçmişi.</summary>
public sealed class LabResultLinesQueryHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _petId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IUserOperationClaimRepository> _claims = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<LabResult>> _labResults = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    public LabResultLinesQueryHandlerTests()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(_tenantId);
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _claims.Setup(x => x.GetOperationClaimNamesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["Admin"]);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pet?)null);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);
    }

    private LabResult Result(DateTime resultDate, params LabResultLineDraft[] lines)
        => new(_tenantId, _clinicId, _petId, null, null, null, resultDate, "Biyokimya", null, null, null, Now, lines);

    private GetLabResultByIdQueryHandler ByIdHandler()
        => new(_tenantContext.Object, _clinicContext.Object, _clientContext.Object, _claims.Object, _userClinics.Object,
            _labResults.Object, _pets.Object, _clients.Object);

    private GetLabParameterHistoryQueryHandler HistoryHandler()
        => new(_tenantContext.Object, _clinicContext.Object, _scopeResolver.Object, _labResults.Object);

    [Fact]
    public async Task Detail_Should_Return_Lines_With_Flag_Previous_Value_And_Trend()
    {
        var current = Result(Now.AddDays(-1),
            new LabResultLineDraft("Glikoz", 140m, "mg/dL", 70m, 120m),
            new LabResultLineDraft("Üre", 30m, "mg/dL", null, null));
        var previous = Result(Now.AddDays(-30), new LabResultLineDraft("GLİKOZ", 110m, "mg/dL", 70m, 120m));
        _labResults.Setup(x => x.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        _labResults.Setup(x => x.ListAsync(It.IsAny<LabResultsWithParameterLinesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([previous]);

        var result = await ByIdHandler().Handle(new GetLabResultByIdQuery(current.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var lines = result.Value!.Lines;
        lines.Should().HaveCount(2);
        lines[0].Flag.Should().Be(LabValueFlag.High);
        lines[0].Previous!.Value.Should().Be(110m);
        lines[0].Previous!.LabResultId.Should().Be(previous.Id);
        lines[0].Previous!.Flag.Should().Be(LabValueFlag.Normal);
        lines[0].Trend.Should().Be(LabValueTrend.Up);
        lines[1].Flag.Should().BeNull();
        lines[1].Previous.Should().BeNull();
        lines[1].Trend.Should().BeNull();
    }

    [Fact]
    public async Task Detail_Should_Not_Compare_Values_With_Different_Units()
    {
        var current = Result(Now.AddDays(-1), new LabResultLineDraft("Glikoz", 7.5m, "mmol/L", null, null));
        var previous = Result(Now.AddDays(-30), new LabResultLineDraft("Glikoz", 110m, "mg/dL", null, null));
        _labResults.Setup(x => x.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        _labResults.Setup(x => x.ListAsync(It.IsAny<LabResultsWithParameterLinesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([previous]);

        var result = await ByIdHandler().Handle(new GetLabResultByIdQuery(current.Id), CancellationToken.None);

        result.Value!.Lines.Single().Previous.Should().BeNull();
    }

    [Fact]
    public async Task Detail_Without_Lines_Should_Not_Query_Previous_Values()
    {
        var textOnly = new LabResult(_tenantId, _clinicId, _petId, null, null, null, Now.AddDays(-1), "Hemogram", "Normal", null, null, Now);
        _labResults.Setup(x => x.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(textOnly);

        var result = await ByIdHandler().Handle(new GetLabResultByIdQuery(textOnly.Id), CancellationToken.None);

        result.Value!.Lines.Should().BeEmpty();
        _labResults.Verify(x => x.ListAsync(It.IsAny<LabResultsWithParameterLinesSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task History_Should_Require_Clinic_Scope()
    {
        var result = await HistoryHandler().Handle(new GetLabParameterHistoryQuery(_petId, "Glikoz"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.ClinicScopeRequired");
    }

    [Fact]
    public async Task History_Should_Return_Points_Newest_First_With_Flags()
    {
        var newer = Result(Now.AddDays(-1), new LabResultLineDraft("Glikoz", 140m, "mg/dL", 70m, 120m));
        var older = Result(Now.AddDays(-30), new LabResultLineDraft("glikoz", 60m, "mg/dL", 70m, 120m));
        _labResults.Setup(x => x.ListAsync(It.IsAny<LabResultsWithParameterLinesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([newer, older]);

        var result = await HistoryHandler().Handle(new GetLabParameterHistoryQuery(_petId, "GLİKOZ", _clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(p => p.LabResultId).Should().Equal(newer.Id, older.Id);
        result.Value!.Select(p => p.Flag).Should().Equal(LabValueFlag.High, LabValueFlag.Low);
    }
}
