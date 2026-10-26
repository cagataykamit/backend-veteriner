using Backend.Veteriner.Application.ClinicalTerms.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ClinicalTerms.Queries.GetList;

/// <summary>Kiracının sözlüğü (sistem + kliniğe özel); varsayılan yalnız aktif kayıtlar.</summary>
public sealed record GetClinicalTermsListQuery(
    PageRequest PageRequest,
    ClinicalTermKind? Kind = null,
    bool IncludeInactive = false)
    : IRequest<Result<PagedResult<ClinicalTermDto>>>;
