using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Persistence for the User aggregate. Write methods save their own changes —
/// there is no separate unit of work.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Inserts the user and persists immediately.</summary>
    Task AddAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>Persists pending changes to an already-loaded user.</summary>
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
}
