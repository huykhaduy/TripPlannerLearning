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
9. [Concurrency & chi tiết EF Core](#9-concurrency--chi-tiết-ef-core)
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
| Cache | `IStaleTolerantCache` (port) trên `IDistributedCache` — in-process (mặc định) hoặc Redis |
| API ngoài | Geoapify (địa điểm/POI), Serper (tìm ảnh), SMTP (email xác thực) |
| Frontend | React + TypeScript + Vite + React Router + Axios |
| Test | xUnit + Moq + EF InMemory + `WebApplicationFactory`; Vitest + React Testing Library |

**Quy mô test:** 371 case backend (302 `TripPlanner.UnitTests` + 69 `TripPlanner.WebApi.Tests`),
263 case frontend (28 file) — tất cả đều pass. Chạy `dotnet test` và `npm test` để xác nhận lại
trước khi trình bày.

`TripPlanner.UnitTests` cố ý **không** soi gương 1-1 với `src/`: một project phủ cả Domain,
Application **và** Infrastructure, vì cả ba chạy in-process và dùng chung fixture. Chia project
theo *thứ cần để chạy* (có host hay không), không theo tên tầng.

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
| `TripPlanner.Application` | Domain | FluentValidation, `DependencyInjection.Abstractions` |
| `TripPlanner.Infrastructure` | Application | EF Core, Npgsql, BCrypt, StackExchange.Redis, JWT |
| `TripPlanner.WebApi` | Application + Infrastructure | ASP.NET Core, Swagger, DotNetEnv |

Điểm mạnh nhất: **Domain không có một `PackageReference` nào**. Nếu vô tình viết
`using Microsoft.EntityFrameworkCore;` trong `AuthService.cs`, project **không build được** —
kiến trúc được compiler ép buộc chứ không phụ thuộc kỷ luật lập trình viên.

Application chỉ còn **hai** package. `Caching.Abstractions` và `Logging.Abstractions` từng có mặt,
và cả hai biến mất khi cơ chế cache được đẩy xuống Infrastructure (mục 7) — một cách kiểm tra
"tầng này có bị rò công nghệ không" mà không cần đọc code: cứ nhìn file `.csproj`.

### Domain không chỉ chứa dữ liệu — nó giữ quy tắc của chính nó

`Trip` là aggregate root của cả ba entity `Trip` / `ItineraryDay` / `ItineraryItem`. Mọi quy tắc về
*ngày nào tồn tại*, *item nằm bucket nào*, *thứ tự ra sao* đều nằm trên `Trip`:

| Method | Giữ bất biến gì |
|---|---|
| `SetDates` | start ≤ end, **và** Days luôn khớp khoảng ngày |
| `MoveItem` | SortOrder liền mạch 0..n ở cả hai bucket bị ảnh hưởng |
| `NextSortOrderIn` | item mới luôn rơi xuống cuối bucket |
| `HasDestinationIn` | một địa điểm xuất hiện tối đa một lần mỗi bucket |

`StartDate`/`EndDate` để `private set`, nên **không có đường nào** đổi ngày mà lách được
validation. Toàn bộ các method này thuần tuý (không I/O), nên `TripTests` kiểm chúng không cần
mock lẫn DbContext.

### Dependency Inversion — Application định nghĩa, Infrastructure thực thi

Infrastructure tham chiếu Application (không phải ngược lại), nhưng lúc chạy Application mới là
bên gọi database. Được như vậy vì Application chỉ khai báo **cái nó cần**:

| Interface (Application) | Implementation (Infrastructure) |
|---|---|
| `IUserRepository`, `ITripRepository`, `IDestinationRepository` | EF Core repositories |
| `IPasswordHasher` | `BCryptPasswordHasher` |
| `IJwtTokenGenerator` | `JwtTokenGenerator` |
| `IDestinationProvider` | `GeoapifyClient` |
| `IStaleTolerantCache` | `DistributedStaleTolerantCache` |
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
trừu tượng không ai dùng tới — nhất là khi `DbContext` **bản thân nó đã là** một Unit of Work
(gom thay đổi, rồi `SaveChangesAsync` đẩy xuống trong một transaction). Bọc thêm một lớp nữa lên
trên là bọc UoW bằng UoW.

**Đánh đổi phải nói thẳng:** `UpdateAsync(trip)` **không dùng** tham số của nó. Change tracker
của EF đã giữ sẵn entity từ lúc query, nên hàm chỉ cần flush; tham số ở đó để chỗ gọi đọc thành
"lưu trip này" thay vì một `SaveChanges()` trơ trọi. Hệ quả là hai điều kiện ẩn, đã được ghi rõ
trong doc comment của cả interface lẫn implementation:

1. Chỉ đúng với entity nạp bằng query **có tracking** (`GetForUpdateAsync`). Đưa vào một trip lấy
   từ `GetDetailsAsync` (`AsNoTracking`) thì **không có gì được lưu — và cũng không có lỗi nào**.
2. Nó flush **mọi thứ** DbContext đang track, không riêng trip này (mọi repository dùng chung một
   context scoped). Đó chính là lý do `TripService.AddDestinationAsync` phải insert `Destination`
   **trước** khi chạm vào graph của trip — xem ghi chú ORDER MATTERS ở đó.

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

Đây là thuật toán khó nhất trong project, và nó nằm **trong Domain** (`Trip.SetDates` →
`Trip.RegenerateDays`), không phải trong service:

```
PUT /api/trips/{tripId}  { name, startDate, endDate }
   ▼
1. validator (tên bắt buộc)
2. Trip.SetDates(start, end)  ← MỘT lời gọi, làm hai việc không tách rời:
     a. quy tắc DOMAIN: start ≤ end, sai thì DomainException → 400 (ném TRƯỚC khi sửa gì)
     b. sinh lại days cho range mới:
        • ngày CÒN nằm trong range   → GIỮ NGUYÊN, giữ luôn các item đã xếp
        • ngày RƠI RA ngoài range    → xoá; item của nó QUAY VỀ Saved Places (không mất)
        • ngày MỚI trong range       → tạo mới
        • cuối cùng: đánh số lại theo thứ tự thời gian
3. UpdateAsync (lưu 1 lần)
```

Không xoá sạch rồi tạo lại — làm vậy sẽ mất hết lịch trình user đã xếp chỉ vì lùi ngày về 1 hôm.
Code còn **mirror cascade `SetNull` của DB vào in-memory**, để DTO trả về đã hiển thị đúng
"item quay lại Saved Places" ngay, không cần load lại.

**Vì sao gộp vào `SetDates` chứ không để service gọi hai bước:** nếu tách, mọi caller đều phải nhớ
gọi bước 2 — quên một lần là trip có ngày không khớp với khoảng ngày của nó. Gộp lại thì bất biến
"Days luôn khớp date range" trở thành **không thể phá vỡ từ bên ngoài**. `StartDate`/`EndDate` cũng
để `private set` vì lý do đó: không có đường nào đổi ngày mà lách được validation.

### 3.4. Kéo thả sắp xếp lịch trình

```
PUT /api/trips/{tripId}/destinations/{itemId}  { itineraryDayId, sortOrder }
   ▼ TripService.UpdateItineraryItemAsync   ← điều phối
       - itineraryDayId = null nghĩa là "Saved Places"
       - Trip.HasDestinationIn(..., excludeItemId: itemId)   ← DOMAIN trả lời "có trùng không"
         ← item được miễn tự-đối-chiếu, nên kéo thả trong cùng 1 ngày vẫn hợp lệ
         ← service mới là chỗ ném ConflictException → 409 (409 là từ vựng của tầng HTTP)
       - Trip.MoveItem: chèn vào vị trí, sortOrder được CLAMP → gửi 99 nghĩa là "cuối cùng"
         ← Resequence CẢ HAI bucket về 0..n → giá trị luôn liền mạch, không có khoảng trống
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

### Tách CHÍNH SÁCH khỏi CƠ CHẾ

Đây là điểm đáng nói nhất khi thuyết trình. Hai thứ trước đây nằm chung trong
`DestinationService` giờ ở hai tầng khác nhau, vì chúng thay đổi vì những lý do khác nhau:

| | Là gì | Ở đâu |
|---|---|---|
| **Chính sách** | TTL bao lâu, khi nào chấp nhận dữ liệu cũ | Application — `DestinationService` |
| **Cơ chế** | JSON, `byte[]`, expiry options, retention 7 ngày | Infrastructure — `DistributedStaleTolerantCache` |

Ranh giới là port `IStaleTolerantCache`: *"cất giá trị kèm thời điểm lấy, và trả lại bất kể nó cũ
đến đâu — cũ bao nhiêu là chấp nhận được thì người gọi tự quyết"*.

```csharp
public interface IStaleTolerantCache
{
    Task<CacheEnvelope<T>?> TryGetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, CacheEnvelope<T> envelope, CancellationToken ct = default);
}

