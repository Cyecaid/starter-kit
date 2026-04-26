namespace bot;

public class StateInit
{
    public int NumAllCustomers;
    public char[,] Map = new char[11, 7];
    
    public V DishwasherPos = V.None;
    public V WindowPos = V.None;
    public V BlueberriesPos = V.None;
    public V IceCreamPos = V.None;
    public V StrawberriesPos = V.None;
    public V ChoppingBoardPos = V.None;
    public V DoughPos = V.None;
    public V OvenPos = V.None;
    
    public List<V> EmptyTables = new(); // Для быстрого поиска куда положить
}

public class State
{
    public StateInit Init { get; set; }
    
    public int TurnsRemaining;
    public double Score; // Реальные очки
    
    public V PlayerPos = V.None;
    public ItemMask PlayerItem = ItemMask.None;
    
    public V PartnerPos = V.None;
    public ItemMask PartnerItem = ItemMask.None;
    
    public Dictionary<V, ItemMask> TablesWithItems = new();
    
    public ItemMask OvenContents = ItemMask.None;
    public int OvenTimer;
    
    public List<(ItemMask Item, int Award)> Customers = new();

    public State Clone()
    {
        var clone = new State
        {
            Init = Init,
            TurnsRemaining = TurnsRemaining,
            Score = Score,
            PlayerPos = PlayerPos,
            PlayerItem = PlayerItem,
            PartnerPos = PartnerPos,
            PartnerItem = PartnerItem,
            OvenContents = OvenContents,
            OvenTimer = OvenTimer,
            Customers = Customers.ToList() 
        };
        
        foreach (var kvp in TablesWithItems)
            clone.TablesWithItems[kvp.Key] = kvp.Value;
            
        return clone;
    }
}