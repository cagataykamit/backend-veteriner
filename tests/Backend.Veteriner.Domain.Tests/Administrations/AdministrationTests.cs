using Backend.Veteriner.Domain.Administrations;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Administrations;

public sealed class AdministrationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid PetId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    private static Administration New(decimal quantity = 1.5m, string? notes = null, Guid? requestId = null)
        => Administration.Create(
            TenantId, ClinicId, PetId, null, null, ProductId, quantity, Now, notes, UserId, Now, requestId).Value!;

    [Fact]
    public void Create_Should_Set_Fields_And_Start_Active()
    {
        var requestId = Guid.NewGuid();

        var a = New(2.25m, "  sol arka bacak  ", requestId);

        a.Quantity.Should().Be(2.25m);
        a.Notes.Should().Be("sol arka bacak");
        a.RequestId.Should().Be(requestId);
        a.IsVoided.Should().BeFalse();
        a.AdministeredAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000.001)]
    [InlineData(1.0005)]
    public void Create_Should_Reject_Invalid_Quantity(double quantity)
    {
        var result = Administration.Create(
            TenantId, ClinicId, PetId, null, null, ProductId, (decimal)quantity, Now, null, UserId, Now, null);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Administrations.Validation");
    }

    [Fact]
    public void Create_Should_Accept_Max_Quantity_And_Three_Decimals()
    {
        Administration.Create(TenantId, ClinicId, PetId, null, null, ProductId, 100_000m, Now, null, UserId, Now, null)
            .IsSuccess.Should().BeTrue();
        Administration.Create(TenantId, ClinicId, PetId, null, null, ProductId, 0.001m, Now, null, UserId, Now, null)
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_Reject_Empty_Ids_And_Long_Notes()
    {
        Administration.Create(TenantId, ClinicId, Guid.Empty, null, null, ProductId, 1m, Now, null, UserId, Now, null)
            .IsSuccess.Should().BeFalse();
        Administration.Create(TenantId, ClinicId, PetId, null, null, Guid.Empty, 1m, Now, null, UserId, Now, null)
            .IsSuccess.Should().BeFalse();
        Administration.Create(TenantId, ClinicId, PetId, Guid.Empty, null, ProductId, 1m, Now, null, UserId, Now, null)
            .IsSuccess.Should().BeFalse();
        Administration.Create(TenantId, ClinicId, PetId, null, null, ProductId, 1m, Now, null, UserId, Now, Guid.Empty)
            .IsSuccess.Should().BeFalse();
        Administration.Create(TenantId, ClinicId, PetId, null, null, ProductId, 1m, Now, new string('x', 2001), UserId, Now, null)
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Void_Should_Record_Who_When_And_Why()
    {
        var a = New();
        var voidedBy = Guid.NewGuid();

        var result = a.Void(voidedBy, "  Yanlış ürün seçildi  ", Now.AddMinutes(5));

        result.IsSuccess.Should().BeTrue();
        a.IsVoided.Should().BeTrue();
        a.VoidedByUserId.Should().Be(voidedBy);
        a.VoidReason.Should().Be("Yanlış ürün seçildi");
        a.VoidedAtUtc.Should().Be(Now.AddMinutes(5));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void Void_Should_Require_Reason(string? reason)
    {
        var a = New();

        var result = a.Void(Guid.NewGuid(), reason, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Administrations.Validation");
        a.IsVoided.Should().BeFalse();
    }

    [Fact]
    public void Void_Twice_Should_Fail_With_AlreadyVoided()
    {
        var a = New();
        a.Void(Guid.NewGuid(), "Yanlış ürün seçildi", Now).IsSuccess.Should().BeTrue();

        var again = a.Void(Guid.NewGuid(), "Tekrar iptal denemesi", Now);

        again.Error.Code.Should().Be("Administrations.AlreadyVoided");
    }
}
