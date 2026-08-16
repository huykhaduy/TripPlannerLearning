using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TripPlanner.Domain.Entities;
using TripPlanner.Infrastructure.Persistence;

// ---------------------------------------------------------------------------
// Why this exists
//
// TripRepository.GetDetailsAsync includes Days AND Items, which are both
// collections hanging off Trip. Without AsSplitQuery, EF LEFT JOINs each of them
// onto the trip row independently and the two join products multiply, so the row
// count is QUADRATIC IN THE ITEM COUNT.
//
// No unit test can show this: the suite runs on EF InMemory, which is not
// relational and silently ignores AsSplitQuery. That gap is exactly what this
// program fills, which is why it measures the real database rather than a
// convenient stand-in.
//
// Nothing is persisted. Every scenario seeds inside a transaction that is rolled
// back, so the benchmark can point at a normal development database without
// leaving rows behind. It needs the schema to already exist — run the API once
// (migrations apply on startup) if the database is empty.
//
//     docker compose up -d
//     dotnet run --project backend/tests/TripPlanner.QueryBenchmarks
//
// Connection string resolution: first CLI argument, else the
// ConnectionStrings__Postgres environment variable, else the docker-compose
// defaults below.
// ---------------------------------------------------------------------------

const int Iterations = 200;

var connectionString = args.FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
    ?? "Host=localhost;Port=5432;Database=tripplanner;Username=tripplanner;Password=tripplanner";

try
{
    _ = new NpgsqlConnectionStringBuilder(connectionString);
}
catch (ArgumentException ex)
{
    // Usually a stray flag: `dotnet run` forwards anything it does not recognise
    // (--nologo, for one) straight through to here as args[0].
    Console.Error.WriteLine($"Not a usable connection string: \"{connectionString}\"");
    Console.Error.WriteLine($"  {ex.Message}");
    Console.Error.WriteLine("Pass one as the only argument, or set ConnectionStrings__Postgres.");
    return 1;
}

// The two shapes under test. Mirrors TripRepository.GetDetailsAsync exactly — if
// that query changes, change this one with it.
static IQueryable<Trip> BuildQuery(ApplicationDbContext db, Guid tripId, Guid userId, bool split, bool limit = true)
{
    IQueryable<Trip> query = db.Trips.AsNoTracking();
    if (split)
    {
        query = query.AsSplitQuery();
    }

    query = query
        .Include(t => t.Days).ThenInclude(d => d.Items).ThenInclude(i => i.Destination)
        .Include(t => t.Items).ThenInclude(i => i.Destination)
        .Where(t => t.Id == tripId && t.UserId == userId);

    // FirstOrDefault cannot be combined with ToQueryString (it executes), so the
    // limit is expressed as Take(1).
    return limit ? query.Take(1) : query;
}

PrintGeneratedSql(connectionString);

await using var connection = new NpgsqlConnection(connectionString);
try
{
    await connection.OpenAsync();
}
catch (NpgsqlException ex)
{
    Console.WriteLine("Could not reach Postgres, so only the SQL shape above was reported.");
    Console.WriteLine($"  {ex.Message}");
    Console.WriteLine();
    Console.WriteLine("Start it with `docker compose up -d`, or pass a connection string as the");
    Console.WriteLine("first argument. The row counts and timings below need a real database —");
    Console.WriteLine("EF InMemory ignores AsSplitQuery, which would make the whole run meaningless.");
    return 1;
}

Console.WriteLine($"{"days",6} {"items",6} | {"rows on wire",12} | {"single ms",10} {"split ms",9} {"faster",8} | same graph?");
Console.WriteLine(new string('-', 82));

// Held at 14 days and swept across item counts, because the item count is what
// drives the join product. The small sizes are not padding: splitting costs three
// round trips instead of one, so there is a break-even point and the sweep is how
// you find it.
foreach (var (dayCount, itemCount) in new[] { (14, 0), (14, 5), (14, 10), (14, 15), (14, 20), (14, 30), (14, 60), (30, 60) })
{
    // Everything this scenario writes dies with the transaction.
    await using var transaction = await connection.BeginTransactionAsync();

    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connection)
        .Options;

    ApplicationDbContext OpenContext()
    {
        var db = new ApplicationDbContext(options);
        db.Database.UseTransaction(transaction);
        return db;
    }

    var (tripId, userId) = Seed(OpenContext, dayCount, itemCount);

    var rows = CountJoinedRows(connection, transaction, OpenContext, tripId, userId);
    var single = MeasureMean(OpenContext, tripId, userId, split: false);
    var split = MeasureMean(OpenContext, tripId, userId, split: true);
    var sameGraph = ProducesSameGraph(OpenContext, tripId, userId);

    Console.WriteLine(
        $"{dayCount,6} {itemCount,6} | {rows,12} | {single,10:F2} {split,9:F2} {(single - split) / single * 100,7:F1}% | {sameGraph}");

    await transaction.RollbackAsync();
}

