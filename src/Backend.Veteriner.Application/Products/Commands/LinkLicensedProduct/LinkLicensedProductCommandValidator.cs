using FluentValidation;

namespace Backend.Veteriner.Application.Products.Commands.LinkLicensedProduct;

public sealed class LinkLicensedProductCommandValidator : AbstractValidator<LinkLicensedProductCommand>
{
    public LinkLicensedProductCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.LicensedProductId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Ruhsatlı ürün geçersiz.");
    }
}
