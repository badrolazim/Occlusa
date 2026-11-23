# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

