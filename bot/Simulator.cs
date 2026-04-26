namespace bot;

public static class Simulator
{
    public static List<BotCommand> GetPossibleCommands(State state)
    {
        var commands = new List<BotCommand>();
        var init = state.Init;
        var hands = state.PlayerItem;

        void TryInteract(V target, bool canUse)
        {
            if (target != V.None && canUse)
                commands.Add(new Use(target));
        }

        TryInteract(init.DishwasherPos, hands == ItemMask.None);
        TryInteract(init.WindowPos, hands != ItemMask.None);
        TryInteract(init.ChoppingBoardPos, hands == ItemMask.Strawberries || hands == ItemMask.Dough);

        bool canAddTier1 = (hands == ItemMask.None);
        TryInteract(init.DoughPos, canAddTier1);
        TryInteract(init.StrawberriesPos, canAddTier1);

        // ПЕРЕДАЕМ СПИСОК КЛИЕНТОВ ДЛЯ СТРОГОЙ ВАЛИДАЦИИ
        TryInteract(init.BlueberriesPos, CanAddTier2(ItemMask.Blueberries, hands, state.Customers));
        TryInteract(init.IceCreamPos, CanAddTier2(ItemMask.IceCream, hands, state.Customers));

        bool canUseOven = false;
        if (state.OvenContents == ItemMask.None) canUseOven = hands.IsWarmable();
        else if (!state.OvenContents.IsWarmable()) canUseOven = hands == ItemMask.None || CanAddTier2(state.OvenContents, hands, state.Customers);
        
        TryInteract(init.OvenPos, canUseOven);

        foreach (var tableKvp in state.TablesWithItems)
        {
            if (tableKvp.Value.HasFlag(ItemMask.Dish)) TryInteract(tableKvp.Key, hands == ItemMask.None);
            else TryInteract(tableKvp.Key, CanAddTier2(tableKvp.Value, hands, state.Customers));
        }

        // Бросить предмет
        if (hands != ItemMask.None)
        {
            int dropped = 0;
            foreach (var tablePos in init.EmptyTables)
            {
                if (!state.TablesWithItems.ContainsKey(tablePos))
                {
                    TryInteract(tablePos, true);
                    if (++dropped >= 2) break; // Ограничиваем вариативность бросания, чтобы не раздувать дерево
                }
            }
        }

        if (commands.Count == 0) commands.Add(new Wait());
        return commands;
    }

    private static bool CanAddTier2(ItemMask item, ItemMask hands, List<(ItemMask Item, int Award)> customers)
    {
        if (hands == ItemMask.None) return true;
        
        // Рецепт: нарезанное тесто + черника
        if (hands == ItemMask.ChoppedDough && item == ItemMask.Blueberries) return true;
        
        if (hands.HasFlag(ItemMask.Dish))
        {
            if (hands.HasFlag(item) || BitOperations.PopCount((uint)hands) >= 5 || !item.CanPutOnDish())
                return false;

            // СТРОГАЯ ВАЛИДАЦИЯ: Захочет ли хоть один клиент тарелку с новым ингредиентом?
            ItemMask futureDish = hands | item;
            foreach (var order in customers)
            {
                ItemMask needed = order.Item;
                // Если наша будущая тарелка является СТРОГИМ ПОДМНОЖЕСТВОМ того, что нужно клиенту
                if ((futureDish & ~needed) == 0)
                {
                    return true; // Эта комбинация валидна!
                }
            }
            return false; // Ни один клиент не хочет эту комбинацию! Запрещаем ход.
        }
        return false;
    }

    public static void ApplyCommand(State state, BotCommand command)
    {
        int timePassed = 1;
        
        if (command is Use useCmd)
        {
            var target = useCmd.Target;
            int dist = state.PlayerPos.MDistTo(target);
            timePassed = dist + 1; // Ходьба + действие

            state.PlayerPos = target; 
            
            if (target == state.Init.ChoppingBoardPos)
            {
                if (state.PlayerItem == ItemMask.Dough) state.PlayerItem = ItemMask.ChoppedDough;
                else if (state.PlayerItem == ItemMask.Strawberries) state.PlayerItem = ItemMask.ChoppedStrawberries;
            }
            else if (target == state.Init.OvenPos)
            {
                if (state.OvenContents != ItemMask.None && !state.OvenContents.IsWarmable())
                {
                    HandlePickup(state, state.OvenContents);
                    state.OvenContents = ItemMask.None;
                    state.OvenTimer = 0;
                }
                else if (state.PlayerItem.IsWarmable())
                {
                    state.OvenContents = state.PlayerItem;
                    state.PlayerItem = ItemMask.None;
                    state.OvenTimer = 10;
                }
            }
            else if (target == state.Init.WindowPos)
            {
                bool delivered = false;
                for (int i = 0; i < state.Customers.Count; i++)
                {
                    if (state.Customers[i].Item == state.PlayerItem)
                    {
                        state.Score += state.Customers[i].Award * 1000; 
                        state.Customers.RemoveAt(i);
                        delivered = true;
                        break;
                    }
                }
                if (delivered) state.PlayerItem = ItemMask.None; // Руки очищаются только при успешной сдаче
            }
            else if (target == state.Init.DishwasherPos) HandlePickup(state, ItemMask.Dish);
            else if (target == state.Init.BlueberriesPos) HandlePickup(state, ItemMask.Blueberries);
            else if (target == state.Init.IceCreamPos) HandlePickup(state, ItemMask.IceCream);
            else if (target == state.Init.StrawberriesPos) HandlePickup(state, ItemMask.Strawberries);
            else if (target == state.Init.DoughPos) HandlePickup(state, ItemMask.Dough);
            else if (state.TablesWithItems.ContainsKey(target))
            {
                HandlePickup(state, state.TablesWithItems[target]);
                state.TablesWithItems.Remove(target);
            }
            else 
            {
                if (state.PlayerItem != ItemMask.None)
                {
                    state.TablesWithItems[target] = state.PlayerItem;
                    state.PlayerItem = ItemMask.None;
                }
            }
        }

        state.TurnsRemaining -= timePassed;
        UpdateOven(state, timePassed);
    }

    private static void HandlePickup(State state, ItemMask itemToPick)
    {
        if (state.PlayerItem == ItemMask.ChoppedDough && itemToPick == ItemMask.Blueberries) state.PlayerItem = ItemMask.RawTart;
        else state.PlayerItem |= itemToPick;
    }

    private static void UpdateOven(State state, int turnsPassed)
    {
        if (state.OvenContents == ItemMask.None) return;

        for (int i = 0; i < turnsPassed; i++)
        {
            if (state.OvenTimer > 0) state.OvenTimer--;
            if (state.OvenTimer == 0)
            {
                if (state.OvenContents == ItemMask.Dough) { state.OvenContents = ItemMask.Croissant; state.OvenTimer = 10; }
                else if (state.OvenContents == ItemMask.RawTart) { state.OvenContents = ItemMask.BlueberryTart; state.OvenTimer = 10; }
                else { state.OvenContents = ItemMask.None; } // Сгорело
            }
        }
    }
}