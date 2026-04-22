using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Writes a representative set of OpenSpec artifacts (changes, sidecars, archive entries,
/// per-branch scenario fixtures) into the mock-mode seeded project so the
/// `openspec-integration` UI scenarios are visible without bringing up a real project.
///
/// Hardcoded scenario fixtures live in <see cref="MainBranchChanges"/> and
/// <see cref="SeedBranchAsync"/>; see the design doc for the per-scenario rationale.
/// </summary>
public sealed class OpenSpecMockSeeder
{
    private readonly ITempDataFolderService _tempFolderService;
    private readonly ILogger<OpenSpecMockSeeder> _logger;

    public OpenSpecMockSeeder(
        ITempDataFolderService tempFolderService,
        ILogger<OpenSpecMockSeeder> logger)
    {
        _tempFolderService = tempFolderService;
        _logger = logger;
    }

    /// <summary>
    /// Writes <c>openspec/project.md</c> and the four main-branch scenario fixtures
    /// (in-progress, ready-to-archive, orphan, archived) into the given project root.
    /// </summary>
    public async Task SeedAsync(
        string projectPath,
        IReadOnlyDictionary<string, string> branchToFleeceId,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.Combine(projectPath, "openspec"));
        await File.WriteAllTextAsync(
            Path.Combine(projectPath, "openspec", "project.md"),
            ProjectMd,
            ct);

        // 2.2 In-progress change with three phases and partial completion.
        await WriteChangeAsync(
            projectPath,
            "api-v2-design",
            ApiV2DesignProposal,
            ApiV2DesignSpec,
            ApiV2DesignTasks,
            sidecarFleeceId: "ISSUE-006",
            createdBy: "agent",
            design: ApiV2DesignDesign,
            ct: ct);

        // 2.3 Ready-to-archive change (all checkboxes ticked) linking to a different seeded issue.
        await WriteChangeAsync(
            projectPath,
            "rate-limiting",
            RateLimitingProposal,
            RateLimitingSpec,
            RateLimitingTasksAllDone,
            sidecarFleeceId: "ISSUE-012",
            createdBy: "server",
            design: null,
            ct: ct);

        // 2.4 Orphan change: no .homespun.yaml.
        await WriteChangeAsync(
            projectPath,
            "orphan-on-main",
            OrphanOnMainProposal,
            spec: null,
            tasks: OrphanOnMainTasks,
            sidecarFleeceId: null,
            createdBy: null,
            design: null,
            ct: ct);

        // 2.5 Archived change with sidecar preserved.
        await WriteArchivedChangeAsync(
            projectPath,
            archiveFolder: "2026-01-15-old-feature",
            ArchivedFeatureProposal,
            ArchivedFeatureTasksAllDone,
            sidecarFleeceId: "ISSUE-014",
            createdBy: "server",
            ct);

        _logger.LogDebug("Seeded main-branch openspec content into {ProjectPath}", projectPath);
    }

    /// <summary>
    /// Writes branch-specific OpenSpec scenario fixtures into a clone directory.
    /// The branch-name → fleece-id mapping in <paramref name="branchToFleeceId"/>
    /// drives the scenario selection — see <see cref="SeedAsync"/> for the
    /// scenarios this expects on each branch.
    /// </summary>
    public async Task SeedBranchAsync(
        string clonePath,
        string branchName,
        string? branchFleeceId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(branchFleeceId))
        {
            // 4.4 / unmapped branches: no openspec/ at all on the branch.
            return;
        }

        switch (branchFleeceId)
        {
            case "ISSUE-006":
                // 4.1 Single in-progress change with sidecar matching the branch.
                await WriteChangeAsync(
                    clonePath,
                    "api-v2-impl",
                    ApiV2ImplProposal,
                    ApiV2ImplSpec,
                    ApiV2ImplTasks,
                    sidecarFleeceId: branchFleeceId,
                    createdBy: "agent",
                    design: null,
                    ct: ct);
                break;

            case "ISSUE-002":
                // 4.2 Two unlinked changes (no sidecar) → multi-orphan UI.
                await WriteChangeAsync(
                    clonePath,
                    "dark-mode-tokens",
                    DarkModeTokensProposal,
                    spec: null,
                    tasks: DarkModeTokensTasks,
                    sidecarFleeceId: null,
                    createdBy: null,
                    design: null,
                    ct: ct);
                await WriteChangeAsync(
                    clonePath,
                    "dark-mode-toggle",
                    DarkModeToggleProposal,
                    spec: null,
                    tasks: DarkModeToggleTasks,
                    sidecarFleeceId: null,
                    createdBy: null,
                    design: null,
                    ct: ct);
                break;

            case "ISSUE-001":
                // 4.3 Inherited change: sidecar fleeceId does not match the branch.
                await WriteChangeAsync(
                    clonePath,
                    "inherited-from-main",
                    InheritedChangeProposal,
                    spec: null,
                    tasks: InheritedChangeTasks,
                    sidecarFleeceId: "ISSUE-005",
                    createdBy: "server",
                    design: null,
                    ct: ct);
                break;

            case "ISSUE-003":
                // 4.4 No openspec/ directory at all (branch-with-no-change indicator path).
                var openspecDir = Path.Combine(clonePath, "openspec");
                if (Directory.Exists(openspecDir))
                {
                    Directory.Delete(openspecDir, recursive: true);
                }
                break;

            default:
                // Other branches inherit only what was copied from main; no extra deltas.
                break;
        }

        _logger.LogDebug(
            "Seeded branch openspec content into {ClonePath} (branch={Branch}, fleece={FleeceId})",
            clonePath, branchName, branchFleeceId);
    }

    private static async Task WriteChangeAsync(
        string rootPath,
        string changeName,
        string proposal,
        string? spec,
        string tasks,
        string? sidecarFleeceId,
        string? createdBy,
        string? design,
        CancellationToken ct)
    {
        var changeDir = Path.Combine(rootPath, "openspec", "changes", changeName);
        Directory.CreateDirectory(changeDir);

        await File.WriteAllTextAsync(Path.Combine(changeDir, "proposal.md"), proposal, ct);
        await File.WriteAllTextAsync(Path.Combine(changeDir, "tasks.md"), tasks, ct);

        if (design is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(changeDir, "design.md"), design, ct);
        }

        if (spec is not null)
        {
            var specDir = Path.Combine(changeDir, "specs", changeName);
            Directory.CreateDirectory(specDir);
            await File.WriteAllTextAsync(Path.Combine(specDir, "spec.md"), spec, ct);
        }

        if (sidecarFleeceId is not null && createdBy is not null)
        {
            await File.WriteAllTextAsync(
                Path.Combine(changeDir, ".homespun.yaml"),
                $"fleeceId: {sidecarFleeceId}\ncreatedBy: {createdBy}\n",
                ct);
        }
    }

    private static async Task WriteArchivedChangeAsync(
        string rootPath,
        string archiveFolder,
        string proposal,
        string tasks,
        string sidecarFleeceId,
        string createdBy,
        CancellationToken ct)
    {
        var archivedDir = Path.Combine(rootPath, "openspec", "changes", "archive", archiveFolder);
        Directory.CreateDirectory(archivedDir);

        await File.WriteAllTextAsync(Path.Combine(archivedDir, "proposal.md"), proposal, ct);
        await File.WriteAllTextAsync(Path.Combine(archivedDir, "tasks.md"), tasks, ct);
        await File.WriteAllTextAsync(
            Path.Combine(archivedDir, ".homespun.yaml"),
            $"fleeceId: {sidecarFleeceId}\ncreatedBy: {createdBy}\n",
            ct);
    }

    // ── Hardcoded fixtures ──────────────────────────────────────────────────

    private const string ProjectMd = """
        # Demo Project

        This is a placeholder OpenSpec project file used by mock mode to exercise the
        issue-graph OpenSpec integration. It is regenerated on every dev-mock boot.
        """;

    private const string ApiV2DesignProposal = """
        ## Why

        We need a versioned API surface that returns a consistent envelope and
        cursor-based pagination across every list endpoint.

        ## What Changes

        - Define the v2 response envelope.
        - Add cursor pagination helpers.
        - Document the error-code taxonomy.
        """;

    private const string ApiV2DesignDesign = """
        ## Context

        v1 endpoints are inconsistent in error shape and pagination.

        ## Decisions

        - Wrap every response in `{ data, error, meta }`.
        - Cursor pagination uses opaque base64-encoded cursors.
        """;

    private const string ApiV2DesignSpec = """
        ## ADDED Requirements

        ### Requirement: v2 response envelope

        Every v2 endpoint SHALL return a JSON object containing a `data` field on
        success or an `error` object on failure.

        #### Scenario: Successful read returns data envelope

        - **WHEN** a v2 GET endpoint succeeds
        - **THEN** the response body SHALL contain `data` and SHALL NOT contain `error`
        """;

    private const string ApiV2DesignTasks = """
        ## Phase 1 — Design

        - [x] 1.1 Draft the response envelope shape
        - [x] 1.2 Document the error-code taxonomy

        ## Phase 2 — Implement

        - [x] 2.1 Add the envelope helper to the shared library
        - [ ] 2.2 Wire pagination cursors into the helper

        ## Phase 3 — Verify

        - [ ] 3.1 Add contract tests against the OpenAPI spec
        - [ ] 3.2 Run end-to-end tests against a sample client
        """;

    private const string RateLimitingProposal = """
        ## Why

        Public v2 endpoints need per-user rate limits to prevent abuse.

        ## What Changes

        - Add a sliding-window rate-limit middleware.
        - Return 429 with Retry-After when limits are exceeded.
        """;

    private const string RateLimitingSpec = """
        ## ADDED Requirements

        ### Requirement: Per-user sliding-window rate limit

        v2 endpoints SHALL enforce a sliding-window rate limit per authenticated user.

        #### Scenario: Exceeding the limit returns 429

        - **WHEN** a user exceeds 100 read requests per minute
        - **THEN** the next request SHALL receive HTTP 429 with a Retry-After header
        """;

    private const string RateLimitingTasksAllDone = """
        ## Phase 1 — Implement

        - [x] 1.1 Add Redis-backed sliding-window store
        - [x] 1.2 Wire middleware into the v2 pipeline

        ## Phase 2 — Verify

        - [x] 2.1 Add load test fixture
        - [x] 2.2 Confirm metrics emit on 429
        """;

    private const string OrphanOnMainProposal = """
        ## Why

        An older proposal that was never linked to an issue.
        """;

    private const string OrphanOnMainTasks = """
        ## Phase 1

        - [ ] 1.1 Investigate
        """;

    private const string ArchivedFeatureProposal = """
        ## Why

        We migrated the build from .NET 8 to .NET 9.

        ## What Changes

        - Upgrade SDK and base images.
        - Update package references.
        """;

    private const string ArchivedFeatureTasksAllDone = """
        ## Phase 1 — Upgrade

        - [x] 1.1 Update SDK pin
        - [x] 1.2 Bump NuGet packages
        - [x] 1.3 Verify CI build green
        """;

    // ── Per-branch fixtures ────────────────────────────────────────────────

    private const string ApiV2ImplProposal = """
        ## Why

        Implement the v2 endpoints described in the api-v2-design change.

        ## What Changes

        - Add v2 controllers for projects, pulls, issues, sessions.
        """;

    private const string ApiV2ImplSpec = """
        ## ADDED Requirements

        ### Requirement: v2 endpoints follow the response envelope

        Every v2 endpoint SHALL wrap its body in the standard envelope.

        #### Scenario: Projects list returns paginated envelope

        - **WHEN** GET /api/v2/projects is called
        - **THEN** the response SHALL include `data`, `meta.cursor`, and HTTP 200
        """;

    private const string ApiV2ImplTasks = """
        ## Phase 1 — Endpoints

        - [x] 1.1 Implement projects controller
        - [x] 1.2 Implement pulls controller
        - [ ] 1.3 Implement issues controller
        - [ ] 1.4 Implement sessions controller

        ## Phase 2 — Tests

        - [ ] 2.1 Add controller integration tests
        - [ ] 2.2 Add cursor-pagination edge-case tests
        """;

    private const string DarkModeTokensProposal = """
        ## Why

        Define dark-mode color tokens used by the new theme.
        """;

    private const string DarkModeTokensTasks = """
        ## Phase 1

        - [ ] 1.1 Define base palette
        - [ ] 1.2 Document semantic mapping
        """;

    private const string DarkModeToggleProposal = """
        ## Why

        Add a settings panel toggle so users can override the system preference.
        """;

    private const string DarkModeToggleTasks = """
        ## Phase 1

        - [ ] 1.1 Add toggle component
        - [ ] 1.2 Persist preference to localStorage
        - [ ] 1.3 Sync preference to server profile
        """;

    private const string InheritedChangeProposal = """
        ## Why

        A change that was created on a different branch and is now inherited here
        without a matching sidecar — the scanner SHALL filter it out.
        """;

    private const string InheritedChangeTasks = """
        ## Phase 1

        - [x] 1.1 Originally completed on the source branch
        """;
}
