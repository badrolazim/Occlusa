# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.8.0] - 2026-11-27
### Added
- **Calm Dental App Color System (`Themes/UniformTheme.xaml`, `Themes/CalmThemePalette.cs`, `.agents/rules/calm-dental-color-system.md`):** Centralized all desktop UI colors into the strict 17-token 60-30-10 clinical palette (`#F7FAFA` canvas, `#FFFFFF` cards, `#EDF3F3` subtle surfaces/sidebars/headers, `#D8E3E3` borders, `#182929`/`#566B6B` typography, `#069494`/`#057878`/`#E3F4F4` teal accent & soft tint, plus calm Success `#2B825B`/`#EAF6F0`, Info `#2E6FA3`/`#EBF3FA`, Warning `#C47B1B`/`#FDF4E7`, and Error `#C44545`/`#FBECEC` semantic pairs) with a frozen C# token accessor (`CalmThemePalette`) and an `always_on` workspace rule.
- **Solid Destructive Confirmation Style (`UniformDangerSolidButtonStyle`) & Active Nav Icon Style (`UniformNavTabIconPathStyle`):** Added a dedicated solid destructive style reserved strictly for final confirmation dialogs while defaulting inline destructive actions (`UniformDangerButtonStyle`) to soft fill (`#FBECEC` / `#C44545`), plus active teal (`#069494`) navigation rail icon styling.
- **Automated Color-System Contract Test (`106` total tests in `Occlusa.Tests`):** Added `CalmDentalColorSystem_EnforcesCentralizedPaletteAndSinglePrimaryCta` in `PatientVisitsAndOdontogramTests.cs` verifying zero hardcoded hexes or framework brushes outside `Themes/`, zero off-palette hex values inside `Themes/`, and at most one `UniformPrimaryButtonStyle` CTA per modal.

### Changed
- **Light Clinical Navigation Sidebar (`MainWindow.xaml`, `UniformTheme.xaml`):** Replaced the dark slate sidebar with a calm `#EDF3F3` secondary surface, `#FFFFFF` branch/staff selectors and connection card with `1px solid #D8E3E3` borders, `#E3F4F4` active item highlight, `#069494` active icons, and calm soft-filled Emergency / AI-Pending alert buttons.
- **Unified Component & Status Styling Across All Views, Controls & ViewModels:**
  - Replaced all ad-hoc hex/RGB brushes across `UniformPagedGrid`, `MedicalAlertsBanner`, `ModalDialogShell`, `AppointmentsWorkspaceView`, `TimelineBackdropControl`, `AnatomicalToothControl`, `AppointmentsViewModel`, `MainDisplayRows`, `VisitRecordViewModel`, and `ValueConverters` with centralized `CalmThemePalette` / `StaticResource` tokens.
  - Updated `UniformPagedGrid` table headers and subtle row hovers to `#EDF3F3`, selected rows to `#E3F4F4`, and card borders to `1px solid #D8E3E3`.
  - Converted solid saturated alert pills in `MedicalAlertsBanner` and solid orange/red inline buttons into calm soft-fill semantic pairs.
  - Enforced strictly one primary CTA (`UniformPrimaryButtonStyle`) per active workspace view and modal (`MainWindow`, `AppointmentDetailModal`, `InvoiceCheckoutModal`, `PatientDetailModal`, `VisitRecordModal`, `WhatsAppConsoleModal`), eliminated pure `#000000` default text across all windows and `DatePicker` controls, and tuned light-background teal text to `#057878` for WCAG AA contrast.
  - Mapped all 8 odontogram conditions and legend swatches onto distinct, clinically legible tokens within the 17-color palette.
- Updated `docs/UI_UNIFORMITY_RULES.md` and `README.md` to document the Calm Dental 60-30-10 color system and `CalmThemePalette`.

