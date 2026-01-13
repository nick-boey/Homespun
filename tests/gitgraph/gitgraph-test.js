// Gitgraph Test - Example data from Homespun repository
// This mirrors the structure used by GitgraphVisualization.razor.js

const SVG_NAMESPACE = "http://www.w3.org/2000/svg";

// Example data based on real Homespun repository PRs and Issues
const exampleData = {
    mainBranchName: "main",
    branches: [
        { name: "main", color: "#6b7280" },
        { name: "git-graph", color: "#51A5C1", parentBranch: "main" },
        { name: "fix-agent-links", color: "#36A390", parentBranch: "main" },
        { name: "fix-beads-paths", color: "#E6B422", parentBranch: "main" },
        { name: "issues", color: "#a855f7", parentBranch: "main" }
    ],
    commits: [
        // Merged PRs (oldest first)
        {
            branch: "main",
            hash: "pr-35",
            subject: "#35: Link beads issues to PRs when PR is created",
            description: "Automatically links beads issues to pull requests when the PR is created, improving traceability between issues and code changes.",
            nodeType: "MergedPullRequest",
            status: "Merged",
            color: "#a855f7",
            pullRequestNumber: 35,
            issueId: null,
            mergedAt: "2026-01-07"
        },
        {
            branch: "main",
            hash: "pr-36",
            subject: "#36: Fix new issue form UI styling",
            description: "Fixes styling issues in the new issue creation form, including proper spacing and sentence case convention.",
            nodeType: "MergedPullRequest",
            status: "Merged",
            color: "#a855f7",
            pullRequestNumber: 36,
            issueId: null,
            mergedAt: "2026-01-07"
        },
        {
            branch: "main",
            hash: "pr-37",
            subject: "#37: Allow multiple agents to start consecutively",
            description: "Enables starting multiple Claude Code agents without blocking the UI, improving workflow efficiency.",
            nodeType: "MergedPullRequest",
            status: "Merged",
            color: "#a855f7",
            pullRequestNumber: 37,
            issueId: null,
            mergedAt: "2026-01-07"
        },
        {
            branch: "main",
            hash: "pr-38",
            subject: "#38: Opus 4.5 should be used as default",
            description: "Updates the default model to Claude Opus 4.5 for improved performance and capabilities.",
            nodeType: "MergedPullRequest",
            status: "Merged",
            color: "#a855f7",
            pullRequestNumber: 38,
            issueId: null,
            mergedAt: "2026-01-07"
        },
        {
            branch: "main",
            hash: "pr-39",
            subject: "#39: Simplify new issue creation with quick-create bar",
            description: "Adds a streamlined quick-create bar at the bottom of the project page for faster issue creation.",
            nodeType: "MergedPullRequest",
            status: "Merged",
            color: "#a855f7",
            pullRequestNumber: 39,
            issueId: null,
            mergedAt: "2026-01-08"
        },
        {
            branch: "main",
            hash: "pr-40",
            subject: "#40: Set up Homespun hosting infrastructure",
            description: "Initial infrastructure setup for hosting Homespun, including server configuration and deployment scripts.",
            nodeType: "MergedPullRequest",
            status: "Merged",
            color: "#a855f7",
            pullRequestNumber: 40,
            issueId: null,
            mergedAt: "2026-01-08"
        },
        {
            branch: "main",
            hash: "pr-41",
            subject: "#41: Simplify hosting to VM/container with Watchtower",
            description: "Simplifies the hosting setup to use Docker containers on a VM with Watchtower for automatic updates.",
            nodeType: "MergedPullRequest",
            status: "Merged",
            color: "#a855f7",
            pullRequestNumber: 41,
            issueId: null,
            mergedAt: "2026-01-12"
        },
        // Open PRs
        {
            branch: "fix-beads-paths",
            hash: "pr-42",
            subject: "#42: Fix beads path mismatch between container and host",
            description: "Resolves path mismatch issues when running beads commands in containerized environments where host and container paths differ.",
            nodeType: "OpenPullRequest",
            status: "Open",
            color: "#51A5C1",
            pullRequestNumber: 42,
            issueId: null,
            createdAt: "2026-01-12"
        },
        {
            branch: "fix-agent-links",
            hash: "pr-43",
            subject: "#43: Fix agent web UI links for containerized builds",
            description: "Fixes the agent web UI links to work correctly when running in containerized environments.",
            nodeType: "OpenPullRequest",
            status: "Open",
            color: "#51A5C1",
            pullRequestNumber: 43,
            issueId: null,
            createdAt: "2026-01-12"
        },
        {
            branch: "git-graph",
            hash: "pr-44",
            subject: "#44: Add Git graph visualization for PRs and Issues",
            description: "Implements a Git-like DAG visualization for displaying pull requests and issues, replacing the old timeline view.",
            nodeType: "OpenPullRequest",
            status: "Open",
            color: "#51A5C1",
            pullRequestNumber: 44,
            issueId: null,
            createdAt: "2026-01-12"
        },
        // Open Issues - using sequential numbers for hash to avoid GitgraphJS rendering issues with long hashes
        {
            branch: "main",
            hash: "100",
            subject: "[bug] When left pane is open on small screens allow clicking to close",
            description: "On mobile/small screens, when the left navigation pane is open, clicking on the main content area should close the pane for better UX.",
            nodeType: "Issue",
            status: "Open",
            color: "#6c757d",
            pullRequestNumber: null,
            issueId: "hsp-pdi",
            priority: "P2",
            type: "bug"
        },
        {
            branch: "main",
            hash: "101",
            subject: "[task] Run bd sync on cloned repositories",
            description: "Automatically run beads sync when a repository is cloned to ensure issues are up to date.",
            nodeType: "Issue",
            status: "Open",
            color: "#6c757d",
            pullRequestNumber: null,
            issueId: "hsp-yl2",
            priority: "P2",
            type: "task"
        },
        {
            branch: "main",
            hash: "102",
            subject: "[feature] Allow beads issues to be edited, closed and deleted",
            description: "Add UI controls in the timeline to edit, close, and delete beads issues directly from the project page.",
            nodeType: "Issue",
            status: "Open",
            color: "#6c757d",
            pullRequestNumber: null,
            issueId: "hsp-d30",
            priority: "P2",
            type: "feature"
        },
        {
            branch: "main",
            hash: "103",
            subject: "[feature] Allow agents to be resumed",
            description: "Enable resuming Claude Code agent sessions that were previously paused or interrupted.",
            nodeType: "Issue",
            status: "Open",
            color: "#6c757d",
            pullRequestNumber: null,
            issueId: "hsp-l33",
            priority: "P2",
            type: "feature"
        },
        {
            branch: "main",
            hash: "104",
            subject: "[feature] Show agent status",
            description: "Display real-time status indicators for running agents including progress and current activity.",
            nodeType: "Issue",
            status: "Open",
            color: "#6c757d",
            pullRequestNumber: null,
            issueId: "hsp-ijv",
            priority: "P2",
            type: "feature"
        },
        {
            branch: "main",
            hash: "105",
            subject: "[feature] Placeholders for past PRs",
            description: "Show placeholder entries for past/merged PRs in the timeline to provide historical context.",
            nodeType: "Issue",
            status: "Open",
            color: "#6c757d",
            pullRequestNumber: null,
            issueId: "hsp-dai",
            priority: "P2",
            type: "feature"
        },
        {
            branch: "main",
            hash: "106",
            subject: "[task] Fix Beads setup for new repositories",
            description: "Improve the beads initialization process for newly added repositories to handle edge cases.",
            nodeType: "Issue",
            status: "Open",
            color: "#6c757d",
            pullRequestNumber: null,
            issueId: "hsp-5wg",
            priority: "P2",
            type: "task"
        }
    ]
};

