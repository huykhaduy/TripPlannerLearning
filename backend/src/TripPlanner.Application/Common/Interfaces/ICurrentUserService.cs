namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Exposes the identity of the caller making the current request.
/// Implemented in the Web API layer by reading the JWT claims from
/// <c>HttpContext.User</c>. Used to enforce NFR 6 — users may only access
/// their own trips and destinations.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>The authenticated user's id, or <c>null</c> when anonymous.</summary>
    Guid? UserId { get; }

    bool IsAuthenticated => UserId is not null;
}
