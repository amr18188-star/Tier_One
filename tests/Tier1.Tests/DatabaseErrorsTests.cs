using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tier1.Application.Abstractions;
using Tier1.Infrastructure.Persistence;

namespace Tier1.Tests;

/// <summary>BE-14: PostgreSQL error codes are read in Infrastructure; the web layer sees only what they mean.</summary>
public sealed class DatabaseErrorsTests
{
    private static readonly PostgresErrors Errors = new();

    private static PostgresException Pg(string state) => new("message", "ERROR", "ERROR", state);

    [Theory]
    [InlineData("23505", DatabaseErrorKind.Duplicate)]
    [InlineData("22001", DatabaseErrorKind.TooLong)]
    [InlineData("23514", DatabaseErrorKind.InvalidValue)]
    [InlineData("22P02", DatabaseErrorKind.InvalidValue)]
    [InlineData("22003", DatabaseErrorKind.InvalidValue)]
    [InlineData("23503", DatabaseErrorKind.InUse)]
    [InlineData("40001", DatabaseErrorKind.None)]
    public void Each_code_has_its_meaning_from_SaveChanges_or_a_bulk_statement(string state, DatabaseErrorKind kind)
    {
        Assert.Equal(kind, Errors.Classify(new DbUpdateException("save", Pg(state))).Kind);
        Assert.Equal(kind, Errors.Classify(Pg(state)).Kind);
    }

    [Fact]
    public void Other_exceptions_are_not_database_errors() =>
        Assert.Equal(DatabaseErrorKind.None, Errors.Classify(new InvalidOperationException("x")).Kind);
}
