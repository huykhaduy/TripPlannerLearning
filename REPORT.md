# TripPlanner — Báo cáo triển khai

**Bản thuyết trình 40 phút.** Tài liệu này nói về **cách làm và lý do quyết định**, không nhắc lại
requirement.

## Cách dùng tài liệu này

File chia làm hai vùng, và ranh giới giữa chúng là thứ quan trọng nhất khi tập nói:

- **Vùng nói** — Mở đầu + năm trụ + Kết. Đây là 40 phút. Mỗi mục có mốc thời gian ngay dưới tiêu đề.
- **Vùng `KHÔNG NÓI`** — Logging, Concurrency, Frontend, Configuration, Q&A, Phụ lục A. Không trình
  bày, chỉ mở ra khi bị hỏi. Phụ lục A là phần giải thích nền tảng ASP.NET Core / EF Core; Q&A là
  phần luyện trả lời chất vấn.

Mỗi trụ viết theo cùng một khuôn, cố ý, để tập ba lần là thuộc nhịp:

> **Câu mở** → hiện vật trung tâm (một đoạn code hoặc một sơ đồ) → cơ chế từng bước →
> **phản chứng** (nếu không làm thế thì vỡ ở đâu) → **câu chốt**.

Phần **phản chứng** là phần đáng thuộc nhất. Hiểu một cơ chế không phải là đọc được nó làm gì, mà là
nói được điều gì vỡ nếu bỏ nó đi — và đó cũng là dạng câu hỏi reviewer hay dùng để dò xem người trình
bày có thật hiểu hay chỉ thuộc lòng.

---

## Mục lục

**Vùng nói — 40 phút**

