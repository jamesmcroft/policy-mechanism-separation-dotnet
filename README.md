# Separating Business Rules from Shared Processing Logic in .NET

Sharing processing logic should not make the shared code responsible for every consumer's business rules.

This reference example uses two synthetic, unrelated domains. Each owns its vocabulary, supported capabilities, business rules governing eligibility and adapter. They reuse the same nontrivial work-plan builder without teaching it domain names. This is not a competing rules framework. There is no database, service, policy DSL, DI container or rules-engine package.

## Run locally

Requires the .NET 10 SDK. `global.json` selects 10.0.401 or a later patch in that feature band. Commands below are PowerShell commands from the repository root.

NuGet package versions are centrally managed in [Directory.Packages.props](Directory.Packages.props). Keep `PackageReference` entries in project files versionless.

```powershell
dotnet restore PolicyMechanismSeparation.slnx
dotnet build src\Sample.Planning\Sample.Planning.csproj --no-restore
dotnet build PolicyMechanismSeparation.slnx --no-restore
dotnet run --project src\Sample.Demo\Sample.Demo.csproj --no-build
```

`dotnet test PolicyMechanismSeparation.slnx` runs all tests together. The separate commands above distinguish behavioral evidence from the compiled dependency boundary check.

## Ownership boundary

| Project | Owns | Depends on |
| --- | --- | --- |
| `src\Sample.Planning` | Shared processing logic: common validation, deterministic ordering and bounded batching | None |
| `src\Sample.Shipping` | Shipment types, payment business rule, refrigerated capability, parcel weight business rule, adapter | Planning |
| `src\Sample.Documents` | Document types, consent business rule, PDF capability, document page business rule, adapter | Planning |
| `src\Sample.Demo` | Synthetic fixtures, independent domain-owned policy variations, visible results and checks | Shipping and Documents |
| `tests\Sample.Tests` | Real functional flows, common-contract edge cases, separate compiled-assembly check | Shipping and Documents |

Both domain entry points apply their own business rules, select an eligible scope and map it to `WorkItem(Id, Units, Priority)`. Shipping maps kilograms to units; document processing maps pages to units. Those meanings belong to the adapters. The demo never combines kilograms and pages into one plan.

The shared processing core knows neither domain's capability set nor its business rules governing eligibility. It does not branch on domain identifiers, labels or types. It still enforces its own contract: nonblank, unpadded, case-sensitive unique identifiers; positive units that fit the batch capacity; nonnegative priorities; and positive capacity and item-count limits. Empty scopes are valid but do not bypass limit validation.

Invalid data throws argument exceptions. Valid but ineligible domain records are returned separately with the first matching domain-owned rejection reason. Payment/consent is evaluated before capability, then the individual limit. Invalid measurements and unknown enum values throw even on otherwise ineligible records. Common identifier/priority validation applies to the **selected** scope, not to domain records that were excluded.

## Shared batching algorithm

1. Materialize and validate the supplied candidates once.
2. Sort by descending priority, then identifier using ordinal comparison.
3. Append consecutive items until the next would exceed capacity or item count, then begin a new batch.

Outputs are read-only collection snapshots containing immutable work items. Capacity checks use remaining capacity so summation cannot overflow. The algorithm is deterministic, priority-preserving next-fit batching. It can deliberately leave space unused. Sorting is `O(n log n)` and storage is `O(n)`; this experiment makes no measured performance claim.

## What the demo proves

The baseline includes ineligible records and prints their domain-owned reasons. Four domain-owned policy variations run in one process against the same shared processing core binary:

| Domain-owned policy variation | Changed eligible batch | Unchanged other-domain baseline |
| --- | --- | --- |
| Enable refrigerated shipping | `S-10, S-20`, total 5 kg | `D-10`, total 4 pages |
| Raise the individual parcel limit to 8 kg | `S-10, S-40`, total 10 kg | `D-10`, total 4 pages |
| Enable PDF processing | `D-20, D-10`, total 6 pages | `S-10`, total 3 kg |
| Raise the individual document limit to 10 pages | `D-10, D-40`, total 12 pages | `S-10`, total 3 kg |

The console compares full other-domain batches and rejection results and checks the shared assembly's SHA-256 before and after the variations. An unexpected change or accepted invalid example terminates with an error rather than claiming success. Another scenario demonstrates both batch limits: `[A] total=4 | [B, C] total=5 | [D, E] total=2 | [F, G] total=2`.

Functional tests call the actual domain entry points and builder. They cover both directions of domain-owned capability/business-rule variation, ordering independent of source order, rejection reasons, common validation through eligible domain paths, invalid domain data, an empty scope, case-sensitive identifiers and an overflow edge. Direct core edge tests cover malformed callers that bypass domain measurement validation.
