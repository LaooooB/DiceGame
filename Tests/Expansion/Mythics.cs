using System.Text.Json.Nodes;
using DiceGame.Core;
using DiceGame.App;

namespace DiceGame.ExpansionTests;
internal static partial class Program
{
    static string MergeOne(Simulation s)
    {Die(s,s.State.Deck.First(t=>t!="order"),1,slot:0);Die(s,s.State.Deck.First(t=>t!="order"),1,slot:1);var result=s.Merge(0,1);Check(result.Ok);return result.Die!.Type;}
    static void Mythic()
    {
        Test("fate A: two different numbers bless matching real pip counts",()=>{
            var s=Sim("fate");Die(s,"fate",6,"A","D",15);Enemy(s);Clock(s,.05);var x=s.State.Expansion;Eq(x.FateNumbers.Count,2);Eq(x.FateNumbers.Distinct().Count(),2);
            var ally=Die(s,"pulse",x.FateNumbers[0],slot:0);Near(s.Stats(ally).Volley,Base(ally)*1.3);
        });
        Test("fate B: a twelve-second strong fate window uses its separate PRNG",()=>{
            var s=Sim("fate");Die(s,"fate",6,"B","D",15);Enemy(s);Clock(s,.05);uint law=s.State.Expansion.FateRng,combat=s.Random.State;
            var ally=Die(s,"pulse",s.State.Expansion.FateNumbers[0],slot:0);Near(s.Stats(ally).Volley,Base(ally)*1.75);Clock(s,11);Eq(s.State.Expansion.FateRng,law);Clock(s,1.1);Check(s.State.Expansion.FateRng!=law);Eq(s.Random.State,combat);
        });
        Test("fate C: a matching rule owner grants a timed global blessing",()=>{
            var s=Sim("fate");Die(s,"fate",6,"A","C",15);Enemy(s);
            for(uint seed=1;;seed++)if(new SeededRandom(seed).Shuffle(Enumerable.Range(1,6))[0]==6){s.State.Expansion.FateRng=seed;break;}
            Clock(s,.05);Near(s.State.Expansion.FateBlessingPower,.15);int unmatched=Enumerable.Range(1,6).First(p=>!s.State.Expansion.FateNumbers.Contains(p));var ally=Die(s,"pulse",unmatched,slot:0);Near(s.Stats(ally).Volley,Base(ally)*1.15);Clock(s,3.1);Near(s.Stats(ally).Volley,Base(ally));
        });
        Test("fate D: lowest-instance blessing is captured without changing real pips",()=>{
            var s=Sim("fate");Die(s,"fate",6,"B","D",15);var low=Die(s,"pulse",1,slot:0);Die(s,"blast",2,slot:1);Enemy(s);Clock(s,.05);Eq(low.Pips,1);Check(s.State.Expansion.FateMinimumIds.SequenceEqual(new[]{low.Id}));s.State.Expansion.FateNumbers=[6];Near(s.Stats(low).Volley,Base(low)*1.75);
            var newcomer=Die(s,"pulse",1,slot:2);Check(!s.State.Expansion.FateMinimumIds.Contains(newcomer.Id));Near(s.Stats(newcomer).Volley,Base(newcomer));
        });
        Test("causality A: long window records actual damage and settles twenty percent",()=>{
            var s=Sim("causality");var d=Die(s,"causality",3,"A","");var e=Enemy(s);Hit(s,e,Shot(s,d));Near(e.Debt!.Recorded,0);s.ApplyDamage(e,100,"#FFFFFF");double hp=e.Hp;Clock(s,3);Near(e.Hp,hp);Clock(s,.6);Near(hp-e.Hp,20);Check(e.Debt is null);
        });
        Test("causality B: short window settles forty percent",()=>{
            var s=Sim("causality");var d=Die(s,"causality",3,"B","");var e=Enemy(s);Hit(s,e,Shot(s,d));s.ApplyDamage(e,100,"#FFFFFF");double hp=e.Hp;Clock(s,1.1);Near(hp-e.Hp,40);
        });
        Test("causality C: early death transfers a sealed, single-generation debt",()=>{
            var s=Sim("causality");var d=Die(s,"causality");var e=Enemy(s);var other=Enemy(s,250,250);Hit(s,e,Shot(s,d));s.ApplyDamage(e,100,"#FFFFFF");e.Hp=1;s.ApplyDamage(e,10000,"#FFFFFF");Check(other.Debt is {Sealed:true});Near(other.Debt!.Recorded,50.5);s.ApplyDamage(other,100,"#FFFFFF");Near(other.Debt.Recorded,50.5);double hp=other.Hp;Clock(s,3.6);Near(hp-other.Hp,10.1);
        });
        Test("causality D: boss settlement uses the dedicated multiplier once",()=>{
            var s=Sim("causality");var d=Die(s,"causality",6,"A","D");var e=Enemy(s,kind:"boss");Hit(s,e,Shot(s,d));s.ApplyDamage(e,100,"#FFFFFF");double hp=e.Hp;Clock(s,3.6);Near(hp-e.Hp,32);
        });
        Test("void A: capped empty spaces trade personal damage for actual ally damage",()=>{
            var s=Sim("void");var d=Die(s,"void",3,"A","");var ally=Die(s,"pulse",1,slot:1);Near(s.Stats(d).Volley,Base(d)*1.3);Near(s.Stats(ally).Volley,Base(ally)*1.216);
        });
        Test("void B: personal empty-space gain replaces, not multiplies, the ally law",()=>{
            var s=Sim("void");var d=Die(s,"void",3,"B","");var ally=Die(s,"pulse",1,slot:1);Near(s.Stats(d).Volley,Base(d)*2.68);Near(s.Stats(ally).Volley,Base(ally));
        });
        Test("void C: eight actual empty slots grant penetration, seven do not",()=>{
            var s=Sim("void");Die(s,"void",6,"A","C",0);var ally=Die(s,"pulse",1,slot:1);for(int i=2;i<8;i++)Die(s,"pulse",1,slot:i);Eq(s.Stats(ally).Pierces,1);Die(s,"pulse",1,slot:8);Eq(s.Stats(ally).Pierces,0);
        });
        Test("void D: eight seconds charges annihilation but never fires automatically",()=>{
            var s=Sim("void");var d=Die(s,"void",6,"B","D");var e=Enemy(s);Clock(s,8.1);Eq(s.State.Shots,0L);Near(d.VoidClock,8);var shots=Volley(s,d);Check(shots[0].Stats.VoidCharged);Near(d.VoidClock,0);var p=Pellet(s,shots[0]);Near(Hit(s,e,p,true),p.Stats.Damage+p.Stats.Volley*1.8);Check(!Volley(s,d)[0].Stats.VoidCharged);
        });
        Test("reincarnation A: present-life power and a new one-pip identity",()=>{
            var s=Sim("reincarnation");var d=Die(s,"reincarnation",6,"A","C");Near(s.Stats(d).Volley,Base(d));long id=d.Id;double energy=s.State.Energy;var result=s.Recycle(0);Check(result.Ok);Eq(result.Die!.Pips,1);Check(result.Die.Id!=id);Eq(result.Die.Tier3,"");Near(s.State.Energy,energy);Eq(s.State.PendingShots.Count,0);Eq(s.State.PendingSkills.Count,0);
        });
        Test("reincarnation B: future reward is based on the completed life snapshot",()=>{
            var s=Sim("reincarnation");var d=Die(s,"reincarnation",6,"B","C");Near(s.Stats(d).Volley,Base(d));s.Recycle(0);Near(s.State.Expansion.RebirthPower,.11);Near(s.Stats(s.State.Board[0]!).Volley,Base(s.State.Board[0]!)*1.11);
        });
        Test("reincarnation C: three larger rewards, fourth rebirth cannot farm another layer",()=>{
            var s=Sim("reincarnation");for(int i=0;i<4;i++){Die(s,"reincarnation",6,"A","C");Check(s.Recycle(0).Ok);}Eq(s.State.Expansion.Rebirths,3);Near(s.State.Expansion.RebirthPower,.27);Eq(s.State.Board[0]!.Pips,1);
        });
        Test("reincarnation D: second-pip restart and a hard thirty-six-percent total cap",()=>{
            var s=Sim("reincarnation");for(int i=0;i<8;i++){Die(s,"reincarnation",6,"B","D");Check(s.Recycle(0).Ok);}Eq(s.State.Board[0]!.Pips,2);Eq(s.State.Expansion.Rebirths,6);Near(s.State.Expansion.RebirthPower,.36);Eq(s.State.Board[0]!.Tier3,"");
        });
        Test("order A: next-result preview is pure and matches the committed merge",()=>{
            var s=Sim("order");Die(s,"order",3,"A","",15);string before=J(s.ExportSave());string expected=s.OrderPreview().Single();Eq(J(s.ExportSave()),before);Eq(MergeOne(s),expected);
        });
        Test("order B: bag boundaries never repeat the previous last type",()=>{
            var s=Sim("order");Die(s,"order",3,"B","",15);string last="";for(int bag=0;bag<10;bag++){var draws=Enumerable.Range(0,6).Select(_=>MergeOne(s)).ToArray();Eq(draws.Distinct().Count(),6);Check(draws[0]!=last);last=draws[^1];}
        });
        Test("order C: three-item prediction crosses bags without consuming either random stream",()=>{
            var s=Sim("order");Die(s,"order",6,"A","C",15);for(int i=0;i<4;i++)MergeOne(s);string before=J(s.ExportSave());var prediction=s.OrderPreview().ToArray();Eq(prediction.Length,3);Eq(J(s.ExportSave()),before);var results=Enumerable.Range(0,3).Select(_=>MergeOne(s)).ToArray();Check(prediction.SequenceEqual(results));
        });
        Test("order D: one rotation per stage preserves all six cards",()=>{
            var s=Sim("order");Die(s,"order",6,"A","D",15);string first=s.OrderPreview()[0];Check(s.SkipOrder().Ok);Check(!s.SkipOrder().Ok);var results=Enumerable.Range(0,6).Select(_=>MergeOne(s)).ToArray();Eq(results.Distinct().Count(),6);Eq(results[^1],first);s.State.Wave=6;Check(s.CanSkipOrder);Check(s.SkipOrder().Ok);
        });
    }
}
