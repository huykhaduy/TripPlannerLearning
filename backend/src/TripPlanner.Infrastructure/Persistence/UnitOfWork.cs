using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.Infrastructure.Persistence;

/// <summary>
/// Pass-through to <see cref="ApplicationDbContext.SaveChangesAsync"/>, which does
/// the audit stamping and concurrency translation. Deleted in a later task once no
/// service depends on it.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
