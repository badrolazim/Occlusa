# Occlusa Repository Rules & Agent Guidelines

All collaborators (including Antigravity users on their own accounts and any other AI coding agents) share the same workspace rules checked into `.agents/rules/`:

- **Patient Flow (5-Stage Standard):** `.agents/rules/patient-flow.md` — Strictly `Scheduled` -> `Waiting` -> `In Chair` -> `Ready for Payment` -> `Completed`.
- **Calm Dental Color System (60-30-10):** `.agents/rules/calm-dental-color-system.md` — Centralized 17-token palette and single primary CTA per view/modal.

## Team Rule Convention
- Always place shared project rules in `.agents/rules/*.md` (never only in machine-local `~/.gemini/config/`) so every collaborator receives them automatically on `git pull`.
- Keep changes to `.agents/rules/**` reviewed via Pull Request.

