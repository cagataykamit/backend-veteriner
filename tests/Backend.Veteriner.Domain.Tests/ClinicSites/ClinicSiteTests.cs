using System.Reflection;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Files;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.ClinicSites;

/// <summary>Site yaşam döngüsü (ADR-045 K2/K9): taslak ve yayın ayrı, sürüm değişmez, dönüş taslağa kopyalar.</summary>
public sealed class ClinicSiteTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private static ClinicSiteConfig Config(string marker = "ilk")
        => ClinicSiteConfig.Parse($$$"""{"schemaVersion":1,"content":{"marker":"{{{marker}}}"}}""").Value!;

    private static ClinicSite NewSite(string marker = "ilk")
        => ClinicSite.Create(TenantId, ClinicId, "ornek-klinik", Config(marker), Now);

    [Fact]
    public void Create_Should_Start_Unpublished_With_Changes()
    {
        var site = NewSite();

        site.IsPublished.Should().BeFalse();
        site.PublishedVersionNumber.Should().BeNull();
        site.IsTemporarilyClosed.Should().BeFalse();
        site.HasUnpublishedChanges.Should().BeTrue();
    }

    [Theory]
    [InlineData("api")]
    [InlineData("Gecersiz Adres")]
    public void Create_Should_Reject_Invalid_Slug(string slug)
        => FluentActions.Invoking(() => ClinicSite.Create(TenantId, ClinicId, slug, Config(), Now)).Should().Throw<ArgumentException>();

    [Fact]
    public void Publish_Should_Number_Sequentially_And_Snapshot_Draft()
    {
        var site = NewSite("ilk");

        var first = site.Publish(UserId, Now).Value!;
        site.SaveDraft(Config("ikinci"), Now.AddMinutes(1));
        var second = site.Publish(UserId, Now.AddMinutes(2)).Value!;

        first.VersionNumber.Should().Be(1);
        first.ConfigJson.Should().Contain("ilk");
        first.PublishedByUserId.Should().Be(UserId);
        second.VersionNumber.Should().Be(2);
        second.ConfigJson.Should().Contain("ikinci");
        site.PublishedVersionNumber.Should().Be(2);
        site.PublishedAtUtc.Should().Be(Now.AddMinutes(2));
    }

    [Fact]
    public void Draft_Change_Should_Not_Touch_Published_Version()
    {
        var site = NewSite("ilk");
        var version = site.Publish(UserId, Now).Value!;
        var before = version.ConfigJson;

        site.SaveDraft(Config("taslak"), Now.AddMinutes(1));

        version.ConfigJson.Should().Be(before);
        site.DraftConfigJson.Should().Contain("taslak");
    }

    [Fact]
    public void HasUnpublishedChanges_Should_Follow_Draft()
    {
        var site = NewSite("ilk");
        site.Publish(UserId, Now);
        site.HasUnpublishedChanges.Should().BeFalse();

        site.SaveDraft(Config("ilk"), Now.AddMinutes(1));
        site.HasUnpublishedChanges.Should().BeFalse("aynı içerik değişiklik sayılmaz");

        site.SaveDraft(Config("yeni"), Now.AddMinutes(2));
        site.HasUnpublishedChanges.Should().BeTrue();
    }

    [Fact]
    public void Publish_Should_Fail_When_Stored_Draft_Is_No_Longer_Valid()
    {
        var site = NewSite();
        typeof(ClinicSite).GetProperty(nameof(ClinicSite.DraftConfigJson))!
            .SetValue(site, """{"schemaVersion":1,"href":"javascript:alert(1)"}""");

        var result = site.Publish(UserId, Now);

        result.IsSuccess.Should().BeFalse();
        site.PublishedVersionNumber.Should().BeNull();
    }

    [Fact]
    public void RestoreToDraft_Should_Copy_Config_Without_Changing_Published()
    {
        var site = NewSite("ilk");
        var first = site.Publish(UserId, Now).Value!;
        site.SaveDraft(Config("ikinci"), Now.AddMinutes(1));
        site.Publish(UserId, Now.AddMinutes(2));

        site.RestoreToDraft(first, Now.AddMinutes(3));

        site.DraftConfigJson.Should().Be(first.ConfigJson);
        site.PublishedVersionNumber.Should().Be(2);
        site.HasUnpublishedChanges.Should().BeTrue();
    }

    [Fact]
    public void RestoreToDraft_Should_Reject_Foreign_Version()
    {
        var other = ClinicSite.Create(TenantId, Guid.NewGuid(), "baska-klinik", Config(), Now);
        var foreign = other.Publish(UserId, Now).Value!;

        FluentActions.Invoking(() => NewSite().RestoreToDraft(foreign, Now)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Close_And_Reopen_Should_Not_Touch_Publication()
    {
        var site = NewSite();
        site.Publish(UserId, Now);

        site.Close(Now);
        site.IsTemporarilyClosed.Should().BeTrue();
        site.PublishedVersionNumber.Should().Be(1);

        site.Reopen(Now);
        site.IsTemporarilyClosed.Should().BeFalse();
        site.PublishedVersionNumber.Should().Be(1);
    }

    [Fact]
    public void Version_Should_Expose_No_Mutator()
    {
        var type = typeof(ClinicSiteVersion);

        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Should().BeEmpty("yayın sürümü oluştuktan sonra değişmez");

        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .Should().Equal(nameof(ClinicSiteVersion.ReferencesImage));

        type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty();
    }

    [Fact]
    public void Version_Should_Know_Referenced_Images()
    {
        var referenced = Guid.NewGuid();
        var site = ClinicSite.Create(
            TenantId,
            ClinicId,
            "ornek-klinik",
            ClinicSiteConfig.Parse($$"""{"schemaVersion":1,"logo":"site-image:{{referenced}}"}""").Value!,
            Now);

        var version = site.Publish(UserId, Now).Value!;

        version.ReferencesImage(referenced).Should().BeTrue();
        version.ReferencesImage(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void Version_List_Search_Should_Match_Whole_Ids_Only()
    {
        var referenced = Guid.NewGuid();
        var other = Guid.NewGuid();
        var list = $"{other:N},{referenced:N}";

        ClinicSiteVersion.ListContainsImage(list, referenced).Should().BeTrue();
        ClinicSiteVersion.ListContainsImage(list, Guid.NewGuid()).Should().BeFalse();
        ClinicSiteVersion.ListContainsImage(string.Empty, referenced).Should().BeFalse();
    }

    [Fact]
    public void ChangeSlug_Should_Not_Limit_Unpublished_Site()
    {
        var site = NewSite();

        for (var i = 0; i < ClinicSite.MaxAddressChangesPerDay + 2; i++)
            site.ChangeSlug($"adres-{i}", Now).IsSuccess.Should().BeTrue();

        site.AddressChangesInWindow.Should().Be(0);
    }

    [Fact]
    public void ChangeSlug_Should_Enforce_Daily_Limit_On_Published_Site()
    {
        var site = NewSite();
        site.Publish(UserId, Now);

        for (var i = 0; i < ClinicSite.MaxAddressChangesPerDay; i++)
            site.ChangeSlug($"adres-{i}", Now.AddMinutes(i)).IsSuccess.Should().BeTrue();

        var blocked = site.ChangeSlug("adres-fazla", Now.AddHours(23));
        blocked.Error.Code.Should().Be(ClinicSite.AddressChangeLimitReachedCode);
        site.Slug.Should().Be($"adres-{ClinicSite.MaxAddressChangesPerDay - 1}");

        site.ChangeSlug("adres-yarin", Now.AddDays(1)).IsSuccess.Should().BeTrue("pencere doldu");
        site.AddressChangesInWindow.Should().Be(1);
    }

    [Fact]
    public void PublishRequirements_Should_List_What_Is_Missing()
    {
        var clinic = new Clinic(TenantId, "Merkez", "İstanbul");

        ClinicSitePublishRequirements.Missing(clinic, []).Should().Equal(
            ClinicSitePublishRequirements.Phone,
            ClinicSitePublishRequirements.Address,
            ClinicSitePublishRequirements.WorkingHours);
    }

    [Fact]
    public void PublishRequirements_Should_Not_Count_Closed_Days_As_Working_Hours()
    {
        var clinic = new Clinic(TenantId, "Merkez", "İstanbul", "05550000000", address: "Kadıköy");
        var closed = ClinicWorkingHour.Create(TenantId, ClinicId, DayOfWeek.Sunday, true, null, null, null, null, Now);
        var open = ClinicWorkingHour.Create(TenantId, ClinicId, DayOfWeek.Monday, false, new TimeOnly(9, 0), new TimeOnly(18, 0), null, null, Now);

        ClinicSitePublishRequirements.Missing(clinic, [closed]).Should().Equal(ClinicSitePublishRequirements.WorkingHours);
        ClinicSitePublishRequirements.Missing(clinic, [closed, open]).Should().BeEmpty();
    }

    [Fact]
    public void Image_Should_Reject_Non_Image_Type_And_Oversize()
    {
        var sha = new string('a', StoredFile.Sha256HexLength);
        ClinicSiteImage Create(StoredFileType type, long size) => ClinicSiteImage.Create(
            Guid.NewGuid(), TenantId, ClinicId, Guid.NewGuid(), "a/b", type, size, sha, UserId, Now);

        FluentActions.Invoking(() => Create(StoredFileType.Pdf, 10)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Create(StoredFileType.Jpeg, ClinicSiteImage.MaxSizeBytes + 1)).Should().Throw<ArgumentOutOfRangeException>();
        Create(StoredFileType.Webp, ClinicSiteImage.MaxSizeBytes).ContentType.Should().Be("image/webp");
    }

    [Fact]
    public void Address_Should_Retire_And_Reactivate()
    {
        var address = ClinicSiteAddress.Create(TenantId, Guid.NewGuid(), "ornek-klinik", Now);
        address.RetiredAtUtc.Should().BeNull();

        address.Retire(Now.AddDays(1));
        address.RetiredAtUtc.Should().Be(Now.AddDays(1));

        address.Reactivate();
        address.RetiredAtUtc.Should().BeNull();
    }
}
