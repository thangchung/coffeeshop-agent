using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ProductCatalogService.Shared;

namespace ProductCatalogService.Resources;

[McpServerResourceType]
public sealed class McpResources(ILogger<McpResources> logger)
{
    [McpServerResource(UriTemplate = "data://products", Name = "Products Resource")]
    [Description("Products Resource")]
    public string ProductsResource()
    {
        logger.LogInformation("[ProductsResource] Get data.");
        return JsonSerializer.Serialize(StuffData.ItemTypeDtos, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }
}
