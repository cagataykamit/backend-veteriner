using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>
/// Bir müşteriye portal daveti gidebilir mi — tek kural (ADR-031 K4, ADR-025). Davet oluşturma, panel durumu ve davet
/// kabulü aynı kuralı kullanır: KVKK "Ret" olan müşteriye davet gitmez; davet müşteri kaydındaki e-postaya gider, e-posta
/// yoksa davet edilemez. Hatırlatma istememek (<see cref="Client.RemindersOptedOut"/>) daveti engellemez: davet personelin
/// başlattığı işlemsel e-postadır, hatırlatma değildir (ADR-031 K4 eki, 2026-11-01).
/// </summary>
public static class OwnerPortalInvitePolicy
{
    public static Result CanInvite(Client client)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (client.IsWalkIn)
            return Result.Failure("OwnerPortal.ClientNotInvitable", "Peşin müşteri sistem kaydıdır; portala davet edilemez.");

        if (string.IsNullOrWhiteSpace(client.Email))
            return Result.Failure("OwnerPortal.ClientEmailMissing", "Müşteri kaydında e-posta yok; davet gönderilemez.");

        if (client.KvkkConsentStatus == KvkkConsentStatus.Declined)
        {
            return Result.Failure(
                "OwnerPortal.ClientOptedOut",
                "Müşteri KVKK rızasını reddetmiş; portal daveti gönderilemez.");
        }

        return Result.Success();
    }
}
