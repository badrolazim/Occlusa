---
trigger: always_on
description: "Enforce the 5-stage clinic patient flow statuses (Scheduled, Waiting, In Chair, Ready for Payment, Completed) across all UI labels, workflows, ViewModels, APIs, and tests."
---

# Clinic Patient Flow Rule (5-Stage Standard)

## Goal
Maintain one consistent, unambiguous patient lifecycle across all clinic screens (Appointments, Live Queue, Clinical Chart, Dispensary/Billing, Invoice Checkout), ViewModels, APIs, notifications, and tests.

The active clinical patient flow consists of **strictly 5 sequential statuses**:

`Scheduled` -> `Waiting` -> `In Chair` -> `Ready for Payment` -> `Completed`

---

## Canonical Status Definitions

Use these exact display strings everywhere in the UI (status pills, filter chips, dropdowns, table cells, tooltips, banners, and status messages):

| Stage | Canonical Status | Trigger / Meaning | Avoid / Legacy Terms |
| :---: | :--- | :--- | :--- |
| **1** | `Scheduled` | Patient has finished booking an appointment and has not yet arrived at the clinic. | `Booked`, `Confirmed` (as queue status) |
| **2** | `Waiting` | Patient has arrived and checked in at the clinic, waiting to be called into a treatment room. | `Arrived`, `Checked In`, `In Lobby` |
| **3** | `In Chair` | Patient has been called into the treatment room and is receiving treatment. | `In Room`, `In Progress`, `Treating` |
| **4** | `Ready for Payment` | Treatment is finished; patient has exited the treatment room and is waiting for dispensary and billing. | `Checkout`, `Awaiting Checkout`, `Billing`, `Dispensary` |
| **5** | `Completed` | Payment has been settled and the patient's visit is finished. | `Done`, `Paid`, `Closed`, `Finished` |

---

## Flow & Transition Rules

1. **Appointment Creation (`Scheduled`)**
   - Once an appointment booking is completed, set the patient's status to `Scheduled`.
   - Walk-in patients who register directly at the front desk enter the flow at `Waiting`.

2. **Clinic Arrival & Check-In (`Scheduled` -> `Waiting`)**
   - When a patient arrives and checks in at the clinic, transition the status to `Waiting`.
   - Always use the label `Waiting` (never display `Arrived` as a separate active status).

3. **Room Call / Treatment Start (`Waiting` -> `In Chair`)**
   - When the patient is called into the treatment room, transition the status to `In Chair`.

4. **Treatment End / Dispensary & Billing (`In Chair` -> `Ready for Payment`)**
   - When treatment finishes and the patient leaves the room to wait for medication dispensary and billing, transition the status to `Ready for Payment`.

5. **Payment Settlement (`Ready for Payment` -> `Completed`)**
   - When payment is completed, transition the status to `Completed`.

---

## Implementation & UI Guidelines

- **Exact Terminology:** All user-facing status badges, filters, dropdown items, timeline labels, and action feedback messages must use the exact 5 canonical names (`Scheduled`, `Waiting`, `In Chair`, `Ready for Payment`, `Completed`).
- **Wait/Elapsed Timers:** If elapsed duration is appended on live queue pills (e.g., `Waiting · 12m` or `In Chair · 25m`), keep the exact canonical status prefix intact.
- **Backward Compatibility with Legacy Enum Values:** If existing persisted data or API contracts contain legacy enum names (such as `Arrived` or `AwaitingCheckout`), normalize/map them to `Waiting` and `Ready for Payment` respectively in all UI surfaces and workflow logic.
- **Non-Flow Exception States:** Administrative or pre-confirmation states outside the active patient journey (such as `Cancelled` or unapproved AI booking requests) are exception states—not additional stages of the 5-status patient flow.
- **Visual Styling:** Style all status pills, badges, and filters using the semantic tokens defined in `calm-dental-color-system.md`. Do not introduce ad-hoc colors.

---

## Strict Constraints (Do's & Don'ts)
- **DO** preserve the exact 5-stage order (`Scheduled` -> `Waiting` -> `In Chair` -> `Ready for Payment` -> `Completed`) in pipelines, filter bars, and workflow actions.
- **DO** use the exact canonical wording in every new or updated UI element, ViewModel helper, and test.
- **DON'T** introduce extra intermediate statuses into the core patient flow.
- **DON'T** shorten or substitute canonical labels in the UI (e.g., never render `Checkout` instead of `Ready for Payment`, or `Done` instead of `Completed`).

