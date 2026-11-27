---
trigger: always_on
description: "Apply this calm dental app color system whenever creating, editing, styling, or auditing UI components, themes, stylesheets, or screens."
---

# Calm Dental App Color System

## Design Goal
Maintain a calm, clean, clinical aesthetic—never loud, neon, or visually aggressive. Follow the **60-30-10 rule** strictly:
- **60% Dominant (Neutral Canvas):** Soft off-white backgrounds and crisp white cards.
- **30% Secondary (Structure & Typography):** Muted cool-tinted grays for borders, sidebars, and readable dark charcoal text.
- **10% Accent (Brand Primary):** Teal (`#069494`) reserved strictly for primary actions, active navigation states, and key focus indicators.

---

## Design Tokens

Map these exact hex values into the project's native theme system (e.g., CSS variables, Tailwind config, Flutter `ColorScheme`, or XAML `ResourceDictionary`). Do not hardcode ad-hoc hex values in components.

### Core Palette (60-30-10)
| Token | Role | Hex | Usage |
| :--- | :--- | :--- | :--- |
| `bg-canvas` | 60% Main Background | `#F7FAFA` | Main app background / page canvas |
| `bg-surface` | 60% Elevated Surface | `#FFFFFF` | Cards, modals, tables, form inputs |
| `bg-subtle` | 30% Secondary Surface | `#EDF3F3` | Sidebars, table headers, hover fills, disabled inputs |
| `border-default` | 30% Borders & Dividers | `#D8E3E3` | Card borders, input outlines, subtle dividers |
| `text-primary` | 30% Primary Text | `#182929` | Headings, body copy, primary labels |
| `text-secondary` | 30% Muted Text | `#566B6B` | Subtitles, timestamps, placeholders, metadata |
| `brand-primary` | 10% Primary Accent | `#069494` | Primary buttons, active tab/nav icons, focus rings |
| `brand-hover` | 10% Primary Hover | `#057878` | Hover and pressed states for `brand-primary` |
| `brand-tint` | 10% Primary Soft Fill | `#E3F4F4` | Active sidebar item background, selected rows, soft brand badges |

### Semantic Palette (Calm State & Feedback)
| State | Text / Icon Token | Text / Icon Hex | Soft Background Token | Soft Background Hex | Usage |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Success** | `status-success` | `#2B825B` | `status-success-bg` | `#EAF6F0` | Confirmed appointments, completed treatments, saved states |
| **Info** | `status-info` | `#2E6FA3` | `status-info-bg` | `#EBF3FA` | Neutral notifications, reminders, system updates |
| **Warning** | `status-warning` | `#C47B1B` | `status-warning-bg` | `#FDF4E7` | Pending actions, reschedule notices, gentle cautions |
| **Error** | `status-error` | `#C44545` | `status-error-bg` | `#FBECEC` | Cancellations, validation errors, destructive alerts |

---

## Component Rules

1. **Buttons**
   - **Primary CTA:** Background `#069494`, text `#FFFFFF`, hover `#057878`. Limit to **one primary button per view/modal**.
   - **Secondary Button:** Background `#FFFFFF`, border `1px solid #D8E3E3`, text `#182929`, hover background `#EDF3F3`.
   - **Ghost / Tertiary Button:** Transparent background, text `#069494`, hover background `#E3F4F4`.
   - **Destructive Button:** Prefer soft fill (`#FBECEC` background with `#C44545` text) for inline actions; reserve solid `#C44545` with `#FFFFFF` text only for final confirmation dialogs.

2. **Status Pills, Badges & Alerts**
   - Always pair each semantic **Soft Background Hex** with its matching **Text / Icon Hex**.
   - Never use solid saturated red, green, orange, or blue fills for routine status pills or inline banners.

3. **Cards, Tables & Layout**
   - Place `#FFFFFF` cards on top of the `#F7FAFA` canvas with a subtle `1px solid #D8E3E3` border (avoid heavy drop shadows).
   - Use `#EDF3F3` for table headers and subtle row hovers; use `#E3F4F4` for selected rows.

4. **Typography & Contrast**
   - Never use pure black (`#000000`) for text; always use `#182929` for primary copy and `#566B6B` for secondary copy.
   - Ensure all text/background pairings maintain at least WCAG AA contrast (4.5:1 for normal text).

---

## Strict Constraints (Do's & Don'ts)
- **DO** centralize all colors into semantic theme tokens before updating screens.
- **DO** replace existing ad-hoc grays, blacks, and saturated status colors with the closest token above.
- **DON'T** introduce new colors outside this palette.
- **DON'T** overuse `#069494` for large page backgrounds or multiple competing buttons on the same screen.
- **DON'T** use neon/default framework colors (e.g., default red `#FF0000`, bright green `#00FF00`, or pure `#000000`).

