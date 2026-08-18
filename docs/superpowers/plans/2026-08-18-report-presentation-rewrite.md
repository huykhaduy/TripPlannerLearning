# Kế hoạch viết lại REPORT.md thành bản thuyết trình 40 phút

> **Cho người triển khai:** đọc spec kèm theo trước —
> [`docs/superpowers/specs/2026-08-18-report-presentation-rewrite-design.md`](../specs/2026-08-18-report-presentation-rewrite-design.md).
> Spec giữ *nội dung từng trụ*; kế hoạch này giữ *thứ tự làm và cách kiểm chứng*. Các bước dùng
> checkbox (`- [ ]`) để theo dõi.

**Mục tiêu:** Biến `REPORT.md` (1779 dòng, dạng tra cứu) thành bản nói được thành lời trong 40 phút,
gồm năm trụ, phần còn lại dán nhãn `KHÔNG NÓI`.

**Cách tiếp cận:** Viết lại tại chỗ theo từng mục, mỗi mục là một task. Mỗi task kết thúc bằng một
bước **kiểm chứng đối chiếu code thật**, không phải chạy test — đây là tài liệu, "test" của nó là
"mọi khẳng định kỹ thuật có khớp code hiện tại hay không".

**Công cụ kiểm chứng:** `grep`, `sed`, `dotnet test`, `npm test`, `dotnet build`.

## Ràng buộc toàn cục

- **KHÔNG commit.** Tác giả tự quản git history. Không có bước `git commit` nào trong kế hoạch này;
  cuối cùng chỉ báo lại diff để tác giả tự quyết.
- **Không phát minh nội dung.** Mọi khẳng định kỹ thuật phải đọc được từ code hiện tại. Khi mô tả một
  class, mở file đó ra đọc trước khi viết — không dựa vào mô tả trong bản REPORT.md cũ, vì chính nó
  là thứ đang được thay.
- **Tiếng Việt**, giọng trực tiếp, nêu lý do, không hoa mỹ. Giữ giọng của bản hiện tại.
- **Giữ nguyên nội dung vùng `KHÔNG NÓI`** — chỉ thêm nhãn vào heading, không viết lại bên trong.
- Mỗi trụ phải có đủ 5 thành phần: câu mở, hiện vật trung tâm, cơ chế từng bước, phản chứng, câu chốt.
- Mốc thời gian đặt ngay dưới heading mỗi mục, dạng blockquote: `> **6 phút** · 10:00 → 16:00`.

## Bố cục file sau khi xong

| Vùng | Mục | Nguồn |
|---|---|---|
| NÓI | Mở đầu | mục 1 + 2 hiện tại, nén |
| NÓI | TRỤ 1 — Hai ví dụ thật | **viết mới**, thay mục 3 |
| NÓI | TRỤ 2 — Middleware & bắt exception | mục 4 + 5 gộp |
| NÓI | TRỤ 3 — Validation ba tầng | mục 6 |
| NÓI | TRỤ 4 — Caching | mục 7 |
| NÓI | TRỤ 5 — Cartesian explosion & AsSplitQuery | mục 9 (phần AsSplitQuery) |
| NÓI | Kết — Testing & hạn chế | mục 11 + 14 |
| KHÔNG NÓI | Logging · Concurrency · Frontend · Config · Q&A · Phụ lục A | mục 8, 9 (phần còn), 10, 12, 13, Phụ lục A |

---

### Task 1: Lấy số case test thật

**Files:** không sửa file nào — thu thập dữ liệu cho Task 8.

- [ ] **Bước 1: Chạy test backend**

```bash
cd backend && dotnet test --nologo
```

Ghi lại: tổng số case, và số của từng project (`TripPlanner.UnitTests`, `TripPlanner.WebApi.Tests`).

- [ ] **Bước 2: Chạy test frontend**

```bash
cd frontend && npm test
```

Ghi lại: tổng số case và số file test.

- [ ] **Bước 3: So với số trong bản cũ**

Bản cũ ghi 371 backend (302 + 69) và 263 frontend / 28 file. Nếu lệch thì **dùng số mới**. Nếu có
case fail thì dừng lại báo tác giả — không viết "tất cả đều pass" khi không phải vậy.

---

### Task 2: Dựng khung, mục lục và Mở đầu

**Files:** Modify `REPORT.md` (thay từ dòng 1 đến hết mục 2, tức dòng ~211)

- [ ] **Bước 1: Viết header + mục lục mới**

