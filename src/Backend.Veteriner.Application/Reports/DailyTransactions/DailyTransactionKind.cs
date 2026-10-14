namespace Backend.Veteriner.Application.Reports.DailyTransactions;

/// <summary>Günlük işlem dökümündeki mali olay türü (sözleşme: <c>DAILY_TRANSACTIONS_REPORT_API_CONTRACT.md</c> K-D6-1).</summary>
public enum DailyTransactionKind
{
    Charge = 0,
    ChargeVoid = 1,
    ChargeCorrection = 2,
    Payment = 3,
    PaymentVoid = 4,
    PaymentCorrection = 5
}
