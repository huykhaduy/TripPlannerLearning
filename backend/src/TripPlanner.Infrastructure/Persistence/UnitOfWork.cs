using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.Infrastructure.Persistence;

/// <summary>
/// Wraps <see cref="ApplicationDbContext.SaveChangesAsync"/> (which also
/// stamps audit timestamps — see that override) and translates a unique-index
/// violation into <see cref="ConcurrencyException"/> so the Application layer
/// never needs to reference Microsoft.EntityFrameworkCore's DbUpdateException.
///
/// Like the DbUpdateException catches this replaces (see CLAUDE.md's
/// "Concurrency via unique index + catch/retry" note), this path is NOT
/// exercised by the EF Core InMemory provider used in tests — InMemory throws
/// a raw ArgumentException for a primary-key collision, not DbUpdateException,
/// and does not enforce non-key unique indexes at all. It must be reasoned
/// about directly against the real SQL provider (SQLite/Postgres).
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
        catch (DbUpdateException ex)
        {
            throw new ConcurrencyException("A concurrent write conflicted with this save.", ex);
        }
    }
}
