using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.Infrastructure.Identity;

/// <summary>
/// Password hashing using BCrypt (an adaptive, salted hash). The generated hash
/// already embeds its salt, so we only need to store the single hash string.
/// </summary>
public class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public bool Verify(string password, string passwordHash) =>
        BCrypt.Net.BCrypt.Verify(password, passwordHash);
}
