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

public record ItemTypeDto(ItemType ItemType, string Name);

[McpServerToolType]
public sealed class McpTools
{
    [McpServerTool, Description("Get item types.")]
    public string GetItemType()
    {
        var response = Enum.GetValues<ItemType>()
            .Select(itemType => new ItemTypeDto(itemType, itemType.ToString().Replace('_', ' ')))
            .ToList();

        return JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }
}
