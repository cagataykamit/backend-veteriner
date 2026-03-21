using Backend.Veteriner.Domain.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class PetConfiguration : IEntityTypeConfiguration<Pet>
{
    public void Configure(EntityTypeBuilder<Pet> b)
    {
        b.ToTable("Pets");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId)
            .IsRequired();

        b.Property(x => x.ClientId)
            .IsRequired();

        b.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        b.Property(x => x.Species)
            .IsRequired()
            .HasMaxLength(100);

        b.Property(x => x.Breed)
            .HasMaxLength(150);

        b.Property(x => x.BirthDate);

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.ClientId });
    }
}
