namespace bot;

public class Solver
{
    private StateInit init;
    private HashSet<string> targetIngredients;
    private string currentOrderId = null;
    
    // Anti-loop переменные
    private V lastActionPos = null;
    private int loopCounter = 0;

    public BotCommand GetCommand(State state, Countdown countdown)
    {
        if (state.Customers.Count == 0)
            return new Wait();
            
        init = state.Init;
        var myIngredients = state.PlayerItem == "NONE"
            ? new HashSet<string>()
            : state.PlayerItem.Split('-').ToHashSet();
            
        var myIngredientsWithoutDish = myIngredients.Where(i => i != "DISH").ToHashSet();
        var hasDish = myIngredients.Contains("DISH");
        
        if (currentOrderId != null && state.Customers.All(c => c.Item != currentOrderId)) {
            currentOrderId = null;
        }
        
        var (targetOrder, _) = state.Customers
            .OrderByDescending(c => EvaluateOrder(c, state, myIngredients))
            .First();
            
        currentOrderId = targetOrder;
        targetIngredients = targetOrder.Split('-').ToHashSet();

        // 1. ПРОВЕРКА НА УСТУПКУ (YIELD) И ИЗБЕЖАНИЕ DEADLOCK'ОВ
        if (hasDish && state.PartnerItem.StartsWith("DISH"))
        {
            var pItems = state.PartnerItem.Split('-').Where(i => i != "DISH").ToHashSet();
            if (myIngredientsWithoutDish.IsSubsetOf(targetIngredients) && pItems.IsSubsetOf(targetIngredients))
            {
                var partnerDone = targetIngredients.Where(i => i != "DISH").All(pItems.Contains);
                var myLen = myIngredientsWithoutDish.Count;
                var pLen = pItems.Count;
                
                var myDist = MDist(state.PlayerPos, init.WindowPos);
                var pDist = MDist(state.PartnerPos, init.WindowPos);

                var iShouldYield = partnerDone || myLen < pLen || 
                                  (myLen == pLen && (myDist > pDist || (myDist == pDist && state.PlayerPos.X > state.PartnerPos.X)));
                
                if (state.TurnsRemaining < pDist + 4 && !partnerDone) 
                    iShouldYield = false;

                if (iShouldYield) {
                    var canBeReused = false;
                    foreach (var c in state.Customers) {
                        if (c.Item == currentOrderId) continue; 
                        var req = c.Item.Split('-').Where(i => i != "DISH").ToHashSet();
                        if (myIngredientsWithoutDish.IsSubsetOf(req)) {
                            canBeReused = true; break;
                        }
                    }
                    return CreateAction(new Use(canBeReused ? FindEmptyTable(init, state) : init.DishwasherPos));
                }
            }
        }

        var needsTart = targetIngredients.Contains("TART");
        var needsCroissant = targetIngredients.Contains("CROISSANT");
        var needsStrawberries = targetIngredients.Contains("CHOPPED_STRAWBERRIES");
        
        // --- ИСПРАВЛЕНИЕ ЗАЦИКЛИВАНИЯ С ТАРЕЛКОЙ ---
        // Если у нас нет тарелки, мы заранее прикидываем, какую тарелку собираемся взять.
        var plannedDishItems = new HashSet<string>(myIngredientsWithoutDish);
        if (!hasDish) {
            int maxItems = -1;
            HashSet<string> bestTableDish = new HashSet<string>();
            foreach (var kvp in state.TablesWithItems) {
                if (!kvp.Value.StartsWith("DISH")) continue;
                var dishItems = kvp.Value.Split('-').Where(i => i != "DISH").ToHashSet();
                if (state.PlayerItem != "NONE" && dishItems.Contains(state.PlayerItem)) continue;
                
                if (dishItems.IsSubsetOf(targetIngredients) && dishItems.Count > maxItems) {
                    maxItems = dishItems.Count;
                    bestTableDish = dishItems;
                }
            }
            plannedDishItems.UnionWith(bestTableDish);
        }

        // Оцениваем готовность ингредиентов только на основе НАШЕЙ запланированной тарелки
        var tartReady = GetTableWithItem("TART", state) != null || state.OvenContents == "TART" || 
            plannedDishItems.Contains("TART") || IsItemWithPartner("TART", state, targetIngredients);
        var tartCooking = state.OvenContents == "RAW_TART";
        var tartInProgressByPartner = state.PartnerItem is "RAW_TART" or "CHOPPED_DOUGH";
        
        var croissantReady = GetTableWithItem("CROISSANT", state) != null || state.OvenContents == "CROISSANT" || 
            plannedDishItems.Contains("CROISSANT") || IsItemWithPartner("CROISSANT", state, targetIngredients);
        var croissantCooking = state.OvenContents == "DOUGH";
        var croissantInProgressByPartner = state.PartnerItem == "DOUGH" && !needsTart;
        
        var boardItem = state.TablesWithItems.GetValueOrDefault(init.ChoppingBoardPos, "NONE");
        var choppedReady = GetTableWithItem("CHOPPED_STRAWBERRIES", state) != null || boardItem == "CHOPPED_STRAWBERRIES" || 
            plannedDishItems.Contains("CHOPPED_STRAWBERRIES") || IsItemWithPartner("CHOPPED_STRAWBERRIES", state, targetIngredients);
        var choppedInProgressByPartner = state.PartnerItem == "STRAWBERRIES";
        // -------------------------------------------
        
        var iAmMakingTart = state.PlayerItem is "RAW_TART" or "CHOPPED_DOUGH" || (state.PlayerItem == "DOUGH" && needsTart);
        var iAmMakingCroissant = state.PlayerItem == "DOUGH" && !needsTart;
        var iAmChopping = state.PlayerItem == "STRAWBERRIES";

        // 2. СБРОС МУСОРА
        var isUseful = false;
        if (state.PlayerItem == "NONE") {
            isUseful = true;
        } else if (hasDish) {
            isUseful = myIngredientsWithoutDish.IsSubsetOf(targetIngredients);
        } else {
            var item = state.PlayerItem;
            if (targetIngredients.Contains(item)) {
                isUseful = true;
            } else if (item == "STRAWBERRIES" && needsStrawberries && !choppedReady) {
                isUseful = true;
            } else if (item == "DOUGH" && ((needsCroissant && !croissantReady && !croissantCooking) || (needsTart && !tartReady && !tartCooking))) {
                isUseful = true;
            } else if (item == "CHOPPED_DOUGH" && needsTart && !tartReady && !tartCooking) {
                isUseful = true;
            } else if (item == "RAW_TART" && needsTart && !tartReady && !tartCooking) {
                isUseful = true;
            }
        }

        if (!isUseful) {
            if (loopCounter > 2) return CreateAction(new Use(init.DishwasherPos));
            return CreateAction(new Use(FindEmptyTable(init, state)));
        }

        // 3. ПЕЧЬ - ЕСЛИ ПУСТЫЕ РУКИ
        if (state.PlayerItem == "NONE" && (state.OvenContents == "CROISSANT" || state.OvenContents == "TART"))
        {
            var myDist = MDist(state.PlayerPos, init.OvenPos);
            var partnerDist = MDist(state.PartnerPos, init.OvenPos);
            var isPartnerCloser = state.PartnerItem == "NONE" && partnerDist < myDist;
            if (!isPartnerCloser) return CreateAction(new Use(init.OvenPos));
        }

        // 4. ГОТОВОЕ БЛЮДО
        if (targetIngredients.IsSubsetOf(myIngredients))
            return CreateAction(new Use(init.WindowPos));

        // 5. ЛОГИКА ПРИГОТОВЛЕНИЯ КОМПОНЕНТОВ
        if (needsTart && !tartReady && !tartCooking && (!tartInProgressByPartner || iAmMakingTart) && !myIngredients.Contains("TART"))
        {
            if (hasDish) return CreateAction(new Use(FindEmptyTable(init, state))); 
            if (state.PlayerItem == "RAW_TART") {
                if (state.OvenContents == "NONE") return CreateAction(new Use(init.OvenPos));
                return CreateAction(new Use(FindEmptyTable(init, state, init.OvenPos))); 
            }
            var rawTartTable = GetTableWithItem("RAW_TART", state);
            if (rawTartTable != null && state.PlayerItem == "NONE" && state.OvenContents == "NONE") {
                return CreateAction(new Use(rawTartTable));
            }
            if (rawTartTable == null) {
                if (state.PlayerItem == "CHOPPED_DOUGH") return CreateAction(new Use(init.BlueberriesPos)); 
                var choppedDoughTable = GetTableWithItem("CHOPPED_DOUGH", state);
                if (choppedDoughTable != null && state.PlayerItem == "NONE") return CreateAction(new Use(choppedDoughTable));
                if (boardItem is "CHOPPED_DOUGH" or "DOUGH")
                    return CreateAction(state.PlayerItem == "NONE" ? new Use(init.ChoppingBoardPos) : new Use(FindEmptyTable(init, state)));
                if (boardItem != "NONE")
                    return CreateAction(state.PlayerItem == "NONE" ? new Use(init.ChoppingBoardPos) : new Use(FindEmptyTable(init, state)));
                return CreateAction(state.PlayerItem switch {
                    "DOUGH" => new Use(init.ChoppingBoardPos),
                    "NONE" => new Use(init.CroissantPos), 
                    _ => new Use(FindEmptyTable(init, state))
                });
            }
        }
        
        if (needsCroissant && !croissantReady && !croissantCooking && (!croissantInProgressByPartner || iAmMakingCroissant) && !myIngredients.Contains("CROISSANT"))
        {
            if (hasDish) return CreateAction(new Use(FindEmptyTable(init, state)));
            if (state.PlayerItem == "DOUGH") {
                if (state.OvenContents == "NONE") return CreateAction(new Use(init.OvenPos));
                return CreateAction(new Use(FindEmptyTable(init, state, init.OvenPos))); 
            }
            var doughTable = GetTableWithItem("DOUGH", state);
            if (doughTable != null && state.PlayerItem == "NONE" && state.OvenContents == "NONE") {
                return CreateAction(new Use(doughTable));
            }
            if (state.OvenContents == "NONE") {
                if (state.PlayerItem != "NONE") return CreateAction(new Use(FindEmptyTable(init, state)));
                return CreateAction(new Use(init.CroissantPos));
            }
        }
        
        if (needsStrawberries && !choppedReady && (!choppedInProgressByPartner || iAmChopping) && !myIngredients.Contains("CHOPPED_STRAWBERRIES"))
        {
            if (hasDish) return CreateAction(new Use(FindEmptyTable(init, state)));
            if (state.PlayerItem == "STRAWBERRIES") {
                if (boardItem == "NONE") return CreateAction(new Use(init.ChoppingBoardPos));
                return CreateAction(new Use(FindEmptyTable(init, state)));
            }
            if (boardItem is "STRAWBERRIES" or "CHOPPED_STRAWBERRIES")
                return CreateAction(state.PlayerItem == "NONE" ? new Use(init.ChoppingBoardPos) : new Use(FindEmptyTable(init, state)));
            if (boardItem != "NONE")
                return CreateAction(state.PlayerItem == "NONE" ? new Use(init.ChoppingBoardPos) : new Use(FindEmptyTable(init, state)));
            var strawbTabl = GetTableWithItem("STRAWBERRIES", state);
            if (strawbTabl != null && state.PlayerItem == "NONE") return CreateAction(new Use(strawbTabl));
            if (state.PlayerItem != "NONE") return CreateAction(new Use(FindEmptyTable(init, state)));
            return CreateAction(new Use(init.StrawberriesPos));
        }

        // 6. ПОИСК ТАРЕЛКИ
        if (!hasDish)
        {
            var holdingValidIngredient = state.PlayerItem is "BLUEBERRIES" or "ICE_CREAM" or "CHOPPED_STRAWBERRIES" or "CROISSANT" or "TART";
            V bestDishPos = null;
            var maxDishItems = -1;
            
            if (state.PlayerItem == "NONE" || holdingValidIngredient) 
            {
                foreach (var kvp in state.TablesWithItems)
                {
                    if (!kvp.Value.StartsWith("DISH")) continue;
                    var dishItems = kvp.Value.Split('-').ToHashSet();
                    var dishItemsWithoutDish = dishItems.Where(i => i != "DISH").ToHashSet();
                    
                    if (state.PlayerItem != "NONE" && dishItems.Contains(state.PlayerItem)) continue;
                    if (dishItemsWithoutDish.IsSubsetOf(targetIngredients))
                    {
                        if (dishItems.Count > maxDishItems) {
                            maxDishItems = dishItems.Count;
                            bestDishPos = kvp.Key;
                        }
                    }
                }
                if (bestDishPos != null) return CreateAction(new Use(bestDishPos));
            }
            
            var partnerAssembling = state.PartnerItem.StartsWith("DISH") && 
                state.PartnerItem.Split('-').Where(i => i != "DISH").All(targetIngredients.Contains);
            
            if (partnerAssembling && state.TurnsRemaining < 12 && MDist(state.PartnerPos, init.WindowPos) > 4)
                partnerAssembling = false;

            if (state.PlayerItem == "NONE" && partnerAssembling) { }
            else
            {
                if (state.PlayerItem == "NONE")
                {
                    foreach (var kvp in state.TablesWithItems)
                        if (kvp.Value == "DISH") return CreateAction(new Use(kvp.Key));
                }
                return CreateAction(state.PlayerItem == "NONE" ? new Use(init.DishwasherPos) : new Use(FindEmptyTable(init, state)));
            }
        }

        // 7. СБОР НЕДОСТАЮЩИХ ИНГРЕДИЕНТОВ (TSP)
        var missingIngredients = targetIngredients.Except(myIngredientsWithoutDish).ToList();
        var reallyMissingIngredients = new List<string>();
        
        var pItemsWithoutDish = state.PartnerItem.StartsWith("DISH") ? state.PartnerItem.Split('-').Where(i => i != "DISH").ToList() : new List<string>();
        var partnerAssemblingThis = state.PartnerItem.StartsWith("DISH") && pItemsWithoutDish.All(targetIngredients.Contains);

        foreach (var needed in missingIngredients)
        {
            if (needed == "DISH") continue;
            if (hasDish && state.PartnerItem == needed) continue;
            if (!hasDish && IsItemWithPartner(needed, state, targetIngredients)) continue;
            if (!hasDish && partnerAssemblingThis && GetTableWithItem(needed, state) != null) continue;
            reallyMissingIngredients.Add(needed);
        }
        
        if (reallyMissingIngredients.Count == 0 && missingIngredients.Count > 0) {
            var dodgePos = DodgePartner(init, state);
            if (dodgePos != null) return CreateAction(new Move(dodgePos));
            
            if (MDist(state.PlayerPos, init.WindowPos) > 2) return CreateAction(new Move(init.WindowPos));
            return CreateAction(new Wait());
        }
        
        BotCommand bestAction = new Wait();
        var bestScore = double.MaxValue;

        var gatherableIngredients = new List<string>();
        foreach (var needed in reallyMissingIngredients) {
            var (tPos, _, _) = GetItemTarget(needed, state, init);
            if (tPos != null) gatherableIngredients.Add(needed);
        }

        if (gatherableIngredients.Count > 0)
        {
            var permutations = GetPermutations(gatherableIngredients);
            foreach (var perm in permutations)
            {
                double currentRouteScore = 0;
                V currentPos = state.PlayerPos;
                V firstTarget = null;
                bool isFirstOvenCookingTarget = false;

                for (int i = 0; i < perm.Count; i++)
                {
                    var needed = perm[i];
                    var (targetPos, penalty, isCooking) = GetItemTarget(needed, state, init);
                    
                    int dist = MDist(currentPos, targetPos);
                    currentRouteScore += dist * 10;

                    if (i == 0) {
                        currentRouteScore += penalty;
                        firstTarget = targetPos;
                        if (isCooking) isFirstOvenCookingTarget = true;
                        
                        var ovenBusy = state.OvenContents != "NONE";
                        if (ovenBusy && targetPos != init.OvenPos) {
                            int safeDist = state.OvenTimer > 4 ? 8 : 4; 
                            if (MDist(targetPos, init.OvenPos) > safeDist) 
                                currentRouteScore += 5000;
                        }
                    }
                    currentPos = targetPos;
                }

                currentRouteScore += MDist(currentPos, init.WindowPos) * 10;

                if (currentRouteScore < bestScore)
                {
                    bestScore = currentRouteScore;
                    bestAction = firstTarget == init.OvenPos && isFirstOvenCookingTarget
                        ? new Move(firstTarget)
                        : new Use(firstTarget);
                }
            }
        }
        
        if (bestAction is Wait && state.OvenContents != "NONE" && MDist(state.PlayerPos, init.OvenPos) > 2)
            return CreateAction(new Move(init.OvenPos));
            
        return CreateAction(bestAction);
    }

