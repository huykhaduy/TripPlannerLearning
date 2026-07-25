# Repository Pattern Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace direct `IApplicationDbContext`/`DbSet<T>` access in `AuthService`, `TripService`, and `DestinationService` with a generic `IRepository<T>` + `IUnitOfWork` pair plus per-entity repository interfaces (`IUserRepository`, `ITripRepository`, `IDestinationRepository`), then retire `IApplicationDbContext` entirely.

**Architecture:** Application defines EF-Core-agnostic repository/unit-of-work interfaces; Infrastructure implements them over the existing `ApplicationDbContext`. Services swap their `IApplicationDbContext` dependency for the specific repositories they need. A new `ConcurrencyException` (Application-layer) replaces catching EF Core's `DbUpdateException` directly, so Application ends up with zero `Microsoft.EntityFrameworkCore` references.

**Tech Stack:** .NET 10, EF Core 10 (SQLite/Postgres in production, InMemory in tests), xUnit + Moq.

**Full design spec:** [docs/superpowers/specs/2026-07-25-repository-pattern-design.md](../specs/2026-07-25-repository-pattern-design.md)

## Global Constraints

- No behavior change: every existing test in `AuthServiceTests`, `TripServiceTests`, `DestinationServiceTests` must still pass after each task, and no controller/DTO/validator changes.
- Application layer must end this plan with **zero** references to `Microsoft.EntityFrameworkCore` (interfaces AND catch clauses) — verified in Task 5.
- Repository methods only stage changes (`Add`/`Remove`); only `IUnitOfWork.SaveChangesAsync` persists (see spec's Unit of Work section).
- Non-trivial queries get a named method on a specific repository interface — no generic `IQueryable` escape hatch (spec's "Query design" decision).
- `ItineraryDay`/`ItineraryItem` use the generic `IRepository<T>` only — no custom interfaces for them; queries that need `Trip`-level ownership context live on `ITripRepository` instead (Trip is the aggregate root).
- Run tests from the `backend/` directory: `dotnet test --filter "FullyQualifiedName~<Name>"`.

---

### Task 1: Generic `IRepository<T>` + `IUnitOfWork` + `ConcurrencyException`

**Files:**
- Create: `backend/src/TripPlanner.Application/Common/Interfaces/IRepository.cs`
- Create: `backend/src/TripPlanner.Application/Common/Interfaces/IUnitOfWork.cs`
- Create: `backend/src/TripPlanner.Application/Common/Exceptions/ConcurrencyException.cs`
- Create: `backend/src/TripPlanner.Infrastructure/Persistence/Repositories/Repository.cs`
- Create: `backend/src/TripPlanner.Infrastructure/Persistence/UnitOfWork.cs`
- Modify: `backend/src/TripPlanner.Infrastructure/DependencyInjection.cs:61-80` (`AddPersistence`)
- Test: `backend/tests/TripPlanner.Application.Tests/Repositories/RepositoryTests.cs`

**Interfaces:**
- Produces: `IRepository<T>.GetByIdAsync(Guid, CancellationToken)`, `.GetAllAsync(CancellationToken)`, `.Add(T)`, `.Remove(T)`; `IUnitOfWork.SaveChangesAsync(CancellationToken)`; `ConcurrencyException(string, Exception)`; `Repository<T>` (base class with `protected ApplicationDbContext Context` and `protected DbSet<T> Set`); `UnitOfWork : IUnitOfWork`. All consumed by Tasks 2-4.

- [ ] **Step 1: Write the failing test file**

```csharp
// backend/tests/TripPlanner.Application.Tests/Repositories/RepositoryTests.cs
using Microsoft.EntityFrameworkCore;
using TripPlanner.Domain.Entities;
using TripPlanner.Infrastructure.Persistence;
using TripPlanner.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TripPlanner.Application.Tests.Repositories;

/// <summary>
/// Unit tests for the generic Repository&lt;T&gt; + UnitOfWork pair, using
/// Destination as a stand-in entity (any BaseEntity would do). Entity-specific
/// repositories are tested alongside the services that use them (Tasks 2-4).
/// </summary>
public class RepositoryTests
{
    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Add_ThenSaveChanges_PersistsTheEntity()
    {
        using var db = CreateDb();
        var repository = new Repository<Destination>(db);
        var unitOfWork = new UnitOfWork(db);

        repository.Add(new Destination { ProviderId = "geo-1", Name = "Golden Bridge" });
        await unitOfWork.SaveChangesAsync();

        Assert.Equal(1, await db.Destinations.CountAsync());
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsThePersistedEntity()
    {
        using var db = CreateDb();
        var repository = new Repository<Destination>(db);
        var unitOfWork = new UnitOfWork(db);
        var destination = new Destination { ProviderId = "geo-1", Name = "Golden Bridge" };
        repository.Add(destination);
        await unitOfWork.SaveChangesAsync();

        var found = await repository.GetByIdAsync(destination.Id);

        Assert.NotNull(found);
        Assert.Equal("Golden Bridge", found!.Name);
    }

    [Fact]
    public async Task GetByIdAsync_WithUnknownId_ReturnsNull()
    {
        using var db = CreateDb();
        var repository = new Repository<Destination>(db);

        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEveryPersistedEntity()
    {
        using var db = CreateDb();
        var repository = new Repository<Destination>(db);
        var unitOfWork = new UnitOfWork(db);
        repository.Add(new Destination { ProviderId = "geo-1", Name = "Golden Bridge" });
        repository.Add(new Destination { ProviderId = "geo-2", Name = "Marble Mountains" });
        await unitOfWork.SaveChangesAsync();

        Assert.Equal(2, (await repository.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Remove_ThenSaveChanges_DeletesTheEntity()
    {
        using var db = CreateDb();
        var repository = new Repository<Destination>(db);
        var unitOfWork = new UnitOfWork(db);
        var destination = new Destination { ProviderId = "geo-1", Name = "Golden Bridge" };
        repository.Add(destination);
        await unitOfWork.SaveChangesAsync();

        repository.Remove(destination);
        await unitOfWork.SaveChangesAsync();

        Assert.Equal(0, await db.Destinations.CountAsync());
    }
}
```

Note: there is deliberately **no** test here for `UnitOfWork`'s `DbUpdateException` → `ConcurrencyException` translation. A throwaway probe confirmed EF Core's InMemory provider throws a raw `System.ArgumentException` on a cross-context primary-key collision, not `DbUpdateException` — it doesn't enforce non-key unique indexes at all (per [CLAUDE.md](../../../CLAUDE.md)'s "Concurrency via unique index + catch/retry" note). This mirrors the existing `catch (DbUpdateException)` blocks in `TripService.cs`, which today have no automated test either — reasoned about against the real SQL provider only.

- [ ] **Step 2: Run the test to verify it fails to compile**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~RepositoryTests"
```
Expected: build error — `Repository<>`, `UnitOfWork`, `IRepository<>` do not exist yet.

- [ ] **Step 3: Create the Application-layer interfaces and exception**

```csharp
// backend/src/TripPlanner.Application/Common/Interfaces/IRepository.cs
using TripPlanner.Domain.Common;

namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Generic data-access abstraction over a single entity type. Implementations
/// live in Infrastructure; the Application layer depends only on this
/// interface, never on EF Core directly.
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<List<T>> GetAllAsync(CancellationToken cancellationToken = default);

    void Add(T entity);

    void Remove(T entity);
}
```

```csharp
// backend/src/TripPlanner.Application/Common/Interfaces/IUnitOfWork.cs
namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Commits changes staged by one or more repositories in a single save, so
/// multi-entity operations (e.g. removing several ItineraryDay rows and
/// adding others when a trip's date range changes) persist atomically.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

