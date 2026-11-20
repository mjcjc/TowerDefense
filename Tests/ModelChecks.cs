using System;
using System.Linq;
using TowerDefense;

public static class ModelChecks
{
    static void Check(bool condition,string message)
    {
        if(!condition) throw new Exception(message);
        Console.WriteLine("PASS "+message);
    }
    static void Finish(CombatSimulation combat)
    {
        while(!combat.Finished && combat.Tick<12000) combat.Step();
        Check(combat.Finished,"wave terminates");
    }
    public static void Main()
    {
        var empty=new CombatSimulation(EnemyDirector.FirstWave(),new TowerPlacement[8],Rules.CoreMaxHp);
        Finish(empty);
        Check(empty.CoreHp==24 && empty.Leaks[0]==6 && empty.Leaks[1]==2,"opening swarm and scouts keep total core pressure at 12");
        var loneSlime = new RunController();
        loneSlime.LockedPlan.Spawns.RemoveRange(1,loneSlime.LockedPlan.Spawns.Count-1);
        loneSlime.TryStartCombat();
        while(loneSlime.Phase==RunPhase.Fighting) loneSlime.StepCombat();
        Check(loneSlime.Wave==2 && loneSlime.CoreHp==Rules.CoreMaxHp-Rules.SlimeCoreDamage
            && loneSlime.TotalLeaks.Sum()==1,"the first slime damages the core and the loss carries into preparation");
        var swarmTowers=new TowerPlacement[8];
        swarmTowers[0]=new TowerPlacement {SlotId=1,Type=TowerType.Artillery};
        swarmTowers[3]=new TowerPlacement {SlotId=4,Type=TowerType.Artillery};
        var swarm=new CombatSimulation(EnemyDirector.FirstWave(),swarmTowers,Rules.CoreMaxHp);
        int biggestBlast=0;
        while(!swarm.Finished && swarm.Tick<600)
        {
            swarm.Step();
            foreach(var shot in swarm.Shots) biggestBlast=Math.Max(biggestBlast,shot.Kills);
        }
        Check(biggestBlast>=3,"opening artillery blast clears at least three slimes");
        Check(swarm.Finished && swarm.CoreHp==Rules.CoreMaxHp && swarm.Leaks.Sum()==0,"two artillery towers clear the opening within ten seconds");
        var run=new RunController();
        Check(run.TryBuild(0,TowerType.Rapid)==null && run.Credits==70,"build charges 40");
        Check(run.TryUpgrade(0)!=null && run.Credits==70,"merge needs three matching towers");
        Check(run.TrySell(0)==null && run.Credits==90,"sale returns half investment");
        run = new RunController(0);
        Check(run.TryDrawTower(out int firstSlot)==null && run.Towers[firstSlot]!=null && run.Credits==85,"first auto draw places and charges once");
        Check(run.TryDrawTower(out int secondSlot)==null && secondSlot!=firstSlot && run.Towers[firstSlot]!=null,"auto draw skips occupied slots");
        var anotherRun = new RunController(1);
        Check(anotherRun.TryDrawTower(out int differentSlot)==null && differentSlot!=firstSlot,"different seeds can pick different opening slots");
        int moveTarget = Array.FindIndex(run.Towers,t=>t==null);
        Check(run.TryMove(secondSlot,moveTarget)==null,"opening auto draw can be repositioned");
        Check(run.TryDrawTower(out _)==null && run.TryDrawTower(out _)==null,"auto draw supports consecutive purchases");
        Check(run.TryDrawTower(out int failedSlot)!=null && failedSlot==-1 && run.Credits==10 && run.Towers.Count(t=>t!=null)==4,"unaffordable draw leaves board and credits intact");
        run.TryStartCombat();
        Check(run.TryDrawTower(out failedSlot)!=null && failedSlot==-1 && run.Credits==10,"auto draw is rejected during combat");
        run = new RunController(0);
        for (int slot=0; slot<run.Towers.Length; slot++)
            run.Towers[slot] = new TowerPlacement {SlotId=slot+1};
        Check(run.TryDrawTower(out failedSlot)!=null && failedSlot==-1 && run.Credits==110,"full board draw leaves credits intact");
        var director=new EnemyDirector();
        int[] budget={0,8,14,20,26,32,40};
        for(int wave=2;wave<=6;wave++)
        {
            if (wave == 5) continue;
            var decision=director.SelectPlan(wave,new TowerPlacement[8],Rules.CoreMaxHp,new int[2],null,0);
            foreach(var candidate in decision.Evaluations)
            {
                var plan=candidate.Plan;
                int slimes=plan.Spawns.Count(s=>s.IsSwarmSlime);
                int formationBudget=plan.Spawns.Where(s=>!s.IsSwarmSlime).Sum(s=>Rules.BudgetCost[(int)s.Type]);
                Check(formationBudget+slimes/Rules.SwarmPackSize*Rules.BudgetCost[(int)EnemyType.Grunt]==budget[wave],
                    "wave "+wave+" formation budget");
                int expectedDamage=plan.Spawns.Where(s=>!s.IsSwarmSlime).Sum(s=>Rules.CoreDamage[(int)s.Type])
                    +slimes*Rules.SlimeCoreDamage;
                Check(candidate.Damage==expectedDamage,"wave "+wave+" mixed formation core damage");
                Check(plan.Spawns.GroupBy(s=>s.Lane).All(lane=>
                {
                    var ticks=lane.Where(s=>s.IsSwarmSlime).Select(s=>s.Tick).ToArray();
                    return ticks.Length>=3 && ticks.Length%3==0 &&
                        Enumerable.Range(0,ticks.Length/3).All(i=>ticks[i*3+2]-ticks[i*3]==10);
                }),"wave "+wave+" slime packs on both lanes");
                int scouts=plan.Spawns.Count(s=>s.Type==EnemyType.Scout);
                int expected=wave==2?2:wave==3?(plan.Template==1?3:0):wave==6?2:0;
                Check(scouts==expected,"wave "+wave+" scout count");
                Check(plan.Spawns.Zip(plan.Spawns.Skip(1),(a,b)=>a.Tick<=b.Tick).All(x=>x),"spawn order");
            }
        }
        var restricted=director.SelectPlan(3,new TowerPlacement[8],Rules.CoreMaxHp,new int[2],Lane.North,2);
        Check(restricted.Evaluations.All(e=>e.Plan.Primary==Lane.South),"consecutive lane limit");
        run=new RunController();
        run.TryStartCombat();
        while(run.Phase==RunPhase.Fighting)run.StepCombat();
        Check(run.Wave==2 && run.CoreHp==24 && run.Credits==145,"first clear settlement once");
        run.TryBuild(0,TowerType.Rapid);
        Check(run.TryMove(0,1)==null && run.TryMove(1,2)==null && run.TryMove(2,0)==null && run.Towers[0].SlotId==1,
            "a tower can move repeatedly during preparation");
        run.TryBuild(1,TowerType.Slow);
        Check(run.TryMove(0,1)!=null && run.Towers[0].Type==TowerType.Rapid && run.Towers[1].Type==TowerType.Slow,
            "moving to an occupied slot preserves both towers");
        run.TryStartCombat();
        Check(run.TryMove(0,2)!=null && run.Towers[0]!=null && run.Towers[2]==null,"moving during combat is rejected");
        run.StartNewRun();
        Check(run.Wave==1 && run.Credits==110 && run.Towers.All(t=>t==null) && run.TotalLeaks.Sum()==0,"retry reset");
        var towers=new TowerPlacement[8];
        towers[0]=new TowerPlacement {SlotId=1,Type=TowerType.Rapid};
        towers[3]=new TowerPlacement {SlotId=4,Type=TowerType.Rapid};
        var a=new CombatSimulation(EnemyDirector.FirstWave(),towers,Rules.CoreMaxHp);
        var b=new CombatSimulation(EnemyDirector.FirstWave(),towers,Rules.CoreMaxHp,true);
        Finish(a);Finish(b);
        Check(a.Damage==b.Damage && a.Tick==b.Tick,"live prediction parity");
        CheckWeaponRecipes();
        CheckOpeningSupply();
        CheckMilestoneAssaults();
        CheckChallenges();
        CheckSurvival();
    }

