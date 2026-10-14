using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Queries.GetPreview;

public sealed record GetImportBatchPreviewQuery(Guid Id) : IRequest<Result<ImportPreviewDto>>;
