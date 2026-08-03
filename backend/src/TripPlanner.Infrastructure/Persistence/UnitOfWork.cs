using Microsoft.EntityFrameworkCore;
using Npgsql;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.Infrastructure.Persistence;

/// <summary>
/// Wraps <see cref="ApplicationDbContext.SaveChangesAsync"/> and translates a
/// unique-index violation into <see cref="ConcurrencyException"/>, so the
/// Application layer never references EF Core's DbUpdateException.
///
/// Only unique violations are translated. Callers turn a ConcurrencyException
/// into "this destination is already in that part of the trip", so translating
/// (say) a length violation too would report the wrong thing to the user.
///
/// Not covered by tests: the InMemory provider doesn't enforce unique indexes.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ConcurrencyException("A concurrent write conflicted with this save.", ex);
        }
    }

    /// <summary>
    /// Postgres-specific by necessity: EF Core offers no provider-agnostic way
    /// to ask "was this a unique violation?", and Postgres is the only
    /// supported database outside tests.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