```csharp
// backend/src/TripPlanner.Application/Common/Exceptions/ConcurrencyException.cs
namespace TripPlanner.Application.Common.Exceptions;

/// <summary>
/// Thrown by <see cref="Interfaces.IUnitOfWork.SaveChangesAsync"/> when a save
/// fails because of a concurrent write (e.g. a unique-index violation). This
/// is NOT one of the exceptions ExceptionHandlingMiddleware maps to an HTTP
/// status — callers must catch it and translate it into a feature-specific
/// exception (usually ConflictException) with a message appropriate to what
/// they were trying to do.
/// </summary>
public class ConcurrencyException : Exception
{
    public ConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
```

- [ ] **Step 4: Create the Infrastructure implementations**

```csharp
// backend/src/TripPlanner.Infrastructure/Persistence/Repositories/Repository.cs
using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Domain.Common;

namespace TripPlanner.Infrastructure.Persistence.Repositories;

/// <summary>
/// Generic EF Core-backed <see cref="IRepository{T}"/>. Entity-specific
/// repositories (UserRepository, TripRepository, DestinationRepository)
/// derive from this for their extra query methods.
/// </summary>
public class Repository<T> : IRepository<T> where T : BaseEntity
{
    protected readonly ApplicationDbContext Context;
    protected readonly DbSet<T> Set;

    public Repository(ApplicationDbContext context)
    {
        Context = context;
        Set = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Set.FindAsync(new object?[] { id }, cancellationToken);

    public async Task<List<T>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Set.ToListAsync(cancellationToken);

    public void Add(T entity) => Set.Add(entity);

    public void Remove(T entity) => Set.Remove(entity);
}
```

```csharp
// backend/src/TripPlanner.Infrastructure/Persistence/UnitOfWork.cs
using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.Infrastructure.Persistence;

/// <summary>
/// Wraps <see cref="ApplicationDbContext.SaveChangesAsync"/> (which also
/// stamps audit timestamps — see that override) and translates a unique-index
/// violation into <see cref="ConcurrencyException"/> so the Application layer
/// never needs to reference Microsoft.EntityFrameworkCore's DbUpdateException.
///
/// Like the DbUpdateException catches this replaces (see CLAUDE.md's
/// "Concurrency via unique index + catch/retry" note), this path is NOT
/// exercised by the EF Core InMemory provider used in tests — InMemory throws
/// a raw ArgumentException for a primary-key collision, not DbUpdateException,
/// and does not enforce non-key unique indexes at all. It must be reasoned
/// about directly against the real SQL provider (SQLite/Postgres).
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new ConcurrencyException("A concurrent write conflicted with this save.", ex);
        }
    }
}
```

- [ ] **Step 5: Register the generic repository and unit of work in DI**

In `backend/src/TripPlanner.Infrastructure/DependencyInjection.cs`, add a using and one line at the end of `AddPersistence` (after the existing `services.AddScoped<IApplicationDbContext>(...)` line — that line is removed later, in Task 5):

```csharp
using TripPlanner.Infrastructure.Persistence.Repositories; // add near the other usings
```

```csharp
        // Expose the context to the Application layer through its interface.
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // Generic repository + unit of work (open generic covers IRepository<ItineraryDay>,
        // IRepository<ItineraryItem> directly; entity-specific repositories are
        // registered individually as they're introduced in later tasks).
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
    }
```

- [ ] **Step 6: Run the test to verify it passes**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~RepositoryTests"
```
Expected: 5 passed.

- [ ] **Step 7: Commit**

```bash
git add backend/src/TripPlanner.Application/Common/Interfaces/IRepository.cs backend/src/TripPlanner.Application/Common/Interfaces/IUnitOfWork.cs backend/src/TripPlanner.Application/Common/Exceptions/ConcurrencyException.cs backend/src/TripPlanner.Infrastructure/Persistence/Repositories/Repository.cs backend/src/TripPlanner.Infrastructure/Persistence/UnitOfWork.cs backend/src/TripPlanner.Infrastructure/DependencyInjection.cs backend/tests/TripPlanner.Application.Tests/Repositories/RepositoryTests.cs
git commit -m "Add generic IRepository<T> and IUnitOfWork abstractions"
```

---

### Task 2: `IUserRepository` + `AuthService` refactor

**Files:**
- Create: `backend/src/TripPlanner.Application/Common/Interfaces/IUserRepository.cs`
- Create: `backend/src/TripPlanner.Infrastructure/Persistence/Repositories/UserRepository.cs`
- Modify: `backend/src/TripPlanner.Infrastructure/DependencyInjection.cs` (register `IUserRepository`)
- Modify: `backend/src/TripPlanner.Application/Features/Auth/AuthService.cs` (full file, shown below)
- Modify: `backend/tests/TripPlanner.Application.Tests/Auth/AuthServiceTests.cs:24-62` (`CreateSut`)

**Interfaces:**
- Consumes: `IRepository<T>`, `IUnitOfWork` (Task 1).
- Produces: `IUserRepository.GetByEmailAsync(string, CancellationToken)`, `.ExistsByEmailAsync(string, CancellationToken)` (plus inherited `GetByIdAsync`/`Add`). Consumed only by `AuthService` in this task.

- [ ] **Step 1: Run the existing Auth tests to confirm the baseline is green**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~AuthServiceTests"
```
Expected: all passing (12 tests) — this is the safety net for the refactor below.

- [ ] **Step 2: Create `IUserRepository` and `UserRepository`**

```csharp
// backend/src/TripPlanner.Application/Common/Interfaces/IUserRepository.cs
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Common.Interfaces;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
}
```

```csharp
// backend/src/TripPlanner.Infrastructure/Persistence/Repositories/UserRepository.cs
using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Repositories;

public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        await Set.AnyAsync(u => u.Email == email, cancellationToken);
}
```

- [ ] **Step 3: Register `IUserRepository` in DI**

