using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Clinics;

/// <summary>
/// PUT /clinics/{id}/working-hours mevcut haftayı siler ve yeni satırları yazar; komut artık tek veritabanı
/// işleminde koşar (ITransactionalRollbackOnFailureRequest). Bu test işlem sarmalının mutlu yolu bozmadığını
/// (sil + 7 ekle + commit) doğrular. Handler içindeki iş kuralı hatası yolu HTTP'den tetiklenemez: validator
/// domain kuralını birebir yansıtır; geri alma kanıtı için bkz. Auth/InviteSignupTransactionIntegrationTests.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ClinicWorkingHoursTransactionIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] UpdatePermissions = [PermissionCatalog.Clinics.Update];

    private readonly CustomWebApplicationFactory _factory;

    public ClinicWorkingHoursTransactionIntegrationTests(CustomWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task PutWorkingHours_Should_ReplaceWeek_InSingleTransaction()
    {
        var (clinicId, http) = await SeedClinicWithWeekAsync();
        (await LoadWeekAsync(clinicId)).Should().OnlyContain(r => r.OpensAt == new TimeOnly(9, 0));

        var items = Enum.GetValues<DayOfWeek>()
            .Select(day => new ClinicWorkingHourDto(day, IsClosed: false, new TimeOnly(10, 0), new TimeOnly(19, 0), null, null))
            .ToList();

        var response = await http.PutAsJsonAsync(
            $"/api/v1/clinics/{clinicId}/working-hours",
            new UpdateClinicWorkingHoursRequest(items));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await LoadWeekAsync(clinicId);
        after.Should().HaveCount(7, "eski hafta silinip yeni hafta yazılmalı; çift kayıt kalmamalı");
        after.Should().OnlyContain(r => r.OpensAt == new TimeOnly(10, 0) && r.ClosesAt == new TimeOnly(19, 0));
    }

    private async Task<List<ClinicWorkingHour>> LoadWeekAsync(Guid clinicId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ClinicWorkingHours.AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .OrderBy(r => r.DayOfWeek)
            .ToListAsync();
    }

    /// <summary>Varsayılan kiracıda yeni klinik + 09:00–18:00 hafta + bu kliniğe atanmış ClinicAdmin kullanıcısı.</summary>
    private async Task<(Guid ClinicId, HttpClient Http)> SeedClinicWithWeekAsync()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid clinicId;
        string email;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);

            var clinic = new Clinic(tenant.Id, $"Wh-{Guid.NewGuid():N}"[..14], "Istanbul");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            foreach (var day in Enum.GetValues<DayOfWeek>())
            {
                db.ClinicWorkingHours.Add(ClinicWorkingHour.Create(
                    tenant.Id, clinic.Id, day, isClosed: false,
                    new TimeOnly(9, 0), new TimeOnly(18, 0), null, null));
            }

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"wh-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"));
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id));
            await db.SaveChangesAsync();

            clinicId = clinic.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, UpdatePermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (clinicId, http);
    }
}
