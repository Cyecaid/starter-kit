namespace bot;

public class Solver
{
    // Битовые маски для оптимизации
    private const int NONE = 0;
    private const int DISH = 1;
    private const int ICE = 2;
    private const int BLUE = 4;
    private const int CHOPPED_STRAW = 8;
    private const int CROISSANT = 16;
    private const int TART = 32;
    private const int STRAW = 64;
    private const int DOUGH = 128;
    private const int CHOPPED_DOUGH = 256;
    private const int RAW_TART = 512;

    public enum CmdType { Use, Wait }

    public struct MacroAction
    {
        public int TargetX, TargetY;
        public int EndPx, EndPy;
        public CmdType Type;
        public int Cost;
    }

    public class SimState : IComparable<SimState>
    {
        public int Px, Py;
        public int PlayerItem;
        public int[] Tables = new int[77];
        public int OvenContents;
        public int OvenTimer;
        public int Score;
        public int TurnsRemaining;
        public MacroAction FirstAction;
        public int EvalScore;

        public void CopyFrom(SimState o)
        {
            Px = o.Px; Py = o.Py; PlayerItem = o.PlayerItem;
            Array.Copy(o.Tables, Tables, 77);
            OvenContents = o.OvenContents; OvenTimer = o.OvenTimer;
            Score = o.Score; TurnsRemaining = o.TurnsRemaining;
            FirstAction = o.FirstAction;
            EvalScore = o.EvalScore;
        }

        public int CompareTo(SimState other)
        {
            // Сортировка по убыванию очков, чтобы лучшие состояния были в начале списка
            return other.EvalScore.CompareTo(this.EvalScore); 
        }
    }

    private SimState[] Pool;
    private int PoolIndex = 0;
    private List<V> Equipment;
    private char[,] Map;
    private int PartnerX, PartnerY;
    private List<(int Mask, int Award)> ActiveCustomers = new List<(int Mask, int Award)>();

    // Переиспользуемые массивы для предотвращения аллокаций памяти (Zero-GC)
    private int[] bfsDist = new int[77];
    private int[] bfsQx = new int[77];
    private int[] bfsQy = new int[77];
    private int[] evalDishes = new int[80];
    private V[] validTargets = new V[100];
    
    private SimState GetState()
    {
        if (PoolIndex >= Pool.Length) return new SimState();
        return Pool[PoolIndex++];
    }

    private int ParseItem(string s)
    {
        if (s == "NONE" || string.IsNullOrEmpty(s)) return NONE;
        int res = NONE;
        foreach (var p in s.Split('-'))
        {
            if (p == "DISH") res |= DISH;
            else if (p == "ICE_CREAM") res |= ICE;
            else if (p == "BLUEBERRIES") res |= BLUE;
            else if (p == "CHOPPED_STRAWBERRIES") res |= CHOPPED_STRAW;
            else if (p == "CROISSANT") res |= CROISSANT;
            else if (p == "TART") res |= TART;
            else if (p == "STRAWBERRIES") res |= STRAW;
            else if (p == "DOUGH") res |= DOUGH;
            else if (p == "CHOPPED_DOUGH") res |= CHOPPED_DOUGH;
            else if (p == "RAW_TART") res |= RAW_TART;
        }
        return res;
    }

    private int BitCount(int n)
    {
        int count = 0;
        while (n != 0) { count++; n &= (n - 1); }
        return count;
    }

