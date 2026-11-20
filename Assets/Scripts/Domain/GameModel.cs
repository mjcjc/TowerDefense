using System;
using System.Collections.Generic;

namespace TowerDefense
{
    public enum Lane { North, South }
    public enum TowerType { Rapid, Artillery, Slow, Marksman, Spark, FrostCannon, Railgun }
    public enum EnemyType { Grunt, Scout, Heavy }
    public enum SupplyReward { Credits, Repair, Promotion }
    public enum AssaultType { Standard, Swarm, Armored }
    public enum ChallengeType { None, Crossfire, Vanguard }
    public enum RunPhase { Preparing, Fighting, Finished }

    public struct Point
    {
        public float X, Y;
        public Point(float x, float y) { X = x; Y = y; }
        public bool Within(Point other, float radius)
        {
            float x = X - other.X, y = Y - other.Y;
            return x * x + y * y <= radius * radius;
        }
    }

    public class Route
    {
        public Point[] Points;
        public double[] Distances;
        public double Length => Distances[Distances.Length - 1];
        public double MergeDistance => Length - 3;
        public Route(params Point[] points)
        {
            Points = points;
            Distances = new double[points.Length];
            for (int i = 1; i < points.Length; i++)
                Distances[i] = Distances[i - 1] + Math.Abs(points[i].X - points[i - 1].X)
                    + Math.Abs(points[i].Y - points[i - 1].Y);
        }
        public Point Position(double distance)
        {
            for (int i = 1; i < Points.Length; i++)
            {
                if (distance > Distances[i]) continue;
                float t = (float)((distance - Distances[i - 1]) / (Distances[i] - Distances[i - 1]));
                return new Point(Points[i - 1].X + (Points[i].X - Points[i - 1].X) * t,
                    Points[i - 1].Y + (Points[i].Y - Points[i - 1].Y) * t);
            }
            return Points[Points.Length - 1];
        }
    }

    public static class Rules
    {
        public const int Hz = 60;
        public const int FinalWave = 30;
        public const int CoreMaxHp = 36;
        public const int SupplyInterval = 5, SupplyCredits = 50, SupplyRepair = 9;
        public const int OpeningSupplyDraws = 2;
        public const int ChallengeCreditReward = 100;
        public const int DrawCost = 25, MergeCount = 3, MaxRank = 3;
        public static Route North = new Route(new Point(0,8), new Point(11,8), new Point(11,5), new Point(14,5));
        public static Route South = new Route(new Point(0,2), new Point(5,2), new Point(5,1),
            new Point(9,1), new Point(9,3), new Point(11,3), new Point(11,5), new Point(14,5));
        public static Point[] Slots = { new Point(3,7), new Point(7,7), new Point(10,7),
            new Point(3,3), new Point(7,2), new Point(10,2), new Point(12,4), new Point(13,6),
            new Point(1,7), new Point(5,7), new Point(9,9), new Point(12,8),
            new Point(1,3), new Point(5,3), new Point(8,2), new Point(13,4),
            new Point(5,9), new Point(7,9), new Point(9,6), new Point(4,5),
            new Point(6,4), new Point(10,4) };
        public static int[] BuildCost = {40,55,30,45,45};
        public static float[] Range = {2.6f,3f,2.4f,4.4f,2.8f,3.1f,4.4f};
        public static int[] TowerDamage = {7,18,0,32,12,19,23};
        public static int[] AttackInterval = {24,105,0,62,48,95,37};
        public static float[] SplashRadius = {0,1.1f,0,0,.65f,1.25f,0};
        public static float[] SlowRange = {0,0,2.4f,0,0,3.1f,0};
        public static float[] RankPower = {1f,2.6f,6.8f};
        public static int[] Hp = {24,14,60}, CoreDamage = {3,3,6}, BudgetCost = {2,2,4};
        public static double[] Speed = {1,1.7,0.75};
        public const int OpeningSlimeCount = 6;
        public const int SwarmPackSize = 3;
        public const int SlimeCoreDamage = 1;
        public static Route Path(Lane lane) => lane == Lane.North ? North : South;
    }

