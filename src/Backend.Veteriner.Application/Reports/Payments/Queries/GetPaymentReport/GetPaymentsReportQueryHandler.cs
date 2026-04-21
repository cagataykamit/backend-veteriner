using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Payments.Queries.GetPaymentReport;

public sealed class GetPaymentsReportQueryHandler
    : IRequestHandler<GetPaymentsReportQuery, Result<PaymentReportResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;

    public GetPaymentsReportQueryHandler(
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

    public async Task<Result<PaymentReportResultDto>> Handle(
        GetPaymentsReportQuery request,
        CancellationToken ct)
    {
        var validated = await PaymentsReportQueryValidation.ValidateAsync(
            _tenantContext,
            _clinicContext,
            _clinics,
            request.ClinicId,
            request.FromUtc,
            request.ToUtc,
            ct);
        if (!validated.IsSuccess)
            return Result<PaymentReportResultDto>.Failure(validated.Error);

        var (tenantId, effectiveClinicId, fromUtc, toUtc) = validated.Value;

        var (searchPattern, searchClientIds, searchPetIds) =
            await PaymentsReportSearchResolution.ResolveSearchAsync(tenantId, request.Search, _clients, _pets, ct);

        var total = await _payments.CountAsync(
            new PaymentsFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.ClientId,
                request.PetId,
                request.Method,
                fromUtc,
                toUtc,
                searchPattern,
                searchClientIds,
                searchPetIds),
            ct);

        var amountRows = await _payments.ListAsync(
            new PaymentsFilteredAmountsSpec(
                tenantId,
                effectiveClinicId,
                request.ClientId,
                request.PetId,
                request.Method,
                fromUtc,
                toUtc,
                searchPattern,
                searchClientIds,
                searchPetIds),
            ct);

        var totalAmount = amountRows.Sum();

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, PaymentsReportConstants.MaxPageSize);

        var rows = await _payments.ListAsync(
            new PaymentsFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.ClientId,
                request.PetId,
                request.Method,
                fromUtc,
                toUtc,
                page,
                pageSize,
                searchPattern,
                searchClientIds,
                searchPetIds),
            ct);

        var items = await PaymentsReportItemMapping.MapAsync(tenantId, rows, _clients, _pets, _clinics, ct);

        return Result<PaymentReportResultDto>.Success(
            new PaymentReportResultDto(total, totalAmount, items));
    }
}
