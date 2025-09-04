using System;

namespace ProductCatalogService.Shared;

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

public class StuffData
{
    private static readonly float Min = 2.0f;
    private static readonly float Max = 5.0f;

    public static readonly List<ItemTypeDto> ItemTypeDtos = Enum.GetValues<ItemType>()
            .Select(itemType => new ItemTypeDto(itemType, itemType.ToString().Replace('_', ' '), (float)(new Random().NextDouble() * (Max - Min) + Min)))
            .ToList();
}
