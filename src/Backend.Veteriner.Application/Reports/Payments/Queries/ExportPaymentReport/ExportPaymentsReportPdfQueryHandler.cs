using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Application.Reports.Payments.ReadModels;
using Backend.Veteriner.Application.Reports.Pdf;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Reports.Payments.Queries.ExportPaymentReport;

public sealed class ExportPaymentsReportPdfQueryHandler
    : IRequestHandler<ExportPaymentsReportPdfQuery, Result<RegisterExportResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IClientReadModelLookupReader _clientLookupReader;
    private readonly IPetReadModelLookupReader _petLookupReader;
    private readonly IPaymentsReportExportReadModelReader _exportReadModelReader;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;
    private readonly ITrialLimitPolicy _trialLimitPolicy;
    private readonly TimeProvider _timeProvider;

    public ExportPaymentsReportPdfQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Payment> payments,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        IClientReadModelLookupReader clientLookupReader,
        IPetReadModelLookupReader petLookupReader,
        IPaymentsReportExportReadModelReader exportReadModelReader,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions,
        ITrialLimitPolicy trialLimitPolicy,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _payments = payments;
        _clients = clients;
        _pets = pets;
        _clinics = clinics;
        _clientLookupReader = clientLookupReader;
        _petLookupReader = petLookupReader;
        _exportReadModelReader = exportReadModelReader;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
        _trialLimitPolicy = trialLimitPolicy;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<RegisterExportResult>> Handle(
        ExportPaymentsReportPdfQuery request,
        CancellationToken ct)
    {
        var loaded = await PaymentsReportExportPipeline.LoadAsync(
            _tenantContext,
            _clinicContext,
            _clinicScopeResolver,
            _payments,
            _clients,
            _pets,
            _clinics,
            request.FromUtc,
            request.ToUtc,
            request.ClinicId,
            request.Method,
            request.ClientId,
            request.PetId,
            request.Search,
            _queryReadModelsOptions.PaymentsSearchLookupEnabled,
            _queryReadModelsOptions.PaymentsReportExportReadEnabled,
            _clientLookupReader,
            _petLookupReader,
            _exportReadModelReader,
            _trialLimitPolicy,
            RegisterExportFormat.Pdf,
            ct);
        if (!loaded.IsSuccess)
            return Result<RegisterExportResult>.Failure(loaded.Error);

        var (items, fromUtc, toUtc) = loaded.Value!;
        var bytes = ReportPdfWriter.Write(
            new ReportPdfHeader(PaymentsPdfColumns.Title, ReportPdfHeader.PeriodText(fromUtc, toUtc), _timeProvider.GetUtcNow().UtcDateTime),
            PaymentsPdfColumns.All,
            items);
        var fileName = $"tahsilat-raporu-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.pdf";

        return Result<RegisterExportResult>.Success(new RegisterExportResult(bytes, fileName));
    }
}
