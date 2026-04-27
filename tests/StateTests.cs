using System;
using System.Diagnostics;
using NUnit.Framework;

namespace bot;

[TestFixture]
public class StateTests
{
    private Solver _solver;
    
    private const string InitData = "20|DISH-BLUEBERRIES-ICE_CREAM 650|DISH-ICE_CREAM-TART 1400|DISH-TART-ICE_CREAM 1400|DISH-BLUEBERRIES-CHOPPED_STRAWBERRIES 850|DISH-TART-CHOPPED_STRAWBERRIES 1600|DISH-CROISSANT-BLUEBERRIES 1100|DISH-TART-CHOPPED_STRAWBERRIES 1600|DISH-CROISSANT-ICE_CREAM 1050|DISH-BLUEBERRIES-CHOPPED_STRAWBERRIES 850|DISH-CHOPPED_STRAWBERRIES-BLUEBERRIES-TART-ICE_CREAM 2050|DISH-CROISSANT-CHOPPED_STRAWBERRIES 1250|DISH-CROISSANT-CHOPPED_STRAWBERRIES 1250|DISH-CHOPPED_STRAWBERRIES-TART-ICE_CREAM-BLUEBERRIES 2050|DISH-CHOPPED_STRAWBERRIES-BLUEBERRIES-ICE_CREAM 1050|DISH-TART-ICE_CREAM-CROISSANT 2050|DISH-TART-CHOPPED_STRAWBERRIES-ICE_CREAM-CROISSANT 2450|DISH-TART-ICE_CREAM 1400|DISH-CHOPPED_STRAWBERRIES-ICE_CREAM 800|DISH-TART-CHOPPED_STRAWBERRIES 1600|DISH-CROISSANT-ICE_CREAM 1050|#####D#####|O.........#|#.####1B#.#|#.#.0#..#.#|I.##.####.H|#.........#|C####W###S#";

    // Массив ходов из логов (замените на реальную серию ходов из вашей игры)
    // Здесь показаны 3 последовательных хода
    private readonly string[] _gameFrames = {
        "199|4 3 NONE|6 2 NONE|0|NONE 0|3|DISH-BLUEBERRIES-ICE_CREAM 650|DISH-ICE_CREAM-TART 1400|DISH-TART-ICE_CREAM 1400",
        "197|2 5 NONE|9 1 NONE|0|NONE 0|3|DISH-BLUEBERRIES-ICE_CREAM 648|DISH-ICE_CREAM-TART 1398|DISH-TART-ICE_CREAM 1398",
        "195|1 2 NONE|9 3 NONE|0|NONE 0|3|DISH-BLUEBERRIES-ICE_CREAM 646|DISH-ICE_CREAM-TART 1396|DISH-TART-ICE_CREAM 1396"
    };

    [SetUp]
    public void Setup()
    {
        _solver = new Solver();
        _solver.GetCommand(CreateDummyState(), new Countdown(TimeSpan.FromMilliseconds(1)));
    }
        
    /*
     * Как отлаживать алгоритм:
     *
     * ConsoleReader после каждого хода пишет в отладочный вывод весь ввод, в котором для удобства
     * переводы строк заменены на "|". Получается одна строка, которую удобно скопировать из интерфейса CG
     * и вставить в этот тест. Аналогично поступить с инизиализационными данными, которые вводятся до первого хода.
     *
     * Если в интерфейсе CG видно, как ваш алгоритм делает странный ход, можно быстро скопировать входные данные,
     * вставить в этот тест, и тем самым повторить проблему в контролируемых условиях.
     * Дальше можно отлаживать проблему привычными способами в IDE.
     */
    [TestCase("Some|init|data", "Some input|copy pasted from|error stream")]
    public void Solve(string initInput, string stepInput)
    {
        var initReader = new ConsoleReader(InitData);
        var init = initReader.ReadInit();
        var solver = new Solver();
            
        Console.WriteLine("Starting Game Replay...");
        Console.WriteLine(new string('-', 40));
        
        for (var i = 0; i < _gameFrames.Length; i++)
        {
            var frameReader = new ConsoleReader(_gameFrames[i]);
            var state = frameReader.ReadState(init);

            var sw = Stopwatch.StartNew();
            var move = solver.GetCommand(state, new Countdown(TimeSpan.FromMilliseconds(50)));
                
            sw.Stop();
            Console.WriteLine($"Turn {i + 1}:");
            Console.WriteLine($"  Decision : {move}");
            Console.WriteLine($"  Time     : {sw.ElapsedMilliseconds} ms");
            Assert.IsNotNull(move, $"Бот вернул null на ходу {i + 1}");
            Assert.Less(sw.ElapsedMilliseconds, i == 0 ? 500 : 50, $"Бот превысил лимит времени на ходу {i + 1}!");
        }
            
        Console.WriteLine(new string('-', 40));
        Console.WriteLine("Replay finished successfully.");
    }
        
