using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Products.Commands.LinkLicensedProduct;

/// <summary>Ürün kartını ilaç rehberindeki ruhsatlı ürüne bağlar; <see cref="LicensedProductId"/> null ise bağı kaldırır (REF-001 Dilim 2).</summary>
public sealed record LinkLicensedProductCommand(Guid ProductId, Guid? LicensedProductId) : IRequest<Result>;
