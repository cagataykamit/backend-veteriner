using Ardalis.Specification;
using Backend.Veteriner.Application.AppointmentRequests.Staff;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments;

/// <summary>
/// Kaynak özeti (TIER-016): kaynak başına talep ve onaylanan sayısı, "bilinmiyor" sonda, talep gelmeyen kaynak satırı yok; tarih
/// aralığı kuralı ve klinik kapsamı kuyrukla aynı.
/// </summary>
public sealed class AppointmentRequestChannelSummaryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();

    private readonly Mock<IReadRepository<AppointmentRequest>> _requests = new();
    private readonly Mock<IClinicReadScopeResolver> _scope = new();
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();

    public AppointmentRequestChannelSummaryTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(TenantId);
        _clinic.SetupGet(c => c.ClinicId).Returns(ClinicId);
        _scope.Setup(s => s.ResolveAsync(TenantId, ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Success(new ClinicReadScope(ClinicId, null)));
    }

    private GetAppointmentRequestChannelSummaryQueryHandler Handler()
        => new(
            new StaffAppointmentRequestAccess(_tenant.Object, _clinic.Object, _scope.Object, new Mock<IRepository<AppointmentRequest>>().Object, _requests.Object),
            _requests.Object);

    private void Rows(params (AppointmentRequestChannel? Channel, AppointmentRequestStatus Status)[] rows)
        => _requests.Setup(r => r.ListAsync(It.IsAny<ISpecification<AppointmentRequest, AppointmentRequestChannelStatusRow>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows.Select(r => new AppointmentRequestChannelStatusRow(r.Channel, r.Status)).ToList());

    [Fact]
    public async Task Should_Count_Total_And_Approved_Per_Channel_With_Unknown_Last()
    {
        Rows(
            (null, AppointmentRequestStatus.Approved),
            (AppointmentRequestChannel.OwnerPortal, AppointmentRequestStatus.Pending),
            (AppointmentRequestChannel.ClinicSite, AppointmentRequestStatus.Approved),
            (AppointmentRequestChannel.ClinicSite, AppointmentRequestStatus.Rejected),
            (AppointmentRequestChannel.ClinicSite, AppointmentRequestStatus.Approved),
            (AppointmentRequestChannel.ClinicLink, AppointmentRequestStatus.Expired));

        var result = await Handler().Handle(new GetAppointmentRequestChannelSummaryQuery(null, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), default);

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        result.Value!.Rows.Select(r => (r.Channel, r.Total, r.Approved)).Should().Equal(
            (AppointmentRequestChannel.ClinicLink, 1, 0),
            (AppointmentRequestChannel.ClinicSite, 3, 2),
            (AppointmentRequestChannel.OwnerPortal, 1, 0),
            ((AppointmentRequestChannel?)null, 1, 1));
        result.Value.Total.Should().Be(6);
        result.Value.Approved.Should().Be(3);
        result.Value.From.Should().Be(new DateOnly(2026, 11, 1));
    }

    [Fact]
    public async Task Should_Return_Empty_Rows_When_No_Request()
    {
        Rows();

        var result = await Handler().Handle(new GetAppointmentRequestChannelSummaryQuery(null, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1)), default);

        result.Value!.Rows.Should().BeEmpty();
        result.Value.Total.Should().Be(0);
    }

    [Fact]
    public async Task Should_Reject_Reversed_Range_Without_Querying()
    {
        var result = await Handler().Handle(new GetAppointmentRequestChannelSummaryQuery(null, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 1)), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Registers.DateRangeInvalid");
        _requests.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_Reject_Clinic_Other_Than_Active_Context()
    {
        var result = await Handler().Handle(new GetAppointmentRequestChannelSummaryQuery(Guid.NewGuid(), new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2)), default);

        result.IsSuccess.Should().BeFalse();
        _requests.VerifyNoOtherCalls();
    }
}
