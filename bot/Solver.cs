namespace bot;

public class Solver
{
    private StateInit _init;
    private HashSet<string> _targetIngredients;

    public BotCommand GetCommand(State state, Countdown countdown)
    {
        if (state.Customers.Count == 0)
            return new Wait();
            
        _init = state.Init;
        var targetOrder = state.Customers[0].Item;
        _targetIngredients = targetOrder.Split('-').ToHashSet();
        
        var myIngredients = state.PlayerItem == "NONE" 
            ? new HashSet<string>() 
            : state.PlayerItem.Split('-').ToHashSet();
            
        var hasDish = myIngredients.Contains("DISH");
        
        if (_targetIngredients.IsSubsetOf(myIngredients))
            return new Use(_init.WindowPos);
            
        var boardItem = state.TablesWithItems.GetValueOrDefault(_init.ChoppingBoardPos, "NONE");
        
        var needsTart = _targetIngredients.Contains("TART") && !myIngredients.Contains("TART");
        var needsCroissant = _targetIngredients.Contains("CROISSANT") && !myIngredients.Contains("CROISSANT");
        var needsStrawberries = _targetIngredients.Contains("CHOPPED_STRAWBERRIES") && !myIngredients.Contains("CHOPPED_STRAWBERRIES");
        
        var tartReady = GetTableWithItem("TART", state) != null || state.OvenContents == "TART" || IsItemInAnyValidDish("TART", state) || IsItemWithPlayerOrPartner("TART", state);
        var tartCooking = state.OvenContents == "RAW_TART";
        
        var croissantReady = GetTableWithItem("CROISSANT", state) != null || state.OvenContents == "CROISSANT" || IsItemInAnyValidDish("CROISSANT", state) || IsItemWithPlayerOrPartner("CROISSANT", state);
        var croissantCooking = state.OvenContents == "DOUGH";
        
        var choppedReady = GetTableWithItem("CHOPPED_STRAWBERRIES", state) != null || boardItem == "CHOPPED_STRAWBERRIES" || IsItemInAnyValidDish("CHOPPED_STRAWBERRIES", state) || IsItemWithPlayerOrPartner("CHOPPED_STRAWBERRIES", state);
        
        var tartInProgressByPartner = state.PartnerItem is "RAW_TART" or "CHOPPED_DOUGH";
        var croissantInProgressByPartner = state.PartnerItem == "DOUGH" && !needsTart;
        var choppedInProgressByPartner = state.PartnerItem == "STRAWBERRIES";

        if (needsTart && !tartReady && !tartCooking && !tartInProgressByPartner)
        {
            if (hasDish) return new Use(FindEmptyTable(_init, state));
            if (state.PlayerItem == "RAW_TART")
                return state.OvenContents == "NONE" ? new Use(_init.OvenPos) : new Use(FindEmptyTable(_init, state));
                
            var rawTartTable = GetTableWithItem("RAW_TART", state);
            if (rawTartTable != null && state.PlayerItem == "NONE")
                switch (state.OvenContents)
                {
                    case "NONE":
                        return new Use(rawTartTable);
                    case "CROISSANT" or "TART":
                        return new Use(_init.OvenPos);
                }
                
            var choppedDoughTable = GetTableWithItem("CHOPPED_DOUGH", state);
            if (choppedDoughTable != null)
                return state.PlayerItem switch
                {
                    "BLUEBERRIES" => new Use(choppedDoughTable),
                    "NONE" => new Use(_init.BlueberriesPos),
                    _ => new Use(FindEmptyTable(_init, state))
                };
                
            if (state.PlayerItem == "CHOPPED_DOUGH") return new Use(FindEmptyTable(_init, state));
            
            if (boardItem != "NONE" && boardItem != "DOUGH" && boardItem != "CHOPPED_DOUGH")
                return state.PlayerItem == "NONE" ? new Use(_init.ChoppingBoardPos) : new Use(FindEmptyTable(_init, state)); 
                
            if (boardItem == "CHOPPED_DOUGH")
                return state.PlayerItem == "NONE" ? new Use(_init.ChoppingBoardPos) : new Use(FindEmptyTable(_init, state));
                
            if (boardItem != "DOUGH")
                return state.PlayerItem switch
                {
                    "DOUGH" when boardItem == "NONE" => new Use(_init.ChoppingBoardPos),
                    "DOUGH" => new Use(FindEmptyTable(_init, state)),
                    "NONE" => state.OvenContents is "CROISSANT" or "TART"
                        ? new Use(_init.OvenPos)
                        : new Use(_init.CroissantPos),
                    _ => new Use(FindEmptyTable(_init, state))
                };
                
            return state.PlayerItem == "NONE" ? new Use(_init.ChoppingBoardPos) : new Use(FindEmptyTable(_init, state));
        }

        if (needsCroissant && !croissantReady && !croissantCooking && !croissantInProgressByPartner)
        {
            if (hasDish) return new Use(FindEmptyTable(_init, state));
            if (state.PlayerItem == "DOUGH")
                return state.OvenContents == "NONE" ? new Use(_init.OvenPos) : new Use(FindEmptyTable(_init, state));
                
            var doughTable = GetTableWithItem("DOUGH", state);
            if (doughTable != null && state.PlayerItem == "NONE")
                switch (state.OvenContents)
                {
                    case "NONE":
                        return new Use(doughTable);
                    case "CROISSANT":
                    case "TART":
                        return new Use(_init.OvenPos);
                }
                
            if (state.PlayerItem != "NONE") 
                return new Use(FindEmptyTable(_init, state));
                
            return state.OvenContents is "CROISSANT" or "TART" ? new Use(_init.OvenPos) : new Use(_init.CroissantPos);
        }

        if (needsStrawberries && !choppedReady && !choppedInProgressByPartner)
        {
            if (hasDish) return new Use(FindEmptyTable(_init, state));
            
            if (boardItem != "NONE" && boardItem != "STRAWBERRIES" && boardItem != "CHOPPED_STRAWBERRIES")
                return state.PlayerItem == "NONE" ? new Use(_init.ChoppingBoardPos) : new Use(FindEmptyTable(_init, state));
                
            if (boardItem == "STRAWBERRIES")
                return state.PlayerItem == "NONE" ? new Use(_init.ChoppingBoardPos) : new Use(FindEmptyTable(_init, state));
                
            if (state.PlayerItem == "STRAWBERRIES")
                return boardItem == "NONE" ? new Use(_init.ChoppingBoardPos) : new Use(FindEmptyTable(_init, state));
                
            var strawbTabl = GetTableWithItem("STRAWBERRIES", state);
            if (strawbTabl != null && state.PlayerItem == "NONE") return new Use(strawbTabl);
            
            return state.PlayerItem == "NONE" ? new Use(_init.StrawberriesPos) : new Use(FindEmptyTable(_init, state));
        }

        if (!hasDish)
        {
            foreach (var kvp in state.TablesWithItems)
            {
                if (!kvp.Value.StartsWith("DISH")) 
                    continue;
                var dishItems = kvp.Value.Split('-').ToHashSet();
                if (dishItems.IsSubsetOf(_targetIngredients)) return new Use(kvp.Key);
            }
            return state.PlayerItem == "NONE" ? new Use(_init.DishwasherPos) : new Use(FindEmptyTable(_init, state));
        }

        var missingIngredients = _targetIngredients.Except(myIngredients).ToList();
        foreach (var needed in missingIngredients)
            switch (needed)
            {
                case "ICE_CREAM":
                    return new Use(_init.IceCreamPos);
                case "BLUEBERRIES":
                    return new Use(_init.BlueberriesPos);
                case "CHOPPED_STRAWBERRIES":
                {
                    var pos = GetTableWithItem("CHOPPED_STRAWBERRIES", state);
                    if (pos != null) return new Use(pos);
                    if (boardItem == "CHOPPED_STRAWBERRIES") return new Use(_init.ChoppingBoardPos);
                    break;
                }
                case "CROISSANT":
                {
                    var pos = GetTableWithItem("CROISSANT", state);
                    if (pos != null) return new Use(pos);
                    if (state.OvenContents == "CROISSANT") return new Use(_init.OvenPos);
                    break;
                }
                case "TART":
                {
                    var pos = GetTableWithItem("TART", state);
                    if (pos != null) return new Use(pos);
                    if (state.OvenContents == "TART") return new Use(_init.OvenPos);
                    break;
                }
            }
            
        return new Wait();
    }
    