    public BotCommand GetCommand(State state, Countdown countdown)
    {
        if (Pool == null)
        {
            Pool = new SimState[150000];
            for (int i = 0; i < Pool.Length; i++) Pool[i] = new SimState();
        }

        Map = state.Init.Map;
        if (Equipment == null)
        {
            Equipment = new List<V>();
            for (int y = 0; y < 7; y++)
            {
                for (int x = 0; x < 11; x++)
                {
                    char c = Map[x, y];
                    if ("DWBISHCO".Contains(c)) Equipment.Add(new V(x, y));
                }
            }
        }

        PartnerX = state.PartnerPos.X;
        PartnerY = state.PartnerPos.Y;

        ActiveCustomers.Clear();
        foreach (var c in state.Customers) ActiveCustomers.Add((ParseItem(c.Item), c.Award));

        PoolIndex = 0;
        
        SimState root = GetState();
        root.Px = state.PlayerPos.X;
        root.Py = state.PlayerPos.Y;
        root.PlayerItem = ParseItem(state.PlayerItem);
        
        for (int i = 0; i < 77; i++) root.Tables[i] = NONE;
        foreach (var kvp in state.TablesWithItems)
            root.Tables[kvp.Key.X + kvp.Key.Y * 11] = ParseItem(kvp.Value);

        root.OvenContents = ParseItem(state.OvenContents);
        root.OvenTimer = state.OvenTimer;
        root.Score = 0;
        root.TurnsRemaining = state.TurnsRemaining;

        MacroAction bestAction = new MacroAction { Type = CmdType.Wait };
        int bestScore = int.MinValue;

        List<SimState> currentBeam = new List<SimState>(1000) { root };
        List<MacroAction> actionBuffer = new List<MacroAction>(32);
        
        int BEAM_WIDTH = 250; // Оптимальная ширина для C# на 50 мс
        
        for (int depth = 0; depth < 15; depth++)
        {
            // Главная проверка таймаута перед погружением в новый слой поиска
            if (countdown.TimeAvailable.TotalMilliseconds < 10) break;

            List<SimState> nextBeam = new List<SimState>(BEAM_WIDTH * 25);
            int stateCount = 0;

            foreach (var s in currentBeam)
            {
                // Регулярно проверяем таймаут внутри луча каждые 20 симуляций
                if (++stateCount % 20 == 0 && countdown.TimeAvailable.TotalMilliseconds < 5) 
                    break;

                if (s.TurnsRemaining <= 0) continue;

                GenerateActions(s, actionBuffer);

                foreach (var action in actionBuffer)
                {
                    SimState next = GetState();
                    next.CopyFrom(s);
                    ApplyAction(next, action);

                    if (depth == 0) next.FirstAction = action;

                    next.EvalScore = Evaluate(next);
                    nextBeam.Add(next);

                    if (next.EvalScore > bestScore)
                    {
                        bestScore = next.EvalScore;
                        bestAction = next.FirstAction;
                    }
                }
            }

            // Экстренный выход, если прервались по времени внутри цикла
            if (countdown.TimeAvailable.TotalMilliseconds < 5) break;

            if (nextBeam.Count > BEAM_WIDTH)
            {
                nextBeam.Sort(); // Использует IComparable, 0 выделений памяти!
                nextBeam.RemoveRange(BEAM_WIDTH, nextBeam.Count - BEAM_WIDTH);
            }
            currentBeam = nextBeam;
            if (currentBeam.Count == 0) break;
        }

        if (bestAction.Type == CmdType.Wait) return new Wait();
        return new Use(new V(bestAction.TargetX, bestAction.TargetY));
    }

