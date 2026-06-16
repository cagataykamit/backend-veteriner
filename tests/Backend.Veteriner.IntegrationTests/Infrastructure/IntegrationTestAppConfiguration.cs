namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// IntegrationTests ortamına özel appsettings override anahtarları.
/// Tüm <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> türevleri aynı sözlüğü kullanmalı.
/// </summary>
internal static class IntegrationTestAppConfiguration
{
    /// <summary>
    /// Login ve diğer endpoint policy'lerinin test suite'ini 429 ile kırmasını önler.
    /// Production/Development config'ine dokunulmaz.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> RateLimitingDisabled { get; } =
        new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "false"
        };
}
