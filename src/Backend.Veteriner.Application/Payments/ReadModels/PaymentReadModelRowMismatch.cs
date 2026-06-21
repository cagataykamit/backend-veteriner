namespace Backend.Veteriner.Application.Payments.ReadModels;

/// <summary>
/// Payment list read-model row-sample parity sapması (PII yok).
/// Yalnızca <see cref="PaymentId"/> ve farklılaşan ilk alan adı (<see cref="Field"/>) raporlanır;
/// alan değerleri (ör. ClientName/Notes) loglanmaz.
/// </summary>
public sealed record PaymentReadModelRowMismatch(Guid PaymentId, string Field);
