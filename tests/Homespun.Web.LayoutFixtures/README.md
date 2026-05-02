# Homespun.Web.LayoutFixtures

Cross-stack golden-fixture suite for the issue-graph layout algorithm.

The TypeScript port of `IIssueLayoutService` lives at
`src/Homespun.Web/src/features/issues/services/layout/`. Because the algorithm
is duplicated across two implementations of a separately-versioned external
library (`Fleece.Core`), drift between the C# reference and the TS port has to
be detected automatically. This project is the C# half of that contract: it
emits the reference output for a curated set of input graphs, and the TS test
suite (`golden-fixtures.test.ts`) asserts byte-equivalent output from the port.

## How it works

`EmitFixturesTests.cs` discovers every `fixtures/*.input.json`, deserializes
the body to `IReadOnlyList<Issue>` (plus an optional `mode`, `matchedIds`,
`visibility`, `assignedTo` envelope), runs `IIssueLayoutService.LayoutForTree`
or `LayoutForNext`, and serializes a normalized `FixtureOutput` envelope
(`{ ok, totalRows, totalLanes, nodes[], edges[] }` for success;
`{ ok: false, cycle[] }` when the layout throws `InvalidGraphException`).

- **Read-only mode** (default): asserts the emitted output structurally
  matches the existing `*.expected.json`. This is what runs in CI and on
  every local `dotnet test` invocation. A diff means the live engine produced
  something different from the committed reference — usually a Fleece.Core
  upgrade or a bug.
- **Update mode** (`UPDATE_FIXTURES=1`): rewrites every `*.expected.json` to
  match the live engine output. Use this when intentionally adopting a new
  Fleece.Core version, or after authoring a new `*.input.json`.

The `*.expected.json` files are **committed** alongside their inputs.

## Adding a new fixture

1. Author `fixtures/NN-name.input.json`. The envelope is:

   ```jsonc
   {
     "mode": "Tree" | "Next",        // optional, defaults to Tree
     "visibility": "Hide" | "IfHasActiveDescendants" | "Always",  // optional, defaults to Hide
     "assignedTo": "user@example",    // optional
     "matchedIds": ["id1", "id2"],    // required for Next mode
     "issues": [ /* IReadOnlyList<Issue> */ ]
   }
   ```

   `issues[]` matches Fleece.Core's `Issue` JSON shape. The minimum a fixture
   needs is `id`, `title`, `status`, `type`, `executionMode`, `lastUpdate`,
   `createdAt`, and `parentIssues[].{parentIssue,sortOrder}`.

2. Emit the expected output:

   ```bash
   UPDATE_FIXTURES=1 dotnet test tests/Homespun.Web.LayoutFixtures
   ```

3. Inspect the new `fixtures/NN-name.expected.json`. Commit both files in the
   same change.

4. Run the TS golden-fixtures test to confirm the port matches:

   ```bash
   cd src/Homespun.Web
   npm test -- features/issues/services/layout/golden-fixtures
   ```

## Regenerating after a Fleece.Core upgrade

When `Fleece.Core` is upgraded (the version is pinned in
`Homespun.Server.csproj`, `Homespun.Shared.csproj`, this project, and
`Dockerfile.base`):

1. Bump the version in this csproj alongside the others.
2. Run `UPDATE_FIXTURES=1 dotnet test tests/Homespun.Web.LayoutFixtures`.
3. Diff the `*.expected.json` changes. Each diff is a behaviour change in
   the upstream layout engine — read the upstream changelog, classify the
   diff (intentional improvement vs. regression), and either:
   - Update the TS port to match (preferred for intentional changes), or
   - Pin Fleece.Core back and file an issue upstream (for regressions).
4. Re-run the TS golden-fixtures test. Failures indicate the TS port is
   still on the previous version's behaviour and needs porting.

## Pinned upstream version

`Fleece.Core` 3.0.0 — matches `Homespun.Server`, `Homespun.Shared`, and
`Dockerfile.base`'s `Fleece.Cli` install.

When upgrading, use the public source as the porting reference:
- https://github.com/nick-boey/Fleece/blob/main/src/Fleece.Core/Services/GraphLayout/GraphLayoutService.cs
- https://github.com/nick-boey/Fleece/blob/main/src/Fleece.Core/Services/GraphLayout/IssueLayoutService.cs

## What the read-only test asserts

Structural JSON equality (whitespace-insensitive, key-ordered, array-ordered).
Numbers are compared by their raw text — `1.0` ≠ `1` is intentional, since
that would indicate the serializer behaviour changed. Strings are case-
sensitive.

If you need the read-only test to be more lenient (e.g. round numeric
precision), add the policy to `AssertJsonEqual` in `EmitFixturesTests.cs`
rather than mutating fixtures.
