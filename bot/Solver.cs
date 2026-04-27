namespace bot;

public class Solver
{
    public const int NONE = 0;
    public const int DISH = 1;
    public const int ICE = 2;
    public const int BLUE = 4;
    public const int CHOPPED_STRAW = 8;
    public const int CROISSANT = 16;
    public const int TART = 32;
    public const int STRAW = 64;
    public const int DOUGH = 128;
    public const int CHOPPED_DOUGH = 256;
    public const int RAW_TART = 512;

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
        public int PartnerItem;
        public int[] Tables = new int[77];
        public int OvenContents;
        public int OvenTimer;
        public int Score;
        public int TurnsRemaining;
        public MacroAction FirstAction;
        public int EvalScore;

        public void CopyFrom(SimState o)
        {
            Px = o.Px; Py = o.Py; PlayerItem = o.PlayerItem; PartnerItem = o.PartnerItem;
            Array.Copy(o.Tables, Tables, 77);
            OvenContents = o.OvenContents; OvenTimer = o.OvenTimer;
            Score = o.Score; TurnsRemaining = o.TurnsRemaining;
            FirstAction = o.FirstAction;
            EvalScore = o.EvalScore;
        }

        public int CompareTo(SimState other)
        {
            return other.EvalScore.CompareTo(this.EvalScore); 
        }
    }

    private SimState[] Pool;
    private int PoolIndex = 0;
    private List<V> Equipment;
    private char[,] Map;
    private int PartnerX, PartnerY;
    private List<(int Mask, int Award)> ActiveCustomers = new List<(int Mask, int Award)>();

    private int[] bfsDist = new int[77];
    private int[] bfsQx = new int[77];
    private int[] bfsQy = new int[77];
    private V[] validTargets = new V[100];
    
    private int neededItemsMask = 0;
    private bool iAmCloserToOven = true;
    private V WindowPos = new V(-1, -1);
    private V OvenPos = new V(-1, -1);
    private bool partnerWantsWindow = false;
    private bool partnerWantsOven = false;
    
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
        WindowPos = new V(-1, -1);
        OvenPos = new V(-1, -1);
        
        if (Equipment == null)
        {
            Equipment = new List<V>();
            for (int y = 0; y < 7; y++)
            {
                for (int x = 0; x < 11; x++)
                {
                    char c = Map[x, y];
                    if ("DWBISHCO".Contains(c)) Equipment.Add(new V(x, y));
                    if (c == 'W') WindowPos = new V(x, y);
                    if (c == 'O') OvenPos = new V(x, y);
                }
            }
        }

        PartnerX = state.PartnerPos.X;
        PartnerY = state.PartnerPos.Y;
        int pItemMask = ParseItem(state.PartnerItem);
        
        partnerWantsWindow = false;
        partnerWantsOven = false;

        ActiveCustomers.Clear();
        int allCustomersMask = 0;
        foreach (var c in state.Customers) {
            int mask = ParseItem(c.Item);
            ActiveCustomers.Add((mask, c.Award));
            allCustomersMask |= mask;
            if ((pItemMask & DISH) != 0 && (pItemMask & ~mask) == 0 && BitCount(pItemMask) == BitCount(mask)) {
                partnerWantsWindow = true;
            }
        }
        
        iAmCloserToOven = true;
        if (OvenPos.X != -1) {
            int myDist = Math.Abs(state.PlayerPos.X - OvenPos.X) + Math.Abs(state.PlayerPos.Y - OvenPos.Y);
            int pDist = Math.Abs(PartnerX - OvenPos.X) + Math.Abs(PartnerY - OvenPos.Y);
            if (pDist < myDist) iAmCloserToOven = false;
            
            if (!iAmCloserToOven) {
                if (pItemMask == DOUGH || pItemMask == RAW_TART) partnerWantsOven = true;
                if (ParseItem(state.OvenContents) != NONE) partnerWantsOven = true;
            }
        }
        
        neededItemsMask = allCustomersMask;
        if ((neededItemsMask & TART) != 0) { neededItemsMask |= BLUE | CHOPPED_DOUGH | RAW_TART | DOUGH; }
        if ((neededItemsMask & CROISSANT) != 0) { neededItemsMask |= DOUGH; }
        if ((neededItemsMask & CHOPPED_STRAW) != 0) { neededItemsMask |= STRAW; }

        PoolIndex = 0;
        
        SimState root = GetState();
        root.Px = state.PlayerPos.X;
        root.Py = state.PlayerPos.Y;
        root.PlayerItem = ParseItem(state.PlayerItem);
        root.PartnerItem = pItemMask;
        
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
        
        int BEAM_WIDTH = 800; 
        
        for (int depth = 0; depth < 6; depth++)
        {
            if (countdown.TimeAvailable.TotalMilliseconds < 10) break;

            List<SimState> nextBeam = new List<SimState>(BEAM_WIDTH * 25);
            int stateCount = 0;

            foreach (var s in currentBeam)
            {
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

            if (countdown.TimeAvailable.TotalMilliseconds < 5) break;

            if (nextBeam.Count > BEAM_WIDTH)
            {
                nextBeam.Sort(); 
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

        while (head < tail)
        {
            int cx = bfsQx[head], cy = bfsQy[head]; head++;
            int cd = bfsDist[cx + cy * 11];
            
            int nx = cx + 1, ny = cy;
            if (nx < 11 && (Map[nx, ny] == '.' || Map[nx, ny] == '0' || Map[nx, ny] == '1') && (nx != PartnerX || ny != PartnerY) && bfsDist[nx + ny * 11] == -1)
            { bfsDist[nx + ny * 11] = cd + 1; bfsQx[tail] = nx; bfsQy[tail] = ny; tail++; }
            nx = cx - 1; ny = cy;
            if (nx >= 0 && (Map[nx, ny] == '.' || Map[nx, ny] == '0' || Map[nx, ny] == '1') && (nx != PartnerX || ny != PartnerY) && bfsDist[nx + ny * 11] == -1)
            { bfsDist[nx + ny * 11] = cd + 1; bfsQx[tail] = nx; bfsQy[tail] = ny; tail++; }
            nx = cx; ny = cy + 1;
            if (ny < 7 && (Map[nx, ny] == '.' || Map[nx, ny] == '0' || Map[nx, ny] == '1') && (nx != PartnerX || ny != PartnerY) && bfsDist[nx + ny * 11] == -1)
            { bfsDist[nx + ny * 11] = cd + 1; bfsQx[tail] = nx; bfsQy[tail] = ny; tail++; }
            nx = cx; ny = cy - 1;
            if (ny >= 0 && (Map[nx, ny] == '.' || Map[nx, ny] == '0' || Map[nx, ny] == '1') && (nx != PartnerX || ny != PartnerY) && bfsDist[nx + ny * 11] == -1)
            { bfsDist[nx + ny * 11] = cd + 1; bfsQx[tail] = nx; bfsQy[tail] = ny; tail++; }
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

            if (mapChar == 'D') useful = (pItem == NONE);
            else if (mapChar == 'W') useful = ((pItem & DISH) != 0);
            else if (mapChar == 'B') useful = (pItem == NONE || ((pItem & DISH) != 0 && (pItem & BLUE) == 0)) && ((neededItemsMask & BLUE) != 0);
            else if (mapChar == 'I') useful = (pItem == NONE || ((pItem & DISH) != 0 && (pItem & ICE) == 0)) && ((neededItemsMask & ICE) != 0);
            else if (mapChar == 'S') useful = (pItem == NONE) && ((neededItemsMask & STRAW) != 0);
            else if (mapChar == 'H') useful = (pItem == NONE) && ((neededItemsMask & DOUGH) != 0);
            else if (mapChar == 'C' || mapChar == '#')
            {
                if (mapChar == 'C' && (pItem == STRAW || pItem == DOUGH) && tableItem == NONE) useful = true;
                else if (pItem == NONE && tableItem != NONE) useful = true;
                else if (pItem != NONE && tableItem == NONE) useful = true;
                else if ((pItem & DISH) != 0 && tableItem != NONE && (tableItem == ICE || tableItem == BLUE || tableItem == CHOPPED_STRAW || tableItem == CROISSANT || tableItem == TART) && (pItem & tableItem) == 0) useful = true;
                else if ((tableItem & DISH) != 0 && pItem != NONE && (pItem == ICE || pItem == BLUE || pItem == CHOPPED_STRAW || pItem == CROISSANT || pItem == TART) && (tableItem & pItem) == 0) useful = true;
                else if ((pItem == BLUE && tableItem == CHOPPED_DOUGH) || (pItem == CHOPPED_DOUGH && tableItem == BLUE)) useful = true;
            }
            else if (mapChar == 'O')
            {
                if ((pItem == DOUGH || pItem == RAW_TART) && simState.OvenContents == NONE) useful = true;
                else if (pItem == NONE)
                {
                    if (simState.OvenContents == CROISSANT || simState.OvenContents == TART) useful = true;
                    if (simState.OvenContents == DOUGH || simState.OvenContents == RAW_TART) useful = true;
                }
                else if ((pItem & DISH) != 0)
                {
                    if (simState.OvenContents == CROISSANT && (pItem & CROISSANT) == 0) useful = true;
                    if (simState.OvenContents == TART && (pItem & TART) == 0) useful = true;
                    if (simState.OvenContents == DOUGH || simState.OvenContents == RAW_TART) useful = true;
                }
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
                    Type = (md == 0 && waitTime > 0) ? CmdType.Wait : CmdType.Use,
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
        int minD = 999; bestNx = -1; bestNy = -1; int bestDistToPartner = -1;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = tx + dx, ny = ty + dy;
                if (nx >= 0 && nx < 11 && ny >= 0 && ny < 7)
                {
                    int d = dist[nx + ny * 11];
                    if (d != -1)
                    {
                        int distToP = Math.Abs(nx - PartnerX) + Math.Abs(ny - PartnerY);
                        if (d < minD || (d == minD && distToP > bestDistToPartner))
                        {
                            minD = d; bestNx = nx; bestNy = ny; bestDistToPartner = distToP;
                        }
                    }
                }
            }
        }
        return minD == 999 ? -1 : minD;
    }

    public void ApplyAction(SimState simState, MacroAction action)
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
                        simState.OvenContents = NONE; 
                        if (iAmCloserToOven) simState.Score -= 50000;
                        else simState.Score -= 2000;
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

        if (mapChar == 'D') { if (pItem == NONE) simState.PlayerItem = DISH; }
        else if (mapChar == 'W')
        {
            if ((pItem & DISH) != 0)
            {
                bool matched = false;
                foreach (var c in ActiveCustomers) {
                    if (pItem == c.Mask) { simState.Score += 100000 + c.Award; simState.PlayerItem = NONE; matched = true; break; }
                }
                if (!matched) simState.PlayerItem = NONE;
            }
        }
        else if (mapChar == 'B') { 
            if (pItem == NONE) simState.PlayerItem = BLUE; 
            else if ((pItem & DISH) != 0 && (pItem & BLUE) == 0) simState.PlayerItem |= BLUE; 
        }
        else if (mapChar == 'I') { 
            if (pItem == NONE) simState.PlayerItem = ICE; 
            else if ((pItem & DISH) != 0 && (pItem & ICE) == 0) simState.PlayerItem |= ICE; 
        }
        else if (mapChar == 'S') { if (pItem == NONE) simState.PlayerItem = STRAW; }
        else if (mapChar == 'H') { if (pItem == NONE) simState.PlayerItem = DOUGH; }
        else if (mapChar == 'C' || mapChar == '#')
        {
            if (mapChar == 'C' && pItem == STRAW && tableItem == NONE) { simState.Tables[tableIdx] = CHOPPED_STRAW; simState.PlayerItem = NONE; }
            else if (mapChar == 'C' && pItem == DOUGH && tableItem == NONE) { simState.Tables[tableIdx] = CHOPPED_DOUGH; simState.PlayerItem = NONE; }
            else if (pItem == NONE && tableItem != NONE) { simState.PlayerItem = tableItem; simState.Tables[tableIdx] = NONE; }
            else if (pItem != NONE && tableItem == NONE) { simState.Tables[tableIdx] = pItem; simState.PlayerItem = NONE; }
            else if ((pItem & DISH) != 0 && tableItem != NONE && (tableItem == ICE || tableItem == BLUE || tableItem == CHOPPED_STRAW || tableItem == CROISSANT || tableItem == TART) && (pItem & tableItem) == 0)
            { simState.PlayerItem |= tableItem; simState.Tables[tableIdx] = NONE; }
            else if ((tableItem & DISH) != 0 && pItem != NONE && (pItem == ICE || pItem == BLUE || pItem == CHOPPED_STRAW || pItem == CROISSANT || pItem == TART) && (tableItem & pItem) == 0)
            { simState.Tables[tableIdx] |= pItem; simState.PlayerItem = NONE; }
            else if ((pItem == BLUE && tableItem == CHOPPED_DOUGH) || (pItem == CHOPPED_DOUGH && tableItem == BLUE))
            { simState.PlayerItem = NONE; simState.Tables[tableIdx] = RAW_TART; }
        }
        else if (mapChar == 'O')
        {
            if (pItem == DOUGH && simState.OvenContents == NONE) { simState.OvenContents = DOUGH; simState.OvenTimer = 10; simState.PlayerItem = NONE; }
            else if (pItem == RAW_TART && simState.OvenContents == NONE) { simState.OvenContents = RAW_TART; simState.OvenTimer = 10; simState.PlayerItem = NONE; }
            else if (pItem == NONE && (simState.OvenContents == CROISSANT || simState.OvenContents == TART)) { simState.PlayerItem = simState.OvenContents; simState.OvenContents = NONE; simState.OvenTimer = 0; }
            else if ((pItem & DISH) != 0 && (simState.OvenContents == CROISSANT || simState.OvenContents == TART) && (pItem & simState.OvenContents) == 0) { simState.PlayerItem |= simState.OvenContents; simState.OvenContents = NONE; simState.OvenTimer = 0; }
        }
    }

    private int Evaluate(SimState simState)
    {
        int score = simState.Score;
        
        Span<int> uniquePlates = stackalloc int[80];
        Span<int> uniquePlatesCounts = stackalloc int[80];
        int numUniquePlates = 0;
        
        // Делаем функции статическими и передаем всё через параметры
        static void AddPlate(int p, Span<int> plates, Span<int> counts, ref int numUnique) {
            if ((p & DISH) == 0) return;
            for (int j = 0; j < numUnique; j++) {
                if (plates[j] == p) {
                    counts[j]++;
                    return;
                }
            }
            plates[numUnique] = p;
            counts[numUnique] = 1;
            numUnique++;
        }
        
        AddPlate(simState.PlayerItem, uniquePlates, uniquePlatesCounts, ref numUniquePlates);
        AddPlate(simState.PartnerItem, uniquePlates, uniquePlatesCounts, ref numUniquePlates);
        for (int i = 0; i < 77; i++) AddPlate(simState.Tables[i], uniquePlates, uniquePlatesCounts, ref numUniquePlates);

        Span<int> loose = stackalloc int[10];
        loose.Clear();
        
        static void AddLoose(int item, Span<int> l) {
            if ((item & DISH) != 0) return;
            if (item == CROISSANT) l[0]++;
            else if (item == TART) l[1]++;
            else if (item == CHOPPED_STRAW) l[2]++;
            else if (item == BLUE) l[3]++;
            else if (item == ICE) l[4]++;
            else if (item == DOUGH) l[5]++;
            else if (item == RAW_TART) l[6]++;
            else if (item == STRAW) l[7]++;
            else if (item == CHOPPED_DOUGH) l[8]++;
        }
        
        AddLoose(simState.PlayerItem, loose);
        AddLoose(simState.PartnerItem, loose);
        for (int i = 0; i < 77; i++) AddLoose(simState.Tables[i], loose);
        
        // В MatchCustomers теперь передаем Span loose напрямую
        int bestMatchingScore = MatchCustomers(0, uniquePlates, uniquePlatesCounts, numUniquePlates, loose, simState, 0);
        score += bestMatchingScore;

        if ((simState.PlayerItem & DISH) != 0)
        {
            bool complete = false;
            foreach (var c in ActiveCustomers) { if (simState.PlayerItem == c.Mask) complete = true; }
            if (complete)
            {
                score += 50;
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
        
        if (partnerWantsWindow && WindowPos.X != -1)
        {
            int myDistToWindow = Abs(simState.Px - WindowPos.X) + Math.Abs(simState.Py - WindowPos.Y);
            if (myDistToWindow <= 1) 
                score -= 15000;
        }

        if (partnerWantsOven && OvenPos.X != -1)
        {
            int myDistToOven = Abs(simState.Px - OvenPos.X) + Math.Abs(simState.Py - OvenPos.Y);
            if (myDistToOven <= 1) 
                score -= 10000;
        }

        return score;
    }

    private int MatchCustomers(int cIdx, Span<int> uniquePlates, Span<int> uniquePlatesCounts, int numUniquePlates, Span<int> loose, SimState simState, int ovenUsedMask)
    {
        if (cIdx >= ActiveCustomers.Count) return 0;
        
        var c = ActiveCustomers[cIdx];
        int bestScore = 0;
        
        // Выделяем looseChanges один раз на уровень рекурсии
        Span<int> looseChanges = stackalloc int[10];
        
        for (int i = 0; i < numUniquePlates; i++)
        {
            if (uniquePlatesCounts[i] == 0) continue;
            int dish = uniquePlates[i];
            
            if ((dish & ~c.Mask) == 0) 
            {
                uniquePlatesCounts[i]--;
                int count = BitCount(dish);
                int currentScore = (count - 1) * 2000;
                
                int missingMask = c.Mask & ~dish;
                looseChanges.Clear();
                int ovenChanges = 0;
                
                currentScore += EvalMissingMutate(missingMask, loose, simState, ref ovenUsedMask, looseChanges, ref ovenChanges);
                currentScore += MatchCustomers(cIdx + 1, uniquePlates, uniquePlatesCounts, numUniquePlates, loose, simState, ovenUsedMask);
                
                // Возвращаем состояние ингредиентов
                for(int j=0; j<10; j++) loose[j] += looseChanges[j];
                ovenUsedMask &= ~ovenChanges;
                
                if (currentScore > bestScore) bestScore = currentScore;
                
                uniquePlatesCounts[i]++;
            }
        }
        
        // Вариант: не пытаться выполнить этот заказ этой тарелкой
        looseChanges.Clear();
        int ovenChanges2 = 0;
        int noPlateScore = EvalMissingMutate(c.Mask, loose, simState, ref ovenUsedMask, looseChanges, ref ovenChanges2);
        noPlateScore += MatchCustomers(cIdx + 1, uniquePlates, uniquePlatesCounts, numUniquePlates, loose, simState, ovenUsedMask);
        
        for(int j=0; j<10; j++) loose[j] += looseChanges[j];
        ovenUsedMask &= ~ovenChanges2;
        
        if (noPlateScore > bestScore) bestScore = noPlateScore;
        
        return bestScore;
    }

    private int EvalMissingMutate(int missingMask, Span<int> loose, SimState simState, ref int ovenUsedMask, Span<int> looseChanges, ref int ovenChanges)
    {
        int missingScore = 0;
        if ((missingMask & CROISSANT) != 0)
        {
            if (loose[0] > 0) { loose[0]--; looseChanges[0]++; missingScore += 800; }
            else if ((ovenUsedMask & CROISSANT) == 0 && simState.OvenContents == CROISSANT) { ovenUsedMask |= CROISSANT; ovenChanges |= CROISSANT; missingScore += 600; }
            else if ((ovenUsedMask & DOUGH) == 0 && simState.OvenContents == DOUGH) { ovenUsedMask |= DOUGH; ovenChanges |= DOUGH; missingScore += 400; }
            else if (loose[5] > 0) { loose[5]--; looseChanges[5]++; missingScore += 200; } 
        }
        if ((missingMask & TART) != 0)
        {
            if (loose[1] > 0) { loose[1]--; looseChanges[1]++; missingScore += 800; }
            else if ((ovenUsedMask & TART) == 0 && simState.OvenContents == TART) { ovenUsedMask |= TART; ovenChanges |= TART; missingScore += 600; }
            else if ((ovenUsedMask & RAW_TART) == 0 && simState.OvenContents == RAW_TART) { ovenUsedMask |= RAW_TART; ovenChanges |= RAW_TART; missingScore += 400; }
            else if (loose[6] > 0) { loose[6]--; looseChanges[6]++; missingScore += 300; } 
            else if (loose[8] > 0) { loose[8]--; looseChanges[8]++; missingScore += 200; } 
            else if (loose[5] > 0) { loose[5]--; looseChanges[5]++; missingScore += 100; } 
        }
        if ((missingMask & CHOPPED_STRAW) != 0)
        {
            if (loose[2] > 0) { loose[2]--; looseChanges[2]++; missingScore += 800; }
            else if (loose[7] > 0) { loose[7]--; looseChanges[7]++; missingScore += 400; } 
        }
        if ((missingMask & BLUE) != 0)
        {
             if (loose[3] > 0) { loose[3]--; looseChanges[3]++; missingScore += 800; }
        }
        if ((missingMask & ICE) != 0)
        {
             if (loose[4] > 0) { loose[4]--; looseChanges[4]++; missingScore += 800; }
        }
        return missingScore;
    }
}