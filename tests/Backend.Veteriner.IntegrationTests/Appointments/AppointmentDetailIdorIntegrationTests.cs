using System.Net;
using System.Net.Http.Headers;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Appointments;

/// <summary>
/// GET /api/v1/appointments/{id} randevu detay IDOR erişim kontrolü (FAZ 2A).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class AppointmentDetailIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AppointmentDetailIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetById_Should_Return200_When_NonTenantWideUserReadsAssignedClinicAppointment()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, assignedClinicId, _) =
            await IntegrationTestAuthHelper.SeedAppointmentReaderUserAsync(_factory.Services, hasher);
        var appointmentId = await IntegrationTestAuthHelper.SeedAppointmentInClinicAsync(
            _factory.Services,
            assignedClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync($"/api/v1/appointments/{appointmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_Should_Return404_When_NonTenantWideUserReadsUnassignedClinicAppointmentInSameTenant()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedAppointmentReaderUserAsync(_factory.Services, hasher);
        var appointmentId = await IntegrationTestAuthHelper.SeedAppointmentInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync($"/api/v1/appointments/{appointmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var code = await IntegrationTestProblemDetails.ReadCodeAsync(response);
        code.Should().Be("Appointments.NotFound");
    }

    [Fact]
    public async Task GetById_Should_Return404_When_NonTenantWideUserWithoutClinicClaimReadsUnassignedClinicAppointment()
    {
        // Login token clinic claim taşımaz; atanmadığı kliniğin randevusu görünmemeli.
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedAppointmentReaderUserAsync(_factory.Services, hasher);
        var appointmentId = await IntegrationTestAuthHelper.SeedAppointmentInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync($"/api/v1/appointments/{appointmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var code = await IntegrationTestProblemDetails.ReadCodeAsync(response);
        code.Should().Be("Appointments.NotFound");
    }

    [Fact]
    public async Task GetById_Should_Return200_When_TenantAdminReadsOtherClinicAppointment()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var appointmentId = await IntegrationTestAuthHelper.SeedAppointmentInClinicAsync(
            _factory.Services,
            extraClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync($"/api/v1/appointments/{appointmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_Should_Return404_When_UserReadsForeignTenantAppointment()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, _) =
            await IntegrationTestAuthHelper.SeedAppointmentReaderUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var foreignAppointmentId = await IntegrationTestAuthHelper.SeedAppointmentInClinicAsync(
            _factory.Services,
            foreignClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync($"/api/v1/appointments/{foreignAppointmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var code = await IntegrationTestProblemDetails.ReadCodeAsync(response);
        code.Should().Be("Appointments.NotFound");
    }
}
