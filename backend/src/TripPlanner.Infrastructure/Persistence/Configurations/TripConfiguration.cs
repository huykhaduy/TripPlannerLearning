using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Configurations;

public class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);

        builder.HasMany(t => t.Days)
            .WithOne(d => d.Trip!)
            .HasForeignKey(d => d.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Items)
            .WithOne(i => i.Trip!)
            .HasForeignKey(i => i.TripId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ItineraryDayConfiguration : IEntityTypeConfiguration<ItineraryDay>
{
    public void Configure(EntityTypeBuilder<ItineraryDay> builder)
    {
        builder.HasKey(d => d.Id);

        builder.HasMany(d => d.Items)
            .WithOne(i => i.ItineraryDay!)
            .HasForeignKey(i => i.ItineraryDayId)
            .OnDelete(DeleteBehavior.SetNull); // Removing a day returns items to "Saved Places".
    }
}

public class ItineraryItemConfiguration : IEntityTypeConfiguration<ItineraryItem>
{
    public void Configure(EntityTypeBuilder<ItineraryItem> builder)
    {
        builder.HasKey(i => i.Id);

        builder.HasOne(i => i.Destination)
            .WithMany()
            .HasForeignKey(i => i.DestinationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Business rule (F3/US4): a destination cannot appear twice in the same day.
        // ItineraryDayId is trip-scoped by its own FK, so this index alone is enough
        // to enforce "once per day" per trip.
        builder.HasIndex(i => new { i.ItineraryDayId, i.DestinationId }).IsUnique();

        // Postgres treats every NULL as distinct, so the index above does NOT cover
        // Saved Places (ItineraryDayId == null) — without this, two concurrent adds
        // of the same destination to a trip's Saved Places would both succeed. Since
        // ItineraryDayId no longer identifies the trip when it's null, scope this one
        // to TripId instead.
        builder.HasIndex(i => new { i.TripId, i.DestinationId })
            .IsUnique()
            .HasFilter("\"ItineraryDayId\" IS NULL")
            .HasDatabaseName("IX_ItineraryItems_TripId_DestinationId_SavedPlaces");

        // EF Core's FK-index convention treats the composite index above as already
        // covering TripId lookups (it doesn't account for the filter), and would
        // otherwise drop this plain FK index — declare it explicitly so ordinary
        // "all items for this trip" queries keep an index to use.
        builder.HasIndex(i => i.TripId);
    }
}
