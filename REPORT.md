# TripPlanner — Báo cáo triển khai

Tài liệu này tập trung vào **cách làm và lý do quyết định**, không nhắc lại requirement.

---

## Mục lục

1. [Tổng quan & tech stack](#1-tổng-quan--tech-stack)
2. [Kiến trúc backend](#2-kiến-trúc-backend)
3. [Luồng hoạt động end-to-end](#3-luồng-hoạt-động-end-to-end)
4. [Middleware & HTTP pipeline](#4-middleware--http-pipeline)
5. [Error handling](#5-error-handling)
6. [Validation](#6-validation)
7. [Cache hoạt động như thế nào](#7-cache-hoạt-động-như-thế-nào)
8. [Logging](#8-logging)
9. [Concurrency](#9-concurrency)
10. [Frontend gọi API như thế nào](#10-frontend-gọi-api-như-thế-nào)
11. [Testing](#11-testing)
12. [Configuration & secrets](#12-configuration--secrets)
13. [Q&A — câu reviewer có thể hỏi](#13-qa--câu-reviewer-có-thể-hỏi)
14. [Hạn chế đã biết](#14-hạn-chế-đã-biết)

---

## 1. Tổng quan & tech stack

| Thành phần | Công nghệ |
|---|---|
| Backend | .NET 10, ASP.NET Core Web API, Clean Architecture (4 project) |
| ORM / DB | EF Core + PostgreSQL (Npgsql) |
| Auth | JWT Bearer, BCrypt hash mật khẩu |
| Validation | FluentValidation |
| Cache | `IDistributedCache` — in-process (mặc định) hoặc Redis |
| API ngoài | Geoapify (địa điểm/POI), Serper (tìm ảnh), SMTP (email xác thực) |
| Frontend | React + TypeScript + Vite + React Router + Axios |
| Test | xUnit + Moq + EF InMemory + `WebApplicationFactory`; Vitest + React Testing Library |

**Quy mô test:** 347 case backend (279 `Application.Tests` + 68 `WebApi.Tests`), 263 case
frontend (28 file) — tất cả đều pass. Chạy `dotnet test` và `npm test` để xác nhận lại trước
khi trình bày.

**API endpoints:**

```
POST   /api/auth/register              (ẩn danh)
POST   /api/auth/login                 (ẩn danh)
POST   /api/auth/verify-email          (ẩn danh)
POST   /api/auth/resend-verification   (ẩn danh)

GET    /api/destinations/locations     (công khai — có chủ đích)
GET    /api/destinations/attractions   (công khai)
GET    /api/destinations/{providerId}  (công khai)

GET    /api/trips                              [Authorize]
GET    /api/trips/{tripId}                     [Authorize]
POST   /api/trips                              [Authorize]
PUT    /api/trips/{tripId}                     [Authorize]
POST   /api/trips/{tripId}/destinations        [Authorize]
PUT    /api/trips/{tripId}/destinations/{itemId} [Authorize]
DELETE /api/trips/{tripId}/destinations/{itemId} [Authorize]
```

`DestinationsController` **cố ý không có `[Authorize]`** — người dùng phải duyệt được điểm đến
trước khi đăng nhập. Có một test riêng pin điều này, vì nếu ai đó lỡ thêm `[Authorize]` vào thì
không có test nào khác trong suite phát hiện được.

---

## 2. Kiến trúc backend

```
         ┌──────────────────────────────────────┐
         │  WebApi (Controllers, Middleware)    │
         └──────────┬───────────────┬───────────┘
                    │               │
                    ▼               ▼
         ┌──────────────────┐   ┌────────────────────────┐
         │   Application    │◀──│    Infrastructure      │
         │ Services, DTOs,  │   │ EF Core, JWT, BCrypt,  │
         │ Validators,      │   │ Geoapify, SMTP, Cache  │
         │ Interfaces       │   └────────────────────────┘
         └────────┬─────────┘
                  ▼
         ┌──────────────────┐
         │      Domain      │  ← KHÔNG có package reference nào
         │ Entities, rules  │
         └──────────────────┘
```

### Bằng chứng của "dependency rule" nằm trong file `.csproj`

| Project | ProjectReference | Package chính |
|---|---|---|
| `TripPlanner.Domain` | *(không có)* | *(không có)* |
| `TripPlanner.Application` | Domain | FluentValidation, các gói `*.Abstractions` |
| `TripPlanner.Infrastructure` | Application | EF Core, Npgsql, BCrypt, StackExchange.Redis, JWT |
| `TripPlanner.WebApi` | Application + Infrastructure | ASP.NET Core, Swagger, DotNetEnv |

Điểm mạnh nhất: **Domain không có một `PackageReference` nào**. Nếu vô tình viết
`using Microsoft.EntityFrameworkCore;` trong `AuthService.cs`, project **không build được** —
kiến trúc được compiler ép buộc chứ không phụ thuộc kỷ luật lập trình viên.

### Dependency Inversion — Application định nghĩa, Infrastructure thực thi

Infrastructure tham chiếu Application (không phải ngược lại), nhưng lúc chạy Application mới là
bên gọi database. Được như vậy vì Application chỉ khai báo **cái nó cần**:

| Interface (Application) | Implementation (Infrastructure) |
|---|---|
| `IUserRepository`, `ITripRepository`, `IDestinationRepository` | EF Core repositories |
| `IPasswordHasher` | `BCryptPasswordHasher` |
| `IJwtTokenGenerator` | `JwtTokenGenerator` |
| `IDestinationProvider` | `GeoapifyClient` |
| `IImageSearchProvider` | `SerperImageClient` |
| `IEmailSender` | `SmtpEmailSender` |
| `IAppUrlProvider` | `AppUrlProvider` |
| `ICurrentUserService` | **`CurrentUserService` — ở WebApi** (xem bên dưới) |

### Composition root

Mỗi tầng sở hữu file `DependencyInjection.cs` riêng; `Program.cs` chỉ gọi 3 dòng:

```csharp
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddWebApi();
```

### Một ngoại lệ có chủ ý

`ICurrentUserService` được implement ở **WebApi**, không phải Infrastructure, vì nó cần
`IHttpContextAccessor` để đọc claim từ JWT. `HttpContext` là khái niệm của **ASP.NET Core**,
không phải hạ tầng kỹ thuật như DB hay SMTP — đặt vào Infrastructure sẽ buộc project đó phải
tham chiếu ASP.NET Core, làm bẩn ranh giới.

### Tổ chức theo feature, không theo kỹ thuật

```
Application/Features/
  Auth/          AuthService.cs, IAuthService.cs, AuthMappings.cs, Dtos/, Validators/
  Destinations/  DestinationService.cs, IDestinationService.cs, DestinationMappings.cs, Dtos/, Validators/
  Trips/         TripService.cs, ITripService.cs, TripMappings.cs, Dtos/, Validators/
```

Sửa một feature chỉ cần mở một thư mục, không phải nhảy giữa `Services/`, `Models/`, `Validators/`.

### Repository pattern — không có Unit of Work, không có `IRepository<T>` generic

Mỗi aggregate một interface; **method ghi tự `SaveChanges`**:

```csharp
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);     // lưu ngay
    Task UpdateAsync(User user, CancellationToken ct = default);  // lưu ngay
}
```

Lý do: mỗi request trong hệ này thao tác đúng **một aggregate**, nên không có kịch bản nào cần
gom nhiều thay đổi vào một transaction do tầng trên điều khiển. Thêm Unit of Work sẽ là một lớp
trừu tượng không ai dùng tới.

---

## 3. Luồng hoạt động end-to-end

### 3.1. Đăng ký → xác thực email → đăng nhập

```
[FE] RegisterPage
   │ authApi.register(email, password, displayName)
   ▼
POST /api/auth/register
   │
   ▼ AuthController.Register  ── không có logic, chỉ gọi service
   ▼ AuthService.RegisterAsync
       1. RegisterValidator.ValidateAndThrowAppExceptionAsync()   → 400 nếu sai
       2. NormalizeEmail()                                        → lower + trim
       3. _users.ExistsByEmailAsync()  → nếu trùng: ConflictException → 409
       4. _passwordHasher.Hash()                                  → BCrypt
       5. _users.AddAsync(user)                                   → INSERT
       6. _tokenGenerator: sinh token xác thực email (purpose claim riêng)
       7. _emailSender.SendAsync(link)  ── LỖI Ở ĐÂY KHÔNG LÀM HỎNG ĐĂNG KÝ
       8. return AuthMappings.ToDto(user)                         → 201
```

Bước 7 là một guarantee có chủ ý: SMTP chết thì tài khoản vẫn được tạo, user dùng
"Resend verification" sau. Có test ở tầng Application khẳng định điều này.

```
[Email] link → [FE] VerifyEmailPage
   │ authApi.verifyEmail(token)
   ▼ POST /api/auth/verify-email  → đánh dấu user đã xác thực

[FE] LoginPage
   │ authApi.login(email, password)
   ▼ POST /api/auth/login
   ▼ AuthService.LoginAsync
       1. LoginValidator (chỉ kiểm tra có nhập hay chưa)
       2. tìm user + verify BCrypt   → sai: UnauthorizedException 401 (message chung chung)
       3. chưa xác thực email        → ForbiddenException 403
       4. sinh JWT access token      → 200 { accessToken, user }
   │
   ▼ [FE] AuthContext.login()
       storeToken(accessToken)              → localStorage['tripplanner.token']
       localStorage['tripplanner.user'] = … → giữ session qua F5
       setUser(...)                         → isAuthenticated = true
```

Ở LoginPage, **403 được xử lý riêng**: hiện nút "Gửi lại email xác thực" thay vì báo lỗi suông.
Đây là chỗ frontend cần `getErrorStatus()` chứ không chỉ message.

### 3.2. Tìm điểm đến → xem chi tiết → thêm vào trip

```
[FE] SearchPage / CitySearchInput
   │ gõ phím → debounce 300 ms
   ▼ GET /api/destinations/locations?query=hano
   ▼ DestinationService.SearchLocationsAsync
       1. trim query rồi mới validate (≥2 ký tự) — validate đúng chuỗi sẽ gửi đi
       2. cache key = "loc:{query viết thường}"  → "Paris" và "paris" dùng chung 1 entry
       3. CACHE HIT còn tươi (<24h) → trả luôn, KHÔNG gọi Geoapify
       4. MISS → _provider.SearchLocationsAsync()
             ├ DistinctBy (name, country)   ← Geoapify trả trùng thành phố nhiều place_id
             ├ OrderBy RelevanceRank        ← khớp chính xác > khớp đầu chuỗi > khớp giữa
             └ Take(5)
       5. cache lại KẾT QUẢ ĐÃ XỬ LÝ → lần sau hit thì bỏ qua luôn cả bước dedupe/rank
   ▼
[FE] chọn 1 city → GET /api/destinations/attractions?lat&lon&radiusKm
   ▼ DestinationService.GetAttractionsAsync
       - cache key làm tròn toạ độ 3 chữ số (~100 m) → các vị trí gần nhau dùng chung entry
       - sắp xếp "Recommended": có rating trước, rating giảm dần
       - Take(20) TRƯỚC khi enrich ảnh → chỉ tốn API ảnh cho 20 kết quả thực trả về
       - EnrichWithImagesAsync: Parallel.ForEachAsync, MaxDegreeOfParallelism = 5
         (Serper là API trả tiền — không bắn 20 request cùng lúc)
       - một place tra ảnh lỗi → chỉ place đó không có ảnh, KHÔNG hỏng cả danh sách
   ▼
[FE] AttractionCard → DestinationDetailsPage
   ▼ GET /api/destinations/{providerId}
   ▼ DestinationService.GetDetailsAsync
       provider (qua cache) → nếu null → fallback sang DB của mình → nếu vẫn null → 404
   ▼
[FE] AddToTripButton (cần đăng nhập)
   ▼ POST /api/trips/{tripId}/destinations  { providerId, itineraryDayId }
   ▼ TripService.AddDestinationAsync
       1. validator
       2. lấy trip theo (userId, tripId)  → không thấy: 404
       3. GetOrCreateDestinationAsync(providerId)  ← UPSERT: providerId mới là chuyện bình thường
       4. EnsureNotDuplicate(...)  → trùng: ConflictException
       5. trip.Items.Add(...)
       6. SaveWithDuplicateGuardAsync → unique index là lớp chặn cuối cho race condition
```

**Điểm cần nhấn:** `DestinationService` **không bao giờ ghi** `Destination` row. Chỉ
`TripService.AddDestinationAsync` mới upsert, đúng lúc user thêm địa điểm vào trip lần đầu.
Duyệt/tìm kiếm không làm phình DB.

### 3.3. Đổi ngày trip → sinh lại itinerary days

Đây là thuật toán khó nhất trong project (`TripService.RegenerateDays`):

```
PUT /api/trips/{tripId}  { name, startDate, endDate }
   ▼
1. validator (tên bắt buộc)
2. Trip.SetDates(start, end)  ← quy tắc DOMAIN: start ≤ end, sai thì DomainException → 400
3. RegenerateDays(trip):
     • ngày CÒN nằm trong range   → GIỮ NGUYÊN, giữ luôn các item đã xếp
     • ngày RƠI RA ngoài range    → xoá; item của nó QUAY VỀ Saved Places (không mất)
     • ngày MỚI trong range       → tạo mới
     • cuối cùng: đánh số lại theo thứ tự thời gian
4. UpdateAsync (lưu 1 lần)
```

Không xoá sạch rồi tạo lại — làm vậy sẽ mất hết lịch trình user đã xếp chỉ vì lùi ngày về 1 hôm.
Code còn **mirror cascade `SetNull` của DB vào in-memory**, để DTO trả về đã hiển thị đúng
"item quay lại Saved Places" ngay, không cần load lại.

### 3.4. Kéo thả sắp xếp lịch trình

```
PUT /api/trips/{tripId}/destinations/{itemId}  { itineraryDayId, sortOrder }
   ▼ TripService.UpdateItineraryItemAsync
       - itineraryDayId = null nghĩa là "Saved Places"
       - EnsureNotDuplicate(..., excludeItemId: itemId)
         ← item được miễn tự-đối-chiếu, nên kéo thả trong cùng 1 ngày vẫn hợp lệ
       - MoveItem: chèn vào vị trí, sortOrder được CLAMP → gửi 99 nghĩa là "cuối cùng"
       - Resequence CẢ HAI bucket về 0..n → giá trị luôn liền mạch, không có khoảng trống
       - MỘT lần save → hai bucket đổi nguyên tử
```

---

## 4. Middleware & HTTP pipeline

Thứ tự trong `Program.cs` (thứ tự **quan trọng**):

```csharp
app.UseMiddleware<ExceptionHandlingMiddleware>();   // 1. NGOÀI CÙNG
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();                               // 2. chỉ ở Development
    app.UseSwaggerUI();
}
app.UseCors(CorsPolicy);                            // 3. trước auth
app.UseAuthentication();                            // 4. đọc & xác thực JWT → User claims
app.UseAuthorization();                             // 5. áp dụng [Authorize]
app.MapControllers();                               // 6. vào controller
app.Run();
```

**Vì sao thứ tự này:**

| Vị trí | Lý do |
|---|---|
| `ExceptionHandlingMiddleware` **đầu tiên** | Nó bọc `await _next(context)` trong `try/catch`, nên phải nằm ngoài cùng mới bắt được exception của mọi middleware phía sau. Đặt sau CORS thì lỗi ở CORS sẽ lọt ra ngoài dưới dạng 500 thô. |
| CORS **trước** Authentication | Preflight `OPTIONS` của trình duyệt không mang header `Authorization`. Nếu Authentication chạy trước, preflight bị chặn và trình duyệt không bao giờ gửi request thật. |
| Authentication **trước** Authorization | `UseAuthentication` mới là bước *dựng* `HttpContext.User` từ token; `UseAuthorization` chỉ *đọc* nó ra để so với `[Authorize]`. Đảo lại thì mọi request đều bị coi là ẩn danh. |
| Swagger chỉ ở Development | Không phơi schema API ra production. |

**Cấu hình JWT Bearer** — điểm đáng nói: `Program.cs` đọc `JwtSettings` qua `IOptions<>` chứ
**không** đọc trực tiếp `builder.Configuration[...]`. Lý do: đọc trực tiếp sẽ resolve config
**ngay lúc dựng**, chạy trước khi test host kịp cung cấp giá trị của nó. Có
`TestHostConfigurationTests` pin điều này — để suite test không bao giờ vô tình ký token bằng
key thật của developer.

`ClockSkew = TimeSpan.Zero` — mặc định của .NET là 5 phút, nghĩa là token hết hạn vẫn dùng được
thêm 5 phút. Đặt về 0 để `ExpiryMinutes` mang đúng nghĩa của nó.

**Migration tự chạy lúc khởi động**, nhưng bỏ qua khi `Environment == "Testing"` vì provider
InMemory không hỗ trợ migration (`GetPendingMigrations()` sẽ throw).

---

## 5. Error handling

### Nguyên tắc: controller không có một `try/catch` nào

Application ném exception mang ngữ nghĩa nghiệp vụ; một middleware duy nhất dịch sang HTTP.

```csharp
var (status, title) = exception switch
{
    ValidationException     => (400, "Validation failed"),
    DomainException         => (400, "Business rule violated"),
    UnauthorizedException   => (401, "Authentication failed"),
    ForbiddenException      => (403, "Forbidden"),
    NotFoundException       => (404, "Resource not found"),
    ConflictException       => (409, "Conflict"),
    NotImplementedException => (501, "Not implemented yet"),
    _                       => (500, "An unexpected error occurred"),
};
```

### Response chuẩn RFC 7807 (`application/problem+json`)

```json
{
  "status": 400,
  "title": "Validation failed",
  "detail": "One or more validation errors occurred.",
  "errors": { "Password": ["Password must be at least 8 characters."] }
}
```

`errors` **chỉ** được gắn khi là `ValidationException` và có phần tử.

### Hai quyết định bảo mật trong middleware

1. **Nhánh 500 giấu message khi không phải Development.** Exception chưa được map có thể chứa
   câu SQL, tên bảng, connection string trong message. Các exception đã map thì message là
   text hướng tới người dùng nên trả nguyên văn.
2. **Chỉ nhánh 500 mới ghi log ở mức Error.** 404/409/400 là hoạt động bình thường của API, ghi
   Error sẽ làm nhiễu log tới mức không ai đọc nữa.

### Ngữ nghĩa an toàn ở tầng service

| Tình huống | Trả về | Lý do |
|---|---|---|
| Trip của người khác | **404**, không phải 403 | Query lọc theo `userId` AND `tripId`, nên "của người khác" và "không tồn tại" không phân biệt được → không lộ sự tồn tại của tài nguyên |
| Đăng ký email đã tồn tại | 409 với message chung `"Unable to register with the provided details."` | Không xác nhận email nào đã đăng ký |
| Sai mật khẩu / sai email | 401 cùng một message | Không cho dò tài khoản |

---

## 6. Validation

### Ba tầng, quy tắc phân chia rõ ràng

| Loại kiểm tra | Đặt ở đâu | Exception | HTTP |
|---|---|---|---|
| Chỉ nhìn input (bắt buộc, độ dài, khoảng giá trị) | Validator class | `ValidationException` | 400 |
| Cần DB (email trùng, trip thuộc về ai) | Service method | `ConflictException` / `ValidationException.ForProperty` | 409 / 400 |
| Bất biến của entity (ngày kết thúc < ngày bắt đầu) | Domain entity | `DomainException` | 400 |

### Đường đi của một message lỗi

```
1. Khai báo   RegisterRequestValidator:
                .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
2. Gom lại    ValidationExtensions.ValidateAndThrowAppExceptionAsync
                result.ToDictionary() → { "Password": ["Password must be at least 8 characters."] }
                → ném ValidationException của DỰ ÁN (không phải của FluentValidation)
3. Serialize  ExceptionHandlingMiddleware → ProblemDetails + extensions["errors"]
4. Đọc        frontend getErrorMessage(err, fallback) → err.response.data.detail
```

Bước 2 tồn tại vì FluentValidation có `ValidationException` **của riêng nó**, mà middleware
không biết type đó. Extension method này là cây cầu nối.

### Validator KHÔNG đăng ký DI — và đó là chủ ý

```csharp
private static readonly RegisterRequestValidator RegisterValidator = new();
```

Lý do: validator là khai báo quy tắc **stateless, không có dependency**. Inject `IValidator<T>`
sẽ thành 10 interface mà mỗi cái đúng một implementation, không bao giờ có ai thay thế.
FluentValidation validator an toàn khi dùng đồng thời sau khi đã dựng xong.

**Điều gì sẽ đảo ngược quyết định này:** một validator cần dependency (ví dụ rule async truy vấn
DB) thì không thể là static instance nữa — khi đó chuyển **riêng validator đó** thành constructor
parameter, giữ nguyên phần còn lại.

### Vài quy tắc tinh tế đáng kể

- `.Cascade(CascadeMode.Stop)` — dừng ở lỗi đầu tiên, để `email.Contains('@')` không chạy trên
  `null`.
- `LoginRequestValidator` **cố tình không có** rule độ dài mật khẩu: nâng policy sau này sẽ khoá
  luôn tài khoản cũ. Có test pin sự vắng mặt này.
- `RegisterRequestValidator` dùng **cùng một message** cho "để trống" và "sai định dạng", để
  không thể dùng message dò xem email đã tồn tại hay chưa.
- `ProviderId` **không giới hạn độ dài** ở validator lẫn DB: `place_id` của Geoapify là prefix
  ~68 ký tự cộng tên địa danh hex-encode (2 ký tự cho mỗi byte UTF-8), thực tế dài 62–328 ký tự.
  `varchar(128)` từng làm "add to trip" lỗi 500 với tên dài hoặc không phải Latin.

---

## 7. Cache hoạt động như thế nào

### Cache cái gì

Chỉ cache **đường duyệt (browse path)** của Destination — kết quả gọi API ngoài. Trip và User
không cache (dữ liệu riêng tư, thay đổi liên tục).

| Đường | Cache key | TTL |
|---|---|---|
| Tìm thành phố | `loc:{query viết thường}` | 24 giờ |
| Danh sách POI | `attr:{lat làm tròn 3}:{lon làm tròn 3}:{radiusKm}` | 6 giờ |
| Chi tiết địa điểm | theo `providerId` | 24 giờ |
| Ảnh của địa điểm | theo place | 24 giờ (cache cả kết quả "không tìm thấy") |

**Thời gian lưu giữ (retention): 7 ngày** — dài hơn TTL rất nhiều. Đây là mấu chốt của thiết kế.

### Thiết kế cốt lõi: "stale-better-than-down"

Không dùng TTL của cache để evict. Thay vào đó bọc giá trị trong một envelope tự quản lý:

```csharp
private sealed record CacheEnvelope<T>(T Value, DateTimeOffset FetchedAt);
```

```csharp
private async Task<T> GetCachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> fetchAsync, CancellationToken ct)
{
    var stale = await TryGetCachedEnvelopeAsync<T>(key, ct);

    // 1. HIT còn tươi → trả luôn, không gọi provider
    if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < ttl)
        return stale.Value;

    try
    {
        // 2. MISS hoặc hết hạn → gọi provider, cache lại
        var value = await fetchAsync();
        await SetCachedEnvelopeAsync(key, new CacheEnvelope<T>(value, _clock.GetUtcNow()), ct);
        return value;
    }
    catch (Exception ex) when (IsTransientExternalFailure(ex) && !ct.IsCancellationRequested && stale is not null)
    {
        // 3. Provider CHẾT nhưng còn entry cũ → trả dữ liệu cũ còn hơn trả lỗi
        return stale.Value;
    }
}
```

**Vì sao tự kiểm tra freshness thay vì để cache tự evict:** nếu để cache evict theo TTL, entry
hết hạn sẽ **biến mất** — lúc Geoapify sập thì không còn gì để fallback. Giữ entry 7 ngày và tự
so `FetchedAt` với TTL cho phép phân biệt ba trạng thái: *tươi*, *cũ nhưng dùng được*, và
*không có*.

`_clock` là `TimeProvider` được inject (đăng ký `TimeProvider.System` trong `AddApplication()`),
nên test có thể tua thời gian để kiểm tra logic hết hạn mà không cần `Thread.Sleep`.

### Ba tầng chống lỗi

```
DestinationService                  ← tầng 3: provider chết → trả stale
   │ IDistributedCache (abstraction)
   ▼
ResilientDistributedCache           ← tầng 2: Redis chết → coi như cache miss
   │ IDistributedCache (thật)
   ▼
MemoryDistributedCache / RedisCache ← tầng 1: backend chọn qua config
```

**`ResilientDistributedCache`** là decorator, bọc bất kỳ backend nào được cấu hình:

```csharp
private static bool IsCacheUnavailable(Exception ex) =>
    ex is RedisConnectionException or RedisTimeoutException or TimeoutException;
```

- Đây là **nơi duy nhất trong solution được tham chiếu exception type của StackExchange.Redis**.
  Application chỉ thấy `IDistributedCache`, và "miss" thì không phân biệt được với "chưa cache".
- **Degrade âm thầm là mục đích, nhưng degrade vô hình thì không**: mọi lỗi bị nuốt đều được ghi
  `LogWarning`. Nếu không, Redis sập trông y hệt một cache không bao giờ hit.
- **Không log cache key** — key có chứa từ khoá tìm kiếm của người dùng.

### Chuyển Memory ↔ Redis chỉ bằng config

```csharp
var provider = configuration["Cache:Provider"] ?? "Memory";
if (provider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
    services.AddStackExchangeRedisCache(o => o.Configuration = configuration.GetConnectionString("Redis"));
else
    services.AddDistributedMemoryCache();

// rồi bọc bất kỳ cái nào vừa đăng ký bằng decorator
```

Trong `.env`: `Cache__Provider=Redis` + `ConnectionStrings__Redis=...`. Không sửa một dòng code
nào ở Application.

### Xử lý entry cũ không còn khớp DTO

Vì entry sống 7 ngày, việc đổi shape của một DTO sẽ khiến mọi entry cũ deserialize lỗi.

```csharp
catch (JsonException ex)
{
    _logger.LogWarning(ex, "Discarding a cache entry for {Key} that no longer matches {Type}.", key, typeof(T).Name);
    return null;   // coi như miss
}
```

Coi như miss (để request không bị 500), nhưng **có log** — nếu không thì hiện tượng này trông
hệt như "cache tự dưng ngừng hoạt động". Lưu ý: **không** áp dụng cách xử lý này lúc *ghi* —
serialize lỗi khi ghi là bug thật, phải để nó nổi lên.

### Vài chi tiết nhỏ đáng nói

- **Cache kết quả ĐÃ xử lý**, không phải response thô của provider. Dedupe/rank/cap là thao tác
  tất định, nên một cache hit bỏ qua luôn cả phần xử lý đó.
- **Key toạ độ làm tròn 3 chữ số** (~100 m) để các vị trí bản đồ gần nhau dùng chung entry.
- **Format bằng `FormattableString.Invariant`** để key không đổi theo locale của server
  (`"16.05"` vs `"16,05"`).
- **Query hạ chữ thường** để `"Paris"` và `"paris"` chung một entry.
- **`Take(20)` TRƯỚC khi enrich ảnh** — chỉ tốn API ảnh (trả tiền) cho đúng số kết quả trả về.

---

## 8. Logging

### Chính sách: adapter log tại nguồn, rồi **ném lại**

Nguyên tắc xuyên suốt: nơi nào biết chi tiết kỹ thuật của lỗi thì nơi đó log, nhưng **không tự
quyết định** thay cho caller. Caller giữ chính sách fallback của riêng nó.

Toàn bộ log trong solution (đây là danh sách đầy đủ):

| Nơi | Mức | Nội dung | Sau khi log |
|---|---|---|---|
| `ExceptionHandlingMiddleware` | **Error** | `"Unhandled exception"` — **chỉ** nhánh 500 | trả ProblemDetails |
| `GeoapifyClient` | Warning | `"Geoapify {Operation} failed."` | **ném lại** |
| `SerperImageClient` | Warning | `"Serper image search failed for {Query}."` | **ném lại** |
| `SmtpEmailSender` | Warning | `"Failed to send email {Subject} over SMTP."` | **ném lại** |
| `SmtpEmailSender` | Debug | `"SMTP is not configured; skipping email {Subject}."` | bỏ qua (dev chưa cấu hình SMTP là bình thường) |
| `ResilientDistributedCache` | Warning | `"Distributed cache unavailable during {Operation}..."` | **nuốt** — degrade là đúng hợp đồng của nó |
| `JwtTokenGenerator` (×3) | Warning | token xác thực không đọc được / sai chữ ký / sai purpose claim | trả về không hợp lệ |
| `DestinationService` | Warning | `"Discarding a cache entry for {Key}..."` | coi như cache miss |

### Vì sao adapter phải ném lại chứ không nuốt

Vì caller cần **phân biệt** các trường hợp mà adapter không có đủ thông tin để phân biệt:

- `DestinationService` quyết định có cache kết quả hay không dựa trên việc provider
  **lỗi** hay **không tìm thấy gì**. Nếu `GeoapifyClient` nuốt lỗi và trả list rỗng,
  service sẽ cache "không có kết quả nào" trong 24 giờ chỉ vì mạng chập một giây.
- `AuthService` có guarantee "đăng ký vẫn thành công khi mail chết" — guarantee đó phải nằm ở
  tầng Application (có test khẳng định), không phải trốn trong adapter SMTP.

`ResilientDistributedCache` là **ngoại lệ duy nhất được phép nuốt**, vì degrade thành cache miss
chính là *hợp đồng* của nó.

### Chỉ một `ILogger` trong tầng Application

`DestinationService` là class duy nhất ở Application có logger, cho đúng **một** loại lỗi phát
sinh *tại tầng này* thay vì từ adapter: entry cache không còn khớp shape DTO — đó là lỗi với hợp
đồng JSON của chính dự án, không có adapter nào để đẩy vào.

### Structured logging

Mọi log dùng message template với placeholder có tên (`{Operation}`, `{Key}`, `{Type}`) chứ không
nội suy chuỗi. Nghĩa là nếu sau này gắn Seq/Application Insights, các trường này trở thành
thuộc tính query được, không phải text phẳng.

**Không log:**
- Cache key (chứa từ khoá người dùng nhập)
- Mật khẩu, token, chuỗi kết nối
- Message của exception 500 ra client khi không phải Development

---

## 9. Concurrency

### Chiến lược: unique index ở DB + catch/retry, không dùng lock ở tầng app

| Bất biến | Unique index | Ai xử lý xung đột |
|---|---|---|
| Email không trùng | `User.Email` | `AuthService.RegisterAsync` |
| Destination không trùng | `Destination.ProviderId` | `TripService.GetOrCreateDestinationAsync` |
| Một địa điểm chỉ 1 lần / ngày | `(ItineraryDayId, DestinationId)` | `TripService.SaveWithDuplicateGuardAsync` |

### Dịch lỗi ở đúng ranh giới

```csharp
// ApplicationDbContext.SaveChangesAsync
catch (DbUpdateException ex) when (IsUniqueViolation(ex))
{
    throw new ConcurrencyException("A concurrent write conflicted with this save.", ex);
}

private static bool IsUniqueViolation(DbUpdateException ex) =>
    ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
```

**Chỉ dịch unique violation.** Nếu dịch luôn cả lỗi độ dài chẳng hạn, caller sẽ báo cho user
"địa điểm này đã có trong lịch trình" — một câu hoàn toàn sai sự thật.

Nhờ vậy tầng Application **không bao giờ thấy** `DbUpdateException` của EF Core — đúng nguyên tắc
Clean Architecture: chi tiết công nghệ dừng lại ở Infrastructure.

### Hai lớp bảo vệ, một thông điệp

`TripService` kiểm tra trùng in-memory trước (`EnsureNotDuplicate`), unique index là lớp chặn cuối
cho race condition lọt giữa lúc kiểm tra và lúc save. **Cả hai lớp trả cùng một message**
(`DuplicateDestinationMessage`) để client không phân biệt được — chúng có cùng ý nghĩa nghiệp vụ.

`UpdateTripAsync` **cố tình save trực tiếp**, không dùng guard: đổi ngày không thể vi phạm index
đó, nên báo "duplicate destination" ở đây sẽ là nói dối.

### Chi tiết EF Core đáng nói

`BaseEntity` tự gán `Id = Guid.NewGuid()`, nên cả 5 entity configuration đều khai báo:

```csharp
builder.Property(x => x.Id).ValueGeneratedNever();
```

Thiếu dòng này, convention của EF coi Guid key là store-generated, và một entity **mới** đã có
sẵn key sẽ bị phân loại là row **đã tồn tại** → phát UPDATE thay vì INSERT → lỗi
`DbUpdateConcurrencyException`. Đây chính là thứ cho phép `TripService` chỉ cần
`trip.Days.Add(...)` rồi một lần `UpdateAsync`.

Migration `SetIdValueGeneratedNever` **cố ý rỗng** — nó chỉ mang thay đổi metadata vào
`ApplicationDbContextModelSnapshot`, thứ mà các migration sau diff với. Không được xoá.

---

## 10. Frontend gọi API như thế nào

### Cấu trúc

```
src/
  api/         client.ts (axios instance + interceptor + error helper)
               auth.ts, destinations.ts, trips.ts  ← wrapper có type
  auth/        AuthContext.tsx, ProtectedRoute.tsx
  features/    auth/ destinations/ trips/  ← page + component
  types.ts     type dùng chung cho mọi DTO
  App.tsx      định nghĩa route
```

### Quy tắc số 1: `src/api/` là nơi DUY NHẤT được import `axios`

Component không bao giờ tự narrow error type. Chúng gọi hai helper được export:

```typescript
export function getErrorMessage(err: unknown, fallback: string): string {
  return axios.isAxiosError(err) ? (err.response?.data?.detail ?? fallback) : fallback;
}

export function getErrorStatus(err: unknown): number | undefined {
  return axios.isAxiosError(err) ? err.response?.status : undefined;
}
```

`getErrorStatus` dùng khi một mã cụ thể **đổi UI** chứ không chỉ đổi message — ví dụ 404 render
trạng thái "không tìm thấy", 403 ở LoginPage hiện nút gửi lại email xác thực.

Nhờ quy tắc này, đổi HTTP client sau này chỉ phải sửa một thư mục.

### Axios instance + hai interceptor

```typescript
export const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080/api',
  headers: { 'Content-Type': 'application/json' },
});

// REQUEST: tự gắn JWT
apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem(TOKEN_STORAGE_KEY);
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// RESPONSE: phát hiện session hết hạn
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (axios.isAxiosError(error) && error.response?.status === 401 && getToken()) {
      clearToken();
      window.dispatchEvent(new Event(AUTH_LOGOUT_EVENT));
    }
    return Promise.reject(error);
  },
);
```

**Chi tiết quan trọng: `&& getToken()`.** Chỉ request **đang mang token** mà bị 401 mới có nghĩa
"session hết hạn". Đăng nhập sai cũng trả 401 nhưng không có token nào để hết hạn — thiếu điều
kiện này, mỗi lần gõ sai mật khẩu sẽ kích hoạt luồng logout một cách vô nghĩa.

### Vì sao dùng custom event thay vì gọi thẳng

`client.ts` không thể import `AuthContext` (React) — sẽ tạo phụ thuộc vòng và trói tầng HTTP vào
React. Thay vào đó nó **phát event**, `AuthProvider` lắng nghe:

```typescript
useEffect(() => {
  window.addEventListener(AUTH_LOGOUT_EVENT, clearSession);
  return () => window.removeEventListener(AUTH_LOGOUT_EVENT, clearSession);
}, []);
```

Không có bước này thì `isAuthenticated` giữ `true` mãi mãi sau khi token hết hạn — user bị kẹt,
mọi request đều lỗi mà UI vẫn nghĩ đang đăng nhập.

### Wrapper có type, không có `fetch`/`axios` thô trong component

```typescript
export async function updateItineraryItem(
  tripId: string, itemId: string,
  itineraryDayId: string | null, sortOrder: number,
): Promise<TripDestination> {
  const { data } = await apiClient.put<TripDestination>(
    `/trips/${tripId}/destinations/${itemId}`, { itineraryDayId, sortOrder });
  return data;
}
```

Mọi shape dùng chung nằm ở `types.ts`, không khai báo interface inline rải rác trong component.

### Quản lý session

| Việc | Cách làm |
|---|---|
| Giữ session qua F5 | `localStorage['tripplanner.token']` + `['tripplanner.user']`, đọc trong initializer của `useState` |
| Đăng ký **không** tạo session | `register()` chỉ gọi API — phải xác thực email mới đăng nhập được |
| Bảo vệ route | `ProtectedRoute` redirect về `/login`, **nhớ** trang đích để sau khi login quay lại đúng chỗ |

### Vài kỹ thuật UI đáng nói

**Debounce + chống tự-search-lại (`CitySearchInput`):** debounce 300 ms. Khi user chọn một gợi ý,
component ghi label đã chọn vào **ref** (`pickedLabel`) để không tự tìm kiếm lại chính chuỗi vừa
ghi. Từng bị bug: để `pickedLabel` trong dependency array của effect làm việc chọn gợi ý bắn
**request thứ hai**, mở lại dropdown đè lên lựa chọn của user. Nó là `ref` chính vì lý do đó —
có test pin lại.

**Kéo thả với optimistic update + rollback (`TripDetailPage`):** cập nhật UI ngay khi thả, gọi
API nền, lỗi thì trả về trạng thái cũ.

**Filter Saved Places ẩn dòng bằng CSS class, không unmount:** để index vị trí khi drop vẫn khớp
mảng chưa lọc.

---

## 11. Testing

### Bốn tầng test

| Tầng | Kiểm gì | Công cụ |
|---|---|---|
| `*ServiceTests` | Business logic | EF InMemory (DB mới mỗi test) + Moq |
| `*ValidatorTests` | Lỗi rơi vào **property** nào, **message** chính xác, **biên** hai phía | `TestValidate` |
| `TripPlanner.WebApi.Tests` | Routing thật, `[Authorize]` thật, middleware map status, binding query string | `WebApplicationFactory<Program>` |
| Frontend | Hành vi người dùng | Vitest + React Testing Library (jsdom) |

```csharp
private static ApplicationDbContext CreateDb() =>
    new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options);
```

### Test pin cả những quyết định "cố ý KHÔNG làm"

Đây là phần đáng nói nhất — test không chỉ kiểm code chạy đúng, mà giữ cho **ý định thiết kế**
không bị vô tình phá:

| Test | Bảo vệ điều gì |
|---|---|
| `Login_WithAPasswordShorterThanRegisterAllows_IsStillValid` | Thêm rule độ dài vào login sẽ khoá tài khoản cũ |
| `UpdateTrip_WithEndBeforeStart_IsNotTheValidatorsJob` | Đó là quy tắc domain (`Trip.SetDates`), không phải việc của validator |
| `UpdateItem_WithAPositionPastTheEndOfTheBucket_IsValid` | `TripService` clamp — "99" nghĩa là "cuối cùng" |
| `DestinationConfigurationTests` | Không ai được thêm lại `HasMaxLength` cho `ProviderId` |
| `DestinationsEndpointsTests` | Endpoint destinations phải giữ **công khai** |
| `TestHostConfigurationTests` | Test host không bao giờ ký token bằng key thật của dev |

`DestinationConfigurationTests` đặc biệt: EF InMemory **bỏ qua** `HasMaxLength`, nên không test
hành vi nào bắt được regression này — test đó pin sự vắng mặt qua **model metadata**.

### Hai bài học đã trả giá (frontend)

1. **Test mà assertion duy nhất là `expect(...).not.toThrow()` thì không bao giờ fail được** —
   React 18 biến setState trên component đã unmount thành no-op im lặng. Test cleanup listener
   giờ assert vào cặp `addEventListener` / `removeEventListener`.
2. **Test chỉ kiểm tra thứ *vắng mặt* vẫn pass ngon lành khi trang crash.** Mỗi assertion "không
   có X" phải đi kèm một assertion khẳng định "có Y".

### Vài quyết định setup

- **`globals: false`** trong Vitest: test phải `import { it, expect } from 'vitest'`, đổi lại
  `npm run lint` type-check luôn file test — mock sai chữ ký làm gãy build thay vì pass im lặng.
- Vì tắt globals nên RTL không tự đăng ký được `afterEach` — `setup.ts` gọi `cleanup()` **và**
  `localStorage.clear()` thủ công (`AuthProvider` đọc localStorage lúc render đầu, session sót
  lại sẽ rò sang test sau).
- Assertion phụ thuộc ngày (status pill) viết **tương đối so với hôm nay** qua helper offset, để
  không tự hỏng vào một ngày trong tương lai.
- `CustomWebApplicationFactory` sinh tên InMemory database **một lần rồi capture vào field** —
  `AddDbContext` có `optionsLifetime` mặc định là **Scoped**, nên viết `Guid.NewGuid()` trực tiếp
  trong lambda sẽ khiến mỗi DI scope có một database rỗng riêng.

---

## 12. Configuration & secrets

**Không có `appsettings.json`.** Mọi cấu hình hoặc là default C# trong settings class, hoặc là
override trong `.env` (git-ignored), nạp bằng `DotNetEnv.Env.Load()` **trước**
`WebApplication.CreateBuilder`.

Key lồng nhau dùng `__` (`Geoapify__ApiKey` → `Geoapify:ApiKey`), vì giá trị `.env` trở thành
biến môi trường và `AddEnvironmentVariables()` coi `__` là dấu phân cách section.

| Giá trị | Bắt buộc? | Nếu thiếu |
|---|---|---|
| `Jwt__Key` | **Bắt buộc** | **Host từ chối khởi động** (`.Validate().ValidateOnStart()`) |
| `ConnectionStrings__Postgres` | **Bắt buộc** | Không kết nối được |
| Geoapify/Serper API key, SMTP | Bắt buộc để tính năng đó chạy | Chuỗi rỗng |
| `Jwt__Issuer/Audience/ExpiryMinutes` | Không | Default trong `JwtSettings.cs` |
| CORS origin, base URL | Không | `?? "localhost default"` trong code |

**Vì sao `Jwt__Key` fail-at-startup còn URL thì fallback:** một signing key mặc định sẽ **vô hiệu
hoá hoàn toàn** JWT security — mọi người đều biết key thì ai cũng ký được token. Một URL sai chỉ
làm tính năng đó không chạy. Rủi ro khác nhau nên cách xử lý khác nhau.

Validation nằm trong phần đăng ký options, **không** trong `Program.cs`: đọc
`builder.Configuration[...]` sớm sẽ chạy trước khi test host kịp cung cấp giá trị của nó.

---

## 13. Q&A — câu reviewer có thể hỏi

### "Sao không dùng MediatR / CQRS?"

Cân nhắc rồi và quyết định không. MediatR có giá trị khi cần pipeline behavior (logging,
validation, transaction) áp cho hàng chục handler, hoặc khi đường đọc và đường ghi có mô hình dữ
liệu khác nhau. Ở đây có 3 service với ~14 method, đọc/ghi dùng chung entity, và validation đã có
một dòng nhất quán ở đầu mỗi method. Thêm MediatR nghĩa là mỗi thao tác thành 2 class
(Command + Handler) và mất khả năng "nhảy tới định nghĩa" — trả giá về khả năng đọc hiểu mà không
nhận lại gì. **Điều gì sẽ đảo ngược:** khi cần cross-cutting behavior thứ ba trở lên áp cho mọi
handler, hoặc khi tách đường đọc sang read model riêng.

### "Sao repository không có Unit of Work? Sao mỗi method tự SaveChanges?"

Vì mỗi request trong hệ này thao tác đúng một aggregate. Không có kịch bản nào cần gom nhiều
thay đổi vào một transaction do tầng trên điều khiển. Ngay cả `UpdateItineraryItemAsync` — thao
tác chạm hai bucket — vẫn nằm trong một aggregate `Trip`, và code **cố ý gọi một lần
`UpdateAsync`** để hai bucket đổi nguyên tử. Unit of Work ở đây sẽ là abstraction không ai dùng.
**Điều gì sẽ đảo ngược:** một use case ghi hai aggregate khác nhau phải cùng thành công/thất bại.

### "Sao không dùng AutoMapper?"

Mapping viết tay trong `AuthMappings` / `TripMappings` / `DestinationMappings`. Mapping viết tay
được compiler kiểm tra: đổi tên property thì gãy lúc build, còn AutoMapper gãy lúc chạy (hoặc âm
thầm map thành null). Số DTO ở đây đủ nhỏ để không cần đánh đổi đó.

### "Sao validator không đăng ký vào DI?"

Chúng stateless và không có dependency. Inject `IValidator<T>` sẽ thành 10 interface mà mỗi cái
đúng một implementation, không ai thay thế bao giờ — DI để làm gì khi không có gì để hoán đổi?
Test cũng khởi tạo trực tiếp chính các class đó. **Điều gì sẽ đảo ngược:** một validator cần
dependency (rule async truy vấn DB) — khi đó chuyển **riêng** validator đó thành constructor
parameter.

### "Sao trip của người khác trả 404 mà không phải 403?"

403 xác nhận tài nguyên **có tồn tại** — kẻ tấn công có thể quét id để lập bản đồ dữ liệu người
khác. Query lọc theo `userId` AND `tripId`, nên "của người khác" và "không tồn tại" trả về cùng
kết quả rỗng, không thể phân biệt.

### "Cache invalidate như thế nào?"

Không invalidate chủ động. Dữ liệu POI gần như tĩnh (tên và toạ độ một địa danh không đổi), nên
TTL theo thời gian là đủ: locations 24h, attractions 6h, details 24h. Entry được giữ 7 ngày để
làm nguồn fallback khi provider sập. Đây là dữ liệu **chỉ đọc từ hệ khác** — mình không sở hữu
nên cũng không có sự kiện nào để invalidate theo.

### "Nếu Redis chết thì sao?"

App vẫn chạy. `ResilientDistributedCache` bắt `RedisConnectionException` / `RedisTimeoutException`
/ `TimeoutException`, ghi Warning, và trả về như một cache miss. Mọi request rơi thẳng xuống
Geoapify — chậm hơn, nhưng không lỗi. Đây cũng là nơi **duy nhất** trong solution biết tới
exception type của Redis.

### "Nếu Geoapify chết thì sao?"

Ba tình huống: (1) còn entry trong retention 7 ngày → trả dữ liệu cũ; (2) không còn entry → lỗi
nổi lên thành response lỗi; (3) riêng đường details còn fallback thêm: tra `Destination` trong DB
của mình, vì địa điểm đã lưu trong trip phải xem được kể cả khi provider quên nó. Chỉ khi trượt
**cả hai** nguồn mới là 404.

### "Tại sao cache lại tự kiểm tra TTL thay vì để cache tự evict?"

Vì cần phân biệt ba trạng thái chứ không phải hai: *tươi*, *cũ nhưng dùng được*, *không có*.
Để cache evict theo TTL thì entry hết hạn biến mất, và đúng lúc provider sập lại không còn gì để
fallback. Bọc trong `CacheEnvelope(Value, FetchedAt)` với retention 7 ngày giải quyết chuyện đó.

### "Đổi ngày trip thì lịch trình đã xếp có mất không?"

Không. `RegenerateDays` giữ nguyên ngày còn nằm trong range cùng toàn bộ item của nó. Ngày rơi ra
ngoài mới bị xoá, và item của nó **quay về Saved Places** chứ không bị xoá theo (DB cấu hình
`SetNull`, và code mirror hành vi đó vào in-memory để DTO trả về đã đúng ngay).

### "Sao endpoint destinations không cần đăng nhập?"

Có chủ ý — người dùng phải duyệt được điểm đến trước khi tạo tài khoản, nếu không thì không có lý
do gì để đăng ký. Có test riêng pin điều này vì thêm nhầm `[Authorize]` vào đó sẽ không làm đỏ
bất kỳ test nào khác.

### "Xử lý race condition thế nào?"

Unique index ở DB làm nguồn sự thật, không dùng lock ở tầng app. `SaveChangesAsync` dịch lỗi
Postgres unique-violation thành `ConcurrencyException`; service bắt cái đó rồi fetch lại hoặc báo
lỗi. Kiểm tra in-memory là lớp một (thân thiện, cho message đẹp), index là lớp chặn cuối. **Chỉ**
dịch unique violation — dịch cả lỗi khác sẽ báo sai nguyên nhân cho user.

### "Test có chạm database thật không?"

Không. Backend dùng EF Core InMemory, **tên database mới cho mỗi test** để không rò trạng thái.
Đánh đổi đã biết: InMemory không enforce unique index, nên đoạn dịch unique-violation không có
test tự động — được bù bằng cách mock `ConcurrencyException` ở ranh giới repository để test phía
caller.

### "Mật khẩu lưu thế nào?"

BCrypt qua `BCrypt.Net-Next`, sau interface `IPasswordHasher`. Salt do BCrypt tự sinh và nhúng
trong chuỗi hash. Không bao giờ log, không bao giờ trả ra DTO.

### "Vì sao chỉ nhánh 500 mới log Error?"

404 / 409 / 400 là hoạt động **bình thường** của một API — user gõ sai id, thêm trùng địa điểm.
Log chúng ở mức Error sẽ làm nhiễu tới mức không ai còn đọc log nữa, và cảnh báo thật bị chôn vùi.

### "Sao dùng custom event cho luồng logout thay vì gọi thẳng?"

`client.ts` không được phép biết React. Import `AuthContext` vào đó sẽ tạo phụ thuộc vòng và trói
tầng HTTP vào framework UI. Phát `window` event giữ hai bên tách rời — thêm listener thứ hai sau
này cũng không phải sửa `client.ts`.

### "Nếu phải scale nhiều instance thì sao?"

JWT là stateless nên auth scale sẵn. Cache đổi sang Redis chỉ bằng một biến `.env` — đó là lý do
`DestinationService` phụ thuộc `IDistributedCache` chứ không phải `IMemoryCache`. Điểm cần chú ý
khi scale: `SortOrder` không có unique index (xem phần Hạn chế).

---

## 14. Hạn chế đã biết

Chủ động nêu ra sẽ tốt hơn nhiều so với để reviewer tự tìm thấy.

### 1. Lỗi validation theo field chưa hiển thị trên UI

API trả `errors: { "Password": [...] }`, nhưng `getErrorMessage` chỉ đọc `detail` — mà `detail`
của `ValidationException` là câu chung `"One or more validation errors occurred."`. Backend đã
sẵn sàng, chỉ thiếu một helper `getFieldErrors(err)` ở `client.ts` và phần render dưới mỗi input.

### 2. Hai lỗ hổng concurrency được chấp nhận có cân nhắc

- Postgres coi **mỗi NULL là khác nhau**, nên `(ItineraryDayId, DestinationId)` **không phủ**
  Saved Places (`ItineraryDayId IS NULL`). Hai request đồng thời thêm cùng một địa điểm vào Saved
  Places của một trip đều có thể thành công. Đã viết filtered unique index rồi **cố ý revert** vì
  over-engineering ở quy mô này. Hậu quả tối đa: một dòng trùng trong trip của chính user đó.
- `ItineraryItem.SortOrder` **không có** unique index — thứ tự được đảm bảo hoàn toàn in-memory
  bằng resequence dày đặc mỗi request, nên hai thao tác reorder đồng thời trên cùng bucket sẽ race.

### 3. Đoạn dịch unique-violation không có test tự động

EF InMemory không enforce unique index, nên `dotnet test` không thể chạm tới nhánh đó. Phải suy
luận trực tiếp trên provider SQL. Bù lại bằng cách mock `ConcurrencyException` ở ranh giới
repository để test phần **caller** (xem `AuthServiceTests`).

### 4. Migration chạy tự động lúc khởi động

Tiện cho môi trường học tập/dev, nhưng ở production nên tách thành bước deploy riêng — hai
instance khởi động cùng lúc có thể cùng chạy migration.

### 5. Chưa có refresh token

Access token hết hạn (mặc định 60 phút) thì user phải đăng nhập lại. Frontend đã xử lý chuyện này
tử tế (interceptor phát hiện 401 → tự logout → redirect), nhưng chưa có luồng gia hạn im lặng.