## [0.7.0] - 2026-11-26
### Added
- **Panel-Fitted, Priority-Adaptive Single-Line Table Engine (`ResponsiveTableColumnPlanner`, `UniformPagedGrid`):** Replaced the legacy 3–4 column cap with a deterministic priority-based responsive column planner (`Priority="1"`..`"4"`, `MeasureBindingPath`, `MeasureExtraWidth`, `ResponsiveColumns`, `VisibleColumnCount`) backed by DPI-aware WPF `FormattedText` width measurement. Tables automatically fill 100% of their host panel width (`1280px`, `1920x1080`, `2560x1440`, and narrow modal sub-panels) with maximum high-value atomic columns on strict single-line `36px` rows—auto-hiding lower-priority columns earlier whenever adding another column would clip or truncate to `...`.
- **Automatic Row Hover Tooltips (`UniformPagedGrid.LoadingRow`):** Bound full multi-line `ToolTipText` (and synthesized column summaries) across all 11 row ViewModels so any hidden optional columns or long clinical/audit strings remain accessible on hover and in double-click modals.
- **6 New xUnit Tests (`105` total tests in `Occlusa.Tests`):** Added `ResponsiveTableColumnPlannerTests.cs` covering `1080p` full-panel fit, `1280p` priority auto-hiding, long single-line essential values pushing optional columns off earlier, narrow modal panels (`~620px`), extreme overflow floor preservation, and `0px` pre-layout safety; extended XAML contract audits in `PatientVisitsAndOdontogramTests.cs` to enforce zero `AllowAutoRowHeight="True"`, zero `FitColumnsToContent="True"`, zero `EnforceColumnLimit="True"`, and explicit `Priority=` on every `UniformColumnDef`.

### Changed
- **Expanded All 12 Workspace & Modal Tables to Atomic Detail-Rich Columns (`MainWindow.xaml`, `AppointmentsWorkspaceView.xaml`, `PatientDetailModal.xaml`, `InvoiceCheckoutModal.xaml`, `DailyReconciliationModal.xaml`, `TreatmentSuggestionRuleModal.xaml`, `MainDisplayRows.cs`, `TreatmentSuggestionRuleViewModel.cs`):**
  - **Live Queue (Simple & Detailed):** Expanded both views full-width across the workspace canvas with strict single-line `36px` rows (`Simple`: 9 cols `Time` · `Patient` · `Treatment` · `Doctor` · `Room` · `Duration` · `Type` · `Reminder` · `Status`; `Detailed`: 12 cols adding `Arrived`, `IC / MyKad`, and `Reception Note`).
  - **Patients Registry (10 cols):** `IC / MyKad` · `Full Name` · `Sex / Age` · `Phone` · `Branch` · `Visits` · `Last Visit` · `Credit (RM)` · `Consent` · `Medical Alerts`.
  - **Billing & Invoices (11 cols):** `Invoice #` · `Date` · `Patient` · `Doctor` · `Items` · `Subtotal` · `Panel / Disc.` · `Payable (RM)` · `Balance Due` · `Methods` · `Status`.
  - **Appointments Directory Table (9 cols):** `Date & Time` · `Duration` · `Type` · `Patient` · `Treatment` · `Dentist` · `Room` · `Reminder` · `Status`.
  - **WhatsApp AI Console (7 cols):** `Patient` · `Phone` · `Latest Message / Reason` · `Msgs` · `Last Update` · `Triage` · `State`.
  - **Master Catalogue (9 cols) & Doctor Suggestion Rules (6 cols):** Split compound `Usage & Commission` into atomic `Category`, `Subcategory`, `Appt Slot`, `Chart Preset`, `Comm.`, `SOAP Keywords`, `Safety Tags`, `Unit Price` and added `Subcategory` + `Base Price` to Doctor Rules.
  - **Modal Tables:** Expanded `PatientDetailModal` Visit History (6 cols), `InvoiceCheckoutModal` Invoice Lines (6 cols), `DailyReconciliationModal` Immutable Audit Trail (5 cols), and `TreatmentSuggestionRuleModal` Companion Picker (7 cols).
- Updated `docs/UI_UNIFORMITY_RULES.md`, `README.md`, `UserPreferencesModal.xaml`, `UserPreferencesViewModel.cs`, and `AppointmentDetailModal.xaml` to document the new panel-fitted single-line uniformity standard.

### Removed
- Removed the reserved empty right-hand split panel on Live Queue and retired the silent 28–48 character string cutoffs on table row cells (`KeywordsDisplay`, `SuggestedSummary`, `SummaryDisplay`, `TreatmentDisplay`, `NoteColumnDisplay`).

