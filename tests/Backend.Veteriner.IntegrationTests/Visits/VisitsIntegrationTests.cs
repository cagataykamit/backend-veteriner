using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Visits;

/// <summary>
/// Visit uçtan uca: HTTP + gerçek SQL Server (LocalDB, izole test veritabanı). Filtreli benzersiz indeksler,
/// tekrar tıklama/eşzamanlı istek, geçişler, düzeltme + audit, muayene bağlantısı ve Bugün sorgusu.
/// Her test kendi kliniğini ve kullanıcısını kurar; Bugün sayıları diğer testlerden etkilenmez.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class VisitsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Reason = "Yanlis hasta secildi";

    private static readonly string[] OperatorPermissions =
    [
        PermissionCatalog.Visits.Read,
        PermissionCatalog.Visits.Create,
        PermissionCatalog.Visits.Update,
        PermissionCatalog.Examinations.Create,
        PermissionCatalog.Examinations.Read,
    ];

    private static readonly string[] CorrectorPermissions = [.. OperatorPermissions, PermissionCatalog.Visits.Correct];

    private readonly CustomWebApplicationFactory _factory;

    public VisitsIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ---- Tekrar tıklama / benzersizlik ----

    [Fact]
    public async Task Walkin_Create_Twice_Should_Return_201_Then_200_With_Same_Visit_And_Create_No_Appointment()
    {
        var ctx = await SeedAsync();
        var first = await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId });
        var second = await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId });

        first.Response.StatusCode.Should().Be(HttpStatusCode.Created);
        first.Result!.Created.Should().BeTrue();
        first.Result.Visit.AppointmentId.Should().BeNull();
        first.Result.Visit.CareStatus.Should().Be(VisitCareStatus.Waiting);
        first.Response.Headers.Location.Should().NotBeNull();

        second.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Result!.Created.Should().BeFalse();
        second.Result.Visit.Id.Should().Be(first.Result.Visit.Id);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Visits.CountAsync(v => v.PetId == ctx.PetId)).Should().Be(1);
        (await db.Appointments.CountAsync(a => a.PetId == ctx.PetId)).Should().Be(0, "randevusuz geliş sahte randevu üretmez");
    }

    [Fact]
    public async Task Concurrent_Walkin_Creates_Should_Produce_Exactly_One_Visit()
    {
        var ctx = await SeedAsync();

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId })))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        var failures = new List<string>();
        foreach (var r in results.Where(r => !r.Response.IsSuccessStatusCode))
            failures.Add($"{(int)r.Response.StatusCode} {await r.Response.Content.ReadAsStringAsync()}");
        failures.Should().BeEmpty();
        results.Count(r => r.Response.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Select(r => r.Result!.Visit.Id).Distinct().Should().HaveCount(1);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Visits.CountAsync(v => v.PetId == ctx.PetId)).Should().Be(1);
    }

    [Fact]
    public async Task Appointment_Visit_Should_Derive_From_Appointment_Keep_It_Scheduled_And_Be_Idempotent()
    {
        var ctx = await SeedAsync();
        var appointmentId = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(30));

        var first = await ctx.PostVisitAsync(new { appointmentId });
        var second = await ctx.PostVisitAsync(new { appointmentId });

        first.Response.StatusCode.Should().Be(HttpStatusCode.Created);
        first.Result!.Visit.AppointmentId.Should().Be(appointmentId);
        first.Result.Visit.PetId.Should().Be(ctx.PetId);
        first.Result.Visit.ClinicId.Should().Be(ctx.ClinicId);
        second.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Result!.Visit.Id.Should().Be(first.Result.Visit.Id);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId))
            .Status.Should().Be(AppointmentStatus.Scheduled, "geliş randevuyu değiştirmez");
    }

    [Fact]
    public async Task Appointment_Create_Should_Fail_For_Cancelled_And_For_Completed_Without_Visit()
    {
        var ctx = await SeedAsync();
        var cancelled = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(30));
        var completed = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(40));
        await ctx.UpdateAppointmentAsync(cancelled, a => a.Cancel());
        await ctx.UpdateAppointmentAsync(completed, a => a.Complete());

        var cancelledResponse = await ctx.PostVisitRawAsync(new { appointmentId = cancelled });
        var completedResponse = await ctx.PostVisitRawAsync(new { appointmentId = completed });

        cancelledResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(cancelledResponse)).Should().Be("Visits.AppointmentCancelled");
        completedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(completedResponse)).Should().Be("Visits.AppointmentNotScheduled");
    }

    [Fact]
    public async Task Database_Should_Reject_Second_Active_Visit_Per_Pet_And_Second_Visit_Per_Appointment()
    {
        var ctx = await SeedAsync();
        var appointmentId = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(30));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Visits.Add(ctx.NewVisit(appointmentId: appointmentId));
        await db.SaveChangesAsync();

        // Aynı hayvan için ikinci aktif geliş (randevusuz) → hayvan başına tek aktif kuralı.
        db.Visits.Add(ctx.NewVisit());
        var activePerPet = () => db.SaveChangesAsync();
        await activePerPet.Should().ThrowAsync<DbUpdateException>();
        db.ChangeTracker.Clear();

        // Aynı randevu için ikinci geliş: önce ilk geliş tamamlanır (aktif kuralı serbest), randevu kuralı hâlâ engeller.
        var first = await db.Visits.SingleAsync(v => v.AppointmentId == appointmentId);
        first.Start(DateTime.UtcNow);
        first.Complete(DateTime.UtcNow);
        await db.SaveChangesAsync();

        db.Visits.Add(ctx.NewVisit(appointmentId: appointmentId));
        var perAppointment = () => db.SaveChangesAsync();
        await perAppointment.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Database_Should_Allow_New_Visit_After_Completed_Or_Voided_Visit()
    {
        var ctx = await SeedAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var completed = ctx.NewVisit();
        completed.Start(DateTime.UtcNow);
        completed.Complete(DateTime.UtcNow);
        var voided = ctx.NewVisit();
        voided.MarkAsMistaken(Reason, DateTime.UtcNow);
        // Voided kayıt ilk eklenir ki aktif kuralı yalnızca tamamlanmış/voided olmayan satırlara bakar.
        db.Visits.AddRange(completed, voided);
        await db.SaveChangesAsync();

        db.Visits.Add(ctx.NewVisit());
        var act = () => db.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }

    // ---- Geçişler ----

    [Fact]
    public async Task Start_And_Complete_Should_Advance_One_Step_And_Be_Idempotent()
    {
        var ctx = await SeedAsync();
        var visitId = (await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId })).Result!.Visit.Id;

        var completeEarly = await ctx.PostRawAsync($"/api/v1/visits/{visitId}/complete");
        completeEarly.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(completeEarly)).Should().Be("Visits.InvalidStatusTransition");

        var started = await ctx.ReadVisitAsync(await ctx.PostRawAsync($"/api/v1/visits/{visitId}/start"));
        var startedAgain = await ctx.ReadVisitAsync(await ctx.PostRawAsync($"/api/v1/visits/{visitId}/start"));
        started.CareStatus.Should().Be(VisitCareStatus.InProgress);
        startedAgain.StartedAtUtc.Should().Be(started.StartedAtUtc, "tekrar istek yan etki üretmez");

        var completed = await ctx.ReadVisitAsync(await ctx.PostRawAsync($"/api/v1/visits/{visitId}/complete"));
        var completedAgain = await ctx.ReadVisitAsync(await ctx.PostRawAsync($"/api/v1/visits/{visitId}/complete"));
        completed.CareStatus.Should().Be(VisitCareStatus.Completed);
        completedAgain.CompletedAtUtc.Should().Be(completed.CompletedAtUtc);

        var startAfterComplete = await ctx.PostRawAsync($"/api/v1/visits/{visitId}/start");
        startAfterComplete.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Tamamlanmış geliş sonrası aynı hayvan için yeni geliş açılabilir.
        var next = await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId });
        next.Response.StatusCode.Should().Be(HttpStatusCode.Created);
        next.Result!.Visit.Id.Should().NotBe(visitId);
    }

    [Fact]
    public async Task Concurrent_Start_Requests_Should_All_Succeed_With_InProgress()
    {
        var ctx = await SeedAsync();
        var visitId = (await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId })).Result!.Visit.Id;

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => Task.Run(() => ctx.PostRawAsync($"/api/v1/visits/{visitId}/start"))));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Visits.AsNoTracking().SingleAsync(v => v.Id == visitId)).CareStatus.Should().Be(VisitCareStatus.InProgress);
    }

    // ---- Düzeltme + audit + izin ----

    [Fact]
    public async Task Correction_Should_Require_Correct_Permission()
    {
        var ctx = await SeedAsync(CorrectorOrNot: false);
        var visitId = (await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId })).Result!.Visit.Id;

        var response = await ctx.PostRawAsync(
            $"/api/v1/visits/{visitId}/corrections", new { reason = Reason, markAsMistaken = true });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Correction_MarkAsMistaken_Should_Void_Visit_Write_Audit_And_Allow_New_Visit()
    {
        var ctx = await SeedAsync();
        var visitId = (await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId })).Result!.Visit.Id;

        var response = await ctx.PostRawAsync(
            $"/api/v1/visits/{visitId}/corrections", new { reason = Reason, markAsMistaken = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await ctx.ReadVisitAsync(response);
        dto.IsVoided.Should().BeTrue();
        dto.VoidReason.Should().Be(Reason);

        var again = await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId });
        again.Response.StatusCode.Should().Be(HttpStatusCode.Created);
        again.Result!.Visit.Id.Should().NotBe(visitId);

        var transitionVoided = await ctx.PostRawAsync($"/api/v1/visits/{visitId}/start");
        transitionVoided.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "Visit.Correct" && a.TargetId == $"VisitId={visitId}" && a.Success)
            .SingleAsync();
        log.ActorUserId.Should().Be(ctx.UserId);
        log.RequestPayload.Should().Contain(Reason);
    }

    [Fact]
    public async Task Correction_With_Missing_Reason_Should_Fail_And_Failed_Attempt_Is_Audited()
    {
        var ctx = await SeedAsync();
        var visitId = (await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId })).Result!.Visit.Id;

        var response = await ctx.PostRawAsync(
            $"/api/v1/visits/{visitId}/corrections", new { reason = "x", targetCareStatus = "InProgress" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Visits.Validation");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogs.AsNoTracking()
            .AnyAsync(a => a.Action == "Visit.Correct" && a.TargetId == $"VisitId={visitId}" && !a.Success))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Correction_Reopen_Should_Return_409_When_Pet_Has_Another_Active_Visit()
    {
        var ctx = await SeedAsync();
        var first = (await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId })).Result!.Visit.Id;
        await ctx.PostRawAsync($"/api/v1/visits/{first}/start");
        await ctx.PostRawAsync($"/api/v1/visits/{first}/complete");
        await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId });

        var response = await ctx.PostRawAsync(
            $"/api/v1/visits/{first}/corrections", new { reason = Reason, targetCareStatus = "Waiting" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Visits.DuplicateActiveVisit");
    }

    [Fact]
    public async Task Correction_Should_Revert_Completed_To_InProgress_When_No_Other_Active_Visit()
    {
        var ctx = await SeedAsync();
        var id = (await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId })).Result!.Visit.Id;
        await ctx.PostRawAsync($"/api/v1/visits/{id}/start");
        await ctx.PostRawAsync($"/api/v1/visits/{id}/complete");

        var dto = await ctx.ReadVisitAsync(await ctx.PostRawAsync(
            $"/api/v1/visits/{id}/corrections", new { reason = Reason, targetCareStatus = "InProgress" }));

        dto.CareStatus.Should().Be(VisitCareStatus.InProgress);
        dto.CompletedAtUtc.Should().BeNull();
    }

    // ---- Muayene bağlantısı ----

    [Fact]
    public async Task Examination_With_VisitId_Should_Start_Visit_Link_Examination_And_Block_Mistaken_Marking()
    {
        var ctx = await SeedAsync();
        var visitId = (await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId })).Result!.Visit.Id;

        var create = await ctx.PostRawAsync("/api/v1/examinations", new
        {
            visitId,
            examinedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            visitReason = "Kontrol",
            findings = "Bulgu",
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var examinationId = (await create.Content.ReadFromJsonAsync<ExaminationWriteResultDto>())!.Id;

        var detail = await ctx.Http.GetFromJsonAsync<ExaminationDetailDto>($"/api/v1/examinations/{examinationId}");
        detail!.VisitId.Should().Be(visitId);
        detail.PetId.Should().Be(ctx.PetId, "hasta tekrar seçilmedi, Visit'ten türetildi");

        (await ctx.Http.GetFromJsonAsync<VisitDto>($"/api/v1/visits/{visitId}"))!
            .CareStatus.Should().Be(VisitCareStatus.InProgress);

        var mistaken = await ctx.PostRawAsync(
            $"/api/v1/visits/{visitId}/corrections", new { reason = Reason, markAsMistaken = true });
        mistaken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(mistaken)).Should().Be("Visits.HasExaminations");
    }

    [Fact]
    public async Task Examination_With_Appointment_Visit_Should_Keep_Appointment_Completion_Behavior()
    {
        var ctx = await SeedAsync();
        var appointmentId = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(30));
        var visitId = (await ctx.PostVisitAsync(new { appointmentId })).Result!.Visit.Id;

        var create = await ctx.PostRawAsync("/api/v1/examinations", new
        {
            visitId,
            examinedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            visitReason = "Kontrol",
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId))
            .Status.Should().Be(AppointmentStatus.Completed);
        (await db.Examinations.AsNoTracking().SingleAsync(e => e.VisitId == visitId))
            .AppointmentId.Should().Be(appointmentId);
    }

    [Fact]
    public async Task Examination_Should_Reject_Completed_Visit_And_Mismatched_Pet()
    {
        var ctx = await SeedAsync();
        var visitId = (await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId })).Result!.Visit.Id;

        var otherPet = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, ctx.ClinicId);
        var mismatch = await ctx.PostRawAsync("/api/v1/examinations", new
        {
            visitId,
            petId = otherPet,
            examinedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            visitReason = "Kontrol",
        });
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(mismatch)).Should().Be("Examinations.VisitMismatch");

        await ctx.PostRawAsync($"/api/v1/visits/{visitId}/start");
        await ctx.PostRawAsync($"/api/v1/visits/{visitId}/complete");
        var closed = await ctx.PostRawAsync("/api/v1/examinations", new
        {
            visitId,
            examinedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            visitReason = "Kontrol",
        });
        closed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(closed)).Should().Be("Visits.NotOpen");
    }

    // ---- Bugün ----

    [Fact]
    public async Task Today_Should_Merge_Visits_And_Planned_Appointments_Into_Single_Ordered_List()
    {
        var ctx = await SeedAsync();

        var walkinPet = ctx.PetId;
        var walkin = (await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = walkinPet })).Result!.Visit;

        var apptPet = await ctx.SeedPetAsync();
        var apptWithVisit = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(20), apptPet);
        var apptVisit = (await ctx.PostVisitAsync(new { appointmentId = apptWithVisit })).Result!.Visit;
        await ctx.PostRawAsync($"/api/v1/visits/{apptVisit.Id}/start");

        var plannedPet = await ctx.SeedPetAsync();
        var planned = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(45), plannedPet);

        var cancelledPet = await ctx.SeedPetAsync();
        var cancelled = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(50), cancelledPet);
        await ctx.UpdateAppointmentAsync(cancelled, a => a.Cancel());

        var completedPet = await ctx.SeedPetAsync();
        var completedVisit = (await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = completedPet })).Result!.Visit;
        await ctx.PostRawAsync($"/api/v1/visits/{completedVisit.Id}/start");
        await ctx.PostRawAsync($"/api/v1/visits/{completedVisit.Id}/complete");

        var today = await ctx.GetTodayAsync();

        today.Items.Should().HaveCount(4, "iptal edilen randevu ve randevulu Visit'in randevusu ayrı satır üretmez");
        today.Items.Select(i => i.VisitId).Should().Equal(walkin.Id, apptVisit.Id, null, completedVisit.Id);
        today.Items[0].CareStatus.Should().Be(VisitCareStatus.Waiting);
        today.Items[0].AppointmentId.Should().BeNull();
        today.Items[1].CareStatus.Should().Be(VisitCareStatus.InProgress);
        today.Items[1].AppointmentId.Should().Be(apptWithVisit);
        today.Items[2].AppointmentId.Should().Be(planned);
        today.Items[2].CareStatus.Should().BeNull();
        today.Items[2].AppointmentStatus.Should().Be(AppointmentStatus.Scheduled);
        today.Items.Should().NotContain(i => i.AppointmentId == cancelled);
        today.Items.Count(i => i.AppointmentId == apptWithVisit).Should().Be(1);
        today.Items.Should().OnlyContain(i => !string.IsNullOrEmpty(i.PetName) && !string.IsNullOrEmpty(i.ClientName));
    }

    [Fact]
    public async Task Today_Payment_Indicator_And_Hospitalization_Should_Be_Separate_From_Care_Status()
    {
        var ctx = await SeedAsync();
        var apptPet = await ctx.SeedPetAsync();
        var appointmentId = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(20), apptPet);
        var visit = (await ctx.PostVisitAsync(new { appointmentId })).Result!.Visit;
        var unpaid = (await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId })).Result!.Visit;

        await ctx.SeedPaymentAsync(appointmentId, apptPet);
        await ctx.SeedActiveHospitalizationAsync(ctx.PetId);

        var today = await ctx.GetTodayAsync();

        var paidRow = today.Items.Single(i => i.VisitId == visit.Id);
        paidRow.PaymentIndicator.Should().Be(TodayPaymentIndicator.PaymentRecorded);
        paidRow.CareStatus.Should().Be(VisitCareStatus.Waiting, "ödeme bakım durumunu etkilemez");
        var unpaidRow = today.Items.Single(i => i.VisitId == unpaid.Id);
        unpaidRow.PaymentIndicator.Should().Be(TodayPaymentIndicator.NoPaymentRecorded);
        unpaidRow.HasActiveHospitalization.Should().BeTrue();
        today.ActiveHospitalizations.Should().ContainSingle(h => h.PetId == ctx.PetId);
    }

    [Fact]
    public async Task Today_Payment_Indicator_Should_Follow_Examination_Linked_To_Visit()
    {
        var ctx = await SeedAsync();
        var visitId = (await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId })).Result!.Visit.Id;
        var create = await ctx.PostRawAsync("/api/v1/examinations", new
        {
            visitId,
            examinedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            visitReason = "Kontrol",
        });
        var examinationId = (await create.Content.ReadFromJsonAsync<ExaminationWriteResultDto>())!.Id;

        (await ctx.GetTodayAsync()).Items.Single(i => i.VisitId == visitId)
            .PaymentIndicator.Should().Be(TodayPaymentIndicator.NoPaymentRecorded);

        await ctx.SeedPaymentAsync(null, ctx.PetId, examinationId);

        (await ctx.GetTodayAsync()).Items.Single(i => i.VisitId == visitId)
            .PaymentIndicator.Should().Be(TodayPaymentIndicator.PaymentRecorded);
    }

    [Fact]
    public async Task Today_Should_Carry_Over_Open_Visits_From_Previous_Day_Only_For_Today()
    {
        var ctx = await SeedAsync();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Visits.Add(ctx.NewVisit(arrivedAtUtc: DateTime.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
        }

        var today = await ctx.GetTodayAsync();
        today.Items.Should().ContainSingle();
        today.Items[0].IsCarriedOver.Should().BeTrue();
        today.Items[0].CareStatus.Should().Be(VisitCareStatus.Waiting);

        var yesterday = OperationDayBounds.ToLocalDate(DateTime.UtcNow.AddDays(-1));
        var past = await ctx.GetTodayAsync(yesterday);
        past.Items.Should().ContainSingle("geliş o günün kaydıdır; devralınma yalnızca bugün için eklenir");
        past.Items[0].IsCarriedOver.Should().BeFalse();
    }

    [Fact]
    public async Task Today_Should_Exclude_Voided_Visits()
    {
        var ctx = await SeedAsync();
        var id = (await ctx.PostVisitAsync(new { clinicId = ctx.ClinicId, petId = ctx.PetId })).Result!.Visit.Id;
        await ctx.PostRawAsync($"/api/v1/visits/{id}/corrections", new { reason = Reason, markAsMistaken = true });

        (await ctx.GetTodayAsync()).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Today_Should_Require_Clinic_And_Respect_Clinic_Assignment()
    {
        var ctx = await SeedAsync();

        var noClinic = await ctx.Http.GetAsync("/api/v1/visits/today");
        noClinic.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noClinic)).Should().Be("Visits.ClinicScopeRequired");

        var otherClinic = await ctx.SeedForeignAssignedClinicAsync();
        var denied = await ctx.Http.GetAsync($"/api/v1/visits/today?clinicId={otherClinic}");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetById_Should_Return_404_For_Visit_In_Unassigned_Clinic()
    {
        var ctx = await SeedAsync();
        var otherClinic = await ctx.SeedForeignAssignedClinicAsync();
        Guid foreignVisitId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenantId = await db.Clinics.Where(c => c.Id == otherClinic).Select(c => c.TenantId).SingleAsync();
            var client = new Client(tenantId, $"Foreign-{Guid.NewGuid():N}"[..14], "905551110055");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var speciesId = await db.Species.Select(s => s.Id).FirstAsync();
            var pet = new Pet(tenantId, client.Id, $"FPet-{Guid.NewGuid():N}"[..12], speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            var visit = new Visit(tenantId, otherClinic, pet.Id, null, null, ctx.UserId, DateTime.UtcNow);
            db.Visits.Add(visit);
            await db.SaveChangesAsync();
            foreignVisitId = visit.Id;
        }

        var response = await ctx.Http.GetAsync($"/api/v1/visits/{foreignVisitId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Visits.NotFound");
    }

    // ---- Kurulum ----

    private async Task<VisitTestContext> SeedAsync(bool CorrectorOrNot = true)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId;
        Guid clinicId;
        Guid userId;
        string email;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Visit-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"visit-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"));
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id));
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            clinicId = clinic.Id;
            userId = user.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services, email, CorrectorOrNot ? CorrectorPermissions : OperatorPermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var ctx = new VisitTestContext(_factory.Services, http, tenantId, clinicId, userId);
        ctx.PetId = await ctx.SeedPetAsync();
        return ctx;
    }

    private sealed class VisitTestContext
    {
        private readonly IServiceProvider _services;

        public VisitTestContext(IServiceProvider services, HttpClient http, Guid tenantId, Guid clinicId, Guid userId)
        {
            _services = services;
            Http = http;
            TenantId = tenantId;
            ClinicId = clinicId;
            UserId = userId;
        }

        public HttpClient Http { get; }
        public Guid TenantId { get; }
        public Guid ClinicId { get; }
        public Guid UserId { get; }
        public Guid PetId { get; set; }

        public Visit NewVisit(Guid? appointmentId = null, DateTime? arrivedAtUtc = null)
            => new(TenantId, ClinicId, PetId, appointmentId, null, UserId, arrivedAtUtc ?? DateTime.UtcNow);

        public async Task<(HttpResponseMessage Response, VisitCreateResultDto? Result)> PostVisitAsync(object body)
        {
            var response = await Http.PostAsJsonAsync("/api/v1/visits", body);
            var result = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<VisitCreateResultDto>()
                : null;
            return (response, result);
        }

        public Task<HttpResponseMessage> PostVisitRawAsync(object body) => Http.PostAsJsonAsync("/api/v1/visits", body);

        public Task<HttpResponseMessage> PostRawAsync(string url, object? body = null)
            => body is null ? Http.PostAsync(url, null) : Http.PostAsJsonAsync(url, body);

        public async Task<VisitDto> ReadVisitAsync(HttpResponseMessage response)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<VisitDto>())!;
        }

        public async Task<TodayDto> GetTodayAsync(DateOnly? localDate = null)
        {
            var url = $"/api/v1/visits/today?clinicId={ClinicId}";
            if (localDate is { } d)
                url += $"&localDate={d:yyyy-MM-dd}";

            var response = await Http.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<TodayDto>())!;
        }

        public async Task<Guid> SeedPetAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client(TenantId, $"VClient-{Guid.NewGuid():N}"[..14], "905551110077");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(TenantId, client.Id, $"VPet-{Guid.NewGuid():N}"[..12], speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            return pet.Id;
        }

        public async Task<Guid> SeedAppointmentAsync(TimeSpan fromNow, Guid? petId = null)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var appointment = new Appointment(
                TenantId, ClinicId, petId ?? PetId, DateTime.UtcNow.Add(fromNow), 30,
                AppointmentType.Consultation, AppointmentStatus.Scheduled);
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            return appointment.Id;
        }

        public async Task UpdateAppointmentAsync(Guid appointmentId, Func<Appointment, Backend.Veteriner.Domain.Shared.Result> change)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var appointment = await db.Appointments.SingleAsync(a => a.Id == appointmentId);
            change(appointment).IsSuccess.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        public async Task SeedPaymentAsync(Guid? appointmentId, Guid petId, Guid? examinationId = null)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clientId = await db.Pets.Where(p => p.Id == petId).Select(p => p.ClientId).SingleAsync();
            db.Payments.Add(new Payment(
                TenantId, ClinicId, clientId, petId, appointmentId, examinationId,
                150m, "TRY", PaymentMethod.Cash, DateTime.UtcNow.AddMinutes(-1), "Visit test"));
            await db.SaveChangesAsync();
        }

        public async Task SeedActiveHospitalizationAsync(Guid petId)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Hospitalizations.Add(new Hospitalization(
                TenantId, ClinicId, petId, null, DateTime.UtcNow.AddHours(-3),
                DateTime.UtcNow.AddDays(1), "Gozlem", null));
            await db.SaveChangesAsync();
        }

        /// <summary>Kullanıcıya atanmamış, aynı kiracıda ikinci klinik.</summary>
        public async Task<Guid> SeedForeignAssignedClinicAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = new Clinic(TenantId, $"Other-{Guid.NewGuid():N}"[..14], "Bursa");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();
            return clinic.Id;
        }
    }
}
