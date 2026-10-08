using System.Security.Cryptography;
using Sample.Documents;
using Sample.Planning;
using Sample.Shipping;

Shipment[] shipments =
[
    new("S-10", 3, 2, true, ShippingTemperature.Ambient),
    new("S-20", 2, 2, true, ShippingTemperature.Chilled),
    new("S-30", 1, 1, false, ShippingTemperature.Ambient),
    new("S-40", 7, 0, true, ShippingTemperature.Ambient)
];
Document[] documents =
[
    new("D-10", 4, 1, true, DocumentFormat.PlainText),
    new("D-20", 2, 2, true, DocumentFormat.Pdf),
    new("D-30", 1, 0, false, DocumentFormat.PlainText),
    new("D-40", 8, 0, true, DocumentFormat.PlainText)
];

var corePath = typeof(WorkPlanBuilder).Assembly.Location;
var coreHash = SHA256.HashData(File.ReadAllBytes(corePath));
var shippingPolicy = new ShippingPolicy(false, 5);
var documentPolicy = new DocumentPolicy(false, 6);
var shippingBaseline = ShippingPlanner.CreatePlan(shipments, shippingPolicy, 10, 2);
var documentBaseline = DocumentPlanner.CreatePlan(documents, documentPolicy, 12, 2);

Console.WriteLine("BASELINE");
PrintShipping(shippingBaseline);
PrintDocuments(documentBaseline);

Console.WriteLine("\nSHIPPING-OWNED CAPABILITY CHANGE: refrigerated handling enabled");
PrintShipping(ShippingPlanner.CreatePlan(shipments, shippingPolicy with { CanKeepCold = true }, 10, 2));
CheckDocumentsUnchanged();

Console.WriteLine("\nSHIPPING-OWNED BUSINESS RULE CHANGE: individual weight limit raised to 8 kg");
PrintShipping(ShippingPlanner.CreatePlan(shipments, shippingPolicy with { MaxParcelWeightKg = 8 }, 10, 2));
CheckDocumentsUnchanged();

Console.WriteLine("\nDOCUMENT-OWNED CAPABILITY CHANGE: PDF processing enabled");
PrintDocuments(DocumentPlanner.CreatePlan(documents, documentPolicy with { SupportsPdf = true }, 12, 2));
CheckShippingUnchanged();

Console.WriteLine("\nDOCUMENT-OWNED BUSINESS RULE CHANGE: individual page limit raised to 10");
PrintDocuments(DocumentPlanner.CreatePlan(documents, documentPolicy with { MaxPagesPerDocument = 10 }, 12, 2));
CheckShippingUnchanged();

Console.WriteLine("\nSHARED PROCESSING: batching algorithm; priority order, then ordinal identifier; capacity 6 kg, at most 2 parcels");
PrintShipping(ShippingPlanner.CreatePlan(
[
    new("G", 1, 0, true, ShippingTemperature.Ambient),
    new("F", 1, 1, true, ShippingTemperature.Ambient),
    new("E", 1, 1, true, ShippingTemperature.Ambient),
    new("D", 1, 2, true, ShippingTemperature.Ambient),
    new("C", 2, 2, true, ShippingTemperature.Ambient),
    new("B", 3, 3, true, ShippingTemperature.Ambient),
    new("A", 4, 3, true, ShippingTemperature.Ambient)
], shippingPolicy, 6, 2));

Console.WriteLine("\nINVALID INPUTS: fail explicitly through the actual entry points");
ShowInvalid("Eligible parcel exceeds common batch capacity", () =>
    ShippingPlanner.CreatePlan(
        [new("too-large", 7, 0, true, ShippingTemperature.Ambient)], new(false, 8), 5, 2));
ShowInvalid("Eligible document has a blank identifier", () =>
    DocumentPlanner.CreatePlan(
        [new("", 1, 0, true, DocumentFormat.PlainText)], documentPolicy, 12, 2));
ShowInvalid("Eligible document identifiers are duplicated", () =>
    DocumentPlanner.CreatePlan(
        [new("same", 1, 0, true, DocumentFormat.PlainText),
         new("same", 2, 1, true, DocumentFormat.PlainText)], documentPolicy, 12, 2));
ShowInvalid("Unknown shipping capability is not supported", () =>
    ShippingPlanner.CreatePlan(
        [new("unknown", 1, 0, true, (ShippingTemperature)99)], shippingPolicy, 10, 2));

if (!coreHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(corePath))))
    throw new InvalidOperationException("The shared batching algorithm binary changed during the experiment.");

Console.WriteLine("\nShared batching algorithm binary unchanged across all four domain-owned policy variations: yes");

void CheckDocumentsUnchanged()
{
    var actual = DocumentPlanner.CreatePlan(documents, documentPolicy, 12, 2);
    if (!SameBatches(documentBaseline.Batches, actual.Batches)
        || !documentBaseline.Rejected.SequenceEqual(actual.Rejected))
        throw new InvalidOperationException("A shipping-owned policy variation changed the document result.");
    Console.WriteLine("  Document result unchanged: yes");
}

void CheckShippingUnchanged()
{
    var actual = ShippingPlanner.CreatePlan(shipments, shippingPolicy, 10, 2);
    if (!SameBatches(shippingBaseline.Batches, actual.Batches)
        || !shippingBaseline.Rejected.SequenceEqual(actual.Rejected))
        throw new InvalidOperationException("A document-owned policy variation changed the shipping result.");
    Console.WriteLine("  Shipping result unchanged: yes");
}

static bool SameBatches(IReadOnlyList<WorkBatch> expected, IReadOnlyList<WorkBatch> actual) =>
    expected.Count == actual.Count
    && expected.Zip(actual).All(pair =>
        pair.First.TotalUnits == pair.Second.TotalUnits
        && pair.First.Items.SequenceEqual(pair.Second.Items));

static void PrintShipping(ShippingPlan plan)
{
    Console.WriteLine($"  Shipping (kg): {FormatBatches(plan.Batches)}");
    Console.WriteLine($"  Rejected: {string.Join("; ", plan.Rejected.Select(r => $"{r.Shipment.Id}={r.Reason}"))}");
}

static void PrintDocuments(DocumentPlan plan)
{
    Console.WriteLine($"  Documents (pages): {FormatBatches(plan.Batches)}");
    Console.WriteLine($"  Rejected: {string.Join("; ", plan.Rejected.Select(r => $"{r.Document.Id}={r.Reason}"))}");
}

static string FormatBatches(IReadOnlyList<WorkBatch> batches) =>
    string.Join(" | ", batches.Select(b => $"[{string.Join(", ", b.Items.Select(i => i.Id))}] total={b.TotalUnits}"));

static void ShowInvalid(string label, Action action)
{
    try
    {
        action();
    }
    catch (ArgumentException error)
    {
        Console.WriteLine($"  {label}: {error.GetType().Name}: {error.Message}");
        return;
    }

    throw new InvalidOperationException($"Invalid input was unexpectedly accepted: {label}.");
}
