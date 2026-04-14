namespace Backend.Veteriner.Application.Common.Diagnostics;

/// <summary>
/// API pipeline içinde yaklaşık eşzamanlı HTTP istek sayısı (stall / pool / lock teşhisi için).
/// Yalnızca API projesindeki counting middleware tarafından artırılır; arka plan işleri dahil değildir.
/// </summary>
public static class ActiveHttpRequestCounter
{
    private static int _active;

    public static int ApproximateActive => Volatile.Read(ref _active);

    public static void EnterHttpRequest() => Interlocked.Increment(ref _active);

    public static void LeaveHttpRequest() => Interlocked.Decrement(ref _active);
}
