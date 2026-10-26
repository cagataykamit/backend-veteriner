using Backend.Veteriner.Application.Imaging.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imaging.Queries.GetById;

public sealed record GetImagingRecordByIdQuery(Guid Id) : IRequest<Result<ImagingRecordDetailDto>>;