// Tooltip element
let tooltip = null;

function createTooltip() {
    tooltip = document.createElement('div');
    tooltip.className = 'graph-tooltip';
    tooltip.style.cssText = `
        position: fixed;
        background: var(--bg-primary);
        border: 1px solid var(--text-muted);
        border-radius: 8px;
        padding: 12px;
        max-width: 350px;
        box-shadow: 0 4px 12px rgba(0,0,0,0.3);
        z-index: 1000;
        pointer-events: none;
        opacity: 0;
        transition: opacity 0.15s ease;
        font-size: 13px;
        line-height: 1.4;
    `;
    document.body.appendChild(tooltip);
}

function showTooltip(data, event) {
    if (!tooltip) createTooltip();

    let content = '';

    if (data.nodeType.includes('PullRequest')) {
        content = `
            <div style="font-weight: 600; margin-bottom: 8px; color: var(--text-primary);">
                PR #${data.pullRequestNumber}: ${data.subject.replace(/^#\d+:\s*/, '')}
            </div>
            <div style="color: var(--text-secondary); margin-bottom: 6px;">
                <span style="display: inline-block; padding: 2px 8px; border-radius: 4px; background: ${data.color}; color: white; font-size: 11px; margin-right: 8px;">
                    ${data.status}
                </span>
                Branch: <code style="background: var(--bg-secondary); padding: 2px 4px; border-radius: 3px;">${data.branch}</code>
            </div>
            ${data.mergedAt ? `<div style="color: var(--text-muted); font-size: 12px; margin-bottom: 6px;">Merged: ${data.mergedAt}</div>` : ''}
            ${data.createdAt ? `<div style="color: var(--text-muted); font-size: 12px; margin-bottom: 6px;">Created: ${data.createdAt}</div>` : ''}
            <div style="color: var(--text-secondary);">${data.description || 'No description'}</div>
        `;
    } else {
        content = `
            <div style="font-weight: 600; margin-bottom: 8px; color: var(--text-primary);">
                ${data.issueId}: ${data.subject.replace(/^\[.*?\]\s*/, '')}
            </div>
            <div style="color: var(--text-secondary); margin-bottom: 6px;">
                <span style="display: inline-block; padding: 2px 8px; border-radius: 4px; background: ${data.color}; color: white; font-size: 11px; margin-right: 8px;">
                    ${data.type || 'issue'}
                </span>
                <span style="display: inline-block; padding: 2px 8px; border-radius: 4px; background: var(--bg-secondary); color: var(--text-secondary); font-size: 11px;">
                    ${data.priority || 'P2'}
                </span>
            </div>
            <div style="color: var(--text-secondary);">${data.description || 'No description'}</div>
        `;
    }

    tooltip.innerHTML = content;
    tooltip.style.opacity = '1';

    // Position tooltip near cursor
    const x = event.clientX + 15;
    const y = event.clientY + 15;

    // Adjust if tooltip would go off screen
    const rect = tooltip.getBoundingClientRect();
    const maxX = window.innerWidth - 360;
    const maxY = window.innerHeight - rect.height - 20;

    tooltip.style.left = Math.min(x, maxX) + 'px';
    tooltip.style.top = Math.min(y, maxY) + 'px';
}

