namespace TripPlanner.Domain.Common;

/// <summary>
/// Base class for all persisted entities. Gives every entity a strongly-typed
/// <see cref="Id"/> and audit timestamps so we do not repeat them everywhere.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
