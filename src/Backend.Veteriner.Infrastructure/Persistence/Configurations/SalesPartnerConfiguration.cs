using Backend.Veteriner.Domain.SalesPartners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class SalesPartnerConfiguration : IEntityTypeConfiguration<SalesPartner>
{
    public void Configure(EntityTypeBuilder<SalesPartner> b)
    {
        b.ToTable("SalesPartners");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Name).IsRequired().HasMaxLength(SalesPartner.MaxNameLength);
        b.Property(x => x.Email).HasMaxLength(SalesPartner.MaxEmailLength);
        b.Property(x => x.NormalizedEmail).HasMaxLength(SalesPartner.MaxEmailLength);
        b.Property(x => x.Phone).HasMaxLength(SalesPartner.MaxPhoneLength);
        b.Property(x => x.LinkedTenantId);
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.Name);
    }
}