    // ==========================================
    // ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ 
    // ==========================================
    private BotCommand CreateAction(BotCommand cmd)
    {
        if (cmd is Use useCmd) {
            if (lastActionPos != null && useCmd.Target.Equals(lastActionPos)) {
                loopCounter++;
            } else {
                loopCounter = 0;
            }
            lastActionPos = useCmd.Target;
        } else {
            loopCounter = 0;
            lastActionPos = null;
        }
        return cmd;
    }

    private V DodgePartner(StateInit init, State state)
    {
        if (MDist(state.PlayerPos, state.PartnerPos) <= 1 && state.PlayerItem == "NONE")
        {
            if (state.PartnerItem is "RAW_TART" or "DOUGH" && MDist(state.PlayerPos, init.OvenPos) <= 2)
                return init.DishwasherPos;
                
            if (state.PartnerItem.StartsWith("DISH") && MDist(state.PlayerPos, init.WindowPos) <= 2)
                return init.DishwasherPos;
        }
        return null;
    }

    private double EvaluateOrder((string Item, int Award) customer, State state, HashSet<string> myIngredients)
    {
        var ingredients = customer.Item.Split('-').ToHashSet();
        var estimatedTurns = 3;
        var myIngredientsWithoutDish = myIngredients.Where(i => i != "DISH").ToHashSet();
        if (myIngredients.Contains("DISH") && !myIngredientsWithoutDish.IsSubsetOf(ingredients))
            return -100000;
        
        if (state.PlayerItem == "NONE" && state.PartnerItem.StartsWith("DISH")) 
        {
            var pItems = state.PartnerItem.Split('-').Where(i => i != "DISH").ToHashSet();
            if (pItems.IsSubsetOf(ingredients)) 
            {
                var missing = ingredients.Where(i => i != "DISH").Except(pItems).ToList();
                var allMissingAvailable = true;
                
                foreach (var m in missing)
                {
                    var isAvailable = GetTableWithItem(m, state) != null;
                    if (m == "CROISSANT" && state.OvenContents is "CROISSANT" or "DOUGH") isAvailable = true;
                    if (m == "TART" && state.OvenContents is "TART" or "RAW_TART") isAvailable = true;
                    if (!isAvailable) {
                        allMissingAvailable = false;
                        break;
                    }
                }
                if (allMissingAvailable) return -50000; 
            }
        }

        foreach (var req in ingredients) 
            estimatedTurns += EstimateIngredientCost(req, state, ingredients);
            
        if (estimatedTurns > state.TurnsRemaining + 2) 
            return -10000;
        
        double finalScore;
        if (state.TurnsRemaining <= 40)
            finalScore = 1000.0 - estimatedTurns + customer.Award / 10000.0;
        else
            finalScore = (double)customer.Award / Math.Max(1, estimatedTurns);
            
        if (customer.Item == currentOrderId) finalScore += 10000; 
        return finalScore;
    }

