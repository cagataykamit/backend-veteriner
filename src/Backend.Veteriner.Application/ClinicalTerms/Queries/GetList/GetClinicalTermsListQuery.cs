using Backend.Veteriner.Application.ClinicalTerms.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ClinicalTerms.Queries.GetList;

/// <summary>
/// Kiracının sözlüğü (sistem + kliniğe özel); varsayılan yalnız aktif kayıtlar. <paramref name="IsSystem"/>: <c>true</c> yalnız
/// sistem terimleri, <c>false</c> yalnız kliniğin eklediği terimler, <c>null</c> ikisi.
/// </summary>
public sealed record GetClinicalTermsListQuery(
    PageRequest PageRequest,
    ClinicalTermKind? Kind = null,
    bool IncludeInactive = false,
    bool? IsSystem = null)
    : IRequest<Result<PagedResult<ClinicalTermDto>>>;
