using Backend.Veteriner.Domain.Catalog;
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

        b.Property(x => x.SpeciesId)
            .IsRequired();

        b.Property(x => x.Breed)
            .HasMaxLength(150);

        b.Property(x => x.BreedId).IsRequired(false);
        b.Property(x => x.ColorId).IsRequired(false);

        b.Property(x => x.Weight)
            .HasColumnType("decimal(6,2)");

        b.Property(x => x.Notes)
            .HasMaxLength(2000);

        b.Property(x => x.Gender)
            .HasConversion<int>();

        b.Property(x => x.BirthDate);

        b.HasOne(x => x.Species)
            .WithMany()
            .HasForeignKey(x => x.SpeciesId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.BreedRef)
            .WithMany()
            .HasForeignKey(x => x.BreedId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.ColorRef)
            .WithMany()
            .HasForeignKey(x => x.ColorId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.ClientId });
        b.HasIndex(x => x.SpeciesId);
        b.HasIndex(x => x.ColorId);
    }
}
