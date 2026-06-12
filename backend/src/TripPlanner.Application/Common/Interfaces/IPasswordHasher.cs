namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Hashes and verifies passwords. Implemented in Infrastructure (BCrypt).
/// The Application layer only knows the contract, not the algorithm.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}