    public class TowerPlacement
    {
        public int SlotId;
        public TowerType Type;
        public int Rank=1;
        public int Invested;
        public TowerPlacement Copy()
        {
            TowerPlacement copy = new TowerPlacement();
            copy.SlotId = SlotId;
            copy.Type = Type;
            copy.Rank = Rank;
            copy.Invested = Invested;
            return copy;
        }
    }
    public class SpawnEntry
    {
        public int Tick, Sequence;
        public Lane Lane;
        public EnemyType Type;
        public bool IsSwarmSlime;
    }
    public class WavePlan
    {
        public int Wave, Template;
        public AssaultType Assault;
        public ChallengeType Challenge;
        public Lane? Primary;
        public List<SpawnEntry> Spawns = new List<SpawnEntry>();
    }
    public class EnemyRuntime
    {
        public int Id, Hp;
        public bool IsSwarmSlime;
        public Lane Lane;
        public EnemyType Type;
        public double Progress, EffectiveSpeed;
        public bool IsSlowed;
        public int MaxHp;
        public Point Position => Rules.Path(Lane).Position(Progress);
    }
    public class Shot
    {
        public int Tick, Slot, Kills;
        public Point Target;
    }

    public class CombatSimulation
    {
        public int Tick { get; private set; }
        public int CoreHp { get; private set; }
        public int Damage { get; private set; }
        public int KillsAfterMerge { get; private set; }
        public int[] Leaks = new int[2];
        public List<EnemyRuntime> Enemies = new List<EnemyRuntime>();
        public List<Shot> Shots = new List<Shot>();
        public bool Finished { get; private set; }
        private WavePlan plan;
        private TowerPlacement[] towers;
        private int[] nextAttack;
        private bool prediction;
        private int nextSpawn;

        public CombatSimulation(WavePlan plan, TowerPlacement[] placements, int hp, bool prediction = false)
        {
            this.plan = plan;
            this.prediction = prediction;
            CoreHp = hp;

            // 예측과 실제 전투는 각각 시작 시점의 포탑 배치를 사용한다.
            towers = new TowerPlacement[placements.Length];
            for (int slot = 0; slot < placements.Length; slot++)
            {
                if (placements[slot] != null)
                    towers[slot] = placements[slot].Copy();
            }
            nextAttack = new int[towers.Length];
        }

        public void Step()
        {
            if (Finished) return;

            Tick++;
            for (int index = Shots.Count - 1; index >= 0; index--)
            {
                if (Tick - Shots[index].Tick > 12)
                    Shots.RemoveAt(index);
            }
            SpawnReadyEnemies();
            MoveEnemies();
            DamageCoreForEscapedEnemies();

            if (!Finished)
                AttackWithTowers();

            // 뒤에서부터 지우면 아직 확인하지 않은 인덱스가 그대로다.
            for (int index = Enemies.Count - 1; index >= 0; index--)
            {
                if (Enemies[index].Hp <= 0)
                    Enemies.RemoveAt(index);
            }
            if (nextSpawn == plan.Spawns.Count && Enemies.Count == 0)
                Finished = true;
        }

        private void SpawnReadyEnemies()
        {
            while (nextSpawn < plan.Spawns.Count && plan.Spawns[nextSpawn].Tick <= Tick)
            {
                SpawnEntry spawn = plan.Spawns[nextSpawn];
                nextSpawn++;

                int healthPercent = 100 + Math.Max(0, plan.Wave - 8) * 12;
                if (plan.Assault == AssaultType.Armored && spawn.Lane == plan.Primary && spawn.Type == EnemyType.Heavy)
                    healthPercent = healthPercent * 135 / 100;
                int health = spawn.IsSwarmSlime ? 5 : Rules.Hp[(int)spawn.Type] * healthPercent / 100;
                EnemyRuntime enemy = new EnemyRuntime();
                enemy.Id = spawn.Sequence;
                enemy.Lane = spawn.Lane;
                enemy.Type = spawn.Type;
                enemy.IsSwarmSlime = spawn.IsSwarmSlime;
                enemy.MaxHp = health;
                enemy.Hp = health;
                Enemies.Add(enemy);
            }
        }

        private void MoveEnemies()
        {
            foreach (EnemyRuntime enemy in Enemies)
            {
                double strongestSlow = 0;
                Point position = enemy.Position;
                foreach (TowerPlacement tower in towers)
                {
                    if (tower == null) continue;
                    float slowRange = Rules.SlowRange[(int)tower.Type];
                    if (slowRange == 0) continue;

                    Point towerPosition = Rules.Slots[tower.SlotId - 1];
                    if (towerPosition.Within(position, slowRange))
                    {
                        double slow = 0.25 + 0.15 * (tower.Rank - 1);
                        strongestSlow = Math.Max(strongestSlow, slow);
                    }
                }

                double baseSpeed = enemy.IsSwarmSlime ? 2.4 : Rules.Speed[(int)enemy.Type];
                enemy.IsSlowed = strongestSlow > 0;
                enemy.EffectiveSpeed = baseSpeed * (1 - strongestSlow);
                enemy.Progress += enemy.EffectiveSpeed / Rules.Hz;
            }
        }

