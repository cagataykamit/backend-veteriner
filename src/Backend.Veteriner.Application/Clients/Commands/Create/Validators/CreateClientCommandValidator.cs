using Backend.Veteriner.Application.Clients.Commands.Create;
using Backend.Veteriner.Domain.Clients;
using FluentValidation;

namespace Backend.Veteriner.Application.Clients.Commands.Create.Validators;

public sealed class CreateClientCommandValidator : AbstractValidator<CreateClientCommand>
{
    public CreateClientCommandValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Ad soyad gereklidir.")
            .MinimumLength(2).WithMessage("Ad soyad en az 2 karakter olmalıdır.")
            .MaximumLength(300).WithMessage("Ad soyad en fazla 300 karakter olabilir.");

        RuleFor(x => x.Email)
            .MaximumLength(320).WithMessage("E-posta en fazla 320 karakter olabilir.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Geçerli bir e-posta adresi giriniz.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        // Telefon zorunlu (içe aktarma hariç) ve Türkiye cep standardı (905XXXXXXXXX); kural ClientPhoneRule.
        RuleFor(x => x.Phone)
            .Must((cmd, p) => ClientPhoneRule.IsSatisfiedOnCreate(p, cmd.FromImport))
            .WithErrorCode(ClientPhoneRule.RequiredCode)
            .WithMessage(ClientPhoneRule.RequiredMessage);

        RuleFor(x => x.Phone)
            .MaximumLength(50).WithMessage("Telefon numarası en fazla 50 karakter olabilir.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.Phone)
            .Must(p => string.IsNullOrWhiteSpace(p) || TurkishMobilePhone.TryNormalize(p, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage(ClientPhoneRule.InvalidMessage);

        RuleFor(x => x.Address)
            .MaximumLength(500).WithMessage("Adres en fazla 500 karakter olabilir.")
            .When(x => !string.IsNullOrWhiteSpace(x.Address));

        RuleFor(x => x.PreferredContactChannel!.Value)
            .IsInEnum().WithMessage("Geçersiz iletişim kanalı.")
            .When(x => x.PreferredContactChannel.HasValue);

        RuleFor(x => x.InvoicingIdentity!)
            .SetValidator(new ClientInvoicingIdentityValidator())
            .When(x => x.InvoicingIdentity is not null);
    }
}