    private void GenerateActions(SimState simState, List<MacroAction> actions)
    {
        actions.Clear();
        for (int i = 0; i < 77; i++) bfsDist[i] = -1;
        int head = 0, tail = 0;

        bfsQx[tail] = simState.Px; bfsQy[tail] = simState.Py; tail++;
        bfsDist[simState.Px + simState.Py * 11] = 0;

        // Скоростной развернутый BFS
        while (head < tail)
        {
            int cx = bfsQx[head], cy = bfsQy[head]; head++;
            int cd = bfsDist[cx + cy * 11];
            
            int nx = cx + 1, ny = cy;
            if (nx < 11 && (Map[nx, ny] == '.' || Map[nx, ny] == '0' || Map[nx, ny] == '1'))
            {
                if ((nx != PartnerX || ny != PartnerY) && bfsDist[nx + ny * 11] == -1)
                {
                    bfsDist[nx + ny * 11] = cd + 1;
                    bfsQx[tail] = nx; bfsQy[tail] = ny; tail++;
                }
            }
            nx = cx - 1; ny = cy;
            if (nx >= 0 && (Map[nx, ny] == '.' || Map[nx, ny] == '0' || Map[nx, ny] == '1'))
            {
                if ((nx != PartnerX || ny != PartnerY) && bfsDist[nx + ny * 11] == -1)
                {
                    bfsDist[nx + ny * 11] = cd + 1;
                    bfsQx[tail] = nx; bfsQy[tail] = ny; tail++;
                }
            }
            nx = cx; ny = cy + 1;
            if (ny < 7 && (Map[nx, ny] == '.' || Map[nx, ny] == '0' || Map[nx, ny] == '1'))
            {
                if ((nx != PartnerX || ny != PartnerY) && bfsDist[nx + ny * 11] == -1)
                {
                    bfsDist[nx + ny * 11] = cd + 1;
                    bfsQx[tail] = nx; bfsQy[tail] = ny; tail++;
                }
            }
            nx = cx; ny = cy - 1;
            if (ny >= 0 && (Map[nx, ny] == '.' || Map[nx, ny] == '0' || Map[nx, ny] == '1'))
            {
                if ((nx != PartnerX || ny != PartnerY) && bfsDist[nx + ny * 11] == -1)
                {
                    bfsDist[nx + ny * 11] = cd + 1;
                    bfsQx[tail] = nx; bfsQy[tail] = ny; tail++;
                }
            }
        }

        int bestEmptyDist1 = 999, bestEmptyDist2 = 999;
        V empty1 = new V(-1, -1), empty2 = new V(-1, -1);
        
        int numTargets = 0;
        for (int i = 0; i < Equipment.Count; i++) validTargets[numTargets++] = Equipment[i];

        for (int i = 0; i < 77; i++)
        {
            int tx = i % 11, ty = i / 11;
            if (Map[tx, ty] == '#')
            {
                if (simState.Tables[i] != NONE) validTargets[numTargets++] = new V(tx, ty);
                else if (simState.PlayerItem != NONE) 
                {
                    int md = GetMinAdjacentDist(tx, ty, bfsDist, out _, out _);
                    if (md != -1)
                    {
                        if (md < bestEmptyDist1)
                        {
                            bestEmptyDist2 = bestEmptyDist1; empty2 = empty1;
                            bestEmptyDist1 = md; empty1 = new V(tx, ty);
                        }
                        else if (md < bestEmptyDist2) { bestEmptyDist2 = md; empty2 = new V(tx, ty); }
                    }
                }
            }
        }
        if (empty1.X != -1) validTargets[numTargets++] = empty1;
        if (empty2.X != -1) validTargets[numTargets++] = empty2;

        for (int i = 0; i < numTargets; i++)
        {
            int tx = validTargets[i].X, ty = validTargets[i].Y;
            char mapChar = Map[tx, ty];
            int tableItem = simState.Tables[tx + ty * 11];
            int pItem = simState.PlayerItem;
            bool useful = false;

            if (mapChar == 'D') useful = (pItem == NONE || (pItem & 1) != 0);
            else if (mapChar == 'W') useful = ((pItem & 1) != 0);
            else if (mapChar == 'B' || mapChar == 'I' || mapChar == 'S' || mapChar == 'H') useful = (pItem == NONE);
            else if (mapChar == 'C')
            {
                if ((pItem == STRAW || pItem == DOUGH) && tableItem == NONE) useful = true;
                else if (pItem == NONE && tableItem != NONE) useful = true;
                else if (pItem != NONE && tableItem == NONE) useful = true;
            }
            else if (mapChar == 'O')
            {
                if ((pItem == DOUGH || pItem == RAW_TART) && simState.OvenContents == NONE) useful = true;
                else if (pItem == NONE || (pItem & 1) != 0)
                {
                    if (simState.OvenContents == CROISSANT || simState.OvenContents == TART) useful = true;
                    if (simState.OvenContents == DOUGH || simState.OvenContents == RAW_TART) useful = true;
                }
            }
            else if (mapChar == '#')
            {
                if (pItem == NONE && tableItem != NONE) useful = true;
                else if (pItem != NONE && tableItem == NONE) useful = true;
                else if ((pItem & 1) != 0 && tableItem != NONE && (tableItem == ICE || tableItem == BLUE || tableItem == CHOPPED_STRAW || tableItem == CROISSANT || tableItem == TART)) useful = true;
                else if ((pItem == BLUE && tableItem == CHOPPED_DOUGH) || (pItem == CHOPPED_DOUGH && tableItem == BLUE)) useful = true;
            }

            if (!useful) continue;

            int md = GetMinAdjacentDist(tx, ty, bfsDist, out int endPx, out int endPy);
            if (md == -1) continue;

            if (mapChar == 'O' && (simState.OvenContents == DOUGH || simState.OvenContents == RAW_TART))
            {
                int travelTime = (md + 3) / 4;
                int waitTime = Math.Max(0, simState.OvenTimer - travelTime);
                actions.Add(new MacroAction {
                    TargetX = tx, TargetY = ty, EndPx = endPx, EndPy = endPy,
                    Type = md == 0 ? CmdType.Wait : CmdType.Use,
                    Cost = travelTime + waitTime + 1
                });
            }
            else
            {
                actions.Add(new MacroAction {
                    TargetX = tx, TargetY = ty, EndPx = endPx, EndPy = endPy,
                    Type = CmdType.Use,
                    Cost = (md + 3) / 4 + 1
                });
            }
        }
    }

