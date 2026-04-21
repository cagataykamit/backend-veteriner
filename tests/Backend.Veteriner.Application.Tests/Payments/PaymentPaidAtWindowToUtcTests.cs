using System.Reflection;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Payments;

/// <summary>
/// <c>PaymentPaidAtWindow.ToUtc</c> için TZ normalizasyon testleri. Sınıf <c>internal</c> olduğundan reflection ile
/// çağırırız; davranış kontratı §12.5 — tahsilat saati TZ semantiği ile hizalıdır.
/// </summary>
public sealed class PaymentPaidAtWindowToUtcTests
{
    private static readonly MethodInfo ToUtcMethod = ResolveToUtc();

    private static DateTime ToUtc(DateTime value)
        => (DateTime)ToUtcMethod.Invoke(null, new object[] { value })!;

    private static MethodInfo ResolveToUtc()
    {
        var asm = typeof(Backend.Veteriner.Application.Payments.Commands.Create.CreatePaymentCommand).Assembly;
        var type = asm.GetType("Backend.Veteriner.Application.Payments.PaymentPaidAtWindow", throwOnError: true)!;
        return type.GetMethod("ToUtc", BindingFlags.Public | BindingFlags.Static)!;
    }

    [Fact]
    public void ToUtc_Should_ReturnValueUnchanged_When_KindIsUtc()
    {
        var utc = new DateTime(2026, 4, 17, 3, 3, 0, DateTimeKind.Utc);

        var result = ToUtc(utc);

        result.Should().Be(utc);
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_Should_ConvertLocal_Using_ToUniversalTime()
    {
        var local = new DateTime(2026, 4, 17, 6, 3, 0, DateTimeKind.Local);

        var result = ToUtc(local);

        result.Should().Be(local.ToUniversalTime());
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_Should_InterpretUnspecifiedAsIstanbulLocal_AndConvertToUtc()
    {
        // 06:03 Europe/Istanbul (UTC+3) yerel bir formdan timezone bilgisi olmadan geldiğini varsay.
        var unspecified = new DateTime(2026, 4, 17, 6, 3, 0, DateTimeKind.Unspecified);

        var result = ToUtc(unspecified);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Year.Should().Be(2026);
        result.Month.Should().Be(4);
        result.Day.Should().Be(17);
        result.Hour.Should().Be(3);
        result.Minute.Should().Be(3);
    }

    [Fact]
    public void ToUtc_Should_CarryPreviousDay_When_UnspecifiedIsEarlyMorningIstanbul()
    {
        // 01:30 Europe/Istanbul = 22:30 önceki günün UTC saati; dashboard "bugün" (İstanbul takvimi) penceresine düşer.
        var unspecified = new DateTime(2026, 4, 17, 1, 30, 0, DateTimeKind.Unspecified);

        var result = ToUtc(unspecified);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Year.Should().Be(2026);
        result.Month.Should().Be(4);
        result.Day.Should().Be(16);
        result.Hour.Should().Be(22);
        result.Minute.Should().Be(30);
    }

    [Fact]
    public void ToUtc_Should_ProduceUtcInsideTodayWindow_ForLateEveningIstanbulLocal()
    {
        // 23:30 Europe/Istanbul -> 20:30 UTC aynı gün (DST'siz TR standart saati sabit UTC+3).
        // Bu senaryo dashboard "bugün" penceresinin (İstanbul-takvim, UTC sınırlı) payment'ı yakalamasını sağlar.
        var unspecified = new DateTime(2026, 4, 17, 23, 30, 0, DateTimeKind.Unspecified);

        var result = ToUtc(unspecified);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Year.Should().Be(2026);
        result.Month.Should().Be(4);
        result.Day.Should().Be(17);
        result.Hour.Should().Be(20);
        result.Minute.Should().Be(30);
    }
}
