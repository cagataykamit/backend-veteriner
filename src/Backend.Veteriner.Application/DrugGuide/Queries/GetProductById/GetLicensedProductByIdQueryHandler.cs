using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.DrugGuide.Contracts.Dtos;
using Backend.Veteriner.Application.DrugGuide.Specs;
using Backend.Veteriner.Domain.DrugGuide;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.DrugGuide.Queries.GetProductById;

public sealed class GetLicensedProductByIdQueryHandler : IRequestHandler<GetLicensedProductByIdQuery, Result<LicensedProductDto>>
{
    private readonly IReadRepository<LicensedProduct> _products;

    public GetLicensedProductByIdQueryHandler(IReadRepository<LicensedProduct> products) => _products = products;

    public async ValueTask<Result<LicensedProductDto>> Handle(GetLicensedProductByIdQuery request, CancellationToken ct)
    {
        var row = await _products.FirstOrDefaultAsync(new LicensedProductsByIdsSpec([request.Id]), ct);
        return row is null
            ? Result<LicensedProductDto>.Failure(DrugGuideErrors.ProductNotFoundCode, DrugGuideErrors.ProductNotFoundMessage)
            : Result<LicensedProductDto>.Success(LicensedProductDto.From(row));
    }
}
