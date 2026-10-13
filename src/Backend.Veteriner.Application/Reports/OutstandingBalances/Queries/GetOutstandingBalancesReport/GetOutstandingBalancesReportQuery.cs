using Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.OutstandingBalances.Queries.GetOutstandingBalancesReport;

/// <summary>Şu anki kalan borç (tarih parametresi yok). <c>MinAmount</c>: kalan tutar alt sınırı (dahil).</summary>
public sealed record GetOutstandingBalancesReportQuery(
    Guid? ClinicId,
    decimal? MinAmount,
    string? Search,
    int Page,
    int PageSize) : IRequest<Result<OutstandingBalancesReportResultDto>>;
