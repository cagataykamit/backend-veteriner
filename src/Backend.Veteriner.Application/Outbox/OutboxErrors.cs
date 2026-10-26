using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Outbox;

/// <summary>Outbox yönetim uçlarının hata kodları. HTTP eşlemesi controller'dadır (uçlar <c>ResultExtensions</c> şeklini kullanmaz).</summary>
public static class OutboxErrors
{
    public static readonly Error NotFound = new("Outbox.NotFound", "Outbox mesajı bulunamadı.");

    public static readonly Error NotDeadLetter = new("Outbox.NotDeadLetter", "Mesaj dead-letter değil.");
}
