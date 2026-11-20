using Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.VitalMeasurements.Queries.GetById;

public sealed record GetVitalMeasurementByIdQuery(Guid Id) : IRequest<Result<VitalMeasurementDetailDto>>;

public sealed class GetVitalMeasurementByIdQueryValidator : AbstractValidator<GetVitalMeasurementByIdQuery>
{
    public GetVitalMeasurementByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
