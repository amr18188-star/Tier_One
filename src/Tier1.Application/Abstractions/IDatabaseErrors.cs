namespace Tier1.Application.Abstractions;

/// <summary>What a database error means for the request, independent of the database engine.</summary>
public enum DatabaseErrorKind
{
    /// <summary>Not a database error, or one the request did not cause (a server error).</summary>
    None,

    /// <summary>A unique constraint: two requests for the same thing at the same moment.</summary>
    Duplicate,

    /// <summary>A text longer than its column.</summary>
    TooLong,

    /// <summary>A value a check constraint or a type rejects.</summary>
    InvalidValue,

    /// <summary>A reference to a row that is gone, or a row still referenced.</summary>
    InUse,
}

/// <summary>A classified database error, with the constraint name when there is one.</summary>
public sealed record DatabaseError(DatabaseErrorKind Kind, string? Constraint = null);

/// <summary>BE-14: turns an engine-specific exception into what it means, so the web layer does not read PostgreSQL error codes.</summary>
public interface IDatabaseErrors
{
    DatabaseError Classify(Exception exception);
}