public sealed record CacheEnvelope<T>(T Value, DateTimeOffset FetchedAt);
```

Kết quả cụ thể, kiểm chứng được: **Application không còn tham chiếu `System.Text.Json` lẫn
`Microsoft.Extensions.Caching.Abstractions`** — cả hai package đã bị gỡ khỏi
`TripPlanner.Application.csproj`.

### Thiết kế cốt lõi: "stale-better-than-down"

Không dùng TTL của cache backend để evict. Toàn bộ chính sách nằm gọn trong một hàm ở Application:

```csharp
private async Task<T> GetCachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> fetchAsync, CancellationToken ct)
{
    var stale = await _cache.TryGetAsync<T>(key, ct);

    // 1. HIT còn tươi → trả luôn, không gọi provider
    if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < ttl)
        return stale.Value;

    try
    {
        // 2. MISS hoặc hết hạn → gọi provider, cache lại
        var value = await fetchAsync();
        await _cache.SetAsync(key, new CacheEnvelope<T>(value, _clock.GetUtcNow()), ct);
        return value;
    }
    catch (ExternalServiceUnavailableException) when (stale is not null)
    {
        // 3. Provider CHẾT nhưng còn entry cũ → trả dữ liệu cũ còn hơn trả lỗi
        return stale.Value;
    }
}
```

Chú ý mệnh đề `catch`: chỉ bắt **đúng một** type do adapter dịch ra. Trước đây chỗ này bắt theo
một predicate ba-loại-exception (`HttpRequestException`/`TaskCanceledException`/`JsonException`)
bị copy sang cả `AuthService` và `TripService` — nghĩa là Application phải biết provider nói
giao thức gì.

**Vì sao tự kiểm tra freshness thay vì để cache tự evict:** nếu để cache evict theo TTL, entry
hết hạn sẽ **biến mất** — lúc Geoapify sập thì không còn gì để fallback. Giữ entry 7 ngày và tự
so `FetchedAt` với TTL cho phép phân biệt ba trạng thái: *tươi*, *cũ nhưng dùng được*, và
*không có*.

`_clock` là `TimeProvider` được inject (đăng ký `TimeProvider.System` trong `AddApplication()`),
nên test có thể tua thời gian để kiểm tra logic hết hạn mà không cần `Thread.Sleep`.

### Bốn tầng chống lỗi

```
DestinationService                  ← tầng 4: provider chết → trả stale
   │ IStaleTolerantCache (port của Application)
   ▼