    [TestCase(0, 0, 1, 0)]
    [TestCase(5, 3, 5, 2)]
    public void SimulationRule_Player_CombinesBlueberriesWithDish(int px, int py, int tx, int ty)
    {
        var simState = new Solver.SimState {
            PlayerItem = Solver.DISH,
            Px = px, Py = py,
            Tables = new int[77]
        };
            
        SetMapChar(tx, ty, 'B');

        var action = new Solver.MacroAction { 
            TargetX = tx, TargetY = ty, 
            EndPx = px, EndPy = py, 
            Type = Solver.CmdType.Use, 
            Cost = 1 
        };

        _solver.ApplyAction(simState, action);

        Assert.AreEqual(Solver.DISH | Solver.BLUE, simState.PlayerItem, 
            $"Игрок должен был добавить чернику в тарелку с ящика на координатах ({tx}, {ty}).");
    }
        
    [TestCase(3, 1, 3, 0)]
    [TestCase(10, 3, 10, 4)]
    [TestCase(0, 5, 0, 6)]
    public void SimulationRule_ChoppingBoard_ChopsStrawberries(int px, int py, int tx, int ty)
    {
        var simState = new Solver.SimState {
            PlayerItem = Solver.STRAW,
            Px = px, Py = py,
            Tables = new int[77]
        };
            
        var tableIdx = tx + ty * 11;
        simState.Tables[tableIdx] = Solver.NONE; 
        SetMapChar(tx, ty, 'C');

        var action = new Solver.MacroAction { 
            TargetX = tx, TargetY = ty, 
            EndPx = px, EndPy = py, 
            Type = Solver.CmdType.Use, 
            Cost = 1 
        };

        _solver.ApplyAction(simState, action);

        Assert.AreEqual(Solver.NONE, simState.PlayerItem, "Игрок должен отпустить клубнику из рук.");
        Assert.AreEqual(Solver.CHOPPED_STRAW, simState.Tables[tableIdx], 
            $"На столе ({tx}, {ty}) с индексом {tableIdx} должна лежать нарезанная клубника.");
    }
        
    [TestCase(9, 0, 10, 0)]
    public void SimulationRule_Window_DeliversCorrectOrderAndGivesScore(int px, int py, int tx, int ty)
    {
        var mockState = CreateDummyState();
        mockState.Customers.Add(("DISH-ICE_CREAM", 1500));
        _solver.GetCommand(mockState, new Countdown(TimeSpan.FromMilliseconds(1))); 

        var simState = new Solver.SimState {
            PlayerItem = Solver.DISH | Solver.ICE,
            Px = px, Py = py,
            Score = 0
        };
            
        SetMapChar(tx, ty, 'W');
            
        var action = new Solver.MacroAction { 
            TargetX = tx, TargetY = ty, 
            EndPx = px, EndPy = py, 
            Type = Solver.CmdType.Use, 
            Cost = 1 
        };

        _solver.ApplyAction(simState, action);

        Assert.AreEqual(Solver.NONE, simState.PlayerItem, "После сдачи заказа руки должны быть пусты.");
        Assert.AreEqual(100000 + 1500 - 10, simState.Score, 
            $"Заказ должен быть принят окном на ({tx}, {ty}) и очки зачислены с учетом штрафа за ход (-10).");
    }

