using System.Net;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.OnlineAppointments;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Appointments;

/// <summary>
/// Takvim kilidi (<c>AppointmentAssignment.LockScheduleAsync</c>): aynı boş saate eşzamanlı yazan istekler gerçek SQL Server'da
/// yarıştırılır; çakışma denetimi kilitten sonra okuduğu için saat yalnız bir kez dolar, kaybedenler çakışma hatası alır (500 yok).
/// Klinik üst üste binmeye kapalıdır (varsayılan) ve hekim yoktur: farklı hayvanların randevuları da aynı saatte çakışır.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class AppointmentScheduleConcurrencyIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const int Contenders = 6;

    private readonly CustomWebApplicationFactory _factory;

    public AppointmentScheduleConcurrencyIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Concurrent_Create_For_Same_Slot_Should_Book_Exactly_One()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var slot = OnlineAppointmentTestContext.NextIstanbul(DayOfWeek.Tuesday, 10);
        var pets = new List<Guid>();
        for (var i = 0; i < Contenders; i++)
            pets.Add((await ctx.SeedClientWithPetAsync()).PetId);

        var responses = await Task.WhenAll(pets.Select(petId => ctx.PostAppointmentAsync(petId, slot, durationMinutes: 30)));

        responses.Count(r => r.IsSuccessStatusCode).Should().Be(1, "aynı saat yalnız bir kez dolar");
        foreach (var lost in responses.Where(r => !r.IsSuccessStatusCode))
            (await IntegrationTestProblemDetails.ReadCodeAsync(lost)).Should().Be("Appointments.ClinicTimeConflict");
        (await CountScheduledAsync(ctx.ClinicId)).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_Approve_Of_Requests_For_Same_Slot_Should_Book_Exactly_One()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var slot = OnlineAppointmentTestContext.NextIstanbul(DayOfWeek.Wednesday, 11);
        var approvals = new List<(Guid RequestId, Guid ClientId, Guid PetId)>();
        for (var i = 0; i < Contenders; i++)
        {
            var (clientId, petId) = await ctx.SeedClientWithPetAsync();
            approvals.Add((await SeedPendingRequestAsync(ctx, slot), clientId, petId));
        }

        var responses = await Task.WhenAll(approvals.Select(a =>
            ctx.Http.PostAsJsonAsync($"/api/v1/appointment-requests/{a.RequestId}/approve", new { clientId = a.ClientId, petId = a.PetId })));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, "aynı saate ikinci onay çift randevu açamaz");
        foreach (var lost in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
            (await IntegrationTestProblemDetails.ReadCodeAsync(lost)).Should().Be("Appointments.ClinicTimeConflict");
        (await CountScheduledAsync(ctx.ClinicId)).Should().Be(1);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = approvals.Select(a => a.RequestId).ToList();
        (await db.AppointmentRequests.AsNoTracking().CountAsync(r => ids.Contains(r.Id) && r.Status == AppointmentRequestStatus.Pending))
            .Should().Be(Contenders - 1, "kaybeden onay geri alınır, talep beklemede kalır");
    }

    private async Task<Guid> SeedPendingRequestAsync(OnlineAppointmentTestContext ctx, DateTime startUtc)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = AppointmentRequest.CreateFromLink(
            ctx.TenantId, ctx.ClinicId, null, new PetText("Pamuk", null, null), null, startUtc,
            "Eşzamanlı Sahip", $"es-{Guid.NewGuid():N}@example.com", "05551234567", null, DateTime.UtcNow).Value!;
        db.AppointmentRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private async Task<int> CountScheduledAsync(Guid clinicId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Appointments.AsNoTracking().CountAsync(a => a.ClinicId == clinicId && a.Status == AppointmentStatus.Scheduled);
    }
}
