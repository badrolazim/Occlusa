# Occlusa Desktop UI Uniformity Rules & Architecture Specification

**Owner:** `ui-architect`  
**Target Platform:** C# .NET 8 WPF (`Occlusa.Desktop`), optimized for **1280px minimum**, **1920x1080**, and **2560x1440** clinic workstations.

---

## 1. Non-Negotiable Visual & UX Invariants

1. **ZERO Scrollbars Anywhere**
   - `ScrollViewer.VerticalScrollBarVisibility` and `ScrollViewer.HorizontalScrollBarVisibility` MUST NEVER be `Auto` or `Visible` in any XAML view, user control, or modal.
   - All multi-row datasets, SOAP note histories, audit trails, and WhatsApp chat transcripts MUST paginate using crisp `◀ Prev` / `Next ▶` (or `◀ Older` / `Newer ▶`) buttons.
   - Do not wrap pages or modal bodies in `<ScrollViewer>`.

2. **ZERO Text Truncation (`...`) & ZERO Single-Line Clipping Anywhere**
   - `TextTrimming="CharacterEllipsis"` and `TextTrimming="WordEllipsis"` are STRICTLY FORBIDDEN across the entire codebase.
   - `TextTrimming="None"` and `TextWrapping="NoWrap"` are enforced in `UniformTheme.xaml` and `UniformPagedGrid`.
   - Every table row is locked to a crisp **single-line `36px` height** (`AllowAutoRowHeight` MUST NOT be enabled on tables).
   - Never artificially chop table values with silent character cutoffs or ellipses. Instead, `UniformPagedGrid` measures the exact rendered single-line width of headers and rows (`FormattedText`) and automatically hides lower-priority columns earlier whenever adding another column would force a visible cell to clip or overflow the panel width. Full multi-paragraph details and hidden columns remain accessible via hover tooltips and True Modal Popups.

3. **Panel-Fitted, Priority-Adaptive Detail-Rich Tables + Double-Click Modals**
   - Every tabular view in `Occlusa.Desktop` MUST use `controls:UniformPagedGrid` stretched to fill its host panel (`HorizontalAlignment="Stretch"`).
   - **No 3–4 Column Cap:** Retire the legacy 3–4 column maximum. Tables maximize high-value **atomic** columns (one clear clinical/financial data point per column rather than `A · B · C` concatenated strings) up to the full width the host panel allows—whether a `~600px` modal sub-panel, a `1280px` compact workstation, a `1920x1080` monitor (`~1,624px`), or a `2560x1440` monitor (`~2,264px`).
   - **Priority-Ordered Column Budget (`Priority` 1 → 4):** Every `UniformColumnDef` declares a `Priority` (`1 = Essential`, `2 = Important`, `3 = Secondary`, `4 = Wide-Monitor Extended`), a realistic `MinWidth` floor, and proportional `StarWidth` growth weight. `UniformPagedGrid` runs `ResponsiveTableColumnPlanner` on load, resize, and data/page updates:
     - All `Priority="1"` columns stay visible.
     - Higher-priority optional columns (`Priority="2"`, `"3"`, `"4"`) are greedily included **only while their measured single-line width fits inside the panel's `ActualWidth`** without clipping or horizontal scrolling.
     - Visible columns retain their declared left-to-right visual order and distribute surplus panel width cleanly by `StarWidth` so there are zero dead horizontal voids.
   - Double-clicking any row opens a **True Modal Popup** (`Window.ShowDialog()`) freezing the workspace behind it.

4. **Responsive 1920x1080 & 2560x1440 Resolution Scaling**
   - `UniformPagedGrid` automatically recomputes `PageSize` on `Loaded` and host `Window.SizeChanged` (unless an explicit `PageSize` override is set for a compact modal sub-table):
     - Window height `<= 1120px` (e.g., 1920x1080): **11 rows per page**.
     - Window height `> 1120px` (e.g., 2560x1440): **17 rows per page**.
   - Uniform single-line row height (`36px`), header height (`36px`), and footer height (`44px`) fit cleanly within both 1080p and 1440p viewports without overflow.