        private void DamageCoreForEscapedEnemies()
        {
            foreach (EnemyRuntime enemy in Enemies)
            {
                double routeLength = Rules.Path(enemy.Lane).Length;
                // 이동 거리를 반복해서 더할 때 생기는 소수점 오차를 허용한다.
                if (enemy.Progress + 1e-8 < routeLength) continue;

                int lane = (int)enemy.Lane;
                Leaks[lane]++;

                int damage = enemy.IsSwarmSlime ? Rules.SlimeCoreDamage : Rules.CoreDamage[(int)enemy.Type];

                Damage += damage;
                CoreHp = Math.Max(0, CoreHp - damage);
                enemy.Hp = 0;

                // 예측은 코어가 파괴된 뒤에도 웨이브 전체의 피해를 계산한다.
                if (CoreHp == 0 && !prediction)
                {
                    Finished = true;
                    return;
                }
            }
        }

        private void AttackWithTowers()
        {
            for (int slot = 0; slot < towers.Length; slot++)
            {
                TowerPlacement tower = towers[slot];
                if (tower == null || tower.Type == TowerType.Slow || Tick < nextAttack[slot]) continue;

                int type = (int)tower.Type;
                EnemyRuntime target = FindTarget(Rules.Slots[slot], Rules.Range[type]);
                if (target == null) continue;

                int damage = (int)Math.Round(Rules.TowerDamage[type] * Rules.RankPower[tower.Rank - 1]);
                nextAttack[slot] = Tick + Rules.AttackInterval[type];
                ApplyShot(slot, target, damage, Rules.SplashRadius[type]);
            }
        }

        private EnemyRuntime FindTarget(Point towerPosition, float range)
        {
            EnemyRuntime target = null;
            double shortestTime = double.PositiveInfinity;
            foreach (EnemyRuntime enemy in Enemies)
            {
                if (enemy.Hp <= 0 || !towerPosition.Within(enemy.Position, range)) continue;

                double distanceToCore = Rules.Path(enemy.Lane).Length - enemy.Progress;
                double timeToCore = distanceToCore / enemy.EffectiveSpeed;
                if (timeToCore < shortestTime)
                {
                    shortestTime = timeToCore;
                    target = enemy;
                }
            }
            return target;
        }

        private void ApplyShot(int slot, EnemyRuntime target, int damage, float splashRadius)
        {
            Shot shot = new Shot();
            shot.Tick = Tick;
            shot.Slot = slot;
            shot.Target = target.Position;
            foreach (EnemyRuntime enemy in Enemies)
            {
                if (enemy.Hp <= 0) continue;
                if (enemy != target && (splashRadius == 0 || !shot.Target.Within(enemy.Position, splashRadius)))
                    continue;

                enemy.Hp -= damage;
                if (enemy.Hp <= 0)
                {
                    shot.Kills++;
                    if (enemy.Progress >= Rules.Path(enemy.Lane).MergeDistance)
                        KillsAfterMerge++;
                }
            }
            Shots.Add(shot);
        }
    }

    public class CandidateEvaluation
    {
        public WavePlan Plan;
        public int Damage, MergeKills, PreviousLeaks, Alternate;
        public int[] Leaks=new int[2];
    }
    public class DirectorDecision
    {
        public WavePlan Selected;
        public string Reason;
        public List<CandidateEvaluation> Evaluations;
    }
    public class EnemyDirector
    {
        private static int[] PrimaryBudget={0,4,10,14,18,22,28,30,32,34,36,38,40};
        private static int[] SecondaryBudget={0,4,4,6,8,10,12,14,16,18,20,22,24};
        public static WavePlan FirstWave()
        {
            WavePlan plan = new WavePlan();
            plan.Wave = 1;
            AddSlimes(plan.Spawns, Lane.North, 24, Rules.OpeningSlimeCount);
            plan.Spawns.Add(new SpawnEntry { Lane=Lane.South, Tick=108, Type=EnemyType.Scout });
            plan.Spawns.Add(new SpawnEntry { Lane=Lane.South, Tick=228, Type=EnemyType.Scout });
            SortSpawns(plan.Spawns);
            return plan;
        }
        internal static Lane ChallengeLane(WavePlan plan)
        {
            Lane primary = plan.Primary.Value;
            if (plan.Challenge == ChallengeType.Vanguard) return primary;
            return primary == Lane.North ? Lane.South : Lane.North;
        }

