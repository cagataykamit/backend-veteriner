namespace Backend.Veteriner.Application.Imports.Commit;

/// <summary>Uygulama (commit) arka plan işi ayarları (<c>Imports:Commit</c>; IMPORT_API_CONTRACT.md §11).</summary>
public sealed class ImportCommitOptions
{
    public const string SectionName = "Imports:Commit";

    /// <summary>False ise hosted service partileri almaz (işlemci elle çağrılabilir kalır; testler).</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>İş yokken yoklama aralığı (saniye).</summary>
    public int PollIntervalSeconds { get; set; } = 2;

    /// <summary>Parça büyüklüğü (sahip birimi); geri basınç ve kira yenilemesi parça başına.</summary>
    public int OwnersPerChunk { get; set; } = 50;

    public ImportBackpressureOptions Backpressure { get; set; } = new();
}

/// <summary>
/// Geri basınç (R4): eşikler sağlık uçlarının eşiklerinden <see cref="HeadroomRatio"/> ile türetilir; böylece işçi,
/// <c>/health/ready</c> Degraded (503) olmadan önce yavaşlar.
/// </summary>
public sealed class ImportBackpressureOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Sağlık eşiğinin bu oranında beklemeye başlanır (0,1–1).</summary>
    public double HeadroomRatio { get; set; } = 0.6;

    /// <summary>Açık projeksiyonun bekleyen olay sayısı üst sınırı.</summary>
    public int ProjectionPendingLimit { get; set; } = 200;

    /// <summary>Eşik aşılınca yeniden ölçme aralığı (saniye).</summary>
    public int WaitSeconds { get; set; } = 2;

    /// <summary>Tek parça için en uzun bekleme; sonra parça yine işlenir (iş sonsuza kadar durmaz).</summary>
    public int MaxWaitSeconds { get; set; } = 600;
}
