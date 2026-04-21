using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Payments.Queries.ExportPaymentReport;

public sealed class ExportPaymentsReportXlsxQueryHandler
    : IRequestHandler<ExportPaymentsReportXlsxQuery, Result<PaymentsXlsxExportResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;

    public ExportPaymentsReportXlsxQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Payment> payments,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _payments = payments;
        _clients = clients;
        _pets = pets;
        _clinics = clinics;
    }

    public async Task<Result<PaymentsXlsxExportResult>> Handle(
        ExportPaymentsReportXlsxQuery request,
        CancellationToken ct)
    {
        var loaded = await PaymentsReportExportPipeline.LoadAsync(
            _tenantContext,
            _clinicContext,
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
            ct);
        if (!loaded.IsSuccess)
            return Result<PaymentsXlsxExportResult>.Failure(loaded.Error);

        var (items, fromUtc, toUtc) = loaded.Value!;
        var bytes = PaymentsXlsxWriter.WriteClinicReceiptWorkbook(items);
        var fileName = $"tahsilat-raporu-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.xlsx";

        return Result<PaymentsXlsxExportResult>.Success(new PaymentsXlsxExportResult(bytes, fileName));
    }
}
