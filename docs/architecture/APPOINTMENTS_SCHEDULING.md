# Appointments & Relational Scheduling Architecture

This document describes the relational Appointments module spanning `Occlusa.Shared`, `Occlusa.Api`, `Occlusa.Desktop`, and Supabase PostgreSQL migrations `002` and `003`.

---

## 1. Domain Contracts & Capabilities (`Occlusa.Shared`)

- **`DentistDto`**: Branch practitioner record (`Id`, `BranchId`, `FullName`, `Specialty`, `IsActive`, `SupportedTreatmentIds`). When `SupportedTreatmentIds` is populated, `AppointmentCalendarHelper.CanDentistPerformTreatment` restricts practitioner selection to procedures the dentist performs.
- **`TreatmentDto`**: Clinic procedure catalog (`Id`, `Name`, `DefaultDurationMinutes`, `DefaultPriceMyr`, `IsActive`).
- **`AppointmentQueueItemDto` / `CreateAppointmentRequest` / `UpdateAppointmentRequest`**: Carry relational `DentistId`, `TreatmentId`, `DurationMinutes`, `Notes`, and computed `EndsAt`, while retaining backward-compatible `DoctorName` and `ProcedureSummary` labels.
- **`RegisterPatientAndAppointmentRequest`**: Combines `UpsertPatientRequest` and `CreateAppointmentRequest` for atomic front-desk registration + booking via `POST /api/appointments/register-and-book`.
- **`ClinicDashboardSnapshotDto`**: Returns synchronized branch `Dentists` and `Treatments` catalogs alongside active queue items and clinical state.

---

## 2. Database Schema & Repositories (`supabase/migrations`, `Occlusa.Api`)

- **`supabase/migrations/002_appointments_dentists_treatments.sql`**: Adds `public.dentists`, `public.treatments`, RLS policies scoped by `clinic_group_id` / `branch_id`, `prevent_hard_delete()` guards, relational columns on `public.appointments`, and seeded catalog rows.
- **`supabase/migrations/003_dentist_treatment_capabilities.sql`**: Adds `supported_treatment_ids UUID[]` to `public.dentists` and assigns procedure sets across the seeded practitioners (`Dr. Aina Rahman`, `Dr. Marcus Tan`, `Dr. Priya Nair`).
- **`InMemoryOcclusaRepository` & `NpgsqlSupabaseRepository`**: Mirror identical treatment capabilities, soft-cancel appointments via `QueueStatus.Cancelled`, allow unrestricted multi-chair/overlapping bookings (`no overlap rejection`), and broadcast SignalR `QueueUpdated` events on create, update, and register-and-book operations.
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
  - **Day View**: Qualified dentist columns against 9 fixed non-scrolling slots (`Full Day`, `AM`, or `PM`), clickable empty slots to quick-book with the active treatment filter, and `+{N} more` jump to filtered **Table** mode.
  - **Week View & Month View**: Monday-aligned 7-column grids with viewport-capped event stacks and one-click drilldown into **Day View**.
  - **Table View**: 4-column `UniformPagedGrid` (`Date & Time` | `Patient` | `Dentist` | `Treatment & Status`) scoped to `Day | Week | Month | All`.
- **Live Queue Isolation**: `AppointmentCalendarHelper.IsTodayForLiveQueue` ensures the **Live Queue** floor board only displays today's local scheduled/active appointments while future dates remain on **Appointments**.
- **Embedded Local API Host (`LocalApiHostService`)**: When `Occlusa.Desktop` starts, it checks `http://localhost:5188/api/health`, uses `UseProxy = !baseUri.IsLoopback` in `OcclusaApiClient`, launches `Occlusa.Api.exe` (or `dotnet run` fallback) automatically when offline, recovers on **Sync Workspace**, and cleans up the managed API process on shutdown.