DistributedStaleTolerantCache       ← tầng 3: entry lệch shape → coi như miss
   │ IDistributedCache (abstraction)
   ▼
ResilientDistributedCache           ← tầng 2: Redis chết → coi như cache miss
   │ IDistributedCache (thật)
   ▼
MemoryDistributedCache / RedisCache ← tầng 1: backend chọn qua config
```

Thứ tự này có chủ đích: tầng 3 nằm **trên** tầng 2, nên khi request tới được chỗ deserialize thì
lỗi kết nối backend đã bị biến thành "miss" rồi. Nhờ vậy `DistributedStaleTolerantCache` chỉ phải
lo đúng một loại lỗi: payload đọc không ra.

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
    logger.LogWarning(ex, "Discarding a cache entry that no longer matches {Type}.", typeof(T).Name);
    return null;   // coi như miss
}
```

Coi như miss (để request không bị 500), nhưng **có log** — nếu không thì hiện tượng này trông
hệt như "cache tự dưng ngừng hoạt động". Lưu ý: **không** áp dụng cách xử lý này lúc *ghi* —
serialize lỗi khi ghi là bug thật, phải để nó nổi lên.

Message log **không kèm cache key**, đúng với chính sách ở mục 8: key nhúng từ khoá người dùng
nhập. Trước khi tách adapter, chỗ này log cả key — mâu thuẫn với chính `ResilientDistributedCache`
ngay bên cạnh, vốn ghi rõ là không log key.

Nhánh này trước đây **không có test nào**: `DestinationService` chỉ từng được truyền
`NullLogger`, nên cái Warning — vốn là toàn bộ hành vi nhìn thấy được của nó — chưa bao giờ được
assert. Sau khi chuyển sang Infrastructure, `DistributedStaleTolerantCacheTests` phủ 8 case, gồm
cả retention 7 ngày (thứ khiến stale-better-than-down khả thi) và việc lỗi serialize khi ghi
**không** bị nuốt.

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
| `DistributedStaleTolerantCache` | Warning | `"Discarding a cache entry that no longer matches {Type}."` — **không kèm key** | coi như cache miss |

### Vì sao adapter phải ném lại chứ không nuốt

Vì caller cần **phân biệt** các trường hợp mà adapter không có đủ thông tin để phân biệt:

