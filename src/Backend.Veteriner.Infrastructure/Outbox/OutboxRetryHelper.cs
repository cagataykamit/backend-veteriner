using Backend.Veteriner.Infrastructure.Persistence.Entities;

namespace Backend.Veteriner.Infrastructure.Outbox;

internal static class OutboxRetryHelper
{
    public static TimeSpan ComputeBackoff(int baseDelaySec, int retry)
    {
        var baseDelay = Math.Max(1, baseDelaySec);
        var seconds = Math.Min(baseDelay * (int)Math.Pow(2, retry - 1), 600);

        var jitterFactor = (Random.Shared.NextDouble() * 0.4) - 0.2;
        var withJitter = seconds + seconds * jitterFactor;

        return TimeSpan.FromSeconds(Math.Max(1, (int)withJitter));
    }

    public static void ApplyFailure(OutboxMessage msg, OutboxOptions options, Exception ex)
    {
        msg.RetryCount++;
        msg.LastError = ex.Message;
        msg.Error = ex.ToString();

        if (msg.RetryCount >= options.MaxRetryCount)
            msg.DeadLetterAtUtc = DateTime.UtcNow;
        else
            msg.NextAttemptAtUtc = DateTime.UtcNow.Add(ComputeBackoff(options.BaseDelaySeconds, msg.RetryCount));
    }

    public static void ApplySuccess(OutboxMessage msg)
    {
        msg.ProcessedAtUtc = DateTime.UtcNow;
        msg.LastError = null;
        msg.Error = null;
        msg.NextAttemptAtUtc = null;
    }
}
