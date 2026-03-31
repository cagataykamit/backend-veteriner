using Backend.Veteriner.Domain.Clients;
using FluentValidation;

namespace Backend.Veteriner.Application.Clients.Commands.Update.Validators;

public sealed class UpdateClientCommandValidator : AbstractValidator<UpdateClientCommand>
{
    public UpdateClientCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
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

        RuleFor(x => x.Phone)
            .MaximumLength(50).WithMessage("Telefon numarası en fazla 50 karakter olabilir.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.Phone)
            .Must(p => string.IsNullOrWhiteSpace(p) || TurkishMobilePhone.TryNormalize(p, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Telefon numarası geçerli değil. Türkiye cep telefonu olarak 05XXXXXXXXX, 5XXXXXXXXX veya +90 5XX XXX XX XX biçiminde girin.");

        RuleFor(x => x.Address)
            .MaximumLength(500).WithMessage("Adres en fazla 500 karakter olabilir.")
            .When(x => !string.IsNullOrWhiteSpace(x.Address));
    }
}
