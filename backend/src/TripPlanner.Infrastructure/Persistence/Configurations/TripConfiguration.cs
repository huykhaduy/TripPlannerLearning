using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Configurations;

public class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.HasKey(t => t.Id);

        // See UserConfiguration — BaseEntity supplies the key, not the store.
        builder.Property(t => t.Id).ValueGeneratedNever();

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

        // See UserConfiguration — this is the one that actually bit us: a new day
        // added to trip.Days was being saved as an UPDATE.
        builder.Property(d => d.Id).ValueGeneratedNever();

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

        // See UserConfiguration — BaseEntity supplies the key, not the store.
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.HasOne(i => i.Destination)
            .WithMany()
            .HasForeignKey(i => i.DestinationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Business rule (F3/US4): a destination cannot appear twice in the same day.
        builder.HasIndex(i => new { i.ItineraryDayId, i.DestinationId }).IsUnique();
    }
}