    private int EstimateIngredientCost(string req, State state, HashSet<string> orderIngredients)
    {
        if (IsItemWithPlayerOrPartner(req, state, orderIngredients)) return 0;
        if (GetTableWithItem(req, state) != null) return 2;
        if (IsItemInAnyValidDish(req, state, orderIngredients)) return 0;
        switch (req)
        {
            case "ICE_CREAM":
            case "BLUEBERRIES": return 3;
            case "CHOPPED_STRAWBERRIES":
                if (state.TablesWithItems.ContainsValue("STRAWBERRIES") || 
                    state.PlayerItem == "STRAWBERRIES" || state.PartnerItem == "STRAWBERRIES") return 4;
                return 7;
            case "CROISSANT":
                if (state.OvenContents == "CROISSANT") return 2;
                if (state.OvenContents == "DOUGH") return state.OvenTimer + 2;
                if (GetTableWithItem("DOUGH", state) != null || 
                    state.PlayerItem == "DOUGH" || state.PartnerItem == "DOUGH") return 12;
                return 15; 
            case "TART":
                if (state.OvenContents == "TART") return 2;
                if (state.OvenContents == "RAW_TART") return state.OvenTimer + 2; 
                if (GetTableWithItem("RAW_TART", state) != null || 
                    state.PlayerItem == "RAW_TART" || state.PartnerItem == "RAW_TART") return 12;
                if (GetTableWithItem("CHOPPED_DOUGH", state) != null || 
                    state.PlayerItem == "CHOPPED_DOUGH" || state.PartnerItem == "CHOPPED_DOUGH") return 15;
                return 19;
        }
        return 5;
    }

