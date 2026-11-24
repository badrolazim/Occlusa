# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0] - 2026-11-24
### Added
- Unified **Today's Live Queue** 5-column paged floor board (`Appt Time` | `Arrived / Walk-In` | `Patient` | `Doctor & Room` | `Status`) with separated appointment vs. arrival/walk-in times (`No Appointment` for walk-ins), status-pill-only color badges, live 30-second wait/chair timers (`Waiting · Xm` with ≥ 30m long-wait highlight, `In Chair · Xm`), and `UniformColumnDef.CellTemplate` support in `UniformPagedGrid`.
- Shared **Walk-In** intake (`VisitType`: `Booked`, `WalkIn`, `AiBooked`) directly from the Live Queue `+ Walk-In` toolbar action (`AppointmentDetailModal` walk-in mode) that automatically appears on today's **Appointments Calendar** (warm orange dashed cards + `Walk-In` badge) and blocks dentist slots in `GetDentistDaySlotAvailability`.
- **Active Staff Attribution ("Who Did What"):** Sidebar `ACTIVE STAFF` selector synced to `X-Occlusa-User` (`OcclusaApiClient.SetActiveUser`), stage milestone timestamps (`ArrivedAt`, `SeatedAt`, `CheckoutAt`, `CompletedAt`), per-stage staff fields (`CreatedBy`, `CheckedInBy`, `SeatedBy`, `CheckoutBy`, `CompletedBy`, `LastUpdatedBy`), and a paged **Staff Activity Trail** (`VisitActivityEntryDto`) in `AppointmentDetailModal` and row hover tooltips.
- Smart **MyKad Check-In** on Live Queue detecting today's scheduled/AI booking for 1-click check-in to `Waiting` or prompting Walk-In intake.
- Supabase PostgreSQL migration `supabase/migrations/004_live_queue_walkins_and_attribution.sql` and mirrored `NpgsqlSupabaseRepository` / `InMemoryOcclusaRepository` persistence.
- 5 new xUnit tests (`79` total tests in `Occlusa.Tests`) covering Live Queue sorting, legacy `Arrived` normalization & elapsed timers, walk-in calendar slot blocking, milestone staff attribution, check-in revert, and end-to-end HTTP pipeline round-trips.

### Changed
- Merged legacy `QueueStatus.Arrived` into `QueueStatus.Waiting` across Live Queue toolbar, booking status dropdown, transitions (`ClinicDomainHelpers.ApplyStatusMilestone`), and SQL backfill (`NormalizeQueueStatus`).
- Sorted unified Live Queue (`AppointmentCalendarHelper.SortLiveQueue`) so active visits remain on top (longest-waiting first) and `Completed` / `Cancelled` visits are automatically pushed to the bottom with softened row opacity.
- Enabled contextual Live Queue toolbar buttons (`Approve AI`, `Check In`, `In Chair`, `Checkout`, `Complete`, `Cancel`, `Visit Details`, `Patient Chart`, `Odontogram`) and routed non-`InChair` row double-click to `AppointmentDetailModal`.

## [0.2.0] - 2026-11-24
### Added
- 15-minute proportional Appointments schedule timeline (`4 rows/hour` across `Full Day` `09:00`-`18:00` = `36` rows, `AM` `09:00`-`13:30` = `18` rows, and `PM` `13:30`-`18:00` = `18` rows; 2-hour bookings consume `8` rows) across **Day View** and **Week View** via `ScheduleTimelinePanel`, `TimelineBackdropControl`, and `AppointmentCalendarHelper.ComputeTimelineLayout`.
- Automatic side-by-side overlap lane splitting with back-to-back lane reuse and `+{N} more` overflow routing for concurrent appointments within the same column.
- Cross-date drag-and-drop rescheduling across **Week View** day lanes, **Day View** 7-day quick-date pills (plus intra-column 15-minute time rescheduling), and **Month View** cells locked strictly to the appointment's original dentist (`IsSameDentist`, `BuildDragRescheduleRequest`).
- Reusable `OcclusaApiApplicationFactory` in `Occlusa.Api` enabling shared builder configuration for both standalone execution and embedded hosting.
- Automated verification expanded by 5 new xUnit tests (`74` total tests in `Occlusa.Tests`) covering embedded host HTTP mutations, proportional row spans, overlap lane splitting, concurrency overflow, and dentist-locked drag rescheduling.