5. **Clinical Safety & Desktop Modal Discipline**
   - **Pinned `MedicalAlertsBanner`:** Must stay pinned at the top of `PatientDetailModal`, `VisitRecordModal`, and `AppointmentDetailModal` whenever a patient record is open, visibly highlighting Allergies, Anticoagulants, Diabetes, Pregnancy, and MyKad IC verification status. List workspaces (`MainWindow` Live Queue, Patients, Billing, WhatsApp) omit the 62px top banner to prevent row-selection flicker and instead display compact red `[ALERT]` badges with full hover details.
   - **Full-Width Live Queue (Simple Default + Detailed Operations View):** Both Live Queue views span the full workspace canvas with strict single-line `36px` rows and responsive priority columns—defaulting to a calm **Simple View** (`Time`, `Patient`, `Treatment`, `Doctor`, `Room`, `Duration`, `Reminder`, `Status`, 1 smart next-step button, finished visits hidden) with an opt-in **Detailed Operations View** (`Appt Time`, `Arrived`, `Type`, `Patient`, `IC / MyKad`, `Treatment`, `Duration`, `Doctor`, `Room`, `Reception Note`, `Reminder`, `Status` with live wait timers and full stage controls) configurable per staff member via `UserPreferencesModal`.
   - **No Browser-Style Back Navigation:** Main navigation uses top-level workspace tabs (`RadioButton` styled with `UniformNavTabRadioStyle`). Detail workflows open in `ShowDialog()` modals wrapped in `ModalDialogShell`—never browser-style Back/Forward page stacks.
   - **`ModalDialogShell` Unsaved-Changes Guard:** Every editable modal binds `HasUnsavedChanges` on `ModalDialogShell`. Attempting to close via `Esc`, `✕`, `Cancel`, or `Alt+F4` prompts `"Save changes?"` (`Yes` / `No` / `Cancel`) before discarding dirty clinical notes or billing edits.

---

## 2. Theme Setup (`Themes/UniformTheme.xaml`)

Merge `Themes/UniformTheme.xaml` inside `App.xaml`:

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Themes/UniformTheme.xaml" />
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

### Key Resource Tokens

| Category | Resource Keys |
| :--- | :--- |
| **Surfaces** | `SurfaceCanvasBrush`, `SurfaceCardBrush`, `SurfaceSubtleBrush`, `SurfaceHoverBrush`, `SurfaceSelectedBrush` |
| **Borders** | `BorderHairlineBrush`, `BorderStrongBrush` |
| **Brand** | `BrandPrimaryBrush`, `BrandPrimaryHoverBrush`, `BrandPrimarySoftBrush`, `BrandSecondaryBrush`, `BrandSecondarySoftBrush` |
| **Text** | `TextPrimaryBrush`, `TextSecondaryBrush`, `TextMutedBrush`, `TextInverseBrush` |
| **Clinical / Status** | `DangerBrush`, `DangerBgBrush`, `DangerBorderBrush`, `WarningBrush`, `WarningBgBrush`, `WarningBorderBrush`, `SuccessBrush`, `SuccessBgBrush`, `SuccessBorderBrush`, `InfoBrush`, `InfoBgBrush`, `InfoBorderBrush` |
| **Odontogram** | `ToothHealthyBrush`, `ToothCariesBrush`, `ToothFillingBrush`, `ToothCrownBrush`, `ToothRootCanalBrush`, `ToothExtractionBrush`, `ToothMissingBrush`, `ToothImplantBrush` |
| **Typography** | `FontFamilyPrimary`, `FontFamilyMono`, `FontSizePageTitle` (20), `FontSizeModalTitle` (17), `FontSizeSectionTitle` (15), `FontSizeBody` (12.5), `FontSizeTableCell` (12), `FontSizeTableHeader` (11.5), `FontSizeCaption` (11) |
| **Buttons** | `UniformPrimaryButtonStyle`, `UniformSecondaryButtonStyle`, `UniformDangerButtonStyle`, `UniformWarningButtonStyle`, `UniformGhostButtonStyle`, `UniformIconCloseButtonStyle` |
| **Navigation** | `UniformNavTabRadioStyle` |
| **Containers & Inputs** | `UniformCardBorderStyle`, `UniformSubtleCardBorderStyle`, `UniformInputTextBoxStyle`, `UniformMultiLineTextBoxStyle`, `UniformComboBoxStyle`, `UniformCheckBoxStyle` |
| **TextBlock Styles** | `UniformPageTitleTextStyle`, `UniformSectionTitleTextStyle`, `UniformModalTitleTextStyle`, `UniformLabelTextStyle`, `UniformBodyTextStyle`, `UniformMutedTextStyle`, `UniformMonoTextStyle` |

