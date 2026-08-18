# Viết lại REPORT.md thành bản thuyết trình 40 phút

**Ngày:** 2026-08-18
**Phạm vi:** `REPORT.md` (hiện 1779 dòng) — viết lại **tại chỗ**, không tạo file mới.

## Vấn đề

`REPORT.md` hiện là **tài liệu tra cứu**: bảng dày, mỗi mục một quyết định kèm lý do. Tốt để tra,
nhưng đọc một lượt thì không có mạch — không có mở đầu dẫn dắt, mọi mục nằm cùng một mức ưu tiên
nên không biết đâu là trọng tâm, và mục 3 (Luồng end-to-end, 136 dòng) kể lại từng bước request
chi tiết hơn mức cần cho một buổi trình bày.

Ba cơ chế khó nhất — middleware bắt exception, validation, caching — *đã có* nội dung, nhưng viết
theo kiểu tra cứu chứ chưa theo kiểu giảng. Ví dụ mục middleware chỉ có bảng "vì sao thứ tự này",
chưa dẫn người nghe thấy `try/catch` bọc `await _next()` hoạt động ra sao.

## Mục tiêu

Biến `REPORT.md` thành thứ **nói được thành lời trong 40 phút**, cho người nghe là **chính tác giả**
(dùng để ôn và tập nói). Vì vậy:

- Phần giải thích nền tảng ASP.NET Core / EF Core (Phụ lục A) **được giữ**, không cắt — tác giả
  đang học C#/ASP.NET nên đó là phần giá trị nhất.
- Mục Q&A **được giữ** làm phần luyện trả lời chất vấn.
- 40 phút nói khoảng 6.000 từ, ngắn hơn file rất nhiều. Nên vấn đề **không phải cắt ngắn** mà là
  **đánh dấu rõ đâu là phần nói, đâu là phần chỉ để tra khi bị hỏi**.

## Bố cục: năm trụ

```
0:00 - 4:00   Mở đầu   (4')    bài toán -> tech stack -> kiến trúc 4 tầng + dependency rule
4:00 -10:00   TRỤ 1    (6')    Hai ví dụ thật: destination search & add-to-trip
10:00-16:00   TRỤ 2    (6')    Middleware & bắt exception
16:00-21:00   TRỤ 3    (5')    Validation ba tầng
21:00-27:00   TRỤ 4    (6')    Caching stale-better-than-down
27:00-33:00   TRỤ 5    (6')    EF Core: cartesian explosion & AsSplitQuery
33:00-36:00   Kết      (3')    Testing -> hạn chế đã biết
36:00-40:00   Đệm      (4')    Chất vấn
```

**Trụ 1 đứng đầu là quyết định có chủ ý.** Nó dựng bản đồ class trước; bốn trụ sau mỗi trụ phóng to
đúng một điểm đã có trên bản đồ đó. Nhờ vậy trụ 2–5 không cần giải thích "cái này được gọi ở đâu"
nữa, và trụ 3 (Validation) đủ sức co xuống 5 phút.

Trụ 5 đứng cuối để bài kết thúc bằng **số đo thật trên Postgres** — phần duy nhất trong báo cáo có
số liệu chứng minh.

## Năm quy ước mới

Đây là thứ bản hiện tại thiếu hoàn toàn.

| Quy ước | Hình thức | Giải quyết gì |
|---|---|---|
| Mốc thời gian | `> 6 phút · 10:00 -> 16:00` ngay dưới heading mỗi mục | Tập nói biết mình nhanh hay chậm |
| **Câu mở** | Một câu in đậm, bật ra miệng được, mở đầu mỗi trụ | Không phải tự nghĩ câu dẫn khi đứng trước người nghe |
| **Phản chứng** | Khối cuối mỗi cơ chế: "nếu không làm thế thì sao" | Biến bảng tra thành bài giảng — hiểu cơ chế là hiểu cái gì vỡ nếu bỏ nó |
| Nhãn KHÔNG NÓI | `## Phụ lục A — KHÔNG NÓI · chỉ để tra khi bị hỏi` | Nhìn là biết mục nào không thuộc bài nói |
| Khuôn chung mỗi trụ | câu mở -> hiện vật trung tâm -> cơ chế từng bước -> phản chứng -> câu chốt | Tập ba lần cùng một nhịp thì thuộc nhanh |

## Nội dung từng trụ

### TRỤ 1 · Hai ví dụ thật (6')

