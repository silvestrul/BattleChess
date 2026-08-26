using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Temporary. The merge swapped two things underneath every clearance
    /// check — a cheaper sweep and a spatial index — and both are the kind of
    /// change that fails by saying "clear" when it should say "blocked".
    /// </summary>
    public sealed class MergeDifferentialTests
    {
        private readonly ITestOutputHelper _out;

        public MergeDifferentialTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void TouchesAgreesWithFirstTouchEverywhere()
        {
            var rng = new Random(20260819);
            int disagreed = 0, touched = 0;

            for (int i = 0; i < 400_000; i++)
            {
                var a = new OrientedRect(
                    new Vec2((float)(rng.NextDouble() * 200 - 100), (float)(rng.NextDouble() * 200 - 100)),
                    Facing.FromRadians((float)(rng.NextDouble() * Math.PI * 2)),
                    new Footprint(10f + (float)rng.NextDouble() * 60f, 5f + (float)rng.NextDouble() * 30f));

                var b = new OrientedRect(
                    new Vec2((float)(rng.NextDouble() * 200 - 100), (float)(rng.NextDouble() * 200 - 100)),
                    Facing.FromRadians((float)(rng.NextDouble() * Math.PI * 2)),
                    new Footprint(10f + (float)rng.NextDouble() * 60f, 5f + (float)rng.NextDouble() * 30f));

                var travel = new Vec2(
                    (float)(rng.NextDouble() * 300 - 150), (float)(rng.NextDouble() * 300 - 150));

                bool cheap = Sweep.Touches(a, travel, b);
                bool full = Sweep.FirstTouch(a, travel, b, out _);

                if (cheap) touched++;
                if (cheap != full) disagreed++;
            }

            _out.WriteLine($"{touched:N0} of 400,000 touched; {disagreed:N0} disagreements");

            Assert.Equal(0, disagreed);
        }

        [Theory]
        [InlineData("crucible")]
        [InlineData("brokencountry")]
        [InlineData("longmarch")]
        public void TheIndexNeverHidesABodyAFullScanWouldFind(string key)
        {
            BattleState battle = BenchScenariosTests.Load(key);

            var rng = new Random(1);
            var got = new List<UnitInstance>();

            int missed = 0, asked = 0;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                float reach = unit.Footprint.BoundingRadius;

                for (int t = 0; t < 40; t++)
                {
                    var from = new Vec2(
                        (float)(rng.NextDouble() * battle.Terrain.Bounds.Max.X),
                        (float)(rng.NextDouble() * battle.Terrain.Bounds.Max.Y));
                    var to = new Vec2(
                        (float)(rng.NextDouble() * battle.Terrain.Bounds.Max.X),
                        (float)(rng.NextDouble() * battle.Terrain.Bounds.Max.Y));

                    battle.WhereEverybodyIs.Near(battle.AllUnits, from, to, reach, got);

                    var quick = new HashSet<UnitInstance>(got);

                    // What a full scan would have had to consider: anything whose
                    // bounding circle can reach the segment at all.
                    foreach (UnitInstance other in battle.UnitsOnField())
                    {
                        float span = reach + other.Footprint.BoundingRadius;

                        if (DistanceToSegment(other.Position, from, to) > span) continue;

                        asked++;
                        if (!quick.Contains(other)) missed++;
                    }
                }
            }

            _out.WriteLine($"{key}: {asked:N0} bodies a full scan would weigh, {missed:N0} the index hid");

            Assert.Equal(0, missed);
        }

        private static float DistanceToSegment(Vec2 point, Vec2 from, Vec2 to)
        {
            Vec2 travel = to - from;
            float length = travel.Length;

            if (length <= 0f) return Vec2.Distance(point, from);

            Vec2 along = travel / length;
            float projected = MathF.Max(0f, MathF.Min(length, Vec2.Dot(point - from, along)));

            return Vec2.Distance(point, from + along * projected);
        }
    }
}
