using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Examinations;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Examinations.Queries.ExportExaminationReport;

public sealed class ExportExaminationsReportXlsxQueryHandler
    : IRequestHandler<ExportExaminationsReportXlsxQuery, Result<ExaminationsXlsxExportResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;

    public ExportExaminationsReportXlsxQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Examination> examinations,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _examinations = examinations;
        _clients = clients;
        _pets = pets;
        _clinics = clinics;
    }

    public async Task<Result<ExaminationsXlsxExportResult>> Handle(
        ExportExaminationsReportXlsxQuery request,
        CancellationToken ct)
    {
        var loaded = await ExaminationsReportExportPipeline.LoadAsync(
            _tenantContext,
            _clinicContext,
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
            ct);
        if (!loaded.IsSuccess)
            return Result<ExaminationsXlsxExportResult>.Failure(loaded.Error);

        var (items, fromUtc, toUtc) = loaded.Value!;
        var bytes = ExaminationsXlsxWriter.WriteReportWorkbook(items);
        var fileName = $"muayene-raporu-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.xlsx";

        return Result<ExaminationsXlsxExportResult>.Success(new ExaminationsXlsxExportResult(bytes, fileName));
    }
}
