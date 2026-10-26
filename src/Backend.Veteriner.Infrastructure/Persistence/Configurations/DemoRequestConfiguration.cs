using Backend.Veteriner.Domain.DemoRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>Tanıtım sitesi canlı demo talepleri (TRIAL-005); kiracısız.</summary>
public sealed class DemoRequestConfiguration : IEntityTypeConfiguration<DemoRequest>
{
    public void Configure(EntityTypeBuilder<DemoRequest> b)
    {
        b.ToTable("DemoRequests");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.ContactName).HasMaxLength(DemoRequest.MaxContactNameLength).IsRequired();
        b.Property(x => x.ClinicName).HasMaxLength(DemoRequest.MaxClinicNameLength).IsRequired();
        b.Property(x => x.City).HasMaxLength(DemoRequest.MaxCityLength);
        b.Property(x => x.Email).HasMaxLength(DemoRequest.MaxEmailLength).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(DemoRequest.MaxPhoneLength);
        b.Property(x => x.ClinicSize).HasConversion<int>().IsRequired();
        b.Property(x => x.CurrentSoftware).HasMaxLength(DemoRequest.MaxCurrentSoftwareLength);
        b.Property(x => x.Message).HasMaxLength(DemoRequest.MaxMessageLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.NotificationQueuedAtUtc);

        b.HasIndex(x => x.CreatedAtUtc);
    }
}