Thay thế mục 3 hiện tại: bỏ lối kể chuyện từng bước, dùng **chuỗi gọi ở mức class** dạng cây, mỗi
nhánh ghi rõ tầng và trỏ tới trụ sẽ phóng to nó.

Ví dụ A — `GET /api/destinations/locations?query=paris` (công khai, không `[Authorize]`):

```
DestinationsController.SearchLocations                 (WebApi)
+- DestinationService.SearchLocationsAsync             (Application)
   +- SearchValidator.ValidateAndThrowAppExceptionAsync      -> TRỤ 3
   +- GetCachedAsync("loc:paris", LocationsTtl, ...)         -> TRỤ 4
   |  +- IStaleTolerantCache.TryGetAsync            (port)
   |  |  +- DistributedStaleTolerantCache            (Infrastructure)
   |  |     +- ResilientDistributedCache -> IDistributedCache (Memory | Redis)
   |  +- IDestinationProvider.SearchLocationsAsync   (port)
   |     +- GeoapifyClient                           (Infrastructure)
   +- DistinctBy -> OrderBy(RelevanceRank) -> Take(5)
```

Ba điểm dạy được: validate chuỗi **đã trim** (đúng chuỗi gửi cho provider); key hạ chữ thường nên
"Paris" và "paris" dùng chung một entry; giá trị cache là danh sách **đã xử lý xong**, nên một hit
bỏ qua luôn cả dedupe/rank/cap.

Ví dụ B — `POST /api/trips/{tripId}/destinations` (`[Authorize]`):

```
TripsController.AddDestination                         (WebApi)
+- TripService.AddDestinationAsync                     (Application)
   +- AddDestinationValidator.ValidateAndThrowAppExceptionAsync
   +- ICurrentUserService.GetRequiredUserId()          -> CurrentUserService (WebApi)
   +- ITripRepository.GetForUpdateAsync(tripId, userId)
   +- EnsureDayBelongsToTripAsync(...)
   +- GetOrCreateDestinationAsync(providerId)          <- upsert
   +- EnsureNotDuplicate(...)  -> Trip.HasDestinationIn (Domain)
   +- trip.Items.Add(new ItineraryItem {
   |      SortOrder = trip.NextSortOrderIn(dayId) })   <- Domain quyết SortOrder
   +- SaveWithDuplicateGuardAsync(trip)                <- catch ConcurrencyException
```

Bốn điểm dạy được:

1. `GetForUpdateAsync(tripId, userId)` lọc theo `userId`, nên "trip của người khác" và "trip không
   tồn tại" không phân biệt được → trả **404 chứ không 403**, không lộ sự tồn tại của tài nguyên.
2. Ranh giới Domain/Application: `SortOrder` do `Trip.NextSortOrderIn` quyết, trùng do
   `Trip.HasDestinationIn` *phát hiện*, nhưng `ConflictException` (409) lại do Application *ném* —
   409 là từ vựng của tầng HTTP, không phải của Domain.
3. `ICurrentUserService` implement ở **WebApi** chứ không Infrastructure — ngoại lệ có chủ ý duy nhất
   của quy tắc "Infrastructure implement interface của Application", vì nó cần `IHttpContextAccessor`.
4. **Phản chứng của trụ này** (comment `ORDER MATTERS` trong code): mọi repository dùng chung một
   scoped `DbContext` và mỗi write method tự gọi `SaveChanges` trên toàn bộ nó. Nên
   `GetOrCreateDestinationAsync` buộc phải chạy **trước** khi chạm vào graph của trip — đảo lại thì
   lần save của destination sẽ đẩy luôn thay đổi nửa vời của trip xuống DB.

### TRỤ 2 · Middleware & bắt exception (6')

Gộp mục 4 và mục 5 hiện tại — chúng là một chủ đề.

- **Câu mở:** "Controller trong dự án này không có một `try/catch` nào. Đây là cách làm được điều đó."
- **Hiện vật:** 6 dòng `InvokeAsync` của `ExceptionHandlingMiddleware`.
- **Mô hình để hiểu:** pipeline là **búp bê Nga lồng nhau**; `await _next(context)` chính là lời gọi
  xuống lớp trong, nên bọc nó bằng `try/catch` là bắt được mọi thứ phía sau. Rồi `switch` dịch
  exception sang status, rồi `ProblemDetails` (RFC 7807).