        internal static WavePlan WithChallenge(WavePlan baseline, ChallengeType challenge)
        {
            var plan = new WavePlan
            {
                Wave = baseline.Wave, Template = baseline.Template, Assault = baseline.Assault,
                Primary = baseline.Primary, Challenge = challenge
            };
            foreach (SpawnEntry spawn in baseline.Spawns)
                plan.Spawns.Add(new SpawnEntry { Lane=spawn.Lane, Tick=spawn.Tick, Type=spawn.Type, IsSwarmSlime=spawn.IsSwarmSlime });
            Lane lane = ChallengeLane(plan);
            if (challenge == ChallengeType.Crossfire)
                AddSlimes(plan.Spawns,lane,100,12+3*(plan.Wave/5));
            else
            {
                plan.Spawns.Add(new SpawnEntry { Lane=lane, Tick=90, Type=EnemyType.Heavy });
                plan.Spawns.Add(new SpawnEntry { Lane=lane, Tick=165, Type=EnemyType.Heavy });
                plan.Spawns.Add(new SpawnEntry { Lane=lane, Tick=110, Type=EnemyType.Scout });
                plan.Spawns.Add(new SpawnEntry { Lane=lane, Tick=185, Type=EnemyType.Scout });
            }
            SortSpawns(plan.Spawns);
            return plan;
        }

        public DirectorDecision SelectPlan(int wave,TowerPlacement[] towers,int hp,int[] previous,Lane? last,int consecutive)
        {
            AssaultType assault = AssaultForWave(wave);
            int[] templates;
            if (assault != AssaultType.Standard)
                templates = new[] { assault == AssaultType.Swarm ? 0 : 2 };
            else if (wave == 2)
                templates = new[] { 1 };
            else if (wave == 3)
                templates = new[] { 0, 1 };
            else if (wave > 6)
                templates = new[] { 1, 2 };
            else
                templates = new[] { 2 };

            var results = new List<CandidateEvaluation>();
            foreach (int template in templates)
            {
                foreach (Lane lane in new[] { Lane.North, Lane.South })
                {
                    // 같은 경로가 두 번 연속 주력이었으면 이번에는 후보에서 제외한다.
                    if (consecutive >= 2 && lane == last) continue;

                    WavePlan plan = CreatePlan(wave, template, lane, assault);
                    var combat = new CombatSimulation(plan, towers, hp, prediction: true);
                    while (!combat.Finished)
                        combat.Step();

                    CandidateEvaluation result = new CandidateEvaluation();
                    result.Plan = plan;
                    result.Damage = combat.Damage;
                    result.MergeKills = combat.KillsAfterMerge;
                    result.PreviousLeaks = Math.Min(2, previous[(int)lane]);
                    result.Alternate = last.HasValue && last != lane ? 1 : 0;
                    Array.Copy(combat.Leaks, result.Leaks, result.Leaks.Length);
                    results.Add(result);
                }
            }

            results.Sort(Compare);
            CandidateEvaluation winner = results[0];
            string reason = "허용 후보 1개";
            if (results.Count > 1)
                reason = GetSelectionReason(winner, results[1]);

            DirectorDecision decision = new DirectorDecision();
            decision.Selected = winner.Plan;
            decision.Evaluations = results;
            decision.Reason = reason;
            return decision;
        }

        private static int Compare(CandidateEvaluation first, CandidateEvaluation second)
        {
            // Sort는 음수를 반환한 후보를 앞에 둔다. 피해와 처치 수는 큰 값부터 정렬한다.
            if (first.Damage != second.Damage)
                return second.Damage.CompareTo(first.Damage);
            if (first.MergeKills != second.MergeKills)
                return second.MergeKills.CompareTo(first.MergeKills);
            if (first.PreviousLeaks != second.PreviousLeaks)
                return second.PreviousLeaks.CompareTo(first.PreviousLeaks);
            if (first.Alternate != second.Alternate)
                return second.Alternate.CompareTo(first.Alternate);
            if (first.Plan.Template != second.Plan.Template)
                return first.Plan.Template.CompareTo(second.Plan.Template);

            return first.Plan.Primary.Value.CompareTo(second.Plan.Primary.Value);
        }

        private static string GetSelectionReason(CandidateEvaluation winner, CandidateEvaluation runnerUp)
        {
            // 정렬과 같은 순서로 두 후보가 처음 달라지는 항목을 화면에 표시한다.
            if (winner.Damage != runnerUp.Damage) return "예상 코어 피해";
            if (winner.MergeKills != runnerUp.MergeKills) return "합류 이후 처치 수";
            if (winner.PreviousLeaks != runnerUp.PreviousLeaks) return "직전 경로 돌파 수";
            if (winner.Alternate != runnerUp.Alternate) return "직전과 다른 경로";
            if (winner.Plan.Template != runnerUp.Plan.Template) return "편성표 순서";
            return "북쪽 우선";
        }

