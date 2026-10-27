using Backend.Veteriner.Domain.Prescriptions;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Prescriptions;

public sealed class PrescriptionItemsTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static Prescription NewPrescription()
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, DateTime.UtcNow, "Başlık", "İçerik", null, null, Now);

    private static PrescriptionItemDraft Draft(
        string name = "Amoksisilin",
        Guid? productId = null,
        string? instructions = null,
        decimal? quantity = null)
        => new(name, productId, instructions, quantity);

    [Fact]
    public void NewPrescription_Should_HaveNoItems()
        => NewPrescription().Items.Should().BeEmpty();

    [Fact]
    public void ReplaceItems_Should_AssignSortOrderFromListPosition_And_Trim()
    {
        var rx = NewPrescription();
        var productId = Guid.NewGuid();

        var result = rx.ReplaceItems(new[]
        {
            Draft("  A  ", productId, "  günde 2  ", 14),
            Draft("B"),
        });

        result.IsSuccess.Should().BeTrue();
        rx.Items.Select(i => i.SortOrder).Should().Equal(1, 2);
        rx.Items[0].Name.Should().Be("A");
        rx.Items[0].ProductId.Should().Be(productId);
        rx.Items[0].Instructions.Should().Be("günde 2");
        rx.Items[0].Quantity.Should().Be(14);
        rx.Items[0].PrescriptionId.Should().Be(rx.Id);
        rx.Items[1].Instructions.Should().BeNull();
    }

    [Fact]
    public void ReplaceItems_Should_ReplaceAll_And_EmptyListClears()
    {
        var rx = NewPrescription();
        rx.ReplaceItems(new[] { Draft("A"), Draft("B") });

        rx.ReplaceItems(new[] { Draft("C") }).IsSuccess.Should().BeTrue();
        rx.Items.Select(i => i.Name).Should().Equal("C");

        rx.ReplaceItems(Array.Empty<PrescriptionItemDraft>()).IsSuccess.Should().BeTrue();
        rx.Items.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceItems_Should_KeepExistingItems_When_ValidationFails()
    {
        var rx = NewPrescription();
        rx.ReplaceItems(new[] { Draft("A") });

        var result = rx.ReplaceItems(new[] { Draft("B"), Draft(" ") });

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Prescriptions.Validation");
        rx.Items.Select(i => i.Name).Should().Equal("A");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100000.001)]
    [InlineData(1.2345)]
    public void ReplaceItems_Should_Reject_InvalidQuantity(double quantity)
        => NewPrescription().ReplaceItems(new[] { Draft(quantity: (decimal)quantity) }).IsSuccess.Should().BeFalse();

    [Fact]
    public void ReplaceItems_Should_Reject_EmptyProductId_TooLongTexts_And_TooManyItems()
    {
        var rx = NewPrescription();

        rx.ReplaceItems(new[] { Draft(productId: Guid.Empty) }).IsSuccess.Should().BeFalse();
        rx.ReplaceItems(new[] { Draft(new string('a', PrescriptionItem.NameMaxLength + 1)) }).IsSuccess.Should().BeFalse();
        rx.ReplaceItems(new[] { Draft(instructions: new string('a', PrescriptionItem.InstructionsMaxLength + 1)) }).IsSuccess.Should().BeFalse();
        rx.ReplaceItems(Enumerable.Range(0, PrescriptionItem.MaxPerPrescription + 1).Select(_ => Draft()).ToList())
            .IsSuccess.Should().BeFalse();
        rx.ReplaceItems(Enumerable.Range(0, PrescriptionItem.MaxPerPrescription).Select(_ => Draft()).ToList())
            .IsSuccess.Should().BeTrue();
    }
}