- Hai chi tiết tinh tế, cả hai đều có thật trong code:
  - `contentType` phải **truyền vào** `WriteAsJsonAsync`, không gán trước vào `Response.ContentType` —
    hàm này luôn tự ghi `Response.ContentType`, sẽ âm thầm ghi đè thành `application/json` và làm mất
    media type `application/problem+json`.
  - Chỉ nhánh 500 mới che message ngoài Development (message chưa map có thể chứa SQL, tên bảng,
    connection string) và **chỉ** nhánh 500 mới log mức Error (404/409/400 là hoạt động bình thường,
    log Error sẽ làm nhiễu tới mức không ai đọc log nữa).
- **Phản chứng:** đặt middleware này *sau* `UseCors` → lỗi phát sinh trong CORS lọt ra ngoài thành
  500 thô, không qua `ProblemDetails`. Đảo `UseAuthorization` lên trước `UseAuthentication` → chưa ai
  dựng `HttpContext.User`, nên mọi request bị coi là ẩn danh.
- **Câu chốt:** một chỗ duy nhất dịch exception sang HTTP; thêm loại exception mới thì sửa đúng một
  `switch`.

### TRỤ 3 · Validation ba tầng (5')

- **Câu mở:** "Ba loại kiểm tra, ba chỗ khác nhau, và ranh giới không được nhòe."
- **Hiện vật:** bảng ba tầng (chỉ nhìn input → validator; cần DB → service; bất biến entity → domain)
  cộng đường đi 4 bước của một message lỗi.
- **Cơ chế:** `ValidateAndThrowAppExceptionAsync` là **cây cầu**, tồn tại vì FluentValidation có
  `ValidationException` *của riêng nó* mà middleware không biết type đó. `result.ToDictionary()` gom
  failure thành `property -> string[]`, đúng hình dạng `ValidationException` của dự án mang theo.
- **Phản chứng (mạnh nhất trong bài):** ném thẳng `ValidationException` của FluentValidation thì
  middleware không nhận ra → rơi vào nhánh `_` → **500 thay vì 400**, và mất toàn bộ lỗi theo field.
  Một dòng `using` sai là đủ.
- Kèm: vì sao validator **không** đăng ký DI (stateless, không dependency, inject sẽ thành 10
  interface mỗi cái đúng một implementation) và **điều gì sẽ đảo ngược quyết định đó** (một validator
  cần dependency, ví dụ rule async truy vấn DB → chuyển riêng nó thành constructor parameter);
  `.Cascade(CascadeMode.Stop)`; và hai sự vắng mặt cố ý (login không có rule độ dài mật khẩu;
  register dùng cùng một message cho "để trống" và "sai định dạng").

### TRỤ 4 · Caching stale-better-than-down (6')

- **Câu mở:** "Provider ngoài chết thì trang vẫn phải mở được."
- **Hiện vật:** 16 dòng `GetCachedAsync`.
- **Ba nhánh:** còn tươi → trả ngay; hết hạn hoặc không có → gọi provider, lưu lại, trả;
  provider chết **và** có bản cũ → trả bản cũ. Mệnh đề `when (stale is not null)` là toàn bộ mẹo —
  không có bản cũ thì exception bay lên thành 500 một cách trung thực.
- **Tách policy khỏi mechanism:** Application giữ *chính sách* (TTL từng endpoint + luật fallback);
  adapter giữ *cơ chế* (JSON, byte array, `DistributedCacheEntryOptions`, cửa sổ giữ 7 ngày).
  Bằng chứng kiểm chứng được: Application không reference `System.Text.Json` lẫn
  `Microsoft.Extensions.Caching.Abstractions`.
- **Đọc lỗi thì hạ thành miss, ghi lỗi thì không** — cái đầu là entry sống lâu hơn một lần đổi hình
  DTO, cái sau là bug trong hợp đồng của chính mình.
- Bốn tầng chống lỗi; chuyển Memory sang Redis chỉ bằng config `Cache__Provider`.
- **Phản chứng:** nếu để cache tự evict theo TTL thì entry hết hạn bị **xoá** → provider chết là
  không còn gì để trả → 500. Đó chính là lý do tự kiểm TTL trong code thay vì giao cho cache, và là
  lý do cửa sổ giữ (7 ngày) phải dài hơn mọi TTL.
- **Câu chốt:** TTL trả lời "dữ liệu còn dùng được không", cửa sổ giữ trả lời "còn gì để cứu không" —
  hai câu hỏi khác nhau nên hai con số khác nhau.

### TRỤ 5 · Cartesian explosion & AsSplitQuery (6')