    private static bool IsItemInAnyValidDish(string item, State state, HashSet<string> target)
    {
        foreach (var kvp in state.TablesWithItems)
        {
            if (!kvp.Value.StartsWith("DISH")) continue;
            var dishItems = kvp.Value.Split('-');
            if (!dishItems.Contains(item)) continue;
            var dishSet = dishItems.Where(i => i != "DISH").ToHashSet();
            if (dishSet.IsSubsetOf(target)) return true;
        }
        return false;
    }

    private static bool IsItemWithPlayerOrPartner(string item, State state, HashSet<string> target)
    {
        if (state.PlayerItem == item || state.PartnerItem == item) return true;
        var myParts = state.PlayerItem.Split('-');
        if (myParts[0] == "DISH" && myParts.Contains(item)) {
            var dishItems = myParts.Where(i => i != "DISH").ToHashSet();
            if (dishItems.IsSubsetOf(target)) return true;
        }
        var partnerParts = state.PartnerItem.Split('-');
        if (partnerParts[0] == "DISH" && partnerParts.Contains(item)) {
            var dishItems = partnerParts.Where(i => i != "DISH").ToHashSet();
            if (dishItems.IsSubsetOf(target)) return true;
        }
        return false;
    }

    private static bool IsItemWithPartner(string item, State state, HashSet<string> target)
    {
        if (state.PartnerItem == item) return true;
        var partnerParts = state.PartnerItem.Split('-');
        if (partnerParts[0] == "DISH" && partnerParts.Contains(item)) {
            var dishItems = partnerParts.Where(i => i != "DISH").ToHashSet();
            if (dishItems.IsSubsetOf(target)) return true;
        }
        return false;
    }

