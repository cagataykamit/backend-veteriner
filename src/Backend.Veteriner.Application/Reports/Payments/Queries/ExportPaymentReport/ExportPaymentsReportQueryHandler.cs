using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Application.Reports.Payments.ReadModels;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Reports.Payments.Queries.ExportPaymentReport;

public sealed class ExportPaymentsReportQueryHandler
    : IRequestHandler<ExportPaymentsReportQuery, Result<PaymentsCsvExportResult>>
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

    public ExportPaymentsReportQueryHandler(
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
        IOptions<QueryReadModelsOptions> queryReadModelsOptions)
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
    }

    public async Task<Result<PaymentsCsvExportResult>> Handle(
        ExportPaymentsReportQuery request,
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
            ct);
        if (!loaded.IsSuccess)
            return Result<PaymentsCsvExportResult>.Failure(loaded.Error);

        var (items, fromUtc, toUtc) = loaded.Value!;
        var bytes = PaymentsCsvWriter.WriteClinicReceiptReportUtf8Bom(items);
        var fileName = $"tahsilat-raporu-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.csv";

        return Result<PaymentsCsvExportResult>.Success(new PaymentsCsvExportResult(bytes, fileName));
    }
}
