using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.VitalMeasurements.Queries.GetList;

/// <summary>Hastanın vital ölçümleri, en yeni üstte. <see cref="PetId"/> zorunlu; klinik verilmezse erişilebilen klinikler.</summary>
public sealed record GetVitalMeasurementsListQuery(
    PageRequest PageRequest,
    Guid PetId,
    Guid? ClinicId = null,
    Guid? HospitalizationId = null,
    Guid? ExaminationId = null,
    DateTime? DateFromUtc = null,
    DateTime? DateToUtc = null,
    bool IncludeCancelled = false)
    : IRequest<Result<PagedResult<VitalMeasurementListItemDto>>>;

public sealed class GetVitalMeasurementsListQueryValidator : AbstractValidator<GetVitalMeasurementsListQuery>
{
    public GetVitalMeasurementsListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x)
            .Must(x => !x.DateFromUtc.HasValue || !x.DateToUtc.HasValue || x.DateFromUtc <= x.DateToUtc)
            .WithMessage("dateFromUtc, dateToUtc'den küçük veya eşit olmalıdır.");
    }
}