        private static AssaultType AssaultForWave(int wave)
        {
            if (wave == 5 || wave == 15 || wave == 25) return AssaultType.Swarm;
            if (wave == 10 || wave == 20) return AssaultType.Armored;
            return AssaultType.Standard;
        }

        private static WavePlan CreatePlan(int wave,int template,Lane primary,AssaultType assault)
        {
            WavePlan plan = new WavePlan();
            plan.Wave = wave;
            plan.Template = template;
            plan.Primary = primary;
            plan.Assault = assault;
            List<EnemyType> main, other;
            if(wave<=6)
            {
                main=Formation(PrimaryBudget[wave],template);
                if(template==1) SetScouts(main,wave==2?2:3);
                other=Formation(SecondaryBudget[wave],wave==6?1:0);
                if(wave==6) SetScouts(other,2);
            }
            else
            {
                int heavy=1+(wave-7)/3;
                int extra=Math.Max(0,wave-12);
                int budgetIndex=Math.Min(wave,12);
                main=LateFormation(PrimaryBudget[budgetIndex]+extra*2,heavy+(template==2?1:0),template==1?5:3);
                other=LateFormation(SecondaryBudget[budgetIndex]+extra*2,wave>=9?1+(wave-9)/4:0,2);
            }
            if (template == 2 && wave <= 6)
            {
                // 초반 중장갑 편성은 웨이브마다 중장갑이 등장하는 순서가 다르다.
                int heavyIndex = 0;
                if (wave == 4)
                    heavyIndex = main.Count - 1;

                for (int index = 0; index < main.Count; index++)
                    main[index] = EnemyType.Grunt;
                main[heavyIndex] = EnemyType.Heavy;
            }

            Lane secondary = primary == Lane.North ? Lane.South : Lane.North;
            int interval = Math.Max(19, 42 - Math.Max(0, wave - 6));
            if (assault == AssaultType.Swarm)
            {
                int packs = 3 + (wave - 5) / 5;
                for (int pack = 0; pack < packs; pack++)
                    AddSlimes(plan.Spawns, primary, 30 + pack * 75, 12);
            }
            else if (assault == AssaultType.Armored)
            {
                int groups = wave == 10 ? 4 : 7;
                for (int group = 0; group < groups; group++)
                {
                    int tick = 30 + group * 90;
                    plan.Spawns.Add(new SpawnEntry {Lane=primary, Tick=tick, Type=EnemyType.Heavy});
                    plan.Spawns.Add(new SpawnEntry {Lane=primary, Tick=tick+26, Type=EnemyType.Scout});
                    AddSlimes(plan.Spawns, primary, tick+40, Rules.SwarmPackSize);
                }
            }
            else
            {
                AddFormationSpawns(plan.Spawns, main, primary, 30, interval);
            }
            AddFormationSpawns(plan.Spawns, other, secondary, 60, interval);
            SortSpawns(plan.Spawns);
            return plan;
        }
        private static void AddFormationSpawns(List<SpawnEntry> spawns, List<EnemyType> formation,
            Lane lane, int startTick, int interval)
        {
            for (int index = 0; index < formation.Count; index++)
            {
                int tick = startTick + index * interval;
                EnemyType type = formation[index];
                if (type == EnemyType.Grunt)
                    AddSlimes(spawns, lane, tick, Rules.SwarmPackSize);
                else
                {
                    SpawnEntry spawn = new SpawnEntry();
                    spawn.Tick = tick;
                    spawn.Lane = lane;
                    spawn.Type = type;
                    spawns.Add(spawn);
                }
            }
        }

        private static List<EnemyType> LateFormation(int budget,int heavy,int scouts)
        {
            var formation=Formation(budget-heavy*Rules.BudgetCost[(int)EnemyType.Heavy],0);
            SetScouts(formation,scouts);
            int length=formation.Count+heavy;
            for(int i=0;i<heavy;i++)
                formation.Insert((i+1)*length/(heavy+1),EnemyType.Heavy);
            return formation;
        }
        private static void AddSlimes(List<SpawnEntry> spawns,Lane lane,int tick,int count)
        {
            for (int index = 0; index < count; index++)
            {
                SpawnEntry spawn = new SpawnEntry();
                spawn.Tick = tick + index * 5;
                spawn.Lane = lane;
                spawn.Type = EnemyType.Grunt;
                spawn.IsSwarmSlime = true;
                spawns.Add(spawn);
            }
        }
        private static void SortSpawns(List<SpawnEntry> spawns)
        {
            spawns.Sort(CompareSpawnOrder);
            for(int i=0;i<spawns.Count;i++) spawns[i].Sequence=i;
        }
        private static int CompareSpawnOrder(SpawnEntry first, SpawnEntry second)
        {
            if (first.Tick != second.Tick)
                return first.Tick.CompareTo(second.Tick);
            return first.Lane.CompareTo(second.Lane);
        }

