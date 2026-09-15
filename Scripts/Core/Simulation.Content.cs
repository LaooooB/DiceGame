namespace DiceGame.Core;

public sealed partial class Simulation
{
    private readonly Dictionary<long, ((string Type, int Pips, string A, string B) Key, Dictionary<string, double> Values)> _contentProfiles = [];
    private Dictionary<string, double> ProfileFor(DieState die)
    {
        var key = (die.Type, die.Pips, die.Tier3, die.Tier6);
        if (_contentProfiles.TryGetValue(die.Id, out var cached) && cached.Key == key) return cached.Values;
        if (_contentProfiles.Count > 128) _contentProfiles.Clear();
        var values = DiceContent.Profile(Data.Types[die.Type], die, S.SkillSets.GetValueOrDefault(die.Type));
        _contentProfiles[die.Id] = (key, values); return values;
    }
    private static double Value(Dictionary<string, double> v, string key, double fallback = 0) => v.GetValueOrDefault(key, fallback);
    private bool IsAdjacent(int a, int b, bool row = false) => a >= 0 && b >= 0 && a != b &&
        (row ? a / C.Board.Columns == b / C.Board.Columns : Math.Abs(a / C.Board.Columns - b / C.Board.Columns) + Math.Abs(a % C.Board.Columns - b % C.Board.Columns) == 1);
    public IEnumerable<int> AdjacentSlots(int slot, bool row = false)
    {
        if (slot < 0 || slot >= S.Board.Length) yield break;
        int columns = C.Board.Columns;
        if (row)
        { int start=slot/columns*columns;for(int i=start;i<Math.Min(start+columns,S.Board.Length);i++)if(i!=slot)yield return i; }
        else
        {
            if(slot>=columns)yield return slot-columns;
            if(slot+columns<S.Board.Length)yield return slot+columns;
            if(slot%columns>0)yield return slot-1;
            if(slot%columns+1<columns && slot+1<S.Board.Length)yield return slot+1;
        }
    }
    private DieState? SourceDie(long id) => id > 0 ? S.Board.FirstOrDefault(d => d?.Id == id) : null;
    public string ContentStatus(DieState die)
    {
        var v = Value(ProfileFor(die),"mirror")>0 ? Stats(die).Traits : ProfileFor(die); var rows = new List<string>();
        if (Value(v, "chargeMax") > 0) rows.Add($"充能 {die.Charge:0.0}/{Value(v, "chargeMax"):0.0} 秒");
        if (Value(v, "rampMax") > 0) rows.Add($"连续命中 {die.Streak}/{Value(v, "rampMax"):0}");
        if (Value(v, "growthEvery") > 0) rows.Add($"寄生成长 {Math.Min(Value(v, "growthMax", 10), Math.Floor(die.GrowthKills / Value(v, "growthEvery"))):0}/{Value(v, "growthMax", 10):0} 层 · 归属击破 {die.GrowthKills}");
        if (Value(v, "timePeriod") > 0) rows.Add(die.PulseUntil > S.Time ? $"时间加速剩余 {die.PulseUntil - S.Time:0.0} 秒" : $"下次时间脉冲 {Math.Max(0, Value(v, "timePeriod") - die.AbilityClock):0.0} 秒");
        if (Value(v, "evolutionPeriod") > 0 && die.Pips < R.MaxPips) rows.Add($"距进化 {Math.Max(0, EvolutionSeconds(die, v) - die.Age):0.0} 秒");
        if (Value(v, "mirror") > 0) { var q = Stats(die); rows.Add(q.AttackType == die.Type ? "没有可模仿的相邻攻击骰" : "模仿：" + Data.Types[q.AttackType].Name); }
        var expansion=ExpansionStatus(die);if(expansion.Length>0)rows.Add(expansion);
        return string.Join("\n", rows);
    }
    public ShotStats Stats(DieState die)
    {
        var q = BaseStats(die);
        return R.EnableDiceContent ? EnhanceContentStats(die, q) : q;
    }
    private ShotStats EnhanceContentStats(DieState die, ShotStats q)
    {
        int slot = Array.FindIndex(S.Board, d => d?.Id == die.Id);
        var own = Data.Types[die.Type]; var ownProfile = ProfileFor(die);
        if (Value(ownProfile, "mirror") > 0)
        {
            var target = AdjacentSlots(slot, Value(ownProfile, "mirrorRow") > 0).Select(i => S.Board[i])
                .Where(d => d is not null && Data.Types[d.Type].Copyable).OrderByDescending(d => d!.Pips).ThenBy(d => d!.Id).FirstOrDefault();
            if (target is not null)
            {
                // BaseStats never resolves a mirror or another aura. Copying can therefore not recurse.
                var copy = new DieState { Type = target.Type, Pips = die.Pips, Tier3 = Value(ownProfile, "mirrorBranch") > 0 ? target.Tier3 : "" };
                q = BaseStats(copy);
                foreach (var pair in own.Traits) q.Traits[pair.Key] = pair.Value;
                ApplyDieSkills(die, q); // The mirror's own A/B/C/D, not the target's C/D.
                q.Volley *= Value(ownProfile, "mirrorFactor", .7); q.Color = own.Color;
                if (Value(ownProfile, "mirrorEcho") > 0) { q.Traits["echoChance"] = 1; q.Traits["echoFactor"] = Value(ownProfile, "mirrorEcho"); }
                foreach (string key in new[] { "mergeRefund", "recycleBonus", "growthEvery", "assistGrowth", "matureAura", "jackpotEnergy", "energyFirst", "evolutionPeriod", "timePeriod" }) q.Traits.Remove(key);
            }
        }
        var v = q.Traits;
        int matches = AdjacentSlots(slot).Count(i => S.Board[i] is { } n && Math.Abs(n.Pips - die.Pips) <= (Value(v, "resonanceLoose") > 0 ? 1 : 0));
        double damage = 1 + matches * Value(v, "resonance");
        if (matches == 0) damage *= 1 + Value(v, "aloneBonus");
        damage *= 1 + Math.Min(die.Charge, Value(v, "chargeMax")) * Value(v, "chargeRate");
        if (Value(v, "growthEvery") > 0) damage *= 1 + Math.Min(Value(v, "growthMax", 10), Math.Floor(die.GrowthKills / Math.Max(1, Value(v, "growthEvery")))) * Value(v, "growthStep", .05);
        damage *= 1 + Math.Min(die.Streak, Value(v, "rampMax", 12)) * Value(v, "rampDamage");
        damage *= 1 + (die.Pips - 1) * Value(v, "evolutionDamage");
        if (die.Pips == R.MaxPips) damage *= Value(v, "finalDamage", 1);
        double auraDamage = die.ForgeBlessingUntil>S.Time ? die.ForgeBlessingPower : 0, auraEcho = 0, auraPierce = 0, auraSplit = 0;
        for (int i = 0; i < S.Board.Length; i++)
        {
            if (i == slot || S.Board[i] is not { } n) continue;
            var a = ProfileFor(n);
            auraDamage=Math.Max(auraDamage,ExpansionAura(n,die,i,slot));
            if (!IsAdjacent(i, slot, Value(a, "auraRow") > 0)) continue;
            auraDamage = Math.Max(auraDamage, Value(a, "auraDamage"));
            auraEcho = Math.Max(auraEcho, Value(a, "auraEcho"));
            auraPierce = Math.Max(auraPierce, Value(a, "auraPierce"));
            auraSplit = Math.Max(auraSplit, Value(a, "auraSplit"));
            if (Value(a, "focusedAura") > 0 && AdjacentSlots(i).Count(j => S.Board[j] is not null) == 1) auraDamage = Math.Max(auraDamage, Value(a, "focusedAura"));
            if (Value(a, "resonanceAura") > 0 && n.Pips == die.Pips) auraDamage = Math.Max(auraDamage, Value(a, "resonanceAura"));
            if (Value(a, "matureAura") > 0 && n.GrowthKills >= Value(a, "growthEvery", 20) * Value(a, "growthMax", 10)) auraDamage = Math.Max(auraDamage, Value(a, "matureAura"));
        }
        damage *= 1 + Math.Min(.5, auraDamage);
        q.Volley *= Math.Clamp(damage, .05, 100);
        q.Reload /= 1 + Value(v, "pipHaste") * (die.Pips - 1) + Value(v, "rampHaste") * Math.Min(die.Streak, Value(v, "rampMax", 12)) + Value(v, "resonanceHaste") * matches;
        if (die.Pips == R.MaxPips) q.Reload *= Value(v, "finalReload", 1);
        q.Reload = Math.Clamp(q.Reload, .06, 20);
        q.Pierces = Math.Clamp(q.Pierces + (int)(Value(v, "pierce") + Value(v, "pipPierce") * (die.Pips - 1) + auraPierce + (die.Charge >= 2 ? Value(v, "chargePierce") : 0)), 0, 8);
        v["echoChance"] = Math.Clamp(Value(v, "echoChance") + auraEcho, 0, 1);
        if (auraEcho > 0 && !v.ContainsKey("echoFactor")) v["echoFactor"] = .65;
        if (auraSplit > 0) v["auraSplit"] = auraSplit;
        ExpansionStats(die,q,slot);
        q.Damage = q.Volley / q.Count;
        return q;
    }
    private int VolleyReservation(DieState die)
    {
        var q = Stats(die);
        return q.Count * (q.Trait("echoChance") > 0 ? 1 + Math.Clamp((int)q.Trait("echoCopies", 1), 1, 2) : 1)+ExtraVolleyReservation(die,q);
    }
    private void RefundMergeMaterials(DieState first, DieState second)
    {
        if (!R.EnableDiceContent) return;
        foreach (var material in new[] { first, second })
        {
            double refund = Math.Min(R.SummonCost * .8, Value(ProfileFor(material), "mergeRefund") * (1 + .12 * (material.Pips - 1)));
            if (refund > 0) { S.Energy = Math.Min(999999, S.Energy + refund); Emit(new CombatEvent { Type = "recycle", Amount = refund }); }
            _contentProfiles.Remove(material.Id);
        }
    }
    public int RecycleValue(DieState die) => CanReincarnate(die)?0:C.Levels[die.Pips-1].Recycle + ContentRecycleBonus(die);
    private int ContentRecycleBonus(DieState die)
    {
        if (!R.EnableDiceContent) return 0;
        var v = ProfileFor(die);
        return (int)Math.Floor(Value(v, "recycleBonus") + Value(v, "mergeRefund") * .5 * (1 + .12 * (die.Pips - 1)));
    }
    private void QueueContentVolley(DieState die, int slot, double angle, double multiplier, bool surge)
    {
        if (S.PendingShots.Count + VolleyReservation(die) > R.MaxQueuedShots) return;
        var stats = PrepareExpansionVolley(die,slot,angle,surge); stats.Damage *= multiplier; stats.Volley *= multiplier;
        die.Attacks = Math.Min(1000000000000, die.Attacks + 1);
        if (stats.Trait("jackpotFactor") > 0)
        {
            double roll = Random.Next(), jackpot = stats.Trait("jackpotChance", .02), weak = stats.Trait("weakChance", .3), twice = stats.Trait("doubleChance", .18);
            bool win = roll < jackpot || stats.Trait("pity") > 0 && die.BadRolls >= stats.Trait("pity") - 1;
            double factor = win ? stats.Trait("jackpotFactor", 8) : roll < jackpot + weak ? (stats.Trait("noWeak") > 0 ? 1 : .5) : roll < jackpot + weak + twice ? 2 : 1;
            die.BadRolls = win ? 0 : Math.Min(10000, die.BadRolls + 1);
            stats.Damage *= factor; stats.Volley *= factor;
            if (win) stats.Traits["energyFirst"] = stats.Trait("energyFirst") + stats.Trait("jackpotEnergy");
            Emit(new CombatEvent { Type = "conduit", Slot = slot, Color = stats.Color, Surge = win || surge });
        }
        if (stats.Trait("seekCone") > 0)
        {
            var target = S.Enemies.Where(e => !e.Dead && Math.Abs(Math.Atan2(e.Y - A.LaunchY, e.X - 216) - angle) <= stats.Trait("seekCone"))
                .OrderByDescending(e => e.Hp).ThenBy(e => e.Id).FirstOrDefault();
            if (target is not null) angle = ClampAim(Math.Atan2(target.Y - A.LaunchY, target.X - 216));
        }
        int echoes = stats.Trait("echoChance") > 0 && Random.Next() < stats.Trait("echoChance") ? Math.Clamp((int)stats.Trait("echoCopies", 1), 1, 2) : 0;
        for (int copy = 0; copy <= echoes; copy++)
        for (int k = 0; k < stats.Count; k++)
        {
            var captured = stats.Copy();
            if (copy > 0) { captured.Damage *= Math.Clamp(stats.Trait("echoFactor", 1), .1, 2); captured.Volley *= Math.Clamp(stats.Trait("echoFactor", 1), .1, 2); }
            captured.Traits["rootFirst"] = copy == 0 && k == 0 ? 1 : 0;
            DecoratePellet(captured,k,stats.Count,copy);
            var snapshot = new ShotSnapshot { Type = die.Type, Pips = die.Pips, Stats = captured, Source = Data.SlotPosition(slot), SourceDieId = die.Id, Surge = surge };
            S.PendingShots.Add(new PendingShot { Due = S.Time + .075 + k * .056 + slot % C.Board.Columns * .019 + copy * .15, Angle = PelletAngle(stats,k,angle), Snapshot = snapshot });
        }
        QueueSoulVolley(die,stats,angle,surge);
        // Echoes retain the same roll, skills and origin. They do not create another attack event.
        die.Charge *= Math.Clamp(stats.Trait("chargeKeep"), 0, .75);
        SortPending(); Emit(new CombatEvent { Type = "conduit", Slot = slot, Color = stats.Color, Surge = surge });
    }
}
