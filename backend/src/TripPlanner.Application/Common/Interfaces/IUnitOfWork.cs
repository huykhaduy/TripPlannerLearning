namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Commits changes staged by one or more repositories in a single save, so
/// multi-entity operations (e.g. removing several ItineraryDay rows and
/// adding others when a trip's date range changes) persist atomically.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
