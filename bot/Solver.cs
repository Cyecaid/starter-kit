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

        var bestCustomer = GetBestCustomer(state);
        
        // Целевые ингредиенты (без слова DISH)
        targetIngredients = bestCustomer.Item.Split('-').Where(i => i != "DISH").ToHashSet();

        var myItems = state.PlayerItem.StartsWith("DISH")
            ? state.PlayerItem.Split('-').Where(i => i != "DISH").ToHashSet()
            : new HashSet<string>();

        bool hasDish = state.PlayerItem.StartsWith("DISH");
        bool emptyHanded = state.PlayerItem == "NONE";

        // 1. Если тарелка собрана полностью - несем в окно
        if (hasDish && targetIngredients.IsSubsetOf(myItems))
            return new Use(init.WindowPos);

        // 2. ВЫСШИЙ ПРИОРИТЕТ: Спасаем еду из духовки, чтобы она не сгорела!
        if (state.OvenContents == "CROISSANT" || state.OvenContents == "TART")
        {
            if (emptyHanded) return new Use(init.OvenPos); // Достаем пустыми руками
            
            // Если в руках тарелка и нам нужен этот ингредиент - забираем сразу на тарелку
            if (hasDish && targetIngredients.Contains(state.OvenContents) && !myItems.Contains(state.OvenContents))
                return new Use(init.OvenPos); 
        }

        // 3. Обрабатываем промежуточные ингредиенты в руках (готовка)
        if (!hasDish && !emptyHanded)
        {
            if (state.PlayerItem == "STRAWBERRIES") 
                return new Use(init.ChoppingBoardPos);
                
            if (state.PlayerItem == "DOUGH")
            {
                // Если нужен и пирог, и круассан - отдаем приоритет пирогу (он дольше готовится)
                if (targetIngredients.Contains("TART") && !myItems.Contains("TART") && !IsItemReadyOrCooking("TART", state))
                    return new Use(init.ChoppingBoardPos);
                    
                if (state.OvenContents == "NONE") 
                    return new Use(init.OvenPos);
            }

            if (state.PlayerItem == "CHOPPED_DOUGH") 
                return new Use(init.BlueberriesPos); // CHOPPED_DOUGH + B = RAW_TART (Магия слияния!)

            if (state.PlayerItem == "RAW_TART")
                return state.OvenContents == "NONE" ? new Use(init.OvenPos) : new Use(GetClosestEmptyTable(state));

            // Если держим готовую еду без тарелки - сбрасываем на ближайший стол
            return new Use(GetClosestEmptyTable(state));
        }

        // 4. Подготовка сложных ингредиентов (если руки пусты)
        var needsTart = targetIngredients.Contains("TART") && !myItems.Contains("TART");
        var needsCroissant = targetIngredients.Contains("CROISSANT") && !myItems.Contains("CROISSANT");
        var needsStrawberries = targetIngredients.Contains("CHOPPED_STRAWBERRIES") && !myItems.Contains("CHOPPED_STRAWBERRIES");

        if (emptyHanded)
        {
            // Готовим TART
            if (needsTart && !IsItemReadyOrCooking("TART", state))
            {
                var rawTart = GetTableWithItem("RAW_TART", state);
                if (rawTart != null) return new Use(rawTart);

                var choppedDough = GetTableWithItem("CHOPPED_DOUGH", state);
                if (choppedDough != null) return new Use(choppedDough);

                var dough = GetTableWithItem("DOUGH", state);
                if (dough != null) return new Use(dough);

                return new Use(init.DoughPos); // Берем тесто (H)
            }

            // Готовим CROISSANT
            if (needsCroissant && !IsItemReadyOrCooking("CROISSANT", state))
            {
                var dough = GetTableWithItem("DOUGH", state);
                if (dough != null) return new Use(dough);

                return new Use(init.DoughPos);
            }

            // Готовим CHOPPED_STRAWBERRIES
            if (needsStrawberries && !IsItemReadyOrCooking("CHOPPED_STRAWBERRIES", state))
            {
                var straw = GetTableWithItem("STRAWBERRIES", state);
                if (straw != null) return new Use(straw);

                return new Use(init.StrawberriesPos);
            }
        }

        // 5. Если есть тарелка - идем за БЛИЖАЙШИМ недостающим ингредиентом
        if (hasDish)
        {
            var missing = targetIngredients.Except(myItems).ToList();
            if (missing.Count > 0)
            {
                var closestTarget = GetClosestIngredientSource(missing, state);
                if (closestTarget != null)
                    return new Use(closestTarget);
            }
        }

        // 6. Берем тарелку (если руки пусты, а сложная еда готовится)
        if (emptyHanded)
        {
            var bestDishOnTable = FindBestDishOnTable(state, targetIngredients);
            if (bestDishOnTable != null)
                return new Use(bestDishOnTable);

            return new Use(init.DishwasherPos);
        }

        // Фоллбэк (если застряли)
        return new Use(GetClosestEmptyTable(state));
    }

    // Проверяем, не делает ли уже этот предмет наш напарник (или предмет уже есть на карте)
    private bool IsItemReadyOrCooking(string item, State state)
    {
        if (GetTableWithItem(item, state) != null) return true;
        if (state.PartnerItem.Contains(item)) return true;

        if (item == "TART")
        {
            return state.OvenContents == "TART" || state.OvenContents == "RAW_TART" ||
                   GetTableWithItem("RAW_TART", state) != null || GetTableWithItem("CHOPPED_DOUGH", state) != null ||
                   state.PartnerItem == "RAW_TART" || state.PartnerItem == "CHOPPED_DOUGH";
        }
        if (item == "CROISSANT")
        {
            return state.OvenContents == "CROISSANT" || state.OvenContents == "DOUGH";
        }
        if (item == "CHOPPED_STRAWBERRIES")
        {
            return state.PartnerItem == "STRAWBERRIES";
        }
        return false;
    }

    private (string Item, int Award) GetBestCustomer(State state)
    {
        var bestOrder = state.Customers[0];
        int bestScore = -1000;

        foreach (var customer in state.Customers)
        {
            var reqs = customer.Item.Split('-').Where(i => i != "DISH").ToHashSet();
            int score = customer.Award;

            foreach (var req in reqs)
            {
                if (req == "ICE_CREAM" || req == "BLUEBERRIES") score -= 1; 
                else if (req == "CROISSANT" || req == "CHOPPED_STRAWBERRIES") score -= 3;
                else if (req == "TART") score -= 5; // TART требует больше всего шагов

                if (GetTableWithItem(req, state) != null) score += 3;
                if ((req == "CROISSANT" || req == "TART") && state.OvenContents == req) score += 4;
            }

            if (FindBestDishOnTable(state, reqs) != null) score += 5;

            if (score > bestScore)
            {
                bestScore = score;
                bestOrder = customer;
            }
        }

        return bestOrder;
    }

    private V GetClosestIngredientSource(List<string> missing, State state)
    {
        V bestPos = null;
        int minDist = int.MaxValue;

        foreach (var req in missing)
        {
            V pos = null;
            if (req == "ICE_CREAM") pos = init.IceCreamPos;
            else if (req == "BLUEBERRIES") pos = init.BlueberriesPos;
            else if (req == "CROISSANT" || req == "TART" || req == "CHOPPED_STRAWBERRIES")
            {
                pos = GetTableWithItem(req, state);
                if (pos == null && (req == "CROISSANT" || req == "TART") && state.OvenContents == req) 
                    pos = init.OvenPos;
            }

            if (pos != null)
            {
                int dist = state.PlayerPos.MDistTo(pos);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestPos = pos;
                }
            }
        }

        return bestPos;
    }

    private V FindBestDishOnTable(State state, HashSet<string> targetReqs)
    {
        V bestPos = null;
        int maxMatch = -1;

        foreach (var kvp in state.TablesWithItems)
        {
            if (kvp.Value.StartsWith("DISH"))
            {
                var dishItems = kvp.Value.Split('-').Where(i => i != "DISH").ToHashSet();

                // Важно: берем тарелку только если на ней нет лишних ингредиентов
                if (dishItems.IsSubsetOf(targetReqs))
                {
                    if (dishItems.Count > maxMatch)
                    {
                        maxMatch = dishItems.Count;
                        bestPos = kvp.Key;
                    }
                }
            }
        }

        return bestPos;
    }

    private V GetClosestEmptyTable(State state)
    {
        V bestPos = init.DishwasherPos;
        int minDist = int.MaxValue;

        for (var y = 0; y < 7; y++)
        {
            for (var x = 0; x < 11; x++)
            {
                var pos = new V(x, y);
                if (init.Map[x, y] == '#' && !state.TablesWithItems.ContainsKey(pos))
                {
                    int dist = state.PlayerPos.MDistTo(pos);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        bestPos = pos;
                    }
                }
            }
        }
        return bestPos;
    }

    private V GetTableWithItem(string item, State state)
    {
        foreach (var kvp in state.TablesWithItems)
            if (kvp.Value == item && kvp.Key != init.ChoppingBoardPos)
                return kvp.Key;
        return null;
    }
}