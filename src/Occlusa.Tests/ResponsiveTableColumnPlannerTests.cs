using Occlusa.Shared.Services;

namespace Occlusa.Tests;

public class ResponsiveTableColumnPlannerTests
{
    [Fact]
    public void ComputeLayout_FullWidth1080p_ShowsAllHighValueColumnsWithoutClippingAndFillsExactWidth()
    {
        // Mirrors PatientsPagedGrid (10 atomic columns) on a 1920x1080 workspace (~1624px).
        var specs = new List<ResponsiveColumnSpec>
        {
            new(0, Priority: 1, RequiredWidth: 148, FloorWidth: 138, Weight: 0.5, IsOptional: false), // IC / MyKad
            new(1, Priority: 1, RequiredWidth: 225, FloorWidth: 175, Weight: 2.0, IsOptional: false), // Full Name
            new(2, Priority: 3, RequiredWidth: 84,  FloorWidth: 84,  Weight: 0.35, IsOptional: true), // Sex / Age
            new(3, Priority: 1, RequiredWidth: 130, FloorWidth: 126, Weight: 0.55, IsOptional: false), // Phone
            new(4, Priority: 3, RequiredWidth: 76,  FloorWidth: 76,  Weight: 0.3,  IsOptional: true), // Branch
            new(5, Priority: 2, RequiredWidth: 76,  FloorWidth: 74,  Weight: 0.35, IsOptional: true), // Visits
            new(6, Priority: 2, RequiredWidth: 108, FloorWidth: 104, Weight: 0.45, IsOptional: true), // Last Visit
            new(7, Priority: 2, RequiredWidth: 106, FloorWidth: 102, Weight: 0.45, IsOptional: true), // Credit (RM)
            new(8, Priority: 4, RequiredWidth: 82,  FloorWidth: 80,  Weight: 0.35, IsOptional: true), // Consent
            new(9, Priority: 1, RequiredWidth: 210, FloorWidth: 120, Weight: 1.8,  IsOptional: false) // Medical Alerts
        };

        const double availableWidth = 1624.0;
        var plan = ResponsiveTableColumnPlanner.ComputeLayout(specs, availableWidth);

        Assert.Equal(specs.Count, plan.Count);
        Assert.All(plan, a => Assert.True(a.IsVisible));

        // Original visual left-to-right order preserved
        for (int i = 0; i < plan.Count; i++)
        {
            Assert.Equal(i, plan[i].Index);
            Assert.True(plan[i].AllocatedWidth >= specs[i].RequiredWidth - 0.01,
                $"Column {i} clipped: allocated {plan[i].AllocatedWidth:F1} < required {specs[i].RequiredWidth:F1}");
        }

        double totalAllocated = plan.Where(a => a.IsVisible).Sum(a => a.AllocatedWidth);
        Assert.InRange(totalAllocated, availableWidth - 0.05, availableWidth + 0.05);
    }

    [Fact]
    public void ComputeLayout_Compact1280p_HidesLowerPriorityColumnsBeforeAnyVisibleCellClips()
    {
        var specs = new List<ResponsiveColumnSpec>
        {
            new(0, Priority: 1, RequiredWidth: 148, FloorWidth: 138, Weight: 0.5, IsOptional: false),
            new(1, Priority: 1, RequiredWidth: 225, FloorWidth: 175, Weight: 2.0, IsOptional: false),
            new(2, Priority: 3, RequiredWidth: 84,  FloorWidth: 84,  Weight: 0.35, IsOptional: true),
            new(3, Priority: 1, RequiredWidth: 130, FloorWidth: 126, Weight: 0.55, IsOptional: false),
            new(4, Priority: 3, RequiredWidth: 76,  FloorWidth: 76,  Weight: 0.3,  IsOptional: true),
            new(5, Priority: 2, RequiredWidth: 76,  FloorWidth: 74,  Weight: 0.35, IsOptional: true),
            new(6, Priority: 2, RequiredWidth: 108, FloorWidth: 104, Weight: 0.45, IsOptional: true),
            new(7, Priority: 2, RequiredWidth: 106, FloorWidth: 102, Weight: 0.45, IsOptional: true),
            new(8, Priority: 4, RequiredWidth: 82,  FloorWidth: 80,  Weight: 0.35, IsOptional: true),
            new(9, Priority: 1, RequiredWidth: 210, FloorWidth: 120, Weight: 1.8,  IsOptional: false)
        };

        // Total P1 = 713px; P1 + P2 = 1003px; at 984px (1280px window), P1 + first two P2 (897px) + Branch (76px = 973px) fit cleanly
        const double availableWidth = 984.0;
        var plan = ResponsiveTableColumnPlanner.ComputeLayout(specs, availableWidth);

        // All Priority 1 columns must remain visible
        Assert.True(plan[0].IsVisible);
        Assert.True(plan[1].IsVisible);
        Assert.True(plan[3].IsVisible);
        Assert.True(plan[9].IsVisible);

        // Priority 4 (Consent) and overflowing optional columns must auto-hide
        Assert.False(plan[8].IsVisible);
        Assert.False(plan[7].IsVisible); // 897 + 106 = 1003 > 984

        // Every visible column is strictly guaranteed >= its required single-line width (zero clipping)
        for (int i = 0; i < plan.Count; i++)
        {
            if (plan[i].IsVisible)
            {
                Assert.True(plan[i].AllocatedWidth >= specs[i].RequiredWidth - 0.01);
            }
            else
            {
                Assert.Equal(0.0, plan[i].AllocatedWidth);
            }
        }

        Assert.InRange(plan.Where(a => a.IsVisible).Sum(a => a.AllocatedWidth), availableWidth - 0.05, availableWidth + 0.05);
    }