## [0.6.0] - 2026-11-26
### Added
- **Simple-by-Default Live Queue & Opt-In Detailed View (`LiveQueueViewMode`, `LiveQueueScopeMode`):** Redesigned Live Queue to default to a calm 4-column **Simple View** (`Time` · `Patient` · `Reminder` · `Status`) with role-smart scoping (`My Queue` for practicing dentists, `All Clinic` for reception/assistants), finished visits hidden behind a 1-click `Show Done (N)` toggle, and a single contextual **Next Step** action button (`Approve Booking` → `Check In` → `Call to Chair` → `Send to Checkout` → `Collect Payment`).
- **Per-User Workspace View Preferences (`UserPreferencesModal`, `UserPreferencesController`, `LocalUserPreferencesStore`):** Added `⚙ My View` beside `ACTIVE STAFF` in the left sidebar (plus a clickable `View: Simple ⚙` / `View: Detailed ⚙` pill in the Live Queue header) so each staff member can choose a 1-click **Simple** or **Detailed Operations** preset or fine-tune individual toggles (`DefaultScope`, `HideFinishedQueueRows`, `ShowWaitTimers`, `ShowInlineReceptionNotes`, `ShowMyKadQuickBar`), persisted per staff profile in Supabase / API with local JSON offline fallback.
- **Hybrid Appointment Reminders & WhatsApp Auto-Confirmation (`ReminderStatus`, `ReminderChannel`, `AppointmentReminderModal`):** Added appointment reminder tracking (`NotReminded`, `Reminded`, `Confirmed`, `NoReply`), 1-click customizable bilingual (**EN/BM**) WhatsApp reminder dispatch via `WhatsAppAiReceptionistService.SendAppointmentReminderAsync` (`POST /api/appointments/{id}/reminder`), manual Call/SMS/Confirmed/No Reply recording, and automatic SignalR-broadcast confirmation when patients reply `"Yes"` / `"Ya"` / `"Confirm"` on WhatsApp (with strict negation/reschedule rejection guardrails). Also wired inline Reminder status + `Remind...` / `✓ Confirm` controls inside `AppointmentDetailModal` and Appointments hover tooltips.
- **Wide-Monitor Left-Docked Live Queue & Content-Fitted Columns:** Added `FitColumnsToContent` (`DataGridLength.Auto` with dynamic refresh + breathing room + stacked header) and `RowSpacing` (`4px` vertical gap with subtle row separation) to `UniformPagedGrid`, docking Live Queue cleanly to the left with an open right panel for ergonomic scanning on 1080p/1440p/ultrawide monitors.
- Supabase PostgreSQL migration `supabase/migrations/007_reminders_and_user_preferences.sql` (reminder columns + index on `public.appointments` and `public.user_workspace_preferences` table with immutability trigger and clinic-group RLS) mirrored across `NpgsqlSupabaseRepository` and `InMemoryOcclusaRepository`.
- 5 new xUnit tests (`99` total tests in `Occlusa.Tests`) in `LiveQueueRemindersAndPreferencesTests.cs` covering role-smart defaults/presets, Live Queue filtering (`MyQueue`, `HideFinished`, `Unreminded` chip), bilingual reminder generation & negation-safe confirmation classification, and end-to-end HTTP reminder + WhatsApp auto-confirm + user-preferences persistence pipelines.

### Changed
- Collapsed the permanent 330px `MyKad IC` header strip by default in Simple View / for dentists into a compact `MyKad ▾` dropdown menu for reception (with an opt-in per-user toggle to restore the full quick bar).
- Standardized modal/toolbar action naming across Live Queue and `AppointmentDetailModal` (`Appointment`, `Patient Profile`, `Clinical Chart`, `Details`) and made Live Queue double-click routing consistent (`Appointment Info` for upcoming, `Clinical Chart` for Waiting/In Chair, `Invoice Checkout` for Checkout) with explicit tooltip hints.
- Updated `docs/UI_UNIFORMITY_RULES.md`, `README.md`, and `PatientVisitsAndOdontogramTests.cs` to reflect modal-pinned `MedicalAlertsBanner` discipline, 4-column Simple / 5-column Detailed Live Queue views, and migration `007`.

