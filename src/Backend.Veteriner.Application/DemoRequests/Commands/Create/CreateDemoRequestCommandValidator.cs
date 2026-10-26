using Backend.Veteriner.Domain.DemoRequests;
using FluentValidation;

namespace Backend.Veteriner.Application.DemoRequests.Commands.Create;

/// <summary>Uzunluklar kırpılmış değere uygulanır (alan kuralı <see cref="DemoRequest"/>); tuzak alan doluysa doğrulama yapılmaz.</summary>
public sealed class CreateDemoRequestCommandValidator : AbstractValidator<CreateDemoRequestCommand>
{
    public CreateDemoRequestCommandValidator()
    {
        When(x => !x.IsHoneypotFilled, () =>
        {
            RuleFor(x => Trim(x.ContactName)).NotEmpty().MinimumLength(DemoRequest.MinNameLength)
                .MaximumLength(DemoRequest.MaxContactNameLength).OverridePropertyName("contactName");
            RuleFor(x => Trim(x.ClinicName)).NotEmpty().MinimumLength(DemoRequest.MinNameLength)
                .MaximumLength(DemoRequest.MaxClinicNameLength).OverridePropertyName("clinicName");
            RuleFor(x => Trim(x.City)).MaximumLength(DemoRequest.MaxCityLength).OverridePropertyName("city");
            RuleFor(x => Trim(x.Email)).NotEmpty().EmailAddress()
                .MaximumLength(DemoRequest.MaxEmailLength).OverridePropertyName("email");
            RuleFor(x => Trim(x.Phone)).MaximumLength(DemoRequest.MaxPhoneLength).OverridePropertyName("phone");
            RuleFor(x => x.ClinicSize).NotNull().IsInEnum().OverridePropertyName("clinicSize");
            RuleFor(x => Trim(x.CurrentSoftware)).MaximumLength(DemoRequest.MaxCurrentSoftwareLength)
                .OverridePropertyName("currentSoftware");
            RuleFor(x => Trim(x.Message)).MaximumLength(DemoRequest.MaxMessageLength).OverridePropertyName("message");
            RuleFor(x => x.PrivacyNoticeAccepted).Equal(true)
                .WithMessage("Gizlilik politikasını okuduğunuzu onaylayın.").OverridePropertyName("privacyNoticeAccepted");
        });
    }

    private static string Trim(string? value) => value?.Trim() ?? string.Empty;
}