        private static void SetScouts(List<EnemyType> list,int count)
        {
            for(int i=0;i<list.Count;i++) list[i]=i%2==0 && i/2<count?EnemyType.Scout:EnemyType.Grunt;
        }
        private static List<EnemyType> Formation(int budget,int template)
        {
            var list=new List<EnemyType>();
            if(template==2)
            {
                list.Add(EnemyType.Heavy);
                budget-=4;
            }
            for(int i=0;i<budget/2;i++)
                list.Add(template==1 && i%2==0?EnemyType.Scout:EnemyType.Grunt);
            return list;
        }
    }

    public class RunController
    {
        public RunPhase Phase {get;private set;}
        public int Wave {get;private set;}
        public int Credits {get;private set;}
        public int CoreHp {get;private set;}
        public bool HasSupplyReward => Phase == RunPhase.Preparing && Wave % Rules.SupplyInterval == 0 && !supplyClaimed;
        public bool HasOpeningSupply => Phase == RunPhase.Preparing && Wave == 1
            && openingDraws >= Rules.OpeningSupplyDraws && !openingSupplyClaimed;
        public bool CanChooseChallenge => Phase == RunPhase.Preparing && LockedPlan.Assault != AssaultType.Standard;
        public ChallengeType LastChallenge {get;private set;}
        public bool LastChallengeWon {get;private set;}
        public bool HasChallengePromotion {get;private set;}
        public bool Won => Phase==RunPhase.Finished && CoreHp>0;
        public TowerPlacement[] Towers {get;private set;}
        public int[] TotalLeaks=new int[2];
        public WavePlan LockedPlan {get;private set;}
        public CombatSimulation Combat {get;private set;}
        public DirectorDecision Decision {get;private set;}
        private Lane? last;
        private int consecutive;
        private EnemyDirector director=new EnemyDirector();
        private Random random;
        private List<TowerType> drawBag=new List<TowerType>();
        private bool supplyClaimed;
        private int openingDraws;
        private bool openingSupplyClaimed;

        public RunController(int? seed=null)
        {
            random=seed.HasValue?new Random(seed.Value):new Random();
            StartNewRun();
        }
        public void StartNewRun()
        {
            Wave = 1;
            Credits = 110;
            CoreHp = Rules.CoreMaxHp;
            supplyClaimed = false;
            openingDraws = 0;
            openingSupplyClaimed = false;
            LastChallenge = ChallengeType.None;
            LastChallengeWon = false;
            HasChallengePromotion = false;
            Phase = RunPhase.Preparing;

            Towers = new TowerPlacement[Rules.Slots.Length];
            Combat = null;
            Decision = null;
            drawBag.Clear();

            Array.Clear(TotalLeaks, 0, TotalLeaks.Length);
            last = null;
            consecutive = 0;
            LockedPlan = EnemyDirector.FirstWave();
        }

        public string TryBuild(int slot, TowerType type)
        {
            if ((int)type < 0 || (int)type >= Rules.BuildCost.Length)
                return "조합 포탑은 재료를 합쳐 만드세요.";
            return TryPlace(slot, type, Rules.BuildCost[(int)type]);
        }

        private bool IsValidSlot(int slot)
        {
            return slot >= 0 && slot < Towers.Length;
        }

        private string TryPlace(int slot, TowerType type, int cost)
        {
            if (Phase != RunPhase.Preparing) return "준비 중에 건설할 수 있습니다.";
            if (!IsValidSlot(slot) || Towers[slot] != null) return "빈 슬롯을 선택하세요.";
            if (Credits < cost) return "크레딧이 부족합니다.";

            TowerPlacement tower = new TowerPlacement();
            tower.SlotId = slot + 1;
            tower.Type = type;
            tower.Invested = cost;
            Towers[slot] = tower;
            Credits -= cost;
            return null;
        }

        public string TryDrawTower(out int placedSlot)
        {
            placedSlot = -1;
            if (Phase != RunPhase.Preparing) return "준비 중에 포탑을 뽑을 수 있습니다.";
            if (Credits < Rules.DrawCost) return "크레딧이 부족합니다.";

            int target = FindRandomEmptySlot();
            if (target < 0) return "빈 슬롯이 없습니다.";
            string error = TryDrawTower(target);
            if (error == null) placedSlot = target;
            return error;
        }

        private int FindRandomEmptySlot()
        {
            var emptySlots = new List<int>();
            for (int slot = 0; slot < Towers.Length; slot++)
            {
                if (Towers[slot] == null) emptySlots.Add(slot);
            }
            return emptySlots.Count == 0 ? -1 : emptySlots[random.Next(emptySlots.Count)];
        }