function hideTooltip() {
    if (tooltip) {
        tooltip.style.opacity = '0';
    }
}


// Handle node click
function handleNodeClick(nodeType, nodeId, prNumber, issueId) {
    const output = document.getElementById('click-output');
    if (nodeType.includes('PullRequest')) {
        output.textContent = `PR #${prNumber} clicked (ID: ${nodeId})`;
    } else {
        output.textContent = `Issue ${issueId} clicked (ID: ${nodeId})`;
    }
}

// Create a diamond SVG path for issues - centered at (size, size) to match circle
function createDiamondPath(size, color) {
    const path = document.createElementNS(SVG_NAMESPACE, 'path');
    // Diamond centered at (size, size) - same center point as circle
    const d = `M ${size} 0 L ${size * 2} ${size} L ${size} ${size * 2} L 0 ${size} Z`;
    path.setAttribute('d', d);
    path.setAttribute('fill', color);
    return path;
}

// Create custom render function for issue nodes (diamond shape)
function createIssueRenderDot(commit, data) {
    const size = commit.style.dot.size;
    const color = data.color || commit.style.dot.color || '#6b7280';

    const g = document.createElementNS(SVG_NAMESPACE, 'g');
    g.classList.add('node-issue');
    g.setAttribute('data-node-id', data.hash);

    const diamond = createDiamondPath(size, color);
    g.appendChild(diamond);

    g.style.cursor = 'pointer';
    g.addEventListener('click', () => {
        handleNodeClick(data.nodeType, data.hash, data.pullRequestNumber, data.issueId);
    });

    // Tooltip on hover
    g.addEventListener('mouseenter', (e) => showTooltip(data, e));
    g.addEventListener('mousemove', (e) => showTooltip(data, e));
    g.addEventListener('mouseleave', hideTooltip);

    return g;
}

