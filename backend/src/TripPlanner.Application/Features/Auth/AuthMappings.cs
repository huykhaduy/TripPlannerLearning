using TripPlanner.Application.Features.Auth.Dtos;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Features.Auth;

/// <summary>
/// Entity → DTO mapping for the Auth feature. Deliberately the only place a
/// <see cref="User"/> is turned into a <see cref="UserDto"/>, so sensitive
/// fields (PasswordHash) can never leak to the client by accident.
/// </summary>
public static class AuthMappings
{
    public static UserDto ToDto(this User user) =>
        new(user.Id, user.Email, user.DisplayName);
}