---

## 3. Reusable Controls (`xmlns:controls="clr-namespace:Occlusa.Desktop.Controls"`)

### A. `UniformPagedGrid`, `UniformColumnDef`, and `ResponsiveTableColumnPlanner`

- **Namespace:** `Occlusa.Desktop.Controls` (layout engine in `Occlusa.Shared.Services.ResponsiveTableColumnPlanner`)
- **DependencyProperties:**
  - `ItemsSource` (`IEnumerable?`)
  - `Columns` (`ObservableCollection<UniformColumnDef>`) — unlimited priority-ordered atomic columns, automatically fitted to panel width
  - `ResponsiveColumns` (`bool`, default `true` — enables exact single-line width measurement + priority-based auto-hide so no visible column clips or truncates)
  - `Title` (`string`)
  - `Subtitle` (`string`)
  - `EmptyMessage` (`string`)
  - `PageSize` (`int`, default `11`; auto-switches `11` @ 1080p / `17` @ 1440p unless explicitly overridden in XAML or `AutoPageSize="False"`)
  - `AutoPageSize` (`bool`, default `true`)
  - `CurrentPage` (`int`, TwoWay)
  - `TotalPages` (`int`)
  - `TotalItems` (`int`)
  - `SelectedItem` (`object?`, TwoWay)
  - `RowDoubleClickCommand` (`ICommand?`)
  - `RowDoubleClickCommandParameter` (`object?`)
  - `EnforceColumnLimit` (`bool`, default `false` — retired legacy cap; kept only for backwards binary/XAML compatibility)
- **Events:**
  - `RowDoubleClicked` (`EventHandler<object?>`)
