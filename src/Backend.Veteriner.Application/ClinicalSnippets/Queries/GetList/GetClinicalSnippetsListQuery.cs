using Backend.Veteriner.Application.ClinicalSnippets.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ClinicalSnippets.Queries.GetList;

/// <summary>Kiracının metin parçaları; varsayılan yalnız aktifler. Arama kısayol ve başlıkta (gövdede değil).</summary>
public sealed record GetClinicalSnippetsListQuery(PageRequest PageRequest, bool IncludeInactive = false)
    : IRequest<Result<PagedResult<ClinicalSnippetDto>>>;
