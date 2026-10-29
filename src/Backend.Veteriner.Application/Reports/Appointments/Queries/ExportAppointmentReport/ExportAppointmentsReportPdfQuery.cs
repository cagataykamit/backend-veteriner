using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Appointments.Queries.ExportAppointmentReport;

public sealed record ExportAppointmentsReportPdfQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    AppointmentStatus? Status,
    Guid? ClientId,
    Guid? PetId,
    string? Search) : IRequest<Result<RegisterExportResult>>;