    static RunController ReachFirstChallenge()
    {
        var run = new RunController(0);
        while (run.Wave < 5 && run.Phase != RunPhase.Finished)
        {
            while (run.Credits >= Rules.DrawCost && run.TryDrawTower(out _) == null) { }
            run.TryStartCombat();
            while (run.Phase == RunPhase.Fighting) run.StepCombat();
        }
        Check(run.Wave == 5 && run.CanChooseChallenge, "special assault offers optional challenges");
        return run;
    }

    static void CheckChallenges()
    {
        var crossfire = ReachFirstChallenge();
        WavePlan baseline = crossfire.LockedPlan;
        int regularCount = baseline.Spawns.Count;
        Check(crossfire.TryChooseChallenge(ChallengeType.Crossfire) == null
            && crossfire.LockedPlan.Spawns.Count > regularCount
            && baseline.Spawns.Count == regularCount
            && EnemyDirector.ChallengeLane(crossfire.LockedPlan) != baseline.Primary,
            "crossfire adds enemies to the other lane without changing the baseline");
        Check(crossfire.TryChooseChallenge(ChallengeType.Vanguard) == null
            && crossfire.LockedPlan.Spawns.Count == regularCount+4
            && EnemyDirector.ChallengeLane(crossfire.LockedPlan) == baseline.Primary
            && crossfire.TryChooseChallenge(ChallengeType.None) == null && crossfire.LockedPlan == baseline,
            "switching and clearing a challenge reuses the original plan without stacking reinforcements");

        crossfire.TryChooseChallenge(ChallengeType.Crossfire);
        crossfire.LockedPlan.Spawns.Clear();
        int credits = crossfire.Credits;
        crossfire.TryStartCombat();
        Check(crossfire.TryChooseChallenge(ChallengeType.None)!=null,"a started challenge cannot be canceled");
        crossfire.StepCombat();
        crossfire.StepCombat();
        Check(crossfire.LastChallengeWon && crossfire.Credits == credits+50+Rules.ChallengeCreditReward,
            "successful credit challenge pays once");

        var vanguard = ReachFirstChallenge();
        vanguard.TryChooseChallenge(ChallengeType.Vanguard);
        vanguard.LockedPlan.Spawns.Clear();
        vanguard.TryStartCombat();
        vanguard.StepCombat();
        int slot = Array.FindIndex(vanguard.Towers,t=>t!=null);
        Check(vanguard.HasChallengePromotion && vanguard.TryUseChallengePromotion(-1)!=null
            && vanguard.TryUseChallengePromotion(slot)==null && vanguard.Towers[slot].Rank==2
            && vanguard.TryUseChallengePromotion(slot)!=null,
            "successful vanguard grants one promotion and an invalid selection does not consume it");

        var leak = ReachFirstChallenge();
        leak.TryChooseChallenge(ChallengeType.Crossfire);
        Lane target = EnemyDirector.ChallengeLane(leak.LockedPlan);
        leak.LockedPlan.Spawns.Clear();
        leak.LockedPlan.Spawns.Add(new SpawnEntry { Lane=target, Tick=1, Type=EnemyType.Grunt, IsSwarmSlime=true });
        for (int i=0;i<leak.Towers.Length;i++)
            if (leak.Towers[i] != null) leak.TrySell(i);
        credits = leak.Credits;
        leak.TryStartCombat();
        while (leak.Phase == RunPhase.Fighting) leak.StepCombat();
        Check(leak.LastChallenge==ChallengeType.Crossfire && !leak.LastChallengeWon && leak.Credits==credits+50,
            "a leak on the challenge lane forfeits only the bonus");
    }

