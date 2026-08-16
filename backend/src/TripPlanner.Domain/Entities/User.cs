using TripPlanner.Domain.Common;

namespace TripPlanner.Domain.Entities;

/// <summary>
/// An application user / account (Feature 4: User Authentication).
/// Passwords are NEVER stored in plain text — only the hash is persisted.
/// </summary>
public class User : BaseEntity
{
    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    public string? DisplayName { get; set; }

    /// <summary>
    /// Feature 4 / US2 — email verification. Registration creates users with this
    /// false; AuthService.VerifyEmailAsync flips it when the emailed link is opened,
    /// and LoginAsync refuses (403) until then.
    /// </summary>
    public bool IsEmailVerified { get; set; }

    // Navigation: a user owns many trips (NFR 6 — users only see their own data).
    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
}
