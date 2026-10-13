namespace Backend.Veteriner.Application.Reports.OutstandingBalances;

/// <summary>CSV ve XLSX dışa aktarma başlıkları (tek tanım).</summary>
internal static class OutstandingBalancesReportColumns
{
    public static readonly string[] Headers =
    [
        "Müşteri", "Telefon", "Kalan Tutar", "Para Birimi", "Açık Ücret Sayısı", "En Eski Açık Ücret"
    ];
}
