# Tasklog

Şəxsi to-do app. Funksiyalar:

- prioritet və son tarix, kateqoriya və teqlər;
- alt tapşırıqlar və təkrarlanan tapşırıqlar;
- **avtomatik dəyişiklik tarixçəsi**;
- gündəlik jurnal və statistika.

Bir şifrə ilə qorunur, istənilən cihazdan brauzerdə açılır.

| Hissə    | Texnologiya |
|----------|-------------|
| Backend  | ASP.NET Core (.NET 10) Minimal API, EF Core, PostgreSQL |
| Frontend | React 19 + TypeScript, Vite, TanStack Query, React Router |
| Hosting  | Bir Docker container (API + React), Render (pulsuz) + Neon PostgreSQL (pulsuz) |

## Cloud-a deploy (pulsuz)

Bu addımlar bir dəfə edilir. Sonra `main`-ə hər push avtomatik deploy olunur.

1. **GitHub.** Kodu repo-ya push et. İlk push-da GitHub Actions EF Core migration-ı yaradıb repo-ya commit edir. CI yaşıl olana qədər gözlə.
2. **Neon** ([neon.com](https://neon.com)): proyekt yarat, region **AWS Europe (Frankfurt)** olsun. Dashboard-da **Connect** düyməsini bas, **Connection pooling-i söndür** və connection string-i kopyala (`postgresql://…`).
3. **Render** ([render.com](https://render.com)): **New → Blueprint** seç və bu repo-nu göstər. Render `render.yaml`-ı oxuyub iki dəyər soruşacaq:
   - `ConnectionStrings__Default` — Neon-dan kopyaladığın connection string;
   - `Auth__Password` — girişdə istifadə edəcəyin şifrə (ən azı 10 simvol).
4. Deploy bitəndə Render `https://tasklog-xxxx.onrender.com` kimi bir link verəcək. Linki aç və şifrə ilə daxil ol.

**Pulsuz planın şərtləri:**
- Render 15 dəqiqə istifadə olunmayanda app-i yatırır. Yatandan sonra ilk açılış ~1 dəqiqə çəkir, sonra normal sürətlə işləyir. Data Neon-dadır, ona görə yatmaq heç nəyi itirmir.
- Neon pulsuz planda 1 GB yer verir. Bu app üçün bu, uzun illər deməkdir.

**Şifrəni dəyişmək:** Render → Environment bölməsində `Auth__Password`-u dəyiş. Bütün cihazlardakı köhnə girişlər avtomatik etibarsız olur.

## Lokal inkişaf

Lazım olanlar: .NET 10 SDK, Node.js 22+, Docker.

```bash
docker compose up -d db                 # PostgreSQL
cd backend/Tasklog.Api && dotnet run    # http://localhost:5080, şifrə: dev
cd frontend && npm install && npm run dev   # http://localhost:5173
```

Bütün app-i lokal olaraq bir container kimi işə salmaq üçün: `docker compose up --build`. Ünvan `http://localhost:8080`, şifrə `local-password`.

**Testlər.** PostgreSQL lazımdır, ona görə əvvəlcə `docker compose up -d db` et:
- `dotnet test` — backend: inteqrasiya testləri real app və real PostgreSQL ilə işləyir;
- `cd frontend && npm test` — frontend.

**Yeni migration.** Model dəyişəndə:

```bash
dotnet tool install --global dotnet-ef   # bir dəfə
cd backend/Tasklog.Api
dotnet ef migrations add <Ad>
```

CI model ilə migration-ların uyğun olub-olmadığını yoxlayır.

## Necə işləyir

### Avtomatik tarixçə (log)

Endpoint-lərin heç biri log yazmır. Log-u `Data/ActivityLogInterceptor.cs` yazır. Bu, EF Core `SaveChangesInterceptor`-udur:

1. Hər `SaveChanges`-dən əvvəl change tracker-də nəyin dəyişdiyinə baxır.
2. Task və subtask dəyişikliklərini `ActivityEntry` sətirlərinə çevirir. Hər sətirdə hansı sahənin dəyişdiyi, köhnə və yeni dəyəri olur.
3. Bu sətirlər həmin transaction-da yadda saxlanır.

Log-un task-a foreign key-i yoxdur, ona görə də tapşırıq silinəndə tarixçəsi qalır.

> `ExecuteUpdate` / `ExecuteDelete` change tracker-dən keçmir. Onları task və subtask üçün istifadə etmə, yoxsa həmin dəyişikliklər log-a düşməz.

### Giriş

Tək istifadəçi və bir şifrə var (`Auth__Password`):

- Giriş HttpOnly, `SameSite=Strict` cookie ilə saxlanır. Cookie 30 gün etibarlıdır və hər istifadədə uzanır.
- Cookie-ni şifrələyən açarlar (ASP.NET Data Protection) database-də saxlanır. Ona görə də app yatıb oyananda giriş itmir.
- Login cəhdləri dəqiqədə 10-la məhdudlaşdırılıb.
- Şifrə dəyişəndə köhnə cookie-lər rədd olunur.

### Vaxt zonaları

- Bazada bütün tarixlər UTC-dir.
- Brauzer öz vaxt zonasını `X-Time-Zone` header-i ilə göndərir (`Asia/Baku`).
- Server bu zonanı istifadəçinin "gün"ünə bağlı hesablamalarda işlədir: təkrarlanan tapşırığın növbəti tarixi, gün üzrə statistika və vərdiş seriyası.

### Təkrarlanan tapşırıqlar

Təkrarlanan tapşırığı tamamlayanda növbəti nüsxə yeni tapşırıq kimi yaradılır. Bütün nüsxələrin `SeriesId`-i eyni olur, vərdiş statistikası da buna əsasən hesablanır. Gecikmiş tapşırığı gec tamamlasan, növbəti nüsxə keçmiş tarixə yox, gələcəyə düşür.

### Sürətli əlavə

Sürətli əlavə sətrində bu işarələr tanınır:

| Yazırsan | Nə olur |
|----------|---------|
| `#teq` | teq |
| `!yüksək` / `!orta` / `!aşağı` | prioritet |
| `@İş` | kateqoriya |
| `bu gün` / `sabah` / `birigün` / `20.11` | gün |
| `15:00` | saat |
| `hər gün` / `iş günləri` / `hər həftə` / `hər ay` | təkrar |

## API qısaca

`/api/auth/login` və `/health` istisna olmaqla hamısı giriş tələb edir.

| Metod | Yol | Nə edir |
|-------|-----|---------|
| POST | `/api/auth/login`, `/api/auth/logout` · GET `/api/auth/me` | giriş |
| GET | `/api/tasks?status=&categoryId=&tag=&priority=&search=&dueBefore=&dueAfter=&completedAfter=` | siyahı |
| GET | `/api/tasks/counts?todayEnd=` | menyu sayğacları |
| POST / PUT / DELETE | `/api/tasks`, `/api/tasks/{id}` | yarat / dəyiş / sil |
| POST | `/api/tasks/{id}/complete`, `/api/tasks/{id}/reopen` | tamamla / yenidən aç |
| POST / PATCH / DELETE | `/api/tasks/{id}/subtasks[/{subId}]` | alt tapşırıqlar |
| GET / POST / PUT / DELETE | `/api/categories[/{id}]` | kateqoriyalar |
| GET | `/api/activity?taskId=&action=&cursor=&limit=` | tarixçə (cursor pagination) |
| GET | `/api/activity/summary?from=&to=` · `/api/activity/export` | gün xülasəsi · CSV |
| GET / PUT | `/api/journal`, `/api/journal/{yyyy-MM-dd}` | gündəlik jurnal |
| GET | `/api/stats?from=&to=` | statistika |

## Struktur

```
backend/Tasklog.Api/
  Domain/       entity-lər və enum-lar
  Data/         AppDbContext, ActivityLogInterceptor, connection string
  Common/       TimeZoneResolver, Validation
  Features/     Auth, Tasks, Categories, Activity, Journal, Stats
backend/Tasklog.Api.Tests/   inteqrasiya + unit testlər
frontend/src/
  api/          fetch client, tiplər, React Query hook-ları
  lib/          tarix formatları, etiketlər, quick-add parser
  components/   Sidebar, TaskRow, TaskDetail, QuickAdd, ikonlar
  pages/        Login, Tasks, Log, Stats
Dockerfile, render.yaml, docker-compose.yml, .github/workflows/ci.yml
```