### Changed
- Redesigned **Day View** and **Week View** into a serene, gridless floating-column canvas with faint inset hour/half-hour rhythm guides, dentist-coded pastel card palettes, adaptive card density, hover quick-book hints, and a live current-time line (zero scrollbars, zero ellipsis).
- Enabled `Full Day` / `AM` / `PM` session zoom across both **Day View** and **Week View** (`IsTimelineCalendarView`).
- Upgraded `LocalApiHostService` in `Occlusa.Desktop` to an embedded in-process ASP.NET Core Kestrel host with external-API `/api/health` verification reuse, automatic ephemeral loopback port fallback (`http://127.0.0.1:0`) on port conflict, and off-UI-thread shutdown disposal.

### Fixed
- Resolved `"Cannot reach Occlusa API"` appointment save errors via transparent `HttpRequestException` recovery (`ExecuteWithRecoveryAsync`), dynamic base URL rotation (`UpdateBaseUrl`), and loopback SignalR proxy bypass in `OcclusaApiClient`, surfacing real active base URL and error details in `AppointmentDetailViewModel` and `MainViewModel`.

## [0.1.0] - 2026-11-23
### Added
- Core multi-branch Malaysian dental practice platform (`Occlusa.Shared`, `Occlusa.Api`, `Occlusa.Desktop`, `Occlusa.Tests`) with Supabase PostgreSQL RLS (`001_occlusa_tier1_tier2_schema_rls.sql`), deterministic `BillingCalculator`, PC/SC Malaysian `MyKadParser`, WhatsApp AI Receptionist with `EmergencyTriageClassifier`, FDI 32-tooth odontogram-to-invoice sync, and `scripts/verify-daily-backup.ps1`.
- Relational **Appointments** workspace (`Calendar`: Day View with `Full Day`/`AM`/`PM` 9-slot grid, Week View, Month View; and 4-column paged `Table` via `UniformPagedGrid`) synchronized in real time with **Live Queue** via SignalR (`/hubs/queue`) and 5-second fallback polling.
- 4-step front-desk appointment workflow in `AppointmentDetailModal`: Treatment-first selection filtering eligible dentists via `DentistDto.SupportedTreatmentIds` (`002_appointments_dentists_treatments.sql` and `003_dentist_treatment_capabilities.sql`), Date selection with clickable 3x3 hourly dentist availability grid (`09:00`-`17:00`, `Open` vs `Booked`), flexible auto-suggest patient lookup (IC with or without dashes, Full Name, or Phone digits), and inline new-patient registration via atomic `POST /api/appointments/register-and-book`.
- Embedded `LocalApiHostService` in `Occlusa.Desktop` that automatically probes `http://localhost:5188/api/health`, launches `Occlusa.Api` in the background when needed, recovers on workspace sync, and terminates its managed process on app exit.
- Automated xUnit verification suite (`69` tests in `Occlusa.Tests`).

### Changed
- Reordered Appointments toolbar filters to `Treatment` -> `Dentist` -> `Patient` and narrowed Day View columns to dentists qualified for the selected treatment.
- Scoped Live Queue floor board strictly to today's local appointments (`IsTodayForLiveQueue`).

### Fixed
- Hidden the main-window `MedicalAlertsBanner` while viewing the **Appointments** workspace (`IsAppointmentsWorkspace`) and suppressed the modal safety banner until a patient is selected or registered (`ShowPatientSafetyBanner`).
- Enabled `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true` in `Occlusa.Api` (`Program.cs`) so optional empty fields (`Notes`, `Phone`, `Address`, `Allergies`) are not rejected with HTTP 400.
- Bypassed system HTTP proxy for loopback calls (`UseProxy = !baseUri.IsLoopback`) in `OcclusaApiClient`.

