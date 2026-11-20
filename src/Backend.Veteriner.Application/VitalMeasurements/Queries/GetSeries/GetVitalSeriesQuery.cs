using Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.VitalMeasurements.Queries.GetSeries;

/// <summary>
/// Hastanın vital eğilimi (HOSP-004): vital ölçümleri ve muayene vitalleri birleşik, artan zaman sırasıyla.
/// <see cref="HospitalizationId"/> verilirse pencere yatış süresidir ve klinik yatışın kliniğine daraltılır.
/// </summary>
public sealed record GetVitalSeriesQuery(
    Guid PetId,
    Guid? ClinicId = null,
    Guid? HospitalizationId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int? Limit = null)
    : IRequest<Result<VitalSeriesDto>>
{
    public const int DefaultLimit = 500;
    public const int MaxLimit = 1000;
}

public sealed class GetVitalSeriesQueryValidator : AbstractValidator<GetVitalSeriesQuery>
{
    public GetVitalSeriesQueryValidator()
    {
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.HospitalizationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir yatış seçin.");
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, GetVitalSeriesQuery.MaxLimit)
            .When(x => x.Limit.HasValue);
        RuleFor(x => x)
            .Must(x => !x.FromUtc.HasValue || !x.ToUtc.HasValue || x.FromUtc < x.ToUtc)
            .WithMessage("fromUtc, toUtc değerinden önce olmalı.");
    }
}
