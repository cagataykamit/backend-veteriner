using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Examinations;
using Backend.Veteriner.Application.Reports.Pdf;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Examinations.Queries.ExportExaminationReport;

public sealed class ExportExaminationsReportPdfQueryHandler
    : IRequestHandler<ExportExaminationsReportPdfQuery, Result<RegisterExportResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly ITrialLimitPolicy _trialLimitPolicy;
    private readonly TimeProvider _timeProvider;

    public ExportExaminationsReportPdfQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Examination> examinations,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        ITrialLimitPolicy trialLimitPolicy,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _examinations = examinations;
        _clients = clients;
        _pets = pets;
        _clinics = clinics;
        _trialLimitPolicy = trialLimitPolicy;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<RegisterExportResult>> Handle(
        ExportExaminationsReportPdfQuery request,
        CancellationToken ct)
    {
        var loaded = await ExaminationsReportExportPipeline.LoadAsync(
            _tenantContext,
            _clinicContext,
            _clinicScopeResolver,
            _examinations,
            _clients,
            _pets,
            _clinics,
            request.FromUtc,
            request.ToUtc,
            request.ClinicId,
            request.ClientId,
            request.PetId,
            request.AppointmentId,
            request.Search,
            _trialLimitPolicy,
            RegisterExportFormat.Pdf,
            ct);
        if (!loaded.IsSuccess)
            return Result<RegisterExportResult>.Failure(loaded.Error);

        var (items, fromUtc, toUtc) = loaded.Value!;
        var bytes = ReportPdfWriter.Write(
            new ReportPdfHeader(ExaminationsPdfColumns.Title, ReportPdfHeader.PeriodText(fromUtc, toUtc), _timeProvider.GetUtcNow().UtcDateTime),
            ExaminationsPdfColumns.All,
            items);
        var fileName = $"muayene-raporu-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.pdf";

        return Result<RegisterExportResult>.Success(new RegisterExportResult(bytes, fileName));
    }
}
