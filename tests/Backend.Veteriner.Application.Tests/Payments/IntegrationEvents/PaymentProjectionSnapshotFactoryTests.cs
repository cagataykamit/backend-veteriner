using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Domain.Payments;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Payments.IntegrationEvents;

public sealed class PaymentProjectionSnapshotFactoryTests
{
    private static Payment CreatePayment(
        Guid? petId = null,
        Guid? appointmentId = null,
        Guid? examinationId = null,
        string? notes = null)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            petId,
            appointmentId,
            examinationId,
            150m,
            "try",
            PaymentMethod.Card,
            new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc),
            notes);

    [Fact]
    public void Create_Enriched_Should_MapAndNormalizeClientAndPet()
    {
        var payment = CreatePayment(petId: Guid.NewGuid(), notes: "  Aşı Bedeli  ");

        var snapshot = PaymentProjectionSnapshotFactory.Create(payment, "  Ada Lovelace ", "  Vetinity Clinic  ", " Rex ");

        snapshot.PaymentId.Should().Be(payment.Id);
        snapshot.ClientName.Should().Be("Ada Lovelace");
        snapshot.ClientNameNormalized.Should().Be("ada lovelace");
        snapshot.ClinicName.Should().Be("Vetinity Clinic");
        snapshot.PetName.Should().Be("Rex");
        snapshot.PetNameNormalized.Should().Be("rex");
        snapshot.Notes.Should().Be("Aşı Bedeli");
        snapshot.NotesNormalized.Should().Be("aşı bedeli");
        snapshot.Method.Should().Be((int)PaymentMethod.Card);
        snapshot.SchemaVersion.Should().Be(PaymentIntegrationEventTypes.SchemaVersion);
    }

    [Fact]
    public void Create_Enriched_WithoutPetOrNotes_Should_LeaveOptionalFieldsNull()
    {
        var payment = CreatePayment(petId: null, notes: null);

        var snapshot = PaymentProjectionSnapshotFactory.Create(payment, "Ada Lovelace", "Vetinity Clinic");

        snapshot.PetId.Should().BeNull();
        snapshot.PetName.Should().BeNull();
        snapshot.PetNameNormalized.Should().BeNull();
        snapshot.Notes.Should().BeNull();
        snapshot.NotesNormalized.Should().BeNull();
        snapshot.ClinicName.Should().Be("Vetinity Clinic");
    }

    [Fact]
    public void Create_Enriched_Should_Throw_WhenClientNameMissing()
    {
        var payment = CreatePayment();

        var act = () => PaymentProjectionSnapshotFactory.Create(payment, "  ", "Vetinity Clinic");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Enriched_Should_Throw_WhenClinicNameMissing()
    {
        var payment = CreatePayment();

        var act = () => PaymentProjectionSnapshotFactory.Create(payment, "Ada Lovelace", "  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_FinanceOnly_Should_LeaveEnrichmentNull()
    {
        var payment = CreatePayment(notes: "ignored");

        var snapshot = PaymentProjectionSnapshotFactory.Create(payment);

        snapshot.ClientName.Should().BeNull();
        snapshot.ClientNameNormalized.Should().BeNull();
        snapshot.ClinicName.Should().BeNull();
        snapshot.PetName.Should().BeNull();
        snapshot.Notes.Should().BeNull();
        snapshot.Amount.Should().Be(150m);
        snapshot.Currency.Should().Be("TRY");
    }
}