- `DestinationService` quyết định có cache kết quả hay không dựa trên việc provider
  **lỗi** hay **không tìm thấy gì**. Nếu `GeoapifyClient` nuốt lỗi và trả list rỗng,
  service sẽ cache "không có kết quả nào" trong 24 giờ chỉ vì mạng chập một giây.
- `AuthService` có guarantee "đăng ký vẫn thành công khi mail chết" — guarantee đó phải nằm ở
  tầng Application (có test khẳng định), không phải trốn trong adapter SMTP.

`ResilientDistributedCache` là **ngoại lệ duy nhất được phép nuốt**, vì degrade thành cache miss
chính là *hợp đồng* của nó.

### Không còn `ILogger` nào trong tầng Application

Trước đây `DestinationService` là class duy nhất ở Application có logger, cho đúng một loại lỗi
phát sinh *tại tầng này*: entry cache không còn khớp shape DTO. Sau khi tách cơ chế cache, lỗi đó
phát sinh trong adapter (`DistributedStaleTolerantCache`) và logger đi theo nó.

Kết quả: **Application không còn logger nào cả**, và `Microsoft.Extensions.Logging.Abstractions`
đã bị gỡ khỏi `TripPlanner.Application.csproj`. Đây là một quy tắc dễ kiểm: nếu bạn thấy cần
`ILogger` trong Application, gần như chắc chắn lỗi đó thuộc về một adapter.

### Structured logging

Mọi log dùng message template với placeholder có tên (`{Operation}`, `{Key}`, `{Type}`) chứ không
nội suy chuỗi. Nghĩa là nếu sau này gắn Seq/Application Insights, các trường này trở thành
thuộc tính query được, không phải text phẳng.

**Không log:**
- Cache key (chứa từ khoá người dùng nhập)
- Mật khẩu, token, chuỗi kết nối
- Message của exception 500 ra client khi không phải Development

---

## 9. Concurrency & chi tiết EF Core

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

### `AsSplitQuery` — cartesian explosion và cách EF Core sinh SQL

Đây là phần đáng đào sâu nhất về EF Core trong dự án, vì nó là một cái bẫy mà tên gọi không hề
gợi ra, và nhìn code thì không thấy gì bất thường.

#### Gốc rễ: SQL chỉ biết trả về bảng phẳng

`Trip` có **hai** collection — `Days` và `Items`. Query nạp chi tiết trip cần cả hai:

```csharp
.Include(t => t.Days).ThenInclude(d => d.Items).ThenInclude(i => i.Destination)
.Include(t => t.Items).ThenInclude(i => i.Destination)
```

Mặc định EF gom tất cả vào **một câu SQL**. Nhưng SQL chỉ trả về được một bảng phẳng, không trả về
được cấu trúc lồng nhau. Nên nó buộc phải `LEFT JOIN` từng nhánh vào dòng trip:

```sql
FROM Trips t
LEFT JOIN (ItineraryDays ⋈ ItineraryItems ⋈ Destinations) AS s0 ON t.Id = s0.TripId
LEFT JOIN (ItineraryItems ⋈ Destinations)                 AS s1 ON t.Id = s1.TripId
```

`s0` và `s1` **không có điều kiện nào ràng buộc lẫn nhau** — cả hai chỉ nối vào `t`. Database vì
thế phải ghép **mọi dòng của `s0` với mọi dòng của `s1`**. Đó là tích Descartes, và tên gọi của
hiện tượng này là *cartesian explosion*.

Thử một trip tí hon để đếm tay: 2 ngày, 3 địa điểm (ngày 1 có A và B, ngày 2 có C).

| | `s1` = A | `s1` = B | `s1` = C |
|---|---|---|---|
| `s0` = (ngày 1, A) | ✓ | ✓ | ✓ |
| `s0` = (ngày 1, B) | ✓ | ✓ | ✓ |
| `s0` = (ngày 2, C) | ✓ | ✓ | ✓ |

**9 dòng** cho dữ liệu thật chỉ gồm 1 trip + 2 ngày + 3 item. Và con số 9 chưa phải chỗ tốn nhất:
**mỗi dòng trong 9 dòng đó chở theo toàn bộ cột** của trip, của ngày, của item *và* của
destination — tức cả `Address`, `Description`, `ImageUrl`, `Website`, `OpeningHours`. Cùng một
destination được gửi qua dây nhiều lần.

#### Vì sao là bậc hai theo item, không phải `ngày × item`

Trực giác ban đầu hay đoán `days × items`. Đếm kỹ thì không phải:

- `s0` = một dòng cho **mỗi cặp (ngày, item trong ngày đó)**, cộng một dòng cho mỗi **ngày trống**
  (`LEFT JOIN` giữ lại ngày không có item). Nếu mọi item đều đã xếp lịch thì `s0` ≈ số item.
