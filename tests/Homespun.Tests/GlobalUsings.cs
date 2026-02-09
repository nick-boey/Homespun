// Global using aliases for types that moved to Homespun.Shared.
// These make the shared types available globally without needing explicit using statements.

// Sessions
global using ClaudeSession = Homespun.Shared.Models.Sessions.ClaudeSession;
global using ClaudeMessage = Homespun.Shared.Models.Sessions.ClaudeMessage;
global using ClaudeMessageRole = Homespun.Shared.Models.Sessions.ClaudeMessageRole;
global using ClaudeMessageContent = Homespun.Shared.Models.Sessions.ClaudeMessageContent;
global using ClaudeContentType = Homespun.Shared.Models.Sessions.ClaudeContentType;
global using ClaudeSessionStatus = Homespun.Shared.Models.Sessions.ClaudeSessionStatus;
global using SessionMode = Homespun.Shared.Models.Sessions.SessionMode;
global using FileChangeInfo = Homespun.Shared.Models.Sessions.FileChangeInfo;
global using FileChangeStatus = Homespun.Shared.Models.Sessions.FileChangeStatus;
global using ToolResultData = Homespun.Shared.Models.Sessions.ToolResultData;
global using ReadToolData = Homespun.Shared.Models.Sessions.ReadToolData;
global using WriteToolData = Homespun.Shared.Models.Sessions.WriteToolData;
global using BashToolData = Homespun.Shared.Models.Sessions.BashToolData;
global using AgentToolData = Homespun.Shared.Models.Sessions.AgentToolData;
global using GrepToolData = Homespun.Shared.Models.Sessions.GrepToolData;
global using GrepMatch = Homespun.Shared.Models.Sessions.GrepMatch;
global using GlobToolData = Homespun.Shared.Models.Sessions.GlobToolData;
global using WebToolData = Homespun.Shared.Models.Sessions.WebToolData;
global using GenericToolData = Homespun.Shared.Models.Sessions.GenericToolData;
global using ExitPlanModeToolData = Homespun.Shared.Models.Sessions.ExitPlanModeToolData;
global using QuestionOption = Homespun.Shared.Models.Sessions.QuestionOption;
global using UserQuestion = Homespun.Shared.Models.Sessions.UserQuestion;
global using PendingQuestion = Homespun.Shared.Models.Sessions.PendingQuestion;
global using QuestionAnswer = Homespun.Shared.Models.Sessions.QuestionAnswer;
global using ClaudeModelInfo = Homespun.Shared.Models.Sessions.ClaudeModelInfo;
global using DiscoveredSession = Homespun.Shared.Models.Sessions.DiscoveredSession;
global using SessionMetadata = Homespun.Shared.Models.Sessions.SessionMetadata;
global using AgentPrompt = Homespun.Shared.Models.Sessions.AgentPrompt;
global using SessionSummary = Homespun.Shared.Models.Sessions.SessionSummary;
global using SessionTodoItem = Homespun.Shared.Models.Sessions.SessionTodoItem;
global using TodoStatus = Homespun.Shared.Models.Sessions.TodoStatus;
global using ITodoParser = Homespun.Shared.Models.Sessions.ITodoParser;
global using TodoParser = Homespun.Shared.Models.Sessions.TodoParser;
global using ResumableSession = Homespun.Shared.Models.Sessions.ResumableSession;
global using ToolExecution = Homespun.Shared.Models.Sessions.ToolExecution;
global using ToolExecutionGroup = Homespun.Shared.Models.Sessions.ToolExecutionGroup;
global using AgentStartupState = Homespun.Shared.Models.Sessions.AgentStartupState;
global using AgentStartupStatus = Homespun.Shared.Models.Sessions.AgentStartupStatus;
global using PromptContext = Homespun.Shared.Models.Sessions.PromptContext;
global using ClaudeSessionStatusExtensions = Homespun.Shared.Models.Sessions.ClaudeSessionStatusExtensions;
global using static Homespun.Shared.Models.Sessions.ClaudeSessionStatusExtensions;

// Projects
global using Project = Homespun.Shared.Models.Projects.Project;
global using CreateProjectResult = Homespun.Shared.Models.Projects.CreateProjectResult;

// PullRequests
global using PullRequest = Homespun.Shared.Models.PullRequests.PullRequest;
global using OpenPullRequestStatus = Homespun.Shared.Models.PullRequests.OpenPullRequestStatus;
global using PullRequestStatus = Homespun.Shared.Models.PullRequests.PullRequestStatus;
global using PullRequestInfo = Homespun.Shared.Models.PullRequests.PullRequestInfo;
global using PullRequestStatusExtensions = Homespun.Shared.Models.PullRequests.PullRequestStatusExtensions;
global using PullRequestWithStatus = Homespun.Shared.Models.PullRequests.PullRequestWithStatus;
global using PullRequestWithTime = Homespun.Shared.Models.PullRequests.PullRequestWithTime;
global using PullRequestReviewInfo = Homespun.Shared.Models.PullRequests.PullRequestReviewInfo;
global using ReviewSummary = Homespun.Shared.Models.PullRequests.ReviewSummary;
global using BranchNameGenerator = Homespun.Shared.Models.PullRequests.BranchNameGenerator;

