using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ClinicSiteConfiguration : IEntityTypeConfiguration<ClinicSite>
{
    public void Configure(EntityTypeBuilder<ClinicSite> b)
    {
        b.ToTable("ClinicSites");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.Slug).IsRequired().HasMaxLength(ClinicSiteSlug.MaxLength).IsUnicode(false);
        b.Property(x => x.DraftConfigJson).IsRequired();
        b.Property(x => x.DraftUpdatedAtUtc).IsRequired();
        b.Property(x => x.HasUnpublishedChanges).IsRequired();
        b.Property(x => x.PublishedVersionNumber);
        b.Property(x => x.PublishedAtUtc);
        b.Property(x => x.IsTemporarilyClosed).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.Ignore(x => x.IsPublished);

        // Klinik başına tek site (ADR-045 K2).
        b.HasIndex(x => new { x.TenantId, x.ClinicId }).IsUnique();
        b.HasIndex(x => x.Slug).IsUnique();

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ClinicSiteVersionConfiguration : IEntityTypeConfiguration<ClinicSiteVersion>
{
    public void Configure(EntityTypeBuilder<ClinicSiteVersion> b)
    {
        b.ToTable("ClinicSiteVersions");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.SiteId).IsRequired();
        b.Property(x => x.VersionNumber).IsRequired();
        b.Property(x => x.ConfigJson).IsRequired();
        b.Property(x => x.ReferencedImageIds).IsRequired().IsUnicode(false);
        b.Property(x => x.PublishedByUserId).IsRequired();
        b.Property(x => x.PublishedAtUtc).IsRequired();

        // Yayın sürümü değişmezdir (ADR-045 K2): kaydedilmiş satırda alan değişikliği EF'te hata verir.
        foreach (var property in b.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        b.HasIndex(x => new { x.TenantId, x.SiteId, x.VersionNumber }).IsUnique();

        b.HasOne<ClinicSite>()
            .WithMany()
            .HasForeignKey(x => x.SiteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ClinicSiteAddressConfiguration : IEntityTypeConfiguration<ClinicSiteAddress>
{
    public void Configure(EntityTypeBuilder<ClinicSiteAddress> b)
    {
        b.ToTable("ClinicSiteAddresses");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.SiteId).IsRequired();
        b.Property(x => x.Slug).IsRequired().HasMaxLength(ClinicSiteSlug.MaxLength).IsUnicode(false);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.RetiredAtUtc);

        // Güncel ve eski adres tüm kiracılarda benzersizdir; başkasına verilmez (ADR-045 K10).
        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.SiteId });

        b.HasOne<ClinicSite>()
            .WithMany()
            .HasForeignKey(x => x.SiteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ClinicSiteImageConfiguration : IEntityTypeConfiguration<ClinicSiteImage>
{
    public void Configure(EntityTypeBuilder<ClinicSiteImage> b)
    {
        b.ToTable("ClinicSiteImages");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.SiteId).IsRequired();
        b.Property(x => x.StorageKey).IsRequired().HasMaxLength(StoredFile.StorageKeyMaxLength).IsUnicode(false);
        b.Property(x => x.ContentType).IsRequired().HasMaxLength(StoredFile.ContentTypeMaxLength).IsUnicode(false);
        b.Property(x => x.SizeBytes).IsRequired();
        b.Property(x => x.Sha256).IsRequired().HasMaxLength(StoredFile.Sha256HexLength).IsFixedLength().IsUnicode(false);
        b.Property(x => x.UploadedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.DeletedAtUtc);
        b.Property(x => x.DeletedByUserId);

        b.Ignore(x => x.IsDeleted);

        b.HasIndex(x => x.StorageKey).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.SiteId });

        b.HasOne<ClinicSite>()
            .WithMany()
            .HasForeignKey(x => x.SiteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
