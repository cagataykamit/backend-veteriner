using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Query.Configurations;

public sealed class PetReadModelConfiguration : IEntityTypeConfiguration<PetReadModel>
{
    public void Configure(EntityTypeBuilder<PetReadModel> b)
    {
        b.ToTable("PetReadModels");
        b.HasKey(x => x.PetId);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClientId).IsRequired();

        b.Property(x => x.ClientFullName).IsRequired().HasMaxLength(QueryReadModelConstraints.ClientName);
        b.Property(x => x.ClientFullNameNormalized).IsRequired()
            .HasMaxLength(QueryReadModelConstraints.ClientNameNormalized);

        b.Property(x => x.Name).IsRequired().HasMaxLength(QueryReadModelConstraints.PetName);
        b.Property(x => x.NameNormalized).IsRequired().HasMaxLength(QueryReadModelConstraints.PetNameNormalized);

        b.Property(x => x.SpeciesId).IsRequired();
        b.Property(x => x.SpeciesName).IsRequired().HasMaxLength(QueryReadModelConstraints.SpeciesName);
        b.Property(x => x.SpeciesNameNormalized).IsRequired()
            .HasMaxLength(QueryReadModelConstraints.SpeciesNameNormalized);

        b.Property(x => x.Breed).HasMaxLength(QueryReadModelConstraints.PetBreed);
        b.Property(x => x.BreedRefName).HasMaxLength(QueryReadModelConstraints.PetBreedRefName);

        b.Property(x => x.ColorName).HasMaxLength(QueryReadModelConstraints.PetColorName);
        b.Property(x => x.ColorNameNormalized).HasMaxLength(QueryReadModelConstraints.PetColorNameNormalized);

        b.Property(x => x.Weight).HasColumnType("decimal(6,2)");

        b.Property(x => x.LastEventId).IsRequired();
        b.Property(x => x.LastEventOccurredAtUtc).IsRequired();
        b.Property(x => x.LastProjectedAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.NameNormalized, x.PetId })
            .HasDatabaseName("IX_PetReadModels_TenantId_NameNormalized_PetId");

        b.HasIndex(x => new { x.TenantId, x.ClientId })
            .HasDatabaseName("IX_PetReadModels_TenantId_ClientId");

        b.HasIndex(x => new { x.TenantId, x.ClientFullNameNormalized, x.PetId })
            .HasDatabaseName("IX_PetReadModels_TenantId_ClientFullNameNormalized_PetId");

        b.HasIndex(x => new { x.TenantId, x.SpeciesId })
            .HasDatabaseName("IX_PetReadModels_TenantId_SpeciesId");

        b.HasIndex(x => new { x.TenantId, x.ColorId })
            .HasDatabaseName("IX_PetReadModels_TenantId_ColorId");
    }
}
