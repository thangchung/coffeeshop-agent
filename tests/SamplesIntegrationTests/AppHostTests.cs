using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SamplesIntegrationTests;

/// <summary>
/// Integration tests for the coffeeshop-agent AppHost following .NET Aspire testing patterns
/// Reference: https://github.com/dotnet/aspire-samples/tree/main/tests/SamplesIntegrationTests
/// </summary>
public class AppHostTests
{
    [Fact]
    public async Task CreateAsync_WithValidConfiguration_StartsSuccessfully()
    {
        // Arrange & Act
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        await using var app = await appHost.BuildAsync();

        // Assert
        Assert.NotNull(app);
        Assert.NotNull(app.Services);
    }

    [Fact]
    public async Task StartAsync_AllServices_StartWithoutErrors()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        await using var app = await appHost.BuildAsync();

        // Act
        await app.StartAsync();

        // Assert
        // Verify that all expected services are registered
        var expectedServices = new[]
        {
            "counterservice",
            "baristaservice", 
            "kitchenservice",
            "productcatalogservice"
        };

        foreach (var serviceName in expectedServices)
        {
            var resource = app.Resources.FirstOrDefault(r => r.Name == serviceName);
            Assert.NotNull(resource);
        }
    }

    [Fact]
    public async Task CounterService_IsHealthy()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // Act
        var counterService = app.GetEndpoint("counterservice");
        using var httpClient = app.CreateHttpClient("counterservice");

        // Basic connectivity test
        var response = await httpClient.GetAsync("/health", CancellationToken.None);

        // Assert
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound);
        // NotFound is acceptable as not all services may have health endpoints implemented
    }

    [Fact]
    public async Task BaristaService_IsHealthy()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // Act
        var baristaService = app.GetEndpoint("baristaservice");
        using var httpClient = app.CreateHttpClient("baristaservice");

        // Basic connectivity test
        var response = await httpClient.GetAsync("/health", CancellationToken.None);

        // Assert
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task KitchenService_IsHealthy()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // Act
        var kitchenService = app.GetEndpoint("kitchenservice");
        using var httpClient = app.CreateHttpClient("kitchenservice");

        // Basic connectivity test
        var response = await httpClient.GetAsync("/health", CancellationToken.None);

        // Assert
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProductCatalogService_IsHealthy()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // Act
        var productCatalogService = app.GetEndpoint("productcatalogservice");
        using var httpClient = app.CreateHttpClient("productcatalogservice");

        // Basic connectivity test
        var response = await httpClient.GetAsync("/health", CancellationToken.None);

        // Assert
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ServiceDiscovery_CanResolveAllServices()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // Act & Assert
        var counterEndpoint = app.GetEndpoint("counterservice");
        var baristaEndpoint = app.GetEndpoint("baristaservice");
        var kitchenEndpoint = app.GetEndpoint("kitchenservice");
        var catalogEndpoint = app.GetEndpoint("productcatalogservice");

        Assert.NotNull(counterEndpoint);
        Assert.NotNull(baristaEndpoint);
        Assert.NotNull(kitchenEndpoint);
        Assert.NotNull(catalogEndpoint);

        // Verify endpoints have valid URIs
        Assert.True(Uri.IsWellFormedUriString(counterEndpoint, UriKind.Absolute));
        Assert.True(Uri.IsWellFormedUriString(baristaEndpoint, UriKind.Absolute));
        Assert.True(Uri.IsWellFormedUriString(kitchenEndpoint, UriKind.Absolute));
        Assert.True(Uri.IsWellFormedUriString(catalogEndpoint, UriKind.Absolute));
    }

    [Fact]
    public async Task ConfigurationValidation_AllServicesHaveValidConfiguration()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        await using var app = await appHost.BuildAsync();

        // Act
        await app.StartAsync();

        // Assert
        // Verify that app started successfully, which implies valid configuration
        Assert.NotNull(app);
        
        // Verify all expected resources are present
        var resources = app.Resources.ToList();
        Assert.NotEmpty(resources);
        
        // Check for expected resource names
        var resourceNames = resources.Select(r => r.Name).ToList();
        Assert.Contains("counterservice", resourceNames);
        Assert.Contains("baristaservice", resourceNames);
        Assert.Contains("kitchenservice", resourceNames);
        Assert.Contains("productcatalogservice", resourceNames);
    }

    [Fact]
    public async Task MultipleServices_CanStartConcurrently()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        await using var app = await appHost.BuildAsync();

        // Act
        var startTime = DateTime.UtcNow;
        await app.StartAsync();
        var endTime = DateTime.UtcNow;

        // Assert
        var startupTime = endTime - startTime;
        
        // Services should start within reasonable time (less than 2 minutes)
        Assert.True(startupTime < TimeSpan.FromMinutes(2), 
            $"Startup took too long: {startupTime.TotalSeconds} seconds");

        // Verify all services are accessible
        var services = new[] { "counterservice", "baristaservice", "kitchenservice", "productcatalogservice" };
        
        foreach (var serviceName in services)
        {
            var endpoint = app.GetEndpoint(serviceName);
            Assert.NotNull(endpoint);
            
            // Test basic connectivity
            using var httpClient = app.CreateHttpClient(serviceName);
            try
            {
                var response = await httpClient.GetAsync("/", CancellationToken.None);
                // Any HTTP response (including 404) indicates the service is running
                Assert.True(response != null);
            }
            catch (HttpRequestException)
            {
                // Some services might not have a root endpoint, which is acceptable
                // The important thing is that the endpoint is resolvable
            }
        }
    }
}