### Removed
- Removed the 62px upper `MedicalAlertsBanner` from `MainWindow.xaml` list workspaces (Live Queue, Patients, Billing, WhatsApp) to eliminate selection-change flicker and reclaim vertical space, while keeping `MedicalAlertsBanner` strictly pinned inside `PatientDetailModal`, `VisitRecordModal`, and `AppointmentDetailModal` alongside compact red `[ALERT]` row badges.

## [0.5.0] - 2026-11-26
### Added
- **Unified Master Catalogue (6th Workspace Tab):** Single source of truth (`BillingCatalogItemDto`, `BillingSettingsController`, `BillingSettingsViewModel`, `BillingCatalogItemModal`) powering **Appointments & Walk-In scheduling** (`IsBookableAppointment`, `DefaultDurationMinutes`), **Visit Odontogram Quick Presets** (`ShowInOdontogramPresets`, `DefaultToothCondition`), **Doctor Suggestion Rules**, **Invoice Checkout**, **Walk-In OTC Retail Sales**, and live **WhatsApp AI** routine fee quotes—with instant propagation across every workspace when items are edited or added.
- **Strictly Decoupled Clinical Notes & Dentist-Initiated Draft Invoicing:** Removed all monetary `Fee (RM)` fields from `VisitRecordModal` and `SoapMacroDto` so receptionists can edit unpaid Draft Invoice items freely without ever mutating a doctor's medico-legal clinical record; added **`Save & Create Invoice →`** inside `VisitRecordModal`, **`Checkout / Invoice`** on Live Queue, **`Send Draft to Counter →`**, and **`No-Charge Visit`** workflows.
- **Deterministic Smart Invoice Suggestion Engine (`InvoiceSuggestionEngine`):** Reads completed visit odontogram procedures, scans clinical SOAP notes against Master Catalogue keywords (`NoteKeywords`), applies per-doctor **Checkout Modes** (`AutoPopulateWithSuggestions`, `BlankWithSuggestions`, `BlankManualOnly`) and per-treatment companion rules (`TreatmentSuggestionRuleModal`, inheriting Clinic Default unless overridden), and flags **Medical Alert Safety Warnings** (`[⚠]`) for allergies, NSAIDs, anticoagulants, or pregnancy.
- **Walk-In Counter OTC Retail Sales & 0% Commission Guard:** Added standalone **`+ Walk-In OTC Sale`** billing (e.g., toothbrushes, toothpaste, mouthwash) requiring no patient NRIC or clinical visit, with `BillingCalculator` strictly enforcing **`0%` doctor commission** on `OtcProduct` lines and pro-rata commission on mixed clinical + OTC invoices.
- **Shared Cross-Branch Patient Credit Wallet & Overpayment Handling:** Added clinic-group shared `PatientDto.CreditBalance` and append-only `PatientCreditEntryDto` ledger (`POST /api/invoices/{id}/pay`, `GET /api/patients/{id}/credits`) supporting **`Cash Change`** vs. **`Save to Credit`** on overpayment, credit redemption (`PaymentMethod.PatientCredit`) at any branch (`HQ-KL` / `PJ-SS2`), credit reversals on audited invoice soft-voids, balanced End-of-Day reconciliation, and automatic Live Queue transition to `Completed` upon full settlement.
- Supabase PostgreSQL migration `supabase/migrations/006_decoupled_billing_credits_and_settings.sql` (`billing_catalog_items`, `doctor_invoice_preferences`, `patient_credit_ledger`, walk-in invoice & OTC line columns, immutability triggers, and RLS) mirrored across `NpgsqlSupabaseRepository` and `InMemoryOcclusaRepository`.
- Dedicated step-by-step user guide `docs/NOTES_AND_INVOICING_WALKTHROUGH.md` covering clinical notes, Master Catalogue configuration, dentist draft checkout, receptionist edits, credit wallet, and walk-in OTC retail sales.
- 5 new xUnit tests (`94` total tests in `Occlusa.Tests`) in `DecoupledBillingAndSuggestionsTests.cs` plus XAML contract regression guards in `PatientVisitsAndOdontogramTests.cs`.