        public string TryDrawTower(int slot)
        {
            if (Phase != RunPhase.Preparing || !IsValidSlot(slot) || Towers[slot] != null)
                return "준비 중에 빈 슬롯을 선택하세요.";
            if (Credits < Rules.DrawCost) return "크레딧이 부족합니다.";

            if (drawBag.Count == 0)
            {
                for (int type = 0; type < Rules.BuildCost.Length; type++)
                    drawBag.Add((TowerType)type);
            }

            int index = random.Next(drawBag.Count);
            string error = TryPlace(slot, drawBag[index], Rules.DrawCost);
            if (error == null)
            {
                drawBag.RemoveAt(index);
                if (Wave == 1) openingDraws++;
            }
            return error;
        }

        private static bool IsSameTypeAndRank(TowerPlacement other, TowerPlacement tower)
        {
            return other != null && other.Type == tower.Type && other.Rank == tower.Rank;
        }

        public bool TryGetRecipe(TowerType source, out TowerType ingredient, out TowerType result)
        {
            switch (source)
            {
                case TowerType.Artillery:
                case TowerType.Slow:
                    ingredient = source == TowerType.Artillery ? TowerType.Slow : TowerType.Artillery;
                    result = TowerType.FrostCannon;
                    return true;
                case TowerType.Rapid:
                case TowerType.Marksman:
                    ingredient = source == TowerType.Rapid ? TowerType.Marksman : TowerType.Rapid;
                    result = TowerType.Railgun;
                    return true;
                default:
                    ingredient = result = source;
                    return false;
            }
        }

        public int FindRecipeIngredient(TowerType ingredient)
        {
            return Array.FindIndex(Towers, tower => tower != null && tower.Type == ingredient && tower.Rank == 1);
        }

        public bool TryGetMissingIngredient(int slot, out TowerType ingredient, out TowerType result)
        {
            ingredient = result = default;
            return IsValidSlot(slot) && Towers[slot]?.Rank == 1
                && TryGetRecipe(Towers[slot].Type, out ingredient, out result)
                && FindRecipeIngredient(ingredient) < 0;
        }

        public string TryClaimOpeningMaterial(int recipeSlot)
        {
            if (!HasOpeningSupply) return "지금 받을 수 있는 첫 보급이 없습니다.";
            if (!TryGetMissingIngredient(recipeSlot, out TowerType ingredient, out _))
                return "빠진 조합 재료가 있는 초급 포탑을 선택하세요.";

            int slot = FindRandomEmptySlot();
            if (slot < 0) return "빈 슬롯이 없습니다.";

            string error = TryPlace(slot, ingredient, 0);
            if (error == null) openingSupplyClaimed = true;
            return error;
        }

        public string TryClaimOpeningCredits()
        {
            if (!HasOpeningSupply) return "지금 받을 수 있는 첫 보급이 없습니다.";

            Credits += Rules.DrawCost;
            openingSupplyClaimed = true;
            return null;
        }

        public string TryCombine(int slot)
        {
            if (Phase != RunPhase.Preparing || !IsValidSlot(slot) || Towers[slot]?.Rank != 1)
                return "준비 중에 조합할 초급 포탑을 선택하세요.";

            TowerPlacement tower = Towers[slot];
            if (!TryGetRecipe(tower.Type, out TowerType ingredient, out TowerType result))
                return "이 포탑은 조합법이 없습니다.";

            int materialSlot = FindRecipeIngredient(ingredient);
            if (materialSlot < 0) return "조합에 필요한 초급 포탑이 없습니다.";

            tower.Type = result;
            tower.Invested += Towers[materialSlot].Invested;
            Towers[materialSlot] = null;
            return null;
        }

        public string TryClaimSupply(SupplyReward reward, int selectedSlot = -1)
        {
            if (!HasSupplyReward) return "지금 받을 수 있는 지원이 없습니다.";

            switch (reward)
            {
                case SupplyReward.Credits:
                    Credits += Rules.SupplyCredits;
                    break;
                case SupplyReward.Repair:
                    if (CoreHp == Rules.CoreMaxHp) return "코어 내구도가 가득 찼습니다.";
                    CoreHp = Math.Min(Rules.CoreMaxHp, CoreHp + Rules.SupplyRepair);
                    break;
                case SupplyReward.Promotion:
                    if (!IsValidSlot(selectedSlot) || Towers[selectedSlot]?.Rank != 1)
                        return "승급할 초급 포탑을 선택하세요.";
                    Towers[selectedSlot].Rank++;
                    break;
                default:
                    return "지원 종류를 선택하세요.";
            }

            supplyClaimed = true;
            return null;
        }

