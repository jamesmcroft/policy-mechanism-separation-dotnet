using Sample.Planning;

namespace Sample.Documents;

/// <summary>A document format understood by the document domain.</summary>
public enum DocumentFormat
{
    /// <summary>A plain-text document.</summary>
    PlainText,
    /// <summary>A PDF document.</summary>
    Pdf
}

/// <summary>A document before document-owned business rules determine its eligibility.</summary>
/// <param name="Id">The document identifier.</param>
/// <param name="PageCount">Positive number of pages.</param>
/// <param name="Priority">Nonnegative processing priority.</param>
/// <param name="HasConsent">Whether processing is authorized.</param>
/// <param name="Format">The document's format.</param>
public sealed record Document(
    string Id, int PageCount, int Priority, bool HasConsent, DocumentFormat Format);

/// <summary>Document-owned processing capabilities and business-rule limits.</summary>
/// <param name="SupportsPdf">Whether PDF processing is available.</param>
/// <param name="MaxPagesPerDocument">Positive page limit for an individual document.</param>
public sealed record DocumentPolicy(bool SupportsPdf, int MaxPagesPerDocument);

/// <summary>The first document rule that excludes a document from the eligible scope.</summary>
public enum DocumentRejection
{
    /// <summary>Processing consent is absent.</summary>
    ConsentMissing,
    /// <summary>The document requires unavailable PDF processing.</summary>
    UnsupportedFormat,
    /// <summary>The document exceeds the policy's individual page limit.</summary>
    ExceedsPageLimit
}

/// <summary>A document excluded by a document-owned business rule.</summary>
/// <param name="Document">The excluded document.</param>
/// <param name="Reason">The first applicable rejection reason.</param>
public sealed record RejectedDocument(Document Document, DocumentRejection Reason);

/// <summary>The shared batches and document-owned rejection results.</summary>
/// <param name="Batches">Eligible documents, with pages mapped to capacity units.</param>
/// <param name="Rejected">Excluded documents, ordered by ordinal identifier.</param>
public sealed record DocumentPlan(
    IReadOnlyList<WorkBatch> Batches, IReadOnlyList<RejectedDocument> Rejected);

/// <summary>Selects processable documents before invoking the shared transformation.</summary>
public static class DocumentPlanner
{
    /// <summary>
    /// Applies document-owned consent, format and individual page business rules. Invalid data throws;
    /// valid but ineligible documents are reported separately, not sent to the shared builder.
    /// </summary>
    /// <param name="documents">Documents to evaluate once.</param>
    /// <param name="policy">The document-owned policy.</param>
    /// <param name="batchPages">Positive total page limit for each batch.</param>
    /// <param name="maxDocumentsPerBatch">Positive document-count limit for each batch.</param>
    /// <returns>A plan containing read-only batches and rejection snapshots.</returns>
    /// <exception cref="ArgumentNullException">Documents or policy is null.</exception>
    /// <exception cref="ArgumentException">Domain data or the eligible common scope is invalid.</exception>
    public static DocumentPlan CreatePlan(
        IEnumerable<Document> documents, DocumentPolicy policy,
        int batchPages, int maxDocumentsPerBatch)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.MaxPagesPerDocument);

        var eligible = new List<WorkItem>();
        var rejected = new List<RejectedDocument>();

        foreach (var document in documents)
        {
            if (document is null)
                throw new ArgumentException("A document cannot be null.", nameof(documents));
            if (document.PageCount <= 0 || !Enum.IsDefined(document.Format))
                throw new ArgumentException(
                    $"Document '{document.Id}' has an invalid page count or format.", nameof(documents));

            DocumentRejection? reason = !document.HasConsent ? DocumentRejection.ConsentMissing
                : document.Format == DocumentFormat.Pdf && !policy.SupportsPdf
                    ? DocumentRejection.UnsupportedFormat
                : document.PageCount > policy.MaxPagesPerDocument ? DocumentRejection.ExceedsPageLimit
                : null;

            if (reason is { } rejection)
                rejected.Add(new(document, rejection));
            else
                eligible.Add(new(document.Id, document.PageCount, document.Priority));
        }

        var batches = WorkPlanBuilder.Build(eligible, batchPages, maxDocumentsPerBatch);
        return new(batches,
            Array.AsReadOnly(rejected.OrderBy(r => r.Document.Id, StringComparer.Ordinal).ToArray()));
    }
}
