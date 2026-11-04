# Tasklog

Şəxsi to-do app: prioritet və son tarix, kateqoriya və teqlər, alt tapşırıqlar,
təkrarlanan tapşırıqlar, **avtomatik dəyişiklik tarixçəsi**, gündəlik jurnal və statistika.

| Hissə    | Texnologiya |
|----------|-------------|
| Backend  | ASP.NET Core (.NET 10) Minimal API, EF Core, SQLite |
| Frontend | React 19 + TypeScript, Vite, TanStack Query, React Router |

## İşə salmaq (lokal)

Lazım olanlar: .NET 10 SDK, Node.js 20+, `dotnet-ef` aləti.

```bash
dotnet tool install --global dotnet-ef   # bir dəfə
```

### 1. Backend

```bash
cd backend/Tasklog.Api
dotnet ef migrations add InitialCreate   # yalnız ilk dəfə
dotnet run                               # http://localhost:5080
```

- İlk işə salınmada `tasklog.db` (SQLite) yaradılır, migration-lar tətbiq olunur və 4 kateqoriya (İş, Təhsil, Şəxsi, Layihələr) əlavə edilir.
- Development-də OpenAPI sənədi: `http://localhost:5080/openapi/v1.json`.
- Kompüterində .NET 10 yoxdursa, `Tasklog.Api.csproj`-da `TargetFramework`-ü `net9.0` və ya `net8.0` et, paket versiyalarını da uyğun (`9.0.*` / `8.0.*`) dəyiş. .NET 8-də `Guid.CreateVersion7()` yoxdur, onu `Guid.NewGuid()` ilə əvəz et.

### 2. Frontend

```bash
cd frontend
npm install
npm run dev      # http://localhost:5173  (/api → :5080 proxy)
```

Digər əmrlər: `npm test` (quick-add parser testləri), `npm run build`, `npm run typecheck`.

## Necə işləyir

### Avtomatik tarixçə (log)

Endpoint-lərin heç biri log yazmır. Log-u `Data/ActivityLogInterceptor.cs` yazır. Bu, EF Core `SaveChangesInterceptor`-udur:

1. Hər `SaveChanges`-dən əvvəl change tracker-də nəyin dəyişdiyinə baxır.
2. Task və subtask dəyişikliklərini `ActivityEntry` sətirlərinə çevirir. Hər sətirdə hansı sahənin dəyişdiyi, köhnə və yeni dəyəri olur.
3. Bu sətirlər həmin transaction-da yadda saxlanır.

Beləliklə, kodun harasında tapşırıq dəyişsə də, tarixçə unudulmur. Log-un task-a foreign key-i yoxdur, ona görə də tapşırıq silinəndə tarixçəsi qalır.

> `ExecuteUpdate` / `ExecuteDelete` change tracker-dən keçmir. Onları task və subtask üçün istifadə etmə, yoxsa həmin dəyişikliklər log-a düşməz.

### Vaxt zonaları

- Bazada bütün tarixlər UTC-dir.
- Brauzer öz vaxt zonasını `X-Time-Zone` header-i ilə göndərir (`Asia/Baku`).
- Server bu zonanı istifadəçinin "gün"ünə bağlı hesablamalarda işlədir: təkrarlanan tapşırığın növbəti tarixi, gün üzrə statistika və vərdiş seriyası.

### Təkrarlanan tapşırıqlar

Təkrarlanan tapşırığı tamamlayanda növbəti nüsxə yeni tapşırıq kimi yaradılır. Bütün nüsxələrin `SeriesId`-i eyni olur, vərdiş statistikası da buna əsasən hesablanır.

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

Parser: `frontend/src/lib/quickAdd.ts`, testləri yanındakı `quickAdd.test.ts` faylındadır.

## API qısaca

| Metod | Yol | Nə edir |
|-------|-----|---------|
| GET | `/api/tasks?status=&categoryId=&tag=&priority=&search=&dueBefore=&dueAfter=&completedAfter=` | siyahı |
| GET | `/api/tasks/counts?todayEnd=` | menyu sayğacları |
| POST / PUT / DELETE | `/api/tasks`, `/api/tasks/{id}` | yarat / dəyiş / sil |
| POST | `/api/tasks/{id}/complete`, `/api/tasks/{id}/reopen` | tamamla / yenidən aç |
| POST / PATCH / DELETE | `/api/tasks/{id}/subtasks[/{subId}]` | alt tapşırıqlar |
| GET / POST / PUT / DELETE | `/api/categories[/{id}]` | kateqoriyalar |
| GET | `/api/activity?taskId=&action=&cursor=&limit=` | tarixçə (cursor pagination) |
| GET | `/api/activity/summary?from=&to=` | gün xülasəsi |
| GET | `/api/activity/export?from=&to=` | CSV |
| GET / PUT | `/api/journal`, `/api/journal/{yyyy-MM-dd}` | gündəlik jurnal |
| GET | `/api/stats?from=&to=` | statistika |

## Struktur

```
backend/Tasklog.Api/
  Domain/       entity-lər və enum-lar
  Data/         AppDbContext, ActivityLogInterceptor
  Common/       TimeZoneResolver, Validation
  Features/     Tasks, Categories, Activity, Journal, Stats (hər biri öz endpoint-ləri ilə)
frontend/src/
  api/          fetch client, tiplər, React Query hook-ları
  lib/          tarix formatları, etiketlər, quick-add parser
  components/   Sidebar, TaskRow, TaskDetail, QuickAdd, ikonlar
  pages/        TasksPage, LogPage, StatsPage
```
