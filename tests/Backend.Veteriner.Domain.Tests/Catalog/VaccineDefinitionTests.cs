using Backend.Veteriner.Domain.Catalog;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Catalog;

public sealed class VaccineDefinitionTests
{
    [Fact]
    public void CreateTenant_defaults_IsCore_false()
    {
        var v = VaccineDefinition.CreateTenant(Guid.NewGuid(), "CUSTOM", "Özel");
        v.IsCore.Should().BeFalse();
    }

    [Fact]
    public void CreateGlobal_normalizes_code_to_uppercase()
    {
        var v = VaccineDefinition.CreateGlobal(" rabies ", "Kuduz");
        v.Code.Should().Be("RABIES");
        v.Name.Should().Be("Kuduz");
        v.TenantId.Should().BeNull();
        v.IsActive.Should().BeTrue();
        v.IsCore.Should().BeTrue();
    }

    [Fact]
    public void CreateTenant_rejects_empty_tenant()
    {
        var act = () => VaccineDefinition.CreateTenant(Guid.Empty, "X", "Y");
        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void Deactivate_sets_inactive_and_touch_updated()
    {
        var v = VaccineDefinition.CreateGlobal("MIXED", "Karma");
        v.Deactivate();
        v.IsActive.Should().BeFalse();
        v.UpdatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void UpdateDetails_rejects_empty_name()
    {
        var v = VaccineDefinition.CreateGlobal("FIV", "FIV");
        var r = v.UpdateDetails("FIV", "  ", null, null, null, isCore: false);
        r.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void UpdateDetails_trims_name_and_sets_species()
    {
        var v = VaccineDefinition.CreateGlobal("LYME", "Lyme");
        var speciesId = Guid.NewGuid();
        var r = v.UpdateDetails("LYME", "  Lyme aşısı  ", "açıklama", 14, speciesId, isCore: true);
        r.IsSuccess.Should().BeTrue();
        v.Name.Should().Be("Lyme aşısı");
        v.Description.Should().Be("açıklama");
        v.DefaultNextDueDays.Should().Be(14);
        v.SpeciesId.Should().Be(speciesId);
        v.IsCore.Should().BeTrue();
    }
}
