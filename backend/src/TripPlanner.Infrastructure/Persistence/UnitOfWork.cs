using Microsoft.EntityFrameworkCore;
using Npgsql;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.Infrastructure.Persistence;

/// <summary>
/// Wraps <see cref="ApplicationDbContext.SaveChangesAsync"/> (which also
/// stamps audit timestamps — see that override) and translates a unique-index
/// violation into <see cref="ConcurrencyException"/> so the Application layer
/// never needs to reference Microsoft.EntityFrameworkCore's DbUpdateException.
///
/// ONLY a unique violation is translated. <see cref="DbUpdateException"/> also
/// covers foreign-key violations, not-null/length violations and connection
/// faults mid-save — none of which are concurrency conflicts. Translating those
/// too would be actively misleading, because every caller of this method
/// (TripService's three catch blocks) turns a ConcurrencyException into
/// "this destination is already in that part of the trip". A too-long
/// Destination.Name would then surface to the user as a duplicate-place 409.
/// Anything else now propagates as-is and becomes a 500, which is the honest
/// answer for a bug or an outage.
///
/// Like the DbUpdateException catches this replaces (see CLAUDE.md's
/// "Concurrency via unique index + catch/retry" note), this path is NOT
/// exercised by the EF Core InMemory provider used in tests — InMemory throws
/// a raw ArgumentException for a primary-key collision, not DbUpdateException,
/// and does not enforce non-key unique indexes at all. It must be reasoned
/// about directly against the real SQL provider (Postgres).
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
    /// Postgres reports a unique-index violation as SQLSTATE 23505, which
    /// Npgsql surfaces as the <see cref="PostgresException"/> wrapped inside
    /// EF Core's <see cref="DbUpdateException"/>. Provider-specific by
    /// necessity — there is no provider-agnostic way to ask "was this a unique
    /// violation?" — which is fine here: <c>AddPersistence</c> registers
    /// <c>UseNpgsql</c> and Postgres is the only supported database outside tests.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