    [Fact]
    public void ComputeLayout_LongSingleLineValueInEssentialColumn_HidesOptionalColumnsEarlierToPreventClipping()
    {
        // Patient has a long untruncated Medical Alerts value requiring 460px on a single line
        var specs = new List<ResponsiveColumnSpec>
        {
            new(0, Priority: 1, RequiredWidth: 148, FloorWidth: 138, Weight: 0.5, IsOptional: false),
            new(1, Priority: 1, RequiredWidth: 215, FloorWidth: 175, Weight: 2.0, IsOptional: false),
            new(2, Priority: 1, RequiredWidth: 130, FloorWidth: 126, Weight: 0.55, IsOptional: false),
            new(3, Priority: 2, RequiredWidth: 108, FloorWidth: 104, Weight: 0.45, IsOptional: true),
            new(4, Priority: 2, RequiredWidth: 106, FloorWidth: 102, Weight: 0.45, IsOptional: true),
            new(5, Priority: 1, RequiredWidth: 460, FloorWidth: 120, Weight: 1.8,  IsOptional: false)
        };

        // P1 alone requires 953px; on 1020px panel, neither 108px nor 106px optional column can fit -> both hide early
        const double availableWidth = 1020.0;
        var plan = ResponsiveTableColumnPlanner.ComputeLayout(specs, availableWidth);

        Assert.False(plan[3].IsVisible);
        Assert.False(plan[4].IsVisible);
        Assert.True(plan[5].IsVisible);
        Assert.True(plan[5].AllocatedWidth >= 460.0);
        Assert.InRange(plan.Where(a => a.IsVisible).Sum(a => a.AllocatedWidth), availableWidth - 0.05, availableWidth + 0.05);
    }

    [Fact]
    public void ComputeLayout_NarrowModalPanel_FitsMaximumAtomicColumnsAndSkipsOverflowingWideOptionalColumn()
    {
        // Mirrors InvoiceCheckoutModal left panel (~620px) with long procedure name
        var specs = new List<ResponsiveColumnSpec>
        {
            new(0, Priority: 1, RequiredWidth: 96,  FloorWidth: 92,  Weight: 0.55, IsOptional: false), // Tooth / Type
            new(1, Priority: 1, RequiredWidth: 265, FloorWidth: 175, Weight: 2.4,  IsOptional: false), // Invoice Item
            new(2, Priority: 3, RequiredWidth: 66,  FloorWidth: 62,  Weight: 0.3,  IsOptional: true),  // Comm.
            new(3, Priority: 2, RequiredWidth: 52,  FloorWidth: 52,  Weight: 0.25, IsOptional: true),  // Qty
            new(4, Priority: 2, RequiredWidth: 96,  FloorWidth: 92,  Weight: 0.55, IsOptional: true),  // Unit (RM)
            new(5, Priority: 1, RequiredWidth: 100, FloorWidth: 96,  Weight: 0.6,  IsOptional: false)  // Total (RM)
        };

        // P1 = 461px; P1 + Qty(52) + Unit(96) = 609px <= 620px; Comm(66) would push to 675 > 620 -> auto-hides
        const double availableWidth = 620.0;
        var plan = ResponsiveTableColumnPlanner.ComputeLayout(specs, availableWidth);

        Assert.True(plan[0].IsVisible);
        Assert.True(plan[1].IsVisible);
        Assert.False(plan[2].IsVisible); // Comm. hidden cleanly
        Assert.True(plan[3].IsVisible);  // Qty visible
        Assert.True(plan[4].IsVisible);  // Unit (RM) visible
        Assert.True(plan[5].IsVisible);  // Total (RM) visible

        Assert.True(plan[1].AllocatedWidth >= 265.0);
        Assert.InRange(plan.Where(a => a.IsVisible).Sum(a => a.AllocatedWidth), availableWidth - 0.05, availableWidth + 0.05);
    }

    [Fact]
    public void ComputeLayout_ExtremeOverflowOnEssentialColumns_CompressesSurplusWithoutHorizontalScrollOrNegativeWidth()
    {
        var specs = new List<ResponsiveColumnSpec>
        {
            new(0, Priority: 1, RequiredWidth: 140, FloorWidth: 100, Weight: 1.0, IsOptional: false),
            new(1, Priority: 1, RequiredWidth: 720, FloorWidth: 160, Weight: 2.0, IsOptional: false),
            new(2, Priority: 2, RequiredWidth: 110, FloorWidth: 90,  Weight: 1.0, IsOptional: true)
        };

        const double availableWidth = 500.0;
        var plan = ResponsiveTableColumnPlanner.ComputeLayout(specs, availableWidth);

        Assert.True(plan[0].IsVisible);
        Assert.True(plan[1].IsVisible);
        Assert.False(plan[2].IsVisible);
        Assert.True(plan[0].AllocatedWidth >= 100.0);
        Assert.True(plan[1].AllocatedWidth >= 160.0);
        Assert.InRange(plan[0].AllocatedWidth + plan[1].AllocatedWidth, availableWidth - 0.05, availableWidth + 0.05);
    }

    [Fact]
    public void ComputeLayout_ZeroInitialWidth_ReturnsEssentialColumnsSafely()
    {
        var specs = new List<ResponsiveColumnSpec>
        {
            new(0, Priority: 1, RequiredWidth: 120, FloorWidth: 90, Weight: 1.0, IsOptional: false),
            new(1, Priority: 2, RequiredWidth: 140, FloorWidth: 100, Weight: 1.0, IsOptional: true)
        };

        var plan = ResponsiveTableColumnPlanner.ComputeLayout(specs, 0.0);
        Assert.Equal(2, plan.Count);
        Assert.True(plan[0].IsVisible);
        Assert.False(plan[1].IsVisible);
    }
}

