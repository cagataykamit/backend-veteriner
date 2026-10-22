using Backend.Veteriner.Domain.SalesPartners;
using FluentValidation;

namespace Backend.Veteriner.Application.SalesPartners.Commands.CreatePartner;

/// <summary>Ortak oluşturma ve güncellemenin ortak alan kuralları.</summary>
internal static class SalesPartnerInputRules
{
    public static void Apply<T>(AbstractValidator<T> v, Func<T, string> name, Func<T, string?> email, Func<T, string?> phone)
    {
        v.RuleFor(x => name(x)).NotEmpty().MinimumLength(2).MaximumLength(SalesPartner.MaxNameLength).OverridePropertyName("name");
        v.RuleFor(x => email(x)).EmailAddress().MaximumLength(SalesPartner.MaxEmailLength)
            .When(x => !string.IsNullOrWhiteSpace(email(x))).OverridePropertyName("email");
        v.RuleFor(x => phone(x)).MaximumLength(SalesPartner.MaxPhoneLength).OverridePropertyName("phone");
    }
}

public sealed class CreateSalesPartnerCommandValidator : AbstractValidator<CreateSalesPartnerCommand>
{
    public CreateSalesPartnerCommandValidator()
        => SalesPartnerInputRules.Apply(this, x => x.Name, x => x.Email, x => x.Phone);
}