    private V FindEmptyTable(StateInit init, State state, V preferredTarget = null)
    {
        var bestTable = init.DishwasherPos;
        var minScore = int.MaxValue;
        for (var y = 0; y < 7; y++)
        for (var x = 0; x < 11; x++)
        {
            var pos = new V(x, y);
            if (init.Map[x, y] == '#' && !state.TablesWithItems.ContainsKey(pos))
            {
                int score;
                if (preferredTarget != null) {
                    score = MDist(pos, preferredTarget) * 10 + MDist(state.PlayerPos, pos);
                } else {
                    var distToMe = MDist(state.PlayerPos, pos);
                    var distToPartner = MDist(state.PartnerPos, pos);
                    score = distToMe * 10 - distToPartner; 
                }

                if (MDist(pos, init.OvenPos) <= 1) score += 500;
                if (MDist(pos, init.WindowPos) <= 1) score += 500;
                if (MDist(pos, init.ChoppingBoardPos) <= 1) score += 200;
                
                // Физический разрыв петли: жестко штрафуем стол, на который мы ТОЛЬКО ЧТО положили предмет
                if (lastActionPos != null && pos.Equals(lastActionPos)) score += 1000;
                
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
            if (kvp.Value == item && kvp.Key != init.ChoppingBoardPos) return kvp.Key;
        return null;
    }

    private static int MDist(V a, V b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);


    // ==========================================
    // НОВЫЕ ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ ДЛЯ TSP
    // ==========================================
    private List<List<string>> GetPermutations(List<string> list) {
        var result = new List<List<string>>();
        if (list.Count == 0) return result;
        if (list.Count == 1) {
            result.Add(new List<string>(list));
            return result;
        }
        for (int i = 0; i < list.Count; i++) {
            var current = list[i];
            var remaining = new List<string>(list);
            remaining.RemoveAt(i);
            var remainingPerms = GetPermutations(remaining);
            foreach (var perm in remainingPerms) {
                perm.Insert(0, current);
                result.Add(perm);
            }
        }
        return result;
    }

    private (V pos, double penalty, bool isCooking) GetItemTarget(string needed, State state, StateInit init)
    {
        V targetPos = null;
        double penalty = 0;
        bool isCooking = false;
        var boardItem = state.TablesWithItems.GetValueOrDefault(init.ChoppingBoardPos, "NONE");

        switch (needed)
        {
            case "ICE_CREAM":
                var icPos = GetTableWithItem("ICE_CREAM", state);
                targetPos = icPos ?? init.IceCreamPos;
                if (icPos != null) penalty = -50;
                break;
            case "BLUEBERRIES":
                var bbPos = GetTableWithItem("BLUEBERRIES", state);
                targetPos = bbPos ?? init.BlueberriesPos;
                if (bbPos != null) penalty = -50;
                break;
            case "CHOPPED_STRAWBERRIES":
                var sPos = GetTableWithItem("CHOPPED_STRAWBERRIES", state);
                if (sPos != null) {
                    targetPos = sPos;
                    penalty = -50;
                }
                else if (boardItem == "CHOPPED_STRAWBERRIES" || boardItem == "STRAWBERRIES" || state.PlayerItem == "STRAWBERRIES") {
                    targetPos = init.ChoppingBoardPos;
                    penalty = -50;
                }
                else {
                    var rawS = GetTableWithItem("STRAWBERRIES", state);
                    targetPos = rawS ?? init.StrawberriesPos;
                }
                break;
            case "CROISSANT":
                var cPos = GetTableWithItem("CROISSANT", state);
                if (cPos != null) {
                    targetPos = cPos;
                    penalty = -50;
                }
                else if (state.OvenContents == "CROISSANT" && (state.PlayerItem == "NONE" || state.PlayerItem.StartsWith("DISH"))) { 
                    targetPos = init.OvenPos;
                    penalty = -1000;
                }
                else if (state.OvenContents == "DOUGH") {
                    targetPos = init.OvenPos;
                    penalty = 10000; 
                    isCooking = true;
                }
                break;
            case "TART":
                var tPos = GetTableWithItem("TART", state);
                if (tPos != null) {
                    targetPos = tPos;
                    penalty = -50;
                }
                else if (state.OvenContents == "TART" && (state.PlayerItem == "NONE" || state.PlayerItem.StartsWith("DISH"))) { 
                    targetPos = init.OvenPos;
                    penalty = -1000;
                }
                else if (state.OvenContents == "RAW_TART") {
                    targetPos = init.OvenPos;
                    penalty = 10000;
                    isCooking = true;
                }
                break;
        }
        return (targetPos, penalty, isCooking);
    }
}