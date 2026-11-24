# Appointments & Relational Scheduling Architecture

This document describes the relational Appointments & Live Queue module spanning `Occlusa.Shared`, `Occlusa.Api`, `Occlusa.Desktop`, and Supabase PostgreSQL migrations `002`, `003`, and `004`.

---

## 1. Domain Contracts & Capabilities (`Occlusa.Shared`)

- **`DentistDto`**: Branch practitioner record (`Id`, `BranchId`, `FullName`, `Specialty`, `IsActive`, `SupportedTreatmentIds`). When `SupportedTreatmentIds` is populated, `AppointmentCalendarHelper.CanDentistPerformTreatment` restricts practitioner selection to procedures the dentist performs.
- **`TreatmentDto`**: Clinic procedure catalog (`Id`, `Name`, `DefaultDurationMinutes`, `DefaultPriceMyr`, `IsActive`).
- **`VisitType` & `VisitActivityEntryDto`**: Tracks whether a visit originated as `Booked`, `WalkIn`, or `AiBooked`, and appends structured `{ Timestamp, Action, StaffName, Details }` milestones for per-visit staff accountability.
- **`AppointmentQueueItemDto` / `CreateAppointmentRequest` / `UpdateAppointmentRequest`**: Carry relational `DentistId`, `TreatmentId`, `DurationMinutes`, `Notes`, `VisitType`, milestone timestamps (`ArrivedAt`, `SeatedAt`, `CheckoutAt`, `CompletedAt`), staff attribution fields (`CreatedBy`, `CheckedInBy`, `SeatedBy`, `CheckoutBy`, `CompletedBy`, `LastUpdatedBy`), `ActivityLog`, and computed `EndsAt`, while retaining backward-compatible `DoctorName` and `ProcedureSummary` labels.
- **`RegisterPatientAndAppointmentRequest`**: Combines `UpsertPatientRequest` and `CreateAppointmentRequest` for atomic front-desk registration + booking via `POST /api/appointments/register-and-book`.
- **`ClinicDashboardSnapshotDto`**: Returns synchronized branch `Dentists` and `Treatments` catalogs alongside active queue items and clinical state.

---

## 2. Database Schema & Repositories (`supabase/migrations`, `Occlusa.Api`)

- **`supabase/migrations/002_appointments_dentists_treatments.sql`**: Adds `public.dentists`, `public.treatments`, RLS policies scoped by `clinic_group_id` / `branch_id`, `prevent_hard_delete()` guards, relational columns on `public.appointments`, and seeded catalog rows.
- **`supabase/migrations/003_dentist_treatment_capabilities.sql`**: Adds `supported_treatment_ids UUID[]` to `public.dentists` and assigns procedure sets across the seeded practitioners (`Dr. Aina Rahman`, `Dr. Marcus Tan`, `Dr. Priya Nair`).
- **`supabase/migrations/004_live_queue_walkins_and_attribution.sql`**: Adds `visit_type`, milestone timestamps (`arrived_at`, `seated_at`, `checkout_at`, `completed_at`), staff attribution columns (`created_by`, `checked_in_by`, `seated_by`, `checkout_by`, `completed_by`, `last_updated_by`), and `activity_log JSONB`, and backfills legacy `'Arrived'` rows into `'Waiting'`.
- **`InMemoryOcclusaRepository` & `NpgsqlSupabaseRepository`**: Mirror identical treatment capabilities, milestone attribution via `ClinicDomainHelpers.ApplyStatusMilestone`, soft-cancel appointments via `QueueStatus.Cancelled`, allow unrestricted multi-chair/overlapping bookings (`no overlap rejection`), and broadcast SignalR `QueueUpdated` events on create, update, status transition, and register-and-book operations.
- **`Program.cs`**: Sets `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true` so optional empty strings (`Notes`, `Phone`, `Address`, `Allergies`) succeed without HTTP 400 validation errors under `<Nullable>enable</Nullable>`.

---

## 3. 4-Step Front-Desk Booking Workflow (`AppointmentDetailModal`)

