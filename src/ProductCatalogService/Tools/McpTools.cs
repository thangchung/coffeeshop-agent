using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ProductCatalogService.Shared;

namespace ProductCatalogService.Tools;

[McpServerToolType]
public sealed class McpTools(ILogger<McpTools> logger)
{
    //[McpServerTool, Description("Get item types.")]
    //public string GetItemType()
    //{
    //    logger.LogInformation("[GetItemType] is called.");
    //    return JsonSerializer.Serialize(StuffData.ItemTypeDtos, new JsonSerializerOptions
    //    {
    //        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    //        WriteIndented = true
    //    });
    //}

    //[McpServerTool, Description("Get item price based on the item type.")]
    //public string GetItemPrice(ItemType itemType)
    //{
    //    logger.LogInformation("[GetItemPrice] itemType: {itemType}.", itemType);
    //    var response = StuffData.ItemTypeDtos.FirstOrDefault(i => i.ItemType == itemType)?.Price.ToString() ?? "0.0";
    //    logger.LogInformation("[GetItemPrice] itemTypeDto: {itemTypeDto}.", response);
    //    return response;
    //}

    [McpServerTool, Description("Get list of price based on the list of item type.")]
    public string GetItemPrices(ItemType[] itemTypes)
    {
        logger.LogInformation("[GetItemPrices] itemType: {itemTypes}.", itemTypes);
        string response = """
            The list of prices are:

            """;

        foreach (var itemType in itemTypes)
        {
            logger.LogInformation("[GetItemPrices] itemType: {itemType}.", itemType);
            response += $"{itemType}: {StuffData.ItemTypeDtos.FirstOrDefault(i => i.ItemType == itemType)?.Price.ToString()}" ?? $"{itemType}: 0.0";
            response += "\r\n";
        }

        logger.LogInformation("[GetItemPrices] response: {prices}.", response);
        return response;
    }
}
