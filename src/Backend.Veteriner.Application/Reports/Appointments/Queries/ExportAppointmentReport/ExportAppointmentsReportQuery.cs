using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Appointments.Queries.ExportAppointmentReport;

public sealed record ExportAppointmentsReportQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    AppointmentStatus? Status,
    Guid? ClientId,
    Guid? PetId,
    string? Search) : IRequest<Result<AppointmentsCsvExportResult>>;

public sealed record AppointmentsCsvExportResult(byte[] ContentUtf8Bom, string FileDownloadName);
