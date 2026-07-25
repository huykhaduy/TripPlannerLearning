using TripPlanner.Domain.Common;

namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Generic data-access abstraction over a single entity type. Implementations
/// live in Infrastructure; the Application layer depends only on this
/// interface, never on EF Core directly.
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<List<T>> GetAllAsync(CancellationToken cancellationToken = default);

    void Add(T entity);

    void Remove(T entity);
}