| Mục | Thời lượng | Mốc | Nội dung |
|---|---:|---:|---|
| [Mở đầu](#mở-đầu) | 4' | 0:00 | Bài toán, tech stack, bốn tầng, dependency rule |
| [TRỤ 1](#trụ-1--hai-ví-dụ-thật-search-và-add-to-trip) | 6' | 4:00 | Hai ví dụ thật — gọi những class nào |
| [TRỤ 2](#trụ-2--middleware-bắt-exception-như-thế-nào) | 6' | 10:00 | Middleware bắt exception như thế nào |
| [TRỤ 3](#trụ-3--validation-chạy-như-thế-nào) | 5' | 16:00 | Validation chạy như thế nào |
| [TRỤ 4](#trụ-4--caching-hoạt-động-như-thế-nào) | 6' | 21:00 | Caching hoạt động như thế nào |
| [TRỤ 5](#trụ-5--cartesian-explosion-và-assplitquery) | 6' | 27:00 | Cartesian explosion và `AsSplitQuery` |
| [Kết](#kết--testing-và-hạn-chế) | 3' | 33:00 | Testing và hạn chế đã biết |
| *Đệm cho chất vấn* | 4' | 36:00 | |

**Vùng KHÔNG NÓI — tra khi bị hỏi**

- [Logging](#logging--không-nói)
- [Concurrency & unique index](#concurrency--unique-index--không-nói)
- [Frontend gọi API](#frontend-gọi-api--không-nói)
- [Configuration & secrets](#configuration--secrets--không-nói)
- [Q&A — câu reviewer có thể hỏi](#qa--không-nói)
- [Phụ lục A — Nền tảng ASP.NET Core & EF Core](#phụ-lục-a--nền-tảng-aspnet-core--ef-core--không-nói)

---

## Mở đầu

> **4 phút** · 0:00 → 4:00

**Câu mở:** "TripPlanner là app lập kế hoạch chuyến đi: tìm điểm đến, xem chi tiết, xếp chúng vào
lịch trình theo ngày. Bốn mươi phút tới tôi sẽ không đi qua từng feature — tôi sẽ mở năm chỗ khó nhất
trong code và giải thích vì sao chúng được làm như vậy."

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

### Bốn tầng và một quy tắc

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

Mũi tên từ Infrastructure **trỏ vào** Application, không phải ngược lại. Application định nghĩa
interface (`IUserRepository`, `IDestinationProvider`, `IEmailSender`…), Infrastructure đi thực thi.
Đó là dependency inversion, và điểm đáng nói là nó **không phải quy ước trên giấy** — nó được ép bằng
file `.csproj`:

| Project | ProjectReference | Package chính |
|---|---|---|
| `TripPlanner.Domain` | *(không có)* | *(không có)* |
| `TripPlanner.Application` | Domain | FluentValidation, `DependencyInjection.Abstractions` |
| `TripPlanner.Infrastructure` | Application | EF Core, Npgsql, BCrypt, StackExchange.Redis, JWT |
| `TripPlanner.WebApi` | Application + Infrastructure | ASP.NET Core, Swagger, DotNetEnv |

**Domain không có một `PackageReference` nào.** Hệ quả cụ thể: viết `using Microsoft.EntityFrameworkCore;`
trong một service của Application thì **project không build được**. Quy tắc kiến trúc ở đây không phải
thứ phải nhớ khi review — compiler nhớ hộ.

### Bề mặt API

```
POST   /api/auth/register              (ẩn danh)
POST   /api/auth/login                 (ẩn danh)
POST   /api/auth/verify-email          (ẩn danh)
POST   /api/auth/resend-verification   (ẩn danh)

GET    /api/destinations/locations     (công khai — có chủ đích)
GET    /api/destinations/attractions   (công khai)
GET    /api/destinations/{providerId}  (công khai)

GET    /api/trips                                [Authorize]
GET    /api/trips/{tripId}                       [Authorize]
POST   /api/trips                                [Authorize]
PUT    /api/trips/{tripId}                       [Authorize]
POST   /api/trips/{tripId}/destinations          [Authorize]
PUT    /api/trips/{tripId}/destinations/{itemId} [Authorize]
DELETE /api/trips/{tripId}/destinations/{itemId} [Authorize]
```

`DestinationsController` **cố ý không có `[Authorize]`** — người dùng phải duyệt được điểm đến trước
khi đăng ký tài khoản. Có một test riêng pin điều này, vì nếu ai đó lỡ thêm `[Authorize]` vào thì
không một test nào khác trong suite phát hiện được: mọi test khác đều đăng nhập trước.

**Chuyển tiếp sang trụ 1:** "Sơ đồ bốn tầng ở trên thì ai cũng vẽ được. Câu hỏi thật là một request
đi qua nó thì gọi vào những class nào. Tôi lấy đúng hai ví dụ."

---

## TRỤ 1 — Hai ví dụ thật: search và add-to-trip

> **6 phút** · 4:00 → 10:00

**Câu mở:** "Hai request này chạm vào cả bốn tầng, và mọi thứ tôi nói trong bốn trụ sau đều là phóng
to một bước nào đó trong hai cây dưới đây."

### Ví dụ A — tìm thành phố

`GET /api/destinations/locations?query=paris` — công khai, không cần đăng nhập.

```
DestinationsController.SearchLocations                        (WebApi)
└─ DestinationService.SearchLocationsAsync                    (Application)
   ├─ query.Trim()
   ├─ SearchValidator.ValidateAndThrowAppExceptionAsync             → TRỤ 3
   └─ GetCachedAsync("loc:paris", LocationsTtl, …)                  → TRỤ 4
      ├─ IStaleTolerantCache.TryGetAsync                  (port, Application)
      │  └─ DistributedStaleTolerantCache                 (Infrastructure)
      │     └─ ResilientDistributedCache
      │        └─ IDistributedCache                       (Memory | Redis)
      └─ IDestinationProvider.SearchLocationsAsync        (port, Application)
         └─ GeoapifyClient                                (Infrastructure)
            └─ DistinctBy → OrderBy(RelevanceRank) → Take(5)
```

Ba chi tiết trong đó đáng chỉ ra, vì cả ba đều là quyết định chứ không phải tình cờ:

**Validate chuỗi đã `Trim()`, không phải chuỗi thô.** Rule là "query tối thiểu 2 ký tự", và chuỗi
được kiểm phải đúng là chuỗi sẽ gửi cho provider. Kiểm chuỗi thô thì `"  a  "` dài 5 ký tự sẽ lọt qua
rồi thành `"a"` khi gọi Geoapify.

**Cache key hạ chữ thường**, nên `"Paris"` và `"paris"` dùng chung một entry thay vì hai.

**Giá trị nằm trong cache là danh sách đã xử lý xong** — sau `DistinctBy`, sau xếp hạng, sau `Take(5)`.
Dedupe và xếp hạng là thuần tính toán, kết quả tất định, nên cache sau khi xử lý thì một lần hit bỏ
qua luôn cả phần việc đó, không chỉ bỏ qua lần gọi mạng.

Về xếp hạng: Geoapify có thể trả cùng một thành phố dưới nhiều `place_id`, nên dedupe theo thứ người
dùng thật sự nhìn thấy (tên + quốc gia), rồi `RelevanceRank` đẩy khớp chính xác lên trước, khớp tiền
tố kế tiếp, còn lại cuối.

### Ví dụ B — thêm địa điểm vào trip

`POST /api/trips/{tripId}/destinations` — cần `[Authorize]`.

```
TripsController.AddDestination                                (WebApi)
└─ TripService.AddDestinationAsync                            (Application)
   ├─ AddDestinationValidator.ValidateAndThrowAppExceptionAsync
   ├─ ICurrentUserService.GetRequiredUserId()  → CurrentUserService   (WebApi!)
   ├─ ITripRepository.GetForUpdateAsync(tripId, userId)               (Infrastructure)
   │     ?? throw new NotFoundException(...)
   ├─ EnsureDayBelongsToTripAsync(...)
   ├─ GetOrCreateDestinationAsync(providerId)          ← upsert, PHẢI đứng ở đây
   ├─ EnsureNotDuplicate(...) → Trip.HasDestinationIn                 (Domain)
   ├─ trip.Items.Add(new ItineraryItem {
   │      SortOrder = trip.NextSortOrderIn(dayId)      ← Domain quyết thứ tự
   │  })
   └─ SaveWithDuplicateGuardAsync(trip)                ← catch ConcurrencyException
```

Bốn điều đáng dạy nằm trong cây này.

**1. Trip của người khác trả 404, không phải 403.** `GetForUpdateAsync(tripId, userId)` lọc theo
**cả hai** tham số. Nên "trip này không thuộc về bạn" và "trip này không tồn tại" cho ra cùng một kết
quả `null`, và service không có cách nào phân biệt. Đó không phải hạn chế mà là mục đích: trả 403 là
tự xác nhận "trip này có thật, chỉ không phải của bạn" — một mẩu thông tin không nên cho.

**2. Ranh giới Domain / Application, thấy rõ ở đúng hai dòng cạnh nhau.** `SortOrder` do
`Trip.NextSortOrderIn(dayId)` quyết, và trùng lặp do `Trip.HasDestinationIn` phát hiện — cả hai là
quy tắc của chuyến đi, nên thuộc entity `Trip`. Nhưng `ConflictException` (→ HTTP 409) lại do
`TripService` ném. Lý do: 409 là từ vựng của tầng HTTP. Domain biết "địa điểm này đã có trong ngày
đó rồi"; nó không cần biết con số 409 tồn tại trên đời.

**3. `ICurrentUserService` được implement ở WebApi, không phải Infrastructure.** Đây là ngoại lệ duy
nhất của quy tắc "Infrastructure thực thi interface của Application", và nó có lý do: lấy user id ra
từ token nghĩa là đọc `HttpContext`, tức `IHttpContextAccessor` — một khái niệm chỉ tồn tại trong
pipeline HTTP. Đặt nó vào Infrastructure là kéo ASP.NET Core vào một project vốn chỉ nên biết về
database và API ngoài.

**4. Phản chứng — thứ tự hai dòng giữa cây là bắt buộc.** Trong code có một comment `ORDER MATTERS`,
và đây là lý do:

> Mọi repository dùng **chung một `DbContext`** trong phạm vi một request, và mỗi write method tự gọi
> `SaveChanges` trên **toàn bộ** context đó — không có unit of work. Nên nếu `trip.Items.Add(...)`
> chạy trước, thì khi `GetOrCreateDestinationAsync` lưu destination mới, lần `SaveChanges` đó sẽ đẩy
> luôn cả thay đổi nửa vời của trip xuống database.

Thêm destination **trong khi trip còn chưa bị chạm tới** giữ cho hai lần save độc lập với nhau. Đây
là loại bug đọc code không thấy: đảo hai dòng lên xuống thì vẫn build, vẫn pass phần lớn test, và chỉ
sai ở đường thất bại.

**Câu chốt:** "Hai cây này là bản đồ. Bốn trụ còn lại mỗi trụ phóng to đúng một điểm trên đó:
validate, bắt lỗi, cache, và câu query."

---

## TRỤ 2 — Middleware bắt exception như thế nào

> **6 phút** · 10:00 → 16:00

**Câu mở:** "Trong toàn bộ backend này, controller không có một `try/catch` nào. Đây là cách làm được
điều đó."

### Hiện vật: sáu dòng

```csharp
public async Task InvokeAsync(HttpContext context)
{
    try
    {
        await _next(context);
    }
    catch (Exception ex)
    {
        await HandleAsync(context, ex);
    }
}
```

### Cơ chế: pipeline là búp bê Nga, không phải hàng đợi

Đây là chỗ dễ hiểu sai nhất về middleware. Pipeline **không phải** một danh sách được gọi lần lượt —
nó là các lớp **lồng vào nhau**. `await _next(context)` chính là lời gọi xuống lớp bên trong, và nó
chỉ trả về khi mọi thứ bên trong đã xong.

```
ExceptionHandlingMiddleware ──┐
  CORS ───────────────────────┐│
    Authentication ──────────┐││
      Authorization ────────┐│││
        Controller  ────────┘│││   ← exception ném ra ở đây
                    ────────┘││    ← bay ngược ra ngoài
                    ─────────┘│
                    ──────────┘    ← try/catch ở lớp ngoài cùng bắt được
```

Vì vậy bọc `await _next()` bằng `try/catch` là bắt được exception của **mọi thứ phía sau** —
authentication, authorization, model binding, controller, service, repository. Và vì vậy nó phải là
middleware **đăng ký đầu tiên**: đầu tiên trong `Program.cs` nghĩa là ngoài cùng trong búp bê.

Bắt được rồi thì dịch sang HTTP bằng một `switch`:

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

Rồi đóng gói theo RFC 7807 (`application/problem+json`):

```json
{
  "status": 400,
  "title": "Validation failed",
  "detail": "One or more validation errors occurred.",
  "errors": { "Password": ["Password must be at least 8 characters."] }
}
```

Khoá `errors` **chỉ** xuất hiện khi exception là `ValidationException` và thật sự có phần tử.

### Hai chi tiết nhỏ, cả hai đều từng là bug

**Một — `contentType` phải truyền vào, không được gán trước.**

```csharp
await context.Response.WriteAsJsonAsync(
    problem, options: null, contentType: "application/problem+json");
```

`WriteAsJsonAsync` **luôn tự ghi** `Response.ContentType`. Nên nếu gán
`context.Response.ContentType = "application/problem+json"` ở dòng trên rồi gọi hàm này, nó sẽ âm
thầm ghi đè thành `application/json` — response vẫn đúng nội dung, vẫn đúng status code, chỉ mất
media type mà cả cái `ProblemDetails` này tồn tại để tuân theo. Không có gì báo lỗi.

**Hai — chỉ nhánh 500 mới che message, và chỉ nhánh 500 mới log Error.**

```csharp
var detail = status == HttpStatusCode.InternalServerError && !_environment.IsDevelopment()
    ? "An unexpected error occurred."
    : exception.Message;
```

Các exception đã được map đều mang message viết cho người dùng đọc, nên trả nguyên văn. Còn thứ rơi
vào nhánh `_` thì không ai kiểm soát message của nó — nó có thể chứa câu SQL, tên bảng, hoặc
connection string. Ngoài Development thì thay bằng câu chung.

Về log: 400, 404, 409 là **hoạt động bình thường** của một API. Ghi chúng ở mức Error sẽ làm log nhiễu
tới mức không ai đọc nữa, và khi đó một lỗi 500 thật sẽ chìm mất giữa hàng nghìn dòng 404. Nên chỉ
nhánh 500 log `LogError`.

### Phản chứng: đổi thứ tự trong `Program.cs` thì vỡ ở đâu

```csharp
app.UseMiddleware<ExceptionHandlingMiddleware>();   // 1. NGOÀI CÙNG
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();                               // 2. chỉ ở Development
    app.UseSwaggerUI();
}
app.UseCors(CorsPolicy);                            // 3. trước auth
app.UseAuthentication();                            // 4. dựng HttpContext.User từ token
app.UseAuthorization();                             // 5. áp dụng [Authorize]
app.MapControllers();                               // 6. vào controller
```

| Đổi gì | Vỡ ra sao |
|---|---|
| Đặt `ExceptionHandlingMiddleware` sau `UseCors` | Lỗi phát sinh trong CORS nằm **ngoài** `try/catch`, lọt ra thành 500 thô không có `ProblemDetails` |
| `UseAuthorization` trước `UseAuthentication` | Chưa ai dựng `HttpContext.User`, nên `[Authorize]` thấy mọi request là ẩn danh → 401 toàn bộ |
| `UseCors` sau `UseAuthentication` | Preflight `OPTIONS` của browser không mang header `Authorization` → bị chặn ở auth → browser không bao giờ gửi request thật |

Hai chi tiết nữa trong `Program.cs` đáng nhắc nếu còn thời gian: `ClockSkew = TimeSpan.Zero` (mặc
định .NET là 5 phút, nghĩa là token hết hạn vẫn dùng được thêm 5 phút — đặt về 0 để `ExpiryMinutes`
mang đúng nghĩa của nó), và migration tự chạy lúc khởi động nhưng bỏ qua khi environment là
`Testing`, vì provider InMemory không hỗ trợ migration.

**Câu chốt:** "Một chỗ duy nhất dịch exception sang HTTP. Thêm một loại exception mới thì sửa đúng
một `switch`, và không controller nào phải biết."

---

## TRỤ 3 — Validation chạy như thế nào

> **5 phút** · 16:00 → 21:00

**Câu mở:** "Có ba loại kiểm tra khác nhau, chúng ở ba chỗ khác nhau, và ranh giới đó không được nhoè."

### Ba tầng

| Loại kiểm tra | Đặt ở đâu | Exception | HTTP |
|---|---|---|---|
| Chỉ cần nhìn input (bắt buộc, độ dài, khoảng giá trị) | Validator class | `ValidationException` | 400 |
| Cần database (email đã tồn tại, trip thuộc về ai) | Service method | `ConflictException` / `ValidationException.ForProperty` | 409 / 400 |
| Bất biến của entity (ngày kết thúc trước ngày bắt đầu) | Domain entity | `DomainException` | 400 |

Phép thử để biết một rule thuộc tầng nào: **rule đó có cần biết gì ngoài chính cái request không?**
"Mật khẩu ≥ 8 ký tự" thì không — validator. "Email này chưa ai dùng" thì cần — service. "Ngày kết
thúc không được trước ngày bắt đầu" cần cả hai giá trị của entity và đúng là bất biến của nó —
domain, và nằm trong `Trip.SetDates`.

### Hiện vật: đường đi của một message lỗi

```
1. Khai báo    RegisterRequestValidator:
                 .MinimumLength(8).WithMessage("Password must be at least 8 characters.")

2. Gom lại     ValidationExtensions.ValidateAndThrowAppExceptionAsync:
                 result.ToDictionary()
                 → { "Password": ["Password must be at least 8 characters."] }
                 → ném ValidationException CỦA DỰ ÁN

3. Serialize   ExceptionHandlingMiddleware:
                 → ProblemDetails + extensions["errors"]     (TRỤ 2)

4. Đọc         frontend getErrorMessage(err, fallback)
                 → err.response.data.detail
```

### Cơ chế: bước 2 là một cây cầu, và nó tồn tại vì một lý do rất cụ thể

```csharp
public static async Task ValidateAndThrowAppExceptionAsync<T>(
    this IValidator<T> validator, T instance, CancellationToken cancellationToken = default)
{
    var result = await validator.ValidateAsync(instance, cancellationToken);
    if (!result.IsValid)
    {
        throw new Exceptions.ValidationException(result.ToDictionary());
    }
}
```

FluentValidation có `ValidationException` **của riêng nó**, và middleware ở trụ 2 không biết type đó.
Extension method này đổi từ type của thư viện sang type của dự án, đồng thời `ToDictionary()` gom các
failure thành hình `property → string[]` — đúng hình mà `ValidationException` của dự án mang theo và
`ProblemDetails.extensions["errors"]` cần.

### Phản chứng: gọi sai một hàm thì 400 thành 500

FluentValidation có sẵn `ValidateAndThrowAsync()`. Gọi hàm đó thay vì hàm của dự án thì:

```
FluentValidation.ValidationException  →  middleware switch không có case nào khớp
                                      →  rơi vào nhánh `_`
                                      →  HTTP 500, message bị che ngoài Development,
                                         và mất sạch field-level errors
```

Client nhận 500 thay vì 400 cho một cái mật khẩu ngắn. Đây là phản chứng đáng nhớ nhất trong bài vì
khoảng cách giữa đúng và sai chỉ là một tên hàm, và không có test nào của validator bắt được — nó chỉ
lộ ra ở test tầng HTTP.

### Vì sao validator không đăng ký vào DI

```csharp
private static readonly RegisterRequestValidator RegisterValidator = new();
```

Chúng là khai báo quy tắc **stateless, không có dependency**, và FluentValidation validator an toàn
khi dùng đồng thời sau khi đã dựng xong. Inject `IValidator<T>` sẽ tạo ra mười interface mà mỗi cái
đúng một implementation, không bao giờ có ai thay thế — nghĩa là mười tầng gián tiếp không mua được gì.

Chi tiết đi kèm để không ai vô tình "sửa lại cho đúng chuẩn": `AddApplication()` **cố ý không có**
`AddValidatorsFromAssembly`, và package được dùng là `FluentValidation` thuần, không phải
`FluentValidation.DependencyInjectionExtensions`.

**Điều gì sẽ đảo ngược quyết định này:** một validator cần dependency — ví dụ một rule async truy vấn
database — thì không thể là static instance nữa. Khi đó chuyển **riêng validator đó** thành constructor
parameter và để yên phần còn lại. Không phải đổi cả mười cái.

### Ba quy tắc tinh tế, mỗi cái có một test pin nó

- `.Cascade(CascadeMode.Stop)` — dừng ở lỗi đầu tiên, để `email.Contains('@')` không chạy trên `null`.
- `LoginRequestValidator` **cố tình không có** rule độ dài mật khẩu. Nếu sau này nâng policy lên 12 ký
  tự, thêm rule vào login sẽ **khoá luôn** những tài khoản có mật khẩu cũ hợp lệ ở thời điểm họ đăng ký.
  Test `Login_WithAPasswordShorterThanRegisterAllows_IsStillValid` pin sự vắng mặt này.
- `RegisterRequestValidator` dùng **cùng một message** cho "để trống" và "sai định dạng", để không ai
  dùng khác biệt message mà dò xem một email đã đăng ký hay chưa.

Một sự vắng mặt nữa: `ProviderId` **không giới hạn độ dài** ở cả validator lẫn database. `place_id`
của Geoapify là một prefix ~68 ký tự cộng tên địa danh hex-encode (2 ký tự cho mỗi byte UTF-8), thực
tế dài 62–328 ký tự. `varchar(128)` từng làm chức năng thêm-vào-trip trả 500 với mọi tên dài hoặc
không phải Latin.

**Câu chốt:** "Validator trả lời được câu hỏi chỉ cần nhìn request. Mọi câu hỏi cần biết thêm thứ gì
khác đều không thuộc về nó."

---

## TRỤ 4 — Caching hoạt động như thế nào

> **6 phút** · 21:00 → 27:00

**Câu mở:** "Geoapify chết thì trang tìm điểm đến vẫn phải mở được. Đây là cơ chế làm điều đó, và nó
gọn hơn nhiều so với vẻ ngoài."

### Cache cái gì, và không cache cái gì

Chỉ cache **đường duyệt** của Destination — tức kết quả gọi API ngoài. Trip và User **không** cache:
dữ liệu riêng tư, người dùng vừa sửa là phải thấy ngay.

| Dữ liệu | TTL |
|---|---|
| Gợi ý thành phố (`locations`) | 24 giờ |
| Danh sách điểm tham quan (`attractions`) | 6 giờ |
| Chi tiết một điểm đến (`details`) | 24 giờ |
| Ảnh (Serper) | 24 giờ |

`attractions` ngắn hơn vì nó là thứ hay đổi nhất trong bốn.

### Hiện vật: mười sáu dòng

```csharp
private async Task<T> GetCachedAsync<T>(
    string key, TimeSpan ttl, Func<Task<T>> fetchAsync, CancellationToken cancellationToken)
{
    var stale = await _cache.TryGetAsync<T>(key, cancellationToken);
    if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < ttl)
    {
        return stale.Value;
    }

    try
    {
        var value = await fetchAsync();
        await _cache.SetAsync(key, new CacheEnvelope<T>(value, _clock.GetUtcNow()), cancellationToken);
        return value;
    }
    catch (ExternalServiceUnavailableException) when (stale is not null)
    {
        return stale.Value; // stale-better-than-down
    }
}
```

Ba nhánh, đọc theo thứ tự:

1. **Có entry và còn trong TTL** → trả ngay, không gọi provider.
2. **Không có entry, hoặc có nhưng đã hết TTL** → gọi provider, lưu lại kèm mốc thời gian, trả về.
3. **Provider chết, và có một bản cũ** → trả bản cũ.

Cả cơ chế nằm ở mệnh đề `when (stale is not null)`. Đó là **exception filter** của C#: `catch` chỉ
bắt khi điều kiện đúng. Nên nếu provider chết mà **không** có bản cũ nào, exception không bị bắt — nó
bay tiếp lên middleware và thành 500. Đúng như vậy là tốt: không có gì để trả thì phải nói thật là
không có, chứ không trả danh sách rỗng làm người dùng tưởng Paris không có điểm tham quan nào.

Biến tên là `stale` chứ không phải `cached`, và đó là chủ ý — ở thời điểm đọc ra thì chưa biết nó còn
tươi hay không, tên biến nói đúng điều đang chắc chắn.

### Tách chính sách khỏi cơ chế

Đây là phần kiến trúc của trụ này, và nó kiểm chứng được.

| | Là gì | Ở đâu |
|---|---|---|
| **Chính sách** | TTL từng endpoint; luật "provider chết thì trả bản cũ" | `DestinationService` (Application) |
| **Cơ chế** | JSON, byte array, `DistributedCacheEntryOptions`, cửa sổ giữ 7 ngày | `DistributedStaleTolerantCache` (Infrastructure) |

Ranh giới là interface `IStaleTolerantCache`, chỉ có hai method: `TryGetAsync` và `SetAsync`, làm việc
với `CacheEnvelope<T>` — giá trị kèm mốc `FetchedAt`.

Bằng chứng cho việc tách này là thật, không phải chỉ đặt tên đẹp: **tầng Application không reference
`System.Text.Json`, cũng không reference `Microsoft.Extensions.Caching.Abstractions`**. Trước khi tách,
`DestinationService` mang cả hai.

### Đọc lỗi thì hạ xuống thành miss; ghi lỗi thì không

Một quy tắc bất đối xứng, và sự bất đối xứng là có lý do:

- **Đọc thất bại** — entry không deserialize được vì DTO đã đổi hình từ lần deploy trước — được báo
  là **miss**. Đây là chuyện bình thường: entry sống lâu hơn code sinh ra nó, và ứng xử đúng là gọi
  lại provider.
- **Ghi thất bại** thì **không** nuốt. Ghi lỗi là bug trong hợp đồng của chính mình. Nuốt nó một lần
  đã làm một lỗi serialize hiện ra dưới dạng "provider hình như đang chết", và câu trả lời khi đó là
  dữ liệu cũ — chẩn đoán sai hoàn toàn.

### Phản chứng: sao không để cache tự hết hạn?

Câu hỏi rất tự nhiên: `IDistributedCache` có sẵn expiry, sao phải tự so `FetchedAt` với TTL trong code?

Vì hai câu hỏi khác nhau bị lẫn vào nhau:

```
TTL             → "dữ liệu này còn dùng được không?"        (6h hoặc 24h)
Cửa sổ giữ      → "nếu provider chết, còn gì để cứu không?"  (7 ngày)
```

Giao TTL cho cache tự evict thì entry hết hạn bị **xoá**. Và lúc provider chết là đúng lúc cần nó
nhất — nhưng nó không còn ở đó nữa, nên chỉ còn 500. Nói cách khác: **để cache tự evict là tự tay
phá đúng cái tính năng này.** Cửa sổ giữ vì thế phải dài hơn mọi TTL — 7 ngày so với 24 giờ.

### Bốn tầng chống lỗi, và đổi Redis bằng một dòng config

Xếp từ trong ra ngoài, mỗi tầng lo một loại thất bại khác nhau:

| Tầng | Lo chuyện gì |
|---|---|
| `GeoapifyClient` / `SerperImageClient` | Provider lỗi → log, rồi ném `ExternalServiceUnavailableException` |
| `ResilientDistributedCache` | Redis chết → log, rồi coi như cache miss |
| `DistributedStaleTolerantCache` | Entry không đọc được → coi như miss |
| `DestinationService.GetCachedAsync` | Provider chết mà có bản cũ → trả bản cũ |

Đổi backend cache: đặt `Cache__Provider=Redis` trong `.env` và `docker compose up -d`. Không dòng code
nào trong Application đổi, vì nó chỉ biết `IStaleTolerantCache`.

Một điểm về các adapter đáng nói vì nó là lý do `catch` ở trên bắt đúng một type: adapter **dịch** lỗi
của công nghệ nó dùng thành `ExternalServiceUnavailableException` rồi mới ném. Nhờ vậy không service
nào trong Application phải nhắc tên `HttpRequestException`, `SmtpException` hay `JsonException`. Và
chúng chỉ dịch khi `CancellationToken` của chính chúng **không** phải nguyên nhân — nên một request bị
người dùng huỷ vẫn bay lên như `TaskCanceledException` và không bao giờ bị hiểu nhầm thành provider chết.

**Câu chốt:** "TTL trả lời dữ liệu còn dùng được không. Cửa sổ giữ trả lời còn gì để cứu không. Hai
câu hỏi khác nhau nên phải là hai con số khác nhau."

---

## TRỤ 5 — Cartesian explosion và AsSplitQuery

> **6 phút** · 27:00 → 33:00

**Câu mở:** "Một câu LINQ trông hoàn toàn vô hại làm Postgres trả về 3 600 dòng cho dữ liệu thật chỉ
có 90 bản ghi. Đây là cái bẫy mà tên gọi của nó không hề gợi ra, và nhìn code thì không thấy gì bất
thường."

### Gốc rễ: SQL chỉ trả về được bảng phẳng

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

`s0` và `s1` **không có điều kiện nào ràng buộc lẫn nhau** — cả hai chỉ nối vào `t`. Database vì thế
phải ghép **mọi dòng của `s0` với mọi dòng của `s1`**. Đó là tích Descartes, và tên gọi của hiện tượng
này là *cartesian explosion*.

Thử một trip tí hon để đếm tay: 2 ngày, 3 địa điểm (ngày 1 có A và B, ngày 2 có C).

| | `s1` = A | `s1` = B | `s1` = C |
|---|---|---|---|
| `s0` = (ngày 1, A) | ✓ | ✓ | ✓ |
| `s0` = (ngày 1, B) | ✓ | ✓ | ✓ |
| `s0` = (ngày 2, C) | ✓ | ✓ | ✓ |

**9 dòng** cho dữ liệu thật chỉ gồm 1 trip + 2 ngày + 3 item. Và con số 9 chưa phải chỗ tốn nhất:
**mỗi dòng trong 9 dòng đó chở theo toàn bộ cột** của trip, của ngày, của item *và* của destination —
tức cả `Address`, `Description`, `ImageUrl`, `Website`, `OpeningHours`. Cùng một destination được gửi
qua dây nhiều lần.

**Bậc hai theo item, không phải `ngày × item`.** Trực giác ban đầu hay đoán sai chỗ này. Đếm kỹ:

- `s0` = một dòng cho **mỗi cặp (ngày, item trong ngày đó)**, cộng một dòng cho mỗi **ngày trống**
  (`LEFT JOIN` giữ lại ngày không có item). Nếu mọi item đều đã xếp lịch thì `s0` ≈ số item.
- `s1` = **mọi item** của trip.

Nhân lại: `(items + ngày trống) × items`. **Số ngày gần như không ảnh hưởng — số item mới là thủ
phạm.** Bảng đo bên dưới xác nhận: 14 ngày/60 item và 30 ngày/60 item đều ra đúng 3 600 dòng.

**Một cái bẫy phụ đáng biết.** Hai nhánh `Include` ở trên chồng lên nhau, vì `ItineraryItem` mang cả
`TripId` (luôn có) và `ItineraryDayId` (null = Saved Places) — nên `day.Items` là **tập con** của
`trip.Items`. Thứ duy nhất nhánh đầu đóng góp riêng là bản thân các dòng `ItineraryDays`. Nhưng bỏ
nhánh trùng đi **không** chữa được cartesian explosion: còn lại `Include(t => t.Days)` và
`Include(t => t.Items)` thì vẫn là **hai collection cùng cấp**, vẫn tích chéo. Nó chỉ bớt dữ liệu lặp,
không đổi bản chất. Dự án vẫn giữ nhánh trùng vì `TripMappings.ToDayDto` đọc `day.Items` để dựng DTO
cho từng ngày.

### `AsSplitQuery` làm gì

Nó bảo EF: đừng nhồi vào một câu, **tách mỗi collection thành một câu riêng**.

```
Câu 1:  SELECT trip                          →  1 dòng
Câu 2:  SELECT days + items + destinations   →  3 dòng
Câu 3:  SELECT items + destinations          →  3 dòng
```

7 dòng thay vì 9, và không dòng nào lặp lại cột của trip. EF **tự ghép** ba kết quả lại trong bộ nhớ
thành đúng một object `Trip` với `Days` và `Items` đầy đủ — **code gọi không thấy khác biệt gì**.

Đo ở tầng SQL bằng `ToQueryString()` nên không cần database chạy: câu gốc **5 JOIN / 2 757 ký tự**,
sau khi split thì câu đầu còn **0 JOIN / 501 ký tự**.

```csharp
await _context.Trips
    .AsNoTracking()
    .AsSplitQuery()          // ← mỗi collection một query, không join chéo
    .Include(t => t.Days).ThenInclude(d => d.Items).ThenInclude(i => i.Destination)
    .Include(t => t.Items).ThenInclude(i => i.Destination)
    .FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, ct);
```

**Một hệ quả tinh tế: identity resolution.** `AsNoTracking()` **không** làm identity resolution, nên
cùng một `ItineraryItem` thành **hai object khác nhau** trong bộ nhớ — một trong `day.Items`, một trong
`trip.Items`. Ở đường đọc (`GetDetailsAsync`) điều này vô hại, chỉ tốn bộ nhớ. Nhưng
`GetForUpdateAsync` thì **có tracking**, nên EF gộp về **một** object duy nhất — và đó chính là điều
kiện để `Trip.MoveItem` sửa một item trong `trip.Items` mà `day.Items` cũng thấy thay đổi ngay. Ai đó
thêm `AsNoTracking()` vào `GetForUpdateAsync` cho "nhẹ hơn" sẽ làm hỏng các đường ghi theo kiểu rất
khó lần ra.

### Số đo thật trên Postgres

Đo bằng `TripPlanner.QueryBenchmarks`, 200 lần chạy mỗi cấu hình, Postgres 17. Cột *dòng SQL* khớp
đúng công thức `(items + ngày trống) × items`:

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

**Đây là một đánh đổi, không phải cải thiện thuần tuý** — và đó là thứ chỉ số đo trên engine thật mới
lộ ra:

- **Split query gần như phẳng ~3–3.7 ms** bất kể dữ liệu lớn cỡ nào. Nó là 3 round trip, và chi phí
  đó là hằng số.
- **Một query thì tăng theo số dòng**, từ 1.8 ms lên 29 ms.
- **Điểm hoà vốn nằm giữa 10 và 15 item.** Dưới ngưỡng đó split **chậm hơn** — nhưng chậm một lượng
  có trần, khoảng 1–1.5 ms tuyệt đối. Trên ngưỡng, phần tiết kiệm tăng theo bình phương: ở 60 item là
  26 ms.

Chọn split vì **hình dạng của rủi ro**: mất tối đa ~1.5 ms ở trip nhỏ, đổi lấy việc trip lớn không bao
giờ rơi xuống 29 ms. Với một app mà mục đích chính là gom thật nhiều địa điểm vào một chuyến đi, phía
đắt là phía cần chặn.

**Một cảnh báo khi đọc bảng:** đo trên localhost, độ trễ mạng gần như bằng 0. Nếu app và DB nằm khác
máy, phạt 3 round trip sẽ lớn hơn và điểm hoà vốn dịch lên cao hơn 15 item.

Chạy lại bất cứ lúc nào:

```bash
docker compose up -d && dotnet run --project backend/tests/TripPlanner.QueryBenchmarks
```

Nó seed dữ liệu **trong một transaction rồi rollback**, nên chạy thẳng vào database dev cũng không để
lại gì. Mỗi cấu hình nó in số dòng SQL, thời gian trung bình của cả hai shape, và kiểm hai shape trả
về **cùng một graph** — một query nhanh hơn mà trả khác dữ liệu thì vô giá trị. Đây không phải test:
`dotnet test` không chạy nó, vì benchmark có thời gian dao động, không nên làm suite đỏ. Nó tồn tại
chính vì suite **không thể** kiểm chỗ này — EF InMemory không phải relational nên bỏ qua `AsSplitQuery`
trong im lặng.

### Phản chứng: cái giá, và khi nào KHÔNG cần tới nó

**Cái giá của `AsSplitQuery`:** 3 round trip thay vì 1, và các câu **không nguyên tử** nếu không bọc
transaction — giữa câu 2 và câu 3, dữ liệu có thể đổi. Chấp nhận được ở đây vì một trip chỉ do chính
chủ ghi, và cả hai đường ghi vẫn save trong một transaction.

**Muốn xuống một câu mà không có tích chéo** thì chỉ có một cách: bỏ `Include`, dùng **projection** —
`Select` thẳng ra DTO. EF sẽ chỉ kéo đúng các cột cần và tự sinh subquery cho từng collection. Đổi lại
phải viết tay hình dạng projection, và kết quả **không còn là entity** nên `TripService` không gọi được
`Trip.MoveItem` lên nó. Vì vậy cách này chỉ hợp cho đường **đọc**, không dùng được cho
`GetForUpdateAsync`. Dự án giữ `Include` cho cả hai để hai đường đọc chung một hình dạng query.

**Quy tắc nhớ:**

> Chỉ cần nghĩ tới `AsSplitQuery` khi query có **từ hai `Include` trỏ tới collection trở lên, ở cùng
> một cấp**.

Một chuỗi `Include` dài nhưng **đơn tuyến** (`Trip → Items → Destination`) thì **không** dính, vì mỗi
item chỉ có một destination — quan hệ một-một không sinh tích chéo. Nó chỉ nổ khi có hai nhánh song
song cùng treo trên một gốc. Trong cả repo này chỉ có đúng hai query rơi vào trường hợp đó, cả hai đều
ở `TripRepository`.

**Câu chốt:** "Không có cách nào nhanh hơn ở mọi kích thước. Có cách chậm đều một lượng có trần, và
cách nhanh hơn cho tới lúc nó không còn nhanh. Tôi chọn cái thứ nhất, và tôi có số đo để nói vì sao."

---

## Kết — Testing và hạn chế

> **3 phút** · 33:00 → 36:00

**Câu mở:** "Hai điều cuối: suite test kiểm gì, và tôi biết dự án này còn thiếu gì."

### Bốn tầng test

| Tầng | Kiểm gì | Công cụ |
|---|---|---|
| `TripTests` (Domain) | Quy tắc của aggregate: sinh lại days, move/resequence, chống trùng | Không mock, không DbContext |
| `*ServiceTests` | Điều phối use-case | EF InMemory (DB mới mỗi test) + Moq |
| `*ValidatorTests` | Lỗi rơi vào **property** nào, **message** chính xác, **biên** hai phía | `TestValidate` |
| `TripPlanner.WebApi.Tests` | Routing thật, `[Authorize]` thật, middleware map status, binding query string | `WebApplicationFactory<Program>` |
| Frontend | Hành vi người dùng | Vitest + React Testing Library (jsdom) |

**Quy mô, đo lúc viết báo cáo này:**

| | Số case | Ghi chú |
|---|---:|---|
| `TripPlanner.UnitTests` | 302 | Domain + Application + Infrastructure trong một project |
| `TripPlanner.WebApi.Tests` | 69 | `WebApplicationFactory` |
| **Backend** | **371** | 0 fail |
| **Frontend** | **277** | 30 file, 0 fail |

`TripPlanner.UnitTests` cố ý **không** soi gương 1-1 với `src/`: một project phủ cả ba tầng, vì cả ba
chạy in-process và dùng chung fixture. Chia project theo *thứ cần để chạy* — có web host hay không —
chứ không theo tên tầng.

### Test pin cả những quyết định "cố ý KHÔNG làm"

Đây là phần đáng nói nhất: test không chỉ kiểm code chạy đúng, mà giữ cho **ý định thiết kế** không bị
người sau vô tình phá.

| Test | Bảo vệ điều gì |
|---|---|
| `Login_WithAPasswordShorterThanRegisterAllows_IsStillValid` | Thêm rule độ dài vào login sẽ khoá tài khoản cũ |
| `UpdateTrip_WithEndBeforeStart_IsNotTheValidatorsJob` | Đó là quy tắc domain (`Trip.SetDates`), không phải việc của validator |
| `UpdateItem_WithAPositionPastTheEndOfTheBucket_IsValid` | `Trip.MoveItem` clamp — "99" nghĩa là "cuối cùng" |
| `SetDates_WhenItRejects_LeavesTheDaysUntouched` | Ngày sai không được để itinerary bị xây dở |
| `SetAsync_RetainsEntriesWellPastAnyTtl` | Bỏ retention là giết luôn stale-better-than-down (TRỤ 4) |
| `DestinationConfigurationTests` | Không ai được thêm lại `HasMaxLength` cho `ProviderId` |
| `DestinationsEndpointsTests` | Endpoint destinations phải giữ **công khai** |
| `TestHostConfigurationTests` | Test host không bao giờ ký token bằng key thật của dev |

`DestinationConfigurationTests` đặc biệt: EF InMemory **bỏ qua** `HasMaxLength`, nên không một test
hành vi nào bắt được regression này — nó pin sự vắng mặt qua **model metadata**.

### Năm hạn chế đã biết

Chủ động nêu ra tốt hơn nhiều so với để reviewer tự tìm thấy.

**1. Lỗi validation theo field chưa hiển thị trên UI.** API trả `errors: { "Password": [...] }` nhưng
`getErrorMessage` chỉ đọc `detail`, mà `detail` của `ValidationException` là câu chung. Backend đã sẵn
sàng; thiếu một helper `getFieldErrors(err)` ở `client.ts` và phần render dưới mỗi input.

**2. Hai lỗ hổng concurrency được chấp nhận có cân nhắc.** Postgres coi **mỗi NULL là khác nhau**, nên
`(ItineraryDayId, DestinationId)` **không phủ** Saved Places. Hai request đồng thời thêm cùng một địa
điểm vào Saved Places đều có thể thành công. Đã viết filtered unique index rồi **cố ý revert** vì
over-engineering ở quy mô này; hậu quả tối đa là một dòng trùng trong trip của chính user đó. Riêng
`SortOrder` không có unique index — thứ tự đảm bảo hoàn toàn in-memory, nên hai thao tác reorder đồng
thời trên cùng bucket sẽ race.

**3. Hai hành vi chỉ có ở provider SQL nên không có test tự động.** Dịch unique-violation (InMemory
không enforce unique index) và `AsSplitQuery` (InMemory bỏ qua trong im lặng). Cả hai phải suy luận
trực tiếp trên Npgsql, và cả hai đã được ghi chú ngay tại code. Đây cũng chính là lý do
`QueryBenchmarks` ở trụ 5 tồn tại.

**4. Migration chạy tự động lúc khởi động.** Tiện cho dev, nhưng production nên tách thành bước deploy
riêng — hai instance khởi động cùng lúc có thể cùng chạy migration.

**5. Chưa có refresh token.** Access token hết hạn (mặc định 60 phút) thì phải đăng nhập lại. Frontend
xử lý tử tế (interceptor phát hiện 401 → tự logout → redirect) nhưng chưa có luồng gia hạn im lặng.

### Nói thêm nếu còn thời gian

Ba chi tiết setup test đáng nói, để dành cho phần đệm:

- **`globals: false`** trong Vitest: test phải `import { it, expect } from 'vitest'`, đổi lại
  `npm run lint` type-check luôn file test — mock sai chữ ký làm gãy build thay vì pass im lặng. Vì
  tắt globals nên RTL không tự đăng ký được `afterEach`, nên `setup.ts` gọi `cleanup()` **và**
  `localStorage.clear()` thủ công (`AuthProvider` đọc localStorage lúc render đầu, session sót lại sẽ
  rò sang test sau).
- `CustomWebApplicationFactory` sinh tên InMemory database **một lần rồi capture vào field** —
  `AddDbContext` có `optionsLifetime` mặc định là **Scoped**, nên viết `Guid.NewGuid()` trực tiếp trong
  lambda sẽ khiến mỗi DI scope có một database rỗng riêng.
- Hai bài học frontend đã trả giá: test mà assertion duy nhất là `expect(...).not.toThrow()` thì
  **không bao giờ fail được** (React 18 biến setState trên component đã unmount thành no-op im lặng);
  và test chỉ kiểm thứ **vắng mặt** vẫn pass ngon lành khi trang crash — mỗi assertion "không có X"
  phải đi kèm một assertion "có Y".

**Câu chốt cả bài:** "Năm chỗ khó nhất trong dự án này đều là đánh đổi có chủ ý, và mỗi chỗ đều có
một thứ giữ cho nó không bị phá — một test, một unique index, hoặc một phép đo."

---

# VÙNG KHÔNG NÓI

> Mọi thứ **phía trên** là 40 phút trình bày. Mọi thứ **phía dưới** không nói, chỉ mở ra khi bị hỏi.
> Phụ lục A là tài liệu học nền tảng ASP.NET Core / EF Core; mục Q&A là phần luyện trả lời chất vấn.

---

## Logging — KHÔNG NÓI

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

## Concurrency & unique index — KHÔNG NÓI

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

## Frontend gọi API — KHÔNG NÓI

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

## Configuration & secrets — KHÔNG NÓI

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

## Q&A — KHÔNG NÓI

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

## Phụ lục A — Nền tảng ASP.NET Core & EF Core — KHÔNG NÓI

Phần thân report giả định người đọc đã quen hai framework này. Phụ lục này giải thích các khái
niệm nền — nhưng **bằng chính code của dự án**, không phải bằng ví dụ trừu tượng, để đọc xong là
mở file ra đối chiếu được ngay.

Đọc theo thứ tự A1 → A6 cho ASP.NET Core, A7 → A12 cho EF Core.

---

### Phần I — ASP.NET Core

### A1. ASP.NET Core thực chất là gì

Không phải một web server cắm sẵn như IIS hay Apache. Nó là một **thư viện** biến chương trình
console C# bình thường thành ứng dụng web: bạn viết `Main`, gọi vài hàm dựng, và nó khởi động một
web server tên **Kestrel** lắng nghe HTTP.

Vì vậy `Program.cs` là file quan trọng nhất — nó là điểm bắt đầu thật sự của cả backend.

#### Hai giai đoạn tách bạch, và đây là chỗ dễ nhầm nhất

```csharp
var builder = WebApplication.CreateBuilder(args);

// ── GIAI ĐOẠN 1: ĐĂNG KÝ ──────────────────────────────
// Khai báo "ứng dụng này cần những gì". Chưa chạy gì cả.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();

var app = builder.Build();      // ← ranh giới: chốt sổ, không đăng ký thêm được nữa

// ── GIAI ĐOẠN 2: XỬ LÝ REQUEST ────────────────────────
// Khai báo "mỗi request đi qua những bước nào".
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseAuthentication();
app.MapControllers();

app.Run();                      // ← chặn tại đây, bắt đầu nghe HTTP
```

Mọi thứ **trước** `Build()` chạy đúng **một lần** lúc khởi động. Mọi thứ **sau** nó chạy **mỗi
request**. Nhầm hai giai đoạn là nguồn gốc của rất nhiều lỗi khó hiểu ở người mới.

Xem [Program.cs](backend/src/TripPlanner.WebApi/Program.cs) — có đánh số từng khối theo đúng thứ
tự này.

### A2. Dependency Injection — trái tim của ASP.NET Core

**Vấn đề nó giải quyết.** Không có DI, `TripsController` muốn có `TripService` thì phải tự tạo:

```csharp
// KHÔNG làm thế này
var service = new TripService(
    new TripRepository(new ApplicationDbContext(...)),
    new DestinationRepository(...), ...);
```

Controller khi đó phải biết cách dựng cả cây phụ thuộc bên dưới nó, và test thì không thay thế
được thứ gì.

**Cách DI làm.** Bạn chỉ **khai báo cái mình cần** ở constructor:

```csharp
public class TripsController : ControllerBase
{
    private readonly ITripService _tripService;

    public TripsController(ITripService tripService)   // "tôi cần một ITripService"
    {
        _tripService = tripService;
    }
}
```

Rồi ở giai đoạn đăng ký, nói cho container biết ai hiện thực cái đó:

```csharp
services.AddScoped<ITripService, TripService>();
```

Lúc có request, container tự dựng toàn bộ cây: thấy controller cần `ITripService` → dựng
`TripService` → thấy nó cần `ITripRepository` → dựng `TripRepository` → thấy nó cần
`ApplicationDbContext` → dựng nốt. Bạn không viết một dòng `new` nào.

**Vì sao dự án này phụ thuộc vào `interface` chứ không phải class:** vì đó là điều kiện để tầng
Application không cần biết EF Core tồn tại (Mở đầu). Container mới là nơi duy nhất biết
`ITripRepository` thật ra là EF Core.

#### Ba vòng đời — và hệ quả thật trong dự án

| Đăng ký | Nghĩa là | Dùng cho |
|---|---|---|
| `AddScoped` | **một instance cho mỗi HTTP request** | `DbContext`, repository, service |
| `AddSingleton` | một instance cho cả vòng đời ứng dụng | cache, `TimeProvider`, config |
| `AddTransient` | tạo mới mỗi lần được yêu cầu | object nhẹ, không giữ state |

`Scoped` không phải chi tiết vụn vặt — nó giải thích một hành vi mà bạn sẽ gặp trong code này:

> `ApplicationDbContext` là **Scoped**, nên trong **cùng một request**, `TripRepository` và
> `DestinationRepository` dùng **chung một** `DbContext`. Đó chính là lý do
> `TripRepository.UpdateAsync` flush luôn cả `Destination` mà repository kia vừa thêm — và là lý
> do `TripService.AddDestinationAsync` phải có ghi chú ORDER MATTERS.

Xem lại Mở đầu để hiểu vì sao điều đó lại quan trọng.

**Nơi đăng ký trong dự án này:** mỗi tầng một file `DependencyInjection.cs`, `Program.cs` chỉ gọi
ba dòng `AddApplication()` / `AddInfrastructure()` / `AddWebApi()`.

### A3. Middleware — request đi qua một dây chuyền

Mỗi request không nhảy thẳng vào controller. Nó chui qua một **chuỗi** các lớp xử lý, mỗi lớp
được quyền làm gì đó **trước** và **sau** phần còn lại của chuỗi:

```
Request  →  ExceptionHandling  →  CORS  →  Authentication  →  Authorization  →  Controller
                    ↑                                                              │
                    └──────────────── Response đi ngược trở ra ────────────────────┘
```

Một middleware nhìn như thế này — chú ý `await _next(context)` chính là "phần còn lại của dây
chuyền":

```csharp
public async Task InvokeAsync(HttpContext context)
{
    try
    {
        await _next(context);        // gọi các lớp phía sau + controller
    }
    catch (Exception ex)             // bắt được mọi lỗi ném ra từ bên trong
    {
        await HandleAsync(context, ex);
    }
}
```

Vì nó bọc `_next` trong `try/catch`, `ExceptionHandlingMiddleware` phải nằm **ngoài cùng** thì mới
bắt được lỗi của mọi thứ phía sau. Đó là lý do thứ tự khai báo trong `Program.cs` quan trọng — mục
4 giải thích từng vị trí.

**Hệ quả trong dự án:** không controller nào có `try/catch`. Service cứ ném exception mang ngữ
nghĩa nghiệp vụ (`NotFoundException`, `ConflictException`), middleware dịch sang HTTP status.

### A4. Controller, routing và model binding

```csharp
[ApiController]
[Route("api/[controller]")]        // [controller] = "Trips" → /api/trips
[Authorize]
public class TripsController : ControllerBase
{
    [HttpPost("{tripId:guid}/destinations")]
    public async Task<ActionResult<TripDestinationDto>> AddDestination(
        Guid tripId,                          // ① lấy từ URL
        AddDestinationRequest request,        // ② lấy từ body JSON
        CancellationToken cancellationToken)  // ③ framework tự truyền
        => Ok(await _tripService.AddDestinationAsync(tripId, request, cancellationToken));
}
```

Ba tham số, ba nguồn khác nhau, và **bạn không viết code lấy chúng** — ASP.NET Core tự làm, gọi là
**model binding**:

1. `tripId` khớp với `{tripId:guid}` trong route. Phần `:guid` là **ràng buộc**: URL không phải
   GUID sẽ 404 ngay, không vào tới method.
2. `request` được deserialize từ JSON body thành C# record. Sai kiểu → 400 tự động.
3. `CancellationToken` được framework cấp, và nó **huỷ khi client ngắt kết nối**. Đó là lý do gần
   như mọi method trong dự án đều nhận và chuyền tiếp nó xuống tận EF Core — user đóng tab thì
   query cũng dừng, không tốn tài nguyên vô ích.

`ActionResult<T>` cho phép trả về **hoặc** dữ liệu **hoặc** một status code: `Ok(x)` → 200,
`NoContent()` → 204, `CreatedAtAction(...)` → 201 kèm header `Location`.

### A5. Authentication vs Authorization

Hai từ hay bị lẫn, và ASP.NET Core tách chúng thành hai bước riêng:

| | Câu hỏi | Middleware | Kết quả |
|---|---|---|---|
| **Authentication** | *Bạn là ai?* | `UseAuthentication()` | **dựng** `HttpContext.User` từ token |
| **Authorization** | *Bạn có được phép không?* | `UseAuthorization()` | **đọc** `HttpContext.User`, so với `[Authorize]` |

Phải theo đúng thứ tự đó: đảo lại thì bước kiểm quyền chạy khi chưa ai dựng danh tính, và **mọi**
request đều bị coi là ẩn danh.

#### JWT hoạt động ra sao

JWT là một chuỗi gồm ba phần ngăn bởi dấu chấm: `header.payload.signature`.

- **payload** chứa các **claim** — mẩu thông tin về người dùng, ở đây là user id và email.
- **signature** được ký bằng `Jwt__Key` mà chỉ server biết.

Điểm mấu chốt: payload **không mã hoá**, ai cũng đọc được (thử dán vào jwt.io). Cái token bảo vệ
không phải là bí mật nội dung, mà là **tính toàn vẹn** — sửa một ký tự trong payload thì chữ ký
không khớp nữa và server từ chối. Nên **không bao giờ để dữ liệu nhạy cảm vào JWT**.

Hệ quả lớn: server **không cần lưu session**. Token tự mang đủ thông tin và tự chứng minh mình
thật. Đây là lý do mục Q&A nói "JWT là stateless nên auth scale sẵn".

Trong dự án, code nghiệp vụ không tự đọc claim. Nó gọi `ICurrentUserService.GetRequiredUserId()`,
và đó là ranh giới giữ cho tầng Application không dính tới `HttpContext`.

### A6. Configuration và `IOptions`

Cấu hình đến từ nhiều nguồn, chồng lên nhau theo thứ tự ưu tiên: biến môi trường (dự án này nạp
từ `.env`), rồi tham số dòng lệnh, rồi giá trị mà test host tự cấp.

Thay vì đọc chuỗi thô rải rác khắp nơi, ASP.NET Core cho **gom vào một class**:

```csharp
public class JwtSettings
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "TripPlanner";   // có default
    public int ExpiryMinutes { get; set; } = 60;
}
```

rồi inject qua `IOptions<JwtSettings>`. Lợi ích: **có kiểu dữ liệu** (sai tên key là gãy lúc
build, không phải lúc chạy), và **kiểm tra được lúc khởi động** — `Jwt__Key` để trống thì host
**từ chối chạy** thay vì âm thầm ký token bằng chuỗi rỗng.

Một chi tiết tinh tế đáng nhớ: dự án đọc `IOptions<JwtSettings>` **thay vì**
`builder.Configuration["Jwt:Key"]`. Lý do là **thời điểm** — đọc trực tiếp sẽ lấy giá trị **ngay
lúc dựng**, chạy trước khi test host kịp cấp giá trị riêng của nó. `IOptions` trì hoãn việc đó
tới lúc thật sự cần. Có test pin lại điều này (xem bảng test ở mục Kết).

---

### Phần II — EF Core

### A7. ORM, `DbContext` và `DbSet`

**EF Core là một ORM** — Object-Relational Mapper. Nó dịch qua lại giữa hai thế giới: class C# và
bảng quan hệ.

| C# | Database |
|---|---|
| class `Trip` | bảng `Trips` |
| property `Name` | cột `Name` |
| một object `Trip` | một dòng |
| `trip.Days` (collection) | quan hệ khoá ngoại sang bảng `ItineraryDays` |

`ApplicationDbContext` là **phiên làm việc với database**. Nó Scoped, tức mỗi HTTP request có một
cái riêng, và nó chết khi request kết thúc.

```csharp
public class ApplicationDbContext : DbContext
{
    public DbSet<Trip> Trips => Set<Trip>();      // "bảng Trips, truy vấn được bằng LINQ"
    public DbSet<User> Users => Set<User>();
}
```

Việc bảng nào ánh xạ ra sao (khoá chính, index, độ dài cột, quan hệ) không nằm trong entity mà ở
các file `*Configuration.cs` riêng — giữ cho `Trip` trong tầng Domain sạch, không dính chút EF Core
nào. Đó là điều kiện để `TripPlanner.Domain.csproj` **không có một package reference nào** (Mở đầu).

### A8. LINQ được dịch thành SQL — và cái bẫy `IQueryable`

Bạn viết C#, EF sinh SQL:

```csharp
_context.Trips.Where(t => t.UserId == userId)
// → SELECT * FROM "Trips" WHERE "UserId" = @userId
```

**Điểm quan trọng nhất: query chưa chạy ngay.** `Where` chỉ *dựng thêm* vào cây biểu thức. SQL chỉ
được gửi đi khi bạn gọi một method **kết thúc**: `ToListAsync()`, `FirstOrDefaultAsync()`,
`AnyAsync()`, `CountAsync()`.

Đây là cái bẫy kinh điển của người mới:

```csharp
// ✅ ĐÚNG — lọc chạy trong SQL, chỉ trip của user này đi qua dây
var mine = await _context.Trips.Where(t => t.UserId == userId).ToListAsync();

// ❌ SAI — ToListAsync() kéo TOÀN BỘ bảng Trips về RAM, rồi mới lọc bằng C#
var mine = (await _context.Trips.ToListAsync()).Where(t => t.UserId == userId);
```

Hai dòng cho ra **cùng kết quả**, nên test vẫn xanh, nhưng dòng dưới sẽ sập khi bảng lớn. Quy tắc:
`IQueryable` = *chưa chạy, còn dịch sang SQL được*; `IEnumerable`/`List` = *đã chạy rồi, từ đây trở
đi là C# trong RAM*.

Dự án tận dụng điều này ở [TripMappings.cs](backend/src/TripPlanner.Application/Features/Trips/TripMappings.cs):
mapping được khai báo dưới dạng `Expression`, nên `trip.Items.Count` được **dịch thành
`COUNT(*)`** thay vì nạp mọi item về rồi mới đếm.

### A9. Change tracker — vì sao không cần lệnh `UPDATE`

Đây là cơ chế đặc trưng nhất của EF Core, và cũng là thứ gây bất ngờ nhất.

Khi bạn query mà **không** có `AsNoTracking()`, `DbContext` **ghi nhớ** object trả về, kèm một
**bản chụp giá trị gốc**:

```csharp
var trip = await _context.Trips.FirstOrDefaultAsync(...);   // context nhớ trip này
trip.Name = "Tên mới";                                       // chỉ sửa object C#, chưa chạm DB
await _context.SaveChangesAsync();                           // ← so sánh với bản chụp, sinh UPDATE
```

`SaveChangesAsync` duyệt mọi entity đang được theo dõi, so hiện tại với bản chụp, rồi tự sinh
`INSERT`/`UPDATE`/`DELETE` cho đúng những gì đã đổi — và gửi **tất cả trong một transaction**.

Ba điều rút ra:

1. **Không có method `Update`** trong EF theo nghĩa thông thường. Bạn sửa object rồi lưu, thế thôi.
2. **`DbContext` bản thân nó chính là một Unit of Work** — nó gom thay đổi rồi commit một lượt. Đây
   là lý do dự án không thêm `IUnitOfWork` (mục Q&A).
3. **`AsNoTracking()` tắt cơ chế này.** Query nhanh hơn và tốn ít RAM hơn vì không phải giữ bản
   chụp — nhưng object trả về **không lưu được**.

Điểm 3 chính là cái bẫy đã được ghi hẳn vào doc comment của `ITripRepository`:

```csharp
var trip = await _trips.GetDetailsAsync(...);   // AsNoTracking!
trip.Name = "Tên mới";
await _trips.UpdateAsync(trip);                 // không lưu gì cả — và KHÔNG báo lỗi
```

Biên dịch sạch, chạy không exception, và không có câu `UPDATE` nào được gửi đi. Đường ghi phải
dùng `GetForUpdateAsync` (có tracking).

### A10. Navigation property và `Include`

`Trip.Days` và `Trip.Items` là **navigation property** — chúng biểu diễn quan hệ khoá ngoại dưới
dạng object C#.

Dự án **không bật lazy loading**, nên mặc định các collection này **rỗng** sau khi query. Muốn có
dữ liệu phải nói rõ — gọi là **eager loading**:

```csharp
await _context.Trips
    .Include(t => t.Days)                    // nạp kèm các ngày
        .ThenInclude(d => d.Items)           // và item của từng ngày
    .FirstOrDefaultAsync(...);
```

Tại sao không bật lazy loading cho tiện? Vì nó gây **N+1 query**: vòng lặp qua 30 item, mỗi lần
chạm `item.Destination` lại lặng lẽ bắn thêm một query — 31 lần đi database mà nhìn code không hề
thấy. `Include` bắt bạn nói trước mình cần gì, nên chi phí luôn nhìn thấy được.

Nhưng `Include` cũng có cái giá của nó, và đó là **cartesian explosion** — TRỤ 5 mổ xẻ chi tiết
với số đo thật. Đọc phần đó sau khi nắm A10.

### A11. Migration — quản lý phiên bản của schema

Sửa entity C# thì database phải đổi theo. EF Core làm việc đó bằng **migration**: so model hiện
tại với snapshot của lần trước, sinh ra file mô tả phần chênh lệch.

```bash
dotnet ef migrations add ThemCotMoi \
  --project src/TripPlanner.Infrastructure \
  --startup-project src/TripPlanner.WebApi
```

Mỗi migration có `Up()` (áp dụng) và `Down()` (quay lui), commit vào git như code thường. Dự án
này tự chạy migration lúc khởi động (`ApplyMigrationsAsync` trong `Program.cs`) — tiện cho môi
trường học tập, nhưng mục Kết có ghi vì sao production nên tách ra.

Một chi tiết dễ hiểu nhầm: migration `SetIdValueGeneratedNever` **rỗng**, không có lệnh SQL nào.
Nó vẫn phải giữ, vì nó mang thay đổi **metadata** vào file snapshot mà các migration sau sẽ diff
với. Xoá đi là migration tiếp theo tính chênh lệch sai.

### A12. Những cái bẫy EF Core đã gặp trong chính dự án này

Tất cả đều là chuyện thật, đều đã được ghi lại ở đâu đó trong report — bảng này gom lại để dễ tra:

| Bẫy | Biểu hiện | Vì sao | Mục |
|---|---|---|---|
| `AsNoTracking` + lưu | Không có `UPDATE`, **không báo lỗi** | Change tracker không biết object đó | A9, Mở đầu |
| Thiếu `ValueGeneratedNever` | `DbUpdateConcurrencyException` khi thêm mới | EF tưởng Guid do DB sinh → coi row mới là row cũ → phát `UPDATE` | Concurrency |
| Hai `Include` collection cùng cấp | Query chậm dần theo bình phương | Cartesian explosion | TRỤ 5 |
| `AsNoTracking` ở đường ghi | Sửa `trip.Items` mà `day.Items` không thấy | Không có identity resolution | TRỤ 5 |
| EF InMemory trong test | Test xanh nhưng production lỗi | InMemory **không** phải relational: bỏ qua unique index, `HasMaxLength`, `AsSplitQuery` | Kết |

Cái cuối đáng nhấn: EF InMemory tiện cho test nhưng **không phải database**. Nó không enforce
constraint, nên có những mảng code chỉ kiểm chứng được bằng suy luận trực tiếp trên Postgres —
hoặc bằng `TripPlanner.QueryBenchmarks`.
