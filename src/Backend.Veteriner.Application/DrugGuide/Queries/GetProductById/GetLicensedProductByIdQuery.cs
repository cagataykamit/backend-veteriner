using Backend.Veteriner.Application.DrugGuide.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.DrugGuide.Queries.GetProductById;

public sealed record GetLicensedProductByIdQuery(Guid Id) : IRequest<Result<LicensedProductDto>>;
