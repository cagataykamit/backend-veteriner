using Backend.Veteriner.Application.Appointments.Queries.GetList;
using Backend.Veteriner.Application.Appointments.Queries.GetList.Validators;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Examinations.Queries.GetList;
using Backend.Veteriner.Application.Examinations.Queries.GetList.Validators;
using Backend.Veteriner.Application.Hospitalizations.Queries.GetList;
using Backend.Veteriner.Application.Hospitalizations.Queries.GetList.Validators;
using Backend.Veteriner.Application.LabResults.Queries.GetList;
using Backend.Veteriner.Application.LabResults.Queries.GetList.Validators;
using Backend.Veteriner.Application.Prescriptions.Queries.GetList;
using Backend.Veteriner.Application.Prescriptions.Queries.GetList.Validators;
using Backend.Veteriner.Application.Treatments.Queries.GetList;
using Backend.Veteriner.Application.Treatments.Queries.GetList.Validators;
using Backend.Veteriner.Application.Vaccinations.Queries.GetList;
using Backend.Veteriner.Application.Vaccinations.Queries.GetList.Validators;
using AwesomeAssertions;
using FluentValidation.Results;

namespace Backend.Veteriner.Application.Tests.Common;

/// <summary>Klinik listelerde <c>sort</c> beyaz listesi, <c>order</c> ve <c>clientId</c> doğrulaması.</summary>
public sealed class ClinicalListSortValidatorTests
{
    private static PageRequest Page(string? sort, string? order = "asc") => new() { Page = 1, PageSize = 20, Sort = sort, Order = order };

    private static ValidationResult Validate(string list, PageRequest page) => list switch
    {
        "appointments" => new GetAppointmentsListQueryValidator().Validate(new GetAppointmentsListQuery(page)),
        "examinations" => new GetExaminationsListQueryValidator().Validate(new GetExaminationsListQuery(page)),
        "vaccinations" => new GetVaccinationsListQueryValidator().Validate(new GetVaccinationsListQuery(page)),
        "treatments" => new GetTreatmentsListQueryValidator().Validate(new GetTreatmentsListQuery(page)),
        "prescriptions" => new GetPrescriptionsListQueryValidator().Validate(new GetPrescriptionsListQuery(page)),
        "labResults" => new GetLabResultsListQueryValidator().Validate(new GetLabResultsListQuery(page)),
        "hospitalizations" => new GetHospitalizationsListQueryValidator().Validate(new GetHospitalizationsListQuery(page)),
        _ => throw new ArgumentOutOfRangeException(nameof(list))
    };

    [Theory]
    [InlineData("appointments", "scheduledAtUtc")]
    [InlineData("appointments", "ScheduledAtUtc")]
    [InlineData("examinations", "examinedAtUtc")]
    [InlineData("examinations", "ExaminationDateUtc")]
    [InlineData("vaccinations", "dueAtUtc")]
    [InlineData("vaccinations", "appliedAtUtc")]
    [InlineData("vaccinations", "NextDueAtUtc")]
    [InlineData("treatments", "treatmentDateUtc")]
    [InlineData("prescriptions", "prescribedAtUtc")]
    [InlineData("labResults", "requestedAtUtc")]
    [InlineData("hospitalizations", "admittedAtUtc")]
    public void Whitelisted_Sort_Should_Be_Valid(string list, string sort)
        => Validate(list, Page(sort, "desc")).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("appointments")]
    [InlineData("examinations")]
    [InlineData("vaccinations")]
    [InlineData("treatments")]
    [InlineData("prescriptions")]
    [InlineData("labResults")]
    [InlineData("hospitalizations")]
    public void Blank_Sort_Should_Be_Valid(string list)
        => Validate(list, Page(null)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("appointments")]
    [InlineData("examinations")]
    [InlineData("vaccinations")]
    [InlineData("treatments")]
    [InlineData("prescriptions")]
    [InlineData("labResults")]
    [InlineData("hospitalizations")]
    public void Unknown_Sort_Should_Be_Invalid(string list)
    {
        var result = Validate(list, Page("notes"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.StartsWith("sort yalnızca"));
    }

    [Theory]
    [InlineData("appointments")]
    [InlineData("examinations")]
    [InlineData("vaccinations")]
    [InlineData("treatments")]
    [InlineData("prescriptions")]
    [InlineData("labResults")]
    [InlineData("hospitalizations")]
    public void Unknown_Order_Should_Be_Invalid(string list)
        => Validate(list, Page(null, "up")).IsValid.Should().BeFalse();

    [Fact]
    public void Empty_ClientId_Should_Be_Invalid_On_Client_Filtered_Lists()
    {
        var page = Page(null);

        new GetAppointmentsListQueryValidator().Validate(new GetAppointmentsListQuery(page, ClientId: Guid.Empty)).IsValid.Should().BeFalse();
        new GetExaminationsListQueryValidator().Validate(new GetExaminationsListQuery(page, ClientId: Guid.Empty)).IsValid.Should().BeFalse();
        new GetVaccinationsListQueryValidator().Validate(new GetVaccinationsListQuery(page, ClientId: Guid.Empty)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Vaccination_DateRange_Should_Reject_From_After_To()
    {
        var now = DateTime.UtcNow;
        var validator = new GetVaccinationsListQueryValidator();

        validator.Validate(new GetVaccinationsListQuery(Page(null), DateFromUtc: now, DateToUtc: now.AddDays(-1))).IsValid.Should().BeFalse();
        validator.Validate(new GetVaccinationsListQuery(Page(null), DateFromUtc: now, DateToUtc: now)).IsValid.Should().BeTrue();
    }
}
