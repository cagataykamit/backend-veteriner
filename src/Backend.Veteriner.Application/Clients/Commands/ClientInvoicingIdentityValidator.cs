using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;

namespace Backend.Veteriner.Application.Clients.Commands;

/// <summary>
/// Müşteri oluşturma/güncellemedeki <c>invoicingIdentity</c> için alan düzeyi mesajlar. Biçim kuralları domain'dedir
/// (<see cref="TurkishTaxIdentity"/>); kişi türü tutarlılığı <see cref="Client.ApplyInvoicingIdentity"/> içindedir.
/// </summary>
public sealed class ClientInvoicingIdentityValidator : AbstractValidator<ClientInvoicingIdentityDto>
{
    public ClientInvoicingIdentityValidator()
    {
        RuleFor(x => x.PersonType)
            .IsInEnum().WithMessage("Geçersiz kişi türü.");

        RuleFor(x => x.NationalId)
            .Must(TurkishTaxIdentity.IsValidNationalId)
            .When(x => !string.IsNullOrWhiteSpace(x.NationalId))
            .WithMessage("TCKN 11 haneli ve geçerli olmalıdır.");

        RuleFor(x => x.TaxNumber)
            .Must(TurkishTaxIdentity.IsValidTaxNumber)
            .When(x => !string.IsNullOrWhiteSpace(x.TaxNumber))
            .WithMessage("VKN 10 haneli ve geçerli olmalıdır.");

        RuleFor(x => x.TaxOffice)
            .MaximumLength(Client.MaxTaxOfficeLength)
            .WithMessage($"Vergi dairesi en fazla {Client.MaxTaxOfficeLength} karakter olabilir.");

        RuleFor(x => x.LegalName)
            .MaximumLength(Client.MaxLegalNameLength)
            .WithMessage($"Unvan en fazla {Client.MaxLegalNameLength} karakter olabilir.");

        RuleFor(x => x.BillingProvince)
            .MaximumLength(Client.MaxBillingProvinceLength)
            .WithMessage($"İl en fazla {Client.MaxBillingProvinceLength} karakter olabilir.");

        RuleFor(x => x.BillingDistrict)
            .MaximumLength(Client.MaxBillingDistrictLength)
            .WithMessage($"İlçe en fazla {Client.MaxBillingDistrictLength} karakter olabilir.");

        RuleFor(x => x.BillingAddressLine)
            .MaximumLength(Client.MaxBillingAddressLineLength)
            .WithMessage($"Adres en fazla {Client.MaxBillingAddressLineLength} karakter olabilir.");
    }
}
