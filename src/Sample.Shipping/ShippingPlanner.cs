using Sample.Planning;

namespace Sample.Shipping;

/// <summary>The temperature handling a shipment requires.</summary>
public enum ShippingTemperature
{
    /// <summary>No refrigerated handling is needed.</summary>
    Ambient,
    /// <summary>Refrigerated handling is required.</summary>
    Chilled
}

/// <summary>A shipment before shipping-owned business rules determine its eligibility.</summary>
/// <param name="Id">The shipment identifier.</param>
/// <param name="WeightKg">Positive weight in whole kilograms.</param>
/// <param name="Priority">Nonnegative dispatch priority.</param>
/// <param name="IsPaid">Whether payment permits dispatch.</param>
/// <param name="Temperature">The required temperature handling.</param>
public sealed record Shipment(
    string Id, int WeightKg, int Priority, bool IsPaid, ShippingTemperature Temperature);

/// <summary>Shipping-owned capabilities and business-rule limits.</summary>
/// <param name="CanKeepCold">Whether refrigerated shipments can be handled.</param>
/// <param name="MaxParcelWeightKg">Positive weight limit for an individual parcel.</param>
public sealed record ShippingPolicy(bool CanKeepCold, int MaxParcelWeightKg);

/// <summary>The first shipping rule that excludes a shipment from the eligible scope.</summary>
public enum ShippingRejection
{
    /// <summary>Payment is required before dispatch.</summary>
    Unpaid,
    /// <summary>The shipment requires unavailable refrigerated handling.</summary>
    ColdStorageUnavailable,
    /// <summary>The parcel exceeds the shipping policy's individual weight limit.</summary>
    ExceedsWeightLimit
}

/// <summary>A shipment excluded by a shipping-owned business rule.</summary>
/// <param name="Shipment">The excluded shipment.</param>
/// <param name="Reason">The first applicable rejection reason.</param>
public sealed record RejectedShipment(Shipment Shipment, ShippingRejection Reason);

/// <summary>The shared batches and shipping-owned rejection results.</summary>
/// <param name="Batches">Eligible shipments, with kilograms mapped to capacity units.</param>
/// <param name="Rejected">Excluded shipments, ordered by ordinal identifier.</param>
public sealed record ShippingPlan(
    IReadOnlyList<WorkBatch> Batches, IReadOnlyList<RejectedShipment> Rejected);

/// <summary>Selects dispatchable shipments before invoking the shared transformation.</summary>
public static class ShippingPlanner
{
    /// <summary>
    /// Applies shipping-owned payment, temperature and individual weight business rules. Invalid data throws;
    /// valid but ineligible shipments are reported separately, not sent to the shared builder.
    /// </summary>
    /// <param name="shipments">Shipments to evaluate once.</param>
    /// <param name="policy">The shipping-owned policy.</param>
    /// <param name="batchWeightKg">Positive total weight limit for each batch.</param>
    /// <param name="maxParcelsPerBatch">Positive parcel-count limit for each batch.</param>
    /// <returns>A plan containing read-only batches and rejection snapshots.</returns>
    /// <exception cref="ArgumentNullException">Shipments or policy is null.</exception>
    /// <exception cref="ArgumentException">Domain data or the eligible common scope is invalid.</exception>
    public static ShippingPlan CreatePlan(
        IEnumerable<Shipment> shipments, ShippingPolicy policy,
        int batchWeightKg, int maxParcelsPerBatch)
    {
        ArgumentNullException.ThrowIfNull(shipments);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.MaxParcelWeightKg);

        var eligible = new List<WorkItem>();
        var rejected = new List<RejectedShipment>();

        foreach (var shipment in shipments)
        {
            if (shipment is null)
                throw new ArgumentException("A shipment cannot be null.", nameof(shipments));
            if (shipment.WeightKg <= 0 || !Enum.IsDefined(shipment.Temperature))
                throw new ArgumentException(
                    $"Shipment '{shipment.Id}' has an invalid weight or temperature.", nameof(shipments));

            ShippingRejection? reason = !shipment.IsPaid ? ShippingRejection.Unpaid
                : shipment.Temperature == ShippingTemperature.Chilled && !policy.CanKeepCold
                    ? ShippingRejection.ColdStorageUnavailable
                : shipment.WeightKg > policy.MaxParcelWeightKg ? ShippingRejection.ExceedsWeightLimit
                : null;

            if (reason is { } rejection)
                rejected.Add(new(shipment, rejection));
            else
                eligible.Add(new(shipment.Id, shipment.WeightKg, shipment.Priority));
        }

        var batches = WorkPlanBuilder.Build(eligible, batchWeightKg, maxParcelsPerBatch);
        return new(batches,
            Array.AsReadOnly(rejected.OrderBy(r => r.Shipment.Id, StringComparer.Ordinal).ToArray()));
    }
}
