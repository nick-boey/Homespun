using Homespun.Client.Services;

namespace Homespun.Tests.Features.Design;

[TestFixture]
public class ComponentRegistryServiceTests
{
    private ComponentRegistryService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sut = new ComponentRegistryService();
    }

    [Test]
    public void GetAllComponents_ReturnsNonEmptyList()
    {
        var result = _sut.GetAllComponents();

        Assert.That(result, Is.Not.Empty);
    }

    [Test]
    public void GetComponent_WithValidId_ReturnsMatchingComponent()
    {
        var result = _sut.GetComponent("work-item");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("work-item"));
        Assert.That(result.Name, Is.EqualTo("WorkItem"));
    }

    [Test]
    public void GetComponent_IsCaseInsensitive()
    {
        var result = _sut.GetComponent("WORK-ITEM");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("work-item"));
    }

    [Test]
    public void GetComponent_WithUnknownId_ReturnsNull()
    {
        var result = _sut.GetComponent("does-not-exist");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetComponentsByCategory_ReturnsOnlyMatchingComponents()
    {
        var result = _sut.GetComponentsByCategory("Chat");

        Assert.That(result, Is.Not.Empty);
        Assert.That(result, Has.All.Property("Category").EqualTo("Chat"));
    }

    [Test]
    public void GetComponentsByCategory_IsCaseInsensitive()
    {
        var result = _sut.GetComponentsByCategory("chat");

        Assert.That(result, Is.Not.Empty);
        Assert.That(result, Has.All.Property("Category").EqualTo("Chat"));
    }

    [Test]
    public void GetComponentsByCategory_WithUnknownCategory_ReturnsEmptyList()
    {
        var result = _sut.GetComponentsByCategory("NonExistent");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetCategories_ReturnsSortedDistinctValues()
    {
        var result = _sut.GetCategories();

        Assert.That(result, Is.Not.Empty);
        Assert.That(result, Is.Ordered);
        Assert.That(result, Is.Unique);
    }
}
