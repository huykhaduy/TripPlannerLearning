using Microsoft.EntityFrameworkCore;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Abstraction over the database used by the Application layer.
/// The Application layer depends on this interface — NOT on the concrete
/// <c>ApplicationDbContext</c> in Infrastructure. That keeps use-case code
/// testable (you can substitute an in-memory or fake context).
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Trip> Trips { get; }
    DbSet<ItineraryDay> ItineraryDays { get; }
    DbSet<Destination> Destinations { get; }
    DbSet<ItineraryItem> ItineraryItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
