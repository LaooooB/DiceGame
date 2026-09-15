namespace DiceGame.Core;

public sealed partial class Simulation
{
    private double TickDiceContent(double dt)
    {
        double haste = 1;
        foreach (var die in S.Board.OfType<DieState>().ToArray())
        {
            var v = Value(ProfileFor(die),"mirror")>0 ? Stats(die).Traits : ProfileFor(die);
            die.Charge = Math.Min(Value(v, "chargeMax"), die.Charge + dt);
            die.Age = Math.Min(1e9, die.Age + dt);
            if (die.Streak > 0 && S.Time - die.LastHitTime > 3) { die.Streak = 0; die.LastTarget = 0; }
            double period = Value(v, "timePeriod");
            if (period > 0)
            {
                die.AbilityClock += dt;
                if (die.AbilityClock >= Math.Max(2, period))
                {
                    die.AbilityClock %= Math.Max(2, period);
                    die.PulseUntil = S.Time + Math.Clamp(Value(v, "timeDuration", 2), .2, 5);
                    if (Value(v, "timeKick") > 0 && S.Time - S.LastTimeKick >= 2)
                    {
                        S.LastTimeKick = S.Time;
                        foreach (var d in S.Board.OfType<DieState>()) d.Cooldown = Math.Max(0, d.Cooldown - Value(v, "timeKick"));
                    }
                    Emit(new CombatEvent { Type = "conduit", Slot = Array.IndexOf(S.Board, die), Color = Data.Types[die.Type].Color, Surge = true });
                }
                if (die.PulseUntil > S.Time) haste = Math.Max(haste, Math.Clamp(Value(v, "timeHaste", 1.4), 1, 2));
            }
            double evolution = Value(v, "evolutionPeriod");
            if (evolution <= 0) continue;
            if (die.Pips < R.MaxPips && die.Age >= EvolutionSeconds(die,v))
            { die.Age = 0; PromoteDie(die); }
            else if (die.Pips == R.MaxPips && Value(v, "evolutionGift") > 0 && die.Age >= Value(v, "evolutionGift") && S.Time - S.LastEvolutionGift >= 15)
            {
                int slot = Array.IndexOf(S.Board, die);
                var targets = AdjacentSlots(slot).Select(i => S.Board[i]).Where(d => d is not null && d.Pips < R.MaxPips && !S.PendingSkills.Any(c => c.DieId == d.Id)).Cast<DieState>().ToList();
                if (targets.Count > 0) { die.Age = 0; S.LastEvolutionGift = S.Time; PromoteDie(Random.Pick(targets)); }
            }
        }
        foreach (long id in _contentProfiles.Keys.Where(id => SourceDie(id) is null).ToArray()) _contentProfiles.Remove(id);
        return haste;
    }
    private void PromoteDie(DieState die)
    {
        if (die.Pips >= R.MaxPips) return;
        die.Pips++; die.Flash = .35;
        if (die.Pips >= 3) QueueEvolutionSkills(die);
        Emit(new CombatEvent { Type = "summon", Slot = Array.IndexOf(S.Board, die), Die = die.Copy() });
    }
    private double EvolutionSeconds(DieState die, Dictionary<string, double> profile) =>
        Math.Max(2, Value(profile, "evolutionPeriod") * (1 + Value(profile, "evolutionScale", .5) * (die.Pips - 1)));
    private void QueueEvolutionSkills(DieState die)
    {
        if (die.Pips is not (3 or 6)) return;
        die.Tier3 = "";
        QueueSkillChoice(die, 3, false, 1);
        if (die.Pips == 6) { die.Tier6 = ""; QueueSkillChoice(die, 6, false, 1); }
        Emit(new CombatEvent { Type = "dice_skill", Slot = Array.IndexOf(S.Board, die), Die = die.Copy() });
    }
    private void TickContentEffects()
    {
        for (int i = S.TimedHits.Count - 1; i >= 0; i--)
        {
            var hit = S.TimedHits[i]; if (hit.Due > S.Time) continue;
            S.TimedHits.RemoveAt(i); AreaDamage(hit.X, hit.Y, hit.Radius, hit.Damage, hit.Color, sourceId: hit.SourceId);
            Emit(new CombatEvent { Type = "explosion", X = hit.X, Y = hit.Y, Radius = hit.Radius, Color = hit.Color });
        }
        foreach (var enemy in S.Enemies.Where(e => !e.Dead).ToArray())
        {
            if (enemy.Poison.Count == 0) continue;
            foreach (var poison in enemy.Poison.ToArray())
            {
                if (enemy.Dead) break;
                double until = Math.Min(S.Time, poison.Until), elapsed = until - poison.LastTick;
                if (elapsed > 0 && (elapsed >= .25 - 1e-8 || S.Time >= poison.Until))
                { poison.LastTick = until; ApplyDamage(enemy, poison.DamagePerSecond * elapsed, poison.Color, poison.SourceId); }
            }
            enemy.Poison.RemoveAll(p => p.Until <= S.Time);
        }
        FlushDamage();
    }
    private void ApplySlow(EnemyState enemy, double factor, double seconds)
    {
        if (enemy.Dead || factor >= 1 || seconds <= 0) return;
        factor = Math.Clamp(factor, enemy.Kind == "boss" ? .65 : .35, 1);
        // A weaker slow must not refresh a stronger slow indefinitely.
        if (enemy.SlowUntil <= S.Time || factor <= enemy.SlowFactor)
        { enemy.SlowFactor = factor; enemy.SlowUntil = Math.Max(enemy.SlowUntil, S.Time + Math.Min(6, seconds)); }
    }
    private double ContentHitDamage(EnemyState e, ProjectileState p, double damage)
    {
        var q = p.Stats;
        if (e.Kind == "armored") damage *= 1 + q.Trait("armorBonus");
        if (e.Kind == "boss") damage *= 1 + q.Trait("bossBonus");
        if (e.Hp / e.MaxHp <= .15) damage *= 1 + q.Trait("finisher") * (e.Kind == "boss" ? .4 : 1);
        if (e.SlowUntil > S.Time) damage *= 1 + q.Trait("chilledBonus");
        double progress = MathEx.Clamp((e.Y - A.Top) / (A.Breach - A.Top), 0, 1);
        damage *= 1 + q.Trait("dangerGain") * progress;
        damage *= 1 + q.Trait("woundedGain") * (1 - (double)S.Health / R.MaxHealth);
        if (progress < q.Trait("dangerThreshold")) damage *= q.Trait("safeDamage", 1);
        if (S.RageUntil > S.Time) damage *= 1 + q.Trait("breachBoost");
        damage *= 1 + Math.Min(8, p.Pierced) * q.Trait("pierceGain");
        return damage;
    }
    private void OnContentHit(EnemyState enemy, ProjectileState p, double damage)
    {
        var q = p.Stats; var owner = SourceDie(p.SourceDieId); bool first = !p.FirstHitDone;
        p.FirstHitDone = true;
        if (first && q.Trait("rootFirst") > 0)
        {
            double income = q.Trait("energyFirst");
            if (q.Trait("dangerEnergy") > 0 && enemy.Y > A.Top + (A.Breach - A.Top) * .75) income += q.Trait("dangerEnergy");
            S.Energy = Math.Min(999999, S.Energy + Math.Min(12, income));
        }
        if (q.Trait("pierceEnergy") > 0 && p.Pierced >= 3 && !p.PierceRewarded) { p.PierceRewarded = true; S.Energy = Math.Min(999999, S.Energy + q.Trait("pierceEnergy")); }
        if (q.Trait("wallEnergy") > 0 && first && p.WallsHit >= 3) S.Energy = Math.Min(999999, S.Energy + q.Trait("wallEnergy"));
        if (owner is not null && q.Trait("rampMax") > 0)
        {
            owner.Streak = owner.LastTarget == enemy.Id ? Math.Min((int)q.Trait("rampMax"), owner.Streak + 1) : Math.Min((int)q.Trait("rampMax"), 1 + (int)(owner.Streak * q.Trait("rampKeep")));
            owner.LastTarget = enemy.Id; owner.LastHitTime = S.Time;
            if (!p.RampBurstDone && owner.Streak >= q.Trait("rampMax") && q.Trait("rampBurst") > 0)
            { p.RampBurstDone = true; AreaDamage(enemy.X, enemy.Y, 44, damage * q.Trait("rampBurst"), q.Color, enemy.Id, p.SourceDieId); Emit(new CombatEvent { Type = "explosion", X = enemy.X, Y = enemy.Y, Radius = 44, Color = q.Color }); }
        }
        if (!enemy.Dead)
        {
            ApplySlow(enemy, q.SlowFactor, q.SlowSeconds);
            if (q.Trait("mark") > 0)
            { double mark = 1 + Math.Min(.3, q.Trait("mark")); if (enemy.MarkUntil <= S.Time || mark >= enemy.MarkFactor) { enemy.MarkFactor = mark; enemy.MarkUntil = Math.Max(enemy.MarkUntil, S.Time + 2); } }
            if (q.Trait("knockback") > 0 && enemy.Y > A.Top + .75 * (A.Breach - A.Top) && enemy.NextKnockback <= S.Time)
            { enemy.Y = Math.Max(A.Top + enemy.H / 2, enemy.Y - Math.Min(3, q.Trait("knockback")) * (enemy.Kind == "boss" ? .2 : 1)); enemy.NextKnockback = S.Time + 1.5; Grid.Rebuild(S.Enemies); }
            if (q.Trait("poison") > 0)
            {
                int cap = Math.Clamp((int)q.Trait("poisonCap", 10), 1, 20);
                for (int i = 0; i < Math.Clamp((int)q.Trait("poisonStacks", 1), 1, 3); i++)
                    AddPoison(enemy, new PoisonStack { SourceId = p.SourceDieId, DamagePerSecond = q.Damage * q.Trait("poison"), Until = S.Time + Math.Clamp(q.Trait("poisonDuration", 4), .25, 10), LastTick = S.Time, Color = q.Color, Spread = q.Trait("poisonSpread") > 0 }, cap);
                if (q.Trait("poisonDetonate") > 0 && enemy.Poison.Count >= 10)
                {
                    double amount = enemy.Poison.Sum(s => s.DamagePerSecond) * q.Trait("poisonDetonate");
                    enemy.Poison.RemoveRange(0, 5); ApplyDamage(enemy, amount, q.Color, p.SourceDieId);
                    Emit(new CombatEvent { Type = "explosion", X = enemy.X, Y = enemy.Y, Radius = 24, Color = q.Color });
                }
            }
        }
        if (q.Trait("slowRadius") > 0)
            foreach (var e in Grid.Query(enemy.X - q.Trait("slowRadius"), enemy.Y - q.Trait("slowRadius"), enemy.X + q.Trait("slowRadius"), enemy.Y + q.Trait("slowRadius")))
                if (MathEx.Dist2(e.X, e.Y, enemy.X, enemy.Y) <= Math.Pow(q.Trait("slowRadius"), 2)) ApplySlow(e, q.SlowFactor, q.SlowSeconds);
        if (!p.AftershockDone && q.Trait("aftershock") > 0 && S.TimedHits.Count < 256)
        { p.AftershockDone = true; S.TimedHits.Add(new TimedAreaHit { Due = S.Time + .25, X = enemy.X, Y = enemy.Y, Radius = Math.Max(32, q.BlastRadius), Damage = damage * q.Trait("aftershock"), Color = q.Color, SourceId = p.SourceDieId }); }
        if (enemy.Dead && q.Trait("deathBurst") > 0)
            AreaDamage(enemy.X, enemy.Y, Math.Max(40, q.BlastRadius), damage * q.Trait("deathBurst"), q.Color, enemy.Id, p.SourceDieId);
        if (!p.Child && first && q.Trait("rootSplash") > 0)
            AreaDamage(enemy.X, enemy.Y, 38, damage * q.Trait("rootSplash"), q.Color, enemy.Id, p.SourceDieId);
        if (!p.Child && first && q.Trait("auraSplit") > 0)
            SpawnPrismChild(p, enemy.Id);
    }
    private void AddPoison(EnemyState enemy, PoisonStack stack, int cap)
    {
        if (enemy.Dead) return;
        if (enemy.Poison.Count < cap) { enemy.Poison.Add(stack); return; }
        var weakest = enemy.Poison.OrderBy(s => s.DamagePerSecond).ThenBy(s => s.Until).First();
        if (stack.DamagePerSecond >= weakest.DamagePerSecond) { enemy.Poison.Remove(weakest); enemy.Poison.Add(stack); }
        // Weaker poison never prolongs stronger stacks or steals their kill attribution.
    }
    private void SpawnPrismChild(ProjectileState p, long enemyId)
    {
        var stats = p.Stats.Copy(); stats.Damage *= .25; stats.Bounces = Math.Min(4, p.Bounces);
        stats.Traits["rootFirst"] = 0; stats.Traits["auraSplit"] = 0; stats.Pierces = 0; stats.ChildPierces = 0;
        var shot = new ShotSnapshot { Type = p.Type, Pips = p.Pips, Stats = stats, SourceDieId = p.SourceDieId };
        double angle = Math.Atan2(p.Vy, p.Vx) + .22;
        if (S.Projectiles.Count < R.MaxProjectiles)
        { var child = MakeProjectile(p.X, p.Y, angle, shot, true); child.LastEnemy = enemyId; S.Projectiles.Add(child); }
        else if (S.PendingShots.Count < R.MaxQueuedShots)
            S.PendingShots.Add(new PendingShot { Due = S.Time + .00001, Angle = angle, Snapshot = shot, Child = true, X = p.X, Y = p.Y, LastEnemy = enemyId });
        SortPending();
    }
    private void OnContentDeath(EnemyState enemy, long sourceId)
    {
        var recipients = new HashSet<long>(); if (sourceId > 0) recipients.Add(sourceId);
        foreach (var (id, time) in enemy.Contributors)
            if (S.Time - time <= 3 && SourceDie(id) is { } contributor && Value(ProfileFor(contributor), "assistGrowth") > 0) recipients.Add(id);
        foreach (long id in recipients)
            if (SourceDie(id) is { } die && Value(ProfileFor(die), "growthEvery") > 0) die.GrowthKills = Math.Min(1000000, die.GrowthKills + 1);
        var poison = enemy.Poison.Where(s => s.Spread && s.Until > S.Time).OrderByDescending(s => s.DamagePerSecond).Take(3).ToArray();
        if (poison.Length == 0) return;
        foreach (var e in Grid.Query(enemy.X - 80, enemy.Y - 80, enemy.X + 80, enemy.Y + 80).Where(e => !e.Dead && e.Id != enemy.Id && MathEx.Dist2(e.X, e.Y, enemy.X, enemy.Y) <= 80 * 80).OrderBy(e => e.Id).Take(3))
            foreach (var stack in poison) AddPoison(e, new PoisonStack { SourceId = stack.SourceId, DamagePerSecond = stack.DamagePerSecond * .5, Until = Math.Min(stack.Until, S.Time + 3), LastTick = S.Time, Color = stack.Color, Spread = false }, 10);
    }
}
