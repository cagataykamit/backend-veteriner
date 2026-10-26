using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ExaminationTemplates.Queries.GetById;

public sealed record GetExaminationTemplateByIdQuery(Guid Id) : IRequest<Result<ExaminationTemplateDto>>;
