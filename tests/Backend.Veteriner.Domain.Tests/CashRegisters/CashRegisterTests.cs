using Backend.Veteriner.Domain.CashRegisters;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.CashRegisters;

/// <summary>TIER-008: Ana kasa pasife alınamaz; pasif kasaya hareket girilmez; giriş/çıkış ve aktarım kuralları, iptal.</summary>
public sealed class CashRegisterTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 8);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();

    private static CashRegister Register(string code = "K2") => CashRegister.Create(TenantId, ClinicId, code, "Kasa " + code, Now).Value!;

    [Fact]
    public void Main_Should_Be_Active_And_Not_Deactivatable()
    {
        var main = CashRegister.CreateMain(TenantId, ClinicId, Now);

        main.IsMain.Should().BeTrue();
        main.Code.Should().Be(CashRegister.MainCode);
        main.Name.Should().Be(CashRegister.MainName);
        main.Update("ANA", "Ön büro kasası", false, Now).Error.Code.Should().Be("CashRegisters.MainRegisterLocked");
        main.Update("ANA", "Ön büro kasası", true, Now).IsSuccess.Should().BeTrue("ad değişebilir");
        main.Name.Should().Be("Ön büro kasası");
        main.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_Normalize_Code_And_Validate_Name()
    {
        var register = CashRegister.Create(TenantId, ClinicId, " k2 ", " Arka kasa ", Now).Value!;
        register.Code.Should().Be("K2");
        register.Name.Should().Be("Arka kasa");
        register.IsMain.Should().BeFalse();

        CashRegister.Create(TenantId, ClinicId, "", "Ad", Now).Error.Code.Should().Be(CashRegister.ValidationCode);
        CashRegister.Create(TenantId, ClinicId, "K3", " ", Now).Error.Code.Should().Be(CashRegister.ValidationCode);
        CashRegister.Create(TenantId, ClinicId, new string('K', CashRegister.MaxCodeLength + 1), "Ad", Now).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Movement_Should_Have_A_Direction_Category_And_Valid_Amount()
    {
        var register = Register();

        var outMovement = CashMovement.Create(register, CashMovementKind.Out, Today, Today, 250.50m, CategoryId, " Bankaya yatırıldı ", UserId, Now).Value!;
        outMovement.SignedAmount.Should().Be(-250.50m);
        outMovement.Description.Should().Be("Bankaya yatırıldı");
        outMovement.CashRegisterId.Should().Be(register.Id);
        outMovement.ClinicId.Should().Be(ClinicId);

        CashMovement.Create(register, CashMovementKind.In, Today, Today, 10m, CategoryId, null, UserId, Now).Value!.SignedAmount.Should().Be(10m);
        CashMovement.Create(register, CashMovementKind.In, Today, Today, 10m, null, null, UserId, Now).Error.Code.Should().Be(CashMovement.ValidationCode);
        CashMovement.Create(register, CashMovementKind.TransferIn, Today, Today, 10m, CategoryId, null, UserId, Now).IsSuccess.Should().BeFalse();
        CashMovement.Create(register, CashMovementKind.In, Today, Today, 0m, CategoryId, null, UserId, Now).IsSuccess.Should().BeFalse();
        CashMovement.Create(register, CashMovementKind.In, Today, Today, 0.001m, CategoryId, null, UserId, Now).IsSuccess.Should().BeFalse();
        CashMovement.Create(register, CashMovementKind.In, Today.AddDays(1), Today, 10m, CategoryId, null, UserId, Now).IsSuccess.Should().BeFalse();
        CashMovement.Create(register, CashMovementKind.In, Today.AddDays(-3), Today, 10m, CategoryId, null, UserId, Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Inactive_Register_Should_Refuse_New_Movements()
    {
        var register = Register();
        register.Update(register.Code, register.Name, false, Now).IsSuccess.Should().BeTrue();

        CashMovement.Create(register, CashMovementKind.In, Today, Today, 10m, CategoryId, null, UserId, Now).Error.Code
            .Should().Be(CashRegister.InactiveCode);
        CashMovement.CreateTransfer(CashRegister.CreateMain(TenantId, ClinicId, Now), register, Today, Today, 10m, null, UserId, Now).Error.Code
            .Should().Be(CashRegister.InactiveCode);
    }

    [Fact]
    public void Transfer_Should_Produce_Two_Linked_Legs_Within_One_Clinic()
    {
        var main = CashRegister.CreateMain(TenantId, ClinicId, Now);
        var drawer = Register();

        var (outLeg, inLeg) = CashMovement.CreateTransfer(main, drawer, Today, Today, 300m, "Gün ortası", UserId, Now).Value;

        outLeg.Kind.Should().Be(CashMovementKind.TransferOut);
        outLeg.CashRegisterId.Should().Be(main.Id);
        outLeg.CounterpartCashRegisterId.Should().Be(drawer.Id);
        inLeg.Kind.Should().Be(CashMovementKind.TransferIn);
        inLeg.CashRegisterId.Should().Be(drawer.Id);
        inLeg.CounterpartCashRegisterId.Should().Be(main.Id);
        inLeg.TransferId.Should().Be(outLeg.TransferId!.Value);
        (outLeg.SignedAmount + inLeg.SignedAmount).Should().Be(0m, "aktarım klinik toplamını değiştirmez");

        CashMovement.CreateTransfer(main, main, Today, Today, 10m, null, UserId, Now).Error.Code.Should().Be(CashMovement.ValidationCode);
        var otherClinic = CashRegister.Create(TenantId, Guid.NewGuid(), "K9", "Başka", Now).Value!;
        CashMovement.CreateTransfer(main, otherClinic, Today, Today, 10m, null, UserId, Now).Error.Code.Should().Be(CashMovement.ValidationCode);
    }

    [Fact]
    public void Void_Should_Need_Reason_And_Happen_Once()
    {
        var movement = CashMovement.Create(Register(), CashMovementKind.Out, Today, Today, 50m, CategoryId, null, UserId, Now).Value!;

        movement.Void(UserId, "kısa", Now).IsSuccess.Should().BeFalse();
        movement.IsVoided.Should().BeFalse();
        movement.Void(UserId, "Yanlış tutar girildi", Now).IsSuccess.Should().BeTrue();
        movement.Void(UserId, "Yanlış tutar girildi", Now).Error.Code.Should().Be("CashMovements.AlreadyVoided");
        movement.Amount.Should().Be(50m, "kayıt ezilmez");
        movement.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void Category_Defaults_Should_Be_The_Small_Starter_List()
    {
        CashMovementCategory.CreateDefaults(TenantId, Now).Select(c => c.Name).Should()
            .Equal("Bankaya yatırma", "Masraf", "Bozuk para", "Diğer");
        CashMovementCategory.Create(TenantId, "  ", Now).IsSuccess.Should().BeFalse();
    }
}
