using Backend.Veteriner.Application.ClinicalSnippets.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ClinicalSnippets.Queries.GetById;

public sealed record GetClinicalSnippetByIdQuery(Guid Id) : IRequest<Result<ClinicalSnippetDto>>;
