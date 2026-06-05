using System.Net.Http.Json;
using System.Text.Json;

namespace Backend.IntegrationTests.Infrastructure;

internal static class IntegrationTestProblemDetails
{
    public static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (json.TryGetProperty("code", out var code))
            return code.GetString();

        if (json.TryGetProperty("extensions", out var extensions)
            && extensions.TryGetProperty("code", out var extCode))
            return extCode.GetString();

        return null;
    }
}
