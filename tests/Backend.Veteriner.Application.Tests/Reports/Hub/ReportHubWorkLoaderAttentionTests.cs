using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Reports.Appointments;
using Backend.Veteriner.Application.Reports.Hub;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Registers.Hbs;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Hub;

/// <summary>
/// "Bakmanız gerekenler" bandında gecikmiş randevu (B-02): Bugün'ün <c>overdueAppointmentCount</c>'u ile aynı okuyucu, pencere ve izin.
/// </summary>
public sealed class ReportHubWorkLoaderAttentionTests
{
    // Cuma 2026-11-20 12:30 İstanbul.
    private static readonly DateTime NowUtc = new(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IVisitOpenItemsReader> _openItems = new();
    private OpenItemsSummaryRequest? _captured;

    public ReportHubWorkLoaderAttentionTests()
    {
        _openItems.Setup(r => r.GetSummaryAsync(It.IsAny<OpenItemsSummaryRequest>(), It.IsAny<CancellationToken>()))
            .Callback((OpenItemsSummaryRequest r, CancellationToken _) => _captured = r)
            .ReturnsAsync(new OpenItemsSummaryDto(2, null, null, null, 0, OverdueAppointmentCount: 4));
    }

    private void Grant(params string[] permissions)
    {
        foreach (var permission in permissions)
            _permissions.Setup(p => p.HasPermission(permission)).Returns(true);
    }

    private Task<ReportHubAttentionDto> LoadAsync()
        => new ReportHubWorkLoader(
                _permissions.Object,
                Mock.Of<IReadRepository<Appointment>>(),
                Mock.Of<IAppointmentsReportStatusBreakdownReader>(),
                Mock.Of<IReadRepository<Examination>>(),
                Mock.Of<IReadRepository<Vaccination>>(),
                Mock.Of<IReadRepository<Pet>>(),
                Mock.Of<IReadRepository<ProductStock>>(),
                _openItems.Object,
                Mock.Of<IRegisterReader<HbsPrescriptionRegisterItemDto>>(),
                Mock.Of<IRegisterReader<HbsVaccinationRegisterItemDto>>())
            .LoadAttentionAsync(_tenantId, _clinicId, NowUtc, null, CancellationToken.None);

    [Fact]
    public async Task Overdue_Appointments_Should_Use_The_Today_Window_And_Uncharged_The_Day_Window()
    {
        Grant(PermissionCatalog.Visits.Read, PermissionCatalog.Appointments.Read, PermissionCatalog.Charges.Read);

        var attention = await LoadAsync();

        attention.OverdueAppointmentCount.Should().Be(4);
        attention.EndOfDayUnchargedVisitCount.Should().Be(2);
        _openItems.Verify(r => r.GetSummaryAsync(It.IsAny<OpenItemsSummaryRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        var today = OpenItemsSummaryRequest.ForToday(_captured!.Scope, NowUtc);
        (_captured.OverdueAppointmentsFromUtc, _captured.OverdueAppointmentsBeforeUtc)
            .Should().Be((today.OverdueAppointmentsFromUtc, today.OverdueAppointmentsBeforeUtc));
        var (dayStartUtc, dayEndUtc) = OperationDayBounds.ForUtcNow(NowUtc);
        (_captured.UnchargedFromUtc, _captured.UnchargedToUtc).Should().Be((dayStartUtc, dayEndUtc));
        _captured.HbsFromUtc.Should().BeNull("HBS sayıları HBS takip listesinden okunur");
        _captured.Scope.Sees(OpenItemSources.Appointments).Should().BeTrue();
    }

    [Fact]
    public async Task Overdue_Appointments_Should_Be_Null_Without_Appointments_Read_And_Uncharged_Without_Charges_Read()
    {
        Grant(PermissionCatalog.Visits.Read, PermissionCatalog.Charges.Read);
        (await LoadAsync()).OverdueAppointmentCount.Should().BeNull();

        var onlyAppointments = new ReportHubWorkLoaderAttentionTests();
        onlyAppointments.Grant(PermissionCatalog.Visits.Read, PermissionCatalog.Appointments.Read);
        var attention = await onlyAppointments.LoadAsync();
        attention.OverdueAppointmentCount.Should().Be(4);
        attention.EndOfDayUnchargedVisitCount.Should().BeNull("ücret okuma izni yok");
    }

    [Fact]
    public async Task Open_Items_Should_Not_Be_Read_Without_Visits_Read()
    {
        Grant(PermissionCatalog.Appointments.Read, PermissionCatalog.Charges.Read);

        var attention = await LoadAsync();

        attention.OverdueAppointmentCount.Should().BeNull("Bugün'ü göremeyen Bugün'ün çipini de görmez");
        attention.EndOfDayUnchargedVisitCount.Should().BeNull();
        _openItems.Verify(r => r.GetSummaryAsync(It.IsAny<OpenItemsSummaryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