Mục lục phải có mốc thời gian từng mục và tách rõ hai vùng. Dạng:

```markdown
## Mục lục

**Phần nói — 40 phút**

| # | Mục | Thời lượng | Mốc |
|---|---|---|---|
| — | [Mở đầu](#mở-đầu) | 4' | 0:00 |
| 1 | [TRỤ 1 — Hai ví dụ thật](#trụ-1--hai-ví-dụ-thật) | 6' | 4:00 |
...

**Phần không nói — tra khi bị hỏi**

- [Logging](#logging--không-nói)
- [Q&A](#qa--không-nói)
- [Phụ lục A — Nền tảng ASP.NET Core & EF Core](#phụ-lục-a--không-nói)
```

- [ ] **Bước 2: Viết mục Mở đầu (4')**

Nén mục 1 + 2 hiện tại. Giữ: bảng tech stack, danh sách endpoint, sơ đồ 4 tầng, bảng dependency rule
(`.csproj`). Bỏ: các tiểu mục dài của mục 2 (Domain giữ quy tắc, DI, composition root, feature layout,
repository pattern) — chúng sẽ xuất hiện tự nhiên trong TRỤ 1 qua chuỗi gọi thật.

Điểm nhấn giữ lại vì nó kiểm chứng được và gây ấn tượng: **Domain không có một `PackageReference` nào**.

- [ ] **Bước 3: Kiểm chứng bảng dependency rule**

```bash
grep -l "PackageReference" backend/src/TripPlanner.Domain/*.csproj
```

Kỳ vọng: không in ra gì (Domain không có package nào). Nếu có thì sửa lại bảng cho đúng thực tế.

- [ ] **Bước 4: Kiểm chứng danh sách endpoint**

```bash
grep -rn "HttpGet\|HttpPost\|HttpPut\|HttpDelete\|Route\|Authorize" backend/src/TripPlanner.WebApi/Controllers/
```

Đối chiếu từng dòng với bảng endpoint trong Mở đầu — nhất là `DestinationsController` phải **không**
có `[Authorize]`.

---

### Task 3: TRỤ 1 — Hai ví dụ thật (6')

**Files:** Modify `REPORT.md` (thay mục 3 hiện tại, dòng ~212–348)

**Consumes:** không gì từ task trước.
**Produces:** hai cây chuỗi gọi mà TRỤ 2–4 sẽ trỏ về ("phóng to bước này"). Tên dùng trong cây phải
khớp chính xác tên method thật, vì các trụ sau tham chiếu lại.

- [ ] **Bước 1: Đọc lại code của cả hai luồng**

```bash
sed -n '1,60p' backend/src/TripPlanner.WebApi/Controllers/DestinationsController.cs
sed -n '100,145p' backend/src/TripPlanner.Application/Features/Destinations/DestinationService.cs
sed -n '40,60p' backend/src/TripPlanner.WebApi/Controllers/TripsController.cs
grep -n "AddDestinationAsync" -A 40 backend/src/TripPlanner.Application/Features/Trips/TripService.cs
```

- [ ] **Bước 2: Viết ví dụ A — destination search**

Cây chuỗi gọi cho `GET /api/destinations/locations?query=paris`, mỗi nhánh ghi tầng, hai nhánh có mũi
tên trỏ sang TRỤ 3 và TRỤ 4. Nội dung chi tiết: xem spec, mục "TRỤ 1 · Ví dụ A".

Ba điểm dạy: validate chuỗi **đã trim**; key hạ chữ thường; giá trị cache là danh sách **đã xử lý xong**.

- [ ] **Bước 3: Viết ví dụ B — add to trip**

Cây chuỗi gọi cho `POST /api/trips/{tripId}/destinations`. Bốn điểm dạy: 404-không-403; ranh giới
Domain/Application (`NextSortOrderIn` + `HasDestinationIn` phát hiện, Application ném 409);
`ICurrentUserService` ở WebApi; và phản chứng `ORDER MATTERS`.

- [ ] **Bước 4: Kiểm chứng phản chứng ORDER MATTERS**

```bash
grep -n "ORDER MATTERS" -A 8 backend/src/TripPlanner.Application/Features/Trips/TripService.cs
```

Kỳ vọng: comment còn đó và nói đúng điều mình viết (repository chia sẻ một scoped `DbContext`, mỗi
write method tự `SaveChanges`). Nếu comment đã đổi thì viết theo code, không theo kế hoạch này.

- [ ] **Bước 5: Kiểm chứng mọi tên method trong hai cây là thật**

```bash
for m in SearchLocationsAsync GetCachedAsync TryGetAsync GetRequiredUserId \
         GetForUpdateAsync GetOrCreateDestinationAsync NextSortOrderIn \
         HasDestinationIn SaveWithDuplicateGuardAsync; do
  printf '%-32s ' "$m"; grep -rl "$m" backend/src --include=*.cs | head -1
done
```

Kỳ vọng: mỗi tên in ra một file. Tên nào trống là tên bịa — phải sửa.

---

### Task 4: TRỤ 2 — Middleware & bắt exception (6')

**Files:** Modify `REPORT.md` (thay mục 4 + 5, dòng ~349–440, thành một mục)

- [ ] **Bước 1: Đọc lại middleware**

```bash
cat backend/src/TripPlanner.WebApi/Middleware/ExceptionHandlingMiddleware.cs
grep -n "UseMiddleware\|UseCors\|UseAuthentication\|UseAuthorization\|MapControllers" backend/src/TripPlanner.WebApi/Program.cs
```

- [ ] **Bước 2: Viết mục**

Câu mở, 6 dòng `InvokeAsync`, mô hình búp bê Nga, bảng `switch`, `ProblemDetails`, hai chi tiết tinh
tế (`contentType` truyền vào `WriteAsJsonAsync`; chỉ nhánh 500 che message + log Error), phản chứng
thứ tự pipeline, câu chốt. Chi tiết: spec mục "TRỤ 2".

- [ ] **Bước 3: Kiểm chứng chi tiết contentType**

```bash
grep -n "WriteAsJsonAsync" -B 5 backend/src/TripPlanner.WebApi/Middleware/ExceptionHandlingMiddleware.cs
```

Kỳ vọng: `contentType: "application/problem+json"` được truyền làm **tham số**, kèm comment giải thích
vì sao không gán trước. Đây là điểm dễ viết sai nhất trong trụ này.

- [ ] **Bước 4: Kiểm chứng bảng exception → status khớp code**

```bash
grep -n "=> (HttpStatusCode" backend/src/TripPlanner.WebApi/Middleware/ExceptionHandlingMiddleware.cs
```

Đối chiếu từng dòng với bảng trong REPORT.md. Chú ý `ExternalServiceUnavailableException` và
`ConcurrencyException` **cố ý không** có trong bảng — nếu bảng liệt kê chúng thì sai.

---

### Task 5: TRỤ 3 — Validation ba tầng (5')

**Files:** Modify `REPORT.md` (thay mục 6, dòng ~441–493)

- [ ] **Bước 1: Đọc lại cây cầu và một validator**

```bash
cat backend/src/TripPlanner.Application/Common/Validation/ValidationExtensions.cs
grep -n "static readonly.*Validator\|Cascade\|WithMessage" backend/src/TripPlanner.Application/Features/Auth/AuthService.cs | head -20
```

- [ ] **Bước 2: Viết mục**

Câu mở, bảng ba tầng, đường đi 4 bước của message lỗi, cây cầu `ValidateAndThrowAppExceptionAsync`,
phản chứng (ném `ValidationException` của FluentValidation → nhánh `_` → **500 thay vì 400**), validator
không vào DI + điều gì đảo ngược quyết định đó, `Cascade.Stop`, hai sự vắng mặt cố ý. Chi tiết: spec
mục "TRỤ 3".

- [ ] **Bước 3: Kiểm chứng validator thật không đăng ký DI**

```bash
grep -n "AddValidatorsFromAssembly\|IValidator<" backend/src/TripPlanner.Application/DependencyInjection.cs
grep -n "FluentValidation" backend/src/TripPlanner.Application/TripPlanner.Application.csproj
```

Kỳ vọng: lệnh đầu không in gì; lệnh sau in `FluentValidation` **không** kèm
`.DependencyInjectionExtensions`. Nếu ngược lại thì cả mục này phải viết lại.

- [ ] **Bước 4: Kiểm chứng hai sự vắng mặt cố ý có test pin**

```bash
grep -rn "IsStillValid\|IsNotTheValidatorsJob" backend/tests/TripPlanner.UnitTests/
```

Kỳ vọng: tìm thấy các test pin sự vắng mặt. Nêu tên test trong REPORT.md để người nghe kiểm được.

---

### Task 6: TRỤ 4 — Caching (6')

**Files:** Modify `REPORT.md` (thay mục 7, dòng ~494–662)

- [ ] **Bước 1: Đọc lại policy và mechanism**

```bash
sed -n '30,100p' backend/src/TripPlanner.Application/Features/Destinations/DestinationService.cs
cat backend/src/TripPlanner.Application/Common/Interfaces/IStaleTolerantCache.cs
cat backend/src/TripPlanner.Application/Common/Caching/CacheEnvelope.cs
grep -n "class DistributedStaleTolerantCache" -A 40 backend/src/TripPlanner.Infrastructure/Caching/DistributedStaleTolerantCache.cs
```

- [ ] **Bước 2: Viết mục**

Câu mở, 16 dòng `GetCachedAsync`, ba nhánh, mẹo `when (stale is not null)`, tách policy/mechanism,
đọc-hạ-thành-miss nhưng ghi-thì-không, bốn tầng chống lỗi, Memory↔Redis bằng config, phản chứng
(để cache tự evict → hết hạn bị xoá → provider chết là hết đường), câu chốt TTL vs cửa sổ giữ.
Chi tiết: spec mục "TRỤ 4".

- [ ] **Bước 3: Kiểm chứng TTL từng endpoint**

```bash
grep -n "Ttl = TimeSpan" backend/src/TripPlanner.Application/Features/Destinations/DestinationService.cs
```

Ghi đúng con số vào REPORT.md (hiện: Image 24h, Locations 24h, Attractions 6h, Details 24h).

- [ ] **Bước 4: Kiểm chứng cửa sổ giữ dài hơn mọi TTL**

```bash
grep -rn "TimeSpan.FromDays\|AbsoluteExpiration" backend/src/TripPlanner.Infrastructure/Caching/
```

Kỳ vọng: cửa sổ giữ 7 ngày > TTL lớn nhất (24h). Đây là điều làm stale-better-than-down khả thi —
nếu số liệu khác thì sửa câu chốt cho khớp.

- [ ] **Bước 5: Kiểm chứng Application không reference JSON/cache package**

```bash
grep -n "System.Text.Json\|Caching.Abstractions" backend/src/TripPlanner.Application/TripPlanner.Application.csproj
grep -rn "using System.Text.Json" backend/src/TripPlanner.Application/
```

Kỳ vọng: cả hai không in gì. Đây là **bằng chứng kiểm chứng được** của việc tách policy/mechanism —
nếu nó in ra gì thì bỏ câu khẳng định đó khỏi REPORT.md.

---

### Task 7: TRỤ 5 — Cartesian explosion & AsSplitQuery (6')

**Files:** Modify `REPORT.md` (mục 9 phần AsSplitQuery, dòng ~773–971)

Nội dung đã tốt — task này chủ yếu **định hình lại theo khuôn**, không viết lại từ đầu.

- [ ] **Bước 1: Thêm câu mở + mốc thời gian, gom tiểu mục**

Câu mở: "Một câu LINQ trông vô hại làm Postgres trả về gấp nhiều lần số dòng cần thiết."
Gộp 8 tiểu mục `####` hiện tại thành tối đa 4, để 6 phút nói không phải nhảy qua 8 heading.

- [ ] **Bước 2: Thêm khối phản chứng**

Cái giá của `AsSplitQuery`: nhiều round trip, và các câu không cùng một snapshot nếu không bọc
transaction. Bản hiện tại có nói tới nhưng chưa đóng thành khối phản chứng theo khuôn.

- [ ] **Bước 3: Kiểm chứng số đo và project benchmark còn thật**

```bash
ls backend/tests/TripPlanner.QueryBenchmarks/
grep -n "AsSplitQuery" backend/src/TripPlanner.Infrastructure/Persistence/Repositories/TripRepository.cs
```

Kỳ vọng: project còn đó và `AsSplitQuery` còn trong repository. Nếu đã bị bỏ thì cả trụ này phải
viết lại theo hiện trạng.

- [ ] **Bước 4: Tách phần concurrency ra khỏi trụ**

Mục 9 hiện gộp concurrency + EF Core. Phần concurrency (dòng ~721–772) chuyển xuống vùng `KHÔNG NÓI`
ở Task 9, chỉ để lại một câu nhắc trong TRỤ 1 (`SaveWithDuplicateGuardAsync`).

---

### Task 8: Kết — Testing & hạn chế (3')

**Files:** Modify `REPORT.md` (mục 11 + 14 gộp thành mục Kết)

**Consumes:** số case test thật từ Task 1.

- [ ] **Bước 1: Viết phần Testing**

Bốn tầng test, và **số thật từ Task 1** — không bê số 371/263 của bản cũ nếu đã lệch. Giữ lý do chia
hai project theo *thứ cần để chạy* (có host hay không), không theo tên tầng.

- [ ] **Bước 2: Viết phần Hạn chế**

Giữ cả 5 hạn chế, nói nhanh. Không cắt — chủ động nêu hạn chế là điểm cộng khi bị chất vấn.

- [ ] **Bước 3: Kiểm chứng lại số case**

```bash
cd backend && dotnet test --nologo 2>&1 | grep -E "Passed!|Failed!|total:"
```

Số in ra phải khớp con số vừa viết vào REPORT.md.

---

### Task 9: Dán nhãn vùng KHÔNG NÓI và kiểm tra toàn file

**Files:** Modify `REPORT.md` (các mục 8, 9-concurrency, 10, 12, 13, Phụ lục A)

- [ ] **Bước 1: Thêm nhãn vào heading**

Sáu heading, giữ nguyên nội dung bên dưới:

```markdown
## Logging — KHÔNG NÓI · tra khi bị hỏi
## Concurrency & unique index — KHÔNG NÓI · tra khi bị hỏi
## Frontend gọi API — KHÔNG NÓI · tra khi bị hỏi
## Configuration & secrets — KHÔNG NÓI · tra khi bị hỏi
## Q&A — KHÔNG NÓI · phần luyện chất vấn
## Phụ lục A — Nền tảng ASP.NET Core & EF Core — KHÔNG NÓI · tài liệu học
```

- [ ] **Bước 2: Thêm một dải phân cách trước vùng KHÔNG NÓI**

Một dòng nói rõ: mọi thứ phía trên là 40 phút nói, mọi thứ phía dưới chỉ để tra.

- [ ] **Bước 3: Kiểm mục lục khớp heading thật**

```bash
grep -n '^#\{1,3\} ' REPORT.md
```

Từng dòng phải có mục tương ứng trong mục lục, và mọi anchor trong mục lục phải trỏ tới heading tồn tại.

- [ ] **Bước 4: Đếm từ vùng nói**

```bash
sed -n '1,/KHÔNG NÓI/p' REPORT.md | wc -w
```

Kỳ vọng: 5.500–6.500 từ (khoảng 40 phút). Vượt nhiều thì cắt, thiếu nhiều thì phần nào bị nén quá tay.

- [ ] **Bước 5: Kiểm mỗi trụ đủ 5 thành phần**

```bash
grep -n "Câu mở\|Phản chứng\|Câu chốt" REPORT.md
```

Kỳ vọng: mỗi nhãn xuất hiện 5 lần (một lần mỗi trụ).

- [ ] **Bước 6: Không sửa gì vào code**

```bash
git status --short
```

Kỳ vọng: chỉ `REPORT.md` và hai file trong `docs/superpowers/`. Bất kỳ file `.cs`/`.tsx` nào hiện ra
là sai — kế hoạch này chỉ sửa tài liệu.

---

## Tự soi kế hoạch

**Phủ spec:** cả 5 trụ + Mở đầu + Kết + vùng KHÔNG NÓI đều có task (2–9). Bảng "mục hiện tại đi đâu"
trong spec có 15 dòng; mỗi dòng thuộc một task: mục 1–2 → Task 2, mục 3 → Task 3, mục 4–5 → Task 4,
mục 6 → Task 5, mục 7 → Task 6, mục 9 → Task 7 + 9, mục 8/10/12/13/Phụ lục A → Task 9,
mục 11 + 14 → Task 8. Không dòng nào thiếu chỗ tới.

**Yêu cầu "đếm lại số case test"** của spec → Task 1 + Task 8 bước 3.

**Không placeholder:** mọi bước có lệnh cụ thể và kỳ vọng cụ thể. Các bước viết prose trỏ tới mục
tương ứng trong spec thay vì nhân bản nội dung — spec là nguồn duy nhất của *nội dung*, kế hoạch này
là nguồn của *thứ tự và cách kiểm chứng*.

**Nhất quán tên:** tên method dùng trong Task 3 (`SearchLocationsAsync`, `GetCachedAsync`,
`GetForUpdateAsync`, `NextSortOrderIn`, `HasDestinationIn`, `SaveWithDuplicateGuardAsync`,
`GetOrCreateDestinationAsync`, `GetRequiredUserId`, `TryGetAsync`) được Task 3 bước 5 kiểm bằng grep,
và Task 4–6 tham chiếu lại đúng các tên đó.
