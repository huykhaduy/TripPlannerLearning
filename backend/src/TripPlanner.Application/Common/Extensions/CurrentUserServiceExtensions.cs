using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.Application.Common.Extensions;

public static class CurrentUserServiceExtensions
{
    /// <summary>
    /// The authenticated user's id. Throws <see cref="UnauthorizedException"/>
    /// (HTTP 401) when the caller is anonymous — use this in services whose
    /// operations require a logged-in user (e.g. everything in TripService).
    /// </summary>
    public static Guid GetRequiredUserId(this ICurrentUserService currentUser) =>
        currentUser.UserId
        ?? throw new UnauthorizedException("You must be logged in to perform this action.");
}
