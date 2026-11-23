# Occlusa Desktop UI Uniformity Rules & Architecture Specification

**Owner:** `ui-architect`  
**Target Platform:** C# .NET 8 WPF (`Occlusa.Desktop`), optimized for **1920x1080** and **2560x1440** clinic workstations.

---

## 1. Non-Negotiable Visual & UX Invariants

1. **ZERO Scrollbars Anywhere**
   - `ScrollViewer.VerticalScrollBarVisibility` and `ScrollViewer.HorizontalScrollBarVisibility` MUST NEVER be `Auto` or `Visible` in any XAML view, user control, or modal.
   - All multi-row datasets, SOAP note histories, audit trails, and WhatsApp chat transcripts MUST paginate using crisp `◀ Prev` / `Next ▶` (or `◀ Older` / `Newer ▶`) buttons.
   - Do not wrap pages or modal bodies in `<ScrollViewer>`.

2. **ZERO Text Truncation Anywhere**
   - `TextTrimming="CharacterEllipsis"` and `TextTrimming="WordEllipsis"` are STRICTLY FORBIDDEN across the entire codebase.
   - `TextTrimming="None"` is enforced in `UniformTheme.xaml` and `UniformPagedGrid`.
   - Keep main table cell values concise so they fit cleanly inside 3–4 column widths. Full clinical notes, full tooth details, itemized breakdown, and long messages live inside True Modal Popups.

3. **Uniform 3–4 Column Paged Tables + Double-Click Modals**
   - Every tabular view in `Occlusa.Desktop` MUST use `controls:UniformPagedGrid`.
   - Every table MUST display **3 to 4 columns maximum** (compact clinical font `12px`, header `11.5px`).
   - Double-clicking any row opens a **True Modal Popup** (`Window.ShowDialog()`) freezing the workspace behind it.

4. **Responsive 1920x1080 & 2560x1440 Resolution Scaling**
   - `UniformPagedGrid` automatically recomputes `PageSize` on `Loaded` and host `Window.SizeChanged` (unless an explicit `PageSize` override is set for a compact modal sub-table):
     - Window height `<= 1120px` (e.g., 1920x1080): **11 rows per page**.
     - Window height `> 1120px` (e.g., 2560x1440): **17 rows per page**.
   - Row height (`36px`), header height (`36px`), and footer height (`42px`) fit cleanly within both 1080p and 1440p viewports without overflow.

5. **Clinical Safety & Desktop Modal Discipline**
   - **Pinned `MedicalAlertsBanner`:** Must stay pinned at the top of the workspace and inside patient/clinical modals whenever a patient record is active, visibly highlighting Allergies, Anticoagulants, Diabetes, Pregnancy, and MyKad IC verification status.
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

### A. `UniformPagedGrid` & `UniformColumnDef`

- **Namespace:** `Occlusa.Desktop.Controls`
- **DependencyProperties:**
  - `ItemsSource` (`IEnumerable?`)
  - `Columns` (`ObservableCollection<UniformColumnDef>`) — max 4 columns enforced when `EnforceColumnLimit="True"` (default)
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
  - `EnforceColumnLimit` (`bool`, default `true`)
- **Events:**
  - `RowDoubleClicked` (`EventHandler<object?>`)
- **`UniformColumnDef` Properties:**
  - `Header` (`string`)
  - `BindingPath` (`string`)
  - `StarWidth` (`double`, default `1.0`; alias `WidthRatio`)
  - `StringFormat` (`string?`)
  - `HorizontalAlignment` (`HorizontalAlignment`)
  - `IsMonospace` (`bool`)
  - `IsBold` (`bool`)
  - `MinWidth` (`double`, default `70`)

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

---

## 4. Audit Checklist (Enforced Before `[ui-audit-passed]`)

Every `.xaml` file under `src\Occlusa.Desktop\` MUST pass all checks:
- [ ] Zero occurrences of `VerticalScrollBarVisibility="Auto"` or `"Visible"`.
- [ ] Zero occurrences of `HorizontalScrollBarVisibility="Auto"` or `"Visible"`.
- [ ] Zero occurrences of `<ScrollViewer` without `VerticalScrollBarVisibility="Disabled"` and `HorizontalScrollBarVisibility="Disabled"` (and zero scrollable containers wrapping views/modals).
- [ ] Zero occurrences of `CharacterEllipsis` or `WordEllipsis`.
- [ ] Every list/table view uses `UniformPagedGrid` with **3 or 4 columns** and double-click modal opening.
- [ ] `MedicalAlertsBanner` pinned on top of `MainWindow`, `PatientDetailModal`, and `OdontogramModal`.
- [ ] Every modal (`Views\*Modal.xaml`) uses `ModalDialogShell` (`ShowDialog()`), has NO back-navigation buttons, and sets `HasUnsavedChanges` on editable clinical/billing modals.