- `s1` = **mọi item** của trip.

Nhân lại: `(items + ngày trống) × items` — **số ngày gần như không ảnh hưởng, số item mới là thủ
phạm**. Bảng đo bên dưới xác nhận: 14 ngày/60 item và 30 ngày/60 item đều ra đúng 3 600 dòng.

#### Hai nhánh `Include` trùng nhau — và vì sao vẫn giữ

Nhìn kỹ sẽ thấy hai nhánh chồng lên nhau. Nguyên nhân nằm ở model: `ItineraryItem` mang **cả hai**
khoá.

```csharp
public Guid  TripId         { get; set; }   // luôn có
public Guid? ItineraryDayId { get; set; }   // null = Saved Places
```

Nên `day.Items` là một **tập con** của `trip.Items`:

| | Nhánh `Days → Items → Destinations` | Nhánh `Items → Destinations` |
|---|---|---|
| Dòng `ItineraryDays` | ✅ chỉ nhánh này có | — |
| Dòng `ItineraryItems` | chỉ item **đã xếp lịch** | **mọi** item |
| Dòng `Destinations` | của các item đã xếp | của mọi item |

Thứ duy nhất nhánh đầu đóng góp riêng là **bản thân các dòng `ItineraryDays`** (`Date`,
`DayNumber`). Phần item và destination trong nó là dữ liệu lấy lại lần hai. EF không tự nhận ra
điều đó vì ta khai báo hai đường điều hướng riêng biệt, và nó dịch từng đường một — nó không suy
luận "hai đường này cùng về một bảng, gộp đi".

Vẫn giữ nhánh trùng vì `TripMappings.ToDayDto` đang đọc `day.Items` để dựng DTO cho từng ngày.

**Cái bẫy cần nhớ:** bỏ nhánh trùng đi **không** chữa được cartesian explosion. Còn lại
`Include(t => t.Days)` và `Include(t => t.Items)` thì vẫn là **hai collection cùng cấp** → vẫn
tích chéo `days × items`. Bỏ nhánh trùng chỉ giúp bớt dữ liệu lặp, không thay bản chất.

#### `AsSplitQuery` làm gì

Nó bảo EF: đừng nhồi vào một câu, **tách mỗi collection thành một câu riêng**.

```
Câu 1:  SELECT trip                          →  1 dòng
Câu 2:  SELECT days + items + destinations   →  3 dòng
Câu 3:  SELECT items + destinations          →  3 dòng
```

7 dòng thay vì 9, và không dòng nào lặp lại cột của trip. EF **tự ghép** ba kết quả lại trong bộ
nhớ thành đúng một object `Trip` với `Days` và `Items` đầy đủ — **code gọi không thấy khác biệt
gì**. Chính vì không thấy khác biệt nên benchmark phải kiểm cột "same graph": một query nhanh hơn
mà trả về khác dữ liệu thì vô giá trị.

Bản chất của lựa chọn: **đổi ít round trip lấy nhiều dữ liệu lặp, hoặc ngược lại.**

#### Một hệ quả tinh tế: identity resolution

`AsNoTracking()` **không** làm identity resolution. Nghĩa là cùng một `ItineraryItem` sẽ được tạo
thành **hai object khác nhau** trong bộ nhớ — một nằm trong `day.Items`, một nằm trong
`trip.Items`. Ở `GetDetailsAsync` điều này vô hại (mapping đọc mỗi bucket từ một nguồn riêng), chỉ
tốn bộ nhớ.

Nhưng `GetForUpdateAsync` thì **có tracking**, nên EF gộp về **một** object duy nhất. Đó là điều
kiện để `Trip.MoveItem` sửa một item trong `trip.Items` mà `day.Items` cũng thấy thay đổi ngay.
Nếu ai đó thêm `AsNoTracking()` vào `GetForUpdateAsync` cho "nhẹ hơn", các đường ghi sẽ hỏng theo
kiểu rất khó lần ra.

#### Muốn xuống một câu mà không có tích chéo thì phải làm gì

Chỉ có một cách: bỏ `Include` mà dùng **projection** — `Select` thẳng ra DTO. Khi đó EF chỉ kéo
đúng các cột cần và tự sinh subquery cho từng collection, không tích chéo.

Đổi lại phải viết tay hình dạng projection, và kết quả **không còn là entity** nên `TripService`
không gọi được `Trip.MoveItem` lên nó. Vì vậy cách này chỉ hợp cho đường **đọc**
(`GetDetailsAsync`), không dùng được cho `GetForUpdateAsync`. Dự án giữ `Include` cho cả hai để
hai đường đọc chung một hình dạng query.

