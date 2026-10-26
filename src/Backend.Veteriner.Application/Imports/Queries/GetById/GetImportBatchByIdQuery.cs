using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Queries.GetById;

public sealed record GetImportBatchByIdQuery(Guid Id) : IRequest<Result<ImportBatchDto>>;
