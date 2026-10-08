using Sample.Documents;
using Sample.Planning;
using Sample.Shipping;

namespace Sample.Tests;

public sealed class DomainFlowTests
{
    private static readonly Shipment[] Shipments =
    [
        new("S-10", 3, 2, true, ShippingTemperature.Ambient),
        new("S-20", 2, 2, true, ShippingTemperature.Chilled),
        new("S-30", 1, 1, false, ShippingTemperature.Ambient),
        new("S-40", 7, 0, true, ShippingTemperature.Ambient)
    ];

    private static readonly Document[] Documents =
    [
        new("D-10", 4, 1, true, DocumentFormat.PlainText),
        new("D-20", 2, 2, true, DocumentFormat.Pdf),
        new("D-30", 1, 0, false, DocumentFormat.PlainText),
        new("D-40", 8, 0, true, DocumentFormat.PlainText)
    ];

    [Fact]
    public void When_BothDomainsPlan_Should_KeepEligibilityAndRejectionsDomainOwned()
    {
        var shipping = ShippingPlanner.CreatePlan(Shipments, new(false, 5), 10, 2);
        var documents = DocumentPlanner.CreatePlan(Documents, new(false, 6), 12, 2);

        Assert.Equal("3:S-10", Snapshot(shipping.Batches));
        Assert.Equal("4:D-10", Snapshot(documents.Batches));
        Assert.Equal(
            [ShippingRejection.ColdStorageUnavailable, ShippingRejection.Unpaid, ShippingRejection.ExceedsWeightLimit],
            shipping.Rejected.Select(r => r.Reason));
        Assert.Equal(["S-20", "S-30", "S-40"], shipping.Rejected.Select(r => r.Shipment.Id));
        Assert.Equal(
            [DocumentRejection.UnsupportedFormat, DocumentRejection.ConsentMissing, DocumentRejection.ExceedsPageLimit],
            documents.Rejected.Select(r => r.Reason));
        Assert.Equal(["D-20", "D-30", "D-40"], documents.Rejected.Select(r => r.Document.Id));
    }

    [Theory]
    [InlineData(true, 5, "5:S-10,S-20")]
    [InlineData(false, 8, "10:S-10,S-40")]
    public void When_ShippingOwnedCapabilityOrBusinessRuleChanges_Should_LeaveDocumentsUnchanged(
        bool canKeepCold, int maxWeight, string expected)
    {
        var documentsBefore = DocumentPlanner.CreatePlan(Documents, new(false, 6), 12, 2);

        var shipping = ShippingPlanner.CreatePlan(Shipments, new(canKeepCold, maxWeight), 10, 2);
        var documentsAfter = DocumentPlanner.CreatePlan(Documents, new(false, 6), 12, 2);

        Assert.Equal(expected, Snapshot(shipping.Batches));
        Assert.Equal(Snapshot(documentsBefore.Batches), Snapshot(documentsAfter.Batches));
        Assert.Equal(documentsBefore.Rejected, documentsAfter.Rejected);
    }

    [Theory]
    [InlineData(true, 6, "6:D-20,D-10")]
    [InlineData(false, 10, "12:D-10,D-40")]
    public void When_DocumentOwnedCapabilityOrBusinessRuleChanges_Should_LeaveShippingUnchanged(
        bool supportsPdf, int maxPages, string expected)
    {
        var shippingBefore = ShippingPlanner.CreatePlan(Shipments, new(false, 5), 10, 2);

        var documents = DocumentPlanner.CreatePlan(Documents, new(supportsPdf, maxPages), 12, 2);
        var shippingAfter = ShippingPlanner.CreatePlan(Shipments, new(false, 5), 10, 2);

        Assert.Equal(expected, Snapshot(documents.Batches));
        Assert.Equal(Snapshot(shippingBefore.Batches), Snapshot(shippingAfter.Batches));
        Assert.Equal(shippingBefore.Rejected, shippingAfter.Rejected);
    }

    [Fact]
    public void When_SourceOrderChanges_Should_PreservePriorityOrderAndBothBatchLimits()
    {
        Shipment[] shipments =
        [
            new("G", 1, 0, true, ShippingTemperature.Ambient),
            new("F", 1, 1, true, ShippingTemperature.Ambient),
            new("E", 1, 1, true, ShippingTemperature.Ambient),
            new("D", 1, 2, true, ShippingTemperature.Ambient),
            new("C", 2, 2, true, ShippingTemperature.Ambient),
            new("B", 3, 3, true, ShippingTemperature.Ambient),
            new("A", 4, 3, true, ShippingTemperature.Ambient)
        ];

        var original = ShippingPlanner.CreatePlan(shipments, new(false, 5), 6, 2);
        var reversed = ShippingPlanner.CreatePlan(shipments.Reverse(), new(false, 5), 6, 2);

        Assert.Equal("4:A|5:B,C|2:D,E|2:F,G", Snapshot(original.Batches));
        Assert.Equal(Snapshot(original.Batches), Snapshot(reversed.Batches));
        Assert.Empty(original.Rejected);
    }

