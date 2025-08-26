using Microsoft.Extensions.Configuration;
using Moq;
using ServiceDefaults.Configuration;
using ServiceDefaults;

namespace ServiceDefaults.Tests.Configuration;

/// <summary>
/// Unit tests for AgentConfigurationService to verify configuration validation and loading
/// </summary>
public class AgentConfigurationServiceTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly AgentConfigurationService _configService;

    public AgentConfigurationServiceTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _configService = new AgentConfigurationService(_mockConfiguration.Object);
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AgentConfigurationService(null));
    }

    [Fact]
    public void GetDownstreamAgentEndpoints_WithNoConfiguration_ReturnsDefaults()
    {
        // Arrange
        _mockConfiguration.Setup(x => x[It.IsAny<string>()]).Returns((string)null);

        // Act
        var endpoints = _configService.GetDownstreamAgentEndpoints();

        // Assert
        Assert.Equal(2, endpoints.Count);
        Assert.Equal(AgentConstants.Defaults.BaristaServiceUrl, endpoints[AgentConstants.AgentTypes.Barista]);
        Assert.Equal(AgentConstants.Defaults.KitchenServiceUrl, endpoints[AgentConstants.AgentTypes.Kitchen]);
    }

    [Fact]
    public void GetDownstreamAgentEndpoints_WithCustomConfiguration_ReturnsCustomValues()
    {
        // Arrange
        var customBaristaUrl = "https://custom-barista:5001/agent";
        var customKitchenUrl = "https://custom-kitchen:5002/agent";

        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.BaristaServiceUrl])
            .Returns(customBaristaUrl);
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.KitchenServiceUrl])
            .Returns(customKitchenUrl);

        // Act
        var endpoints = _configService.GetDownstreamAgentEndpoints();

        // Assert
        Assert.Equal(customBaristaUrl, endpoints[AgentConstants.AgentTypes.Barista]);
        Assert.Equal(customKitchenUrl, endpoints[AgentConstants.AgentTypes.Kitchen]);
    }

    [Fact]
    public void GetMcpServerConfiguration_WithNoConfiguration_ReturnsDefaults()
    {
        // Arrange
        _mockConfiguration.Setup(x => x[It.IsAny<string>()]).Returns((string)null);

        // Act
        var config = _configService.GetMcpServerConfiguration();

        // Assert
        Assert.Equal(AgentConstants.Defaults.McpServerUrl, config.Url);
        Assert.Equal(AgentConstants.Defaults.McpServerClientName, config.ClientName);
    }

    [Fact]
    public void GetMcpServerConfiguration_WithCustomConfiguration_ReturnsCustomValues()
    {
        // Arrange
        var customUrl = "https://custom-mcp:5003";
        var customClientName = "CustomMcpClient";

        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.McpServerUrl])
            .Returns(customUrl);
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.McpServerClientName])
            .Returns(customClientName);

        // Act
        var config = _configService.GetMcpServerConfiguration();

        // Assert
        Assert.Equal(customUrl, config.Url);
        Assert.Equal(customClientName, config.ClientName);
    }

    [Theory]
    [InlineData("https://valid-url.com", true)]
    [InlineData("http://valid-url.com", true)]
    [InlineData("invalid-url", false)]
    [InlineData("ftp://invalid-scheme.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidateConfiguration_ValidatesUrls(string url, bool expectedValid)
    {
        // Arrange
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.BaristaServiceUrl])
            .Returns(url);
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.KitchenServiceUrl])
            .Returns("https://valid-kitchen.com");
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.McpServerUrl])
            .Returns("https://valid-mcp.com");
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.McpServerClientName])
            .Returns("ValidClient");

        // Act
        var result = _configService.ValidateConfiguration();

        // Assert
        if (expectedValid)
        {
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }
        else
        {
            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Errors);
            Assert.Contains(result.Errors, e => e.Contains("Invalid URL"));
        }
    }

    [Theory]
    [InlineData("ValidClientName", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    public void ValidateConfiguration_ValidatesClientName(string clientName, bool expectedValid)
    {
        // Arrange
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.BaristaServiceUrl])
            .Returns("https://valid-barista.com");
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.KitchenServiceUrl])
            .Returns("https://valid-kitchen.com");
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.McpServerUrl])
            .Returns("https://valid-mcp.com");
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.McpServerClientName])
            .Returns(clientName);

        // Act
        var result = _configService.ValidateConfiguration();

        // Assert
        if (expectedValid)
        {
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }
        else
        {
            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Errors);
            Assert.Contains(result.Errors, e => e.Contains("client name is required"));
        }
    }

    [Fact]
    public void ValidateConfiguration_WithMultipleErrors_ReturnsAllErrors()
    {
        // Arrange
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.BaristaServiceUrl])
            .Returns("invalid-barista-url");
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.KitchenServiceUrl])
            .Returns("invalid-kitchen-url");
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.McpServerUrl])
            .Returns("invalid-mcp-url");
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.McpServerClientName])
            .Returns("");

        // Act
        var result = _configService.ValidateConfiguration();

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(4, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Contains("Invalid URL for agent"));
        Assert.Contains(result.Errors, e => e.Contains("Invalid MCP server URL"));
        Assert.Contains(result.Errors, e => e.Contains("client name is required"));
    }

    [Fact]
    public void ValidateConfiguration_WithAllValidConfiguration_ReturnsValid()
    {
        // Arrange
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.BaristaServiceUrl])
            .Returns("https://valid-barista.com");
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.KitchenServiceUrl])
            .Returns("https://valid-kitchen.com");
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.McpServerUrl])
            .Returns("https://valid-mcp.com");
        _mockConfiguration.Setup(x => x[AgentConstants.ConfigurationKeys.McpServerClientName])
            .Returns("ValidClient");

        // Act
        var result = _configService.ValidateConfiguration();

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}