    static void CheckOpeningSupply()
    {
        var run = new RunController(0);
        Check(!run.HasOpeningSupply && run.TryClaimOpeningCredits()!=null,"opening supply requires two draws");
        Check(run.TryDrawTower(out _) == null && !run.HasOpeningSupply
            && run.TryDrawTower(out _) == null && run.HasOpeningSupply,"second draw unlocks opening supply");
        Check(run.TryClaimOpeningMaterial(-1)!=null && run.HasOpeningSupply,"invalid opening material preserves the choice");
        int source = Enumerable.Range(0,run.Towers.Length).First(slot=>run.TryGetMissingIngredient(slot,out _,out _));
        run.TryGetMissingIngredient(source,out TowerType ingredient,out TowerType weapon);
        int credits = run.Credits;
        Check(run.TryClaimOpeningMaterial(source)==null && run.Credits==credits
            && run.FindRecipeIngredient(ingredient)>=0 && !run.HasOpeningSupply
            && run.TryClaimOpeningCredits()!=null,"opening material grants one missing tower without charging");
        Check(run.TryCombine(source)==null && run.Towers[source].Type==weapon,"opening supply allows a weapon recipe before combat");
        run.StartNewRun();
        Check(!run.HasOpeningSupply && run.TryDrawTower(out _)==null
            && run.TryDrawTower(out _)==null && run.TryClaimOpeningCredits()==null
            && run.Credits==110-Rules.DrawCost && run.TryClaimOpeningCredits()!=null,
            "a fresh run can choose one extra draw instead of material");
    }

