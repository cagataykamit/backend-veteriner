using Backend.Veteriner.Application.ClinicalTerms.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ClinicalTerms.Queries.GetById;

public sealed record GetClinicalTermByIdQuery(Guid Id) : IRequest<Result<ClinicalTermDto>>;
