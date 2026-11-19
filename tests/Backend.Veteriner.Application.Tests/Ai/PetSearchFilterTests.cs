using AwesomeAssertions;
using Backend.Veteriner.Application.Ai.PatientSearch;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Tests.Ai;

public sealed class PetSearchFilterTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);
    private static readonly string[] Species = ["CAT", "DOG"];

    [Fact]
    public void Normalize_Keeps_Valid_Values_And_Drops_Invalid_Ones_With_Reasons()
    {
        var raw = new PetSearchFilter(
            Species: ["cat", "DRAGON", "CAT"],
            AgeMinYears: 2,
            AgeMaxYears: 99,
            From: Today.AddMonths(-3),
            To: Today,
            ClinicalTerms: ["  kusma  ", "Kusma", "kus%ma'; DROP", "x", "ishal"],
            MedicationTerms: ["amoksisilin"]);

        var (filter, dropped) = PetSearchFilterRules.Normalize(raw, Species, Today);

        filter.Species.Should().Equal("CAT");
        filter.AgeMinYears.Should().Be(2);
        filter.AgeMaxYears.Should().BeNull();
        filter.From.Should().Be(new DateOnly(2026, 8, 19));
        filter.ClinicalTerms.Should().Equal("kusma", "ishal");
        filter.MedicationTerms.Should().Equal("amoksisilin");
        dropped.Should().HaveCount(4);
        dropped.Should().Contain(d => d.Contains("DRAGON"))
            .And.Contain(d => d.Contains("99"))
            .And.Contain(d => d.Contains("kus%ma"))
            .And.Contain(d => d.EndsWith(": x"));
    }

    [Fact]
    public void Normalize_Swaps_Reversed_Ranges_And_Rejects_Far_Dates()
    {
        var (filter, dropped) = PetSearchFilterRules.Normalize(
            new PetSearchFilter(AgeMinYears: 10, AgeMaxYears: 3, From: Today, To: Today.AddDays(-30), VaccineTerms: ["karma"]),
            Species, Today);

        (filter.AgeMinYears, filter.AgeMaxYears).Should().Be((3, 10));
        (filter.From, filter.To).Should().Be((Today.AddDays(-30), Today));
        filter.VaccineState.Should().Be(PetSearchVaccineState.Any, "aşı adı verilip durum verilmediyse her aşı kaydı");
        dropped.Should().BeEmpty();

        var far = PetSearchFilterRules.Normalize(new PetSearchFilter(From: new DateOnly(1950, 1, 1), To: Today.AddYears(5)), Species, Today);
        far.Filter.IsEmpty.Should().BeTrue();
        far.Dropped.Should().HaveCount(2);
    }

    [Fact]
    public void Normalize_Limits_Term_Count()
    {
        var (filter, dropped) = PetSearchFilterRules.Normalize(
            new PetSearchFilter(ClinicalTerms: ["a1", "b2", "c3", "d4", "e5", "f6"]), Species, Today);

        filter.ClinicalTerms.Should().HaveCount(PetSearchFilterRules.MaxTermsPerList);
        dropped.Should().ContainSingle().Which.Should().Contain("f6");
    }

    [Fact]
    public void Criteria_Flags_Follow_The_Filter_Shape()
    {
        new PetSearchFilter().IsEmpty.Should().BeTrue();
        new PetSearchFilter(Species: ["CAT"]).HasClinicalCriteria.Should().BeFalse();

        var periodOnly = new PetSearchFilter(From: Today.AddMonths(-1));
        periodOnly.HasExaminationCriterion.Should().BeTrue("yalnız tarih aralığı = o aralıkta muayeneye gelenler");

        var vaccineWithPeriod = new PetSearchFilter(From: Today, VaccineState: PetSearchVaccineState.Overdue);
        vaccineWithPeriod.HasExaminationCriterion.Should().BeFalse("tarih aralığı aşı ölçütüne aittir");
        vaccineWithPeriod.HasVaccineCriterion.Should().BeTrue();
    }

    [Fact]
    public void Parse_Reads_Known_Fields_And_Drops_Wrong_Types()
    {
        var output = PetSearchPrompt.Parse("""
            {"species":["CAT"],"sex":"Female","neutered":"evet","ageMinYears":2,"status":"Lost",
             "from":"2026-08-19","to":"19.11.2026","clinicalTerms":["kusma"],"vaccineState":"Overdue",
             "sql":"DROP TABLE Pets","unsupported":["sahibinin adı","sahibinin adı",""]}
            """);

        output.Should().NotBeNull();
        var filter = output!.Filter;
        filter.Species.Should().Equal("CAT");
        filter.Sex.Should().Be(PetGender.Female);
        filter.Neutered.Should().BeNull();
        filter.Status.Should().BeNull();
        filter.From.Should().Be(new DateOnly(2026, 8, 19));
        filter.To.Should().BeNull();
        filter.ClinicalTerms.Should().Equal("kusma");
        filter.VaccineState.Should().Be(PetSearchVaccineState.Overdue);
        output.Unsupported.Should().Equal("sahibinin adı");
        output.Dropped.Should().BeEquivalentTo("Anlaşılamayan alan: neutered", "Anlaşılamayan alan: status", "Anlaşılamayan alan: to");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("filtre değil")]
    [InlineData("[1,2]")]
    public void Parse_Returns_Null_For_Non_Object_Output(string? text)
        => PetSearchPrompt.Parse(text).Should().BeNull();

    [Fact]
    public void Parse_Rejects_Numeric_Enum_Values()
        => PetSearchPrompt.Parse("""{"sex":"1"}""")!.Filter.Sex.Should().BeNull();

    [Fact]
    public void Schema_Allows_Only_Filter_Fields_And_Known_Species()
    {
        var schema = PetSearchPrompt.Schema(Species);

        schema["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        schema["properties"]!.AsObject().Select(p => p.Key).Should().BeEquivalentTo(
            "species", "sex", "neutered", "ageMinYears", "ageMaxYears", "status", "from", "to",
            "clinicalTerms", "vaccineTerms", "vaccineState", "medicationTerms", "unsupported");
        schema["properties"]!["species"]!["items"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>())
            .Should().Equal("CAT", "DOG");
    }

    [Theory]
    [InlineData("2020-11-19", 6)]
    [InlineData("2020-11-20", 5)]
    [InlineData("2026-01-01", 0)]
    public void AgeYears_Counts_Completed_Years(string birth, int expected)
        => PetSearchRunner.AgeYears(DateOnly.Parse(birth), Today).Should().Be(expected);
}
