using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auth;

/// <summary>
/// POST /public/invites/{token}/signup-and-accept önce kullanıcıyı yazar, sonra daveti kabul eder.
/// Repository yazımları anında commit ettiği için komut tek veritabanı işleminde koşmalı: kabul iş kuralıyla
/// reddedilirse (davetteki klinik pasif) kullanıcı da kalmamalı; aksi halde aynı e-posta ile yeniden kayıt
/// "Users.DuplicateEmail" ile kalıcı olarak engellenir.
/// </summary>
public sealed class InviteSignupTransactionIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public InviteSignupTransactionIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static object[] AllCurrentDocuments() =>
        LegalDocumentCatalog.CurrentVersions
            .Select(kv => (object)new { documentType = kv.Key.ToString(), version = kv.Value })
            .ToArray();

    [Fact]
    public async Task SignupAndAccept_Should_NotLeaveUser_When_AcceptFails_Because_ClinicInactive()
    {
        var rawToken = $"tx-invite-{Guid.NewGuid():N}";
        var email = $"tx-invitee-{Guid.NewGuid():N}@example.com";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokenHash = scope.ServiceProvider.GetRequiredService<ITokenHashService>();
            var now = DateTime.UtcNow;

            var tenant = new Tenant($"Tx Kiracı {Guid.NewGuid():N}"[..24]);
            var clinic = new Clinic(tenant.Id, "Tx Klinik", "İzmir");
            db.Tenants.Add(tenant);
            db.Clinics.Add(clinic);
            db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Premium, now, 14));
            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "Veteriner");
            db.TenantInvites.Add(TenantInvite.CreatePending(
                tenant.Id, clinic.Id, email, tokenHash.ComputeSha256(rawToken), claim.Id, now.AddDays(3), now));
            await db.SaveChangesAsync();

            // Davet gönderildikten sonra klinik pasifleştirildi → kabul iş kuralıyla reddedilir.
            clinic.Deactivate();
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync($"/api/v1/public/invites/{rawToken}/signup-and-accept",
            new { password = "Abc12345!", acceptedLegalDocuments = AllCurrentDocuments() });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Invites.ClinicInvalid");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var normalized = email.ToLowerInvariant();
            (await db.Users.AsNoTracking().AnyAsync(u => u.Email == normalized))
                .Should().BeFalse("kabul başarısızsa kullanıcı kaydı geri alınmalı; aksi halde e-posta kalıcı olarak kilitlenir");
            var invite = await db.TenantInvites.AsNoTracking().SingleAsync(i => i.Email == normalized);
            invite.Status.Should().Be(TenantInviteStatus.Pending, "davet kabul edilmemiş kalmalı");
        }

        // Aynı e-posta ile yeniden deneme çift kayıt hatasına değil, aynı iş kuralı hatasına düşmeli.
        var retry = await _client.PostAsJsonAsync($"/api/v1/public/invites/{rawToken}/signup-and-accept",
            new { password = "Abc12345!", acceptedLegalDocuments = AllCurrentDocuments() });
        (await IntegrationTestProblemDetails.ReadCodeAsync(retry)).Should().Be("Invites.ClinicInvalid");
    }
}
