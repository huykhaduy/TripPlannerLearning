using Microsoft.EntityFrameworkCore;
using TripPlanner.Domain.Common;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence;

/// <summary>
/// The EF Core database context, used directly by the repository classes in
/// this namespace (see Repositories/). Entity-to-table mapping lives in the
/// *Configuration classes in this folder (applied via
/// <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/>).
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<ItineraryDay> ItineraryDays => Set<ItineraryDay>();
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<ItineraryItem> ItineraryItems => Set<ItineraryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Stamp audit timestamps automatically whenever entities are saved.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
