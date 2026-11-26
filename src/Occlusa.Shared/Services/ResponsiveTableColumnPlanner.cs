namespace Occlusa.Shared.Services;

/// <summary>
/// Input descriptor for one candidate column evaluated by <see cref="ResponsiveTableColumnPlanner"/>.
/// </summary>
public sealed record ResponsiveColumnSpec(
    int Index,
    int Priority,
    double RequiredWidth,
    double FloorWidth,
    double Weight = 1.0,
    bool IsOptional = true);

/// <summary>
/// Computed visibility and allocated width for one column inside a fixed-width table panel.
/// </summary>
public sealed record ResponsiveColumnAllocation(
    int Index,
    bool IsVisible,
    double AllocatedWidth,
    double EffectiveMinWidth);

/// <summary>
/// Deterministic priority-based responsive column planner for single-line paginated desktop tables.
/// Guarantees:
/// 1) Maximum high-value atomic columns that fit cleanly inside the host panel width,
/// 2) Zero horizontal scrollbars and zero ellipsis truncation,
/// 3) Zero single-line right-edge clipping whenever essential columns fit the panel (allocated width &gt;= required width),
/// 4) Original left-to-right visual column order preserved regardless of priority selection order.
/// </summary>
public static class ResponsiveTableColumnPlanner
{
    private const double MinColumnFloor = 36.0;
    private const double ExtremeMinColumnFloor = 24.0;
    private const double MinColumnWeight = 0.15;

    public static IReadOnlyList<ResponsiveColumnAllocation> ComputeLayout(
        IReadOnlyList<ResponsiveColumnSpec> columns,
        double availableWidth)
    {
        if (columns is null || columns.Count == 0)
        {
            return Array.Empty<ResponsiveColumnAllocation>();
        }

        int count = columns.Count;
        var normalized = new NormalizedColumn[count];
        int minPriority = int.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var col = columns[i];
            int priority = Math.Max(1, col.Priority);
            double floor = double.IsFinite(col.FloorWidth) ? Math.Max(MinColumnFloor, col.FloorWidth) : MinColumnFloor;
            double required = double.IsFinite(col.RequiredWidth) ? Math.Max(floor, col.RequiredWidth) : floor;
            double weight = double.IsFinite(col.Weight) ? Math.Max(MinColumnWeight, col.Weight) : 1.0;

            normalized[i] = new NormalizedColumn(col.Index, i, priority, required, floor, weight, col.IsOptional);
            if (priority < minPriority)
            {
                minPriority = priority;
            }
        }

        // Before initial layout (availableWidth <= 0), render essential columns at their measured widths.
        if (!double.IsFinite(availableWidth) || availableWidth <= 0.0)
        {
            var initial = new ResponsiveColumnAllocation[count];
            for (int i = 0; i < count; i++)
            {
                var c = normalized[i];
                bool show = !c.IsOptional || c.Priority <= minPriority;
                initial[i] = new ResponsiveColumnAllocation(
                    c.ExternalIndex,
                    show,
                    show ? c.RequiredWidth : 0.0,
                    show ? c.FloorWidth : 0.0);
            }

            return initial;
        }

        var selectedFlags = new bool[count];
        double usedWidth = 0.0;
        int selectedCount = 0;

        // Phase 1: Always include essential columns (non-optional or Priority == minPriority).
        for (int i = 0; i < count; i++)
        {
            var c = normalized[i];
            if (!c.IsOptional || c.Priority <= minPriority)
            {
                selectedFlags[i] = true;
                usedWidth += c.RequiredWidth;
                selectedCount++;
            }
        }

        // Phase 2: Greedily add optional columns in (Priority asc, visual order asc) while they fit cleanly on 1 line.
        if (usedWidth < availableWidth)
        {
            var optionalCandidates = new List<NormalizedColumn>(count - selectedCount);
            for (int i = 0; i < count; i++)
            {
                if (!selectedFlags[i])
                {
                    optionalCandidates.Add(normalized[i]);
                }
            }

            optionalCandidates.Sort(static (a, b) =>
            {
                int cmp = a.Priority.CompareTo(b.Priority);
                return cmp != 0 ? cmp : a.SlotIndex.CompareTo(b.SlotIndex);
            });

            foreach (var candidate in optionalCandidates)
            {
                if (usedWidth + candidate.RequiredWidth <= availableWidth + 0.001)
                {
                    selectedFlags[candidate.SlotIndex] = true;
                    usedWidth += candidate.RequiredWidth;
                    selectedCount++;
                }
            }
        }

        // Phase 3: Allocate exact widths across selected columns in original left-to-right visual order.
        var allocations = new ResponsiveColumnAllocation[count];

        if (usedWidth <= availableWidth + 0.001)
        {
            double extraSpace = Math.Max(0.0, availableWidth - usedWidth);
            double totalWeight = 0.0;
            for (int i = 0; i < count; i++)
            {
                if (selectedFlags[i])
                {
                    totalWeight += normalized[i].Weight;
                }
            }

            if (totalWeight <= 0.0)
            {
                totalWeight = Math.Max(1, selectedCount);
            }

            for (int i = 0; i < count; i++)
            {
                var c = normalized[i];
                if (!selectedFlags[i])
                {
                    allocations[i] = new ResponsiveColumnAllocation(c.ExternalIndex, false, 0.0, 0.0);
                    continue;
                }

                double allocated = c.RequiredWidth + (extraSpace * (c.Weight / totalWeight));
                allocations[i] = new ResponsiveColumnAllocation(c.ExternalIndex, true, allocated, c.RequiredWidth);
            }

            return allocations;
        }

        // Tight fallback when mandatory Priority 1 columns alone exceed availableWidth:
        // preserve floors first, compress only surplus above floor, never scroll horizontally.
        double totalFloor = 0.0;
        for (int i = 0; i < count; i++)
        {
            if (selectedFlags[i])
            {
                totalFloor += normalized[i].FloorWidth;
            }
        }

        if (totalFloor <= availableWidth)
        {
            double surplusBudget = availableWidth - totalFloor;
            double requestedSurplus = Math.Max(1.0, usedWidth - totalFloor);

            for (int i = 0; i < count; i++)
            {
                var c = normalized[i];
                if (!selectedFlags[i])
                {
                    allocations[i] = new ResponsiveColumnAllocation(c.ExternalIndex, false, 0.0, 0.0);
                    continue;
                }

                double colSurplus = Math.Max(0.0, c.RequiredWidth - c.FloorWidth);
                double allocated = c.FloorWidth + (surplusBudget * (colSurplus / requestedSurplus));
                allocations[i] = new ResponsiveColumnAllocation(c.ExternalIndex, true, allocated, Math.Min(c.FloorWidth, allocated));
            }
        }
        else
        {
            double scale = availableWidth / Math.Max(1.0, totalFloor);
            for (int i = 0; i < count; i++)
            {
                var c = normalized[i];
                if (!selectedFlags[i])
                {
                    allocations[i] = new ResponsiveColumnAllocation(c.ExternalIndex, false, 0.0, 0.0);
                    continue;
                }

                double allocated = Math.Max(ExtremeMinColumnFloor, c.FloorWidth * scale);
                allocations[i] = new ResponsiveColumnAllocation(c.ExternalIndex, true, allocated, Math.Min(ExtremeMinColumnFloor, allocated));
            }
        }

        return allocations;
    }

    private readonly record struct NormalizedColumn(
        int ExternalIndex,
        int SlotIndex,
        int Priority,
        double RequiredWidth,
        double FloorWidth,
        double Weight,
        bool IsOptional);
}