1. **Step 1 — Treatment & Eligible Dentist**: Selecting a `Treatment` automatically updates `EligibleDentists` via `AppointmentCalendarHelper.GetEligibleDentists`. Selecting a treatment in the Appointments workspace toolbar also narrows the toolbar `Dentist` filter and **Day View** columns to qualified practitioners.
2. **Step 2 — Date & Dentist Schedule Availability**: Choosing a target date computes a fixed 3x3 grid of hourly slots (`09:00`–`17:00`) via `AppointmentCalendarHelper.GetDentistDaySlotAvailability`, displaying `Open` vs `Booked (N)` (ignoring `QueueStatus.Cancelled`) with one-click time selection.
3. **Step 3 — Flexible Patient Auto-Suggest Lookup**: Typing into the patient lookup box invokes `AppointmentCalendarHelper.MatchesPatientQuery` across **IC number** (matching with or without hyphens), **Full Name**, and **Phone** digits (requiring at least 3 digits for numeric normalization), displaying up to 4 live suggestions without scrolling.
4. **Step 4 — Confirm Existing or Inline Register New Patient**: Selecting a suggested patient locks the confirmed patient card and reveals `MedicalAlertsBanner` (`ShowPatientSafetyBanner = true`; hidden in Create mode before selection and hidden on the main Appointments overview via `IsAppointmentsWorkspace`). If no patient matches, an inline `+ Register New Patient` prompt pre-fills the typed IC/name/phone, parses Malaysian DOB/gender via `MyKadParser`, and commits atomically via `POST /api/appointments/register-and-book`.

---

## 4. Zero-Scrollbar Calendar Projections & API Lifecycle

- **Calendar & Table Projections (`AppointmentCalendarHelper` & `AppointmentsViewModel`)**:
  - **15-Minute Proportional Timeline (Day View & Week View)**: Renders 4 rows per hour (`15m` per row across `Full Day` `09:00–18:00` = 36 rows, `AM` `09:00–13:30` = 18 rows, or `PM` `13:30–18:00` = 18 rows) via `ScheduleTimelinePanel` and `TimelineBackdropControl` on floating rounded lane surfaces with soft inset hour/half-hour rhythm guides (no spreadsheet gridlines or scrollbars). A 2-hour (`14:00–16:00`) booking spans 8 rows proportionally.
  - **Overlap Column Splitting (`ComputeTimelineLayout`)**: Automatically clusters overlapping appointments within a column and splits them cleanly into side-by-side lanes while allowing back-to-back bookings to reuse freed lanes and routing excess concurrency to `+N more`.
  - **Cross-Date Drag-and-Drop Rescheduling**: Appointments can be dragged across dates in **Week View**, onto the 7-day quick-date pill strip in **Day View**, or across cells in **Month View**, as well as vertically within a dentist column to adjust 15-minute start times. Cross-dentist drops are blocked via `AppointmentCalendarHelper.IsSameDentist` and persisted via `BuildDragRescheduleRequest`.
  - **Month View & Table View**: Monday-aligned 7-column month grid with drag-reschedule support, plus 4-column `UniformPagedGrid` (`Date & Time` | `Patient` | `Dentist` | `Treatment & Status`) scoped to `Day | Week | Month | All`.
- **Live Queue & Shared Walk-In Synchronization**:
  - `AppointmentCalendarHelper.IsTodayForLiveQueue` scopes **Live Queue** to today's local scheduled/active visits while future dates remain on **Appointments**.
  - `AppointmentCalendarHelper.NormalizeQueueStatus` merges legacy `QueueStatus.Arrived` into `QueueStatus.Waiting`, and `AppointmentCalendarHelper.SortLiveQueue` keeps active visits on top (ordered by `AwaitingCheckout` -> `InChair` -> `Waiting` by longest wait -> `AiBookedPending` -> `Scheduled`) while pushing `Completed` and `Cancelled` rows to the bottom.
  - Walk-in visits registered from Live Queue (`VisitType.WalkIn`, `"No Appointment"` in `Appt Time`) share the `appointments` table so they render on today's **Appointments Calendar** as dashed `Walk-In` blocks and count against `GetDentistDaySlotAvailability` (`"(Walk-In)"` summary) to prevent phone double-bookings.
- **Embedded Local API Host (`LocalApiHostService`) & Auto-Recovery**: When `Occlusa.Desktop` targets a loopback URL, `LocalApiHostService` probes `/api/health` (verifying `HealthStatusDto`) to reuse an external `Occlusa.Api` if running, or hosts `Occlusa.Api` in-process via `OcclusaApiApplicationFactory` with automatic ephemeral loopback port fallback (`http://127.0.0.1:0`) if the configured port is busy. `OcclusaApiClient` bypasses system proxies for loopback HTTP and SignalR connections, recovers transparently on `HttpRequestException` by ensuring/rebinding the local API URL before retrying, and `LocalApiHostService.Dispose` stops the embedded host cleanly off the WPF UI thread on shutdown.

