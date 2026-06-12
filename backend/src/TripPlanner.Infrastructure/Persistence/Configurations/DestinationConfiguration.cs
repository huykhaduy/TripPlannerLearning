using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Configurations;

public class DestinationConfiguration : IEntityTypeConfiguration<Destination>
{
    public void Configure(EntityTypeBuilder<Destination> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.ProviderId).IsRequired().HasMaxLength(128);
        builder.HasIndex(d => d.ProviderId).IsUnique(); // Cache one row per external place.

        builder.Property(d => d.Name).IsRequired().HasMaxLength(300);
        builder.Property(d => d.Category).HasMaxLength(200);
        builder.Property(d => d.ImageUrl).HasMaxLength(2048);
        builder.Property(d => d.Website).HasMaxLength(2048);
    }
}