### Changed
- Projected `GetTreatmentsAsync()` directly from active bookable Master Catalogue items and removed separate `treatments` seed duplication and `LinkedTreatmentId` mapping; `WhatsAppAiReceptionistService` now quotes live Master Catalogue prices and `InvoiceCheckoutViewModel` loads corporate panels from `BillingSettingsSnapshotDto.CorporatePanels`.
- Replaced hardcoded Odontogram procedure presets in `VisitRecordViewModel` with pure-clinical dynamic chips (`OdontogramProcedurePresetVm`) sourced from Master Catalogue items marked `ShowInOdontogramPresets`.
- Updated `InvoiceCheckoutModal` to support line-item editing (`+ Add`, `Update`, `Remove`, `Clear Blank`, `Scan Dr Notes`), 1-click suggestion chips, customer mode switching (Registered Patient vs. Walk-In Retail), shared credit wallet redemption, and overpayment handling.
- Updated `README.md` and `docs/UI_UNIFORMITY_RULES.md` to document the 6-workspace layout, unified Master Catalogue, migration `006`, and WPF `DataContext` visibility guardrails.

### Fixed
- Fixed WPF `DataContext` shadowing in `MainWindow.xaml` where placing `Visibility="{Binding IsSettingsWorkspace, ...}"` and `DataContext="{Binding BillingSettings}"` on the same element caused the **Master Catalogue** toolbar and table to remain visible over other workspace tabs; split the Master Catalogue toolbar into two compact rows for clean layout at `1280px+` widths and added an automated XAML regression check.

### Removed
- Removed `SoapMacroDto.DefaultFee`, `BillingCatalogItemDto.LinkedTreatmentId`, and duplicate hardcoded treatment/preset/fee tables across API and Desktop projects.

## [0.4.0] - 2026-11-25
### Added
- First-class **Patient Visit Records (`0..N` visits per patient)** with **independent per-visit odontograms** aligned with Malaysia Dental Association (MDA / MDC) and KKM dental record-keeping requirements (`PatientVisitDto`, `CreatePatientVisitRequest`, `UpdatePatientVisitRequest`, `ResetVisitOdontogramRequest`, `VisitsController`, and `OcclusaApiClient` visit endpoints).
- **Kreloses-Style Anatomical Tooth Outline + 5-Surface (`M`, `O/I`, `D`, `B`, `L`) Odontogram:** Native WPF vector `AnatomicalToothControl` and frozen GPU `ToothAnatomyCatalog` covering all **52 FDI teeth** — **Permanent (`11–48`)** and **Primary / Pediatric (`51–85`)** — with midline-aware occlusal cross charting (crown and root silhouettes adapted from `ZoliQua/React-Advanced-Odontogram`, MIT License).
- Dedicated **`VisitRecordModal`** (`VisitRecordViewModel`) combining each visit's pinned `MedicalAlertsBanner`, anatomical 52-tooth odontogram, clickable 5-surface inspector, `0. Record Baseline (RM 0)` / Proposed / Accepted / Completed staging, `Reset to Blank` / `Re-Sync Prior Visit` actions, and 2x2 Clinical SOAP Notes workspace.
- **Carry-Forward Odontogram Initialization (`OdontogramHelper`):** New visits carry forward baseline conditions and surfaces from the patient's most recent prior visit without double-billing completed procedures (`Fee = 0`, empty `ProcedureName`; completed `ExtractionNeeded` transitions to `Missing`; unfinished `Proposed`/`Accepted` items carry forward intact).
- **Standalone Patient Registration (`+ Register Patient`):** Reception can register new patients with `0` visits directly in `PatientDetailModal` (`Create Mode`), while seating an appointment `InChair` in Live Queue automatically creates/links a `PatientVisit` record (`Scheduled`, `Waiting`, `AiBookedPending`, and `Cancelled` create `0` visits).
- **Visit-Scoped Billing & Settled Historical Invoice Protection:** Completed tooth procedures sync into that visit's draft invoice with surface codes (e.g., `Tooth #16 (OD) - Composite Filling`); editing historical visit records or odontograms is audited (`PATIENT_VISIT_UPDATED`, `VISIT_ODONTOGRAM_UPDATED`) while leaving already-settled invoices (`Payments.Count > 0`) untouched.
- Supabase PostgreSQL migration `supabase/migrations/005_patient_visits_and_visit_odontograms.sql` (`patient_visits`, `visit_odontogram_entries`, `visit_id` links on `appointments` & `invoices`, `prevent_hard_delete()` triggers, clinic-group RLS policies, and idempotent legacy backfill) mirrored across `NpgsqlSupabaseRepository` and `InMemoryOcclusaRepository` (including seeded 3-visit, 1-visit, and 0-visit patients).
- 8 new xUnit tests (`89` total tests in `Occlusa.Tests`) in `PatientVisitsAndOdontogramTests.cs` covering 52-tooth FDI validation, 5-surface normalization, standalone 0-visit registration, `InChair` visit auto-creation, multi-visit carry-forward & reset isolation, settled historical invoice protection, end-to-end Kestrel HTTP visit endpoints, and automated zero-scrollbar / zero-ellipsis XAML & migration audits.