    private int GetMinAdjacentDist(int tx, int ty, int[] dist, out int bestNx, out int bestNy)
    {
        int minD = 999; bestNx = -1; bestNy = -1;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = tx + dx, ny = ty + dy;
                if (nx >= 0 && nx < 11 && ny >= 0 && ny < 7)
                {
                    int d = dist[nx + ny * 11];
                    if (d != -1 && d < minD)
                    {
                        minD = d; bestNx = nx; bestNy = ny;
                    }
                }
            }
        }
        return minD == 999 ? -1 : minD;
    }

    private void ApplyAction(SimState simState, MacroAction action)
    {
        int turns = action.Cost;
        simState.TurnsRemaining -= turns;
        simState.Score -= turns * 10; 

        for (int i = 0; i < turns; i++)
        {
            if (simState.OvenTimer > 0)
            {
                simState.OvenTimer--;
                if (simState.OvenTimer == 0)
                {
                    if (simState.OvenContents == DOUGH) { simState.OvenContents = CROISSANT; simState.OvenTimer = 10; }
                    else if (simState.OvenContents == RAW_TART) { simState.OvenContents = TART; simState.OvenTimer = 10; }
                    else if (simState.OvenContents == CROISSANT || simState.OvenContents == TART)
                    {
                        simState.OvenContents = NONE; simState.Score -= 50000; 
                    }
                }
            }
        }

        if (action.Type == CmdType.Wait) return;

        simState.Px = action.EndPx; simState.Py = action.EndPy;

        int tx = action.TargetX, ty = action.TargetY;
        char mapChar = Map[tx, ty];
        int tableIdx = tx + ty * 11;
        int tableItem = simState.Tables[tableIdx];
        int pItem = simState.PlayerItem;

        if (mapChar == 'D')
        {
            if (pItem == NONE || (pItem & 1) != 0) simState.PlayerItem = DISH;
        }
        else if (mapChar == 'W')
        {
            if ((pItem & 1) != 0)
            {
                bool matched = false;
                foreach (var c in ActiveCustomers)
                {
                    if (pItem == c.Mask)
                    {
                        simState.Score += 100000 + c.Award;
                        simState.PlayerItem = NONE;
                        matched = true; break;
                    }
                }
                if (!matched) simState.PlayerItem = NONE;
            }
        }
        else if (mapChar == 'B') { if (pItem == NONE) simState.PlayerItem = BLUE; }
        else if (mapChar == 'I') { if (pItem == NONE) simState.PlayerItem = ICE; }
        else if (mapChar == 'S') { if (pItem == NONE) simState.PlayerItem = STRAW; }
        else if (mapChar == 'H') { if (pItem == NONE) simState.PlayerItem = DOUGH; }
        else if (mapChar == 'C')
        {
            if (pItem == STRAW) { simState.Tables[tableIdx] = CHOPPED_STRAW; simState.PlayerItem = NONE; }
            else if (pItem == DOUGH) { simState.Tables[tableIdx] = CHOPPED_DOUGH; simState.PlayerItem = NONE; }
            else if (pItem == NONE && tableItem != NONE) { simState.PlayerItem = tableItem; simState.Tables[tableIdx] = NONE; }
            else if (pItem != NONE && tableItem == NONE) { simState.Tables[tableIdx] = pItem; simState.PlayerItem = NONE; }
        }
        else if (mapChar == 'O')
        {
            if (pItem == DOUGH && simState.OvenContents == NONE) { simState.OvenContents = DOUGH; simState.OvenTimer = 10; simState.PlayerItem = NONE; }
            else if (pItem == RAW_TART && simState.OvenContents == NONE) { simState.OvenContents = RAW_TART; simState.OvenTimer = 10; simState.PlayerItem = NONE; }
            else if (pItem == NONE && (simState.OvenContents == CROISSANT || simState.OvenContents == TART)) { simState.PlayerItem = simState.OvenContents; simState.OvenContents = NONE; simState.OvenTimer = 0; }
            else if ((pItem & 1) != 0 && (simState.OvenContents == CROISSANT || simState.OvenContents == TART)) { simState.PlayerItem |= simState.OvenContents; simState.OvenContents = NONE; simState.OvenTimer = 0; }
        }
        else if (mapChar == '#')
        {
            if (pItem == NONE && tableItem != NONE) { simState.PlayerItem = tableItem; simState.Tables[tableIdx] = NONE; }
            else if (pItem != NONE && tableItem == NONE) { simState.Tables[tableIdx] = pItem; simState.PlayerItem = NONE; }
            else if ((pItem & 1) != 0 && tableItem != NONE && (tableItem == ICE || tableItem == BLUE || tableItem == CHOPPED_STRAW || tableItem == CROISSANT || tableItem == TART))
            {
                simState.PlayerItem |= tableItem; simState.Tables[tableIdx] = NONE;
            }
            else if ((pItem == BLUE && tableItem == CHOPPED_DOUGH) || (pItem == CHOPPED_DOUGH && tableItem == BLUE))
            {
                simState.PlayerItem = NONE; simState.Tables[tableIdx] = RAW_TART; 
            }
        }
    }

    private int Evaluate(SimState simState)
    {
        int score = simState.Score;
        int numDishes = 0;
        if ((simState.PlayerItem & 1) != 0) evalDishes[numDishes++] = simState.PlayerItem;
        for (int i = 0; i < 77; i++)
        {
            if ((simState.Tables[i] & 1) != 0) evalDishes[numDishes++] = simState.Tables[i];
        }

        foreach (var c in ActiveCustomers)
        {
            int bestDishScore = 0;
            int bestDish = 0, maxCount = -1;

            for (int i = 0; i < numDishes; i++)
            {
                int dish = evalDishes[i];
                if ((dish & ~c.Mask) == 0) 
                {
                    int count = BitCount(dish);
                    int s = (count - 1) * 2000;
                    if (s > bestDishScore) bestDishScore = s;

                    if (count > maxCount) { maxCount = count; bestDish = dish; }
                }
            }
            score += bestDishScore;

            int missingMask = c.Mask & ~bestDish;
            int missingScore = 0;
            if ((missingMask & CROISSANT) != 0)
            {
                if (HasItem(simState, CROISSANT)) missingScore += 800;
                else if (simState.OvenContents == CROISSANT) missingScore += 600;
                else if (simState.OvenContents == DOUGH) missingScore += 400;
                else if (HasItem(simState, DOUGH)) missingScore += 200;
            }
            if ((missingMask & TART) != 0)
            {
                if (HasItem(simState, TART)) missingScore += 800;
                else if (simState.OvenContents == TART) missingScore += 600;
                else if (simState.OvenContents == RAW_TART) missingScore += 400;
                else if (HasItem(simState, RAW_TART)) missingScore += 300;
                else if (HasItem(simState, CHOPPED_DOUGH)) missingScore += 200;
                else if (HasItem(simState, DOUGH)) missingScore += 100;
            }
            if ((missingMask & CHOPPED_STRAW) != 0)
            {
                if (HasItem(simState, CHOPPED_STRAW)) missingScore += 800;
                else if (HasItem(simState, STRAW)) missingScore += 400;
            }
            if ((missingMask & BLUE) != 0 && HasItem(simState, BLUE)) missingScore += 800;
            if ((missingMask & ICE) != 0 && HasItem(simState, ICE)) missingScore += 800;
            
            score += missingScore;
        }

        if ((simState.PlayerItem & 1) != 0)
        {
            score += 50;
            bool complete = false;
            foreach (var c in ActiveCustomers) { if (simState.PlayerItem == c.Mask) complete = true; }
            if (complete)
            {
                V window = new V(-1, -1);
                for (int i = 0; i < Equipment.Count; i++) {
                    if (Map[Equipment[i].X, Equipment[i].Y] == 'W') { window = Equipment[i]; break; }
                }
                if (window.X != -1) {
                    int distToW = Math.Abs(simState.Px - window.X) + Math.Abs(simState.Py - window.Y);
                    score += (20 - distToW) * 100;
                }
            }
        }

        return score;
    }

    private bool HasItem(SimState simState, int item)
    {
        if (simState.PlayerItem == item) return true;
        for (int i = 0; i < 77; i++) if (simState.Tables[i] == item) return true;
        return false;
    }
}