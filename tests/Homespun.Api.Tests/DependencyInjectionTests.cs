using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Api.Tests;

[TestFixture]
public class DependencyInjectionTests
{
    private HomespunWebApplicationFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        // Force the host to build by creating a client
        _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public void AllControllerDependencies_CanBeResolved()
    {
        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();

        // Discover all registered controller types
        var controllerFeature = new ControllerFeature();
        var partManager = _factory.Services.GetRequiredService<ApplicationPartManager>();
        partManager.PopulateFeature(controllerFeature);
        var controllerTypes = controllerFeature.Controllers;

        Assert.That(controllerTypes, Is.Not.Empty, "No controllers found");

        // Resolve each controller within a scope
        using var scope = scopeFactory.CreateScope();
        var errors = new List<string>();

        foreach (var controllerType in controllerTypes)
        {
            try
            {
                ActivatorUtilities.CreateInstance(scope.ServiceProvider, controllerType);
            }
            catch (Exception ex)
            {
                errors.Add($"{controllerType.Name}: {ex.Message}");
            }
        }

        Assert.That(errors, Is.Empty,
            $"Failed to resolve {errors.Count} controller(s):\n{string.Join("\n", errors)}");
    }

    [Test]
    public void IDiffService_CanBeResolved()
    {
        using var scope = _factory.Services.CreateScope();

        var diffService = scope.ServiceProvider
            .GetRequiredService<Fleece.Core.Services.Interfaces.IDiffService>();

        Assert.That(diffService, Is.Not.Null);
    }
}
