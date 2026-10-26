using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Tenants;

/// <summary>TRIAL-008: aktivasyon işaretleri yalnız ilk kez yazılır; sonraki çağrılar değeri korur.</summary>
public sealed class TenantActivationTests
{
    private static readonly DateTime First = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Ctor_Should_Reject_EmptyTenant()
    {
        var act = () => new TenantActivation(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkFirstAction_Should_Write_Once_And_Keep_First_Value()
    {
        var activation = new TenantActivation(Guid.NewGuid());
        var userId = Guid.NewGuid();

        activation.MarkFirstAction(ActivationSignalKind.ClientCreated, userId, First).Should().BeTrue();
        activation.MarkFirstAction(ActivationSignalKind.ExaminationCreated, Guid.NewGuid(), First.AddMinutes(5)).Should().BeFalse();

        activation.FirstActionAtUtc.Should().Be(First);
        activation.FirstActionKind.Should().Be(ActivationSignalKind.ClientCreated);
        activation.FirstActionUserId.Should().Be(userId);
    }

    [Fact]
    public void MarkExampleClinicLoaded_Should_Write_Once_And_Normalize_To_Utc()
    {
        var activation = new TenantActivation(Guid.NewGuid());
        var unspecified = DateTime.SpecifyKind(First, DateTimeKind.Unspecified);

        activation.MarkExampleClinicLoaded(unspecified).Should().BeTrue();
        activation.MarkExampleClinicLoaded(First.AddHours(1)).Should().BeFalse();

        activation.ExampleClinicLoadedAtUtc.Should().Be(First);
        activation.ExampleClinicLoadedAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Marks_Should_Be_Independent()
    {
        var activation = new TenantActivation(Guid.NewGuid());

        activation.MarkExampleClinicLoaded(First);

        activation.FirstActionAtUtc.Should().BeNull("örnek klinik yüklemesi kullanıcı aksiyonu değildir");
        activation.MarkFirstAction(ActivationSignalKind.PetCreated, null, First.AddMinutes(3)).Should().BeTrue();
        activation.ExampleClinicLoadedAtUtc.Should().Be(First);
    }
}
