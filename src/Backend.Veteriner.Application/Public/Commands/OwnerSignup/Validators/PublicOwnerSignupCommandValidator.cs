using Backend.Veteriner.Application.Common.Validation;
using Backend.Veteriner.Application.Public.TrialSignup;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;

namespace Backend.Veteriner.Application.Public.Commands.OwnerSignup.Validators;

public sealed class PublicOwnerSignupCommandValidator : AbstractValidator<PublicOwnerSignupCommand>
{
    public PublicOwnerSignupCommandValidator()
    {
        RuleFor(x => x.PlanCode)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.TenantName)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(300)
            .NotReservedTenantName();

        RuleFor(x => x.ClinicName)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(300);

        RuleFor(x => x.ClinicCity)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(200);

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(200);

        RuleFor(x => x.Password)
            .NotEmpty()
            .StrongPasswordRules(includeMaxLength: true);

        RuleFor(x => x.EmailVerificationToken)
            .NotEmpty().WithMessage(TrialSignupErrors.VerificationRequired.Message)
            .MaximumLength(200);

        // Zorunluluk ayara bağlıdır ve TrialSignupIdentityService'te denetlenir; burada yalnız dolu değerin biçimi.
        RuleFor(x => x.TaxIdentityNumber)
            .Must(TurkishTaxIdentity.IsValidTaxpayerIdentity)
            .When(x => !string.IsNullOrWhiteSpace(x.TaxIdentityNumber))
            .WithMessage(TrialSignupErrors.InvalidTaxIdentityMessage);
    }
}