In `backend/src/TripPlanner.Infrastructure/DependencyInjection.cs`, add to `AddPersistence` right after the `IUnitOfWork` line from Task 1:

```csharp
        services.AddScoped<IUserRepository, UserRepository>();
```

- [ ] **Step 4: Refactor `AuthService.cs` to use `IUserRepository` + `IUnitOfWork`**

Replace the full file with:

```csharp
using System.Net.Mail;
using System.Net.Sockets;
using FluentValidation;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Auth.Dtos;
using TripPlanner.Domain.Entities;
using ValidationException = TripPlanner.Application.Common.Exceptions.ValidationException;

namespace TripPlanner.Application.Features.Auth;

/// <summary>
/// ============================================================================
/// REFERENCE IMPLEMENTATION — read this carefully.
/// ============================================================================
/// This is the one feature slice that is fully built out. It demonstrates the
/// shape every other use-case in this project should follow:
///
///   * depend on INTERFACES from the Application layer (IUserRepository,
///     IUnitOfWork, IPasswordHasher, IJwtTokenGenerator) — never on
///     EF/Infrastructure types;
///   * validate input and enforce business rules, throwing the Application
///     exceptions (Validation/Conflict/Unauthorized) that the API maps to HTTP;
///   * map entities to DTOs so we never leak the password hash to the client.
///
/// Use it as the blueprint for TripService and DestinationService.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly IEmailSender _emailSender;
    private readonly IAppUrlProvider _appUrls;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<RegisterRequest> _registerValidator;

    public AuthService(
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator tokenGenerator,
        IEmailSender emailSender,
        IAppUrlProvider appUrls,
        ICurrentUserService currentUser,
        IValidator<RegisterRequest> registerValidator)
    {
        _users = users;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _emailSender = emailSender;
        _appUrls = appUrls;
        _currentUser = currentUser;
        _registerValidator = registerValidator;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        // Input rules live in RegisterRequestValidator (Feature 4 / US1);
        // failures surface as our ValidationException -> HTTP 400.
        await _registerValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var email = NormalizeEmail(request.Email);

        // Business rule: email must be unique.
        var emailTaken = await _users.ExistsByEmailAsync(email, cancellationToken);
        if (emailTaken)
        {
            // Generic message — do not reveal whether the email exists (avoids
            // account enumeration, Feature 4 / US1).
            throw new ConflictException("Unable to register with the provided details.");
        }

        var user = new User
        {
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
            IsEmailVerified = false, // F4/US2 — flipped by VerifyEmailAsync once the emailed link is opened.
        };

        _users.Add(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Registration still succeeds even if the email itself can't be sent
        // (SMTP down/misconfigured) — the user can retry via "resend
        // verification email"; a mail outage shouldn't block sign-up.
        await SendVerificationEmailAsync(user, cancellationToken);

        return BuildAuthResponse(user);
    }

    public async Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        var userId = _tokenGenerator.ValidateEmailVerificationToken(token)
            ?? throw new ValidationException("This verification link is invalid or has expired.");

        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        if (!user.IsEmailVerified)
        {
            user.IsEmailVerified = true;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ResendVerificationEmailAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetRequiredUserId();

        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        if (user.IsEmailVerified)
        {
            return; // nothing to resend
        }

        await SendVerificationEmailAsync(user, cancellationToken);
    }

    private async Task SendVerificationEmailAsync(User user, CancellationToken cancellationToken)
    {
        var token = _tokenGenerator.GenerateEmailVerificationToken(user);
        var link = $"{_appUrls.FrontendBaseUrl}/verify-email?token={Uri.EscapeDataString(token)}";
        var greetingName = user.DisplayName is null ? "" : $" {user.DisplayName}";

        try
        {
            await _emailSender.SendAsync(
                user.Email,
                "Verify your TripPlanner email",
                $"<p>Hi{greetingName},</p>"
                    + "<p>Click below to verify your email and activate your account:</p>"
                    + $"<p><a href=\"{link}\">{link}</a></p>"
                    + "<p>This link expires in 24 hours.</p>",
                cancellationToken);
        }
        catch (Exception ex) when (IsTransientEmailFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            // SMTP down, misconfigured, or the recipient was rejected — the
            // account still exists; resending is a separate, retryable step.
        }
    }

    private static bool IsTransientEmailFailure(Exception ex) =>
        ex is SmtpException or SocketException; // SmtpFailedRecipientException derives from SmtpException

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(request.Email);

        var user = await _users.GetByEmailAsync(email, cancellationToken);

        // Verify even when the user is missing? We short-circuit here for clarity.
        // The error is deliberately the same for "no such user" and "wrong password".
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        return BuildAuthResponse(user);
    }

    private AuthResponse BuildAuthResponse(User user)
    {
        var (token, expiresAt) = _tokenGenerator.GenerateToken(user);
        return new AuthResponse(token, expiresAt, user.ToDto());
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
```

- [ ] **Step 5: Update `AuthServiceTests.CreateSut` to build real repositories**

In `backend/tests/TripPlanner.Application.Tests/Auth/AuthServiceTests.cs`, add `using TripPlanner.Infrastructure.Persistence.Repositories;` to the usings, then replace the `CreateSut` method (lines 29-62) with:

```csharp
    private static AuthService CreateSut(ApplicationDbContext db, Guid? currentUserId = null, Mock<IEmailSender>? emailSender = null)
    {
        var users = new UserRepository(db);
        var unitOfWork = new UnitOfWork(db);

        // Real BCrypt hasher (cheap enough for tests); fake token generator.
        var hasher = new BCryptPasswordHasher();

        var tokenGenerator = new Mock<IJwtTokenGenerator>();
        tokenGenerator
            .Setup(t => t.GenerateToken(It.IsAny<User>()))
            .Returns(("fake-jwt", DateTimeOffset.UtcNow.AddHours(1)));
        // Deterministic fake "token": round-trips back to the same user id via
        // ValidateEmailVerificationToken; any other string is "invalid".
        tokenGenerator
            .Setup(t => t.GenerateEmailVerificationToken(It.IsAny<User>()))
            .Returns<User>(u => $"verify-token-for-{u.Id}");
        tokenGenerator
            .Setup(t => t.ValidateEmailVerificationToken(It.IsAny<string>()))
            .Returns<string>(token =>
                token.StartsWith("verify-token-for-", StringComparison.Ordinal)
                    ? Guid.Parse(token["verify-token-for-".Length..])
                    : null);

        var email = emailSender ?? new Mock<IEmailSender>();

        var appUrls = new Mock<IAppUrlProvider>();
        appUrls.Setup(p => p.FrontendBaseUrl).Returns("http://localhost:5173");

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.UserId).Returns(currentUserId);

        // Real validator — it's pure logic, so mocking it would only hide bugs.
        return new AuthService(
            users, unitOfWork, hasher, tokenGenerator.Object, email.Object, appUrls.Object, currentUser.Object,
            new RegisterRequestValidator());
    }
```

