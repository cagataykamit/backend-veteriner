using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Hedef paketin sınırları ve kiracının bugünkü kullanımı (ADR-035 2026-11-08, paket düşürmede seçenek A). Kullanıcı sayımı davet
/// kuralıyla aynıdır (<see cref="TenantSubscriptionSeatEvaluator"/>): üye + süresi dolmamış bekleyen davet. Pasif klinik sayılmaz.
/// <see cref="MaxActiveClinics"/> <c>null</c> ise klinik sınırı yoktur.
/// </summary>
public sealed record SubscriptionPlanFit(
    SubscriptionPlanCode TargetPlanCode,
    int? MaxActiveClinics,
    int MaxUsers,
    int ActiveClinicCount,
    int MemberCount,
    int PendingInviteCount)
{
    public int ExcessClinicCount => MaxActiveClinics is { } max ? Math.Max(0, ActiveClinicCount - max) : 0;

    public int ExcessUserCount => Math.Max(0, MemberCount + PendingInviteCount - MaxUsers);

    public bool Fits => ExcessClinicCount == 0 && ExcessUserCount == 0;
}