    [TestCase(Solver.DOUGH, 3, Solver.CROISSANT)] 
    [TestCase(Solver.RAW_TART, 5, Solver.TART)]  
    public void SimulationRule_Oven_BakesRawFoodCorrectly(int inputFood, int waitTurns, int expectedFood)
    {
        var simState = new Solver.SimState {
            OvenContents = inputFood,
            OvenTimer = waitTurns, 
            TurnsRemaining = 100
        };

        var waitAction = new Solver.MacroAction { Cost = waitTurns, Type = Solver.CmdType.Wait };
        _solver.ApplyAction(simState, waitAction);

        Assert.AreEqual(expectedFood, simState.OvenContents, "Еда в духовке должна испечься в правильный продукт.");
        Assert.AreEqual(10, simState.OvenTimer, "После запекания таймер должен сброситься на 10 для сгорания.");
    }

    [Test]
    public void SimulationRule_Oven_BurnsFoodAppliesPenalty()
    {
        var simState = new Solver.SimState {
            OvenContents = Solver.CROISSANT,
            OvenTimer = 1,
            Score = 0
        };

        var waitAction = new Solver.MacroAction { Cost = 1, Type = Solver.CmdType.Wait };
        _solver.ApplyAction(simState, waitAction);

        Assert.AreEqual(Solver.NONE, simState.OvenContents, "Духовка должна очиститься после сгорания еды.");
        Assert.Less(simState.Score, -1000, "Должен быть начислен штраф за сгоревшую еду.");
    }
    
    [Test]
    public void SimulationRule_Table_DropItemOnEmptyTable()
    {
        var simState = new Solver.SimState {
            PlayerItem = Solver.CROISSANT,
            Tables = new int[77],
            Px = 0, Py = 0
        };
        
        SetMapChar(1, 0, '#');

        var action = new Solver.MacroAction { TargetX = 1, TargetY = 0, Type = Solver.CmdType.Use, Cost = 1 };
        _solver.ApplyAction(simState, action);

        Assert.AreEqual(Solver.NONE, simState.PlayerItem, "Игрок должен освободить руки.");
        Assert.AreEqual(Solver.CROISSANT, simState.Tables[1 + 0 * 11], "Круассан должен остаться на столе.");
    }

    [Test]
    public void SimulationRule_Table_PickUpItemFromTable()
    {
        var simState = new Solver.SimState {
            PlayerItem = Solver.NONE,
            Tables = new int[77],
            Px = 0, Py = 0
        };
        
        const int tableIdx = 1 + 0 * 11;
        simState.Tables[tableIdx] = Solver.TART;
        SetMapChar(1, 0, '#');

        var action = new Solver.MacroAction { TargetX = 1, TargetY = 0, Type = Solver.CmdType.Use, Cost = 1 };
        _solver.ApplyAction(simState, action);

        Assert.AreEqual(Solver.TART, simState.PlayerItem, "Игрок должен забрать Тарт со стола.");
        Assert.AreEqual(Solver.NONE, simState.Tables[tableIdx], "Стол должен стать пустым.");
    }

    [Test]
    public void SimulationRule_Combine_BlueberriesAndChoppedDough_MakesRawTart()
    {
        var simState = new Solver.SimState {
            PlayerItem = Solver.BLUE,
            Tables = new int[77]
        };
        
        const int tableIdx = 1 + 0 * 11;
        simState.Tables[tableIdx] = Solver.CHOPPED_DOUGH;
        SetMapChar(1, 0, '#'); 

        var action = new Solver.MacroAction { TargetX = 1, TargetY = 0, Type = Solver.CmdType.Use, Cost = 1 };
        _solver.ApplyAction(simState, action);

        Assert.AreEqual(Solver.NONE, simState.PlayerItem, "Руки игрока должны опустеть.");
        Assert.AreEqual(Solver.RAW_TART, simState.Tables[tableIdx], "На столе должен появиться Сырой Тарт (RAW_TART).");
    }