        public int MatchingTowers(TowerPlacement tower)
        {
            int count = 0;
            foreach (TowerPlacement other in Towers)
            {
                if (IsSameTypeAndRank(other, tower))
                    count++;
            }
            return count;
        }

        public string TryUpgrade(int slot)
        {
            if (Phase != RunPhase.Preparing || !IsValidSlot(slot) || Towers[slot] == null)
                return "타워를 선택하세요.";

            TowerPlacement tower = Towers[slot];
            if (tower.Rank == Rules.MaxRank) return "이미 고급 포탑입니다.";
            if (MatchingTowers(tower) < Rules.MergeCount)
                return "같은 종류와 등급의 포탑이 3개 필요합니다.";

            // 선택한 포탑을 남기고 앞 슬롯부터 같은 종류와 등급의 재료를 소비한다.
            int materialsNeeded = Rules.MergeCount - 1;
            for (int index = 0; index < Towers.Length && materialsNeeded > 0; index++)
            {
                TowerPlacement material = Towers[index];
                if (index == slot || !IsSameTypeAndRank(material, tower)) continue;

                tower.Invested += material.Invested;
                Towers[index] = null;
                materialsNeeded--;
            }

            tower.Rank++;
            return null;
        }

        public string TrySell(int slot)
        {
            if (Phase != RunPhase.Preparing || !IsValidSlot(slot) || Towers[slot] == null)
                return "타워를 선택하세요.";

            Credits += Towers[slot].Invested / 2;
            Towers[slot] = null;
            return null;
        }

        public string TryMove(int from, int to)
        {
            if (Phase != RunPhase.Preparing || !IsValidSlot(from) || !IsValidSlot(to)
                || Towers[from] == null || Towers[to] != null)
                return "이동할 빈 슬롯을 선택하세요.";

            TowerPlacement tower = Towers[from];
            Towers[from] = null;
            Towers[to] = tower;
            tower.SlotId = to + 1;
            return null;
        }

        public void TryStartCombat()
        {
            if (Phase != RunPhase.Preparing) return;

            HasChallengePromotion = false;
            Combat = new CombatSimulation(LockedPlan, Towers, CoreHp);
            Phase = RunPhase.Fighting;
        }

        public string TryChooseChallenge(ChallengeType choice)
        {
            if (!CanChooseChallenge) return "특수 공세 준비 중에 도전을 고를 수 있습니다.";
            switch (choice)
            {
                case ChallengeType.None:
                    LockedPlan = Decision.Selected;
                    break;
                case ChallengeType.Crossfire:
                case ChallengeType.Vanguard:
                    LockedPlan = EnemyDirector.WithChallenge(Decision.Selected,choice);
                    break;
                default:
                    return "도전 종류를 선택하세요.";
            }
            return null;
        }

        public string TryUseChallengePromotion(int slot)
        {
            if (Phase != RunPhase.Preparing || !HasChallengePromotion) return "사용할 승급권이 없습니다.";
            if (!IsValidSlot(slot) || Towers[slot] == null || Towers[slot].Rank >= Rules.MaxRank)
                return "승급할 초급 또는 중급 포탑을 선택하세요.";
            Towers[slot].Rank++;
            HasChallengePromotion = false;
            return null;
        }

        public void StepCombat()
        {
            if (Phase != RunPhase.Fighting) return;

            Combat.Step();
            CoreHp = Combat.CoreHp;
            if (!Combat.Finished) return;

            for (int lane = 0; lane < TotalLeaks.Length; lane++)
                TotalLeaks[lane] += Combat.Leaks[lane];

            LastChallenge = LockedPlan.Challenge;
            LastChallengeWon = CoreHp > 0 && LastChallenge != ChallengeType.None
                && Combat.Leaks[(int)EnemyDirector.ChallengeLane(LockedPlan)] == 0;
            if (CoreHp == 0 || Wave == Rules.FinalWave)
            {
                Phase = RunPhase.Finished;
                return;
            }

            Credits += Wave == 1 ? 35 : 50;
            if (LastChallengeWon)
            {
                if (LastChallenge == ChallengeType.Crossfire) Credits += Rules.ChallengeCreditReward;
                else HasChallengePromotion = true;
            }
            Wave++;
            supplyClaimed = false;

            Decision = director.SelectPlan(Wave, Towers, CoreHp, Combat.Leaks, last, consecutive);
            LockedPlan = Decision.Selected;
            consecutive = last == LockedPlan.Primary ? consecutive + 1 : 1;
            last = LockedPlan.Primary;
            Phase = RunPhase.Preparing;
        }
    }
}
