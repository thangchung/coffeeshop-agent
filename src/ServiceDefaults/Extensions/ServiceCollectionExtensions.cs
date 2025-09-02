using ServiceDefaults.Agents;
using ServiceDefaults.Configuration;
using ServiceDefaults.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceDefaults.Extensions;

/// <summary>
/// Extension methods for registering agent services with dependency injection.
/// Implements Dependency Inversion Principle (DIP) by providing centralized service registration.
/// Reference: SOLID Principles in C# - https://docs.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/architectural-principles#solid
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all agent-related services to the dependency injection container
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddAgentServices(this IServiceCollection services)
    {
        // Add HTTP context accessor for authentication
        services.AddHttpContextAccessor();
        
        // Configuration services
        services.AddSingleton<IAgentConfigurationService, AgentConfigurationService>();
        
        // Core agent services
        services.AddSingleton<IA2AClientManager, A2AClientManager>();
        services.AddScoped<IInputValidationService, InputValidationService>();
        services.AddScoped<IOrderParsingService, OrderParsingService>();
        services.AddScoped<IA2AResponseMapper, A2AResponseMapper>();
        services.AddScoped<IA2AMessageService, A2AMessageService>();

        return services;
    }

    /// <summary>
    /// Adds simple agent services for Barista and Kitchen services
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddSimpleAgentServices(this IServiceCollection services)
    {
        services.AddAgentServices();
        return services;
    }

    /// <summary>
    /// Adds counter agent services with all dependencies
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddCounterAgentServices(this IServiceCollection services)
    {
        services.AddAgentServices();
        return services;
    }
}