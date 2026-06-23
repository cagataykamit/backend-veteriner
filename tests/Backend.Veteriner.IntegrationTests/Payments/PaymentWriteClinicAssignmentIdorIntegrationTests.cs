using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Payments;

/// <summary>Payment write clinic assignment IDOR (IDOR-7H).</summary>
[Collection("pilot-smoke-api")]
public sealed class PaymentWriteClinicAssignmentIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PaymentWriteClinicAssignmentIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static readonly string[] PaymentWritePermissions =
    [
        PermissionCatalog.Payments.Create,
        PermissionCatalog.Payments.Update,
    ];

    private static readonly DateTime ValidPaidAtUtc = DateTime.UtcNow.AddHours(-2);

    private async Task<string> IssuePaymentWriteTokenAsync(string email, Guid? clinicId = null)
        => await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            PaymentWritePermissions,
            clinicId);

    [Fact]
    public async Task Create_Should_Return403_When_NonTenantWideUserCreatesInUnassignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedPaymentWriterUserAsync(_factory.Services, hasher);
        var (clientId, _) = await SeedClientInClinicAsync(unassignedClinicId);

        var token = await IssuePaymentWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/payments", new
        {
            ClinicId = unassignedClinicId,
            ClientId = clientId,
            Amount = 150m,
            Currency = "TRY",
            Method = PaymentMethod.Cash,
            PaidAtUtc = ValidPaidAtUtc,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Create_Should_Return201_When_NonTenantWideUserCreatesInAssignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, assignedClinicId, _) =
            await IntegrationTestAuthHelper.SeedPaymentWriterUserAsync(_factory.Services, hasher);
        var (clientId, _) = await SeedClientInClinicAsync(assignedClinicId);

        var token = await IssuePaymentWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/payments", new
        {
            ClinicId = assignedClinicId,
            ClientId = clientId,
            Amount = 150m,
            Currency = "TRY",
            Method = PaymentMethod.Cash,
            PaidAtUtc = ValidPaidAtUtc,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_Should_Return403_And_NotPersist_When_UnassignedClinicExamination()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedPaymentWriterUserAsync(_factory.Services, hasher);
        var exam = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var examRow = await db.Examinations.AsNoTracking()
                .Where(e => e.Id == exam.ExaminationId)
                .Select(e => new { e.PetId, e.ClinicId })
                .SingleAsync();
            var clientId = await db.Pets.AsNoTracking()
                .Where(p => p.Id == examRow.PetId)
                .Select(p => p.ClientId)
                .SingleAsync();

            var token = await IssuePaymentWriteTokenAsync(email);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.PostAsJsonAsync("/api/v1/payments", new
            {
                ClinicId = unassignedClinicId,
                ClientId = clientId,
                PetId = examRow.PetId,
                ExaminationId = exam.ExaminationId,
                Amount = 150m,
                Currency = "TRY",
                Method = PaymentMethod.Cash,
                PaidAtUtc = ValidPaidAtUtc,
            });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");
        }

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await verifyDb.Payments.CountAsync(p =>
            p.ExaminationId == exam.ExaminationId);
        count.Should().Be(0);
    }

    [Fact]
    public async Task Update_Should_Return403_And_NotMutate_When_UnassignedClinicPayment()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedPaymentWriterUserAsync(_factory.Services, hasher);
        var paymentId = await IntegrationTestAuthHelper.SeedPaymentInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssuePaymentWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (clinicId, clientId, amount, paidAt, notes) = await GetPaymentSnapshotAsync(paymentId);

        var response = await client.PutAsJsonAsync($"/api/v1/payments/{paymentId}", new
        {
            ClinicId = clinicId,
            ClientId = clientId,
            Amount = amount + 10m,
            Currency = "TRY",
            Method = PaymentMethod.Cash,
            PaidAtUtc = paidAt,
            Notes = notes,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

        var (_, _, afterAmount, _, afterNotes) = await GetPaymentSnapshotAsync(paymentId);
        afterAmount.Should().Be(amount);
        afterNotes.Should().Be(notes);
    }

    [Fact]
    public async Task Update_Should_Return403_And_NotPull_When_EntityInOtherClinic_WithActiveAssignedContext()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, assignedClinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedPaymentWriterUserAsync(_factory.Services, hasher);
        var paymentId = await IntegrationTestAuthHelper.SeedPaymentInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssuePaymentWriteTokenAsync(email, assignedClinicId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (_, clientId, amount, paidAt, notes) = await GetPaymentSnapshotAsync(paymentId);

        var response = await client.PutAsJsonAsync($"/api/v1/payments/{paymentId}", new
        {
            ClinicId = assignedClinicId,
            ClientId = clientId,
            Amount = amount,
            Currency = "TRY",
            Method = PaymentMethod.Cash,
            PaidAtUtc = paidAt,
            Notes = "Pulled",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

        var (afterClinicId, _, _, _, afterNotes) = await GetPaymentSnapshotAsync(paymentId);
        afterClinicId.Should().Be(unassignedClinicId);
        afterNotes.Should().Be(notes);
    }

    [Fact]
    public async Task CreateUpdate_Should_Succeed_When_TenantAdminInOtherClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var (clientId, _) = await SeedClientInClinicAsync(extraClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/payments", new
        {
            ClinicId = extraClinicId,
            ClientId = clientId,
            Amount = 200m,
            Currency = "TRY",
            Method = PaymentMethod.Cash,
            PaidAtUtc = ValidPaidAtUtc,
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var paymentId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/payments/{paymentId}", new
        {
            ClinicId = extraClinicId,
            ClientId = clientId,
            Amount = 220m,
            Currency = "TRY",
            Method = PaymentMethod.Cash,
            PaidAtUtc = ValidPaidAtUtc,
            Notes = "Admin update",
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_Should_Return404_When_ForeignTenantPayment()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, _) =
            await IntegrationTestAuthHelper.SeedPaymentWriterUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var paymentId = await IntegrationTestAuthHelper.SeedPaymentInClinicAsync(
            _factory.Services,
            foreignClinicId);

        var token = await IssuePaymentWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/payments/{paymentId}", new
        {
            ClinicId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Amount = 100m,
            Currency = "TRY",
            Method = PaymentMethod.Cash,
            PaidAtUtc = ValidPaidAtUtc,
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(updateResponse)).Should().Be("Payments.NotFound");
    }

    [Fact]
    public async Task Create_Should_Return403_When_UserHasNoPaymentCreatePermission()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (_, _, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var (clientId, _) = await SeedClientInClinicAsync(extraClinicId);

        var (plainEmail, plainPassword) =
            await IntegrationTestAuthHelper.SeedPlainTenantMemberAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, plainEmail, plainPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/payments", new
        {
            ClinicId = extraClinicId,
            ClientId = clientId,
            Amount = 150m,
            Currency = "TRY",
            Method = PaymentMethod.Cash,
            PaidAtUtc = ValidPaidAtUtc,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(Guid ClientId, Guid TenantId)> SeedClientInClinicAsync(Guid clinicId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clinic = await db.Clinics.AsNoTracking().SingleAsync(c => c.Id == clinicId);
        var client = new Backend.Veteriner.Domain.Clients.Client(
            clinic.TenantId,
            $"PayWriteClient-{Guid.NewGuid():N}"[..14],
            "905551110044");
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return (client.Id, clinic.TenantId);
    }

    private async Task<(Guid ClinicId, Guid ClientId, decimal Amount, DateTime PaidAtUtc, string? Notes)> GetPaymentSnapshotAsync(
        Guid paymentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId);
        return (row.ClinicId, row.ClientId, row.Amount, row.PaidAtUtc, row.Notes);
    }
}
