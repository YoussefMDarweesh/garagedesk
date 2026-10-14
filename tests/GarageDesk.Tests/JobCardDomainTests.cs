using GarageDesk.Web.Models;
using GarageDesk.Web.Services;

namespace GarageDesk.Tests;

public class JobCardDomainTests
{
    // ---- totals ----

    private static JobLine Line(JobLineType type, decimal quantity, decimal unitPrice) =>
        new() { Type = type, Description = "x", Quantity = quantity, UnitPrice = unitPrice };

    [Fact]
    public void Two_parts_and_one_labor_line_add_up()
    {
        var job = new JobCard
        {
            Lines =
            [
                Line(JobLineType.Part, 2, 150.00m),    // 300.00
                Line(JobLineType.Part, 1, 85.50m),     //  85.50
                Line(JobLineType.Labor, 1.5m, 200m),   // 300.00
            ],
        };

        Assert.Equal(385.50m, job.PartsTotal);
        Assert.Equal(300.00m, job.LaborTotal);
        Assert.Equal(685.50m, job.Total);
    }

    [Fact]
    public void A_job_card_without_lines_totals_zero()
    {
        var job = new JobCard();

        Assert.Equal(0m, job.Total);
        Assert.Equal(0m, job.PartsTotal);
        Assert.Equal(0m, job.LaborTotal);
    }

    [Theory]
    [InlineData(3, 33.335, 100.01)]   // 100.005 rounds half away from zero, like a printed invoice
    [InlineData(1, 10.004, 10.00)]
    [InlineData(0.5, 0.01, 0.01)]     // 0.005 -> 0.01
    [InlineData(4, 25, 100.00)]
    public void A_line_total_is_rounded_to_the_piastre(double quantity, double unitPrice, double expected)
    {
        var line = Line(JobLineType.Part, (decimal)quantity, (decimal)unitPrice);

        Assert.Equal((decimal)expected, line.LineTotal);
    }

    [Fact]
    public void The_total_is_the_sum_of_the_rounded_lines()
    {
        // Each line is 1.005 -> 1.01, so the card is 2.02 (not 2.01 from rounding the sum once).
        var job = new JobCard { Lines = [Line(JobLineType.Part, 1, 1.005m), Line(JobLineType.Part, 1, 1.005m)] };

        Assert.Equal(2.02m, job.Total);
    }

    [Fact]
    public void Only_a_paid_job_card_is_locked()
    {
        foreach (var status in Enum.GetValues<JobStatus>())
        {
            Assert.Equal(status == JobStatus.Paid, new JobCard { Status = status }.IsLocked);
        }
    }

    // ---- status flow ----

    [Theory]
    // forward, one step
    [InlineData(JobStatus.Booked, JobStatus.InProgress, true)]
    [InlineData(JobStatus.InProgress, JobStatus.Done, true)]
    [InlineData(JobStatus.Done, JobStatus.Paid, true)]
    // back, one step, to fix a mistake
    [InlineData(JobStatus.InProgress, JobStatus.Booked, true)]
    [InlineData(JobStatus.Done, JobStatus.InProgress, true)]
    // no skipping
    [InlineData(JobStatus.Booked, JobStatus.Done, false)]
    [InlineData(JobStatus.Booked, JobStatus.Paid, false)]
    [InlineData(JobStatus.InProgress, JobStatus.Paid, false)]
    [InlineData(JobStatus.Done, JobStatus.Booked, false)]
    // paid is final
    [InlineData(JobStatus.Paid, JobStatus.Done, false)]
    [InlineData(JobStatus.Paid, JobStatus.InProgress, false)]
    [InlineData(JobStatus.Paid, JobStatus.Booked, false)]
    // staying put is not a move
    [InlineData(JobStatus.Booked, JobStatus.Booked, false)]
    [InlineData(JobStatus.Paid, JobStatus.Paid, false)]
    public void Status_changes_follow_the_flow(JobStatus from, JobStatus to, bool allowed)
    {
        Assert.Equal(allowed, JobStatusFlow.CanMove(from, to));
    }

    [Fact]
    public void Exactly_five_of_the_sixteen_moves_are_allowed()
    {
        var statuses = Enum.GetValues<JobStatus>();

        var allowed = statuses.SelectMany(from => statuses, (from, to) => JobStatusFlow.CanMove(from, to)).Count(x => x);

        Assert.Equal(5, allowed);
    }

    [Theory]
    [InlineData(JobStatus.Booked, JobStatus.InProgress, null)]
    [InlineData(JobStatus.InProgress, JobStatus.Done, JobStatus.Booked)]
    [InlineData(JobStatus.Done, JobStatus.Paid, JobStatus.InProgress)]
    [InlineData(JobStatus.Paid, null, null)]
    public void Next_and_previous_offer_only_allowed_moves(JobStatus status, JobStatus? next, JobStatus? previous)
    {
        Assert.Equal(next, JobStatusFlow.Next(status));
        Assert.Equal(previous, JobStatusFlow.Previous(status));
    }
}
