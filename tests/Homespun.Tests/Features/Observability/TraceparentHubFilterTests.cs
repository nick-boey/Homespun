using System.Diagnostics;
using System.Reflection;
using Homespun.Features.Observability;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Tests.Features.Observability;

[TestFixture]
public class TraceparentHubFilterTests
{
    private TraceparentHubFilter _filter = null!;
    private ActivityListener _listener = null!;
    private List<Activity> _startedActivities = null!;

    private const string ActivitySourceName = "Homespun.Signalr";

    [SetUp]
    public void SetUp()
    {
        _filter = new TraceparentHubFilter();
        _startedActivities = new List<Activity>();

        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _startedActivities.Add(activity),
        };

        ActivitySource.AddActivityListener(_listener);
    }

    [TearDown]
    public void TearDown()
    {
        _listener.Dispose();
    }

    [Test]
    public async Task HappyPath_ValidTraceparent_ParentsActivityToClientContext()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var traceparent = $"00-{traceId}-{spanId}-01";

        var ctx = BuildContext(
            hubType: typeof(FakeHub),
            methodName: nameof(FakeHub.SendMessage),
            args: new object?[] { traceparent, "session-abc", "hello" });

        var result = await _filter.InvokeMethodAsync(ctx, _ => ValueTask.FromResult<object?>("ok"));

        Assert.That(result, Is.EqualTo("ok"));
        Assert.That(_startedActivities, Has.Count.EqualTo(1));
        var activity = _startedActivities[0];
        Assert.That(activity.OperationName, Is.EqualTo("SignalR.FakeHub/SendMessage"));
        Assert.That(activity.Kind, Is.EqualTo(ActivityKind.Server));
        Assert.That(activity.TraceId, Is.EqualTo(traceId));
        Assert.That(activity.ParentSpanId, Is.EqualTo(spanId));
    }

    [Test]
    public async Task SecondArgumentString_TaggedAsSessionId()
    {
        var traceparent = $"00-{ActivityTraceId.CreateRandom()}-{ActivitySpanId.CreateRandom()}-01";
        var ctx = BuildContext(
            hubType: typeof(FakeHub),
            methodName: nameof(FakeHub.SendMessage),
            args: new object?[] { traceparent, "session-xyz", "payload" });

        await _filter.InvokeMethodAsync(ctx, _ => ValueTask.FromResult<object?>(null));

        var activity = _startedActivities[0];
        Assert.That(activity.GetTagItem("homespun.session.id"), Is.EqualTo("session-xyz"));
        Assert.That(activity.GetTagItem("homespun.signalr.hub"), Is.EqualTo("FakeHub"));
        Assert.That(activity.GetTagItem("homespun.signalr.method"), Is.EqualTo("SendMessage"));
    }

    [Test]
    public async Task MissingTraceparent_StartsActivityWithoutExplicitParent()
    {
        var ctx = BuildContext(
            hubType: typeof(FakeHub),
            methodName: nameof(FakeHub.NoArgs),
            args: Array.Empty<object?>());

        await _filter.InvokeMethodAsync(ctx, _ => ValueTask.FromResult<object?>(null));

        Assert.That(_startedActivities, Has.Count.EqualTo(1));
        Assert.That(_startedActivities[0].ParentSpanId, Is.EqualTo(default(ActivitySpanId)));
    }

    [Test]
    public async Task MalformedTraceparent_FallsThroughWithoutThrowing()
    {
        var ctx = BuildContext(
            hubType: typeof(FakeHub),
            methodName: nameof(FakeHub.SendMessage),
            args: new object?[] { "not-a-valid-traceparent", "session", "msg" });

        await _filter.InvokeMethodAsync(ctx, _ => ValueTask.FromResult<object?>(null));

        Assert.That(_startedActivities, Has.Count.EqualTo(1));
        Assert.That(_startedActivities[0].ParentSpanId, Is.EqualTo(default(ActivitySpanId)));
    }

    [Test]
    public void Exception_InInvocation_PropagatesWithErrorStatusSetOnActivity()
    {
        var traceparent = $"00-{ActivityTraceId.CreateRandom()}-{ActivitySpanId.CreateRandom()}-01";
        var ctx = BuildContext(
            hubType: typeof(FakeHub),
            methodName: nameof(FakeHub.SendMessage),
            args: new object?[] { traceparent, "session", "msg" });

        var boom = new InvalidOperationException("boom");
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _filter.InvokeMethodAsync(ctx, _ => throw boom));

        Assert.That(ex, Is.SameAs(boom));
        var activity = _startedActivities[0];
        Assert.That(activity.Status, Is.EqualTo(ActivityStatusCode.Error));
        Assert.That(activity.StatusDescription, Is.EqualTo("boom"));
    }

    private static HubInvocationContext BuildContext(
        Type hubType,
        string methodName,
        object?[] args)
    {
        var callerContext = new FakeHubCallerContext();
        var serviceProvider = new FakeServiceProvider();
        var hub = (Hub)Activator.CreateInstance(hubType)!;
        var method = hubType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)!;
        return new HubInvocationContext(callerContext, serviceProvider, hub, method, args);
    }

    // Minimal concrete Hub used only for reflection + context construction. The
    // filter never calls these methods — `next` in the test injects the result.
    public class FakeHub : Hub
    {
        public Task SendMessage(string traceparent, string sessionId, string message) => Task.CompletedTask;

        public Task NoArgs() => Task.CompletedTask;
    }

    private sealed class FakeHubCallerContext : HubCallerContext
    {
        public override string ConnectionId => "test-connection";
        public override string? UserIdentifier => null;
        public override System.Security.Claims.ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