    private static V FindEmptyTable(StateInit init, State state)
    {
        V bestTable = init.DishwasherPos;
        var minScore = int.MaxValue;
        
        for (var y = 0; y < 7; y++)
        for (var x = 0; x < 11; x++)
        {
            var pos = new V(x, y);
            if (init.Map[x, y] == '#' && !state.TablesWithItems.ContainsKey(pos))
            {
                var distToMe = Abs(state.PlayerPos.X - x) + Abs(state.PlayerPos.Y - y);
                var distToPartner = Abs(state.PartnerPos.X - x) + Abs(state.PartnerPos.Y - y);
                
                var score = distToMe * 10 - distToPartner; 

                if (score < minScore)
                {
                    minScore = score;
                    bestTable = pos;
                }
            }
        }
        return bestTable;
    }

    private V GetTableWithItem(string item, State state)
    {
        foreach (var kvp in state.TablesWithItems)
            if (kvp.Value == item && kvp.Key != _init.ChoppingBoardPos) return kvp.Key;
        return null;
    }

    private bool IsItemInAnyValidDish(string item, State state)
    {
        foreach (var kvp in state.TablesWithItems)
        {
            if (!kvp.Value.StartsWith("DISH") || !kvp.Value.Contains(item)) 
                continue;
            var dishItems = kvp.Value.Split('-').ToHashSet();
            if (dishItems.IsSubsetOf(_targetIngredients)) return true;
        }
        return false;
    }
    
    private static bool IsItemWithPlayerOrPartner(string item, State state)
    {
        if (state.PlayerItem == item || state.PartnerItem == item) return true;
        if (state.PlayerItem.StartsWith("DISH") && state.PlayerItem.Contains(item)) return true;
        if (state.PartnerItem.StartsWith("DISH") && state.PartnerItem.Contains(item)) return true;
        return false;
    }
}