// Git
global using CloneInfo = Homespun.Shared.Models.Git.CloneInfo;
global using CloneStatus = Homespun.Shared.Models.Git.CloneStatus;
global using BranchInfo = Homespun.Shared.Models.Git.BranchInfo;
global using RebaseConflict = Homespun.Shared.Models.Git.RebaseConflict;
global using RebaseResult = Homespun.Shared.Models.Git.RebaseResult;
global using LostCloneInfo = Homespun.Shared.Models.Git.LostCloneInfo;

// GitHub
global using GitHubAuthStatus = Homespun.Shared.Models.GitHub.GitHubAuthStatus;
global using GitHubAuthMethod = Homespun.Shared.Models.GitHub.GitHubAuthMethod;
global using IssuePullRequestStatus = Homespun.Shared.Models.GitHub.IssuePullRequestStatus;
global using SyncResult = Homespun.Shared.Models.GitHub.SyncResult;
global using RemovedPrInfo = Homespun.Shared.Models.GitHub.RemovedPrInfo;

// Notifications
global using NotificationType = Homespun.Shared.Models.Notifications.NotificationType;
global using NotificationDto = Homespun.Shared.Models.Notifications.NotificationDto;

// Fleece
global using FleeceIssueSyncResult = Homespun.Shared.Models.Fleece.FleeceIssueSyncResult;
global using PullResult = Homespun.Shared.Models.Fleece.PullResult;
global using BranchStatusResult = Homespun.Shared.Models.Fleece.BranchStatusResult;

// Commands
global using CommandResult = Homespun.Shared.Models.Commands.CommandResult;

// Gitgraph
global using IGraphNode = Homespun.Shared.Models.Gitgraph.IGraphNode;
global using Graph = Homespun.Shared.Models.Gitgraph.Graph;
global using GraphBranch = Homespun.Shared.Models.Gitgraph.GraphBranch;
global using GraphNodeType = Homespun.Shared.Models.Gitgraph.GraphNodeType;
global using GraphNodeStatus = Homespun.Shared.Models.Gitgraph.GraphNodeStatus;
global using RowLaneInfo = Homespun.Shared.Models.Gitgraph.RowLaneInfo;
global using TimelineLaneLayout = Homespun.Shared.Models.Gitgraph.TimelineLaneLayout;
global using AgentStatusData = Homespun.Shared.Models.Gitgraph.AgentStatusData;
global using TimelineLaneCalculator = Homespun.Shared.Models.Gitgraph.TimelineLaneCalculator;
global using TimelineSvgRenderer = Homespun.Shared.Models.Gitgraph.TimelineSvgRenderer;

// Feature namespaces (for service interfaces and types that remain in these namespaces)
global using Homespun.Features.ClaudeCode.Data;
global using Homespun.Features.PullRequests;
global using Homespun.Features.Git;
global using Homespun.Features.GitHub;
global using Homespun.Features.Notifications;
global using Homespun.Features.Commands;
global using Homespun.Features.Gitgraph.Data;

// Requests
global using CreateSessionRequest = Homespun.Shared.Requests.CreateSessionRequest;
global using SendMessageRequest = Homespun.Shared.Requests.SendMessageRequest;
global using CreateProjectRequest = Homespun.Shared.Requests.CreateProjectRequest;
global using UpdateProjectRequest = Homespun.Shared.Requests.UpdateProjectRequest;
global using CreatePullRequestRequest = Homespun.Shared.Requests.CreatePullRequestRequest;
global using UpdatePullRequestRequest = Homespun.Shared.Requests.UpdatePullRequestRequest;
global using CreateCloneRequest = Homespun.Shared.Requests.CreateCloneRequest;
global using CreateCloneResponse = Homespun.Shared.Requests.CreateCloneResponse;
global using CloneExistsResponse = Homespun.Shared.Requests.CloneExistsResponse;
global using CreateIssueRequest = Homespun.Shared.Requests.CreateIssueRequest;
global using UpdateIssueRequest = Homespun.Shared.Requests.UpdateIssueRequest;
global using GenerateBranchIdRequest = Homespun.Shared.Requests.GenerateBranchIdRequest;
global using GenerateBranchIdResponse = Homespun.Shared.Requests.GenerateBranchIdResponse;
global using CreateNotificationRequest = Homespun.Shared.Requests.CreateNotificationRequest;