Console.WriteLine();
Console.WriteLine("Rows on the wire is (items + empty days) x items — quadratic in items, and");
Console.WriteLine("barely affected by the number of days. Nothing was committed.");
return 0;

// ---------------------------------------------------------------------------

static void PrintGeneratedSql(string connectionString)
{
    // No connection is opened: ToQueryString only needs the provider to build the
    // statement, so this half of the report works with the database down.
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connectionString)
        .Options;

    using var db = new ApplicationDbContext(options);
    var singleSql = BuildQuery(db, Guid.Empty, Guid.Empty, split: false).ToQueryString();
    var splitSql = BuildQuery(db, Guid.Empty, Guid.Empty, split: true).ToQueryString();

    Console.WriteLine("=== SQL the app sends (no connection opened) ===");
    Console.WriteLine($"single query : {singleSql.Split("JOIN").Length - 1} JOINs, {singleSql.Length} chars");
    Console.WriteLine($"split query  : {splitSql.Split("JOIN").Length - 1} JOINs in the root statement, {splitSql.Length} chars");
    Console.WriteLine("               (ToQueryString shows only the first of the split statements)");
    Console.WriteLine();
}

static (Guid TripId, Guid UserId) Seed(Func<ApplicationDbContext> openContext, int dayCount, int itemCount)
{
    using var db = openContext();

    // Fresh ids per scenario: the email and ProviderId unique indexes are real
    // here, and the target database may already hold data.
    var user = new User
    {
        Email = $"bench-{Guid.NewGuid():N}@example.com",
        PasswordHash = "not-a-real-hash",
        IsEmailVerified = true,
    };
    db.Users.Add(user);

    var trip = new Trip { Name = "Benchmark trip", UserId = user.Id };
    db.Trips.Add(trip);

    var start = new DateOnly(2026, 8, 1);
    var days = new List<ItineraryDay>();
    for (var d = 0; d < dayCount; d++)
    {
        var day = new ItineraryDay { TripId = trip.Id, Date = start.AddDays(d), DayNumber = d + 1 };
        days.Add(day);
        db.Add(day);
    }

    // Spread items evenly across the days, the way a real itinerary looks.
    for (var i = 0; i < itemCount; i++)
    {
        var destination = new Destination
        {
            ProviderId = $"bench-{Guid.NewGuid():N}",
            Name = $"Place {i}",
            Latitude = 10 + i,
            Longitude = 100 + i,
        };
        db.Add(destination);
        db.Add(new ItineraryItem
        {
            TripId = trip.Id,
            DestinationId = destination.Id,
            ItineraryDayId = days[i % dayCount].Id,
            SortOrder = i / dayCount,
        });
    }

    db.SaveChanges();
    return (trip.Id, user.Id);
}

// Wraps EF's own single-query SQL in a COUNT, so the number is what the database
// really hands back rather than an estimate.
static long CountJoinedRows(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Func<ApplicationDbContext> openContext,
    Guid tripId,
    Guid userId)
{
    using var db = openContext();
    var sql = BuildQuery(db, tripId, userId, split: false, limit: false).ToQueryString();

    // ToQueryString prefixes the parameter values as "--" comment lines.
    var body = string.Join('\n', sql.Split('\n').Where(line => !line.TrimStart().StartsWith("--")));

    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = $"SELECT COUNT(*) FROM (\n{body}\n) AS joined"; // Postgres requires the alias
    command.Parameters.AddWithValue("tripId", tripId);
    command.Parameters.AddWithValue("userId", userId);
    return (long)command.ExecuteScalar()!;
}

static double MeasureMean(Func<ApplicationDbContext> openContext, Guid tripId, Guid userId, bool split)
{
    // Warm up first: the first execution pays for query compilation, which would
    // otherwise land entirely on whichever shape happens to run first.
    for (var i = 0; i < 3; i++)
    {
        using var warmup = openContext();
        _ = BuildQuery(warmup, tripId, userId, split).FirstOrDefault();
    }

    var stopwatch = Stopwatch.StartNew();
    for (var i = 0; i < Iterations; i++)
    {
        // A fresh context per iteration, like a real scoped request.
        using var db = openContext();
        _ = BuildQuery(db, tripId, userId, split).FirstOrDefault();
    }

    stopwatch.Stop();
    return stopwatch.Elapsed.TotalMilliseconds / Iterations;
}

// A faster query returning a different graph would be worthless, so the two
// shapes are compared before any timing is believed.
static bool ProducesSameGraph(Func<ApplicationDbContext> openContext, Guid tripId, Guid userId)
{
    using var db = openContext();
    var single = BuildQuery(db, tripId, userId, split: false).FirstOrDefault()!;
    var split = BuildQuery(db, tripId, userId, split: true).FirstOrDefault()!;

    return single.Days.Count == split.Days.Count
        && single.Items.Count == split.Items.Count
        && single.Days.Sum(d => d.Items.Count) == split.Days.Sum(d => d.Items.Count);
}
