using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PaymentOrchestratorAssessment.Infrastructure.DbContexts;

namespace PaymentOrchestratorAssessment.Tests.TestDoubles;

/// <summary>
/// A real SQLite database held in memory, one per test.
/// </summary>
/// <remarks>
/// Deliberately not EF Core's InMemory provider. InMemory is not a relational
/// database: it ignores unique indexes, so the idempotency tests here would pass
/// against it while failing in production, and it cannot execute
/// <c>ExecuteUpdateAsync</c> at all. SQLite in-memory mode gives real relational
/// behaviour at roughly the same speed.
///
/// The connection must stay open for the lifetime of the test: closing the last
/// connection to a SQLite in-memory database destroys it.
/// </remarks>
public sealed class SqliteDatabaseFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteDatabaseFixture()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        using var setup = NewContext();
        setup.Database.EnsureCreated();
    }

    /// <summary>
    /// A fresh context over the same database. Using a new context per logical
    /// operation stops the change tracker from hiding bugs that only show up when
    /// data is genuinely re-read.
    /// </summary>
    public PaymentsDbContext NewContext() => new(
        new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlite(_connection)
            .Options);

    public void Dispose() => _connection.Dispose();
}
