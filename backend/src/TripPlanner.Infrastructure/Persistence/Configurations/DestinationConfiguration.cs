using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Configurations;

public class DestinationConfiguration : IEntityTypeConfiguration<Destination>
{
    public void Configure(EntityTypeBuilder<Destination> builder)
    {
        builder.HasKey(d => d.Id);

        // See UserConfiguration — BaseEntity supplies the key, not the store.
        builder.Property(d => d.Id).ValueGeneratedNever();

        // Deliberately unbounded (text). A Geoapify place_id is a ~68-char prefix
        // followed by the hex-encoded UTF-8 place name, so its length scales with the
        // name: 2 chars per byte, i.e. 6 per character in a non-Latin script. Real ids
        // run 62–328 chars, and the old varchar(128) failed with Postgres 22001 for any
        // place named longer than ~30 Latin / ~10 non-Latin characters. Any replacement
        // number would be the same guess about an opaque external id, and in Postgres
        // text costs nothing over varchar(n) — the only real ceiling is the btree limit
        // (~2704 bytes) on the unique index below.
        builder.Property(d => d.ProviderId).IsRequired();
        builder.HasIndex(d => d.ProviderId).IsUnique(); // Cache one row per external place.

        builder.Property(d => d.Name).IsRequired().HasMaxLength(300);
        builder.Property(d => d.Category).HasMaxLength(200);
        builder.Property(d => d.ImageUrl).HasMaxLength(2048);
        builder.Property(d => d.Website).HasMaxLength(2048);
    }
}