    static void CheckMilestoneAssaults()
    {
        var director = new EnemyDirector();
        var emptyTowers = new TowerPlacement[Rules.Slots.Length];
        var swarm = director.SelectPlan(5,emptyTowers,Rules.CoreMaxHp,new int[2],null,0).Selected;
        Check(swarm.Assault==AssaultType.Swarm
            && swarm.Spawns.Count(s=>s.Lane==swarm.Primary && s.IsSwarmSlime)==36
            && swarm.Spawns.Any(s=>s.Lane!=swarm.Primary),"milestone swarm packs the primary lane and keeps the other lane active");

        int primarySlot = swarm.Primary==Lane.North ? 0 : 3;
        int secondarySlot = swarm.Primary==Lane.North ? 3 : 0;
        var reinforced = new TowerPlacement[Rules.Slots.Length];
        var unreinforced = new TowerPlacement[Rules.Slots.Length];
        reinforced[primarySlot] = new TowerPlacement {SlotId=primarySlot+1,Type=TowerType.FrostCannon};
        unreinforced[secondarySlot] = new TowerPlacement {SlotId=secondarySlot+1,Type=TowerType.FrostCannon};
        var ready = new CombatSimulation(swarm,reinforced,Rules.CoreMaxHp,prediction:true);
        var misplaced = new CombatSimulation(swarm,unreinforced,Rules.CoreMaxHp,prediction:true);
        Finish(ready);
        Finish(misplaced);
        Check(ready.Damage<misplaced.Damage,$"reinforcing the forecast lane reduces predicted damage ({misplaced.Damage} -> {ready.Damage})");

        var armored = director.SelectPlan(10,emptyTowers,Rules.CoreMaxHp,new int[2],null,0).Selected;
        var attack = new CombatSimulation(armored,emptyTowers,Rules.CoreMaxHp,prediction:true);
        while(attack.Tick<35) attack.Step();
        int normalHeavyHp = Rules.Hp[(int)EnemyType.Heavy] * (100 + (armored.Wave - 8) * 12) / 100;
        Check(armored.Assault==AssaultType.Armored
            && armored.Spawns.Count(s=>s.Lane==armored.Primary && s.Type==EnemyType.Heavy)==4
            && attack.Enemies.Any(e=>e.Lane==armored.Primary && e.Type==EnemyType.Heavy && e.MaxHp>normalHeavyHp),
            "armored assault opens with reinforced heavies");
    }

