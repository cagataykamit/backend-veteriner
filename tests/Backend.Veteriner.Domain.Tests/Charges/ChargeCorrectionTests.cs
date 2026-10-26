using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Payments;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Charges;

public sealed class ChargeCorrectionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);
    private const string Reason = "Fiyat listesi yanlış okundu";

    private static Charge NewCharge(decimal unitPrice = 1000m)
        => Charge.Create(TenantId, ClinicId, ClientId, null, null, null, "Muayene", 1m, unitPrice, UserId, Now, null).Value!;

    private static Payment NewPayment(decimal amount)
        => new(TenantId, ClinicId, ClientId, null, null, null, amount, "TRY", PaymentMethod.Cash, Now, null);

    [Fact]
    public void Correct_Should_Recalculate_Amount_And_Advance_Sequence()
    {
        var charge = NewCharge(1000m);

        var result = charge.Correct("Muayene (indirimli)", 2m, 300.50m, Reason);

        result.IsSuccess.Should().BeTrue();
        charge.Description.Should().Be("Muayene (indirimli)");
        charge.Quantity.Should().Be(2m);
        charge.UnitPrice.Should().Be(300.50m);
        charge.Amount.Should().Be(601.00m);
        charge.MutationSequence.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void Correct_Should_Require_Reason(string? reason)
    {
        var charge = NewCharge();

        var result = charge.Correct("Muayene", 1m, 500m, reason);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Charges.Validation");
        charge.Amount.Should().Be(1000m);
    }

    [Fact]
    public void Correct_Should_Reject_When_Nothing_Changes()
    {
        var charge = NewCharge();

        var result = charge.Correct("Muayene", 1m, 1000m, Reason);

        result.Error.Code.Should().Be("Charges.Validation");
        charge.MutationSequence.Should().Be(0);
    }

    [Fact]
    public void Correct_Should_Reject_Amount_Below_Allocated_And_Allow_Equal()
    {
        var charge = NewCharge(1000m);
        NewPayment(400m).AllocateFully([(charge, 400m)], UserId, Now).IsSuccess.Should().BeTrue();

        var below = charge.Correct("Muayene", 1m, 399.99m, Reason);
        below.Error.Code.Should().Be("Charges.CorrectionBelowAllocated");
        charge.Amount.Should().Be(1000m);

        var equal = charge.Correct("Muayene", 1m, 400m, Reason);
        equal.IsSuccess.Should().BeTrue();
        charge.OutstandingAmount.Should().Be(0m);
    }

    [Fact]
    public void Correct_Should_Apply_Creation_Rules()
    {
        var charge = NewCharge();

        charge.Correct(" ", 1m, 500m, Reason).Error.Code.Should().Be("Charges.Validation");
        charge.Correct("Muayene", 0m, 500m, Reason).Error.Code.Should().Be("Charges.Validation");
        charge.Correct("Muayene", 1m, 0.001m, Reason).Error.Code.Should().Be("Charges.Validation");
    }

    [Fact]
    public void Void_Should_Mark_Charge_And_Trim_Reason()
    {
        var charge = NewCharge();

        var result = charge.Void(UserId, $"  {Reason}  ", Now);

        result.IsSuccess.Should().BeTrue();
        charge.IsVoided.Should().BeTrue();
        charge.VoidedAtUtc.Should().Be(Now);
        charge.VoidedByUserId.Should().Be(UserId);
        charge.VoidReason.Should().Be(Reason);
        charge.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void Void_Should_Be_Blocked_By_Allocation()
    {
        var charge = NewCharge(1000m);
        NewPayment(100m).AllocateFully([(charge, 100m)], UserId, Now);

        var result = charge.Void(UserId, Reason, Now);

        result.Error.Code.Should().Be("Charges.VoidBlockedByAllocation");
        charge.IsVoided.Should().BeFalse();
    }

    [Fact]
    public void Voided_Charge_Cannot_Be_Voided_Corrected_Or_Allocated_Again()
    {
        var charge = NewCharge();
        charge.Void(UserId, Reason, Now).IsSuccess.Should().BeTrue();

        charge.Void(UserId, Reason, Now).Error.Code.Should().Be("Charges.AlreadyVoided");
        charge.Correct("Muayene", 1m, 500m, Reason).Error.Code.Should().Be("Charges.AlreadyVoided");
        charge.ApplyAllocation(10m).Error.Code.Should().Be("Charges.AlreadyVoided");
    }

    [Fact]
    public void Void_Should_Require_Reason_And_User()
    {
        var charge = NewCharge();

        charge.Void(UserId, "yok", Now).Error.Code.Should().Be("Charges.Validation");
        charge.Void(Guid.Empty, Reason, Now).Error.Code.Should().Be("Charges.Validation");
        charge.IsVoided.Should().BeFalse();
    }

    [Fact]
    public void ReleaseAllocation_Should_Not_Go_Below_Zero()
    {
        var charge = NewCharge(1000m);
        NewPayment(300m).AllocateFully([(charge, 300m)], UserId, Now);

        charge.ReleaseAllocation(301m).IsSuccess.Should().BeFalse();
        charge.ReleaseAllocation(300m).IsSuccess.Should().BeTrue();
        charge.AllocatedAmount.Should().Be(0m);
    }
}
