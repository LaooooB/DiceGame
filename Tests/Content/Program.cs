using System.Diagnostics;
using System.Text.Json;
using DiceGame.Core;
using DiceGame.App;

namespace DiceGame.ContentTests;
internal static class Program
{
    static readonly GameData Data = GameData.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Data"));
    static readonly List<object> Results = [];
    static int Passed, Failed;
    static int Center => Data.Game.Board.Columns + 1;
    static int Left => Center - 1;
    static int Right => Center + 1;
    static int RowEnd => (Center / Data.Game.Board.Columns + 1) * Data.Game.Board.Columns - 1;
    static int NextRow => RowEnd + 1;
    static void MainTest(string name, Action action)
    {
        try { action(); Passed++; Results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
        catch (Exception e) { Failed++; Results.Add(new { name, passed = false, error = e.ToString() }); Console.WriteLine("FAIL " + name + "\n" + e); }
    }
    static int Main()
    {
        MainTest("catalog: 47 dice / 188 branches / five rarities", () =>
        {
            Eq(Data.Dice.Length, 47); Eq(Data.Skills.Values.Sum(s => s.Level3.Length + s.Level6.Length), 188);
            Eq(Data.Dice.Count(d => d.Rarity == "common"), 11); Eq(Data.Dice.Count(d => d.Rarity == "rare"), 13);
            Eq(Data.Dice.Count(d => d.Rarity == "epic"), 10); Eq(Data.Dice.Count(d => d.Rarity == "legendary"), 8); Eq(Data.Dice.Count(d => d.Rarity == "mythic"), 5);
            Eq(Data.Dice.Count(d => d.AvailableFromStart), 41);
        });
        MainTest("authoring: malformed rarity, trait, negative resolved profile and probability sum rejected", () =>
        {
            void Bad(Action<DiceDefinition[]> change) { var defs = CampaignCatalog.Copy(Data.Dice); change(defs); Throws(() => new GameData(J(Data.Game), J(defs), J(Data.Upgrades), J(Data.Skills.Values))); }
            Bad(a => a[0].Rarity = "mythic"); Bad(a => a[0].Traits["echChance"] = .2);
            Bad(a => a.Single(x => x.Id == "gamble").Traits["jackpotChance"] = .9);
            var sets = CampaignCatalog.Copy(Data.Skills.Values.ToArray()); sets.Single(s => s.Type == "charge").Level3[1].Modifiers.ContentAdd["chargeMax"] = -100;
            Throws(() => new GameData(J(Data.Game), J(Data.Dice), J(Data.Upgrades), J(sets)));
        });
        MainTest("availability: new and existing campaigns expose configurable additions without expanding decks", () =>
        {
            var catalog = new CampaignCatalog(Data, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data/campaign.json")));
            var store = new Store(); var app = new GameApp(Data, store, new Silent(), catalog) { NativeUi = true };
            Eq(app.Campaign!.UnlockedDice.Count, 47); Eq(app.Deck.Count, 6); app.Save();
            var save = SaveCodec.Decode(Data, store.Value!); save.Campaign!.UnlockedDice = Data.DefaultDeck.ToHashSet(); store.Value = SaveCodec.Encode(save);
            var loaded = new GameApp(Data, store, new Silent(), catalog); Eq(loaded.Campaign!.UnlockedDice.Count, 47); Eq(loaded.Deck.Count, 6);
            Check(!Data.ValidDeck(Data.Dice.Select(d => d.Id)));
        });
        foreach (var type in Data.Dice.Select(d => d.Id))
        {
            string id = type;
            MainTest("result-bound choices and save continuation / " + id, () => ChoiceFlow(id));
            MainTest("all four final builds execute, persist and respect budgets / " + id, () =>
            {
                foreach (string a in new[] { "A", "B" }) foreach (string b in new[] { "C", "D" })
                {
                    var s = Sim(id); var d = Die(s, id, 6, a, b, Center); d.Charge = 3; d.Streak = 12; d.GrowthKills = 400; d.BadRolls = 23;
                    Die(s, "pulse", 6, "A", "C", Left); Die(s, "pulse", 6, "B", "D", Right);
                    Enemy(s, 216, 220); Enemy(s, 216, 270, "boss"); Enemy(s, 240, 230, "armored");
                    var q = s.Stats(d); Check(double.IsFinite(q.Volley) && q.Volley > 0 && q.Reload >= .06);
                    Near(q.Damage * q.Count, q.Volley); Check(q.Count <= 18 && q.Pierces <= 8 && q.ChildCount <= 10);
                    Check(s.Fire(-Math.PI / 2).Ok); Clock(s, 1);
                    var copy = Simulation.Restore(Data, s.ExportSave());
                    for (int i = 0; i < 20; i++) { s.Step(.05); copy.Step(.05); }
                    Eq(J(s.ExportSave()), J(copy.ExportSave()));
                    Check(s.State.PendingShots.Count <= Data.Game.Rules.MaxQueuedShots && s.State.Projectiles.Count <= Data.Game.Rules.MaxProjectiles);
                }
            });
        }
        MainTest("cannon A/B: heavy payload and wide splash are real stats", () =>
        { var s = Sim("cannon"); var d = Die(s,"cannon",3);var q=s.Stats(d);d.Tier3="A";Near(s.Stats(d).Volley,q.Volley*1.35);Near(s.Stats(d).Reload,q.Reload*1.2);d.Tier3="B";Near(s.Stats(d).BlastRadius,q.BlastRadius*1.8);Near(s.Stats(d).SplashFactor,q.SplashFactor*2); });
        MainTest("cannon C/D: armor bonus and one delayed blast", () =>
        { var s=Sim("cannon");var d=Die(s,"cannon",6,"A","C");var e=Enemy(s,216,220,"armored");var p=Shot(s,d);double hp=e.Hp;s.PrimaryHit(e,p);Near(hp-e.Hp,p.Stats.Damage*1.8);d.Tier6="D";p=Shot(s,d);s.PrimaryHit(e,p);s.PrimaryHit(e,p);Eq(s.State.TimedHits.Count,1);hp=e.Hp;Clock(s,.3);Check(e.Hp<hp);Eq(s.State.TimedHits.Count,0); });
        MainTest("pierce A/B: retention removed and actual extra pierces", () =>
        { var s=Sim("pierce");var d=Die(s,"pierce",3,"A");Near(s.Stats(d).Trait("pierceRetention"),1);d.Tier3="B";Eq(s.Stats(d).Pierces,5); });
        MainTest("pierce C/D: third penetration pays once and depth increases damage", () =>
        { var s=Sim("pierce");var d=Die(s,"pierce",6,"A","C");var p=Shot(s,d);p.Pierced=3;var e=Enemy(s,216,220);double energy=s.State.Energy;s.PrimaryHit(e,p);s.PrimaryHit(e,p);Near(s.State.Energy-energy,.5);d.Tier6="D";p=Shot(s,d);p.Pierced=4;double hp=e.Hp;s.PrimaryHit(e,p);Near(hp-e.Hp,p.Stats.Damage*1.48); });
        MainTest("overlapping pierced colliders are not farmed before leaving bounds", () =>
        { var s=Sim("pierce");var d=Die(s,"pierce",6,"A","C");var a=Enemy(s,216,250);var b=Enemy(s,216,250);s.Grid.Rebuild(s.State.Enemies);var p=Shot(s,d,216,300);s.MoveProjectile(p,.15);Near(a.MaxHp-a.Hp,p.Stats.Damage);Near(b.MaxHp-b.Hp,p.Stats.Damage);Check(p.Vy<0); });
        MainTest("hunt A/B: boss specialization and wider aim cone", () =>
        { var s=Sim("hunt");var d=Die(s,"hunt",3,"A");Near(s.Stats(d).BossDamageMultiplier,1.5);d.Tier3="B";Near(s.Stats(d).Trait("seekCone"),.453785605); });
        MainTest("hunt acquisition follows manual direction and highest current HP, never auto-fires", () =>
        { var s=Sim("hunt");var d=Die(s,"hunt",1);var low=Enemy(s,200,220);var high=Enemy(s,240,220);high.Hp=2e6;high.MaxHp=2e6;Enemy(s,40,220).Hp=1e9;Clock(s,.5);Eq(s.State.PendingShots.Count,0);s.Fire(-Math.PI/2);Near(s.State.PendingShots[0].Angle,Math.Atan2(high.Y-530,high.X-216)); });
        MainTest("hunt C/D: finisher conditional and target mark amplifies later damage", () =>
        { var s=Sim("hunt");var d=Die(s,"hunt",6,"A","C");var e=Enemy(s,216,220);e.Hp=e.MaxHp*.1;var p=Shot(s,d);double hp=e.Hp;s.PrimaryHit(e,p);Near(hp-e.Hp,p.Stats.Damage*2);d.Tier6="D";s.PrimaryHit(e,Shot(s,d));hp=e.Hp;s.ApplyDamage(e,100,"#FFFFFF");Near(hp-e.Hp,115); });
        MainTest("storm A/B: higher cap and damage/haste tradeoff", () =>
        { var s=Sim("storm");var d=Die(s,"storm",3,"A");var e=Enemy(s,216,220);for(int i=0;i<30;i++)s.PrimaryHit(e,Shot(s,d));Eq(d.Streak,20);d.Tier3="B";var q=s.Stats(d);Near(q.Trait("rampHaste"),.02);Check(q.Volley>11*Data.Game.Levels[2].VolleyPower/3); });
        MainTest("storm C/D: streak transfer and bounded full-streak shock", () =>
        { var s=Sim("storm");var d=Die(s,"storm",6,"A","C");var a=Enemy(s,216,220);var b=Enemy(s,225,220);d.Streak=10;d.LastTarget=a.Id;s.PrimaryHit(b,Shot(s,d));Eq(d.Streak,6);d.Tier6="D";d.Streak=20;d.LastTarget=a.Id;var p=Shot(s,d);s.PrimaryHit(a,p);int n=s.State.DamageQueue.Count;s.PrimaryHit(a,p);Eq(s.State.DamageQueue.Count,n);Check(n>0); });
        MainTest("poison A/B: concentration, per-hit layers and cap", () =>
        { var s=Sim("poison");var d=Die(s,"poison",3,"A");var e=Enemy(s,216,220);var p=Shot(s,d);s.PrimaryHit(e,p);Near(e.Poison[0].DamagePerSecond,p.Stats.Damage*.25*1.45);e.Poison.Clear();d.Tier3="B";s.PrimaryHit(e,Shot(s,d));Eq(e.Poison.Count,2);for(int i=0;i<20;i++)s.PrimaryHit(e,Shot(s,d));Eq(e.Poison.Count,16); });
        MainTest("poison C/D: one-generation spread and consuming detonation", () =>
        { var s=Sim("poison");var d=Die(s,"poison",6,"A","C");var a=Enemy(s,216,220);var b=Enemy(s,240,220);s.PrimaryHit(a,Shot(s,d));s.ApplyDamage(a,1e8,"#FFFFFF",d.Id);Check(b.Poison.Count>0&&b.Poison.All(x=>!x.Spread));var c=Enemy(s,300,220);s.ApplyDamage(b,1e8,"#FFFFFF",d.Id);Eq(c.Poison.Count,0);d.Tier6="D";c.X=216;for(int i=0;i<10;i++)s.PrimaryHit(c,Shot(s,d));Eq(c.Poison.Count,5); });
        MainTest("poison expiration and stronger-stack attribution survive weaker replacement attempts", () =>
        { var s=Sim("poison");var d=Die(s,"poison",6,"A","C");var e=Enemy(s,216,220);for(int i=0;i<10;i++)s.PrimaryHit(e,Shot(s,d));double strength=e.Poison.Min(p=>p.DamagePerSecond);var weak=Shot(s,d);weak.Stats.Damage/=100;s.PrimaryHit(e,weak);Near(e.Poison.Min(p=>p.DamagePerSecond),strength);double hp=e.Hp;Clock(s,4.2);Check(e.Hp<hp);Eq(e.Poison.Count,0); });
        MainTest("echo A/B: effective probability and copy damage differ", () =>
        {var s=Sim("echo");var d=Die(s,"echo",3,"A");Near(s.Stats(d).Trait("echoChance"),.45);Near(s.Stats(d).Trait("echoFactor"),.7);d.Tier3="B";Near(s.Stats(d).Trait("echoChance"),.15);Near(s.Stats(d).Trait("echoFactor"),1.8);});
        MainTest("echo C/D: only two snapshots, and neighbors receive the aura", () =>
        {var s=Sim("echo");var d=Die(s,"echo",6,"A","C",Center);ForceRoll(s,.1);s.QueueVolley(d,Center,-1.5,1,false);Eq(s.State.PendingShots.Count,18);Check(s.State.PendingShots.Count(x=>x.Snapshot.Stats.Trait("rootFirst")>0)==1);d.Tier6="D";var ally=Die(s,"pulse",1,"","",Right);Near(s.Stats(ally).Trait("echoChance"),.12);});
        MainTest("sacrifice A/B: each input refund and adjacent damage are applied", () =>
        {var s=Sim("sacrifice");var a=Die(s,"sacrifice",3,"A","",0);Die(s,"sacrifice",3,"A","",1);double energy=s.State.Energy;double refund=2*Math.Min(s.SummonCost*.8,4*1.5*(1+.12*2));Check(s.Merge(0,1).Ok);Near(s.State.Energy-energy,refund);s=Sim("sacrifice");var support=Die(s,"sacrifice",3,"B","",0);var ally=Die(s,"pulse",1,"","",1);double boosted=s.Stats(ally).Volley;s.State.Board[0]=null;Near(boosted,s.Stats(ally).Volley*1.1);});
        MainTest("sacrifice C/D: no six-pip merge requirement, no repeated income per bounce", () =>
        {var s=Sim("sacrifice");var d=Die(s,"sacrifice",6,"A","C");var e=Enemy(s,216,220);s.QueueVolley(d,0,-1.5,1,false);var p=s.MakeProjectile(216,300,-1.5,s.State.PendingShots[0].Snapshot);double energy=s.State.Energy;for(int i=0;i<5;i++)s.PrimaryHit(e,p);Near(s.State.Energy-energy,3);d.Tier6="D";int expected=Data.Game.Levels[5].Recycle+24+(int)Math.Floor(6*.5*1.6);Eq(s.RecycleValue(d),expected);Eq(s.Recycle(0).Amount,expected);});
        MainTest("charge A/B: capacity and rate; waiting is not automatic firing", () =>
        {var s=Sim("charge");var d=Die(s,"charge",3,"A");Clock(s,6.2);Near(d.Charge,6);Eq(s.State.Shots,0L);double charged=s.Stats(d).Volley;d.Charge=0;Near(charged,s.Stats(d).Volley*3.7);d.Tier3="B";Clock(s,3);Near(d.Charge,2);Near(s.Stats(d).Trait("chargeRate"),.8);});
        MainTest("charge C/D: captured pierces and retained charge", () =>
        {var s=Sim("charge");var d=Die(s,"charge",6,"A","C");d.Charge=3;s.Fire(-1.5);Eq(s.State.PendingShots[0].Snapshot.Stats.Pierces,3);Near(d.Charge,0);d.Tier6="D";d.Cooldown=0;d.Charge=4;s.Fire(-1.5);Near(d.Charge,1.8);});
        MainTest("resonance A/B: one-pip tolerance and haste need actual neighbors", () =>
        {var s=Sim("resonance");var d=Die(s,"resonance",3,"A","",Center);var ally=Die(s,"pulse",2,"","",Right);double v=s.Stats(d).Volley;s.State.Board[Right]=null;Near(v,s.Stats(d).Volley*1.1);s.State.Board[Right]=ally;ally.Pips=3;ally.Tier3="A";d.Tier3="B";double reload=s.Stats(d).Reload;s.State.Board[Right]=null;Near(reload,s.Stats(d).Reload/1.06);});
        MainTest("resonance C/D: outbound same-pip aura and isolated bonus", () =>
        {var s=Sim("resonance");var d=Die(s,"resonance",6,"A","C",Center);var ally=Die(s,"pulse",6,"A","C",Right);double v=s.Stats(ally).Volley;s.State.Board[Center]=null;Near(v,s.Stats(ally).Volley*1.18);s.State.Board[Center]=d;s.State.Board[Right]=null;d.Tier6="D";v=s.Stats(d).Volley;d.Tier6="";Near(v,s.Stats(d).Volley*1.75);});
        MainTest("rage A/B: health and enemy position, not board position", () =>
        {var s=Sim("rage");var d=Die(s,"rage",3,"A");s.State.Health=6;var e=Enemy(s,216,300);var p=Shot(s,d);double hp=e.Hp;s.PrimaryHit(e,p);Near(hp-e.Hp,p.Stats.Damage*(1+1.5*(300-136)/376)*1.25);d.Tier3="B";p=Shot(s,d);hp=e.Hp;s.PrimaryHit(e,p);Near(hp-e.Hp,p.Stats.Damage*(1+2.2*(300-136)/376)*.75);});
        MainTest("rage C/D: breach window and shared knockback gate", () =>
        {var s=Sim("rage");var d=Die(s,"rage",6,"A","C");var e=Enemy(s,216,470);s.State.RageUntil=5;var p=Shot(s,d);double hp=e.Hp;s.PrimaryHit(e,p);Near(hp-e.Hp,p.Stats.Damage*(1+1.5*(470-136)/376)*2);d.Tier6="D";p=Shot(s,d);double y=e.Y;s.PrimaryHit(e,p);s.PrimaryHit(e,p);Near(e.Y,y-3);});
        MainTest("prism A/B: real extra pierce and nonrecursive weak child", () =>
        {var s=Sim("prism");var d=Die(s,"prism",3,"A","",Center);var ally=Die(s,"pulse",1,"","",Right);Eq(s.Stats(ally).Pierces,2);d.Tier3="B";var e=Enemy(s,216,220);var p=Shot(s,ally);s.PrimaryHit(e,p);Eq(s.State.Projectiles.Count,1);var child=s.State.Projectiles[0];Near(child.Stats.Damage,p.Stats.Damage*.25);s.PrimaryHit(e,child);Eq(s.State.Projectiles.Count,1);});
        MainTest("prism C/D: focused neighbor and same-row boundaries", () =>
        {var s=Sim("prism");var d=Die(s,"prism",6,"A","C",Center);var ally=Die(s,"pulse",1,"","",Right);double v=s.Stats(ally).Volley;s.State.Board[Center]=null;Near(v,s.Stats(ally).Volley*1.18);s.State.Board[Center]=d;d.Tier6="D";s.State.Board[Right]=null;s.State.Board[RowEnd]=ally;Eq(s.Stats(ally).Pierces,2);s.State.Board[RowEnd]=null;s.State.Board[NextRow]=ally;Eq(s.Stats(ally).Pierces,0);});
        MainTest("parasite A/B: instance kills reach different growth curves", () =>
        {var s=Sim("parasite");var d=Die(s,"parasite",3,"A");double v=s.Stats(d).Volley;for(int i=0;i<10;i++)s.ApplyDamage(Enemy(s,216,220),1e8,"#FFFFFF",d.Id);Near(s.Stats(d).Volley,v*1.05);d.Tier3="B";d.GrowthKills=400;v=s.Stats(d).Volley;d.GrowthKills=0;Near(v,s.Stats(d).Volley*1.8);});
        MainTest("parasite C/D: assists counted once and mature aura", () =>
        {var s=Sim("parasite");var d=Die(s,"parasite",6,"A","C",Center);var e=Enemy(s,216,220);s.PrimaryHit(e,Shot(s,d));s.ApplyDamage(e,1e8,"#FFFFFF");s.ApplyDamage(e,1e8,"#FFFFFF",d.Id);Eq(d.GrowthKills,1L);d.Tier6="D";d.GrowthKills=100;var ally=Die(s,"pulse",1,"","",Right);double v=s.Stats(ally).Volley;s.State.Board[Center]=null;Near(v,s.Stats(ally).Volley*1.18);});
        MainTest("source identity: old shots and DOT cannot donate growth to replacement die", () =>
        {var s=Sim("parasite");var d=Die(s,"parasite",6,"A","C");var p=Shot(s,d);s.Recycle(0);var replacement=Die(s,"parasite",6,"A","C");var e=Enemy(s,216,220);e.Hp=1;s.PrimaryHit(e,p);Eq(replacement.GrowthKills,0L);});
        MainTest("gamble A/B: weak result is normal, and high-stakes probability is genuine", () =>
        {var s=Sim("gamble");var d=Die(s,"gamble",3,"A");ForceRoll(s,.1);double v=s.Stats(d).Damage;s.QueueVolley(d,0,-1.5,1,false);Near(s.State.PendingShots[0].Snapshot.Stats.Damage,v);s.State.PendingShots.Clear();d.Tier3="B";ForceRoll(s,.04);v=s.Stats(d).Damage;s.QueueVolley(d,0,-1.5,1,false);Near(s.State.PendingShots[0].Snapshot.Stats.Damage,v*8);});
        MainTest("gamble C/D: pity is saved, jackpot income only once", () =>
        {var s=Sim("gamble");var d=Die(s,"gamble",6,"A","C");d.BadRolls=23;ForceRoll(s,.8);double v=s.Stats(d).Damage;s.QueueVolley(d,0,-1.5,1,false);Near(s.State.PendingShots[0].Snapshot.Stats.Damage,v*8);Eq(d.BadRolls,0);s.State.PendingShots.Clear();d.Tier6="D";ForceRoll(s,.005);v=s.Stats(d).Damage;s.QueueVolley(d,0,-1.5,1,false);var pending=s.State.PendingShots[0];Near(pending.Snapshot.Stats.Damage,v*12);var e=Enemy(s,216,220);var p=s.MakeProjectile(216,300,-1.5,pending.Snapshot);double energy=s.State.Energy;s.PrimaryHit(e,p);s.PrimaryHit(e,p);Near(s.State.Energy-energy,8);});
        MainTest("mirror A/B: own pip scaling, no recursive support or mirror selection", () =>
        {var s=Sim("mirror","poison","time");var d=Die(s,"mirror",3,"A","",Center);var target=Die(s,"poison",1,"","",Right);var q=s.Stats(d);Eq(q.Effect,"pulse");Eq(q.AttackType,"poison");Near(q.Volley,Data.Types["poison"].BaseDamage*Data.Game.Levels[2].VolleyPower*Data.Game.Rules.DamageScale*.9);s.State.Board[Right]=null;target=Die(s,"time",6,"A","D",Right);Eq(s.Stats(d).AttackType,"mirror");d.Tier3="B";s.State.Board[Right]=null;Die(s,"poison",1,"","",RowEnd);Eq(s.Stats(d).AttackType,"poison");});
        MainTest("mirror C/D: only A/B copied and one bounded shadow volley", () =>
        {var s=Sim("mirror","poison");var d=Die(s,"mirror",6,"A","C",Center);Die(s,"poison",6,"B","D",Right);var q=s.Stats(d);Near(q.Trait("poisonStacks"),2);Near(q.Trait("poisonDetonate"),0);d.Tier6="D";s.QueueVolley(d,Center,-1.5,1,false);Eq(s.State.PendingShots.Count,12);double baseDamage=s.State.PendingShots[0].Snapshot.Stats.Damage;Eq(s.State.PendingShots.Count(x=>Math.Abs(x.Snapshot.Stats.Damage-baseDamage)<1e-9),6);Eq(s.State.PendingShots.Count(x=>Math.Abs(x.Snapshot.Stats.Damage-baseDamage*.35)<1e-9),6);Check(s.State.PendingShots.Zip(s.State.PendingShots.Skip(1)).All(x=>x.First.Due<=x.Second.Due));});
        MainTest("mirror can actually charge when copying a charging attack", () =>
        {var s=Sim("mirror","charge");var d=Die(s,"mirror",3,"A","",Center);Die(s,"charge",3,"A","",Right);Clock(s,2.2);Check(d.Charge>=2);});
        MainTest("time A/B: periodic windows, strongest source only, no automatic attacks", () =>
        {var s=Sim("time");var d=Die(s,"time",3,"A");var ally=Die(s,"pulse",1,"","",1);Clock(s,5.9);Near(d.PulseUntil,0);ally.Cooldown=10;Clock(s,.2);Check(d.PulseUntil>s.State.Time);Check(ally.Cooldown<9.8);Eq(s.State.Shots,0L);d.Tier3="B";d.AbilityClock=9.9;d.PulseUntil=0;Clock(s,.2);Check(d.PulseUntil-s.State.Time>2.8);});
        MainTest("time C/D: startup reduction gate and 70% acceleration", () =>
        {var s=Sim("time");var d=Die(s,"time",6,"A","C",0);var other=Die(s,"time",6,"A","C",1);var ally=Die(s,"pulse",1,"","",2);d.AbilityClock=other.AbilityClock=5.99;ally.Cooldown=10;s.Step(.02);Near(ally.Cooldown,10-.35-.02*1.4);d.Tier6="D";other.Tier6="D";ally.Cooldown=10;s.Step(.1);Near(ally.Cooldown,10-.17);});
        MainTest("evolution A/B: timing tradeoff and extra pip damage", () =>
        {var s=Sim("evolution");var d=Die(s,"evolution",3,"A");d.Age=29.99;s.Step(.02);Eq(d.Pips,4);Eq(d.Tier3,"A");d.Pips=3;d.Tier3="B";d.Age=40;s.Step(.1);Eq(d.Pips,3);double v=s.Stats(d).Volley;d.Tier3="";Near(v,s.Stats(d).Volley*1.16);});
        MainTest("evolution milestones: reselect A/B then C/D, without free merge salvo", () =>
        {var s=Sim("evolution");var d=Die(s,"evolution",2);d.Age=29.99;s.Step(.02);Eq(s.CurrentSkillChoice!.Tier,3);Choose(s,"B");Eq(s.State.PendingShots.Count,0);d.Pips=5;d.Age=71.99;s.Step(.02);Eq(d.Pips,6);Eq(d.Tier3,"");Eq(s.State.PendingSkills.Count,2);Choose(s,"A");Choose(s,"D");Eq(s.State.PendingShots.Count,0);});
        MainTest("evolution C/D: adjacent upgrade triggers proper choices and final form works", () =>
        {var s=Sim("evolution");var d=Die(s,"evolution",6,"A","C",Center);var ally=Die(s,"pulse",2,"","",Right);d.Age=39.99;s.Step(.02);Eq(ally.Pips,3);Eq(s.CurrentSkillChoice!.DieId,ally.Id);Choose(s,"A");d.Tier6="D";double v=s.Stats(d).Volley;double reload=s.Stats(d).Reload;d.Tier6="";Near(v,s.Stats(d).Volley*1.9);Near(reload,s.Stats(d).Reload*.8);});
        MainTest("saved corruption: nonfinite traits, unbounded DOT and bad clock rejected", () =>
        {var s=Sim("poison");var d=Die(s,"poison",6,"A","C");s.Fire(-1.5);var saved=s.ExportSave();saved.PendingShots[0].Snapshot.Stats.Traits["poison"]=double.NaN;Throws(()=>Simulation.Restore(Data,saved));saved=s.ExportSave();saved.Board[0]!.Age=double.PositiveInfinity;Throws(()=>Simulation.Restore(Data,saved));var e=Enemy(s,216,220);saved=s.ExportSave();saved.Enemies[0].Poison=Enumerable.Range(0,21).Select(_=>new PoisonStack()).ToList();Throws(()=>Simulation.Restore(Data,saved));});
        MainTest("busy firing and merging do not consume gamble RNG, charge, pity, money or material", () =>
        {var s=Sim("gamble","charge");var d=Die(s,"gamble",6,"A","C");d.BadRolls=23;var c=Die(s,"charge",1,"","",1);c.Charge=2;for(int i=0;i<Data.Game.Rules.MaxQueuedShots;i++)s.State.PendingShots.Add(new PendingShot());uint rng=s.Random.State;Check(!s.Fire(-1.5).Ok);Eq(s.Random.State,rng);Eq(d.BadRolls,23);Near(c.Charge,2);s.State.Board[0]=null;s.State.Board[1]=null;Die(s,"gamble",1,"","",0);Die(s,"gamble",1,"","",1);Check(!s.Merge(0,1).Ok);Eq(s.Random.State,rng);Eq(s.Count,2);});
        MainTest("snapshot dictionaries are deep-copied between pellets, echoes and exported saves", () =>
        {var s=Sim("echo");var d=Die(s,"echo",6,"A","C");ForceRoll(s,.1);s.QueueVolley(d,0,-1.5,1,false);var saved=s.ExportSave();saved.PendingShots[0].Snapshot.Stats.Traits["echoFactor"]=999;Check(s.State.PendingShots[0].Snapshot.Stats.Trait("echoFactor")!=999);s.State.PendingShots[0].Snapshot.Stats.Traits["rootFirst"]=99;Check(s.State.PendingShots[1].Snapshot.Stats.Trait("rootFirst")!=99);});
        MainTest("stress: 16 six-pip proc dice / snapshots / DOT stay bounded", Stress);
        Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/content-test-results.json",JsonSerializer.Serialize(new{passed=Passed,failed=Failed,tests=Results},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"\n{Passed} content tests passed, {Failed} failed.");return Failed==0?0:1;
    }
    static void ChoiceFlow(string type)
    {
        var s=Sim(type);Die(s,"pulse",5,"A","",0);Die(s,"pulse",5,"B","",1);
        var rng=new SeededRandom(s.Random.State);for(int i=0;i<10000;i++){uint state=rng.State;if(rng.Pick(s.State.Deck)==type){s.Random.State=state;break;}}
        Check(s.Merge(0,1).Ok);Eq(s.CurrentSkillChoice!.DiceType,type);Eq(s.State.PendingSkills.Count,2);Eq(s.State.Board[1]!.Tier3,"");
        uint seed=s.Random.State;string before=J(s.ExportSave());s.PreviewSkill(s.CurrentSkillChoice.ChoiceId,"A");Eq(J(s.ExportSave()),before);
        Choose(s,"B");var clone=Simulation.Restore(Data,s.ExportSave());Eq(clone.CurrentSkillChoice!.Tier,6);Choose(s,"D");Choose(clone,"D");Eq(J(s.ExportSave()),J(clone.ExportSave()));
        // Only dice with a probabilistic attack consume RNG when the saved merge salvo finally fires.
        Check(s.Random.State==clone.Random.State);Eq(s.State.Board[1]!.Tier3,"B");Eq(s.State.Board[1]!.Tier6,"D");
    }
    static void Stress()
    {
        var s=Sim("poison","echo","prism","time","mirror","gamble");
        for(int i=0;i<16;i++)Die(s,s.State.Deck[i%6],6,i%2==0?"A":"B",i%3==0?"C":"D",i);
        for(int i=0;i<56;i++){var e=Enemy(s,60+i%7*48,165+i/7*30);e.W=e.H=22;e.Hp=e.MaxHp=1e12;}
        s.Grid.Rebuild(s.State.Enemies);var watch=Stopwatch.StartNew();int maxP=0,maxQ=0,maxPoison=0;
        for(int i=0;i<2400;i++)
        {if(i%12==0)s.Fire(-Math.PI/2+Math.Sin(i*.03)*.65);s.Step(1d/120);maxP=Math.Max(maxP,s.State.Projectiles.Count);maxQ=Math.Max(maxQ,s.State.PendingShots.Count);maxPoison=Math.Max(maxPoison,s.State.Enemies.Max(e=>e.Poison.Count));Check(maxP<=Data.Game.Rules.MaxProjectiles&&maxQ<=Data.Game.Rules.MaxQueuedShots&&maxPoison<=20&&s.State.TimedHits.Count<=256);}
        watch.Stop();SaveCodec.ValidateRun(Data,s.ExportSave());Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/content-stress.json",J(new{steps=2400,simulatedSeconds=20,wallMilliseconds=watch.Elapsed.TotalMilliseconds,maxProjectiles=maxP,maxPending=maxQ,maxPoison,scope="CPU simulation, not a graphical FPS measurement"}));
        Console.WriteLine($"CONTENT STRESS: {watch.Elapsed.TotalMilliseconds:0} ms; projectiles {maxP}, queue {maxQ}, poison {maxPoison}");
    }
    static Simulation Sim(params string[] types)
    {var deck=types.Concat(Data.DefaultDeck).Distinct().Take(6).ToArray();var s=new Simulation(Data,deck,723);Array.Clear(s.State.Board);s.State.Enemies.Clear();s.State.PendingShots.Clear();s.State.Projectiles.Clear();s.Events.Clear();s.State.Energy=1000;return s;}
    static DieState Die(Simulation s,string type,int pips,string a="",string b="",int slot=0)
    {var d=s.MakeDie(type,pips);d.Tier3=a;d.Tier6=b;s.State.Board[slot]=d;return d;}
    static EnemyState Enemy(Simulation s,double x,double y,string kind="normal")
    {var e=s.AddEnemy(new EnemyState{X=x,Y=y,W=20,H=20,Hp=1e6,Speed=0,Wave=1,Kind=kind});s.Grid.Rebuild(s.State.Enemies);return e;}
    static ProjectileState Shot(Simulation s,DieState d,double x=216,double y=300)=>s.MakeProjectile(x,y,-Math.PI/2,new ShotSnapshot{Type=d.Type,Pips=d.Pips,Stats=s.Stats(d),SourceDieId=d.Id});
    static void Clock(Simulation s,double duration)
    {if(s.State.Enemies.All(e=>e.Dead)){var e=Enemy(s,370,180);e.Hp=e.MaxHp=1e12;}int count=(int)Math.Ceiling(duration/.05);for(int i=0;i<count;i++)s.Step(duration/count);}
    static void Choose(Simulation s,string key)=>Check(s.ChooseDiceSkill(s.CurrentSkillChoice!.ChoiceId,key));
    static void ForceRoll(Simulation s,double value)
    {for(uint seed=1;seed<1000000;seed++){var r=new SeededRandom(seed);if(Math.Abs(r.Next()-value)<.001){s.Random.State=seed;return;}}throw new Exception("Cannot find deterministic roll.");}
    static string J<T>(T v)=>JsonSerializer.Serialize(v,GameData.JsonOptions);
    static void Check(bool value){if(!value)throw new Exception("Assertion failed.");}
    static void Eq<T>(T got,T expected){if(!EqualityComparer<T>.Default.Equals(got,expected))throw new Exception($"Expected {expected}, got {got}.");}
    static void Near(double got,double expected){if(Math.Abs(got-expected)>1e-6*Math.Max(1,Math.Abs(expected)))throw new Exception($"Expected {expected:R}, got {got:R}.");}
    static void Throws(Action action){try{action();}catch{return;}throw new Exception("Expected rejection.");}
    sealed class Store:IDesktopStorage{public string? Value;public string? Read()=>Value;public uint NewSeed()=>723;public void Write(string s)=>Value=s;}
    sealed class Silent:ISoundOutput{public bool Enabled{get;set;}public bool Music{get;set;}public void Unlock(){}public void Play(string kind,int variant=0){}public void Tick(double dt,bool active){}public void Suspend(){}}
}