    static void CheckWeaponRecipes()
    {
        var run = new RunController(3);
        Check(run.TryBuild(0,TowerType.FrostCannon)!=null,"combined weapons are recipe only");
        Check(run.TryDrawTower(out _) == null && run.TryDrawTower(out _) == null
            && run.TryDrawTower(out _) == null && run.TryDrawTower(out _) == null
            && run.Towers.Where(t=>t!=null).Select(t=>t.Type).Distinct().Count()==4
            && run.Towers.Where(t=>t!=null).All(t=>(int)t.Type<Rules.BuildCost.Length),
            "draw bag contains distinct base weapons");

        run.StartNewRun();
        run.TryBuild(0,TowerType.Artillery);
        Check(run.TryCombine(0)!=null && run.Towers[0].Type==TowerType.Artillery,"missing recipe material preserves the selected tower");
        run.TryBuild(3,TowerType.Slow);
        Check(run.TryCombine(0)==null && run.Towers[0].Type==TowerType.FrostCannon
            && run.Towers[3]==null && run.Towers[0].Invested==85 && run.Credits==25,
            "frost cannon consumes the other tower and preserves investment");
        run.TryStartCombat();
        Check(run.TryCombine(0)!=null,"weapon combination is unavailable during combat");
        bool fired = false;
        bool slowed = false;
        while (run.Phase==RunPhase.Fighting)
        {
            run.StepCombat();
            fired |= run.Combat.Shots.Any(shot => shot.Slot==0 && shot.Kills>=2);
            slowed |= run.Combat.Enemies.Any(enemy => enemy.EffectiveSpeed<2.4);
        }
        Check(fired && slowed,"frost cannon applies slow and kills several enemies with one shot");

        run.StartNewRun();
        run.TryBuild(0,TowerType.Rapid);
        run.TryBuild(3,TowerType.Marksman);
        Check(run.TryCombine(3)==null && run.Towers[3].Type==TowerType.Railgun && run.Towers[0]==null
            && Rules.Range[(int)TowerType.Railgun]==Rules.Range[(int)TowerType.Marksman]
            && Rules.AttackInterval[(int)TowerType.Railgun]<Rules.AttackInterval[(int)TowerType.Marksman],
            "railgun keeps marksman range and shoots more often");
    }

    static void CheckSurvival()
    {
        bool checkedRepair = false;
        bool checkedPromotion = false;
        foreach(int seed in new[]{0,1,2})
        {
            var run=new RunController(seed);
            bool madeAdvanced=false;
            while(run.Phase!=RunPhase.Finished)
            {
                if (run.HasSupplyReward)
                {
                    Check(run.Wave % Rules.SupplyInterval == 0, "supply appears on milestone waves");
                    Check(run.TryClaimSupply(SupplyReward.Promotion) != null && run.HasSupplyReward,
                        "invalid promotion leaves the supply choice available");
                    int beginner = Array.FindIndex(run.Towers, t => t?.Rank == 1);
                    if (!checkedRepair && run.CoreHp < Rules.CoreMaxHp)
                    {
                        int repaired = Math.Min(Rules.CoreMaxHp, run.CoreHp + Rules.SupplyRepair);
                        Check(run.TryClaimSupply(SupplyReward.Repair) == null && run.CoreHp == repaired, "supply repairs the core up to its maximum");
                        checkedRepair = true;
                    }
                    else if (!checkedPromotion && beginner >= 0)
                    {
                        int invested = run.Towers[beginner].Invested;
                        Check(run.TryClaimSupply(SupplyReward.Promotion, beginner) == null
                            && run.Towers[beginner].Rank == 2 && run.Towers[beginner].Invested == invested,
                            "supply promotes one beginner without adding resale value");
                        checkedPromotion = true;
                    }
                    else
                    {
                        int credits = run.Credits;
                        Check(run.TryClaimSupply(SupplyReward.Credits) == null && run.Credits == credits + Rules.SupplyCredits,
                            "supply grants credits");
                    }
                    Check(!run.HasSupplyReward && run.TryClaimSupply(SupplyReward.Credits) != null, "only one supply reward can be claimed per milestone");
                }

                while(true)
                {
                    int merge=Array.FindIndex(run.Towers,t=>t!=null && t.Rank<Rules.MaxRank && run.MatchingTowers(t)>=Rules.MergeCount);
                    if(merge>=0)
                    {
                        int before=run.Towers.Count(t=>t!=null);
                        if(run.TryUpgrade(merge)!=null || run.Towers.Count(t=>t!=null)!=before-Rules.MergeCount+1)
                            throw new Exception("Merge did not consume the matching materials");
                        madeAdvanced|=run.Towers[merge].Rank==Rules.MaxRank;
                        continue;
                    }
                    if(run.Credits<Rules.DrawCost || run.Towers.All(t=>t!=null)) break;
                    if(run.TryDrawTower(out _)!=null) throw new Exception("Auto draw failed with an empty slot");
                }
                run.TryStartCombat();
                while(run.Phase==RunPhase.Fighting)run.StepCombat();
            }
            Check(run.Won && run.Wave==30 && madeAdvanced,$"seed {seed} wins 30 waves with an advanced merge (core {run.CoreHp}/{Rules.CoreMaxHp})");
        }
        Check(checkedRepair && checkedPromotion, "repair and promotion were exercised during full runs");
    }
}
