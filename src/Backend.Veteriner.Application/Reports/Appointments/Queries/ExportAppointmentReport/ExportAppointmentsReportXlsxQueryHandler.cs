using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Appointments;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Appointments.Queries.ExportAppointmentReport;

public sealed class ExportAppointmentsReportXlsxQueryHandler
    : IRequestHandler<ExportAppointmentsReportXlsxQuery, Result<AppointmentsXlsxExportResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;

    public ExportAppointmentsReportXlsxQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointments,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _appointments = appointments;
        _clients = clients;
        _pets = pets;
        _clinics = clinics;
    }

    public async Task<Result<AppointmentsXlsxExportResult>> Handle(
        ExportAppointmentsReportXlsxQuery request,
        CancellationToken ct)
    {
        var loaded = await AppointmentsReportExportPipeline.LoadAsync(
            _tenantContext,
            _clinicContext,
            _appointments,
            _clients,
            _pets,
            _clinics,
            request.FromUtc,
            request.ToUtc,
            request.ClinicId,
            request.Status,
            request.ClientId,
            request.PetId,
            request.Search,
            ct);
        if (!loaded.IsSuccess)
            return Result<AppointmentsXlsxExportResult>.Failure(loaded.Error);

        var (items, fromUtc, toUtc) = loaded.Value!;
        var bytes = AppointmentsXlsxWriter.WriteReportWorkbook(items);
        var fileName = $"randevu-raporu-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.xlsx";

        return Result<AppointmentsXlsxExportResult>.Success(new AppointmentsXlsxExportResult(bytes, fileName));
    }
}