#### Quy tắc nhớ

> Chỉ cần nghĩ tới `AsSplitQuery` khi query có **từ hai `Include` trỏ tới collection trở lên, ở
> cùng một cấp**.

Một chuỗi `Include` dài nhưng **đơn tuyến** (`Trip → Items → Destination`) thì **không** dính, vì
mỗi item chỉ có một destination — quan hệ một-một không sinh tích chéo. Nó chỉ nổ khi có hai nhánh
song song cùng treo trên một gốc.

Trong repo này chỉ có đúng hai query rơi vào trường hợp đó, cả hai đều ở `TripRepository`.

#### Bằng chứng ở tầng SQL

Lấy bằng `ToQueryString()` nên không cần database chạy: câu gốc **5 JOIN / 2 757 ký tự**, sau khi
split thì câu đầu còn **0 JOIN / 501 ký tự**.

### Số đo thật trên Postgres

Đo bằng `TripPlanner.QueryBenchmarks` (xem dưới), 200 lần chạy mỗi cấu hình, Postgres 17. Cột
*dòng SQL* khớp đúng công thức `(items + ngày trống) × items`:

| Ngày | Item | Bản ghi thật | Dòng SQL | Một query | Split query | Chênh |
|---:|---:|---:|---:|---:|---:|---:|
| 14 | 0 | 14 | 14 | 1.81 ms | 3.47 ms | **chậm hơn 92%** |
| 14 | 5 | 19 | 70 | 2.32 ms | 3.63 ms | **chậm hơn 57%** |
| 14 | 10 | 24 | 140 | 2.95 ms | 3.71 ms | **chậm hơn 26%** |
| 14 | 15 | 29 | 225 | 4.42 ms | 3.01 ms | nhanh hơn 32% |
| 14 | 20 | 34 | 400 | 4.27 ms | 3.03 ms | nhanh hơn 29% |
| 14 | 30 | 44 | 900 | 7.81 ms | 3.08 ms | nhanh hơn 61% |
| 14 | 60 | 74 | 3 600 | 29.21 ms | 3.19 ms | nhanh hơn 89% |
| 30 | 60 | 90 | 3 600 | 29.44 ms | 3.68 ms | nhanh hơn 88% |

**Đây là một đánh đổi, không phải cải thiện thuần tuý** — và đó là thứ chỉ số đo trên engine thật
mới lộ ra:

- **Split query gần như phẳng ~3–3.7 ms** bất kể dữ liệu lớn cỡ nào. Nó là 3 round trip, và chi
  phí đó là hằng số.
- **Một query thì tăng theo số dòng**, từ 1.8 ms lên 29 ms.
- **Điểm hoà vốn nằm giữa 10 và 15 item.** Dưới ngưỡng đó, split **chậm hơn** — nhưng chậm một
  lượng có trần, khoảng 1–1.5 ms tuyệt đối. Trên ngưỡng, phần tiết kiệm tăng theo bình phương:
  ở 60 item là 26 ms.

Chọn split vì hình dạng rủi ro: mất tối đa ~1.5 ms ở trip nhỏ, đổi lấy việc trip lớn không bao giờ
rơi xuống 29 ms. Với một app mà mục đích chính là gom thật nhiều địa điểm vào một chuyến đi, phía
đắt là phía cần chặn.

**Một cảnh báo khi đọc bảng:** đo trên localhost, độ trễ mạng gần như bằng 0. Nếu app và DB nằm
khác máy, phạt 3 round trip sẽ lớn hơn và điểm hoà vốn dịch lên cao hơn 15 item.

### Đo lại được

`backend/tests/TripPlanner.QueryBenchmarks` giữ nguyên phép đo này để chạy lại bất cứ lúc nào:

```bash
docker compose up -d && dotnet run --project backend/tests/TripPlanner.QueryBenchmarks
```

Nó seed dữ liệu **trong một transaction rồi rollback**, nên chạy thẳng vào database dev cũng không
để lại gì. Với mỗi cấu hình nó in số dòng SQL, thời gian trung bình của cả hai shape trên 200 lần
chạy, và kiểm tra hai shape trả về **cùng một graph** — một query nhanh hơn nhưng trả về khác dữ
liệu thì vô giá trị.

Đây không phải test: `dotnet test` không chạy nó (không có test SDK), vì benchmark có thời gian
chạy dao động, không nên làm suite đỏ. Nó tồn tại chính vì suite **không thể** kiểm chỗ này — EF
InMemory không phải relational nên bỏ qua `AsSplitQuery` trong im lặng.

