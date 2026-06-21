namespace Backend.Veteriner.Application.Projections.Payments;

/// <summary>
/// Payment list read-model (PaymentReadModels) freshness/drift sinyali (CQRS-14F).
/// <see cref="PaymentProjectionHealthEvaluator"/> tarafından, finance kuyruk sinyallerinden bağımsız bir
/// ek sağlık boyutu olarak değerlendirilir. PII içermez (yalnızca sayım + flag).
///
/// Bu sinyal yalnızca rollout için anlamlıdır:
/// - Projection ve list read flag'i kapalıyken hiç hesaplanmaz (production-safe boş read-model).
/// - Hesaplandığında count drift = Command Payments sayısı - Query PaymentReadModels sayısı (global).
/// </summary>
public sealed record PaymentReadModelHealthSignal(
    long CommandPaymentCount,
    long ReadModelCount,
    bool PaymentsListReadEnabled)
{
    public long CountDrift => CommandPaymentCount - ReadModelCount;

    public long AbsoluteCountDrift => Math.Abs(CountDrift);

    public bool CountInSync => CommandPaymentCount == ReadModelCount;
}
