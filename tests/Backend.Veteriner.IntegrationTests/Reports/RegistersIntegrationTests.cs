using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Registers.Examinations;
using Backend.Veteriner.Application.Reports.Registers.Prescriptions;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using ClosedXML.Excel;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Reports;

/// <summary>
/// GET /reports/registers/examinations ve /prescriptions (+ export, export-xlsx) uçtan uca: HTTP + gerçek SQL Server (LocalDB, izole test veritabanı).
/// Her test kendi kiracısını kurar. Kanıtlananlar: İstanbul gün sınırı, sıra no + sayfalama, eksik alanların boşluğu,
/// sonradan eklenen / iptal edilen uygulama, reçete satır genişletmesi, ekran = CSV = XLSX, klinik kapsamı, IDOR, yetki.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class RegistersIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string ExaminationsUrl = "/api/v1/reports/registers/examinations";
    private const string PrescriptionsUrl = "/api/v1/reports/registers/prescriptions";

    // Europe/Istanbul = UTC+3 (kalıcı): 10 Mart 00:00 İstanbul = 9 Mart 21:00 UTC.
    private static readonly DateTime DayStartUtc = new(2026, 3, 9, 21, 0, 0, DateTimeKind.Utc);
    private const string Day = "2026-03-10";

    private static readonly string[] ReaderPermissions =
    [
        PermissionCatalog.Examinations.Read,
        PermissionCatalog.Prescriptions.Read,
    ];

    private static int _phoneSeed = 6_000_000;

    private readonly CustomWebApplicationFactory _factory;

    public RegistersIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Examination_Register_Should_Fill_Available_Columns_And_Leave_Missing_Ones_Empty()
    {
        var env = await SeedTenantAsync();
        var client = await env.SeedClientAsync("Ayse Yilmaz", "Bornova / Izmir");
        var pet = await env.SeedPetAsync(client, "Pamuk", birthDate: new DateOnly(2023, 1, 10), gender: PetGender.Female);
        var examId = await env.SeedExaminationAsync(pet, DayStartUtc.AddHours(10), "Kusma", "Gastrit", plan: "Diyet", withVisit: true, finalize: true);
        await env.SeedTreatmentAsync(pet, examId, "Serum tedavisi", DayStartUtc.AddHours(10));
        await env.SeedPrescriptionAsync(pet, examId, DayStartUtc.AddHours(10), ("Amoksilin", 2.5m), ("Probiyotik", null));
        var product = await env.SeedProductAsync("Asi X", "doz");
        await env.SeedAdministrationAsync(pet, examId, product, 1m, lateAdded: false, voided: false);
        await env.SeedAdministrationAsync(pet, examId, product, 2m, lateAdded: true, voided: false);
        await env.SeedAdministrationAsync(pet, examId, product, 5m, lateAdded: false, voided: true);

        var hidden = await env.GetExaminationsAsync();
        var shown = await env.GetExaminationsAsync("&includeVoided=true");

        hidden.TotalCount.Should().Be(1);
        var row = hidden.Items.Single();
        row.RowNumber.Should().Be(1);
        row.ExaminationId.Should().Be(examId);
        row.ExaminedAtUtc.Should().BeCloseTo(DayStartUtc.AddHours(10), TimeSpan.FromSeconds(1));
        row.IsFinalized.Should().BeTrue();
        row.OwnerName.Should().Be("Ayse Yilmaz");
        row.OwnerPhone.Should().StartWith("90555");
        row.OwnerAddress.Should().Be("Bornova / Izmir");
        row.PetName.Should().Be("Pamuk");
        row.Species.Should().NotBeNullOrEmpty();
        row.Breed.Should().Be("Tekir");
        row.Gender.Should().Be("Dişi");
        row.Age.Should().Be("3 yıl 2 ay");
        row.MicrochipNumber.Should().Be("900111222333444");
        row.ProtocolNumber.Should().Be("P-1");
        row.Complaint.Should().Be("Kusma");
        row.Diagnosis.Should().Be("Gastrit");
        row.Treatments.Should().Equal("Serum tedavisi");
        row.VeterinarianName.Should().Be("Dr. Kayit Defteri");

        // Eksik alanlar her zaman boş.
        row.CardNumber.Should().BeNull();
        row.EPrescriptionId.Should().BeNull();

        var prescription = row.Prescriptions.Should().ContainSingle().Subject;
        prescription.Items.Select(i => (i.Name, i.Quantity)).Should().Equal(("Amoksilin", 2.5m), ("Probiyotik", (decimal?)null));

        // İptal edilen uygulama varsayılan gizli; sonradan eklenen işaretli.
        row.Administrations.Select(a => (a.Quantity, a.AddedAfterFinalization, a.IsVoided))
            .Should().Equal((1m, false, false), (2m, true, false));
        shown.Items.Single().Administrations.Select(a => (a.Quantity, a.IsVoided))
            .Should().Equal((1m, false), (2m, false), (5m, true));
    }

    [Fact]
    public async Task Examination_Register_Should_Use_Istanbul_Day_Boundaries()
    {
        var env = await SeedTenantAsync();
        var pet = await env.SeedPetAsync(await env.SeedClientAsync("Sinir Test"), "Sinir");
        var before = await env.SeedExaminationAsync(pet, DayStartUtc.AddSeconds(-1), "Onceki gun 23:59:59");
        var first = await env.SeedExaminationAsync(pet, DayStartUtc, "Gun basi 00:00:00");
        var last = await env.SeedExaminationAsync(pet, DayStartUtc.AddDays(1).AddSeconds(-1), "Gun sonu 23:59:59");
        var after = await env.SeedExaminationAsync(pet, DayStartUtc.AddDays(1), "Sonraki gun 00:00:00");

        var one = await env.GetExaminationsAsync();
        var two = await env.GetExaminationsAsync(from: "2026-03-09", to: "2026-03-10");

        one.Items.Select(i => i.ExaminationId).Should().Equal(first, last);
        two.Items.Select(i => i.ExaminationId).Should().Equal(before, first, last);
        two.Items.Select(i => i.ExaminationId).Should().NotContain(after);
    }

    [Fact]
    public async Task Examination_Register_Should_Number_Rows_By_Date_And_Page_Without_Overlap()
    {
        var env = await SeedTenantAsync();
        var pet = await env.SeedPetAsync(await env.SeedClientAsync("Sayfa Test"), "Sayfa");
        var ids = new List<Guid>();
        // Ters sırada eklenir: sıra tarihten gelir, ekleme sırasından değil.
        for (var i = 4; i >= 0; i--)
            ids.Add(await env.SeedExaminationAsync(pet, DayStartUtc.AddHours(1 + i), $"Sebep {i}"));
        ids.Reverse();

        var page1 = await env.GetExaminationsAsync("&page=1&pageSize=2");
        var page2 = await env.GetExaminationsAsync("&page=2&pageSize=2");
        var page3 = await env.GetExaminationsAsync("&page=3&pageSize=2");

        page1.Items.Select(i => i.ExaminationId).Should().Equal(ids[0], ids[1]);
        page2.Items.Select(i => i.ExaminationId).Should().Equal(ids[2], ids[3]);
        page3.Items.Select(i => i.ExaminationId).Should().Equal(ids[4]);
        page1.Items.Concat(page2.Items).Concat(page3.Items).Select(i => i.RowNumber).Should().Equal(1, 2, 3, 4, 5);
        new[] { page1, page2, page3 }.Should().OnlyContain(p => p.TotalCount == 5);
    }

    [Fact]
    public async Task Veterinarian_Should_Be_Empty_When_Visit_Has_No_Responsible_Veterinarian_Even_If_Finalized()
    {
        var env = await SeedTenantAsync();
        var client = await env.SeedClientAsync("Ayse Yilmaz", "Bornova / Izmir");
        var pet = await env.SeedPetAsync(client, "Pamuk");
        var noVet = await env.SeedExaminationAsync(pet, DayStartUtc.AddHours(9), "Birinci", withVisit: true, finalize: true, visitHasVeterinarian: false);
        var noVisit = await env.SeedExaminationAsync(pet, DayStartUtc.AddHours(11), "Ikinci", finalize: true);
        await env.SeedPrescriptionAsync(pet, noVet, DayStartUtc.AddHours(9), ("Amoksilin", 2m));

        var examinations = await env.GetExaminationsAsync();
        var prescriptions = await env.GetPrescriptionsAsync();

        examinations.Items.Select(i => i.ExaminationId).Should().Equal(noVet, noVisit);
        examinations.Items.Should().OnlyContain(i => i.VeterinarianName == null);
        prescriptions.Items.Should().ContainSingle().Which.VeterinarianName.Should().BeNull();
    }

    [Fact]
    public async Task Examination_Screen_Csv_And_Xlsx_Should_Match()
    {
        var env = await SeedTenantAsync();
        var client = await env.SeedClientAsync("Ayse Yilmaz", "Bornova / Izmir");
        var pet = await env.SeedPetAsync(client, "Pamuk", birthDate: new DateOnly(2024, 1, 10));
        var first = await env.SeedExaminationAsync(pet, DayStartUtc.AddHours(9), "Birinci", "Teshis A", withVisit: true, finalize: true);
        var second = await env.SeedExaminationAsync(pet, DayStartUtc.AddHours(11), "Ikinci");
        await env.SeedPrescriptionAsync(pet, first, DayStartUtc.AddHours(9), ("Amoksilin", 2m));
        var product = await env.SeedProductAsync("Asi X", "doz");
        await env.SeedAdministrationAsync(pet, first, product, 1m, lateAdded: true, voided: false);

        var screen = await env.GetExaminationsAsync();
        var csvResponse = await env.Http.GetAsync($"{ExaminationsUrl}/export?from={Day}&to={Day}");
        var xlsxResponse = await env.Http.GetAsync($"{ExaminationsUrl}/export-xlsx?from={Day}&to={Day}");

        screen.Items.Select(i => i.ExaminationId).Should().Equal(first, second);
        csvResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        csvResponse.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        csvResponse.Content.Headers.ContentDisposition!.FileName.Should().MatchRegex(@"^muayene-defteri-\d{8}\.csv$");
        var csvBytes = await csvResponse.Content.ReadAsByteArrayAsync();
        csvBytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var csv = Encoding.UTF8.GetString(csvBytes).TrimStart('﻿')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split(';')).ToList();
        csv.Select(r => r[0]).Should().Equal(screen.Items.Select(i => i.RowNumber.ToString(CultureInfo.InvariantCulture)));
        csv.Select(r => r[1]).Should().OnlyContain(v => v == "10.03.2026");
        csv.Select(r => r[2]).Should().Equal(screen.Items.Select(i => i.OwnerName));
        csv.Select(r => r[5]).Should().Equal(screen.Items.Select(i => i.PetName));
        csv.Select(r => r[9]).Should().Equal(screen.Items.Select(i => i.Age ?? string.Empty));
        string.Join(";", csv[0]).Should().Contain("Recete: Amoksilin ×2; Satış: Asi X 1 doz (sonradan eklendi)");

        xlsxResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        xlsxResponse.Content.Headers.ContentDisposition!.FileName.Should().MatchRegex(@"^muayene-defteri-\d{8}\.xlsx$");
        using var wb = new XLWorkbook(new MemoryStream(await xlsxResponse.Content.ReadAsByteArrayAsync()));
        var ws = wb.Worksheet(1);
        var rows = Enumerable.Range(3, ws.LastRowUsed()!.RowNumber() - 2).ToList();
        rows.Select(r => (int)ws.Cell(r, 1).GetDouble()).Should().Equal(screen.Items.Select(i => i.RowNumber));
        rows.Select(r => ws.Cell(r, 3).GetString()).Should().Equal(screen.Items.Select(i => i.OwnerName));
        rows.Select(r => ws.Cell(r, 15).GetString()).Should().Equal(screen.Items.Select(i => i.Complaint));
        ws.Cell(3, 2).GetDateTime().Should().Be(new DateTime(2026, 3, 10));
    }

    [Fact]
    public async Task Prescription_Register_Should_Expand_Lines_Handle_Legacy_And_Match_Csv_And_Xlsx()
    {
        var env = await SeedTenantAsync();
        var client = await env.SeedClientAsync("Ayse Yilmaz", "Bornova / Izmir");
        var pet = await env.SeedPetAsync(client, "Pamuk");
        var examId = await env.SeedExaminationAsync(pet, DayStartUtc.AddHours(9), "Muayene", withVisit: true, finalize: true);
        var structured = await env.SeedPrescriptionAsync(pet, examId, DayStartUtc.AddHours(9), ("Amoksilin", 2.5m), ("Probiyotik", null));
        var legacy = await env.SeedLegacyPrescriptionAsync(pet, null, DayStartUtc.AddHours(12), "Serbest metin recete");
        var otherDay = await env.SeedLegacyPrescriptionAsync(pet, null, DayStartUtc.AddDays(1), "Ertesi gun");

        var screen = await env.GetPrescriptionsAsync();

        screen.TotalCount.Should().Be(2);
        screen.Items.Select(e => e.PrescriptionId).Should().Equal(structured, legacy);
        screen.Items.Select(e => e.PrescriptionId).Should().NotContain(otherDay);
        screen.Items.Select(e => e.RowNumber).Should().Equal(1, 2);
        var entry = screen.Items[0];
        entry.OwnerName.Should().Be("Ayse Yilmaz");
        entry.OwnerAddress.Should().Be("Bornova / Izmir");
        entry.VeterinarianName.Should().Be("Dr. Kayit Defteri");
        entry.VeterinarianAddress.Should().Be("Klinik Adres 1");
        entry.AnimalCount.Should().Be(1);
        entry.Species.Should().NotBeNullOrEmpty();
        entry.Items.Select(i => (i.Name, i.Quantity)).Should().Equal(("Amoksilin", 2.5m), ("Probiyotik", (decimal?)null));
        entry.PrescriptionClass.Should().BeNull();
        entry.VolumeSequenceNumber.Should().BeNull();
        entry.EarTagNumber.Should().BeNull();
        entry.Items.Should().OnlyContain(i => i.PharmacologicalForm == null && i.Packaging == null);
        var legacyEntry = screen.Items[1];
        legacyEntry.VeterinarianName.Should().BeNull();
        legacyEntry.Items.Should().ContainSingle().Which.Name.Should().Be("Serbest metin recete");

        var csvResponse = await env.Http.GetAsync($"{PrescriptionsUrl}/export?from={Day}&to={Day}");
        csvResponse.Content.Headers.ContentDisposition!.FileName.Should().MatchRegex(@"^recete-defteri-\d{8}\.csv$");
        var csv = Encoding.UTF8.GetString(await csvResponse.Content.ReadAsByteArrayAsync()).TrimStart('﻿')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        csv.Should().HaveCount(2);
        csv.Select(l => l.Split(';')[0]).Should().Equal("1", "2");
        csv[0].Should().EndWith("\"Amoksilin ×2,5; Probiyotik\"");
        csv[1].Should().EndWith(";Serbest metin recete");

        var xlsxResponse = await env.Http.GetAsync($"{PrescriptionsUrl}/export-xlsx?from={Day}&to={Day}");
        using var wb = new XLWorkbook(new MemoryStream(await xlsxResponse.Content.ReadAsByteArrayAsync()));
        var ws = wb.Worksheet(1);
        ws.Cell(1, 2).GetString().Should().Be("Reçetenin");
        ws.Cell(2, 2).GetString().Should().Be("Tarihi");
        var rows = Enumerable.Range(3, ws.LastRowUsed()!.RowNumber() - 2).ToList();
        rows.Select(r => ws.Cell(r, 12).GetString()).Should().Equal("Amoksilin ×2,5; Probiyotik", "Serbest metin recete");
    }

    [Fact]
    public async Task Prescription_Register_Should_Page_By_Prescription_Not_By_Line()
    {
        var env = await SeedTenantAsync();
        var pet = await env.SeedPetAsync(await env.SeedClientAsync("Sayfa Test"), "Sayfa");
        var a = await env.SeedPrescriptionAsync(pet, null, DayStartUtc.AddHours(1), ("A1", 1m), ("A2", 1m), ("A3", 1m));
        var b = await env.SeedPrescriptionAsync(pet, null, DayStartUtc.AddHours(2), ("B1", 1m));
        var c = await env.SeedPrescriptionAsync(pet, null, DayStartUtc.AddHours(3), ("C1", 1m), ("C2", 1m));

        var page1 = await env.GetPrescriptionsAsync("&page=1&pageSize=2");
        var page2 = await env.GetPrescriptionsAsync("&page=2&pageSize=2");

        page1.TotalCount.Should().Be(3);
        page1.Items.Select(e => e.PrescriptionId).Should().Equal(a, b);
        page1.Items.Sum(e => e.Items.Count).Should().Be(4);
        page2.Items.Select(e => e.PrescriptionId).Should().Equal(c);
        page2.Items[0].RowNumber.Should().Be(3);
    }

    [Fact]
    public async Task Registers_Should_Only_Show_Clinics_Assigned_To_The_User()
    {
        var env = await SeedTenantAsync();
        var clinicB = await env.SeedClinicAsync();
        var pet = await env.SeedPetAsync(await env.SeedClientAsync("Klinik Test"), "Klinik");
        var mine = await env.SeedExaminationAsync(pet, DayStartUtc.AddHours(2), "Benim klinik");
        await env.SeedExaminationAsync(pet, DayStartUtc.AddHours(3), "Baska klinik", clinicId: clinicB);
        var myRx = await env.SeedPrescriptionAsync(pet, null, DayStartUtc.AddHours(2), ("X", 1m));
        await env.SeedPrescriptionInClinicAsync(clinicB, pet, DayStartUtc.AddHours(3), ("Y", 1m));

        (await env.GetExaminationsAsync()).Items.Select(i => i.ExaminationId).Should().Equal(mine);
        (await env.GetExaminationsAsync($"&clinicId={env.ClinicId}")).TotalCount.Should().Be(1);
        (await env.GetPrescriptionsAsync()).Items.Select(i => i.PrescriptionId).Should().Equal(myRx);

        foreach (var url in new[] { ExaminationsUrl, $"{ExaminationsUrl}/export", $"{ExaminationsUrl}/export-xlsx", PrescriptionsUrl, $"{PrescriptionsUrl}/export", $"{PrescriptionsUrl}/export-xlsx" })
        {
            var foreign = await env.Http.GetAsync($"{url}?from={Day}&to={Day}&clinicId={clinicB}");
            foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await IntegrationTestProblemDetails.ReadCodeAsync(foreign)).Should().Be("Clinics.AccessDenied");
        }
    }

    [Fact]
    public async Task Registers_Should_Not_Expose_Other_Tenant_Records()
    {
        var one = await SeedTenantAsync();
        var two = await SeedTenantAsync();
        var petOne = await one.SeedPetAsync(await one.SeedClientAsync("Birinci Sahip"), "Birinci");
        var petTwo = await two.SeedPetAsync(await two.SeedClientAsync("Ikinci Sahip"), "Ikinci");
        var e1 = await one.SeedExaminationAsync(petOne, DayStartUtc.AddHours(2), "Bir");
        var e2 = await two.SeedExaminationAsync(petTwo, DayStartUtc.AddHours(2), "Iki");
        var r1 = await one.SeedPrescriptionAsync(petOne, null, DayStartUtc.AddHours(2), ("Bir", 1m));
        var r2 = await two.SeedPrescriptionAsync(petTwo, null, DayStartUtc.AddHours(2), ("Iki", 1m));

        (await one.GetExaminationsAsync()).Items.Select(i => i.ExaminationId).Should().Equal(e1);
        (await two.GetExaminationsAsync()).Items.Select(i => i.ExaminationId).Should().Equal(e2);
        (await one.GetPrescriptionsAsync()).Items.Select(i => i.PrescriptionId).Should().Equal(r1);
        (await two.GetPrescriptionsAsync()).Items.Select(i => i.PrescriptionId).Should().Equal(r2);

        foreach (var suffix in new[] { "", "/export", "/export-xlsx" })
        {
            foreach (var baseUrl in new[] { ExaminationsUrl, PrescriptionsUrl })
            {
                var cross = await one.Http.GetAsync($"{baseUrl}{suffix}?from={Day}&to={Day}&clinicId={two.ClinicId}");
                cross.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
            }
        }
    }

    [Fact]
    public async Task Each_Register_Needs_Its_Own_Read_Permission()
    {
        var env = await SeedTenantAsync();
        var anonymous = _factory.CreateClient();
        var examinationsOnly = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Examinations.Read]);
        var prescriptionsOnly = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Prescriptions.Read]);
        var neither = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Pets.Read, PermissionCatalog.Clients.Read]);
        var query = $"?from={Day}&to={Day}";

        foreach (var suffix in new[] { "", "/export", "/export-xlsx" })
        {
            (await anonymous.GetAsync($"{ExaminationsUrl}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await anonymous.GetAsync($"{PrescriptionsUrl}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            (await examinationsOnly.GetAsync($"{ExaminationsUrl}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await examinationsOnly.GetAsync($"{PrescriptionsUrl}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await prescriptionsOnly.GetAsync($"{PrescriptionsUrl}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await prescriptionsOnly.GetAsync($"{ExaminationsUrl}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await neither.GetAsync($"{ExaminationsUrl}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await neither.GetAsync($"{PrescriptionsUrl}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Registers_Should_Return_400_For_Invalid_Dates_And_403_For_Clinic_Conflict()
    {
        var env = await SeedTenantAsync();

        foreach (var baseUrl in new[] { ExaminationsUrl, PrescriptionsUrl })
        {
            foreach (var suffix in new[] { "", "/export", "/export-xlsx" })
            {
                await ExpectAsync(env.Http, $"{baseUrl}{suffix}", "Registers.DateRangeInvalid");
                await ExpectAsync(env.Http, $"{baseUrl}{suffix}?from=2026-03-11&to=2026-03-10", "Registers.DateRangeInvalid");
                await ExpectAsync(env.Http, $"{baseUrl}{suffix}?from=2025-01-01&to=2026-03-10", "Registers.RangeTooLong");
            }
        }

        var clinicB = await env.SeedClinicAsync();
        var scoped = await env.NewHttpForAnotherUserAsync(ReaderPermissions, clinicContext: env.ClinicId);
        var mismatch = await scoped.GetAsync($"{ExaminationsUrl}?from={Day}&to={Day}&clinicId={clinicB}");
        mismatch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(mismatch)).Should().Be("Context.ClinicConflict");

        static async Task ExpectAsync(HttpClient http, string url, string code)
        {
            var response = await http.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, url);
            (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be(code, url);
        }
    }

    // ---- Kurulum ----

    private async Task<Env> SeedTenantAsync()
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
            var tenant = new Tenant($"Reg-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
            db.Tenants.Add(tenant);
            db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
            var clinic = new Clinic(tenant.Id, $"Reg-{Guid.NewGuid():N}"[..14], "Izmir", address: "Klinik Adres 1");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            (userId, email) = await Env.SeedUserAsync(db, hasher, tenant.Id, clinic.Id, "Dr. Kayit Defteri");
            tenantId = tenant.Id;
            clinicId = clinic.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, ReaderPermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Env(_factory.Services, hasher, _factory.CreateClient, http, tenantId, clinicId, userId);
    }

    private sealed class Env
    {
        private readonly IServiceProvider _services;
        private readonly IPasswordHasher _hasher;
        private readonly Func<HttpClient> _createClient;

        public Env(IServiceProvider services, IPasswordHasher hasher, Func<HttpClient> createClient, HttpClient http, Guid tenantId, Guid clinicId, Guid userId)
        {
            _services = services;
            _hasher = hasher;
            _createClient = createClient;
            Http = http;
            TenantId = tenantId;
            ClinicId = clinicId;
            UserId = userId;
        }

        public HttpClient Http { get; }
        public Guid TenantId { get; }
        public Guid ClinicId { get; }
        public Guid UserId { get; }

        public static async Task<(Guid UserId, string Email)> SeedUserAsync(
            AppDbContext db, IPasswordHasher hasher, Guid tenantId, Guid clinicId, string? displayName = null)
        {
            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            var email = $"reg-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            if (displayName is not null)
                user.SetDisplayName(displayName, DateTime.UtcNow).IsSuccess.Should().BeTrue();
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenantId, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinicId, DateTime.UtcNow));
            await db.SaveChangesAsync();
            return (user.Id, email);
        }

        public async Task<HttpClient> NewHttpForAnotherUserAsync(string[] permissions, Guid? clinicContext = null)
        {
            string email;
            await using (var scope = _services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                (_, email) = await SeedUserAsync(db, _hasher, TenantId, ClinicId);
            }

            var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_services, email, permissions, clinicContext);
            var http = _createClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        /// <summary><paramref name="extra"/>: <c>&amp;</c> ile başlayan ek parametreler. Varsayılan aralık tek gün (<see cref="Day"/>).</summary>
        public Task<RegisterReadResult<ExaminationRegisterItemDto>> GetExaminationsAsync(string? extra = null, string from = Day, string to = Day)
            => GetAsync<RegisterReadResult<ExaminationRegisterItemDto>>(ExaminationsUrl, extra, from, to);

        public Task<RegisterReadResult<PrescriptionRegisterEntryDto>> GetPrescriptionsAsync(string? extra = null, string from = Day, string to = Day)
            => GetAsync<RegisterReadResult<PrescriptionRegisterEntryDto>>(PrescriptionsUrl, extra, from, to);

        private async Task<T> GetAsync<T>(string url, string? extra, string from, string to)
        {
            var response = await Http.GetAsync($"{url}?from={from}&to={to}{extra}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }

        public async Task<Guid> SeedClientAsync(string name, string? address = null)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var phone = $"90555{Interlocked.Increment(ref _phoneSeed):D7}";
            var client = new Client(TenantId, name, DateTime.UtcNow, phone, address: address);
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            return client.Id;
        }

        public async Task<Guid> SeedPetAsync(Guid clientId, string name, DateOnly? birthDate = null, PetGender? gender = null)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(
                TenantId, clientId, name, speciesId, breed: "Tekir", birthDate: birthDate, gender: gender,
                microchipNumber: "900111222333444", specialProtocolNumber: "P-1");
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            return pet.Id;
        }

        public async Task<Guid> SeedExaminationAsync(
            Guid petId, DateTime examinedAtUtc, string visitReason, string? assessment = null, string? plan = null,
            bool withVisit = false, bool finalize = false, Guid? clinicId = null, bool visitHasVeterinarian = true)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Guid? visitId = null;
            if (withVisit)
            {
                var visit = new Visit(TenantId, clinicId ?? ClinicId, petId, null, visitHasVeterinarian ? UserId : null, UserId, examinedAtUtc);
                db.Visits.Add(visit);
                visitId = visit.Id;
            }

            var examination = new Examination(
                TenantId, clinicId ?? ClinicId, petId, null, examinedAtUtc, visitReason, null, assessment, null,
                DateTime.UtcNow,
                plan: plan, visitId: visitId);
            if (finalize)
                examination.Finalize(UserId, examinedAtUtc.AddMinutes(30)).IsSuccess.Should().BeTrue();
            db.Examinations.Add(examination);
            await db.SaveChangesAsync();
            return examination.Id;
        }

        public async Task SeedTreatmentAsync(Guid petId, Guid examinationId, string title, DateTime atUtc)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Treatments.Add(new Treatment(TenantId, ClinicId, petId, examinationId, atUtc, title, "Aciklama", null, null, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        public Task<Guid> SeedPrescriptionAsync(
            Guid petId, Guid? examinationId, DateTime atUtc, params (string Name, decimal? Quantity)[] items)
            => SeedPrescriptionCoreAsync(petId, examinationId, atUtc, null, null, items);

        public Task<Guid> SeedPrescriptionInClinicAsync(
            Guid clinicId, Guid petId, DateTime atUtc, params (string Name, decimal? Quantity)[] items)
            => SeedPrescriptionCoreAsync(petId, null, atUtc, null, clinicId, items);

        /// <summary>Eski, serbest metin reçete: satırı yoktur.</summary>
        public Task<Guid> SeedLegacyPrescriptionAsync(Guid petId, Guid? examinationId, DateTime atUtc, string content)
            => SeedPrescriptionCoreAsync(petId, examinationId, atUtc, content, null, []);

        private async Task<Guid> SeedPrescriptionCoreAsync(
            Guid petId, Guid? examinationId, DateTime atUtc, string? content, Guid? clinicId, (string Name, decimal? Quantity)[] items)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var prescription = new Prescription(
                TenantId, clinicId ?? ClinicId, petId, examinationId, null, atUtc, "Recete", content ?? "Recete icerigi", null, null, DateTime.UtcNow);
            if (items.Length > 0)
            {
                prescription.ReplaceItems(items.Select(i => new PrescriptionItemDraft(i.Name, null, null, i.Quantity)).ToList())
                    .IsSuccess.Should().BeTrue();
            }

            db.Prescriptions.Add(prescription);
            await db.SaveChangesAsync();
            return prescription.Id;
        }

        public async Task<Guid> SeedProductAsync(string name, string unit)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var product = new Product(TenantId, name, unit, 10m, "TRY", DateTime.UtcNow);
            db.Products.Add(product);
            await db.SaveChangesAsync();
            return product.Id;
        }

        public async Task SeedAdministrationAsync(
            Guid petId, Guid examinationId, Guid productId, decimal quantity, bool lateAdded, bool voided)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var administration = Administration.Create(
                    TenantId, ClinicId, petId, examinationId, null, productId, quantity,
                    DayStartUtc.AddHours(10).AddMinutes((double)quantity), null,
                    lateAdded, lateAdded ? "Sonradan eklenen ilac kaydi" : null, UserId, DateTime.UtcNow, null)
                .Value!;
            if (voided)
                administration.Void(UserId, "Yanlis kayit iptali", AdministrationVoidStockOutcome.WrittenOff, DateTime.UtcNow).IsSuccess.Should().BeTrue();
            db.Administrations.Add(administration);
            await db.SaveChangesAsync();
        }

        public async Task<Guid> SeedClinicAsync()
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