- **`UniformColumnDef` Properties:**
  - `Header` (`string`)
  - `BindingPath` (`string`)
  - `Priority` (`int`, default `1` — `1 = Essential`, `2 = Important`, `3 = Secondary`, `4 = Wide-Monitor Extended`)
  - `StarWidth` (`double`, default `1.0`; alias `WidthRatio` — relative share of surplus panel width after every visible column's measured single-line width is reserved)
  - `MinWidth` (`double`, default `70` — minimum floor width in DIPs)
  - `StringFormat` (`string?`)
  - `HorizontalAlignment` (`HorizontalAlignment` — also propagated to the column `HeaderStyle`)
  - `IsMonospace` (`bool`)
  - `IsBold` (`bool`)
  - `CellTemplate` (`DataTemplate?`)
  - `MeasureBindingPath` (`string?` — optional property path measured for custom `CellTemplate` columns)
  - `MeasureExtraWidth` (`double`, default `0` — extra DIP width reserved for badges/pills/dots inside custom `CellTemplate` columns)

### B. `MedicalAlertsBanner`

- **DependencyProperties (standalone, zero external model dependencies):**
  - `PatientName` (`string`)
  - `IcNumber` (`string`)
  - `IsMyKadVerified` (`bool`)
  - `Allergies` (`string`)
  - `OnAnticoagulants` (`bool`)
  - `HasDiabetes` (`bool`)
  - `IsPregnant` (`bool`)
  - `AlertNotes` (`string`)
- **Layout Stability:** Locked to a fixed `62px` height (`Height="62"`) across Idle, Clear, and Alert states, with single-line non-wrapping alert pills and a permanently reserved Row 1 safety-note slot (with full-text tooltips) to eliminate vertical layout shift.

### C. `ModalDialogShell`

- **DependencyProperties:**
  - `Title` (`string`)
  - `Subtitle` (`string`)
  - `BadgeText` (`string`)
  - `DialogContent` (`object?`, `[ContentProperty]`)
  - `FooterContent` (`object?`)
  - `ShowDefaultCloseButton` (`bool`, default `true`)
  - `CloseButtonText` (`string`, default `"Close"`)
  - `HasUnsavedChanges` (`bool`, TwoWay, default `false`)
  - `UnsavedPromptTitle` (`string`, default `"Unsaved Changes"`)
  - `UnsavedPromptMessage` (`string`, default `"You have unsaved changes. Do you want to save changes before closing?"`)
  - `SaveCommand` (`ICommand?`)
  - `SaveCommandParameter` (`object?`)
  - `AutoAttachWindowHooks` (`bool`, default `true`) — binds `Esc` key and `Window.Closing` guard automatically.
- **Events & Methods:**
  - `SaveBeforeCloseRequested` (`EventHandler<CancelEventArgs>`)
  - `CloseRequested` (`EventHandler<CancelEventArgs>`)
  - `RequestClose()`, `ForceCloseWithoutPrompt()`

### D. `AnatomicalToothControl` & `ToothAnatomyCatalog`

- **Namespace:** `Occlusa.Desktop.Controls`
- **Purpose:** Renders Kreloses-style anatomical buccal/labial crown + root vector silhouettes paired with a midline-aware 5-surface (`M`, `O`/`I`, `D`, `B`, `L`) occlusal diagram and stage badge inside `VisitRecordModal`.
- **Dentition Coverage:** Supports all **52 FDI teeth** — **Permanent (`11–48`)** and **Primary / Pediatric (`51–85`)** — switched via a zero-scrollbar mode toggle inside `VisitRecordModal`.
- **Attribution:** Anatomical crown and root vector curves adapted from `ZoliQua/React-Advanced-Odontogram` (MIT License) and frozen as immutable WPF `Geometry` instances.

---

## 4. Audit Checklist (Enforced Before `[ui-audit-passed]`)

Every `.xaml` file under `src\Occlusa.Desktop\` MUST pass all checks:
- [ ] Zero occurrences of `VerticalScrollBarVisibility="Auto"` or `"Visible"`.
- [ ] Zero occurrences of `HorizontalScrollBarVisibility="Auto"` or `"Visible"`.
- [ ] Zero occurrences of `<ScrollViewer` without `VerticalScrollBarVisibility="Disabled"` and `HorizontalScrollBarVisibility="Disabled"` (and zero scrollable containers wrapping views/modals).
- [ ] Zero occurrences of `CharacterEllipsis` or `WordEllipsis`.
- [ ] Every list/table view uses `UniformPagedGrid` stretched to its host panel with strict single-line `36px` rows (`AllowAutoRowHeight="True"` and `FitColumnsToContent="True"` are NOT used), priority-ordered atomic `UniformColumnDef` definitions (`Priority="1"`..`"4"`), hover tooltips, and double-click modal opening.
- [ ] `MedicalAlertsBanner` pinned on top of `PatientDetailModal`, `VisitRecordModal`, and `AppointmentDetailModal` (and omitted from `MainWindow` list tabs, which use compact `[ALERT]` row badges).
- [ ] Every modal (`Views\*Modal.xaml`) uses `ModalDialogShell` (`ShowDialog()`), has NO back-navigation buttons, and sets `HasUnsavedChanges` on editable clinical/billing/preference modals (`PatientDetailModal`, `VisitRecordModal`, `AppointmentDetailModal`, `AppointmentReminderModal`, `UserPreferencesModal`, `InvoiceCheckoutModal`, `BillingCatalogItemModal`, `TreatmentSuggestionRuleModal`).
- [ ] `VisitRecordModal.xaml` contains zero monetary fee inputs (`FeeInput`, `Fee (RM)`)—clinical charting stays strictly decoupled from invoicing.
- [ ] Workspace tab containers never combine `Visibility="{Binding Is*Workspace, ...}"` and `DataContext="{Binding ...}"` on the same XAML tag (wrap child `DataContext` containers inside an outer `<Grid Visibility="...">` bound to `MainViewModel`).