    [Theory]
    [InlineData(true, "", 1, 0)]
    [InlineData(false, "", 1, 0)]
    [InlineData(true, " padded ", 1, 0)]
    [InlineData(false, " padded ", 1, 0)]
    [InlineData(true, "valid-id", 1, -1)]
    [InlineData(false, "valid-id", 1, -1)]
    [InlineData(true, "valid-id", 6, 0)]
    [InlineData(false, "valid-id", 6, 0)]
    public void When_EligibleItemViolatesCommonContract_Should_RejectThroughActualCore(
        bool shipping, string id, int units, int priority)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            PlanSingle(shipping, id, units, priority, 5, 2));

        Assert.Equal("items", error.ParamName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void When_EligibleIdentifiersAreDuplicated_Should_RejectThroughActualCore(bool shipping)
    {
        var error = Assert.Throws<ArgumentException>(() =>
        {
            if (shipping)
                ShippingPlanner.CreatePlan(
                    [new("same", 1, 0, true, ShippingTemperature.Ambient),
                     new("same", 2, 1, true, ShippingTemperature.Ambient)], new(false, 5), 5, 2);
            else
                DocumentPlanner.CreatePlan(
                    [new("same", 1, 0, true, DocumentFormat.PlainText),
                     new("same", 2, 1, true, DocumentFormat.PlainText)], new(false, 5), 5, 2);
        });

        Assert.Equal("items", error.ParamName);
    }

    [Theory]
    [InlineData(true, 0, 2, "capacity")]
    [InlineData(false, 0, 2, "capacity")]
    [InlineData(true, 5, 0, "maxItemsPerBatch")]
    [InlineData(false, 5, 0, "maxItemsPerBatch")]
    public void When_BatchLimitsAreInvalid_Should_RejectEvenAnEmptyEligibleScope(
        bool shipping, int capacity, int maxItems, string parameter)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            if (shipping)
                ShippingPlanner.CreatePlan([], new(false, 5), capacity, maxItems);
            else
                DocumentPlanner.CreatePlan([], new(false, 5), capacity, maxItems);
        });

        Assert.Equal(parameter, error.ParamName);
    }

    [Fact]
    public void When_EveryItemIsIneligible_Should_ReportRejectionsWithoutBatches()
    {
        var shipping = ShippingPlanner.CreatePlan(
            [new("unpaid", 1, 0, false, ShippingTemperature.Ambient)], new(false, 5), 5, 2);
        var documents = DocumentPlanner.CreatePlan(
            [new("no-consent", 1, 0, false, DocumentFormat.PlainText)], new(false, 5), 5, 2);

        Assert.Empty(shipping.Batches);
        Assert.Equal(ShippingRejection.Unpaid, Assert.Single(shipping.Rejected).Reason);
        Assert.Empty(documents.Batches);
        Assert.Equal(DocumentRejection.ConsentMissing, Assert.Single(documents.Rejected).Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void When_DomainMeasurementsAreInvalid_Should_ThrowRatherThanReportIneligibility(int units)
    {
        Assert.Throws<ArgumentException>(() => ShippingPlanner.CreatePlan(
            [new("unpaid", units, 0, false, ShippingTemperature.Ambient)], new(false, 5), 5, 2));
        Assert.Throws<ArgumentException>(() => DocumentPlanner.CreatePlan(
            [new("no-consent", units, 0, false, DocumentFormat.PlainText)], new(false, 5), 5, 2));
    }

    [Fact]
    public void When_DomainCapabilitiesAreUnknown_Should_NotTreatThemAsSupported()
    {
        Assert.Throws<ArgumentException>(() => ShippingPlanner.CreatePlan(
            [new("shipment", 1, 0, true, (ShippingTemperature)99)], new(true, 5), 5, 2));
        Assert.Throws<ArgumentException>(() => DocumentPlanner.CreatePlan(
            [new("document", 1, 0, true, (DocumentFormat)99)], new(true, 5), 5, 2));
    }

    [Fact]
    public void When_DomainPolicyLimitsAreInvalid_Should_ThrowBeforePlanning()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShippingPlanner.CreatePlan([], new(false, 0), 5, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DocumentPlanner.CreatePlan([], new(false, 0), 5, 2));
    }

    [Fact]
    public void When_TotalWouldOverflow_Should_StartANewBatchWithoutOverflow()
    {
        Document[] documents =
        [
            new("A", int.MaxValue - 1, 0, true, DocumentFormat.PlainText),
            new("B", 2, 0, true, DocumentFormat.PlainText)
        ];

        var plan = DocumentPlanner.CreatePlan(documents, new(false, int.MaxValue), int.MaxValue, 2);

        Assert.Equal("2147483646:A|2:B", Snapshot(plan.Batches));
    }

    [Fact]
    public void When_IdentifiersDifferByCase_Should_KeepThemDistinctAndOrderOrdinally()
    {
        Document[] documents =
        [
            new("a", 1, 0, true, DocumentFormat.PlainText),
            new("Z", 1, 0, true, DocumentFormat.PlainText),
            new("A", 1, 0, true, DocumentFormat.PlainText)
        ];

        var plan = DocumentPlanner.CreatePlan(documents, new(false, 5), 5, 3);

        Assert.Equal("3:A,Z,a", Snapshot(plan.Batches));
    }

    private static void PlanSingle(
        bool shipping, string id, int units, int priority, int capacity, int maxItems)
    {
        if (shipping)
            ShippingPlanner.CreatePlan(
                [new(id, units, priority, true, ShippingTemperature.Ambient)],
                new(false, int.MaxValue), capacity, maxItems);
        else
            DocumentPlanner.CreatePlan(
                [new(id, units, priority, true, DocumentFormat.PlainText)],
                new(false, int.MaxValue), capacity, maxItems);
    }

    private static string Snapshot(IReadOnlyList<WorkBatch> batches) =>
        string.Join("|", batches.Select(b => $"{b.TotalUnits}:{string.Join(",", b.Items.Select(i => i.Id))}"));
}
