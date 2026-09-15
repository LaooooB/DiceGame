namespace DiceGame.Core;

public sealed partial class Simulation
{
    public bool AwaitingDiceSkill => S.PendingSkills.Count > 0;
    public DiceSkillChoice? CurrentSkillChoice => S.PendingSkills.FirstOrDefault();
    public DiceSkillSet SkillsFor(string type) => S.SkillSets.TryGetValue(type, out var set) ? set : throw new InvalidOperationException("Missing pinned die skills: " + type);
    public IReadOnlyList<DiceSkillDefinition> SkillOptions => CurrentSkillChoice is { } q ? (q.Tier == 3 ? SkillsFor(q.DiceType).Level3 : SkillsFor(q.DiceType).Level6) : [];
    public string SkillDescription(DieState die)
    {
        if (!R.EnableDiceSkills) return "";
        var set = SkillsFor(die.Type); var rows = new List<string>();
        if (die.Tier3 != "" && set.Find(3, die.Tier3) is { } a) rows.Add($"{a.Key} · {a.Name}：{a.Description}");
        if (die.Tier6 != "" && set.Find(6, die.Tier6) is { } b) rows.Add($"{b.Key} · {b.Name}：{b.Description}");
        return rows.Count == 0 ? "升到 3 点时选择 A/B；6 点时重新选择 A/B，再选择 C/D。" : string.Join("\n", rows);
    }
    private void ApplyDieSkills(DieState die, ShotStats stats)
    {
        if (!R.EnableDiceSkills) return;
        var set = SkillsFor(die.Type);
        if (die.Pips >= 3 && set.Find(3, die.Tier3) is { } a) a.Modifiers.Apply(stats);
        if (die.Pips >= 6 && set.Find(6, die.Tier6) is { } b) b.Modifiers.Apply(stats);
        stats.Count = Math.Clamp(stats.Count, 1, 18); stats.ChainCount = Math.Clamp(stats.ChainCount, 1, 12);
        stats.ChildCount = Math.Clamp(stats.ChildCount, 1, 10); stats.Bounces = Math.Clamp(stats.Bounces, 1, 36);
        stats.Pierces = Math.Clamp(stats.Pierces, 0, 8); stats.ChildPierces = Math.Clamp(stats.ChildPierces, 0, 8);
        stats.MaxBoost = Math.Clamp(stats.MaxBoost, 1, 6); stats.SlowSeconds = Math.Clamp(stats.SlowSeconds, .1, 15);
        stats.Reload = Math.Max(.06, stats.Reload);
        stats.Damage = stats.Volley / stats.Count; // More pellets redistribute the volley; do not multiply it accidentally.
    }
    private int MergeShotReservation(int pips)
    {
        if (R.EnableDiceContent) return Math.Min(18, pips + 12) * 3;
        if (!R.EnableDiceSkills || pips < 3) return pips;
        int extra=0;
        foreach(var set in S.SkillSets.Values)
        {
            int add=set.Level3.Max((DiceSkillDefinition skill)=>skill.Modifiers.ExtraProjectiles);
            if(pips>=6) add+=set.Level6.Max((DiceSkillDefinition skill)=>skill.Modifiers.ExtraProjectiles);
            extra=Math.Max(extra,add);
        }
        return Math.Min(18,pips+extra);
    }
    private void QueueSkillChoice(DieState die, int tier, bool finishMerge, double surge)
    {
        S.PendingSkills.Add(new DiceSkillChoice { ChoiceId = S.NextId++, DieId = die.Id, DiceType = die.Type,
            ResultPips = die.Pips, Tier = tier, FinishMerge = finishMerge, SurgeAngle = S.LastAim, SurgeMultiplier = surge });
    }
    private void FinishDieMerge(DieState die, int slot, double angle, double multiplier)
    {
        QueueVolley(die, slot, angle, multiplier, true);
        die.Cooldown = Stats(die).Reload * .36;
    }
    /// <summary>Called only after the random result is committed. Choices never reroll RNG or the result identity.</summary>
    private void ResolveMergeSkills(DieState die, DieState target, int slot)
    {
        double pipScale = R.EnableDiceSkills ? die.Pips switch { 2 => 1.0, 3 => 1.25, 4 => 1.5, 5 => 1.8, 6 => 2.2, _ => 1.0 } : 1.0;
        double surge = 1 + R.MergeSurge * pipScale + .35 * UpgradeLevel("surge");
        if (R.EnableDiceSkills && die.Pips is 3 or 6)
        {
            QueueSkillChoice(die, 3, die.Pips == 3, surge);
            if (die.Pips == 6) QueueSkillChoice(die, 6, true, surge);
            Emit(new CombatEvent { Type = "dice_skill", Slot = slot, Die = die.Copy() });
        }
        else
        {
            // At 4/5, the target slot determines the branch. Resolve that letter on the NEW die type.
            if (R.EnableDiceSkills && die.Pips > 3) die.Tier3 = target.Tier3;
            FinishDieMerge(die, slot, S.LastAim, surge);
        }
    }
    /// <summary>A terminal old save has no modal. If it is extended, resolve missing skills before its next tick.</summary>
    private void QueueMissingSkills()
    {
        if (!R.EnableDiceSkills) return;
        foreach (var die in S.Board)
        {
            if (die is null) continue;
            if (die.Pips >= 3 && die.Tier3 == "" && !S.PendingSkills.Any(q => q.DieId == die.Id && q.Tier == 3))
                QueueSkillChoice(die, 3, false, 1);
            if (die.Pips >= 6 && die.Tier6 == "" && !S.PendingSkills.Any(q => q.DieId == die.Id && q.Tier == 6))
                QueueSkillChoice(die, 6, false, 1);
        }
    }
    public bool ChooseDiceSkill(long choiceId, string key)
    {
        if (S.Over || CurrentSkillChoice is not { } q || q.ChoiceId != choiceId) return false;
        int slot = Array.FindIndex(S.Board, d => d?.Id == q.DieId);
        if (slot < 0 || S.Board[slot] is not { } die || die.Type != q.DiceType || die.Pips != q.ResultPips ||
            !SkillOptions.Any(s => s.Key == key)) return false;
        if (q.Tier == 6 && die.Tier3 is not ("A" or "B")) return false;
        if (q.Tier == 3) die.Tier3 = key; else die.Tier6 = key;
        S.PendingSkills.RemoveAt(0);
        if (q.FinishMerge) FinishDieMerge(die, slot, q.SurgeAngle, q.SurgeMultiplier);
        Emit(new CombatEvent { Type = "skill_chosen", Slot = slot, Die = die.Copy(), Id = key });
        return true;
    }
    public ShotStats PreviewSkill(long choiceId, string key)
    {
        var q = CurrentSkillChoice;
        if (q is null || q.ChoiceId != choiceId || !SkillOptions.Any(s => s.Key == key)) throw new ArgumentException("Stale skill preview.");
        var die = S.Board.Single(d => d?.Id == q.DieId)!.Copy();
        if (q.Tier == 3) die.Tier3 = key; else die.Tier6 = key;
        return Stats(die);
    }
    private double TargetDamage(EnemyState enemy, double damage, ShotStats stats)
    {
        if (enemy.Kind == "boss") damage *= stats.BossDamageMultiplier;
        if (enemy.SlowUntil > S.Time && enemy.SlowFactor < 1) damage *= stats.ChilledDamageMultiplier;
        return damage;
    }
    private void ApplySlow(EnemyState enemy, ShotStats stats)
    {
        if(R.EnableDiceContent) { ApplySlow(enemy,stats.SlowFactor,stats.SlowSeconds); return; }
        if (enemy.Dead) return;
        bool alreadySlowed = enemy.SlowUntil > S.Time;
        enemy.SlowUntil = Math.Max(enemy.SlowUntil, S.Time + stats.SlowSeconds);
        // Weak area slow must not overwrite an existing stronger slow.
        enemy.SlowFactor = Math.Max(.35, alreadySlowed ? Math.Min(enemy.SlowFactor, stats.SlowFactor) : stats.SlowFactor);
    }
}
