namespace bot;

public static class StateReader
{
    public static State ReadState(this ConsoleReader reader)
    {
        var init = reader.ReadInit();
        return reader.ReadState(init);
    }

    public static State ReadState(this ConsoleReader Console, StateInit init)
    {
        var state = new State { Init = init };
        
        state.TurnsRemaining = int.Parse(Console.ReadLine());
        
        var inputs = Console.ReadLine().Split(' ');
        state.PlayerPos = new V(int.Parse(inputs[0]), int.Parse(inputs[1]));
        state.PlayerItem = inputs[2];
        
        inputs = Console.ReadLine().Split(' ');
        state.PartnerPos = new V(int.Parse(inputs[0]), int.Parse(inputs[1]));
        state.PartnerItem = inputs[2];
        
        var numTables = int.Parse(Console.ReadLine());
        for (var i = 0; i < numTables; i++)
        {
            inputs = Console.ReadLine().Split(' ');
            var pos = new V(int.Parse(inputs[0]), int.Parse(inputs[1]));
            state.TablesWithItems[pos] = inputs[2];
        }
        
        inputs = Console.ReadLine().Split(' ');
        state.OvenContents = inputs[0];
        state.OvenTimer = int.Parse(inputs[1]);
        
        var numCustomers = int.Parse(Console.ReadLine());
        for (var i = 0; i < numCustomers; i++)
        {
            inputs = Console.ReadLine().Split(' ');
            state.Customers.Add((inputs[0], int.Parse(inputs[1])));
        }

        return state;
    }
    
    public static StateInit ReadInit(this ConsoleReader Console)
    {
        var init = new StateInit();
        init.NumAllCustomers = int.Parse(Console.ReadLine());
        
        for (var i = 0; i < init.NumAllCustomers; i++)
        {
            var inputs = Console.ReadLine().Split(' ');
        }

        for (var y = 0; y < 7; y++)
        {
            var line = Console.ReadLine();
            for (var x = 0; x < 11; x++)
            {
                var c = line[x];
                init.Map[x, y] = c;

                switch (c)
                {
                    case 'D':
                        init.DishwasherPos = new V(x, y);
                        break;
                    case 'W':
                        init.WindowPos = new V(x, y);
                        break;
                    case 'B':
                        init.BlueberriesPos = new V(x, y);
                        break;
                    case 'I':
                        init.IceCreamPos = new V(x, y);
                        break;
                    case 'S':
                        init.StrawberriesPos = new V(x, y);
                        break;
                    case 'C':
                        init.ChoppingBoardPos = new V(x, y);
                        break;
                    case 'H':
                        init.DoughPos = new V(x, y);
                        break;
                    case 'O':
                        init.OvenPos = new V(x, y);
                        break;
                }
            }
        }

        return init;
    }
}