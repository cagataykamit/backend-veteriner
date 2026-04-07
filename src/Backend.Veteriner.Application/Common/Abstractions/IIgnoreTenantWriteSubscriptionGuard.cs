namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// <see cref="Common.Behaviors.TenantWriteSubscriptionGuardBehavior{TRequest,TResponse}"/> bu isteği atlar.
/// Kimlik oturumu, public onboarding, platform kiracı oluşturma ve JWT kiracı bağlamı olmadan çalışan davet kabulü gibi akışlar için kullanılır.
/// </summary>
public interface IIgnoreTenantWriteSubscriptionGuard;
