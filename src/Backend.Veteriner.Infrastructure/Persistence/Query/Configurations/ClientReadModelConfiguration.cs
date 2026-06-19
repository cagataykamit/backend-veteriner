using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Query.Configurations;

public sealed class ClientReadModelConfiguration : IEntityTypeConfiguration<ClientReadModel>
{
    public void Configure(EntityTypeBuilder<ClientReadModel> b)
    {
        b.ToTable("ClientReadModels");
        b.HasKey(x => x.ClientId);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.FullName).IsRequired().HasMaxLength(QueryReadModelConstraints.ClientName);
        b.Property(x => x.FullNameNormalized).IsRequired().HasMaxLength(QueryReadModelConstraints.ClientNameNormalized);
        b.Property(x => x.Email).HasMaxLength(QueryReadModelConstraints.ClientEmail);
        b.Property(x => x.Phone).HasMaxLength(QueryReadModelConstraints.ClientPhone);
        b.Property(x => x.PhoneNormalized).HasMaxLength(QueryReadModelConstraints.ClientPhoneNormalized);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.LastEventId).IsRequired();
        b.Property(x => x.LastProjectedAtUtc).IsRequired();
        b.Property(x => x.LastEventOccurredAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.FullNameNormalized, x.ClientId })
            .HasDatabaseName("IX_ClientReadModels_TenantId_FullNameNormalized_ClientId");

        b.HasIndex(x => new { x.TenantId, x.PhoneNormalized })
            .HasDatabaseName("IX_ClientReadModels_TenantId_PhoneNormalized");

        b.HasIndex(x => new { x.TenantId, x.Email })
            .HasDatabaseName("IX_ClientReadModels_TenantId_Email");
    }
}
