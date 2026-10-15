using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Models;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Common;

public sealed class ListSortCatalogTests
{
    private enum Field
    {
        A,
        B
    }

    private static readonly ListSortCatalog<Field> Catalog = new(("aAtUtc", Field.A), ("bAtUtc", Field.B), ("legacyA", Field.A));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("aAtUtc")]
    [InlineData("AATUTC")]
    [InlineData(" bAtUtc ")]
    [InlineData("legacyA")]
    public void IsAllowedSort_Should_Accept_Blank_And_Whitelisted_Keys(string? sort)
        => Catalog.IsAllowedSort(sort).Should().BeTrue();

    [Theory]
    [InlineData("Id")]
    [InlineData("createdAtUtc")]
    [InlineData("aAtUtc;drop")]
    public void IsAllowedSort_Should_Reject_Unknown_Keys(string sort)
        => Catalog.IsAllowedSort(sort).Should().BeFalse();

    [Fact]
    public void InvalidSortMessage_Should_List_Allowed_Keys()
        => Catalog.InvalidSortMessage.Should().Contain("aAtUtc").And.Contain("bAtUtc");

    [Fact]
    public void Resolve_Should_Return_Null_When_Sort_Blank_Even_If_Order_Given()
        => Catalog.Resolve(new PageRequest { Sort = " ", Order = "asc" }).Should().BeNull();

    [Theory]
    [InlineData("asc", false)]
    [InlineData("ASC", false)]
    [InlineData("desc", true)]
    [InlineData(null, true)]
    [InlineData("", true)]
    public void Resolve_Should_Map_Order(string? order, bool expectedDescending)
    {
        var choice = Catalog.Resolve(new PageRequest { Sort = "bAtUtc", Order = order });

        choice.Should().Be(new ListSortChoice<Field>(Field.B, expectedDescending));
    }

    [Fact]
    public void Resolve_Should_Map_Alias_To_Same_Field()
        => Catalog.Resolve(new PageRequest { Sort = "LEGACYA", Order = "asc" })!.Value.Field.Should().Be(Field.A);

    [Fact]
    public void Resolve_Should_Throw_For_Unvalidated_Unknown_Sort()
    {
        var act = () => Catalog.Resolve(new PageRequest { Sort = "Id" });

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("asc", true)]
    [InlineData("Desc", true)]
    [InlineData("up", false)]
    public void ListSortOrder_IsAllowed(string? order, bool expected)
        => ListSortOrder.IsAllowed(order).Should().Be(expected);
}
