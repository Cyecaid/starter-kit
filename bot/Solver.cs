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
        
        var bestCustomer = state.Customers
            .OrderByDescending(c => EvaluateOrder(c, state))
            .First();

        var targetOrder = bestCustomer.Item;
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
        
        var tartReady = GetTableWithItem("TART", state) != null || state.OvenContents == "TART" || IsItemInAnyValidDish("TART", state, _targetIngredients) || IsItemWithPlayerOrPartner("TART", state, _targetIngredients);
        var tartCooking = state.OvenContents == "RAW_TART";
        
        var croissantReady = GetTableWithItem("CROISSANT", state) != null || state.OvenContents == "CROISSANT" || IsItemInAnyValidDish("CROISSANT", state, _targetIngredients) || IsItemWithPlayerOrPartner("CROISSANT", state, _targetIngredients);
        var croissantCooking = state.OvenContents == "DOUGH";
        
        var choppedReady = GetTableWithItem("CHOPPED_STRAWBERRIES", state) != null || boardItem == "CHOPPED_STRAWBERRIES" || IsItemInAnyValidDish("CHOPPED_STRAWBERRIES", state, _targetIngredients) || IsItemWithPlayerOrPartner("CHOPPED_STRAWBERRIES", state, _targetIngredients);
        
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
                    case "NONE": return new Use(rawTartTable);
                    case "CROISSANT" or "TART": return new Use(_init.OvenPos);
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
                    "NONE" => state.OvenContents is "CROISSANT" or "TART" ? new Use(_init.OvenPos) : new Use(_init.CroissantPos),
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
                    case "NONE": return new Use(doughTable);
                    case "CROISSANT" or "TART": return new Use(_init.OvenPos);
                }
                
            if (state.PlayerItem != "NONE") return new Use(FindEmptyTable(_init, state));
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
                if (!kvp.Value.StartsWith("DISH")) continue;
                var dishItems = kvp.Value.Split('-').ToHashSet();
                if (dishItems.IsSubsetOf(_targetIngredients)) return new Use(kvp.Key);
            }
            return state.PlayerItem == "NONE" ? new Use(_init.DishwasherPos) : new Use(FindEmptyTable(_init, state));
        }

        var missingIngredients = _targetIngredients.Except(myIngredients).ToList();
        foreach (var needed in missingIngredients)
            switch (needed)
            {
                case "ICE_CREAM": return new Use(_init.IceCreamPos);
                case "BLUEBERRIES": return new Use(_init.BlueberriesPos);
                case "CHOPPED_STRAWBERRIES":
                    var sPos = GetTableWithItem("CHOPPED_STRAWBERRIES", state);
                    if (sPos != null) return new Use(sPos);
                    if (boardItem == "CHOPPED_STRAWBERRIES") return new Use(_init.ChoppingBoardPos);
                    break;
                case "CROISSANT":
                    var cPos = GetTableWithItem("CROISSANT", state);
                    if (cPos != null) return new Use(cPos);
                    if (state.OvenContents == "CROISSANT") return new Use(_init.OvenPos);
                    break;
                case "TART":
                    var tPos = GetTableWithItem("TART", state);
                    if (tPos != null) return new Use(tPos);
                    if (state.OvenContents == "TART") return new Use(_init.OvenPos);
                    break;
            }
            
        return new Wait();
    }

    private double EvaluateOrder((string Item, int Award) customer, State state)
    {
        var ingredients = customer.Item.Split('-').ToHashSet();
        var estimatedTurns = 3;
        
        foreach (var req in ingredients) 
            estimatedTurns += EstimateIngredientCost(req, state, ingredients);
        
        if (estimatedTurns > state.TurnsRemaining + 2) 
            return -10000;
        
        return (double)customer.Award / Math.Max(1, estimatedTurns); 
    }

    private int EstimateIngredientCost(string req, State state, HashSet<string> orderIngredients)
    {
        if (IsItemWithPlayerOrPartner(req, state, orderIngredients)) return 0;
        if (GetTableWithItem(req, state) != null) return 2;
        if (IsItemInAnyValidDish(req, state, orderIngredients)) return 0;
        
        switch (req)
        {
            case "ICE_CREAM":
            case "BLUEBERRIES":
                return 3;
                
            case "CHOPPED_STRAWBERRIES":
                if (state.TablesWithItems.ContainsValue("STRAWBERRIES") || 
                    state.PlayerItem == "STRAWBERRIES" || state.PartnerItem == "STRAWBERRIES")
                    return 4;
                return 7;
                
            case "CROISSANT":
                if (state.OvenContents == "CROISSANT") return 2;
                if (state.OvenContents == "DOUGH") return 10;
                if (GetTableWithItem("DOUGH", state) != null || 
                    state.PlayerItem == "DOUGH" || state.PartnerItem == "DOUGH")
                    return 12;
                return 15; 
                
            case "TART":
                if (state.OvenContents == "TART") return 2;
                if (state.OvenContents == "RAW_TART") return 10; 
                if (GetTableWithItem("RAW_TART", state) != null || 
                    state.PlayerItem == "RAW_TART" || state.PartnerItem == "RAW_TART")
                    return 12;
                if (GetTableWithItem("CHOPPED_DOUGH", state) != null || 
                    state.PlayerItem == "CHOPCHED_DOUGH" || state.PartnerItem == "CHOPPED_DOUGH")
                    return 15;
                return 19;
        }
        return 5;
    }

    private static bool IsItemInAnyValidDish(string item, State state, HashSet<string> target)
    {
        foreach (var kvp in state.TablesWithItems)
        {
            if (!kvp.Value.StartsWith("DISH") || !kvp.Value.Contains(item)) continue;
            var dishItems = kvp.Value.Split('-').ToHashSet();
            if (dishItems.IsSubsetOf(target)) return true;
        }
        return false;
    }
    
    private static bool IsItemWithPlayerOrPartner(string item, State state, HashSet<string> target)
    {
        if (state.PlayerItem == item || state.PartnerItem == item) return true;
        
        if (state.PlayerItem.StartsWith("DISH") && state.PlayerItem.Contains(item))
        {
            var items = state.PlayerItem.Split('-').ToHashSet();
            if (items.IsSubsetOf(target)) return true;
        }
        if (state.PartnerItem.StartsWith("DISH") && state.PartnerItem.Contains(item))
        {
            var items = state.PartnerItem.Split('-').ToHashSet();
            if (items.IsSubsetOf(target)) return true;
        }
        return false;
    }

    private static V FindEmptyTable(StateInit init, State state)
    {
        var bestTable = init.DishwasherPos;
        var minScore = int.MaxValue;
        
        for (var y = 0; y < 7; y++)
        for (var x = 0; x < 11; x++)
        {
            var pos = new V(x, y);
            if (init.Map[x, y] == '#' && !state.TablesWithItems.ContainsKey(pos))
            {
                var distToMe = Math.Abs(state.PlayerPos.X - x) + Math.Abs(state.PlayerPos.Y - y);
                var distToPartner = Math.Abs(state.PartnerPos.X - x) + Math.Abs(state.PartnerPos.Y - y);
                
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
}