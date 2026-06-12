using System.Security.Claims;
using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.WebApi.Services;

/// <summary>
/// Reads the authenticated user's id from the JWT claims on the current request.
/// This is the Web API's implementation of the Application-layer contract, so
/// use-cases (e.g. TripService) can ask "who is calling?" without touching HTTP.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var sub = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? _httpContextAccessor.HttpContext?.User.FindFirstValue("sub");

            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }
}