- [ ] **Step 6: Run the Auth tests to verify they still pass**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~AuthServiceTests"
```
Expected: all passing (12 tests), unchanged from Step 1.

- [ ] **Step 7: Commit**

```bash
git add backend/src/TripPlanner.Application/Common/Interfaces/IUserRepository.cs backend/src/TripPlanner.Infrastructure/Persistence/Repositories/UserRepository.cs backend/src/TripPlanner.Infrastructure/DependencyInjection.cs backend/src/TripPlanner.Application/Features/Auth/AuthService.cs backend/tests/TripPlanner.Application.Tests/Auth/AuthServiceTests.cs
git commit -m "Route AuthService through IUserRepository and IUnitOfWork"
```

---

### Task 3: `IDestinationRepository` + `DestinationService` refactor

**Files:**
- Create: `backend/src/TripPlanner.Application/Common/Interfaces/IDestinationRepository.cs`
- Create: `backend/src/TripPlanner.Infrastructure/Persistence/Repositories/DestinationRepository.cs`
- Modify: `backend/src/TripPlanner.Infrastructure/DependencyInjection.cs` (register `IDestinationRepository`)
- Modify: `backend/src/TripPlanner.Application/Features/Destinations/DestinationService.cs:1-83` (imports, class doc, field, constructor) and `:341-345` (`GetDetailsAsync`'s DB fallback)
- Modify: `backend/tests/TripPlanner.Application.Tests/Destinations/DestinationServiceTests.cs:78-86` (`CreateSut`)

**Interfaces:**
- Consumes: `IRepository<T>` (Task 1).
- Produces: `IDestinationRepository.GetByProviderIdAsync` (tracked — for `TripService`'s upsert path in Task 4), `.GetByProviderIdReadOnlyAsync` (`AsNoTracking` — for `DestinationService`'s read-only cache fallback).

- [ ] **Step 1: Run the existing Destination tests to confirm the baseline is green**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~DestinationServiceTests"
```
Expected: all passing (29 tests) — this is the safety net for the refactor below.

- [ ] **Step 2: Create `IDestinationRepository` and `DestinationRepository`**

```csharp
// backend/src/TripPlanner.Application/Common/Interfaces/IDestinationRepository.cs
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Common.Interfaces;

public interface IDestinationRepository : IRepository<Destination>
{
    /// <summary>Tracked fetch — used before a possible Add (see TripService.GetOrCreateDestinationAsync).</summary>
    Task<Destination?> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default);

    /// <summary>Read-only fetch — used by DestinationService's cache fallback, which never mutates the row.</summary>
    Task<Destination?> GetByProviderIdReadOnlyAsync(string providerId, CancellationToken cancellationToken = default);
}
```

```csharp
// backend/src/TripPlanner.Infrastructure/Persistence/Repositories/DestinationRepository.cs
using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Repositories;

public class DestinationRepository : Repository<Destination>, IDestinationRepository
{
    public DestinationRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<Destination?> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(d => d.ProviderId == providerId, cancellationToken);

    public async Task<Destination?> GetByProviderIdReadOnlyAsync(string providerId, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().FirstOrDefaultAsync(d => d.ProviderId == providerId, cancellationToken);
}
```

- [ ] **Step 3: Register `IDestinationRepository` in DI**

In `backend/src/TripPlanner.Infrastructure/DependencyInjection.cs`, add to `AddPersistence` after the `IUserRepository` line from Task 2:

```csharp
        services.AddScoped<IDestinationRepository, DestinationRepository>();
```

- [ ] **Step 4: Refactor `DestinationService.cs`**

Replace lines 1-12 (imports + namespace) with:

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Caching.Memory;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Destinations.Dtos;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Features.Destinations;
```

(This drops the `using Microsoft.EntityFrameworkCore;` line — `DestinationService` no longer calls any EF Core extension method directly.)

Replace the field/constructor block (lines 55-82) with:

```csharp
    private readonly IDestinationRepository _destinations;
    private readonly IDestinationProvider _provider;
    private readonly IImageSearchProvider _imageSearch;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;
    private readonly IValidator<SearchLocationsRequest> _searchValidator;
    private readonly IValidator<GetAttractionsRequest> _attractionsValidator;
    private readonly IValidator<GetDestinationDetailsRequest> _detailsValidator;

    public DestinationService(
        IDestinationRepository destinations,
        IDestinationProvider provider,
        IImageSearchProvider imageSearch,
        IMemoryCache cache,
        TimeProvider clock,
        IValidator<SearchLocationsRequest> searchValidator,
        IValidator<GetAttractionsRequest> attractionsValidator,
        IValidator<GetDestinationDetailsRequest> detailsValidator)
    {
        _destinations = destinations;
        _provider = provider;
        _imageSearch = imageSearch;
        _cache = cache;
        _clock = clock;
        _searchValidator = searchValidator;
        _attractionsValidator = attractionsValidator;
        _detailsValidator = detailsValidator;
    }
```

Replace the DB fallback lines (341-345) inside `GetDetailsAsync`:

```csharp
        // Saved-trip destinations must stay viewable even if the provider forgets
        // them (§8.4's snapshot rationale) — only a miss on BOTH sources is 404.
        var cached = await _destinations.GetByProviderIdReadOnlyAsync(providerId, cancellationToken);

        return cached?.ToDetailsDto() ?? throw new NotFoundException(nameof(Destination), providerId);
```

- [ ] **Step 5: Update `DestinationServiceTests.CreateSut`**

In `backend/tests/TripPlanner.Application.Tests/Destinations/DestinationServiceTests.cs`, add `using TripPlanner.Infrastructure.Persistence.Repositories;` to the usings, then replace the `CreateSut(ApplicationDbContext, ...)` overload (lines 78-86) with:

```csharp
    private static DestinationService CreateSut(
        ApplicationDbContext db,
        Mock<IDestinationProvider> provider,
        FakeClock? clock = null,
        Mock<IImageSearchProvider>? imageSearch = null) =>
        new(new DestinationRepository(db), provider.Object, (imageSearch ?? NoOpImageSearch()).Object,
            new MemoryCache(new MemoryCacheOptions()), clock ?? new FakeClock(),
            new SearchLocationsRequestValidator(), new GetAttractionsRequestValidator(),
            new GetDestinationDetailsRequestValidator());
```

- [ ] **Step 6: Run the Destination tests to verify they still pass**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~DestinationServiceTests"
```
Expected: all passing, unchanged from Step 1.

- [ ] **Step 7: Commit**

```bash
git add backend/src/TripPlanner.Application/Common/Interfaces/IDestinationRepository.cs backend/src/TripPlanner.Infrastructure/Persistence/Repositories/DestinationRepository.cs backend/src/TripPlanner.Infrastructure/DependencyInjection.cs backend/src/TripPlanner.Application/Features/Destinations/DestinationService.cs backend/tests/TripPlanner.Application.Tests/Destinations/DestinationServiceTests.cs
git commit -m "Route DestinationService through IDestinationRepository"
```

