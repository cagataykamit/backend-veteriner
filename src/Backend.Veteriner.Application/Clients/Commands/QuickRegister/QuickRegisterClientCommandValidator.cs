using FluentValidation;

namespace Backend.Veteriner.Application.Clients.Commands.QuickRegister;

/// <summary>
/// Yalnızca bu uca özgü gereklilikler (zorunlu alanlar). Biçim/uzunluk/telefon geçerliliği gibi alan kuralları,
/// alt komutların (<c>CreateClientCommand</c>, <c>CreatePetCommand</c>) doğrulayıcılarında tek yerde yaşar ve
/// hiçbir kayıt kalmadan transaction içinde uygulanır.
/// </summary>
public sealed class QuickRegisterClientCommandValidator : AbstractValidator<QuickRegisterClientCommand>
{
    public QuickRegisterClientCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Ad soyad gereklidir.");
        RuleFor(x => x.Phone).NotEmpty().WithMessage("Telefon numarası gereklidir.");
        RuleFor(x => x.PetName).NotEmpty().WithMessage("Hayvan adı gereklidir.");
        RuleFor(x => x.SpeciesId).NotEmpty().WithMessage("Tür gereklidir.");
    }
}
