namespace bot;

public class Solver
{
    private readonly Random _random = new Random(42);

    public BotCommand GetCommand(State currentState, Countdown countdown)
    {
        double bestScore = double.NegativeInfinity;
        BotCommand bestCommand = new Wait();
        int simCount = 0;
        
        while (countdown.TimeAvailable.TotalMilliseconds > 5)
        {
            simCount++;
            var testState = currentState.Clone();
            double runScore = 0;
            double discount = 1.0; // Штраф за время (заставляет делать всё быстрее)
            BotCommand firstAction = null;
            
            for (int i = 0; i < 6; i++) // Глубина 6 макро-шагов
            {
                if (testState.TurnsRemaining <= 0 || testState.Customers.Count == 0) break;

                var possibleActions = Simulator.GetPossibleCommands(testState);
                if (possibleActions.Count == 0) break;
                
                var action = possibleActions[_random.Next(possibleActions.Count)];
                if (i == 0) firstAction = action;
                
                Simulator.ApplyCommand(testState, action);
                
                runScore += Evaluate(testState) * discount;
                discount *= 0.95; // Каждый следующий шаг ценится чуть меньше
            }
            
            if (runScore > bestScore && firstAction != null)
            {
                bestScore = runScore;
                bestCommand = firstAction;
            }
        }

        Console.Error.WriteLine($"Simulations: {simCount}, Best Score: {bestScore}");
        return bestCommand;
    }

    private double Evaluate(State state)
    {
        double score = state.Score; 
        ItemMask hands = state.PlayerItem;
        double maxH = 0;

        if (state.Customers.Count == 0) return score + 10000;

        foreach (var order in state.Customers)
        {
            ItemMask needed = order.Item;
            double h = 0;

            if (hands == needed)
            {
                h = 5000; // Идеальное совпадение, бежим сдавать!
            }
            else if (hands != ItemMask.None)
            {
                if (hands.HasFlag(ItemMask.Dish))
                {
                    // Если тарелка содержит только нужные клиенту вещи (строгое подмножество)
                    if ((hands & ~needed) == 0)
                    {
                        h = BitOperations.PopCount((uint)hands) * 300;
                    }
                }
                else
                {
                    // Логика для сырых ингредиентов
                    if (needed.HasFlag(hands)) h = 200;
                    if (needed.HasFlag(ItemMask.Croissant) && hands == ItemMask.Dough) h = 150;
                    if (needed.HasFlag(ItemMask.ChoppedStrawberries) && hands == ItemMask.Strawberries) h = 150;
                    
                    if (needed.HasFlag(ItemMask.BlueberryTart)) {
                        if (hands == ItemMask.RawTart) h = 250;
                        if (hands == ItemMask.ChoppedDough) h = 150;
                        if (hands == ItemMask.Dough) h = 50;
                    }
                }
            }

            // Награда за предметы в печи (чтобы он про них не забывал)
            if (state.OvenContents != ItemMask.None)
            {
                if (needed.HasFlag(ItemMask.Croissant)) {
                    if (state.OvenContents == ItemMask.Croissant) h += 400;
                    if (state.OvenContents == ItemMask.Dough) h += 200;
                }
                if (needed.HasFlag(ItemMask.BlueberryTart)) {
                    if (state.OvenContents == ItemMask.BlueberryTart) h += 400;
                    if (state.OvenContents == ItemMask.RawTart) h += 200;
                }
            }

            if (h > maxH) maxH = h;
        }

        // ЖЕСТКИЙ ШТРАФ: Если мы держим тарелку, и она не подходит НИ ОДНОМУ клиенту (maxH == 0)
        // Бот мгновенно бросит её на стол, потому что держать её - себе в убыток
        if (hands.HasFlag(ItemMask.Dish) && maxH == 0)
        {
            return score - 2000;
        }

        return score + maxH;
    }
}