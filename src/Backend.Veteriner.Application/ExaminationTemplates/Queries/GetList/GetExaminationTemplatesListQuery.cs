using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ExaminationTemplates.Queries.GetList;

/// <summary>Kiracının şablonları (sistem + kliniğe özel), satırlarıyla; varsayılan yalnız aktifler.</summary>
public sealed record GetExaminationTemplatesListQuery(PageRequest PageRequest, bool IncludeInactive = false)
    : IRequest<Result<PagedResult<ExaminationTemplateDto>>>;
