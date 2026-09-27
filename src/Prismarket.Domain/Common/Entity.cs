namespace Prismarket.Domain.Common;

/// <summary>Base class for all persisted entities with integer identity.</summary>
public abstract class Entity
{
    public int Id { get; set; }
}

/// <summary>Entity that tracks creation / modification timestamps (filled by an EF interceptor).</summary>
public abstract class AuditableEntity : Entity, IAuditable
{
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
}

/// <summary>Marker: changes of this entity are written to the audit log.</summary>
public interface ITrackChanges;
