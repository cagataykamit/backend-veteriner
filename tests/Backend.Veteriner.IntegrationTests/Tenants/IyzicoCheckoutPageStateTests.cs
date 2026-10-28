using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Infrastructure.Billing;
using AwesomeAssertions;

namespace Backend.IntegrationTests.Tenants;

/// <summary>
/// iyzico ödeme formu sorgusunun sayfa durumuna eşlenmesi; satırlar sandbox'ta gözlenen yanıtlardır (2026-10-28):
/// açılmamış/vazgeçilmiş form 5122, kartı reddedilen 10051 + FAILURE, 3D'si iptal edilen success + FAILURE (üçü de
/// yeniden ödenebilir); başarılı ödeme success + SUCCESS ve form artık "Geçersiz token" verir.
/// </summary>
public sealed class IyzicoCheckoutPageStateTests
{
    [Theory]
    [InlineData("failure", null, "5122", CheckoutPageState.Payable)]
    [InlineData("failure", "FAILURE", "10051", CheckoutPageState.Payable)]
    [InlineData("success", "FAILURE", null, CheckoutPageState.Payable)]
    [InlineData("success", "SUCCESS", null, CheckoutPageState.Paid)]
    [InlineData("success", "INIT_THREEDS", null, CheckoutPageState.Unusable)]
    [InlineData("failure", null, "1000", CheckoutPageState.Unusable)]
    [InlineData(null, null, null, CheckoutPageState.Unusable)]
    public void ToPageState_Should_Map_Retrieve_Response(string? status, string? paymentStatus, string? errorCode, CheckoutPageState expected)
        => IyzicoBillingCheckoutProvider.ToPageState(status, paymentStatus, errorCode).Should().Be(expected);
}
