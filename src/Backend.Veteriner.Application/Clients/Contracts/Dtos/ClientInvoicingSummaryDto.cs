using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

/// <summary>
/// Müşteri fatura kimliğinin okuma görünümü (ADR-036): TCKN/VKN yalnız maskeli (<see cref="TurkishTaxIdentity.Mask"/>).
/// Müşteri detayı ve veri kopyası bu tek görünümü kullanır; tam değer yalnız <c>GET /clients/{id}/invoicing-identity</c>.
/// </summary>
public sealed record ClientInvoicingSummaryDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ClientPersonType? PersonType,
    string? NationalIdMasked,
    string? TaxNumberMasked,
    string? TaxOffice,
    string? LegalName,
    string? BillingProvince,
    string? BillingDistrict,
    string? BillingAddressLine)
{
    public static ClientInvoicingSummaryDto From(Client client)
        => new(
            client.PersonType,
            TurkishTaxIdentity.Mask(client.NationalId),
            TurkishTaxIdentity.Mask(client.TaxNumber),
            client.TaxOffice,
            client.LegalName,
            client.BillingProvince,
            client.BillingDistrict,
            client.BillingAddressLine);
}
