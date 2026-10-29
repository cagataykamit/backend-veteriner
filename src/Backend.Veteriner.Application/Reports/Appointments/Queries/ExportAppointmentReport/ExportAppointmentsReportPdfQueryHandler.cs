using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Appointments;
using Backend.Veteriner.Application.Reports.Pdf;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Appointments.Queries.ExportAppointmentReport;

public sealed class ExportAppointmentsReportPdfQueryHandler
    : IRequestHandler<ExportAppointmentsReportPdfQuery, Result<RegisterExportResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly ITrialLimitPolicy _trialLimitPolicy;
    private readonly TimeProvider _timeProvider;

    public ExportAppointmentsReportPdfQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Appointment> appointments,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        ITrialLimitPolicy trialLimitPolicy,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _appointments = appointments;
        _clients = clients;
        _pets = pets;
        _clinics = clinics;
        _trialLimitPolicy = trialLimitPolicy;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<RegisterExportResult>> Handle(
        ExportAppointmentsReportPdfQuery request,
        CancellationToken ct)
    {
        var loaded = await AppointmentsReportExportPipeline.LoadAsync(
            _tenantContext,
            _clinicContext,
            _clinicScopeResolver,
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
            _trialLimitPolicy,
            RegisterExportFormat.Pdf,
            ct);
        if (!loaded.IsSuccess)
            return Result<RegisterExportResult>.Failure(loaded.Error);

        var (items, fromUtc, toUtc) = loaded.Value!;
        var bytes = ReportPdfWriter.Write(
            new ReportPdfHeader(AppointmentsPdfColumns.Title, ReportPdfHeader.PeriodText(fromUtc, toUtc), _timeProvider.GetUtcNow().UtcDateTime),
            AppointmentsPdfColumns.All,
            items);
        var fileName = $"randevu-raporu-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.pdf";

        return Result<RegisterExportResult>.Success(new RegisterExportResult(bytes, fileName));
    }
}
