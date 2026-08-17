using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Configurations;

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
