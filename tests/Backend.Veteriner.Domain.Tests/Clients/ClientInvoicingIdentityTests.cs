using Backend.Veteriner.Domain.Clients;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Clients;

/// <summary>ADR-036 Dilim 1: müşteri fatura kimliği kuralları (<see cref="Client.ApplyInvoicingIdentity"/>).</summary>
public sealed class ClientInvoicingIdentityTests
{
    private const string Tckn = "10000000146";
    private const string Vkn = "1234567890";

    private static Client NewClient() => new(Guid.NewGuid(), "Ayşe Yılmaz");

    [Fact]
    public void Individual_With_Valid_Tckn_And_Address_Should_Be_Stored_Trimmed()
    {
        var client = NewClient();

        var result = client.ApplyInvoicingIdentity(
            ClientPersonType.Individual, $" {Tckn} ", null, " ", null, " İstanbul ", "Kadıköy", " Moda Cad. 1 ");

        result.IsSuccess.Should().BeTrue();
        client.PersonType.Should().Be(ClientPersonType.Individual);
        client.NationalId.Should().Be(Tckn);
        client.TaxNumber.Should().BeNull();
        client.TaxOffice.Should().BeNull();
        client.BillingProvince.Should().Be("İstanbul");
        client.BillingDistrict.Should().Be("Kadıköy");
        client.BillingAddressLine.Should().Be("Moda Cad. 1");
    }

    [Fact]
    public void Corporate_With_Valid_Vkn_Should_Be_Stored()
    {
        var client = NewClient();

        var result = client.ApplyInvoicingIdentity(
            ClientPersonType.Corporate, null, Vkn, "Kadıköy VD", "Örnek Ltd. Şti.", null, null, null);

        result.IsSuccess.Should().BeTrue();
        client.TaxNumber.Should().Be(Vkn);
        client.TaxOffice.Should().Be("Kadıköy VD");
        client.LegalName.Should().Be("Örnek Ltd. Şti.");
        client.NationalId.Should().BeNull();
    }

    [Fact]
    public void All_Fields_Optional_Including_Empty_Identity()
    {
        var client = NewClient();
        client.ApplyInvoicingIdentity(ClientPersonType.Individual, Tckn, null, null, null, "İzmir", null, null);

        var cleared = client.ApplyInvoicingIdentity(null, null, null, null, null, null, null, null);

        cleared.IsSuccess.Should().BeTrue();
        client.PersonType.Should().BeNull();
        client.NationalId.Should().BeNull();
        client.BillingProvince.Should().BeNull();
    }

    [Fact]
    public void Address_Without_Person_Type_Should_Be_Allowed()
        => NewClient().ApplyInvoicingIdentity(null, null, null, null, null, "Ankara", "Çankaya", null)
            .IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData(null, Tckn, null, null, null)]
    [InlineData(null, null, Vkn, null, null)]
    [InlineData(null, null, null, "VD", null)]
    [InlineData(ClientPersonType.Individual, null, Vkn, null, null)]
    [InlineData(ClientPersonType.Individual, Tckn, null, "VD", null)]
    [InlineData(ClientPersonType.Individual, null, null, null, "Unvan")]
    [InlineData(ClientPersonType.Corporate, Tckn, null, null, null)]
    [InlineData(ClientPersonType.Individual, "12345678901", null, null, null)]
    [InlineData(ClientPersonType.Corporate, null, "1234567891", null, null)]
    public void Inconsistent_Or_Invalid_Identity_Should_Be_Rejected_Without_Change(
        ClientPersonType? type, string? tckn, string? vkn, string? office, string? legalName)
    {
        var client = NewClient();

        var result = client.ApplyInvoicingIdentity(type, tckn, vkn, office, legalName, null, null, null);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.Validation");
        client.PersonType.Should().BeNull();
        client.NationalId.Should().BeNull();
        client.TaxNumber.Should().BeNull();
    }

    [Fact]
    public void Too_Long_Field_Should_Be_Rejected()
        => NewClient().ApplyInvoicingIdentity(
                ClientPersonType.Corporate, null, Vkn, new string('a', Client.MaxTaxOfficeLength + 1), null, null, null, null)
            .Error.Code.Should().Be("Clients.Validation");

    [Fact]
    public void Undefined_Person_Type_Should_Be_Rejected()
        => NewClient().ApplyInvoicingIdentity((ClientPersonType)99, null, null, null, null, null, null, null)
            .IsSuccess.Should().BeFalse();
}
