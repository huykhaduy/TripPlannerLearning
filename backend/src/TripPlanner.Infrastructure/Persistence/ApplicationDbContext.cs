using Microsoft.EntityFrameworkCore;
using Npgsql;
using TripPlanner.Application.Common.Exceptions;
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
    /// Stamps audit timestamps, and translates a unique-index violation into
    /// <see cref="ConcurrencyException"/> so the Application layer never sees
    /// EF Core's DbUpdateException.
    ///
    /// Only unique violations are translated. Callers turn a ConcurrencyException
    /// into "this destination is already in that part of the trip", so translating
    /// (say) a length violation too would report the wrong thing to the user.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ConcurrencyException("A concurrent write conflicted with this save.", ex);
        }
    }

    /// <summary>
    /// Postgres-specific by necessity: EF Core offers no provider-agnostic way to
    /// ask "was this a unique violation?", and Postgres is the only supported
    /// database outside tests.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
