using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Tenants.Commands.Checkout;

/// <summary>
/// Kullanıcı sağlayıcının ödeme sayfasında vazgeçti: açık oturum kapatılır. Sağlayıcı o sayfanın token'ını geçersiz kıldığı için
/// oturum açık kalırsa sonraki "ödeme başlat" ölü sayfaya yönlenir. Oturum zaten kapalıysa başarı döner (tekrarlanabilir).
/// </summary>
public sealed record CancelSubscriptionCheckoutCommand(Guid TenantId, Guid CheckoutSessionId)
    : IRequest<Result>, IIgnoreTenantWriteSubscriptionGuard;
