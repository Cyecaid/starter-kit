namespace bot;

public class Solver
{
    private StateInit init;
    private HashSet<string> targetIngredients;
    private string currentOrderId = null;

    private V lastActionPos = null;
    private int loopCounter = 0;
    private List<string> actionHistory = new List<string>();

    // Глобальные переменные для радара BFS
    private int[,] myBfs = new int[11, 7];
    private int[,] partnerBfs = new int[11, 7];
    private V statePlayerPos;
    private V statePartnerPos;

    public BotCommand GetCommand(State state, Countdown countdown)
    {
        if (state.Customers.Count == 0)
            return new Wait();

        init = state.Init;
        statePlayerPos = state.PlayerPos;
        statePartnerPos = state.PartnerPos;

        // ЗАПУСК РАДАРА (Стоимость клетки с партнером = 2, чтобы обходить мягко)
        ComputeRealDistances(state);

        var myIngredients = state.PlayerItem == "NONE"
            ? new HashSet<string>()
            : state.PlayerItem.Split('-').ToHashSet();

        var myIngredientsWithoutDish = myIngredients.Where(i => i != "DISH").ToHashSet();
        var hasDish = myIngredients.Contains("DISH");

        if (currentOrderId != null && state.Customers.All(c => c.Item != currentOrderId))
        {
            currentOrderId = null;
        }

        var targetOrderData = state.Customers
            .OrderByDescending(c => EvaluateOrder(c, state, myIngredients))
            .First();

        currentOrderId = targetOrderData.Item;
        targetIngredients = currentOrderId.Split('-').ToHashSet();

        // 1. BURN IMMINENT: Экстренное спасение выпечки с учетом партнера
        int myTimeToOven = MyDist(init.OvenPos) + (state.PlayerItem == "NONE" ? 0 : 1);
        if ((state.OvenContents == "CROISSANT" || state.OvenContents == "TART") && state.OvenTimer <= myTimeToOven)
        {
            int pTimeToOven = PartnerDist(init.OvenPos);
            bool partnerCanSave = state.PartnerItem == "NONE" || (state.PartnerItem.StartsWith("DISH") && targetIngredients.Contains(state.OvenContents) && !state.PartnerItem.Contains(state.OvenContents));
            
            // Если партнер не спасет сам (или он дальше нас), спасаем мы!
            if (!(partnerCanSave && pTimeToOven <= state.OvenTimer + 1 && pTimeToOven < myTimeToOven))
            {
                if (state.PlayerItem == "NONE" || (hasDish && targetIngredients.Contains(state.OvenContents) && !myIngredients.Contains(state.OvenContents)))
                    return CreateAction(new Use(init.OvenPos), state);
                else if (state.PlayerItem is not "ICE_CREAM" and not "BLUEBERRIES")
                    return CreateAction(new Use(FindEmptyTable(init, state, state.PlayerPos)), state); 
            }
        }

        // Умный перехват готовой выпечки без толкотни
        if (state.OvenContents == "CROISSANT" || state.OvenContents == "TART")
        {
            var ovenItem = state.OvenContents;
            var myDist = MyDist(init.OvenPos);
            var partnerDist = PartnerDist(init.OvenPos);
            
            bool partnerCanTake = state.PartnerItem == "NONE" || (state.PartnerItem.StartsWith("DISH") && targetIngredients.Contains(ovenItem) && !state.PartnerItem.Contains(ovenItem));
            var isPartnerCloser = partnerCanTake && partnerDist < myDist;

            bool canTake = false;
            if (state.PlayerItem == "NONE") canTake = true;
            else if (hasDish && targetIngredients.Contains(ovenItem) && !myIngredients.Contains(ovenItem)) canTake = true;

            if (canTake && !isPartnerCloser && myDist <= 3) 
                return CreateAction(new Use(init.OvenPos), state);
        }

        if (hasDish && state.PartnerItem.StartsWith("DISH"))
        {
            var pItems = state.PartnerItem.Split('-').Where(i => i != "DISH").ToHashSet();
            if (myIngredientsWithoutDish.IsSubsetOf(targetIngredients) && pItems.IsSubsetOf(targetIngredients))
            {
                var partnerDone = targetIngredients.Where(i => i != "DISH").All(pItems.Contains);
                var myLen = myIngredientsWithoutDish.Count;
                var pLen = pItems.Count;

                var myDist = MyDist(init.WindowPos);
                var pDist = PartnerDist(init.WindowPos);

                var iShouldYield = partnerDone || myLen < pLen ||
                                   (myLen == pLen && (myDist > pDist ||
                                                      (myDist == pDist && state.PlayerPos.X > state.PartnerPos.X)));

                if (state.TurnsRemaining < pDist + 4 && !partnerDone)
                    iShouldYield = false;

                if (iShouldYield)
                {
                    return CreateAction(new Use(FindEmptyTable(init, state)), state);
                }
            }
        }

        var needsTart = targetIngredients.Contains("TART");
        var needsCroissant = targetIngredients.Contains("CROISSANT");
        var needsStrawberries = targetIngredients.Contains("CHOPPED_STRAWBERRIES");

        var plannedDishItems = new HashSet<string>(myIngredientsWithoutDish);
        if (!hasDish)
        {
            var maxItems = -1;
            var bestTableDish = new HashSet<string>();
            foreach (var dishItems in from kvp in state.TablesWithItems where kvp.Value.StartsWith("DISH") select kvp.Value.Split('-').Where(i => i != "DISH").ToHashSet() into dishItems where state.PlayerItem == "NONE" || !dishItems.Contains(state.PlayerItem) where dishItems.IsSubsetOf(targetIngredients) && dishItems.Count > maxItems select dishItems)
            {
                maxItems = dishItems.Count;
                bestTableDish = dishItems;
            }
            plannedDishItems.UnionWith(bestTableDish);
        }

        var tartReady = GetTableWithItem("TART", state) != null || state.OvenContents == "TART" ||
                        plannedDishItems.Contains("TART") || IsItemWithPartner("TART", state, targetIngredients);
        var tartCooking = state.OvenContents == "RAW_TART";
        var tartInProgressByPartner = state.PartnerItem is "RAW_TART" or "CHOPPED_DOUGH";

        var croissantReady = GetTableWithItem("CROISSANT", state) != null || state.OvenContents == "CROISSANT" ||
                             plannedDishItems.Contains("CROISSANT") ||
                             IsItemWithPartner("CROISSANT", state, targetIngredients);
        var croissantCooking = state.OvenContents == "DOUGH";
        var croissantInProgressByPartner = state.PartnerItem == "DOUGH" && !needsTart;

        var boardItem = state.TablesWithItems.GetValueOrDefault(init.ChoppingBoardPos, "NONE");
        var choppedReady = GetTableWithItem("CHOPPED_STRAWBERRIES", state) != null ||
                           boardItem == "CHOPPED_STRAWBERRIES" ||
                           plannedDishItems.Contains("CHOPPED_STRAWBERRIES") ||
                           IsItemWithPartner("CHOPPED_STRAWBERRIES", state, targetIngredients);
        var choppedInProgressByPartner = state.PartnerItem == "STRAWBERRIES";

        var iAmMakingTart = state.PlayerItem is "RAW_TART" or "CHOPPED_DOUGH" ||
                            (state.PlayerItem == "DOUGH" && needsTart);
        var iAmMakingCroissant = state.PlayerItem == "DOUGH" && !needsTart;
        var iAmChopping = state.PlayerItem == "STRAWBERRIES";

        var isUseful = false;
        if (state.PlayerItem == "NONE")
            isUseful = true;
        else if (hasDish)
            isUseful = myIngredientsWithoutDish.IsSubsetOf(targetIngredients);
        else
        {
            var item = state.PlayerItem;
            if (targetIngredients.Contains(item)) isUseful = true;
            else if (item == "STRAWBERRIES" && needsStrawberries && !choppedReady) isUseful = true;
            else if (item == "DOUGH" && ((needsCroissant && !croissantReady && !croissantCooking) || (needsTart && !tartReady && !tartCooking))) isUseful = true;
            else if (item == "CHOPPED_DOUGH" && needsTart && !tartReady && !tartCooking) isUseful = true;
            else if (item == "RAW_TART" && needsTart && !tartReady && !tartCooking) isUseful = true;
        }

        if (!isUseful)
        {
            if (hasDish) return CreateAction(new Use(FindEmptyTable(init, state, state.PlayerPos)), state);
            return CreateAction(loopCounter > 2 ? new Use(init.DishwasherPos) : new Use(FindEmptyTable(init, state, state.PlayerPos)), state);
        }
        
        if (targetIngredients.IsSubsetOf(myIngredients))
            return CreateAction(new Use(init.WindowPos), state);
        
        // --- Логика цепочек рецептов ---
        if (needsTart && !tartReady && !tartCooking && (!tartInProgressByPartner || iAmMakingTart) && !myIngredients.Contains("TART"))
        {
            if (hasDish) return CreateAction(new Use(FindEmptyTable(init, state, state.PlayerPos)), state);
            if (state.PlayerItem == "RAW_TART") return CreateAction(state.OvenContents == "NONE" ? new Use(init.OvenPos) : new Use(FindEmptyTable(init, state, init.OvenPos)), state);

            var rawTartTable = GetTableWithItem("RAW_TART", state);
            if (rawTartTable != null && state.PlayerItem == "NONE" && state.OvenContents == "NONE") return CreateAction(new Use(rawTartTable), state);

            if (rawTartTable == null)
            {
                if (state.PlayerItem == "CHOPPED_DOUGH") return CreateAction(new Use(init.BlueberriesPos), state);
                var choppedDoughTable = GetTableWithItem("CHOPPED_DOUGH", state);
                if (choppedDoughTable != null && state.PlayerItem == "NONE") return CreateAction(new Use(choppedDoughTable), state);
                
                if (boardItem is "CHOPPED_DOUGH" or "DOUGH") return CreateAction(state.PlayerItem == "NONE" ? new Use(init.ChoppingBoardPos) : new Use(FindEmptyTable(init, state, state.PlayerPos)), state);
                if (boardItem != "NONE") return CreateAction(state.PlayerItem == "NONE" ? new Use(init.ChoppingBoardPos) : new Use(FindEmptyTable(init, state, state.PlayerPos)), state);
                    
                return CreateAction(state.PlayerItem switch {
                    "DOUGH" => new Use(init.ChoppingBoardPos),
                    "NONE" => new Use(init.CroissantPos),
                    _ => new Use(FindEmptyTable(init, state, state.PlayerPos))
                }, state);
            }
        }

        if (needsCroissant && !croissantReady && !croissantCooking && (!croissantInProgressByPartner || iAmMakingCroissant) && !myIngredients.Contains("CROISSANT"))
        {
            if (hasDish) return CreateAction(new Use(FindEmptyTable(init, state, state.PlayerPos)), state);
            if (state.PlayerItem == "DOUGH") return CreateAction(state.OvenContents == "NONE" ? new Use(init.OvenPos) : new Use(FindEmptyTable(init, state, init.OvenPos)), state);

            var doughTable = GetTableWithItem("DOUGH", state);
            if (doughTable != null && state.PlayerItem == "NONE" && state.OvenContents == "NONE") return CreateAction(new Use(doughTable), state);

            if (state.OvenContents == "NONE") return CreateAction(state.PlayerItem != "NONE" ? new Use(FindEmptyTable(init, state, state.PlayerPos)) : new Use(init.CroissantPos), state);
        }

        if (needsStrawberries && !choppedReady && (!choppedInProgressByPartner || iAmChopping) && !myIngredients.Contains("CHOPPED_STRAWBERRIES"))
        {
            if (hasDish) return CreateAction(new Use(FindEmptyTable(init, state, state.PlayerPos)), state);
            if (state.PlayerItem == "STRAWBERRIES") return CreateAction(boardItem == "NONE" ? new Use(init.ChoppingBoardPos) : new Use(FindEmptyTable(init, state, state.PlayerPos)), state);

            if (boardItem is "STRAWBERRIES" or "CHOPPED_STRAWBERRIES") return CreateAction(state.PlayerItem == "NONE" ? new Use(init.ChoppingBoardPos) : new Use(FindEmptyTable(init, state, state.PlayerPos)), state);
            if (boardItem != "NONE") return CreateAction(state.PlayerItem == "NONE" ? new Use(init.ChoppingBoardPos) : new Use(FindEmptyTable(init, state, state.PlayerPos)), state);
                
            var strawbTabl = GetTableWithItem("STRAWBERRIES", state);
            if (strawbTabl != null && state.PlayerItem == "NONE") return CreateAction(new Use(strawbTabl), state);
            
            return CreateAction(state.PlayerItem != "NONE" ? new Use(FindEmptyTable(init, state, state.PlayerPos)) : new Use(init.StrawberriesPos), state);
        }
        
        // --- Сборка блюда (Использует Истинное расстояние) ---
        if (!hasDish)
        {
            var holdingValidIngredient = state.PlayerItem is "BLUEBERRIES" or "ICE_CREAM" or "CHOPPED_STRAWBERRIES" or "CROISSANT" or "TART";
            var partnerAssembling = state.PartnerItem.StartsWith("DISH") && state.PartnerItem.Split('-').Where(i => i != "DISH").All(targetIngredients.Contains);

            if (partnerAssembling && state.TurnsRemaining < 12 && MyDist(init.WindowPos) > 4) partnerAssembling = false;

            if (state.PlayerItem == "NONE" && partnerAssembling) { }
            else
            {
                V bestDishPos = null;
                var maxDishItems = -1;
                var minDishDist = int.MaxValue;

                if (state.PlayerItem == "NONE" || holdingValidIngredient)
                {
                    foreach (var kvp in state.TablesWithItems)
                    {
                        if (!kvp.Value.StartsWith("DISH")) continue;
                        var dishItems = kvp.Value.Split('-').ToHashSet();
                        var dishItemsWithoutDish = dishItems.Where(i => i != "DISH").ToHashSet();

                        if (state.PlayerItem != "NONE" && dishItems.Contains(state.PlayerItem)) continue;
                        if (dishItems.Count > 1 && PartnerDist(kvp.Key) <= 2 && state.PartnerItem == "NONE") continue;

                        if (dishItemsWithoutDish.IsSubsetOf(targetIngredients))
                        {
                            int dist = MyDist(kvp.Key);
                            // ИСПРАВЛЕНИЕ: Берем самую полную тарелку, а если их несколько — самую ближнюю!
                            if (dishItems.Count > maxDishItems || (dishItems.Count == maxDishItems && dist < minDishDist))
                            {
                                maxDishItems = dishItems.Count;
                                bestDishPos = kvp.Key;
                                minDishDist = dist;
                            }
                        }
                    }

                    if (bestDishPos != null) return CreateAction(new Use(bestDishPos), state);
                }

                if (state.PlayerItem == "NONE")
                {
                    // ИСПРАВЛЕНИЕ: Из всех пустых тарелок берем ближайшую к нам
                    var bestEmptyDish = state.TablesWithItems.Where(kvp => kvp.Value == "DISH" && PartnerDist(kvp.Key) > 1)
                                            .OrderBy(kvp => MyDist(kvp.Key))
                                            .FirstOrDefault().Key;
                                            
                    if (bestEmptyDish != null) return CreateAction(new Use(bestEmptyDish), state);
                }

                return CreateAction(state.PlayerItem == "NONE" ? new Use(init.DishwasherPos) : new Use(FindEmptyTable(init, state, state.PlayerPos)), state);
            }
        }
        
        // --- Сбор недостающих ингредиентов ---
        var missingIngredients = targetIngredients.Except(myIngredientsWithoutDish).ToList();
        var reallyMissingIngredients = new List<string>();

        var pItemsWithoutDish = state.PartnerItem.StartsWith("DISH") ? state.PartnerItem.Split('-').Where(i => i != "DISH").ToList() : new List<string>();
        var partnerAssemblingThis = state.PartnerItem.StartsWith("DISH") && pItemsWithoutDish.All(targetIngredients.Contains);
        bool dishAvailable = state.TablesWithItems.Any(kvp => kvp.Value.StartsWith("DISH") && kvp.Value.Split('-').Where(i=>i!="DISH").All(targetIngredients.Contains));

        foreach (var needed in missingIngredients)
        {
            if (needed == "DISH") continue;
            if (hasDish && state.PartnerItem == needed) continue;
            if (!hasDish && IsItemWithPartner(needed, state, targetIngredients)) continue;
            if (!hasDish && partnerAssemblingThis && GetTableWithItem(needed, state) != null) continue;
            
            if ((needed == "ICE_CREAM" || needed == "BLUEBERRIES") && !hasDish && !dishAvailable) 
                continue;

            reallyMissingIngredients.Add(needed);
        }

        if (reallyMissingIngredients.Count == 0 && missingIngredients.Count > 0)
        {
            var dodgePos = DodgePartner(init, state);
            if (dodgePos != null) return CreateAction(new Move(dodgePos), state);

            if (MyDist(init.WindowPos) > 2) return CreateAction(new Move(init.WindowPos), state);
            return CreateAction(new Wait(), state);
        }

        BotCommand bestAction = new Wait();
        var bestScore = double.MaxValue;

        var gatherableIngredients = new List<string>();
        foreach (var needed in reallyMissingIngredients)
        {
            var (tPos, _, _) = GetItemTarget(needed, state, init);
            if (tPos != null) gatherableIngredients.Add(needed);
        }

        if (gatherableIngredients.Count > 0)
        {
            var permutations = GetPermutations(gatherableIngredients);
            foreach (var perm in permutations)
            {
                double currentRouteScore = 0;
                int timeElapsed = 0; 
                var currentPos = state.PlayerPos;
                V firstTarget = null;
                var isFirstOvenCookingTarget = false;

                for (var i = 0; i < perm.Count; i++)
                {
                    var needed = perm[i];
                    var (targetPos, penalty, isCooking) = GetItemTarget(needed, state, init);

                    // Для первого шага - реальное расстояние радаром. Для остальных - Манхэттен.
                    var dist = (i == 0) ? MyDist(targetPos) : MDist(currentPos, targetPos);
                    
                    timeElapsed += dist + 1; 
                    currentRouteScore += dist * 10;

                    if (i == 0)
                    {
                        currentRouteScore += penalty;
                        firstTarget = targetPos;
                        if (isCooking) isFirstOvenCookingTarget = true;
                    }

                    if (isCooking && targetPos == init.OvenPos)
                    {
                        if (i != 0) currentRouteScore -= penalty; 

                        int arrivalDiff = timeElapsed - state.OvenTimer;
                        if (arrivalDiff < 0)
                        {
                            if (arrivalDiff < -4) currentRouteScore += 1000; // Не стоим слишком долго
                            else currentRouteScore += (-arrivalDiff) * 4; 
                        }
                        else if (arrivalDiff > 2)
                        {
                            currentRouteScore += arrivalDiff * 50; 
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

        if (bestAction is Wait && state.OvenContents != "NONE" && MyDist(init.OvenPos) > 2)
            return CreateAction(new Move(init.OvenPos), state);

        return CreateAction(bestAction, state);
    }

    // Хак для проброса state в CreateAction
    private string stateTempItem = "NONE"; 
    private BotCommand CreateAction(BotCommand cmd, State state)
    {
        stateTempItem = state.PlayerItem;
        return CreateAction(cmd);
    }

    private BotCommand CreateAction(BotCommand cmd)
    {
        if (cmd is Use u) lastActionPos = u.Target;
        else lastActionPos = null;
        
        var currentAction = cmd is Use useCmd ? $"USE {useCmd.Target.X} {useCmd.Target.Y}" :
            cmd is Move moveCmd ? $"MOVE {moveCmd.Destination.X} {moveCmd.Destination.Y}" : "WAIT";

        actionHistory.Add(currentAction);
        if (actionHistory.Count > 4) actionHistory.RemoveAt(0);

        var looping = false;
        if (actionHistory.Count == 4)
        {
            if (actionHistory[0] == actionHistory[2] && actionHistory[1] == actionHistory[3]) looping = true;
            if (actionHistory[1] == actionHistory[2] && actionHistory[2] == actionHistory[3]) looping = true;
        }

        if (looping) loopCounter++;
        else loopCounter = 0;

        if (loopCounter >= 3) 
        {
            loopCounter = 0;
            actionHistory.Clear();
            
            if (init != null)
            {
                if (actionHistory.Count > 0 && actionHistory[0].StartsWith("USE") && stateTempItem != null && stateTempItem.StartsWith("DISH"))
                    return new Use(init.WindowPos); 
            }
            return new Wait(); 
        }

        return cmd;
    }

    private static V DodgePartner(StateInit init, State state)
    {
        if (MDist(state.PlayerPos, state.PartnerPos) > 1 || state.PlayerItem != "NONE") 
            return null;
        if (state.PartnerItem is "RAW_TART" or "DOUGH" && MDist(state.PlayerPos, init.OvenPos) <= 2)
            return init.WindowPos;
        if (state.PartnerItem.StartsWith("DISH") && MDist(state.PlayerPos, init.WindowPos) <= 2)
            return init.DishwasherPos;

        return null;
    }

    private double EvaluateOrder((string Item, int Award) customer, State state, HashSet<string> myIngredients)
    {
        var ingredients = customer.Item.Split('-').ToHashSet();
        var myIngredientsWithoutDish = myIngredients.Where(i => i != "DISH").ToHashSet();
        if (myIngredients.Contains("DISH") && !myIngredientsWithoutDish.IsSubsetOf(ingredients))
            return -100000;

        if (state.PartnerItem.StartsWith("DISH"))
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
                    if (!isAvailable) { allMissingAvailable = false; break; }
                }

                if (allMissingAvailable && state.Customers.Count > 1) 
                    return -50000; 
            }
        }

        var maxIngredientCost = 0;
        var totalCost = 0;
        int missingCount = 0;
    
        foreach (var req in ingredients)
        {
            if (req == "DISH") continue;
            var cost = EstimateIngredientCost(req, state, ingredients);
            if (cost > 0) missingCount++;
            if (cost > maxIngredientCost) maxIngredientCost = cost;
            totalCost += cost;
        }
        
        var estimatedTurns = 3 + maxIngredientCost + missingCount * 2;

        if (estimatedTurns > state.TurnsRemaining + 2)
            return -10000;
        
        double finalScore;
        if (state.TurnsRemaining <= 40)
            finalScore = 1000.0 - estimatedTurns + customer.Award / 10000.0;
        else
            finalScore = (double)customer.Award / Math.Max(1, estimatedTurns);

        // Даем огромный вес текущему заказу, чтобы бот не скакал между ними
        if (customer.Item == currentOrderId) finalScore += 50000;
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
                if (state.TablesWithItems.ContainsValue("STRAWBERRIES") || state.PlayerItem == "STRAWBERRIES" || state.PartnerItem == "STRAWBERRIES") return 4;
                return 7;
            case "CROISSANT":
                if (state.OvenContents == "CROISSANT") return 2;
                if (state.OvenContents == "DOUGH") return state.OvenTimer + 2;
                if (GetTableWithItem("DOUGH", state) != null || state.PlayerItem == "DOUGH" || state.PartnerItem == "DOUGH") return 12;
                return 15;
            case "TART":
                if (state.OvenContents == "TART") return 2;
                if (state.OvenContents == "RAW_TART") return state.OvenTimer + 2;
                if (GetTableWithItem("RAW_TART", state) != null || state.PlayerItem == "RAW_TART" || state.PartnerItem == "RAW_TART") return 12;
                if (GetTableWithItem("CHOPPED_DOUGH", state) != null || state.PlayerItem == "CHOPPED_DOUGH" || state.PartnerItem == "CHOPPED_DOUGH") return 15;
                return 19;
        }
        return 5;
    }

    private static bool IsItemInAnyValidDish(string item, State state, HashSet<string> target) => (from kvp in state.TablesWithItems where kvp.Value.StartsWith("DISH") select kvp.Value.Split('-') into dishItems where dishItems.Contains(item) select dishItems.Where(i => i != "DISH").ToHashSet()).Any(dishSet => dishSet.IsSubsetOf(target));

    private static bool IsItemWithPlayerOrPartner(string item, State state, HashSet<string> target)
    {
        if (state.PlayerItem == item || state.PartnerItem == item) return true;
        var myParts = state.PlayerItem.Split('-');
        if (myParts[0] == "DISH" && myParts.Contains(item))
        {
            var dishItems = myParts.Where(i => i != "DISH").ToHashSet();
            if (dishItems.IsSubsetOf(target)) return true;
        }

        var partnerParts = state.PartnerItem.Split('-');
        if (partnerParts[0] != "DISH" || !partnerParts.Contains(item)) return false;
        {
            var dishItems = partnerParts.Where(i => i != "DISH").ToHashSet();
            if (dishItems.IsSubsetOf(target)) return true;
        }

        return false;
    }

    private static bool IsItemWithPartner(string item, State state, HashSet<string> target)
    {
        if (state.PartnerItem == item) return true;
        var partnerParts = state.PartnerItem.Split('-');
        if (partnerParts[0] != "DISH" || !partnerParts.Contains(item)) return false;
        var dishItems = partnerParts.Where(i => i != "DISH").ToHashSet();
        return dishItems.IsSubsetOf(target);
    }

    // ИСПРАВЛЕНИЕ: Ищем ближайший пустой стол, не блокирующий проходы
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
                if (preferredTarget != null)
                {
                    score = MyDist(pos) * 2 + MDist(pos, preferredTarget);
                }
                else
                {
                    score = MyDist(pos) * 3 + MDist(pos, init.WindowPos) * 2;
                }

                if (MDist(pos, init.OvenPos) <= 1) score += 30;
                if (MDist(pos, init.WindowPos) <= 1) score += 30;
                if (MDist(pos, init.ChoppingBoardPos) <= 1) score += 20;

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

    // ИСПРАВЛЕНИЕ: Функция поиска предмета теперь возвращает БЛИЖАЙШИЙ через радар!
    private V GetTableWithItem(string item, State state)
    {
        V best = null;
        int minDist = int.MaxValue;
        foreach (var kvp in state.TablesWithItems)
        {
            if (kvp.Value == item && kvp.Key != init.ChoppingBoardPos)
            {
                int d = MyDist(kvp.Key);
                if (d < minDist)
                {
                    minDist = d;
                    best = kvp.Key;
                }
            }
        }
        return best;
    }

    private static int MDist(V a, V b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    // --- РАДАР (АЛГОРИТМ ВОЛНЫ) ---
    private void ComputeRealDistances(State state)
    {
        BuildWave(state.PlayerPos, myBfs, state.PartnerPos);
        BuildWave(state.PartnerPos, partnerBfs, V.None); 
    }

    private void BuildWave(V start, int[,] map, V avoidPos)
    {
        for (int x = 0; x < 11; x++)
        for (int y = 0; y < 7; y++)
            map[x, y] = 999;

        if (start == null || !start.InRange(11, 7)) return;

        Queue<V> q = new Queue<V>();
        q.Enqueue(start);
        map[start.X, start.Y] = 0;

        while (q.Count > 0)
        {
            var curr = q.Dequeue();
            foreach (var dir in V.Directions4)
            {
                var next = curr + dir;
                if (next.InRange(11, 7) && init.Map[next.X, next.Y] == '.')
                {
                    // Штраф 2 за партнера - обойдет, если есть путь на 1 клетку длиннее
                    int cost = (avoidPos != null && next.Equals(avoidPos)) ? 2 : 1; 
                    if (map[curr.X, curr.Y] + cost < map[next.X, next.Y])
                    {
                        map[next.X, next.Y] = map[curr.X, curr.Y] + cost;
                        q.Enqueue(next);
                    }
                }
            }
        }
    }

    private int MyDist(V target) => RealDist(myBfs, target, statePlayerPos);
    private int PartnerDist(V target) => RealDist(partnerBfs, target, statePartnerPos);
    
    private int RealDist(int[,] wave, V target, V fallbackPos)
    {
        if (target == null || !target.InRange(11, 7)) return 999;
        if (init.Map[target.X, target.Y] == '.') return wave[target.X, target.Y]; 
        
        int minDist = 999;
        foreach (var dir in V.Directions4)
        {
            var adj = target + dir;
            if (adj.InRange(11, 7) && init.Map[adj.X, adj.Y] == '.')
            {
                if (wave[adj.X, adj.Y] < minDist) minDist = wave[adj.X, adj.Y];
            }
        }
        
        return minDist == 999 ? MDist(fallbackPos ?? new V(0,0), target) * 2 : minDist + 1; 
    }

    private static List<List<string>> GetPermutations(List<string> list)
    {
        var result = new List<List<string>>();
        if (list.Count == 0) return result;
        if (list.Count == 1)
        {
            result.Add(new List<string>(list));
            return result;
        }
        for (var i = 0; i < list.Count; i++)
        {
            var current = list[i];
            var remaining = new List<string>(list);
            remaining.RemoveAt(i);
            var remainingPerms = GetPermutations(remaining);
            foreach (var perm in remainingPerms)
            {
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
        var isCooking = false;
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
                if (sPos != null)
                {
                    targetPos = sPos;
                    penalty = -50;
                }
                else if (boardItem == "CHOPPED_STRAWBERRIES" || boardItem == "STRAWBERRIES" ||
                         state.PlayerItem == "STRAWBERRIES")
                {
                    targetPos = init.ChoppingBoardPos;
                    penalty = -50;
                }
                else
                {
                    var rawS = GetTableWithItem("STRAWBERRIES", state);
                    targetPos = rawS ?? init.StrawberriesPos;
                }
                break;
            case "CROISSANT":
                var cPos = GetTableWithItem("CROISSANT", state);
                if (cPos != null)
                {
                    targetPos = cPos;
                    penalty = -50;
                }
                else if (state.OvenContents == "CROISSANT" && (state.PlayerItem == "NONE" || state.PlayerItem.StartsWith("DISH")))
                {
                    targetPos = init.OvenPos;
                    penalty = -1000;
                }
                else if (state.OvenContents == "DOUGH")
                {
                    targetPos = init.OvenPos;
                    penalty = 10000;
                    isCooking = true;
                }
                break;
            case "TART":
                var tPos = GetTableWithItem("TART", state);
                if (tPos != null)
                {
                    targetPos = tPos;
                    penalty = -50;
                }
                else if (state.OvenContents == "TART" && (state.PlayerItem == "NONE" || state.PlayerItem.StartsWith("DISH")))
                {
                    targetPos = init.OvenPos;
                    penalty = -1000;
                }
                else if (state.OvenContents == "RAW_TART")
                {
                    targetPos = init.OvenPos;
                    penalty = 10000;
                    isCooking = true;
                }
                break;
        }

        return (targetPos, penalty, isCooking);
    }
}