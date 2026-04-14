namespace Backend.Veteriner.Application.Common.Options;

/// <summary>
/// Request / MediatR / SQL / outbound billing HTTP ve kritik handler adım ölçümleri için eşikler.
/// </summary>
public sealed class PerformanceDiagnosticsOptions
{
    public const string SectionName = "PerformanceDiagnostics";

    /// <summary>Tüm bu tanıdaki uyarıları kapatır (unit test veya prod’da gerektiğinde).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>HTTP pipeline tam süre; üzerinde Warning (method, path, correlation).</summary>
    public int HttpRequestWarningMs { get; set; } = 750;

    /// <summary>MediatR pipeline (tek handler) toplam süre uyarısı.</summary>
    public int MediatRSlowMs { get; set; } = 400;

    /// <summary>EF komutu yürütme süresi; üzerinde kısa SQL özeti ile Warning.</summary>
    public int SlowSqlMs { get; set; } = 200;

    /// <summary>
    /// Havuzdan bağlantı alma / fiziksel açılış süresi (pool exhaustion veya ağ gecikmesi sinyali).
    /// </summary>
    public int SlowConnectionOpenMs { get; set; } = 1000;

    /// <summary>Iyzico vb. dış çağrı süreleri.</summary>
    public int OutboundHttpWarningMs { get; set; } = 500;

    /// <summary>Kritik handler toplam süre; üzerinde HandlerPerf.Critical Warning.</summary>
    public int CriticalHandlerTotalMsWarning { get; set; } = 400;

    /// <summary>Tek bir MarkStep süresi; üzerinde aynı logda en yavaş adım vurgulanır.</summary>
    public int CriticalHandlerSlowStepMsWarning { get; set; } = 150;

    /// <summary>
    /// Development ortamında kritik handler’lar için her başarılı istekte Information + adım özeti.
    /// Prod’da false bırakın (spam önleme).
    /// </summary>
    public bool AlwaysLogCriticalHandlerMetricsInDevelopment { get; set; }

    public int SqlPreviewMaxChars { get; set; } = 200;
}
