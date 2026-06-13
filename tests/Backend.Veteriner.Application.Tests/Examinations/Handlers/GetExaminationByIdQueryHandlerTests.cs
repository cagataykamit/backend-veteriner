using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Queries.GetById;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Examinations.Handlers;

public sealed class GetExaminationByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private readonly Guid _userId = Guid.NewGuid();

    public GetExaminationByIdQueryHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
        _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
    }

    private GetExaminationByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _userOperationClaims.Object,
            _userClinics.Object,
            _examinations.Object,
            _pets.Object,
            _clients.Object);

    private void SetupTenant(Guid tenantId) => _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

    private static Examination BuildExamination(Guid tenantId, Guid clinicId, Guid petId, Guid? examinationId = null)
    {
        var entity = new Examination(
            tenantId,
            clinicId,
            petId,
            null,
            DateTime.UtcNow.AddHours(-2),
            "Şikayet",
            "Bulgu",
            null,
            null);
        if (examinationId.HasValue)
            typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(entity, examinationId.Value);
        return entity;
    }

    private void SetupExaminationFound(Examination entity)
        => _examinations.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

    private void SetupTenantWideRole(params string[] claimNames)
        => _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimNames);

    private void SetupAssignment(Guid clinicId, bool assigned)
        => _userClinics.Setup(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assigned);

    private void SetupDetailGraph(Guid tenantId, Guid petId, Guid clientId)
    {
        var pet = new Pet(tenantId, clientId, "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);
        var client = new Client(tenantId, "Ali Veli");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);

        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetExaminationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _examinations.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Examination>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_UserContextMissing()
    {
        SetupTenant(Guid.NewGuid());
        _clientContext.SetupGet(x => x.UserId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetExaminationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.UserContextMissing");
        _examinations.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Examination>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_NoRowForTenant()
    {
        var tid = Guid.NewGuid();
        SetupTenant(tid);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Examination?)null);

        var result = await CreateHandler().Handle(new GetExaminationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_ExaminationBelongsToOtherTenant()
    {
        SetupTenant(Guid.NewGuid());
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Examination?)null);

        var result = await CreateHandler().Handle(new GetExaminationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_TenantWideUserReadsWithoutActiveClinicContext()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupTenantWideRole("Admin");
        SetupExaminationFound(BuildExamination(tid, clinicId, petId, examId));
        SetupDetailGraph(tid, petId, clientId);

        var result = await CreateHandler().Handle(new GetExaminationByIdQuery(examId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(examId);
        _userClinics.Verify(
            x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_AssignedNonTenantWideUserReadsOwnClinicExamination()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        SetupAssignment(clinicId, assigned: true);
        var entity = BuildExamination(tid, clinicId, petId, examId);
        SetupExaminationFound(entity);
        SetupDetailGraph(tid, petId, clientId);

        var result = await CreateHandler().Handle(new GetExaminationByIdQuery(examId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(clinicId);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_NonTenantWideUserNotAssignedToExaminationClinic()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        SetupAssignment(clinicId, assigned: false);
        SetupExaminationFound(BuildExamination(tid, clinicId, petId, examId));

        var result = await CreateHandler().Handle(new GetExaminationByIdQuery(examId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFound");
        _pets.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Pet>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_ActiveClinicContextDiffersFromExaminationClinic()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupTenantWideRole("Owner");
        SetupExaminationFound(BuildExamination(tid, clinicId, petId, examId));

        var result = await CreateHandler().Handle(new GetExaminationByIdQuery(examId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFound");
        _userOperationClaims.Verify(
            x => x.GetOperationClaimNamesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _pets.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Pet>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_Found()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        SetupTenant(tid);
        SetupTenantWideRole("PlatformAdmin");
        var entity = BuildExamination(tid, clinicId, petId);
        SetupExaminationFound(entity);
        SetupDetailGraph(tid, petId, clientId);

        var result = await CreateHandler().Handle(new GetExaminationByIdQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(entity.Id);
        result.Value.TenantId.Should().Be(tid);
        result.Value.VisitReason.Should().Be("Şikayet");
        result.Value.PetName.Should().Be("Pamuk");
        result.Value.ClientId.Should().Be(clientId);
        result.Value.ClientName.Should().Be("Ali Veli");
        result.Value.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        result.Value.UpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task CancellationToken_Should_BePassed_ToRepositoryCalls()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: true);
        SetupExaminationFound(BuildExamination(tenantId, clinicId, petId, examId));
        SetupDetailGraph(tenantId, petId, Guid.NewGuid());

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await CreateHandler().Handle(new GetExaminationByIdQuery(examId), token);

        _examinations.Verify(x => x.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), token), Times.Once);
        _userOperationClaims.Verify(x => x.GetOperationClaimNamesByUserIdAsync(_userId, token), Times.Once);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, token), Times.Once);
    }
}
