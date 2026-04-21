using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Appointments.Queries.ExportAppointmentReport;

public sealed record ExportAppointmentsReportXlsxQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    AppointmentStatus? Status,
    Guid? ClientId,
    Guid? PetId,
    string? Search) : IRequest<Result<AppointmentsXlsxExportResult>>;

public sealed record AppointmentsXlsxExportResult(byte[] Content, string FileDownloadName);
