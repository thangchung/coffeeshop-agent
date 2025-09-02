using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace ProductCatalogService.Tools;

public enum ItemType
{
    // Beverages
    CAPPUCCINO,
    COFFEE_BLACK,
    COFFEE_WITH_ROOM,
    ESPRESSO,
    ESPRESSO_DOUBLE,
    LATTE,
    // Food
    CAKEPOP,
    CROISSANT,
    MUFFIN,
    CROISSANT_CHOCOLATE,
    // Others
    CHICKEN_MEATBALLS,
}

public record ItemTypeDto(ItemType ItemType, string Name, float Price);

[McpServerToolType]
public sealed class McpTools(ILogger<McpTools> logger)
{
    private List<ItemTypeDto> itemTypeDtos = Enum.GetValues<ItemType>()
            .Select(itemType => new ItemTypeDto(itemType, itemType.ToString().Replace('_', ' '), 3.5f))
            .ToList();

    [McpServerTool, Description("Get item types.")]
    public string GetItemType()
    {
        logger.LogInformation("[GetItemType] is called.");
        return JsonSerializer.Serialize(itemTypeDtos, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    [McpServerTool, Description("Get item price based on the item type.")]
    public string GetItemPrice(ItemType itemType)
    {
        logger.LogInformation("[GetItemPrice] itemType: {itemType}.", itemType);
        var response = itemTypeDtos.FirstOrDefault(i => i.ItemType == itemType)?.Price.ToString() ?? "0.0";
        logger.LogInformation("[GetItemPrice] itemTypeDto: {itemTypeDto}.", response);
        return response;
    }
}
