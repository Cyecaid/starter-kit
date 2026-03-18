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
    public V CroissantPos = V.None;
    public V OvenPos = V.None;
}

public class State
{
    public StateInit Init { get; set; }
    
    public int TurnsRemaining;
    
    public V PlayerPos = V.None;
    public string PlayerItem = "";
    
    public V PartnerPos = V.None;
    public string PartnerItem = "";
    
    public Dictionary<V, string> TablesWithItems = new();
    
    public string OvenContents = "";
    public int OvenTimer;
    
    public List<(string Item, int Award)> Customers = new();
}