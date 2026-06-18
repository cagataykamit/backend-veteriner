using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Operational;

[Collection("products-api")]
public sealed class OperationalListsEndpointPaginationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public OperationalListsEndpointPaginationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Pets_GetList_Page1_PageSize25_Should_ReturnRequestedPageSize()
    {
        var ctx = await SeedPetsScenarioAsync(30);
        var client = CreateClient(ctx.Token);

        var json = await GetPagedJsonAsync(client, "/api/v1/pets?Page=1&PageSize=25");

        json.GetProperty("page").GetInt32().Should().Be(1);
        json.GetProperty("pageSize").GetInt32().Should().Be(25);
        json.GetProperty("items").GetArrayLength().Should().Be(25);
        json.GetProperty("totalItems").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task Pets_GetList_Page2_PageSize25_Should_ReturnDifferentItemsThanPage1()
    {
        var ctx = await SeedPetsScenarioAsync(30);
        var client = CreateClient(ctx.Token);

        var page1 = await GetPagedJsonAsync(client, "/api/v1/pets?Page=1&PageSize=25");
        var page2 = await GetPagedJsonAsync(client, "/api/v1/pets?Page=2&PageSize=25");

        page2.GetProperty("page").GetInt32().Should().Be(2);
        page2.GetProperty("pageSize").GetInt32().Should().Be(25);
        page2.GetProperty("items").GetArrayLength().Should().Be(5);

        var page1Ids = page1.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToHashSet();
        var page2Ids = page2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToHashSet();
        page2Ids.Overlaps(page1Ids).Should().BeFalse();
    }

    [Fact]
    public async Task Appointments_GetList_Page1_PageSize25_Should_ReturnRequestedPageSize()
    {
        var ctx = await SeedAppointmentsScenarioAsync(30);
        var client = CreateClient(ctx.Token);

        var json = await GetPagedJsonAsync(
            client,
            $"/api/v1/appointments?Page=1&PageSize=25&clinicId={ctx.ClinicId:D}");

        json.GetProperty("page").GetInt32().Should().Be(1);
        json.GetProperty("pageSize").GetInt32().Should().Be(25);
        json.GetProperty("items").GetArrayLength().Should().Be(25);
    }

    [Fact]
    public async Task Appointments_GetList_Without_ClinicScope_Should_Return400_ClinicScopeRequired()
    {
        var ctx = await SeedAppointmentsScenarioAsync(5);
        var client = CreateClient(ctx.Token);

        // Aktif clinic context yok (token clinic claim taşımıyor) ve clinicId verilmedi:
        // tüm kiracı randevuları DÖNMEMELİ; güvenli application error beklenir.
        var response = await client.GetAsync("/api/v1/appointments?Page=1&PageSize=25");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Appointments.ClinicScopeRequired");
    }

    [Fact]
    public async Task Clients_GetList_Page1_PageSize25_Should_ReturnRequestedPageSize()
    {
        var ctx = await SeedClientsScenarioAsync(30);
        var client = CreateClient(ctx.Token);

        var json = await GetPagedJsonAsync(client, "/api/v1/clients?Page=1&PageSize=25");

        json.GetProperty("page").GetInt32().Should().Be(1);
        json.GetProperty("pageSize").GetInt32().Should().Be(25);
        json.GetProperty("items").GetArrayLength().Should().Be(25);
        json.GetProperty("totalItems").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task Clients_GetList_Page2_PageSize25_Should_ReturnDifferentItemsThanPage1()
    {
        var ctx = await SeedClientsScenarioAsync(30);
        var client = CreateClient(ctx.Token);

        var page1 = await GetPagedJsonAsync(client, "/api/v1/clients?Page=1&PageSize=25");
        var page2 = await GetPagedJsonAsync(client, "/api/v1/clients?Page=2&PageSize=25");

        page2.GetProperty("page").GetInt32().Should().Be(2);
        page2.GetProperty("pageSize").GetInt32().Should().Be(25);

        var page1Ids = page1.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToHashSet();
        var page2Ids = page2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToHashSet();
        page2Ids.Overlaps(page1Ids).Should().BeFalse();
    }

    [Fact]
    public async Task Examinations_GetList_Without_ClinicScope_Should_Return400_ClinicScopeRequired()
    {
        var ctx = await SeedExaminationsScenarioAsync(5);
        var client = CreateClient(ctx.Token);

        // Aktif clinic context yok (token clinic claim taşımıyor) ve clinicId verilmedi:
        // tüm kiracı muayeneleri DÖNMEMELİ; güvenli application error beklenir.
        var response = await client.GetAsync("/api/v1/examinations?Page=1&PageSize=25");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Examinations.ClinicScopeRequired");
    }

    [Fact]
    public async Task Treatments_GetList_Without_ClinicScope_Should_Return400_ClinicScopeRequired()
    {
        var ctx = await SeedTreatmentsScenarioAsync(5);
        var client = CreateClient(ctx.Token);

        var response = await client.GetAsync("/api/v1/treatments?Page=1&PageSize=25");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Treatments.ClinicScopeRequired");
    }

    [Fact]
    public async Task Prescriptions_GetList_Without_ClinicScope_Should_Return400_ClinicScopeRequired()
    {
        var ctx = await SeedPrescriptionsScenarioAsync(5);
        var client = CreateClient(ctx.Token);

        var response = await client.GetAsync("/api/v1/prescriptions?Page=1&PageSize=25");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Prescriptions.ClinicScopeRequired");
    }

    [Fact]
    public async Task Vaccinations_GetList_Without_ClinicScope_Should_Return400_ClinicScopeRequired()
    {
        var ctx = await SeedVaccinationsScenarioAsync(5);
        var client = CreateClient(ctx.Token);

        var response = await client.GetAsync("/api/v1/vaccinations?Page=1&PageSize=25");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Vaccinations.ClinicScopeRequired");
    }

    [Fact]
    public async Task Hospitalizations_GetList_Without_ClinicScope_Should_Return400_ClinicScopeRequired()
    {
        var ctx = await SeedHospitalizationsScenarioAsync(5);
        var client = CreateClient(ctx.Token);

        var response = await client.GetAsync("/api/v1/hospitalizations?Page=1&PageSize=25");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Hospitalizations.ClinicScopeRequired");
    }

    [Fact]
    public async Task LabResults_GetList_Without_ClinicScope_Should_Return400_ClinicScopeRequired()
    {
        var ctx = await SeedLabResultsScenarioAsync(5);
        var client = CreateClient(ctx.Token);

        var response = await client.GetAsync("/api/v1/lab-results?Page=1&PageSize=25");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("LabResults.ClinicScopeRequired");
    }

    [Fact]
    public async Task Payments_GetList_Without_ClinicScope_Should_Return400_ClinicScopeRequired()
    {
        var ctx = await SeedPaymentsScenarioAsync(5);
        var client = CreateClient(ctx.Token);

        var response = await client.GetAsync("/api/v1/payments?Page=1&PageSize=25");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Payments.ClinicScopeRequired");
    }

    [Fact]
    public async Task Treatments_GetList_Page1_PageSize25_Should_ReturnRequestedPageSize()
    {
        var ctx = await SeedTreatmentsScenarioAsync(30);
        var client = CreateClient(ctx.Token);

        var json = await GetPagedJsonAsync(
            client,
            $"/api/v1/treatments?Page=1&PageSize=25&clinicId={ctx.ClinicId:D}");

        json.GetProperty("page").GetInt32().Should().Be(1);
        json.GetProperty("pageSize").GetInt32().Should().Be(25);
        json.GetProperty("items").GetArrayLength().Should().Be(25);
    }

    [Fact]
    public async Task Vaccinations_GetList_Page1_PageSize25_Should_ReturnRequestedPageSize_AndVaccineName()
    {
        var ctx = await SeedVaccinationsScenarioAsync(30);
        var client = CreateClient(ctx.Token);

        var json = await GetPagedJsonAsync(
            client,
            $"/api/v1/vaccinations?Page=1&PageSize=25&clinicId={ctx.ClinicId:D}");

        json.GetProperty("page").GetInt32().Should().Be(1);
        json.GetProperty("pageSize").GetInt32().Should().Be(25);
        json.GetProperty("items").GetArrayLength().Should().Be(25);
        json.GetProperty("items")[0].GetProperty("vaccineName").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private HttpClient CreateClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> GetPagedJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>())!;
    }

    private sealed record TenantTokenCtx(string Token, Guid TenantId, Guid ClinicId);

    private async Task<TenantTokenCtx> SeedClientsScenarioAsync(int clientCount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"CliPg-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");

        var clients = Enumerable.Range(1, clientCount)
            .Select(i => new Client(tenant.Id, $"Müşteri-{i:D3}", $"90555{i:D7}", $"cli{i}@example.com"))
            .ToList();

        db.Add(tenant);
        db.Add(clinic);
        db.AddRange(clients);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        var token = IssueToken(jwt, tenant.Id, "Clients.Read");
        return new TenantTokenCtx(token, tenant.Id, clinic.Id);
    }

    private async Task<TenantTokenCtx> SeedTreatmentsScenarioAsync(int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"TrtPg-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var clientEntity = new Client(tenant.Id, "Müşteri", "905551110003", "trtpg@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, clientEntity.Id, "PetTrt", speciesId);

        var baseDate = DateTime.UtcNow.AddDays(-count);
        var treatments = Enumerable.Range(0, count)
            .Select(i => new Treatment(
                tenant.Id,
                clinic.Id,
                pet.Id,
                examinationId: null,
                baseDate.AddDays(i),
                $"Tedavi-{i}",
                "Açıklama",
                notes: null,
                followUpDateUtc: null))
            .ToList();

        db.Add(tenant);
        db.Add(clinic);
        db.Add(clientEntity);
        db.Add(pet);
        db.AddRange(treatments);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        var token = IssueToken(jwt, tenant.Id, "Treatments.Read");
        return new TenantTokenCtx(token, tenant.Id, clinic.Id);
    }

    private async Task<TenantTokenCtx> SeedPrescriptionsScenarioAsync(int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"PrsPg-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var clientEntity = new Client(tenant.Id, "Müşteri", "905551110004", "prspg@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, clientEntity.Id, "PetPrs", speciesId);

        var baseDate = DateTime.UtcNow.AddDays(-count);
        var prescriptions = Enumerable.Range(0, count)
            .Select(i => new Prescription(
                tenant.Id,
                clinic.Id,
                pet.Id,
                examinationId: null,
                treatmentId: null,
                baseDate.AddDays(i),
                $"Reçete-{i}",
                "İçerik",
                notes: null,
                followUpDateUtc: null))
            .ToList();

        db.Add(tenant);
        db.Add(clinic);
        db.Add(clientEntity);
        db.Add(pet);
        db.AddRange(prescriptions);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        var token = IssueToken(jwt, tenant.Id, "Prescriptions.Read");
        return new TenantTokenCtx(token, tenant.Id, clinic.Id);
    }

    private async Task<TenantTokenCtx> SeedHospitalizationsScenarioAsync(int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"HospPg-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var clientEntity = new Client(tenant.Id, "Müşteri", "905551110005", "hosppg@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pets = Enumerable.Range(0, count)
            .Select(i => new Pet(tenant.Id, clientEntity.Id, $"PetHosp-{i}", speciesId))
            .ToList();

        var baseDate = DateTime.UtcNow.AddDays(-count);
        var hospitalizations = pets
            .Select((pet, i) => new Hospitalization(
                tenant.Id,
                clinic.Id,
                pet.Id,
                examinationId: null,
                baseDate.AddDays(i),
                DateTime.UtcNow.AddDays(i + 2),
                $"Yatış-{i}",
                null))
            .ToList();

        db.Add(tenant);
        db.Add(clinic);
        db.Add(clientEntity);
        db.AddRange(pets);
        db.AddRange(hospitalizations);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        var token = IssueToken(jwt, tenant.Id, "Hospitalizations.Read");
        return new TenantTokenCtx(token, tenant.Id, clinic.Id);
    }

    private async Task<TenantTokenCtx> SeedLabResultsScenarioAsync(int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"LabPg-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var clientEntity = new Client(tenant.Id, "Müşteri", "905551110006", "labpg@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, clientEntity.Id, "PetLab", speciesId);

        var baseDate = DateTime.UtcNow.AddDays(-count);
        var labResults = Enumerable.Range(0, count)
            .Select(i => new LabResult(
                tenant.Id,
                clinic.Id,
                pet.Id,
                examinationId: null,
                baseDate.AddDays(i),
                $"Test-{i}",
                "Normal",
                null,
                null))
            .ToList();

        db.Add(tenant);
        db.Add(clinic);
        db.Add(clientEntity);
        db.Add(pet);
        db.AddRange(labResults);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        var token = IssueToken(jwt, tenant.Id, "LabResults.Read");
        return new TenantTokenCtx(token, tenant.Id, clinic.Id);
    }

    private async Task<TenantTokenCtx> SeedPaymentsScenarioAsync(int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"PayPg-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var clientEntity = new Client(tenant.Id, "Müşteri", "905551110007", "paypg@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, clientEntity.Id, "PetPay", speciesId);

        var baseDate = DateTime.UtcNow.AddDays(-count);
        var payments = Enumerable.Range(0, count)
            .Select(i => new Payment(
                tenant.Id,
                clinic.Id,
                clientEntity.Id,
                pet.Id,
                appointmentId: null,
                examinationId: null,
                100m + i,
                "TRY",
                PaymentMethod.Cash,
                baseDate.AddDays(i),
                null))
            .ToList();

        db.Add(tenant);
        db.Add(clinic);
        db.Add(clientEntity);
        db.Add(pet);
        db.AddRange(payments);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        var token = IssueToken(jwt, tenant.Id, "Payments.Read");
        return new TenantTokenCtx(token, tenant.Id, clinic.Id);
    }

    private async Task<TenantTokenCtx> SeedPetsScenarioAsync(int petCount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"PetPg-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var clientEntity = new Client(tenant.Id, "Müşteri", "905551110000", "petpg@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();

        var pets = Enumerable.Range(1, petCount)
            .Select(i => new Pet(tenant.Id, clientEntity.Id, $"Pet-{i:D3}", speciesId))
            .ToList();

        db.Add(tenant);
        db.Add(clinic);
        db.Add(clientEntity);
        db.AddRange(pets);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        var token = IssueToken(jwt, tenant.Id, "Pets.Read");
        return new TenantTokenCtx(token, tenant.Id, clinic.Id);
    }

    private async Task<TenantTokenCtx> SeedAppointmentsScenarioAsync(int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"ApptPg-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var clientEntity = new Client(tenant.Id, "Müşteri", "905551110001", "apptpg@example.com");
        var species = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => new { s.Id, s.Name }).FirstAsync();
        var pet = new Pet(tenant.Id, clientEntity.Id, "PetAppt", species.Id);

        var baseTime = DateTime.UtcNow.AddDays(1);
        var appointments = Enumerable.Range(0, count)
            .Select(i => new Appointment(
                tenant.Id,
                clinic.Id,
                pet.Id,
                baseTime.AddHours(i),
                30,
                AppointmentType.Consultation,
                AppointmentStatus.Scheduled))
            .ToList();

        db.Add(tenant);
        db.Add(clinic);
        db.Add(clientEntity);
        db.Add(pet);
        db.AddRange(appointments);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        var projectedAtUtc = DateTime.UtcNow;
        foreach (var appointment in appointments)
        {
            queryDb.AppointmentReadModels.Add(new AppointmentReadModel
            {
                AppointmentId = appointment.Id,
                TenantId = tenant.Id,
                ClinicId = clinic.Id,
                ClinicName = clinic.Name,
                PetId = pet.Id,
                PetName = pet.Name,
                SpeciesId = species.Id,
                SpeciesName = species.Name,
                ClientId = clientEntity.Id,
                ClientName = clientEntity.FullName,
                ClientPhone = clientEntity.Phone,
                ClientPhoneNormalized = clientEntity.PhoneNormalized,
                ClientEmail = clientEntity.Email,
                ScheduledAtUtc = appointment.ScheduledAtUtc,
                ScheduledEndUtc = appointment.ScheduledAtUtc.AddMinutes(appointment.DurationMinutes),
                DurationMinutes = appointment.DurationMinutes,
                AppointmentType = (int)appointment.AppointmentType,
                Status = (int)appointment.Status,
                Notes = appointment.Notes,
                LastEventId = Guid.NewGuid(),
                LastProjectedAtUtc = projectedAtUtc
            });
        }

        await queryDb.SaveChangesAsync();

        var token = IssueToken(jwt, tenant.Id, "Appointments.Read");
        return new TenantTokenCtx(token, tenant.Id, clinic.Id);
    }

    private async Task<TenantTokenCtx> SeedExaminationsScenarioAsync(int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"ExamPg-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var clientEntity = new Client(tenant.Id, "Müşteri", "905551110003", "exampg@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, clientEntity.Id, "PetExam", speciesId);

        var baseTime = DateTime.UtcNow.AddHours(-1);
        var examinations = Enumerable.Range(0, count)
            .Select(i => new Examination(
                tenant.Id,
                clinic.Id,
                pet.Id,
                appointmentId: null,
                baseTime.AddHours(-i),
                $"Kontrol-{i}",
                "Bulgu",
                null,
                null))
            .ToList();

        db.Add(tenant);
        db.Add(clinic);
        db.Add(clientEntity);
        db.Add(pet);
        db.AddRange(examinations);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        var token = IssueToken(jwt, tenant.Id, "Examinations.Read");
        return new TenantTokenCtx(token, tenant.Id, clinic.Id);
    }

    private async Task<TenantTokenCtx> SeedVaccinationsScenarioAsync(int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"VacPg-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var clientEntity = new Client(tenant.Id, "Müşteri", "905551110002", "vacpg@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, clientEntity.Id, "PetVac", speciesId);

        var catalog = await db.VaccineDefinitions.AsNoTracking()
            .Where(v => v.TenantId == null && v.Code == "RABIES")
            .Select(v => new { v.Id, v.Name })
            .FirstAsync();

        var now = DateTime.UtcNow;
        var vaccinations = Enumerable.Range(0, count)
            .Select(i => new Vaccination(
                tenant.Id,
                pet.Id,
                clinic.Id,
                examinationId: null,
                catalog.Id,
                catalog.Name,
                VaccinationStatus.Scheduled,
                appliedAtUtc: null,
                dueAtUtc: now.AddDays(i + 1),
                notes: null))
            .ToList();

        db.Add(tenant);
        db.Add(clinic);
        db.Add(clientEntity);
        db.Add(pet);
        db.AddRange(vaccinations);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        var token = IssueToken(jwt, tenant.Id, "Vaccinations.Read");
        return new TenantTokenCtx(token, tenant.Id, clinic.Id);
    }

    private static string IssueToken(IJwtTokenService jwt, Guid tenantId, string permission)
    {
        var claims = new List<Claim>
        {
            new("permission", permission),
            new(VeterinerClaims.TenantId, tenantId.ToString("D"))
        };
        var (accessToken, _, _) = jwt.Create(Guid.NewGuid(), $"op-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return accessToken;
    }
}