    [Test]
    public void SimulationRule_Combine_AddIngredientToExistingDishOnTable()
    {
        var simState = new Solver.SimState {
            PlayerItem = Solver.ICE,
            Tables = new int[77]
        };
        
        const int tableIdx = 1 + 0 * 11;
        simState.Tables[tableIdx] = Solver.DISH | Solver.BLUE;
        SetMapChar(1, 0, '#'); 

        var action = new Solver.MacroAction { TargetX = 1, TargetY = 0, Type = Solver.CmdType.Use, Cost = 1 };
        _solver.ApplyAction(simState, action);

        Assert.AreEqual(Solver.NONE, simState.PlayerItem, "Игрок кладет мороженое.");
        Assert.AreEqual(Solver.DISH | Solver.BLUE | Solver.ICE, simState.Tables[tableIdx], 
            "На столе должна оказаться тарелка с черникой И мороженым.");
    }

    [Test]
    public void SimulationRule_Dishwasher_TakesNewDish()
    {
        var simState = new Solver.SimState {
            PlayerItem = Solver.NONE
        };
        SetMapChar(1, 0, 'D');

        var action = new Solver.MacroAction { TargetX = 1, TargetY = 0, Type = Solver.CmdType.Use, Cost = 1 };
        _solver.ApplyAction(simState, action);

        Assert.AreEqual(Solver.DISH, simState.PlayerItem, "Игрок должен взять новую тарелку из посудомоечной машины.");
    }

    [Test]
    public void SimulationRule_Oven_TakeOutCookedFood()
    {
        var simState = new Solver.SimState {
            PlayerItem = Solver.NONE,
            OvenContents = Solver.CROISSANT,
            OvenTimer = 5
        };
        SetMapChar(1, 0, 'O');

        var action = new Solver.MacroAction { TargetX = 1, TargetY = 0, Type = Solver.CmdType.Use, Cost = 1 };
        _solver.ApplyAction(simState, action);

        Assert.AreEqual(Solver.CROISSANT, simState.PlayerItem, "Игрок должен достать круассан из духовки.");
        Assert.AreEqual(Solver.NONE, simState.OvenContents, "Духовка должна стать пустой.");
        Assert.AreEqual(0, simState.OvenTimer, "Таймер духовки должен сброситься.");
    }

    [Test]
    public void SimulationRule_Oven_AddDirectlyToDish()
    {
        var simState = new Solver.SimState {
            PlayerItem = Solver.DISH | Solver.ICE,
            OvenContents = Solver.TART,
            OvenTimer = 8
        };
        SetMapChar(1, 0, 'O');

        var action = new Solver.MacroAction { TargetX = 1, TargetY = 0, Type = Solver.CmdType.Use, Cost = 1 };
        _solver.ApplyAction(simState, action);

        Assert.AreEqual(Solver.DISH | Solver.ICE | Solver.TART, simState.PlayerItem, 
            "Тарт должен переместиться из духовки прямо на тарелку в руках игрока.");
        Assert.AreEqual(Solver.NONE, simState.OvenContents, "Духовка должна опустеть.");
    }
        

    private void SetMapChar(int x, int y, char c)
    {
        var mapField = typeof(Solver).GetField("Map", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var map = (char[,])mapField.GetValue(_solver);
            
        for (var i=0; i<11; i++) 
        for (var j=0; j<7; j++) 
            if (map[i,j] == c) map[i,j] = '#'; 

        map[x, y] = c;
    }

    private static State CreateDummyState()
    {
        var init = new StateInit { NumAllCustomers = 1, Map = new char[11, 7] };
        for (var x=0; x<11; x++) for (var y=0; y<7; y++) init.Map[x,y] = '.';
            
        return new State {
            Init = init,
            TurnsRemaining = 200,
            PlayerPos = new V(0, 0),
            PartnerPos = new V(10, 6)
        };
    }
}