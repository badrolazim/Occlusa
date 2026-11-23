using System.Text.Json;
using Occlusa.Shared.Models;

namespace Occlusa.Tests;

public class DtosTests
{
    [Fact]
    public void MedicalAlertDto_HasAnyCriticalAlert_IgnoresNoneAndDetectsFlags()
    {
        var clean = new MedicalAlertDto { Allergies = "None" };
        Assert.False(clean.HasAnyCriticalAlert);
        Assert.Equal("No Critical Alerts", clean.BuildAlertSummary());

        var nilAlert = new MedicalAlertDto { Allergies = "NIL" };
        Assert.False(nilAlert.HasAnyCriticalAlert);

        var critical = new MedicalAlertDto
        {
            Allergies = "Penicillin",
            OnAnticoagulants = true,
            HasDiabetes = true,
            IsPregnant = false
        };
        Assert.True(critical.HasAnyCriticalAlert);
        var summary = critical.BuildAlertSummary();
        Assert.Contains("Allergy: Penicillin", summary);
        Assert.Contains("Anticoagulants", summary);
        Assert.Contains("Diabetes", summary);
    }

    [Fact]
    public void InvoiceDto_BalanceDue_ZeroWhenVoided()
    {
        var invoice = new InvoiceDto
        {
            PatientPayableAmount = 250m,
            PaidAmount = 100m,
            IsVoided = false
        };
        Assert.Equal(150m, invoice.BalanceDue);

        invoice.IsVoided = true;
        Assert.Equal(0m, invoice.BalanceDue);
    }

    [Fact]
    public void ClinicDashboardSnapshotDto_RoundTripsCleanlyWithSystemTextJson()
    {
        var snapshot = new ClinicDashboardSnapshotDto
        {
            BranchId = "HQ-KL",
            DatabaseMode = "LocalSeededFallback",
            Queue =
            [
                new AppointmentQueueItemDto
                {
                    BranchId = "HQ-KL",
                    PatientName = "Ahmad Daniel",
                    Status = QueueStatus.AiBookedPending,
                    HasCriticalMedicalAlert = true,
                    AlertSummary = "Diabetes"
                }
            ],
            Patients =
            [
                new PatientDto
                {
                    FullName = "Ahmad Daniel",
                    IcNumber = "900101-14-5511",
                    DateOfBirth = new DateOnly(1990, 1, 1),
                    Gender = "Male",
                    IsMyKadVerified = true
                }
            ]
        };

        var json = JsonSerializer.Serialize(snapshot);
        var restored = JsonSerializer.Deserialize<ClinicDashboardSnapshotDto>(json);

        Assert.NotNull(restored);
        Assert.Equal("HQ-KL", restored!.BranchId);
        Assert.Single(restored.Queue);
        Assert.Equal(QueueStatus.AiBookedPending, restored.Queue[0].Status);
        Assert.Single(restored.Patients);
        Assert.Equal(new DateOnly(1990, 1, 1), restored.Patients[0].DateOfBirth);
    }
}
