using Homespun.Client.Services;

namespace Homespun.Tests.Services;

[TestFixture]
public class PanelServiceTests
{
    private PanelService _service = null!;

    [SetUp]
    public void Setup()
    {
        _service = new PanelService();
    }

    [Test]
    public void InitialState_IsPanelAvailable_IsFalse()
    {
        Assert.That(_service.IsPanelAvailable, Is.False);
    }

    [Test]
    public void InitialState_IsPanelOpen_IsFalse()
    {
        Assert.That(_service.IsPanelOpen, Is.False);
    }

    [Test]
    public void SetPanelAvailable_True_SetsAvailable()
    {
        _service.SetPanelAvailable(true);

        Assert.That(_service.IsPanelAvailable, Is.True);
    }

    [Test]
    public void SetPanelAvailable_False_SetsNotAvailable()
    {
        _service.SetPanelAvailable(true);
        _service.SetPanelAvailable(false);

        Assert.That(_service.IsPanelAvailable, Is.False);
    }

    [Test]
    public void SetPanelAvailable_WhenOpenAndBecomesUnavailable_ClosesPanelAutomatically()
    {
        _service.SetPanelAvailable(true);
        _service.SetPanelOpen(true);

        _service.SetPanelAvailable(false);

        Assert.That(_service.IsPanelOpen, Is.False);
    }

    [Test]
    public void SetPanelOpen_WhenAvailable_OpensPanel()
    {
        _service.SetPanelAvailable(true);

        _service.SetPanelOpen(true);

        Assert.That(_service.IsPanelOpen, Is.True);
    }

    [Test]
    public void SetPanelOpen_WhenNotAvailable_DoesNotOpenPanel()
    {
        _service.SetPanelOpen(true);

        Assert.That(_service.IsPanelOpen, Is.False);
    }

    [Test]
    public void SetPanelOpen_False_ClosesPanel()
    {
        _service.SetPanelAvailable(true);
        _service.SetPanelOpen(true);

        _service.SetPanelOpen(false);

        Assert.That(_service.IsPanelOpen, Is.False);
    }

    [Test]
    public void TogglePanel_WhenAvailableAndClosed_OpensPanel()
    {
        _service.SetPanelAvailable(true);

        _service.TogglePanel();

        Assert.That(_service.IsPanelOpen, Is.True);
    }

    [Test]
    public void TogglePanel_WhenAvailableAndOpen_ClosesPanel()
    {
        _service.SetPanelAvailable(true);
        _service.SetPanelOpen(true);

        _service.TogglePanel();

        Assert.That(_service.IsPanelOpen, Is.False);
    }

    [Test]
    public void TogglePanel_WhenNotAvailable_DoesNothing()
    {
        _service.TogglePanel();

        Assert.That(_service.IsPanelOpen, Is.False);
        Assert.That(_service.IsPanelAvailable, Is.False);
    }

    [Test]
    public void OnStateChanged_RaisedWhenAvailabilityChanges()
    {
        var eventRaised = false;
        _service.OnStateChanged += () => eventRaised = true;

        _service.SetPanelAvailable(true);

        Assert.That(eventRaised, Is.True);
    }

    [Test]
    public void OnStateChanged_RaisedWhenOpenStateChanges()
    {
        _service.SetPanelAvailable(true);

        var eventRaised = false;
        _service.OnStateChanged += () => eventRaised = true;

        _service.SetPanelOpen(true);

        Assert.That(eventRaised, Is.True);
    }

    [Test]
    public void OnStateChanged_RaisedOnToggle()
    {
        _service.SetPanelAvailable(true);

        var eventCount = 0;
        _service.OnStateChanged += () => eventCount++;

        _service.TogglePanel();

        Assert.That(eventCount, Is.EqualTo(1));
    }

    [Test]
    public void OnStateChanged_NotRaisedWhenSettingSameValue()
    {
        _service.SetPanelAvailable(true);

        var eventCount = 0;
        _service.OnStateChanged += () => eventCount++;

        _service.SetPanelAvailable(true); // Same value

        Assert.That(eventCount, Is.Zero);
    }

    [Test]
    public void OnStateChanged_NotRaisedWhenTryingToOpenUnavailablePanel()
    {
        var eventCount = 0;
        _service.OnStateChanged += () => eventCount++;

        _service.SetPanelOpen(true); // Panel not available, should not raise

        Assert.That(eventCount, Is.Zero);
    }

    [Test]
    public void OnStateChanged_RaisedWhenPanelBecomeUnavailableAndWasOpen()
    {
        _service.SetPanelAvailable(true);
        _service.SetPanelOpen(true);

        var eventCount = 0;
        _service.OnStateChanged += () => eventCount++;

        _service.SetPanelAvailable(false); // Will close and change availability

        Assert.That(eventCount, Is.EqualTo(1));
    }
}
