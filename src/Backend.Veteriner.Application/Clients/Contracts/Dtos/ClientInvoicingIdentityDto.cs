using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

/// <summary>
/// Müşteri fatura kimliği (ADR-036 Dilim 1), tam değerlerle. İki yerde kullanılır: <c>POST/PUT /clients</c> gövdesindeki
/// isteğe bağlı <c>invoicingIdentity</c> nesnesi ve yalnız <c>Clients.Update</c> iznine açık
/// <c>GET /clients/{id}/invoicing-identity</c> yanıtı. Diğer okuma yüzeyleri maskeli alanları taşır.
/// Bkz. docs/INVOICING_IDENTITY_API_CONTRACT.md §2.
/// </summary>
public sealed record ClientInvoicingIdentityDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ClientPersonType? PersonType,
    string? NationalId,
    string? TaxNumber,
    string? TaxOffice,
    string? LegalName,
    string? BillingProvince,
    string? BillingDistrict,
    string? BillingAddressLine)
{
    /// <summary>Nesnenin tamamını müşteriye yazar; kurallar <see cref="Client.ApplyInvoicingIdentity"/> içindedir.</summary>
    public Result ApplyTo(Client client, DateTime utcNow)
        => client.ApplyInvoicingIdentity(
            PersonType,
            NationalId,
            TaxNumber,
            TaxOffice,
            LegalName,
            BillingProvince,
            BillingDistrict,
            BillingAddressLine,
            utcNow);

    public static ClientInvoicingIdentityDto From(Client client)
        => new(
            client.PersonType,
            client.NationalId,
            client.TaxNumber,
            client.TaxOffice,
            client.LegalName,
            client.BillingProvince,
            client.BillingDistrict,
            client.BillingAddressLine);
}
