using System.Reflection;
using System.Security.Claims;
using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Api.Controllers;
using Backend.Veteriner.Application.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace Backend.Veteriner.IntegrationTests.Auth;

/// <summary>
/// Klinik çalışma saatleri / randevu varsayılanları read yetkisi composite ("any-of") policy testleri.
/// Sekreter ve Veteriner gibi <c>Clinics.Read</c>'e sahip olmayan ama <c>Appointments.Create</c>
/// veya <c>Appointments.Reschedule</c> taşıyan rollerin ilgili GET endpoint'lerini okuyabilmesi gerekir.
/// Update endpointleri (PUT working-hours / PUT appointment-settings / klinik profili güncelleme) ise
/// hâlâ <c>Clinics.Update</c> ile korunmalıdır.
/// </summary>
public sealed class ClinicsSchedulingReadPolicyTests
{
    private static AuthorizationHandlerContext BuildContext(
        PermissionAnyOfRequirement requirement,
        params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(authenticationType: "TestAuth");
        foreach (var (t, v) in claims)
            identity.AddClaim(new Claim(t, v));
        var user = new ClaimsPrincipal(identity);
        return new AuthorizationHandlerContext(new[] { requirement }, user, resource: null);
    }

    private static PermissionAnyOfRequirement BuildSchedulingReadRequirement()
        => new(new[]
        {
            PermissionCatalog.Clinics.Read,
            PermissionCatalog.Clinics.Update,
            PermissionCatalog.Appointments.Create,
            PermissionCatalog.Appointments.Reschedule,
        });

    [Theory]
    [InlineData("Clinics.Read")]
    [InlineData("Clinics.Update")]
    [InlineData("Appointments.Create")]
    [InlineData("Appointments.Reschedule")]
    public async Task Handler_Should_Succeed_When_User_Has_Any_AllowedPermission_AsSingleClaim(string code)
    {
        var handler = new PermissionAnyOfAuthorizationHandler();
        var requirement = BuildSchedulingReadRequirement();
        var context = BuildContext(requirement, ("permission", code));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_Should_Succeed_When_User_Has_AllowedPermission_InCsvClaim()
    {
        var handler = new PermissionAnyOfAuthorizationHandler();
        var requirement = BuildSchedulingReadRequirement();
        var context = BuildContext(
            requirement,
            ("permissions", "Pets.Read, Appointments.Create, Reminders.Read"));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_Should_Fail_When_User_Has_No_AllowedPermission()
    {
        var handler = new PermissionAnyOfAuthorizationHandler();
        var requirement = BuildSchedulingReadRequirement();
        var context = BuildContext(
            requirement,
            ("permission", "Pets.Read"),
            ("permissions", "Reminders.Read,Dashboard.Read"));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Handler_Should_Fail_When_User_Has_NoClaims()
    {
        var handler = new PermissionAnyOfAuthorizationHandler();
        var requirement = BuildSchedulingReadRequirement();
        var context = BuildContext(requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Handler_Should_Fail_When_Requirement_Has_EmptyCodes()
    {
        var handler = new PermissionAnyOfAuthorizationHandler();
        var requirement = new PermissionAnyOfRequirement(Array.Empty<string>());
        var context = BuildContext(requirement, ("permission", "Appointments.Create"));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public void ClinicsController_GetWorkingHours_Should_Use_SchedulingReadPolicy()
    {
        var policy = GetAuthorizePolicy(nameof(ClinicsController.GetWorkingHours));
        policy.Should().Be(AuthorizationPolicyNames.ClinicsSchedulingRead);
    }

    [Fact]
    public void ClinicsController_GetAppointmentSettings_Should_Use_SchedulingReadPolicy()
    {
        var policy = GetAuthorizePolicy(nameof(ClinicsController.GetAppointmentSettings));
        policy.Should().Be(AuthorizationPolicyNames.ClinicsSchedulingRead);
    }

    [Fact]
    public void ClinicsController_PutWorkingHours_Should_Still_Require_ClinicsUpdate()
    {
        var policy = GetAuthorizePolicy(nameof(ClinicsController.PutWorkingHours));
        policy.Should().Be(PermissionCatalog.Clinics.Update);
    }

    [Fact]
    public void ClinicsController_PutAppointmentSettings_Should_Still_Require_ClinicsUpdate()
    {
        var policy = GetAuthorizePolicy(nameof(ClinicsController.PutAppointmentSettings));
        policy.Should().Be(PermissionCatalog.Clinics.Update);
    }

    [Fact]
    public void ClinicsController_Update_Should_Still_Require_ClinicsUpdate()
    {
        var policy = GetAuthorizePolicy(nameof(ClinicsController.Update));
        policy.Should().Be(PermissionCatalog.Clinics.Update);
    }

    [Fact]
    public void ClinicsController_GetById_Should_Still_Require_ClinicsRead()
    {
        var policy = GetAuthorizePolicy(nameof(ClinicsController.GetById));
        policy.Should().Be(PermissionCatalog.Clinics.Read);
    }

    [Fact]
    public void ClinicsController_GetList_Should_Still_Require_ClinicsRead()
    {
        var policy = GetAuthorizePolicy(nameof(ClinicsController.GetList));
        policy.Should().Be(PermissionCatalog.Clinics.Read);
    }

    [Fact]
    public void ClinicsController_Create_Should_Still_Require_ClinicsCreate()
    {
        var policy = GetAuthorizePolicy(nameof(ClinicsController.Create));
        policy.Should().Be(PermissionCatalog.Clinics.Create);
    }

    private static string? GetAuthorizePolicy(string actionName)
    {
        var method = typeof(ClinicsController).GetMethod(
            actionName,
            BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull($"ClinicsController üzerinde '{actionName}' action'ı bulunmalı");

        var authorize = method!.GetCustomAttributes<AuthorizeAttribute>(inherit: false).FirstOrDefault();
        authorize.Should().NotBeNull($"'{actionName}' action'ında [Authorize] attribute'u bulunmalı");

        return authorize!.Policy;
    }
}
