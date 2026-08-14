using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Configurations;

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
