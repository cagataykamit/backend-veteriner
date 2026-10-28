using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Tenants.Commands.Billing;

/// <summary>
/// Kullanıcı sağlayıcı ödeme sayfasından döndü (iyzico callback): açık oturum sağlayıcıya sorulur, ödeme alınmışsa abonelik
/// hemen etkinleşir; webhook gelmese de ödeme boşa gitmez. Dönüş isteğindeki veriye güvenilmez: istek yalnız oturumu bulmaya
/// yarar (kimlik ya da token), ödeme sağlayıcı sorgusuyla doğrulanır. Sonuç bulunan oturumun kimliğidir; yoksa <c>null</c>.
/// </summary>
public sealed record ConfirmCheckoutReturnCommand(Guid? CheckoutSessionId, string? ProviderToken)
    : IRequest<Result<Guid?>>, IIgnoreTenantWriteSubscriptionGuard;
