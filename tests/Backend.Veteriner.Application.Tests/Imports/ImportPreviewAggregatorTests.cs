using Backend.Veteriner.Application.Imports.Validation;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>Önizleme toplulaştırması: eşlenemeyen değerler, sorun sayıları ve hatırlatma etkisi (ADR-019 K3).</summary>
public sealed class ImportPreviewAggregatorTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private static string? Issues(params ImportIssue[] issues) => ImportIssueJson.Serialize(issues);

    private static ImportIssue Unmapped(ImportRule rule, string value) => rule.At("X", value: value);

    [Fact]
    public void Unmapped_Values_Should_Be_Grouped_By_Folded_Source_And_Count_Rows_Once()
    {
        var (unmapped, _) = ImportPreviewAggregator.AggregateIssues(
        [
            Issues(Unmapped(ImportRules.SpeciesUnmapped, "Kopek")),
            Issues(Unmapped(ImportRules.SpeciesUnmapped, "KÖPEK ")),
            Issues(Unmapped(ImportRules.SpeciesUnmapped, "Ejderha")),
            Issues(Unmapped(ImportRules.VaccineUnmapped, "Süper Aşı")),
            Issues(Unmapped(ImportRules.BreedFreeText, "Sokak")),
            Issues(Unmapped(ImportRules.ColorFreeText, "Sarı")),
            null,
        ]);

        unmapped.Select(u => (u.Kind, u.Source, u.RowCount, u.Blocking)).Should().Equal(
            ("species", "Kopek", 2, true),
            ("species", "Ejderha", 1, true),
            ("breed", "Sokak", 1, false),
            ("vaccine", "Süper Aşı", 1, true),
            ("color", "Sarı", 1, false));
    }

    [Fact]
    public void Same_Value_In_One_Row_Should_Count_Once()
    {
        var (unmapped, _) = ImportPreviewAggregator.AggregateIssues(
            [Issues(Unmapped(ImportRules.SpeciesUnmapped, "Kopek"), Unmapped(ImportRules.SpeciesUnmapped, "kopek"))]);

        unmapped.Single().RowCount.Should().Be(1);
    }

    [Fact]
    public void Issue_Counts_Should_Count_Rows_Per_Code_Ordered_By_Count()
    {
        var (_, counts) = ImportPreviewAggregator.AggregateIssues(
        [
            Issues(ImportRules.PhoneInvalid.At("Telefon"), ImportRules.EmailInvalid.At("Eposta")),
            Issues(ImportRules.PhoneInvalid.At("Telefon")),
            Issues(ImportRules.DateInvalid.At("DogumTarihi")),
        ]);

        counts.Select(c => (c.Code, c.Severity, c.RowCount)).Should().Equal(
            ("Imports.PhoneInvalid", "Warning", 2),
            ("Imports.DateInvalid", "Error", 1),
            ("Imports.EmailInvalid", "Warning", 1));
    }

    [Fact]
    public void Blocking_Codes_Should_Include_Only_Species_And_Vaccine_With_Rows()
    {
        var (unmapped, _) = ImportPreviewAggregator.AggregateIssues(
        [
            Issues(Unmapped(ImportRules.SpeciesUnmapped, "Kopek")),
            Issues(Unmapped(ImportRules.BreedFreeText, "Sokak")),
        ]);

        ImportPreviewAggregator.BlockingCodes(unmapped).Should().Equal("Imports.SpeciesUnmapped");
    }

    // ---------------------------------------------------------------- Hatırlatma etkisi (K3)

    private static string Vaccination(
        ImportRowAction action = ImportRowAction.Create,
        DateTime? due = null,
        string owner = "file:2",
        bool hasEmail = true)
        => ImportNormalizedJson.Serialize(new ImportNormalizedVaccination(
            "file:2", owner, hasEmail, Guid.NewGuid(), "Kuduz", new DateOnly(2025, 1, 1), null, null, due, null)
        { Action = action });

    [Fact]
    public void Reminder_Impact_Should_Split_Past_Future_And_Window_And_Count_Owners_With_Email()
    {
        var impact = ImportPreviewAggregator.ReminderImpact(
        [
            Vaccination(due: Now.AddDays(-30)), // geçmiş: hatırlatma üretmez
            Vaccination(due: Now.AddDays(2)), // pencere içi (3 gün)
            Vaccination(due: Now.AddDays(2), owner: "file:3"), // pencere içi, başka sahip
            Vaccination(due: Now.AddDays(10)), // gelecek, pencere dışı
            Vaccination(due: Now.AddDays(2), owner: "file:4", hasEmail: false), // e-postasız
            Vaccination(due: null), // sonraki aşı yok
            Vaccination(action: ImportRowAction.Skip, due: Now.AddDays(1)), // oluşmayacak
            null,
        ], Now, windowDays: 3, tenantRemindersEnabled: false);

        impact.VaccinationsWithDueDate.Should().Be(5);
        impact.VaccinationsWithPastDueDate.Should().Be(1);
        impact.VaccinationsWithFutureDueDate.Should().Be(4);
        impact.ClientsWithEmail.Should().Be(2, "file:2 ve file:3 (e-postalı, gelecek vadeli)");
        impact.EmailsWithinReminderWindow.Should().Be(2);
        impact.ReminderWindowDays.Should().Be(3);
        impact.TenantRemindersEnabled.Should().BeFalse();
        impact.ExcludedByDefault.Should().BeTrue();
    }

    [Fact]
    public void Reminder_Window_Should_Be_Open_At_Now_And_Closed_At_End()
    {
        var impact = ImportPreviewAggregator.ReminderImpact(
        [
            Vaccination(due: Now), // şimdi: geçmiş sayılır (DueAtUtc > now şartı)
            Vaccination(due: Now.AddDays(3)), // pencere sonu dahil
            Vaccination(due: Now.AddDays(3).AddSeconds(1)),
        ], Now, windowDays: 3, tenantRemindersEnabled: true);

        impact.VaccinationsWithPastDueDate.Should().Be(1);
        impact.EmailsWithinReminderWindow.Should().Be(1);
        impact.TenantRemindersEnabled.Should().BeTrue();
    }

    [Fact]
    public void Reminder_Impact_Of_Empty_Input_Is_Zero_And_Still_Excluded()
    {
        var impact = ImportPreviewAggregator.ReminderImpact([], Now, 3, false);

        impact.Should().Be(new Backend.Veteriner.Application.Imports.Contracts.Dtos.ImportReminderImpactDto(0, 0, 0, 0, 0, 3, false, true));
    }
}
