namespace bot;

[Flags]
public enum ItemMask : uint
{
    None = 0,
    Dish = 1 << 0,
    IceCream = 1 << 1,
    Blueberries = 1 << 2,
    Strawberries = 1 << 3,
    ChoppedStrawberries = 1 << 4,
    Croissant = 1 << 5,
    Dough = 1 << 6,
    BlueberryTart = 1 << 7,
    ChoppedDough = 1 << 8,
    RawTart = 1 << 9,
}

public static class ItemExtensions
{
    public static ItemMask ParseItem(string itemStr)
    {
        if (string.IsNullOrEmpty(itemStr) || itemStr == "NONE") return ItemMask.None;
        
        var mask = ItemMask.None;
        var parts = itemStr.Split('-');
        foreach (var p in parts)
        {
            switch (p)
            {
                case "DISH": mask |= ItemMask.Dish; break;
                case "ICE_CREAM": mask |= ItemMask.IceCream; break;
                case "BLUEBERRIES": mask |= ItemMask.Blueberries; break;
                case "STRAWBERRIES": mask |= ItemMask.Strawberries; break;
                case "CHOPPED_STRAWBERRIES": mask |= ItemMask.ChoppedStrawberries; break;
                case "DOUGH": mask |= ItemMask.Dough; break;
                case "CHOPPED_DOUGH": mask |= ItemMask.ChoppedDough; break;
                case "RAW_TART": mask |= ItemMask.RawTart; break;
                case "CROISSANT": mask |= ItemMask.Croissant; break;
                case "TART":
                case "BLUEBERRIES_TART": mask |= ItemMask.BlueberryTart; break;
            }
        }
        return mask;
    }

    public static bool CanPutOnDish(this ItemMask item)
    {
        return item != ItemMask.Dough && 
               item != ItemMask.Strawberries && 
               item != ItemMask.ChoppedDough && 
               item != ItemMask.RawTart;
    }

    public static bool IsWarmable(this ItemMask item)
    {
        return item is ItemMask.Dough or ItemMask.RawTart;
    }
}