---

### Task 4: `ITripRepository` + `TripService` refactor

**Files:**
- Create: `backend/src/TripPlanner.Application/Common/Interfaces/ITripRepository.cs`
- Create: `backend/src/TripPlanner.Infrastructure/Persistence/Repositories/TripRepository.cs`
- Modify: `backend/src/TripPlanner.Infrastructure/DependencyInjection.cs` (register `ITripRepository`)
- Modify: `backend/src/TripPlanner.Application/Features/Trips/TripService.cs` (full file, shown below)
- Modify: `backend/tests/TripPlanner.Application.Tests/Trips/TripServiceTests.cs:32-42` (`CreateSut`)

**Interfaces:**
- Consumes: `IRepository<T>`, `IUnitOfWork` (Task 1), `IDestinationRepository` (Task 3), `TripMappings.ToSummaryRowExpression` / `TripSummaryRow` (existing, `Features/Trips/TripMappings.cs`).
- Produces: `ITripRepository.GetSummaryRowsForUserAsync`, `.GetDetailsAsync`, `.GetTrackedWithFullGraphAsync`, `.DayBelongsToTripAsync`, `.GetOwnedItemAsync` (plus inherited `Add`). Consumed only by `TripService` in this task.

Note on `GetTrackedWithFullGraphAsync`: it Includes `Days.Items` and `Items.Destination` — the same two Include chains the original `UpdateTripAsync` used. Because the query is tracked (not `AsNoTracking`), EF's change tracker identity-maps the same `ItineraryItem` instances across both navigation paths, so `Day.Items[].Destination` ends up populated too even though the `Days.Items` chain itself doesn't re-include `Destination` — exactly how the original code relied on it. Reusing this one method for `AddDestinationAsync` and `UpdateItineraryItemAsync` (which each only need a subset of this graph) loads slightly more data than their original bespoke queries did, but is behaviorally identical.

- [ ] **Step 1: Run the existing Trip tests to confirm the baseline is green**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~TripServiceTests"
```
Expected: all passing (27 tests) — this is the safety net for the refactor below.

- [ ] **Step 2: Create `ITripRepository` and `TripRepository`**

```csharp
// backend/src/TripPlanner.Application/Common/Interfaces/ITripRepository.cs
using TripPlanner.Application.Features.Trips;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Trip is the aggregate root for the trip-planning feature; queries that
/// enforce invariants across Trip/ItineraryDay/ItineraryItem (ownership,
/// "does this day belong to this trip") live here rather than on the generic
/// IRepository&lt;ItineraryDay&gt;/IRepository&lt;ItineraryItem&gt;.
/// </summary>
public interface ITripRepository : IRepository<Trip>
{
    /// <summary>Projected list rows for GetMyTripsAsync — see TripMappings.ToSummaryRowExpression.</summary>
    Task<List<TripSummaryRow>> GetSummaryRowsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Read-only, full graph (Days.Items.Destination + Items.Destination) — for GetTripAsync.</summary>
    Task<Trip?> GetDetailsAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Tracked, full graph (Days.Items + Items.Destination) — for any write path needing the trip's days and/or items.</summary>
    Task<Trip?> GetTrackedWithFullGraphAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Whether the given day belongs to the given trip (checked before scheduling a destination into it).</summary>
    Task<bool> DayBelongsToTripAsync(Guid itineraryDayId, Guid tripId, CancellationToken cancellationToken = default);

    /// <summary>The item, tracked, only if it belongs to a trip owned by userId (NFR 6) — used by RemoveDestinationAsync.</summary>
    Task<ItineraryItem?> GetOwnedItemAsync(Guid tripId, Guid itemId, Guid userId, CancellationToken cancellationToken = default);
}
```

```csharp
// backend/src/TripPlanner.Infrastructure/Persistence/Repositories/TripRepository.cs
using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Trips;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Repositories;

public class TripRepository : Repository<Trip>, ITripRepository
{
    public TripRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<List<TripSummaryRow>> GetSummaryRowsForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await Set
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(TripMappings.ToSummaryRowExpression)
            .ToListAsync(cancellationToken);

    public async Task<Trip?> GetDetailsAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
        await Set
            .AsNoTracking()
            .Include(t => t.Days).ThenInclude(d => d.Items).ThenInclude(i => i.Destination)
            .Include(t => t.Items).ThenInclude(i => i.Destination)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, cancellationToken);

    public async Task<Trip?> GetTrackedWithFullGraphAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
        await Set
            .Include(t => t.Days).ThenInclude(d => d.Items)
            .Include(t => t.Items).ThenInclude(i => i.Destination)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, cancellationToken);

    public async Task<bool> DayBelongsToTripAsync(Guid itineraryDayId, Guid tripId, CancellationToken cancellationToken = default) =>
        await Context.Set<ItineraryDay>()
            .AnyAsync(d => d.Id == itineraryDayId && d.TripId == tripId, cancellationToken);

    public async Task<ItineraryItem?> GetOwnedItemAsync(Guid tripId, Guid itemId, Guid userId, CancellationToken cancellationToken = default) =>
        await Context.Set<ItineraryItem>()
            .FirstOrDefaultAsync(i => i.Id == itemId && i.TripId == tripId && i.Trip!.UserId == userId, cancellationToken);
}
```

- [ ] **Step 3: Register `ITripRepository` in DI**

In `backend/src/TripPlanner.Infrastructure/DependencyInjection.cs`, add to `AddPersistence` after the `IDestinationRepository` line from Task 3:

```csharp
        services.AddScoped<ITripRepository, TripRepository>();
```

- [ ] **Step 4: Refactor `TripService.cs`**

Replace the full file with:

```csharp
using System.Text.Json;
using FluentValidation;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Destinations;
using TripPlanner.Application.Features.Trips.Dtos;
using TripPlanner.Domain.Entities;
using ValidationException = TripPlanner.Application.Common.Exceptions.ValidationException;

namespace TripPlanner.Application.Features.Trips;

/// <summary>
/// STUB — students implement this (Feature 3: Trip Planner).
///
/// Use <see cref="AuthService"/> as your reference for structure. Key points:
///   * read the owner from <see cref="ICurrentUserService.UserId"/> and filter
///     every query by it — never trust a trip id alone (NFR 6 / authorization);
///   * throw <see cref="Common.Exceptions.NotFoundException"/> when a trip the
///     current user owns does not exist;
///   * when dates change (US2) regenerate <c>ItineraryDay</c> rows for the new
///     range and call <c>Trip.SetDates</c> to enforce start ≤ end.
/// </summary>
public class TripService : ITripService
{
    private readonly ITripRepository _trips;
    private readonly IRepository<ItineraryDay> _itineraryDays;
    private readonly IRepository<ItineraryItem> _itineraryItems;
    private readonly IDestinationRepository _destinations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IDestinationProvider _destinationProvider;
    private readonly IImageSearchProvider _imageSearch;
    private readonly IValidator<CreateTripRequest> _createTripValidator;
    private readonly IValidator<UpdateTripRequest> _updateTripValidator;
    private readonly IValidator<AddDestinationRequest> _addDestinationValidator;
    private readonly IValidator<UpdateItineraryItemRequest> _updateItemValidator;

