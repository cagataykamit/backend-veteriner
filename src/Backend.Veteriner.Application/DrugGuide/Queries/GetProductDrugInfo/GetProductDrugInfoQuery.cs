using Backend.Veteriner.Application.DrugGuide.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.DrugGuide.Queries.GetProductDrugInfo;

/// <summary>
/// Reçete ve uygulama satırlarındaki klinik ürünleri için rehber bilgisi (etken madde, hedef tür) ve hasta verilirse
/// engellemeyen hedef tür notu. Rehbere bağlı olmayan ürün yanıtta yer almaz.
/// </summary>
public sealed record GetProductDrugInfoQuery(IReadOnlyList<Guid> ProductIds, Guid? PetId = null)
    : IRequest<Result<IReadOnlyList<ProductDrugInfoDto>>>;