Giữ gần như nguyên nội dung hiện có (mục 9, phần `AsSplitQuery`), thêm câu mở, mốc thời gian và
phản chứng theo khuôn chung.

- **Câu mở:** "Một câu LINQ trông vô hại làm Postgres trả về gấp nhiều lần số dòng cần thiết."
- **Cơ chế:** SQL chỉ biết trả bảng phẳng → hai nhánh `Include` cùng cấp → tích chéo → số dòng bậc
  hai theo **item** (không phải ngày × item — giữ nguyên phần giải thích vì sao).
- `AsSplitQuery` tách thành nhiều câu, EF khâu lại bằng identity resolution.
- **Phản chứng / cái giá:** nhiều round trip, và các câu không cùng một snapshot nếu không bọc
  transaction.
- **Câu chốt:** số đo thật trên Postgres + project `TripPlanner.QueryBenchmarks` để đo lại được.

### Kết (3')

Testing 4 tầng, 371 case backend + 263 frontend; rồi 5 hạn chế đã biết, nói nhanh. Phần hạn chế
**không cắt** — chủ động nêu hạn chế của mình là điểm cộng khi bị chất vấn.

## Mục hiện tại đi đâu

Không mục nào bị bỏ sót.

| Mục hiện tại | Đi đâu |
|---|---|
| 1. Tổng quan & tech stack | Mở đầu (nén) |
| 2. Kiến trúc backend | Mở đầu (giữ bảng dependency rule, nén phần còn lại) |
| 3. Luồng end-to-end | **Thay bằng TRỤ 1** (chuỗi gọi mức class) |
| 4. Middleware & HTTP pipeline | TRỤ 2 |
| 5. Error handling | TRỤ 2 (gộp — cùng một chủ đề) |
| 6. Validation | TRỤ 3 |
| 7. Cache | TRỤ 4 |
| 8. Logging | KHÔNG NÓI (một câu trong TRỤ 2 về log Error) |
| 9. Concurrency & EF Core | Phần `AsSplitQuery` sang TRỤ 5; phần concurrency sang KHÔNG NÓI (nhắc một câu ở TRỤ 1) |
| 10. Frontend | KHÔNG NÓI |
| 11. Testing | Kết |
| 12. Configuration & secrets | KHÔNG NÓI |
| 13. Q&A | KHÔNG NÓI — phần luyện chất vấn |
| 14. Hạn chế đã biết | Kết |
| Phụ lục A (ASP.NET/EF Core) | KHÔNG NÓI — giữ nguyên, tài liệu học |

## Ràng buộc

- **Không phát minh nội dung.** Mọi khẳng định kỹ thuật phải khớp code hiện tại. Trụ 1 đã tra bằng
  cách đọc `DestinationsController`, `DestinationService`, `TripsController`, `TripService`; khi viết
  các trụ còn lại thì đọc lại code tương ứng thay vì dựa vào mô tả trong bản báo cáo cũ.
- **Giữ số liệu đã có.** Số đo Postgres, TTL từng endpoint — không làm tròn lại, không đoán. Nếu một
  con số không kiểm chứng được thì bỏ hẳn chứ không viết áng.
- **Đếm lại số case test trước khi viết phần Kết.** Bản hiện tại ghi 371 backend (302 UnitTests +
  69 WebApi.Tests) và 263 frontend, kèm chính lời nhắc tự xác nhận lại. Chạy `dotnet test` và
  `npm test`, lấy số thật; đây là con số dễ lệch nhất trong cả báo cáo vì nó đổi theo mỗi commit
  thêm test, và là con số reviewer dễ kiểm tra nhất.
- **Tiếng Việt**, giữ giọng của bản hiện tại (trực tiếp, nêu lý do, không hoa mỹ).
- Mục lục phải khớp bố cục mới, kèm mốc thời gian từng mục.
- **Không commit** trừ khi tác giả yêu cầu.

## Cách xác nhận đạt

1. Đếm từ vùng "nói" (mở đầu + 5 trụ + kết): nằm trong khoảng 5.500–6.500 từ (khoảng 40 phút).
2. Mỗi trụ có đủ 5 thành phần của khuôn: câu mở, hiện vật, cơ chế, phản chứng, câu chốt.
3. Mọi mục ngoài vùng nói đều có nhãn `KHÔNG NÓI`.
4. Mục lục khớp heading thật (kiểm bằng `grep -n '^#\{1,4\} ' REPORT.md`).
5. Không mục nào trong bảng "đi đâu" bị mất nội dung mà không có chỗ tới.
