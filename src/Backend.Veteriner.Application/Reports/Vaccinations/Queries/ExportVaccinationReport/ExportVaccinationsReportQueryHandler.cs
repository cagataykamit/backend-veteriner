using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Vaccinations;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Vaccinations.Queries.ExportVaccinationReport;

public sealed class ExportVaccinationsReportQueryHandler
    : IRequestHandler<ExportVaccinationsReportQuery, Result<VaccinationsCsvExportResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;

    public ExportVaccinationsReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _vaccinations = vaccinations;
        _clients = clients;
        _pets = pets;
        _clinics = clinics;
    }

    public async Task<Result<VaccinationsCsvExportResult>> Handle(
        ExportVaccinationsReportQuery request,
        CancellationToken ct)
    {
        var loaded = await VaccinationsReportExportPipeline.LoadAsync(
            _tenantContext,
            _clinicContext,
            _vaccinations,
            _clients,
            _pets,
            _clinics,
            request.FromUtc,
            request.ToUtc,
            request.ClinicId,
            request.ClientId,
            request.PetId,
            request.Status,
            request.Search,
            ct);
        if (!loaded.IsSuccess)
            return Result<VaccinationsCsvExportResult>.Failure(loaded.Error);

        var (items, fromUtc, toUtc) = loaded.Value!;
        var bytes = VaccinationsCsvWriter.WriteReportUtf8Bom(items);
        var fileName = $"asi-raporu-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.csv";

        return Result<VaccinationsCsvExportResult>.Success(new VaccinationsCsvExportResult(bytes, fileName));
    }
}
