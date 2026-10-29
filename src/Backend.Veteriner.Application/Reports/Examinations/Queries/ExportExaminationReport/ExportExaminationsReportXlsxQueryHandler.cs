using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Examinations;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Examinations.Queries.ExportExaminationReport;

public sealed class ExportExaminationsReportXlsxQueryHandler
    : IRequestHandler<ExportExaminationsReportXlsxQuery, Result<ExaminationsXlsxExportResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly ITrialLimitPolicy _trialLimitPolicy;

    public ExportExaminationsReportXlsxQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Examination> examinations,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        ITrialLimitPolicy trialLimitPolicy)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _examinations = examinations;
        _clients = clients;
        _pets = pets;
        _clinics = clinics;
        _trialLimitPolicy = trialLimitPolicy;
    }

    public async ValueTask<Result<ExaminationsXlsxExportResult>> Handle(
        ExportExaminationsReportXlsxQuery request,
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
            RegisterExportFormat.Xlsx,
            ct);
        if (!loaded.IsSuccess)
            return Result<ExaminationsXlsxExportResult>.Failure(loaded.Error);

        var (items, fromUtc, toUtc) = loaded.Value!;
        var bytes = ExaminationsXlsxWriter.WriteReportWorkbook(items);
        var fileName = $"muayene-raporu-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.xlsx";

        return Result<ExaminationsXlsxExportResult>.Success(new ExaminationsXlsxExportResult(bytes, fileName));
    }
}
