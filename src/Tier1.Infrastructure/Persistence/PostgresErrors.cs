using Npgsql;
using Tier1.Application.Abstractions;

namespace Tier1.Infrastructure.Persistence;

/// <summary>PostgreSQL error codes, from the exception itself (ExecuteUpdate) or inside DbUpdateException (SaveChanges).</summary>
internal sealed class PostgresErrors : IDatabaseErrors
{
    public DatabaseError Classify(Exception exception)
    {
        if ((exception as PostgresException ?? exception.InnerException as PostgresException) is not { } pg) return new(DatabaseErrorKind.None);
        return pg.SqlState switch
        {
            "23505" => new(DatabaseErrorKind.Duplicate, pg.ConstraintName),
            "22001" => new(DatabaseErrorKind.TooLong),
            "23514" or "22P02" or "22003" => new(DatabaseErrorKind.InvalidValue, pg.ConstraintName),
            "23503" => new(DatabaseErrorKind.InUse, pg.ConstraintName),
            _ => new(DatabaseErrorKind.None),
        };
    }
}
