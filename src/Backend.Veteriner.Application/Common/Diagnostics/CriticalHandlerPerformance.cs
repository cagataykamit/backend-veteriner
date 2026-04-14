using Backend.Veteriner.Application.Common.Options;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Common.Diagnostics;

/// <summary>
/// Kritik handler’larda MarkStep toplamlarını eşik üstünde Warning, Development’ta isteğe bağlı Information ile yazar.
/// </summary>
public static class CriticalHandlerPerformance
{
    public static void TryLogCriticalHandler(
        ILogger logger,
        PerformanceDiagnosticsOptions options,
        string handlerName,
        int stepCount,
        string slowestStep,
        long slowestStepMs,
        long totalElapsedMs)
    {
        if (!options.Enabled)
            return;

        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var isDev = string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
        var always = options.AlwaysLogCriticalHandlerMetricsInDevelopment && isDev;
        var isSlow = totalElapsedMs >= options.CriticalHandlerTotalMsWarning
                     || slowestStepMs >= options.CriticalHandlerSlowStepMsWarning;

        if (!always && !isSlow)
            return;

        var level = isSlow ? LogLevel.Warning : LogLevel.Information;
        logger.Log(
            level,
            "HandlerPerf.Critical {HandlerName} StepCount={StepCount} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} TotalElapsedMs={TotalElapsedMs} ActiveHttpRequestsApprox={ActiveHttpRequestsApprox}",
            handlerName,
            stepCount,
            slowestStep,
            slowestStepMs,
            totalElapsedMs,
            ActiveHttpRequestCounter.ApproximateActive);
    }
}