```csharp
await _context.Trips
    .AsNoTracking()
    .AsSplitQuery()          // ← mỗi collection một query, không join chéo
    .Include(t => t.Days).ThenInclude(d => d.Items).ThenInclude(i => i.Destination)
    .Include(t => t.Items).ThenInclude(i => i.Destination)
    .FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, ct);
```

Đánh đổi: split query là nhiều round trip và **không nguyên tử** nếu không bọc transaction. Chấp
nhận được ở đây vì một trip chỉ do chính chủ ghi, và hai đường ghi vẫn save trong một transaction.

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
| `TripTests` (Domain) | Quy tắc của aggregate: sinh lại days, move/resequence, chống trùng | Không mock, không DbContext |
| `*ServiceTests` | Điều phối use-case | EF InMemory (DB mới mỗi test) + Moq |
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
| `UpdateItem_WithAPositionPastTheEndOfTheBucket_IsValid` | `Trip.MoveItem` clamp — "99" nghĩa là "cuối cùng" |
| `SetDates_WhenItRejects_LeavesTheDaysUntouched` | Ngày sai không được để itinerary bị xây dở |
| `SetAsync_RetainsEntriesWellPastAnyTtl` | Bỏ retention là giết luôn stale-better-than-down |
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

### "Sao logic lịch trình nằm trong entity `Trip` mà không phải trong service?"

Vì đó là quy tắc về **tính nhất quán nội tại** của chính `Trip`, không phải quy tắc use-case.
`RegenerateDays`, `MoveItem`, `Resequence`, `HasDestinationIn` đều thuần tuý: không chạm
repository, không chạm clock, không chạm provider — chúng chỉ đọc và sửa `trip.Days` / `trip.Items`.
Đó đúng là định nghĩa của hành vi aggregate.

Trước đây chúng nằm trong `TripService` dưới dạng `private static`, và hệ quả rất cụ thể:

- `Trip.cs` chỉ có 39 dòng với đúng một method — Domain là nơi chứa dữ liệu, không phải nơi chứa
  quy tắc. Đó là cách một dự án "Clean Architecture" âm thầm thoái hoá thành CRUD phân tầng.
- Muốn test thuật toán sinh lại ngày phải đi qua `TripServiceTests` với **5 interface được mock**.
  Giờ `TripTests` test thẳng, không mock, chạy trong ~55 ms.

Ranh giới được giữ có chủ đích: `Trip.HasDestinationIn` chỉ **trả lời** "có trùng không";
`TripService` mới là chỗ ném `ConflictException` → 409. Nếu đẩy cả việc ném xuống Domain thì nó
thành `DomainException` → 400, sai ngữ nghĩa. Domain nói *sự thật*, Application quyết định *hậu quả
HTTP*.

### "Sao repository không có Unit of Work? Sao mỗi method tự SaveChanges?"

Hai lý do, lý do thứ nhất quan trọng hơn:

1. **`DbContext` đã là Unit of Work rồi.** Nó gom mọi thay đổi vào change tracker và
   `SaveChangesAsync` đẩy tất cả xuống trong **một** transaction — đúng định nghĩa của pattern.
   Thêm `IUnitOfWork` lên trên là bọc một UoW bằng một UoW.
2. **Không có use case nào cần nó.** Mỗi request thao tác đúng một aggregate. Ngay cả
   `UpdateItineraryItemAsync` — chạm hai bucket — vẫn nằm trong một aggregate `Trip`, và code
   **cố ý gọi một lần `UpdateAsync`** để hai bucket đổi nguyên tử. Chỗ duy nhất ghi hai aggregate
   là `AddDestinationAsync` (`Destination` rồi `Trip`), và ở đó việc tách rời là **có chủ ý**: nếu
   thêm vào trip hỏng, thứ còn lại là một dòng `Destination` — vốn chỉ là cache dữ liệu Geoapify
   khoá theo `ProviderId`, lần sau có người thêm đúng địa điểm đó thì dùng lại, đỡ một lần gọi API.

**Điều gì sẽ đảo ngược:** một use case ghi hai aggregate khác nhau mà hỏng một nửa là không chấp
nhận được. Kể cả lúc đó, một port hẹp kiểu `ITransactionRunner.RunAsync(...)` cũng đủ.

### "Sao `UpdateAsync(trip)` không hề dùng tham số `trip`?"

Vì change tracker của EF đã biết entity đó từ lúc query — truyền vào cũng không thêm thông tin gì.
Đối chiếu với `AddAsync`: ở đó tham số là **bắt buộc**, vì object vừa `new` chưa được context biết
tới nên phải `Add()` để đăng ký. Tham số của `UpdateAsync` giữ lại thuần tuý cho chỗ gọi dễ đọc.