    public TripService(
        ITripRepository trips,
        IRepository<ItineraryDay> itineraryDays,
        IRepository<ItineraryItem> itineraryItems,
        IDestinationRepository destinations,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IDestinationProvider destinationProvider,
        IImageSearchProvider imageSearch,
        IValidator<CreateTripRequest> createTripValidator,
        IValidator<UpdateTripRequest> updateTripValidator,
        IValidator<AddDestinationRequest> addDestinationValidator,
        IValidator<UpdateItineraryItemRequest> updateItemValidator)
    {
        _trips = trips;
        _itineraryDays = itineraryDays;
        _itineraryItems = itineraryItems;
        _destinations = destinations;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _destinationProvider = destinationProvider;
        _imageSearch = imageSearch;
        _createTripValidator = createTripValidator;
        _updateTripValidator = updateTripValidator;
        _addDestinationValidator = addDestinationValidator;
        _updateItemValidator = updateItemValidator;
    }

    public async Task<IReadOnlyList<TripSummaryDto>> GetMyTripsAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetRequiredUserId();

        var rows = await _trips.GetSummaryRowsForUserAsync(userId, cancellationToken);

        // SQLite cannot ORDER BY a DateTimeOffset column, so the CreatedAt sort
        // happens in memory — a user's trip list is small.
        return rows
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.Summary)
            .ToList();
    }

    public async Task<TripDetailDto> GetTripAsync(Guid tripId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetRequiredUserId();

        // NFR 6: filtering by owner AND id means "someone else's trip" and
        // "no such trip" are indistinguishable to the caller — both 404.
        var trip = await _trips.GetDetailsAsync(tripId, userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Trip), tripId);

        return trip.ToDetailDto();
    }

    public async Task<TripSummaryDto> CreateTripAsync(CreateTripRequest request, CancellationToken cancellationToken = default)
    {
        // F3/US1 — name is required (CreateTripRequestValidator -> HTTP 400).
        await _createTripValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var trip = new Trip
        {
            Name = request.Name.Trim(),
            // NFR 6: the trip belongs to the caller; throws 401 when anonymous.
            UserId = _currentUser.GetRequiredUserId(),
        };

        _trips.Add(trip);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return trip.ToSummaryDto();
    }

    public async Task<TripDetailDto> UpdateTripAsync(Guid tripId, UpdateTripRequest request, CancellationToken cancellationToken = default)
    {
        await _updateTripValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var userId = _currentUser.GetRequiredUserId();

        var trip = await _trips.GetTrackedWithFullGraphAsync(tripId, userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Trip), tripId);

        trip.Name = request.Name.Trim();
        // Domain rule: start ≤ end (throws DomainException -> 400).
        trip.SetDates(request.StartDate, request.EndDate);

        RegenerateDays(trip);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return trip.ToDetailDto();
    }

    /// <summary>
    /// F3/US2 day regeneration (spec §11.1): keep days still inside the date
    /// range (preserving their scheduled items), delete days that fell out of
    /// it (their items return to Saved Places), create days for new dates,
    /// then renumber chronologically.
    /// </summary>
    private void RegenerateDays(Trip trip)
    {
        var targetDates = new HashSet<DateOnly>();
        if (trip.StartDate is { } start && trip.EndDate is { } end)
        {
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                targetDates.Add(date);
            }
        }

        foreach (var day in trip.Days.Where(d => !targetDates.Contains(d.Date)).ToList())
        {
            // Mirror the DB's SetNull cascade in memory so the DTO we return
            // already shows these items back in Saved Places.
            foreach (var item in day.Items)
            {
                item.ItineraryDayId = null;
            }

            trip.Days.Remove(day);
            _itineraryDays.Remove(day);
        }

        var existingDates = trip.Days.Select(d => d.Date).ToHashSet();
        foreach (var date in targetDates.Where(d => !existingDates.Contains(d)))
        {
            var day = new ItineraryDay { TripId = trip.Id, Date = date };
            trip.Days.Add(day);
            // Explicit Add: BaseEntity self-assigns the Guid key, so EF's graph
            // discovery would classify this as an EXISTING row (UPDATE, not INSERT).
            _itineraryDays.Add(day);
        }

        var dayNumber = 1;
        foreach (var day in trip.Days.OrderBy(d => d.Date))
        {
            day.DayNumber = dayNumber++;
        }
    }

    public async Task<TripDestinationDto> AddDestinationAsync(Guid tripId, AddDestinationRequest request, CancellationToken cancellationToken = default)
    {
        await _addDestinationValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var userId = _currentUser.GetRequiredUserId();

        var trip = await _trips.GetTrackedWithFullGraphAsync(tripId, userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Trip), tripId);

        await EnsureDayBelongsToTripAsync(request.ItineraryDayId, trip.Id, cancellationToken);

        var destination = await GetOrCreateDestinationAsync(request.ProviderId, cancellationToken);

        // Duplicate rule (US4/US6): once per day — and per Saved Places bucket.
        if (trip.Items.Any(i => i.DestinationId == destination.Id && i.ItineraryDayId == request.ItineraryDayId))
        {
            throw new ConflictException("This destination is already in that part of the trip.");
        }

        var bucket = trip.Items.Where(i => i.ItineraryDayId == request.ItineraryDayId).ToList();

        var item = new ItineraryItem
        {
            TripId = trip.Id,
            DestinationId = destination.Id,
            Destination = destination,
            ItineraryDayId = request.ItineraryDayId,
            SortOrder = bucket.Count == 0 ? 0 : bucket.Max(i => i.SortOrder) + 1,
        };
        trip.Items.Add(item);
        _itineraryItems.Add(item);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            // Unique index (ItineraryDayId, DestinationId): a concurrent request
            // added the same destination between our check and the save.
            throw new ConflictException("This destination is already in that part of the trip.");
        }

        return item.ToDestinationDto();
    }

    public async Task<TripDestinationDto> UpdateItineraryItemAsync(Guid tripId, Guid itemId, UpdateItineraryItemRequest request, CancellationToken cancellationToken = default)
    {
        await _updateItemValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var userId = _currentUser.GetRequiredUserId();

        var trip = await _trips.GetTrackedWithFullGraphAsync(tripId, userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Trip), tripId);

        var item = trip.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new NotFoundException(nameof(ItineraryItem), itemId);

        await EnsureDayBelongsToTripAsync(request.ItineraryDayId, trip.Id, cancellationToken);

        // Duplicate rule on the TARGET day (US4/US6) — the moved item itself is
        // exempt, so reordering within the same day passes this check.
        if (trip.Items.Any(i => i.Id != item.Id
                && i.DestinationId == item.DestinationId
                && i.ItineraryDayId == request.ItineraryDayId))
        {
            throw new ConflictException("This destination is already in that part of the trip.");
        }

        var sourceDayId = item.ItineraryDayId;
        item.ItineraryDayId = request.ItineraryDayId;

        // Spec §11.1 US4-US6: insert at the requested position, then renumber
        // 0..n so values stay dense. Clamp so "position 99" means "last".
        var target = trip.Items
            .Where(i => i.Id != item.Id && i.ItineraryDayId == request.ItineraryDayId)
            .OrderBy(i => i.SortOrder)
            .ToList();
        target.Insert(Math.Min(request.SortOrder, target.Count), item);
        Resequence(target);

        // The bucket the item left keeps its relative order but closes the gap.
        if (sourceDayId != request.ItineraryDayId)
        {
            Resequence(trip.Items
                .Where(i => i.Id != item.Id && i.ItineraryDayId == sourceDayId)
                .OrderBy(i => i.SortOrder)
                .ToList());
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken); // single save: both buckets move atomically
        }
        catch (ConcurrencyException)
        {
            // Unique index (ItineraryDayId, DestinationId): a concurrent request
            // put the same destination into the target day between check and save.
            throw new ConflictException("This destination is already in that part of the trip.");
        }

        return item.ToDestinationDto();
    }

    private static void Resequence(List<ItineraryItem> bucket)
    {
        for (var position = 0; position < bucket.Count; position++)
        {
            bucket[position].SortOrder = position;
        }
    }

    /// <summary>
    /// A target day, when given, must exist and belong to THIS trip
    /// (spec §11.1 rule 2). Also used by the schedule/move endpoint (US4-US6).
    /// </summary>
    private async Task EnsureDayBelongsToTripAsync(Guid? itineraryDayId, Guid tripId, CancellationToken cancellationToken)
    {
        if (itineraryDayId is null)
        {
            return; // no day targeted -> Saved Places, nothing to check
        }

        var belongs = await _trips.DayBelongsToTripAsync(itineraryDayId.Value, tripId, cancellationToken);
        if (!belongs)
        {
            throw ValidationException.ForProperty(
                nameof(AddDestinationRequest.ItineraryDayId),
                "The itinerary day does not belong to this trip.");
        }
    }

    /// <summary>
    /// Upsert of the <see cref="Destination"/> cache (spec §11.0-6): rows are
    /// created lazily the first time any user adds a place to any trip. A fresh
    /// ProviderId is the normal case — never 404 on a cache miss; only when the
    /// external provider does not know the id either.
    /// </summary>
    private async Task<Destination> GetOrCreateDestinationAsync(string providerId, CancellationToken cancellationToken)
    {
        var cached = await _destinations.GetByProviderIdAsync(providerId, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var details = await _destinationProvider.GetDestinationDetailsAsync(providerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Destination), providerId);

        var destination = details.ToEntity();

        // The provider's own image data is sparse (Geoapify only has
        // wiki_and_media for some places) — fall back to a Serper image search
        // by name, same source the attraction cards use, so a saved
        // destination isn't stuck showing the placeholder icon everywhere.
        if (destination.ImageUrl is null)
        {
            try
            {
                destination.ImageUrl = await _imageSearch.SearchImageAsync(destination.Name, cancellationToken);
            }
            catch (Exception ex) when (IsTransientExternalFailure(ex) && !cancellationToken.IsCancellationRequested)
            {
                // Serper down, timed out, or returned something unparseable —
                // the destination is still saved, just without a photo.
            }
        }

        _destinations.Add(destination);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return destination;
        }
        catch (ConcurrencyException)
        {
            // Unique index on ProviderId: a concurrent request inserted the same
            // place first. Discard our copy and use the winner's row.
            _destinations.Remove(destination);
            return await _destinations.GetByProviderIdAsync(providerId, cancellationToken)
                ?? throw new InvalidOperationException($"Destination '{providerId}' vanished after a concurrency conflict.");
        }
    }

    /// <summary>
    /// True for the external-call failure modes treated as "no image found"
    /// rather than "the whole request must fail": connection failures,
    /// HttpClient timeouts (surfaced as TaskCanceledException, not
    /// HttpRequestException), and an unparseable response body.
    /// </summary>
    private static bool IsTransientExternalFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException;

    public async Task RemoveDestinationAsync(Guid tripId, Guid itemId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetRequiredUserId();

        var item = await _trips.GetOwnedItemAsync(tripId, itemId, userId, cancellationToken) // NFR 6
            ?? throw new NotFoundException(nameof(ItineraryItem), itemId);

        // SortOrder gaps left by the removal are harmless — ordering is relative.
        _itineraryItems.Remove(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 5: Update `TripServiceTests.CreateSut`**

In `backend/tests/TripPlanner.Application.Tests/Trips/TripServiceTests.cs`, add `using TripPlanner.Infrastructure.Persistence.Repositories;` to the usings, then replace the `CreateSut` method (lines 32-42) with:

```csharp
    private static TripService CreateSut(
        ApplicationDbContext db, Guid? userId, IDestinationProvider? provider = null, Mock<IImageSearchProvider>? imageSearch = null)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.UserId).Returns(userId);

        return new TripService(
            new TripRepository(db),
            new Repository<Domain.Entities.ItineraryDay>(db),
            new Repository<Domain.Entities.ItineraryItem>(db),
            new DestinationRepository(db),
            new UnitOfWork(db),
            currentUser.Object,
            provider ?? Mock.Of<IDestinationProvider>(),
            (imageSearch ?? NoOpImageSearch()).Object,
            new CreateTripRequestValidator(), new UpdateTripRequestValidator(),
            new AddDestinationRequestValidator(), new UpdateItineraryItemRequestValidator());
    }
```

(Fully-qualifying `Domain.Entities.ItineraryDay`/`ItineraryItem` matches this test file's existing style — it has no top-level `using TripPlanner.Domain.Entities;` and fully qualifies entity types elsewhere, e.g. `new Domain.Entities.ItineraryDay { ... }` at line 110.)

- [ ] **Step 6: Run the Trip tests to verify they still pass**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~TripServiceTests"
```
Expected: all passing (27 tests), unchanged from Step 1.

- [ ] **Step 7: Commit**

```bash
git add backend/src/TripPlanner.Application/Common/Interfaces/ITripRepository.cs backend/src/TripPlanner.Infrastructure/Persistence/Repositories/TripRepository.cs backend/src/TripPlanner.Infrastructure/DependencyInjection.cs backend/src/TripPlanner.Application/Features/Trips/TripService.cs backend/tests/TripPlanner.Application.Tests/Trips/TripServiceTests.cs
git commit -m "Route TripService through ITripRepository, IDestinationRepository, and IUnitOfWork"
```

---

### Task 5: Retire `IApplicationDbContext`

**Files:**
- Delete: `backend/src/TripPlanner.Application/Common/Interfaces/IApplicationDbContext.cs`
- Modify: `backend/src/TripPlanner.Infrastructure/Persistence/ApplicationDbContext.cs` (drop the interface implementation)
- Modify: `backend/src/TripPlanner.Infrastructure/DependencyInjection.cs:78-79` (remove the exposing registration)
- Modify: `backend/src/TripPlanner.Application/TripPlanner.Application.csproj` (remove the EF Core package reference)
- Modify: `TECHNICAL_SPEC.md:131-141`

**Interfaces:** None produced — this task only removes the now-dead `IApplicationDbContext` seam. By the end of Task 4, nothing references it except its own file, its implementation, and its one DI registration line.

- [ ] **Step 1: Confirm nothing but the three known files still references `IApplicationDbContext`**

```bash
cd backend && grep -rn "IApplicationDbContext" --include="*.cs" .
```
Expected: only `src/TripPlanner.Application/Common/Interfaces/IApplicationDbContext.cs` (the definition), `src/TripPlanner.Infrastructure/Persistence/ApplicationDbContext.cs` (`: IApplicationDbContext`), and `src/TripPlanner.Infrastructure/DependencyInjection.cs` (the registration line). If anything else shows up, stop — a service was missed in Tasks 2-4 and must be fixed before continuing.

- [ ] **Step 2: Delete the interface file**

```bash
git rm backend/src/TripPlanner.Application/Common/Interfaces/IApplicationDbContext.cs
```

- [ ] **Step 3: Drop the interface implementation from `ApplicationDbContext.cs`**

Replace the file's class declaration and doc comment:

```csharp
using Microsoft.EntityFrameworkCore;
using TripPlanner.Domain.Common;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence;

/// <summary>
/// The EF Core database context, used directly by the repository classes in
/// this namespace (see Repositories/). Entity-to-table mapping lives in the
/// *Configuration classes in this folder (applied via
/// <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/>).
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<ItineraryDay> ItineraryDays => Set<ItineraryDay>();
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<ItineraryItem> ItineraryItems => Set<ItineraryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Stamp audit timestamps automatically whenever entities are saved.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
```

(Note: `using TripPlanner.Application.Common.Interfaces;` is removed — this file no longer references anything in that namespace.)

- [ ] **Step 4: Remove the DI registration**

In `backend/src/TripPlanner.Infrastructure/DependencyInjection.cs`, delete these two lines from `AddPersistence` (the comment and the registration — everything else added in Tasks 1-4 stays):

```csharp
        // Expose the context to the Application layer through its interface.
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
```

- [ ] **Step 5: Remove the EF Core package reference from Application**

In `backend/src/TripPlanner.Application/TripPlanner.Application.csproj`, replace:

```xml
  <ItemGroup>
    <!-- EF Core abstractions only: lets us expose DbSet<T> via IApplicationDbContext.
         The concrete DbContext lives in Infrastructure. -->
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.9" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.9" />
  </ItemGroup>
```

with:

```xml
  <ItemGroup>
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.9" />
  </ItemGroup>
```

- [ ] **Step 6: Update `TECHNICAL_SPEC.md`**

Replace lines 131-141:

```markdown
- The Application layer consumes only interfaces it defines itself
  (`IApplicationDbContext`, `IPasswordHasher`, `IJwtTokenGenerator`,
  `ICurrentUserService`, `IDestinationProvider`); implementations live in
  Infrastructure/WebApi and are bound in DI extension methods
  ([Application/DependencyInjection.cs](backend/src/TripPlanner.Application/DependencyInjection.cs),
  [Infrastructure/DependencyInjection.cs](backend/src/TripPlanner.Infrastructure/DependencyInjection.cs)).
- One deliberate deviation from strict Clean Architecture: `IApplicationDbContext`
  exposes EF Core `DbSet<T>` properties, so the Application layer takes a package
  dependency on `Microsoft.EntityFrameworkCore`. The Application `.csproj` comment
  acknowledges this explicitly ("EF Core abstractions only"). There is **no
  repository pattern** — services query `DbSet`s directly. [Observed]
```

with:

```markdown
- The Application layer consumes only interfaces it defines itself
  (`IRepository<T>`, `IUnitOfWork`, `IUserRepository`, `ITripRepository`,
  `IDestinationRepository`, `IPasswordHasher`, `IJwtTokenGenerator`,
  `ICurrentUserService`, `IDestinationProvider`); implementations live in
  Infrastructure/WebApi and are bound in DI extension methods
  ([Application/DependencyInjection.cs](backend/src/TripPlanner.Application/DependencyInjection.cs),
  [Infrastructure/DependencyInjection.cs](backend/src/TripPlanner.Infrastructure/DependencyInjection.cs)).
- A generic `IRepository<T>` + `IUnitOfWork` pair, plus per-entity repository
  interfaces for `User`, `Trip`, and `Destination` (see
  [docs/superpowers/specs/2026-07-25-repository-pattern-design.md](docs/superpowers/specs/2026-07-25-repository-pattern-design.md)),
  is the data-access layer services depend on. The Application layer takes no
  package dependency on `Microsoft.EntityFrameworkCore` — a unique-index
  violation surfaces from `IUnitOfWork.SaveChangesAsync` as the EF-agnostic
  `ConcurrencyException`, which services catch and translate into a
  feature-specific `ConflictException`. [Observed]
```

- [ ] **Step 7: Build and run the full test suite**

```bash
cd backend && dotnet build && dotnet test
```
Expected: build succeeds, all tests pass (Auth: 12, Trips: 27, Destinations: 29, Identity: existing, Repositories: 5).

- [ ] **Step 8: Confirm zero EF Core references remain in Application**

```bash
grep -rn "EntityFrameworkCore" backend/src/TripPlanner.Application --include="*.cs" --include="*.csproj"
```
Expected: no output.

- [ ] **Step 9: Commit**

```bash
git add -A backend/src/TripPlanner.Application/Common/Interfaces/IApplicationDbContext.cs backend/src/TripPlanner.Infrastructure/Persistence/ApplicationDbContext.cs backend/src/TripPlanner.Infrastructure/DependencyInjection.cs backend/src/TripPlanner.Application/TripPlanner.Application.csproj TECHNICAL_SPEC.md
git commit -m "Retire IApplicationDbContext now that repositories own all data access"
```