// Create custom render function for PR nodes (circle with click handler)
function createPRRenderDot(commit, data) {
    const size = commit.style.dot.size;
    const color = data.color || commit.style.dot.color || '#6b7280';

    const g = document.createElementNS(SVG_NAMESPACE, 'g');
    g.classList.add('node-pr');
    g.setAttribute('data-node-id', data.hash);

    const circle = document.createElementNS(SVG_NAMESPACE, 'circle');
    circle.setAttribute('cx', size.toString());
    circle.setAttribute('cy', size.toString());
    circle.setAttribute('r', size.toString());
    circle.setAttribute('fill', color);
    g.appendChild(circle);

    g.style.cursor = 'pointer';
    g.addEventListener('click', () => {
        handleNodeClick(data.nodeType, data.hash, data.pullRequestNumber, data.issueId);
    });

    // Tooltip on hover
    g.addEventListener('mouseenter', (e) => showTooltip(data, e));
    g.addEventListener('mousemove', (e) => showTooltip(data, e));
    g.addEventListener('mouseleave', hideTooltip);

    return g;
}

// Get theme colors from CSS custom properties
function getThemeColors() {
    const style = getComputedStyle(document.documentElement);
    return {
        main: style.getPropertyValue('--color-basalt').trim() || '#6b7280',
        branch1: style.getPropertyValue('--color-ocean').trim() || '#51A5C1',
        branch2: style.getPropertyValue('--color-lagoon').trim() || '#36A390',
        branch3: style.getPropertyValue('--color-wattle').trim() || '#E6B422',
        branch4: style.getPropertyValue('--status-merged').trim() || '#a855f7',
    };
}

// Initialize the graph
function initializeGraph() {
    const container = document.getElementById('gitgraph');
    const themeColors = getThemeColors();

    // Create custom template
    const template = GitgraphJS.templateExtend(GitgraphJS.TemplateName.Metro, {
        colors: [themeColors.main, themeColors.branch1, themeColors.branch2, themeColors.branch3, themeColors.branch4],
        branch: {
            lineWidth: 1.5,
            spacing: 15,
            label: {
                display: true,
                font: '12px sans-serif'
            }
        },
        commit: {
            spacing: 25,
            dot: {
                size: 5,
                strokeWidth: 0
            },
            message: {
                displayAuthor: false,
                displayHash: false,
                font: '14px sans-serif'
            }
        }
    });

    // Create the graph
    const gitgraph = GitgraphJS.createGitgraph(container, {
        template,
        orientation: GitgraphJS.Orientation.VerticalReverse
    });

    // Track created branches
    const branches = new Map();

    // Create main branch first
    const mainBranch = gitgraph.branch(exampleData.mainBranchName);
    branches.set(exampleData.mainBranchName, mainBranch);

    // Build branch color lookup
    const branchColors = {};
    for (const branchData of exampleData.branches) {
        branchColors[branchData.name] = branchData.color;
    }

    // Helper to create commit options
    const textColor = getComputedStyle(document.documentElement).getPropertyValue('--text-primary').trim() || '#000';

    function createCommitOptions(commitData) {
        const isIssue = commitData.nodeType.includes('Issue');
        return {
            subject: commitData.subject,
            hash: commitData.hash,
            style: {
                dot: {
                    color: commitData.color || undefined
                }
            },
            renderDot: isIssue
                ? (commit) => createIssueRenderDot(commit, commitData)
                : (commit) => createPRRenderDot(commit, commitData),
            renderMessage: (commit) => {
                const text = document.createElementNS(SVG_NAMESPACE, 'text');
                text.setAttribute('dominant-baseline', 'middle');
                text.setAttribute('dy', '0.35em');  // Fine-tune vertical alignment
                text.setAttribute('fill', textColor);
                text.style.cursor = 'pointer';
                text.textContent = commit.subject;

                text.addEventListener('click', () => {
                    handleNodeClick(commitData.nodeType, commitData.hash, commitData.pullRequestNumber, commitData.issueId);
                });

                // Tooltip on hover
                text.addEventListener('mouseenter', (e) => showTooltip(commitData, e));
                text.addEventListener('mousemove', (e) => showTooltip(commitData, e));
                text.addEventListener('mouseleave', hideTooltip);

                return text;
            }
        };
    }

    // Process commits - create branches lazily when first needed
    // This ensures branches are created at the right point in the graph
    for (const commitData of exampleData.commits) {
        let branch = branches.get(commitData.branch);

        // If branch doesn't exist yet, create it from main (at current HEAD)
        if (!branch) {
            const color = branchColors[commitData.branch];
            branch = mainBranch.branch({
                name: commitData.branch,
                style: color ? { color: color } : undefined
            });
            branches.set(commitData.branch, branch);
        }

        branch.commit(createCommitOptions(commitData));
    }

    console.log('Gitgraph initialized with', exampleData.commits.length, 'commits');
}

// Initialize when DOM is ready
document.addEventListener('DOMContentLoaded', initializeGraph);
