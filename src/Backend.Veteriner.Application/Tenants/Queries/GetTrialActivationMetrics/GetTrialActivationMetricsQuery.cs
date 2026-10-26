using Backend.Veteriner.Application.Tenants.Activation;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Tenants.Queries.GetTrialActivationMetrics;

/// <summary>
/// Platform yöneticisi: dönemde deneme başlatan kiracıların aktivasyon özeti (TRIAL-008, sözleşme §3).
/// <paramref name="FromUtc"/> yoksa <c>ToUtc − 30 gün</c>, <paramref name="ToUtc"/> yoksa istek anı.
/// </summary>
public sealed record GetTrialActivationMetricsQuery(DateTime? FromUtc, DateTime? ToUtc) : IRequest<Result<TrialActivationMetricsDto>>;

public sealed class GetTrialActivationMetricsQueryHandler : IRequestHandler<GetTrialActivationMetricsQuery, Result<TrialActivationMetricsDto>>
{
    public const int DefaultPeriodDays = 30;
    public const int MaxPeriodDays = 366;

    private readonly ITrialActivationMetricsReader _reader;
    private readonly TimeProvider _timeProvider;

    public GetTrialActivationMetricsQueryHandler(ITrialActivationMetricsReader reader, TimeProvider timeProvider)
    {
        _reader = reader;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<TrialActivationMetricsDto>> Handle(GetTrialActivationMetricsQuery request, CancellationToken ct)
    {
        var toUtc = Utc(request.ToUtc ?? _timeProvider.GetUtcNow().UtcDateTime);
        var fromUtc = Utc(request.FromUtc ?? toUtc.AddDays(-DefaultPeriodDays));

        if (fromUtc >= toUtc)
            return Result<TrialActivationMetricsDto>.Failure("TrialMetrics.InvalidRange", "Başlangıç bitişten önce olmalı.");

        if (toUtc - fromUtc > TimeSpan.FromDays(MaxPeriodDays))
            return Result<TrialActivationMetricsDto>.Failure("TrialMetrics.InvalidRange", $"Dönem en çok {MaxPeriodDays} gün olabilir.");

        var cohort = await _reader.ListCohortAsync(fromUtc, toUtc, ct);
        return Result<TrialActivationMetricsDto>.Success(TrialActivationMetricsCalculator.Calculate(fromUtc, toUtc, cohort));
    }

    private static DateTime Utc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
