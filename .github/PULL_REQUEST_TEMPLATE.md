## Summary
<!-- Briefly describe what changed and why. -->

## Rule & Quality Checklist
- [ ] **Patient Flow Rule (`.agents/rules/patient-flow.md`):** Uses the exact 5 canonical statuses (`Scheduled` → `Waiting` → `In Chair` → `Ready for Payment` → `Completed`) across UI labels, ViewModels, APIs, and tests.
- [ ] **Calm Dental Color System (`.agents/rules/calm-dental-color-system.md`):** Uses centralized theme tokens (no hardcoded hex values, no pure `#000000`, max 1 primary CTA per view/modal).
- [ ] **Shared Rules Location:** Any new team-wide agent rules are placed in `.agents/rules/` (not local-only `~/.gemini/config/`).
- [ ] **Tests:** `dotnet test` passes locally.

