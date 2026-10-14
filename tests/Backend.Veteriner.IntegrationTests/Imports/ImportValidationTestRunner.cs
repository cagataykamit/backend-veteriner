using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Veteriner.Infrastructure.Imports;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend.IntegrationTests.Imports;

/// <summary>
/// Arka plan doğrulamasını (ADR-019 K13) testte bitirir: arka plan işçisi test yapılandırmasında kapalıdır
/// (<c>Imports:Commit:WorkerEnabled=false</c>), işçi doğrudan ve yalnız verilen parti için çalıştırılır (başka testlerin
/// kuyrukta bıraktığı partilere dokunmaz).
/// </summary>
internal static class ImportValidationTestRunner
{
    /// <summary>Doğrulamayı başlatır (202), işçiyi çalıştırır ve son parti nesnesini (<c>GET .../{id}</c>) döndürür.</summary>
    public static async Task<JsonElement> ValidateAsync(IServiceProvider services, HttpClient client, Guid id)
    {
        using var response = await client.PostAsync($"/api/v1/imports/batches/{id}/validate", null);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Validating");

        await RunWorkerAsync(services, id);

        using var batch = await client.GetAsync($"/api/v1/imports/batches/{id}");
        batch.StatusCode.Should().Be(HttpStatusCode.OK, await batch.Content.ReadAsStringAsync());
        return await batch.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Kuyruktaki doğrulamayı işler; parti alınamazsa (kuyrukta değil / kira başka işçide) test başarısız olur.</summary>
    public static async Task RunWorkerAsync(IServiceProvider services, Guid id)
        => (await Processor(services).ProcessBatchAsync(id, CancellationToken.None))
            .Should().BeTrue("doğrulama işi kuyrukta olmalı ve işçi tarafından alınabilmeli");

    public static ImportValidationProcessor Processor(IServiceProvider services, string? workerId = null)
        => new(
            services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<ImportValidationProcessor>.Instance,
            workerId ?? $"w-val-{Guid.NewGuid():N}"[..24]);
}
