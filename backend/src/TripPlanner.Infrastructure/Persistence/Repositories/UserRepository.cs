using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;

    public UserRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        await _context.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        await _context.Users.AnyAsync(u => u.Email == email, cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The user is already tracked, so this only needs to flush. The parameter stays
    /// for a readable call site — it is deliberately unused, and the flush covers
    /// everything the scoped DbContext is tracking, not only this user.
    ///
    /// Only valid for a user loaded by a TRACKING query (GetByIdAsync /
    /// GetByEmailAsync, neither of which uses AsNoTracking). A detached user would
    /// be saved silently as a no-op — see TripRepository.UpdateAsync for the same
    /// trap spelled out in full.
    /// </summary>
    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default) =>
        await _context.SaveChangesAsync(cancellationToken);
}
