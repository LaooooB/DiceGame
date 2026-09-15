namespace DiceGame.Core;

/// <summary>Town-only ricochet. It cannot touch battle RNG, enemies or projectile state.</summary>
public sealed class TownSimulation(CampaignProgression progression)
{
    private CampaignCatalog Catalog => progression.Catalog;
    public BoundsD TileBounds(int index)
    {
        var t = Catalog.Definition.Town;
        double cellWidth = 760.0 / t.Columns, cellHeight = 300.0 / t.Rows;
        double x = 20 + (index % t.Columns + .5) * cellWidth, y = 36 + (index / t.Columns + .5) * cellHeight;
        return new BoundsD(x - cellWidth * .40, x + cellWidth * .40, y - cellHeight * .32, y + cellHeight * .32);
    }
    public void Launch(CampaignState state, double angle, string die)
    {
        if (state.Flight is not null || state.ActiveRunId != "" || state.Balance("supplies") < 1 || !state.UnlockedDice.Contains(die))
            throw new InvalidOperationException("每次城镇派遣需要 1 补给，且不能同时进行远征。");
        if (!double.IsFinite(angle)) throw new ArgumentOutOfRangeException(nameof(angle));
        angle = MathEx.Clamp(angle, -Math.PI + .18, -.18);
        state.Resources["supplies"] = state.Balance("supplies") - 1;
        state.Flight = new TownFlight { Vx = Math.Cos(angle) * 520, Vy = Math.Sin(angle) * 520, Remaining = Catalog.Definition.Town.DispatchSeconds, Die = die };
        state.Revision++;
    }
    public double AutoAngle(CampaignState state)
    {
        int target = state.Buildings.Values.FirstOrDefault(b => b.Constructing)?.Tile ?? Catalog.Definition.Town.WoodTiles.Concat(Catalog.Definition.Town.StoneTiles).FirstOrDefault();
        var box = TileBounds(target);
        return Math.Atan2((box.Top + box.Bottom) * .5 - 430, (box.Left + box.Right) * .5 - 400);
    }
    public void Step(CampaignState state, double dt)
    {
        if (!MathEx.Finite(dt, 0, .101)) throw new ArgumentOutOfRangeException(nameof(dt));
        var f = state.Flight; if (f is null) return;
        f.Remaining -= dt;
        // Substeps bound displacement to ~4px; every block is wider than 12px even at maximum grid dimensions.
        int substeps = Math.Max(1, (int)Math.Ceiling(dt * 120));
        for (int step = 0; step < substeps; step++)
        {
            double x0 = f.X, y0 = f.Y;
            f.X += f.Vx * dt / substeps; f.Y += f.Vy * dt / substeps;
            if (f.X < 8) { f.X = 16 - f.X; f.Vx = Math.Abs(f.Vx); }
            if (f.X > 792) { f.X = 1584 - f.X; f.Vx = -Math.Abs(f.Vx); }
            if (f.Y < 8) { f.Y = 16 - f.Y; f.Vy = Math.Abs(f.Vy); }
            if (f.Y >= 460 || f.Remaining <= 0 || f.Hits >= Catalog.Definition.Town.MaxHits)
            { state.Flight = null; state.Revision++; return; }
            var t = Catalog.Definition.Town;
            for (int index = 0; index < t.Columns * t.Rows; index++)
            {
                var building = state.Buildings.FirstOrDefault(p => p.Value.Tile == index);
                if (!Catalog.IsResourceTile(index) && building.Value is null) continue;
                var box = TileBounds(index);
                bool inside = f.X >= box.Left - 6 && f.X <= box.Right + 6 && f.Y >= box.Top - 6 && f.Y <= box.Bottom + 6;
                if (f.LastTile == index) { if (!inside) f.LastTile = -1; else continue; }
                if (!inside) continue;
                bool vertical = y0 < box.Top - 6 || y0 > box.Bottom + 6;
                if (vertical) { f.Vy = -f.Vy; f.Y = y0; } else { f.Vx = -f.Vx; f.X = x0; }
                f.LastTile = index; f.Hits++;
                string? resource = t.WoodTiles.Contains(index) ? "wood" : t.StoneTiles.Contains(index) ? "stone" : null;
                if (resource is not null) state.Resources[resource] = checked(state.Balance(resource) + t.ResourcePerHit);
                if (building.Value is { Constructing: true } b)
                { b.Work += t.WorkPerHit; progression.CompleteConstruction(state, building.Key); }
                state.Revision++; break;
            }
        }
    }
}