### Changed
- Redesigned `PatientDetailModal` (`PatientDetailViewModel`) with editable patient demographics and medical alerts on the left and a 4-column `UniformPagedGrid` of `0..N` visit records on the right, plus `+ New Visit Record` and double-click opening into `VisitRecordModal`.
- Updated `MainWindow` Patient Registry with `+ Register Patient`, `Phone & Visits` summary column, and `Visit Chart` action, and wired Live Queue `InChair` double-click / `AppointmentDetailModal` charting callbacks to `VisitRecordModal`.
- Updated `docs/UI_UNIFORMITY_RULES.md` and `README.md` to document `AnatomicalToothControl`, `VisitRecordModal`, and migration `005`.

### Removed
- Removed obsolete patient-wide `OdontogramModal.xaml`, `OdontogramModal.xaml.cs`, and `OdontogramViewModel.cs` (replaced by per-visit `VisitRecordModal`).

## [0.3.1] - 2026-11-24
### Added
- Compact single-line **Reception Note** display beneath the patient name in Today's Live Queue (`AppointmentCalendarHelper.FormatCompactReceptionNote`, `QueueRowItem.ReceptionNoteDisplay` / `HasReceptionNote`) rendered in smaller muted typography (`10.5px` vs. `12px` patient name) and collapsing cleanly when empty so zero vertical space remains under the name.
- Opt-in `AllowAutoRowHeight` mode (`MinHeight="36"`, `Height="Auto"`, DPI-safe auto page sizing) and column-header `HorizontalAlignment` propagation (`HeaderStyle`) in `UniformPagedGrid`.
- 2 new xUnit tests (`81` total tests in `Occlusa.Tests`) covering `FormatCompactReceptionNote` whitespace/word-boundary compaction (zero ellipsis) and end-to-end HTTP lifecycle persistence of Walk-In and appointment reception notes (create, queue status transitions, inline register-and-book walk-in, update, and clear).

### Changed
- Reordered Live Queue columns to `Appt Time` | `Arrived / Walk-In` | `Status` | `Patient` | `Doctor & Room`, right-aligning both the `Status` column header and uniform-size (`136x24`) status pills flush beside the patient name.
- Renamed `"Reception / Clinical Notes"` to `"Reception Note"` (with a `"Shown under patient name in Live Queue"` hint) across `AppointmentDetailModal` (New Appointment, Edit Appointment Details, and all Walk-In patient intake flows), updated the walk-in guidance prompt, and relabeled row hover tooltips to `Reception Note:`.

### Fixed
- Eliminated vertical layout shift (table rows jumping up/down) when clicking between patients with and without clinical safety alerts by locking `MedicalAlertsBanner` to a constant `62px` height across Idle, Clear, and Alert states, replacing `<WrapPanel>` with a single-line horizontal `<StackPanel>` for alert flag pills, and permanently reserving the single-line Row 1 safety note slot with word-boundary compaction and full-text hover tooltips.

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

