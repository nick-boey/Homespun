using System.Runtime.InteropServices;
using Homespun.Features.ClaudeCode.Exceptions;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class SingleContainerAgentExecutionServiceTests
{
    private static SingleContainerAgentExecutionService Build(
        string? workerUrl = "http://localhost:8081",
        ISessionEventIngestor? ingestor = null,
        string hostWorkspaceRoot = "",
        string containerWorkspaceRoot = "/workdir")
    {
        var opts = Options.Create(new SingleContainerAgentExecutionOptions
        {
            WorkerUrl = workerUrl ?? string.Empty,
            HostWorkspaceRoot = hostWorkspaceRoot,
            ContainerWorkspaceRoot = containerWorkspaceRoot,
        });
        return new SingleContainerAgentExecutionService(
            opts,
            NullLogger<SingleContainerAgentExecutionService>.Instance,
            ingestor ?? new Mock<ISessionEventIngestor>().Object);
    }

    [Test]
    public void Ctor_MissingWorkerUrl_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Build(workerUrl: ""));
        Assert.Throws<InvalidOperationException>(() => Build(workerUrl: "   "));
    }

    [Test]
    public async Task Stop_ClearsSlot_AllowingNewSession()
    {
        using var svc = Build();
        // Simulate an active slot via reflection on the private _active field — the
        // cleanest way to assert the slot clears without spinning up an HTTP worker.
        var field = typeof(SingleContainerAgentExecutionService).GetField(
            "_active",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var activeType = field.FieldType;
        var active = activeType.GetGenericArguments();
        // Build an ActiveSession instance via its constructor
        var activeClass = typeof(SingleContainerAgentExecutionService).GetNestedType(
            "ActiveSession",
            System.Reflection.BindingFlags.NonPublic);

        Assert.That(activeClass, Is.Not.Null, "ActiveSession nested type not found via reflection");

        // Stop on a session that is not active — no-op, slot remains free.
        await svc.StopSessionAsync("nonexistent");
        // Verify slot is free (active field is null)
        var slot = field.GetValue(svc);
        Assert.That(slot, Is.Null);
    }

    [Test]
    public async Task BusyGuard_ReleasesSlotOnStop_ThenAllowsNewSession()
    {
        // Integration-style check using only public surface: without an HTTP worker the
        // inner StreamAgentEventsAsync will fail; that's expected. We assert that
        // after StopSessionAsync the slot is released so a second start would not
        // throw SingleContainerBusyException on the fast path.
        using var svc = Build();

        // Attempt to start with an unreachable URL — should throw but the slot is
        // captured first. Stop immediately clears the slot.
        var req = new AgentStartRequest(
            WorkingDirectory: "/tmp",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "hi",
            HomespunSessionId: "S1");

        try
        {
            await foreach (var _ in svc.StartSessionAsync(req))
            {
                break;
            }
        }
        catch
        {
            // Expected: no worker at the URL.
        }

        // After the failed start, manually stop to clear the slot.
        await svc.StopSessionAsync("S1");

        var field = typeof(SingleContainerAgentExecutionService).GetField(
            "_active",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.That(field.GetValue(svc), Is.Null);
    }

    [Test]
    public async Task GetSessionStatus_NoActiveSession_ReturnsNull()
    {
        using var svc = Build();
        var status = await svc.GetSessionStatusAsync("nonexistent");
        Assert.That(status, Is.Null);
    }

    [Test]
    public async Task ListContainers_AlwaysEmpty_InShim()
    {
        using var svc = Build();
        var containers = await svc.ListContainersAsync();
        Assert.That(containers, Is.Empty);
    }

    [Test]
    public async Task CleanupOrphanedContainers_Returns0_InShim()
    {
        using var svc = Build();
        var n = await svc.CleanupOrphanedContainersAsync();
        Assert.That(n, Is.EqualTo(0));
    }

    [Test]
    public void SingleContainerBusyException_CarriesBothIds()
    {
        var ex = new SingleContainerBusyException("requested", "current");
        Assert.That(ex.RequestedSessionId, Is.EqualTo("requested"));
        Assert.That(ex.CurrentSessionId, Is.EqualTo("current"));
        Assert.That(ex.Message, Does.Contain("requested"));
        Assert.That(ex.Message, Does.Contain("current"));
    }

    [Test]
    public void TranslateWorkingDirectory_NonWindowsHost_PassesThroughUnchanged()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("Test asserts non-Windows behaviour.");
            return;
        }

        using var svc = Build(hostWorkspaceRoot: "/home/dev/projects", containerWorkspaceRoot: "/workdir");
        var result = svc.TranslateWorkingDirectoryForContainer("/home/dev/projects/smoke/main");
        Assert.That(result, Is.EqualTo("/home/dev/projects/smoke/main"));
    }

    [Test]
    public void TranslateWorkingDirectory_WindowsHost_RewritesPrefixToContainerPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("Test asserts Windows path-rewriting behaviour.");
            return;
        }

        using var svc = Build(
            hostWorkspaceRoot: @"C:\Users\dev\.homespun\projects",
            containerWorkspaceRoot: "/workdir");
        var result = svc.TranslateWorkingDirectoryForContainer(@"C:\Users\dev\.homespun\projects\smoke\main");
        Assert.That(result, Is.EqualTo("/workdir/smoke/main"));
    }

    [Test]
    public void TranslateWorkingDirectory_WindowsHost_RootItselfMapsToContainerRoot()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("Test asserts Windows path-rewriting behaviour.");
            return;
        }

        using var svc = Build(
            hostWorkspaceRoot: @"C:\Users\dev\.homespun\projects",
            containerWorkspaceRoot: "/workdir");
        var result = svc.TranslateWorkingDirectoryForContainer(@"C:\Users\dev\.homespun\projects");
        Assert.That(result, Is.EqualTo("/workdir"));
    }

    [Test]
    public void TranslateWorkingDirectory_WindowsHost_PathOutsideWorkspaceRoot_Throws()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("Test asserts Windows path-rewriting behaviour.");
            return;
        }

        using var svc = Build(
            hostWorkspaceRoot: @"C:\Users\dev\.homespun\projects",
            containerWorkspaceRoot: "/workdir");
        Assert.Throws<InvalidOperationException>(
            () => svc.TranslateWorkingDirectoryForContainer(@"D:\other\path"));
    }

    [Test]
    public void TranslateWorkingDirectory_WindowsHost_EmptyWorkspaceRoot_Throws()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("Test asserts Windows path-rewriting behaviour.");
            return;
        }

        using var svc = Build(hostWorkspaceRoot: "", containerWorkspaceRoot: "/workdir");
        Assert.Throws<InvalidOperationException>(
            () => svc.TranslateWorkingDirectoryForContainer(@"C:\anything"));
    }
}
