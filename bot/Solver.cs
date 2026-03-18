namespace bot;

public class Solver
{
    private StateInit init;
    private HashSet<string> targetIngredients;
    public BotCommand GetCommand(State state, Countdown countdown)
    {
        if (state.Customers.Count == 0)
            return new Wait();
        init = state.Init;
        var targetOrder = state.Customers[0].Item;
        targetIngredients = targetOrder.Split('-').ToHashSet();
        var myIngredients = state.PlayerItem == "NONE" 
            ? new HashSet<string>() 
            : state.PlayerItem.Split('-').ToHashSet();
        var hasDish = myIngredients.Contains("DISH");
        if (targetIngredients.IsSubsetOf(myIngredients))
            return new Use(init.WindowPos);
        var boardItem = state.TablesWithItems.GetValueOrDefault(init.ChoppingBoardPos, "NONE");
        var needsTart = targetIngredients.Contains("TART") && !myIngredients.Contains("TART");
        var needsCroissant = targetIngredients.Contains("CROISSANT") && !myIngredients.Contains("CROISSANT");
        var needsStrawberries = targetIngredients.Contains("CHOPPED_STRAWBERRIES") && !myIngredients.Contains("CHOPPED_STRAWBERRIES");
        var tartReady = GetTableWithItem("TART", state) != null || state.OvenContents == "TART" || IsItemInAnyValidDish("TART", state);
        var tartCooking = state.OvenContents == "RAW_TART";
        var croissantReady = GetTableWithItem("CROISSANT", state) != null || state.OvenContents == "CROISSANT" || IsItemInAnyValidDish("CROISSANT", state);
        var croissantCooking = state.OvenContents == "DOUGH";
        var choppedReady = GetTableWithItem("CHOPPED_STRAWBERRIES", state) != null || boardItem == "CHOPPED_STRAWBERRIES" || IsItemInAnyValidDish("CHOPPED_STRAWBERRIES", state);
        var tartInProgressByPartner = state.PartnerItem is "RAW_TART" or "CHOPPED_DOUGH";
        var croissantInProgressByPartner = state.PartnerItem == "DOUGH" && !needsTart;
        var choppedInProgressByPartner = state.PartnerItem == "STRAWBERRIES";
        if (needsTart && !tartReady && !tartCooking && !tartInProgressByPartner)
        {
            if (hasDish) return new Use(FindEmptyTable(init, state));
            if (state.PlayerItem == "RAW_TART")
                return state.OvenContents == "NONE" ? new Use(init.OvenPos) : new Use(FindEmptyTable(init, state));
            var rawTartTable = GetTableWithItem("RAW_TART", state);
            if (rawTartTable != null && state.PlayerItem == "NONE")
                switch (state.OvenContents)
                {
                    case "NONE":
                        return new Use(rawTartTable);
                    case "CROISSANT" or "TART":
                        return new Use(init.OvenPos);
                }
            var choppedDoughTable = GetTableWithItem("CHOPPED_DOUGH", state);
            if (choppedDoughTable != null)
                return state.PlayerItem switch
                {
                    "BLUEBERRIES" => new Use(choppedDoughTable),
                    "NONE" => new Use(init.BlueberriesPos),
                    _ => new Use(FindEmptyTable(init, state))
                };
            if (state.PlayerItem == "CHOPPED_DOUGH") return new Use(FindEmptyTable(init, state));
            if (boardItem != "NONE" && boardItem != "DOUGH" && boardItem != "CHOPPED_DOUGH")
                return state.PlayerItem == "NONE" ? new Use(init.ChoppingBoardPos) :
                    new Use(FindEmptyTable(init, state)); 
            if (boardItem == "CHOPPED_DOUGH")
                return state.PlayerItem == "NONE" ? new Use(init.ChoppingBoardPos) : new Use(FindEmptyTable(init, state));
            if (boardItem != "DOUGH")
                return state.PlayerItem switch
                {
                    "DOUGH" when boardItem == "NONE" => new Use(init.ChoppingBoardPos),
                    "DOUGH" => new Use(FindEmptyTable(init, state)),
                    "NONE" => state.OvenContents is "CROISSANT" or "TART"
                        ? new Use(init.OvenPos)
                        : new Use(init.CroissantPos),
                    _ => new Use(FindEmptyTable(init, state))
                };
            return state.PlayerItem == "NONE" ? new Use(init.ChoppingBoardPos) : new Use(FindEmptyTable(init, state));
        }
        if (needsCroissant && !croissantReady && !croissantCooking && !croissantInProgressByPartner)
        {
            if (hasDish) return new Use(FindEmptyTable(init, state));
            if (state.PlayerItem == "DOUGH")
                return state.OvenContents == "NONE" ? new Use(init.OvenPos) : new Use(FindEmptyTable(init, state));
            var doughTable = GetTableWithItem("DOUGH", state);
            if (doughTable != null && state.PlayerItem == "NONE")
                switch (state.OvenContents)
                {
                    case "NONE":
                        return new Use(doughTable);
                    case "CROISSANT":
                    case "TART":
                        return new Use(init.OvenPos);
                }
            if (state.PlayerItem != "NONE") 
                return new Use(FindEmptyTable(init, state));
            return state.OvenContents is "CROISSANT" or "TART" ? new Use(init.OvenPos) : new Use(init.CroissantPos);
        }
        if (needsStrawberries && !choppedReady && !choppedInProgressByPartner)
        {
            if (hasDish) return new Use(FindEmptyTable(init, state));
            if (boardItem != "NONE" && boardItem != "STRAWBERRIES" && boardItem != "CHOPPED_STRAWBERRIES")
                return state.PlayerItem == "NONE" ? new Use(init.ChoppingBoardPos) : new Use(FindEmptyTable(init, state));
            if (boardItem == "STRAWBERRIES")
                return state.PlayerItem == "NONE" ? new Use(init.ChoppingBoardPos) : new Use(FindEmptyTable(init, state));
            if (state.PlayerItem == "STRAWBERRIES")
                return boardItem == "NONE" ? new Use(init.ChoppingBoardPos) : new Use(FindEmptyTable(init, state));
            var strawbTabl = GetTableWithItem("STRAWBERRIES", state);
            if (strawbTabl != null && state.PlayerItem == "NONE") return new Use(strawbTabl);
            return state.PlayerItem == "NONE" ? new Use(init.StrawberriesPos) : new Use(FindEmptyTable(init, state));
        }
        if (!hasDish)
        {
            foreach (var kvp in state.TablesWithItems)
            {
                if (!kvp.Value.StartsWith("DISH")) 
                    continue;
                var dishItems = kvp.Value.Split('-').ToHashSet();
                if (dishItems.IsSubsetOf(targetIngredients)) return new Use(kvp.Key);
            }
            return state.PlayerItem == "NONE" ? new Use(init.DishwasherPos) : new Use(FindEmptyTable(init, state));
        }
        var missingIngredients = targetIngredients.Except(myIngredients).ToList();
        foreach (var needed in missingIngredients)
            switch (needed)
            {
                case "ICE_CREAM":
                    return new Use(init.IceCreamPos);
                case "BLUEBERRIES":
                    return new Use(init.BlueberriesPos);
                case "CHOPPED_STRAWBERRIES":
                {
                    var pos = GetTableWithItem("CHOPPED_STRAWBERRIES", state);
                    if (pos != null) return new Use(pos);
                    if (boardItem == "CHOPPED_STRAWBERRIES") return new Use(init.ChoppingBoardPos);
                    break;
                }
                case "CROISSANT":
                {
                    var pos = GetTableWithItem("CROISSANT", state);
                    if (pos != null) return new Use(pos);
                    if (state.OvenContents == "CROISSANT") return new Use(init.OvenPos);
                    break;
                }
                case "TART":
                {
                    var pos = GetTableWithItem("TART", state);
                    if (pos != null) return new Use(pos);
                    if (state.OvenContents == "TART") return new Use(init.OvenPos);
                    break;
                }
            }
        return new Wait();
    }
    private static V FindEmptyTable(StateInit init, State state)
    {
        for (var y = 0; y < 7; y++)
        for (var x = 0; x < 11; x++)
        {
            var pos = new V(x, y);
            if (init.Map[x, y] == '#' && !state.TablesWithItems.ContainsKey(pos))
                return pos;
        }
        return init.DishwasherPos;
    }
    private V GetTableWithItem(string item, State state)
    {
        foreach (var kvp in state.TablesWithItems)
            if (kvp.Value == item && kvp.Key != init.ChoppingBoardPos) return kvp.Key;
        return null;
    }
    bool IsItemInAnyValidDish(string item, State state)
    {
        foreach (var kvp in state.TablesWithItems)
        {
            if (!kvp.Value.StartsWith("DISH") || !kvp.Value.Contains(item)) 
                continue;
            var dishItems = kvp.Value.Split('-').ToHashSet();
            if (dishItems.IsSubsetOf(targetIngredients)) return true;
        }
        return false;
    }
}