Cái giá của nó — và đây mới là phần đáng nói khi bị hỏi: chữ ký hàm **hứa sai hai chiều**. Nó lưu
*ít* hơn (không gì cả, im lặng, nếu entity được nạp bằng `AsNoTracking`) và *nhiều* hơn (flush cả
những gì repository khác vừa thêm vào cùng context) so với những gì cái tên gợi ra. Không sửa
code, nhưng đã viết hẳn hai điều kiện đó vào doc comment của `ITripRepository` / `IUserRepository`
và cả hai implementation — cảnh báo đặt trên port là quan trọng nhất, vì người viết tầng
Application chỉ đọc interface chứ không mở Infrastructure ra xem.

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

### "Sao phải thêm `IStaleTolerantCache`? `IDistributedCache` đã là abstraction rồi mà?"

Đúng là abstraction, nhưng là abstraction **về công nghệ**, không phải về nghiệp vụ: nó nói bằng
`byte[]`, chuỗi key và `DistributedCacheEntryOptions`. Tầng Application không cần biết gì trong
số đó.

Cái Application thực sự cần diễn đạt chỉ là: *"cất giá trị kèm thời điểm lấy, trả lại bất kể cũ
đến đâu"*. Phần còn lại — serialize JSON, cửa sổ retention 7 ngày, xử lý entry đọc không ra — là
chi tiết cài đặt.

Bằng chứng đây không phải thay đổi hình thức: sau khi tách, **hai package reference biến mất khỏi
`TripPlanner.Application.csproj`** (`System.Text.Json` qua `Caching.Abstractions`, và
`Logging.Abstractions`). Trước đó `DestinationService` dài 410 dòng mà khoảng 40% là cơ chế cache;
giờ còn 328 dòng và đọc ra đúng ba use-case.

### "Nếu Geoapify chết thì sao?"

Ba tình huống: (1) còn entry trong retention 7 ngày → trả dữ liệu cũ; (2) không còn entry → lỗi
nổi lên thành response lỗi; (3) riêng đường details còn fallback thêm: tra `Destination` trong DB
của mình, vì địa điểm đã lưu trong trip phải xem được kể cả khi provider quên nó. Chỉ khi trượt
**cả hai** nguồn mới là 404.

### "Tại sao cache lại tự kiểm tra TTL thay vì để cache tự evict?"

Vì cần phân biệt ba trạng thái chứ không phải hai: *tươi*, *cũ nhưng dùng được*, *không có*.
Để cache evict theo TTL thì entry hết hạn biến mất, và đúng lúc provider sập lại không còn gì để
fallback. Bọc trong `CacheEnvelope(Value, FetchedAt)` với retention 7 ngày giải quyết chuyện đó.
Việc *so sánh* TTL là chính sách nên nằm ở Application; việc *lưu* kèm timestamp là cơ chế nên
nằm sau `IStaleTolerantCache`.

### "Đổi ngày trip thì lịch trình đã xếp có mất không?"

Không. `Trip.SetDates` giữ nguyên ngày còn nằm trong range cùng toàn bộ item của nó. Ngày rơi ra
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
`DestinationService` phụ thuộc `IStaleTolerantCache` (implement bằng `IDistributedCache`) chứ
không phải `IMemoryCache`. Điểm cần chú ý
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

### 3. Vài hành vi chỉ có ở provider SQL nên không có test tự động

EF InMemory không phải Postgres, nên hai chỗ nằm ngoài tầm với của `dotnet test`:

- **Dịch unique-violation**: InMemory không enforce unique index, nên nhánh đó không bao giờ chạy.
  Bù lại bằng cách mock `ConcurrencyException` ở ranh giới repository để test phần **caller**
  (xem `AuthServiceTests`).
- **`AsSplitQuery`**: là API chỉ dành cho relational provider; InMemory **bỏ qua trong im lặng**.
  Suite vẫn xanh nhưng không chứng minh được gì về hành vi thật trên Npgsql.

Cả hai đều phải suy luận trực tiếp trên provider SQL, và đều đã được ghi chú ngay tại code.

### 4. Migration chạy tự động lúc khởi động

Tiện cho môi trường học tập/dev, nhưng ở production nên tách thành bước deploy riêng — hai
instance khởi động cùng lúc có thể cùng chạy migration.

### 5. Chưa có refresh token

Access token hết hạn (mặc định 60 phút) thì user phải đăng nhập lại. Frontend đã xử lý chuyện này
tử tế (interceptor phát hiện 401 → tự logout → redirect), nhưng chưa có luồng gia hạn im lặng.

