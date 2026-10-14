using FluentValidation;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Create;

public sealed class CreateVaccinationCommandValidator : AbstractValidator<CreateVaccinationCommand>
{
    public CreateVaccinationCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.VaccineDefinitionId).NotEmpty();

        RuleFor(x => x.Status)
            .IsInEnum();

        RuleFor(x => x.ProductId).NotEmpty().When(x => x.ProductId.HasValue);
        RuleFor(x => x.Quantity).GreaterThan(0).When(x => x.Quantity.HasValue);
        RuleFor(x => x.BatchId).NotEmpty().When(x => x.BatchId.HasValue);
        RuleFor(x => x.RequestId).NotEmpty().When(x => x.RequestId.HasValue);
        RuleFor(x => x)
            .Must(x => x.ProductId.HasValue || (!x.Quantity.HasValue && !x.BatchId.HasValue && !x.RequestId.HasValue))
            .WithMessage("quantity, batchId ve requestId yalnız productId ile birlikte verilebilir.")
            .WithErrorCode("Vaccinations.Validation");

        RuleFor(x => x.Notes)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Notes));
    }
}
