using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Security tests, not balance tests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything else in this suite asks whether a rule produces a good game.
    /// These ask whether a commander can learn something he has not earned, and
    /// they fail for a different reason: not "this is no fun" but "this is
    /// cheating". A fog bug that draws the enemy in the wrong place is a bug; a
    /// fog bug that hands over their morale is a broken promise, and the two
    /// deserve different alarms.
    /// </para>
    /// <para>
    /// Most of them work by walking the whole <see cref="PlayerView"/> object
    /// graph with reflection rather than by checking the properties anybody
    /// thought to check. That matters: the leak nobody writes a test for is
    /// exactly the field somebody adds next month, and a reflective walk catches
    /// it without being told the field exists.
    /// </para>
    /// </remarks>
    public sealed class FogLeakTests
    {
        // ---- Hidden means absent ----------------------------------------------

        [Fact]
        public void AnEnemyBehindARangeAppearsNowhereInTheView()
        {
            Field field = Field.WithRidge();

            field.State.Vision.Recompute(field.State);

            PlayerView view = PlayerViewProjector.Project(field.State, field.Ours.Owner);

            Assert.Empty(view.Sighted);
            Assert.Empty(view.Remembered);

            Assert.DoesNotContain(FloatsIn(view), value => Near(value, field.Theirs.Position.X));

            Assert.DoesNotContain(FloatsIn(view), value => Near(value, field.Theirs.Position.Y));
        }

        [Fact]
        public void NothingInAViewLeadsBackToTheSimulation()
        {
            Field field = Field.InTheOpen();

            field.State.Vision.Recompute(field.State);

            PlayerView view = PlayerViewProjector.Project(field.State, field.Ours.Owner);

            Assembly rules = typeof(BattleState).Assembly;

            string[] reachable = Walk(view)
                .Select(o => o.GetType())
                .Where(t => t.Assembly == rules)
                .Select(t => t.FullName ?? t.Name)
                .Distinct()
                .ToArray();

            Assert.True(reachable.Length == 0,
                "A view must be made entirely of public contract types. These came from the rules " +
                "assembly and would hand a client a door into the live battle: " +
                string.Join(", ", reachable));
        }

        // ---- Body, not spirit --------------------------------------------------

        [Fact]
        public void ASightingTellsYouNothingAboutMoraleCohesionOrAmmunition()
        {
            string[] forbidden = { "morale", "organization", "cohesion", "ammunition", "shots", "quality", "stance", "order", "route" };

            string[] leaked = typeof(SightedUnit)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .Where(name => forbidden.Any(word => name.ToLowerInvariant().Contains(word)))
                .ToArray();

            Assert.True(leaked.Length == 0,
                "Whether the line opposite is about to crack is the judgement the whole game turns on. " +
                "It must be bought by pressing the attack, never read off a bar: " + string.Join(", ", leaked));
        }

        [Fact]
        public void YouKnowYourOwnRegimentsCompletely()
        {
            Field field = Field.InTheOpen();

            field.Ours.Morale = 0.42f;
            field.State.Vision.Recompute(field.State);

            CommandedUnit mine = PlayerViewProjector
                .Project(field.State, field.Ours.Owner)
                .Own.Single();

            Assert.Equal(field.Ours.Strength, mine.Strength);
            Assert.Equal(0.42f, mine.Morale, 3);

            Assert.True(mine.Organization > 0f,
                "A commander knows the state of his own men exactly. Fog is about the other side.");
        }

        [Fact]
        public void AVisibleEnemyIsCountedRoughlyRatherThanExactly()
        {
            Field field = Field.InTheOpen();

            field.Theirs.TakeCasualties(37);
            field.State.Vision.Recompute(field.State);

            SightedUnit seen = PlayerViewProjector
                .Project(field.State, field.Ours.Owner)
                .Sighted.Single();

            int band = (int)MathF.Round(field.Theirs.Def.DefaultStrength * 0.05f);

            Assert.True(Math.Abs(seen.EstimatedStrength - field.Theirs.Strength) <= band,
                $"An estimate should be close: {seen.EstimatedStrength} against a true {field.Theirs.Strength}.");

            Assert.True(seen.EstimatedStrength % band == 0,
                $"And coarse — {seen.EstimatedStrength} must fall on a band of {band}, or the player is " +
                "reading an exact headcount off a field half a mile away.");
        }

        [Fact]
        public void AnEstimateNeverReportsAnEmptyFieldWhereMenAreStanding()
        {
            Field field = Field.InTheOpen();

            field.Theirs.TakeCasualties(field.Theirs.Strength - 3);
            field.State.Vision.Recompute(field.State);

            SightedUnit seen = PlayerViewProjector
                .Project(field.State, field.Ours.Owner)
                .Sighted.Single();

            Assert.True(seen.EstimatedStrength > 0,
                "Three men left is a regiment in ruins, not an empty stretch of grass. Rounding must " +
                "never turn something you can plainly see into nothing.");
        }

        // ---- A memory that updates itself is not a memory -----------------------

        [Fact]
        public void AGhostMarkerHoldsTheStrengthTheyHadThenNotTheStrengthTheyHaveNow()
        {
            Field field = Field.WithRidge();

            // In plain view, on our side of the ridge, at full strength.
            field.Theirs.Position = field.State.Terrain.Bounds.Centre - new Vec2(50f, 0f);
            field.State.Vision.Recompute(field.State, tick: 0);

            int whenSeen = field.Theirs.Strength;

            // They slip behind the range and are cut up out of sight.
            field.Theirs.Position = field.State.Terrain.Bounds.Centre + new Vec2(100f, 0f);
            field.Theirs.TakeCasualties(whenSeen / 2);
            field.State.Vision.Recompute(field.State, tick: 60);

            PlayerView view = PlayerViewProjector.Project(field.State, field.Ours.Owner, tick: 60);

            Assert.Empty(view.Sighted);

            RememberedUnit ghost = view.Remembered.Single();

            Assert.Equal(60, ghost.AgeTicks);

            Assert.True(Math.Abs(ghost.EstimatedStrength - whenSeen) < Math.Abs(ghost.EstimatedStrength - field.Theirs.Strength),
                $"The marker must report what was seen ({whenSeen}), not what is true now " +
                $"({field.Theirs.Strength}). It reported {ghost.EstimatedStrength}.");

            Assert.DoesNotContain(FloatsIn(view), value => Near(value, field.Theirs.Position.X));
        }

        [Fact]
        public void AGhostMarkerStaysWhereTheyWereLastSeen()
        {
            Field field = Field.WithRidge();

            Vec2 seenAt = field.State.Terrain.Bounds.Centre - new Vec2(50f, 0f);

            field.Theirs.Position = seenAt;
            field.State.Vision.Recompute(field.State, tick: 0);

            field.Theirs.Position = field.State.Terrain.Bounds.Centre + new Vec2(300f, 0f);
            field.State.Vision.Recompute(field.State, tick: 120);

            RememberedUnit ghost = PlayerViewProjector
                .Project(field.State, field.Ours.Owner, tick: 120)
                .Remembered.Single();

            Assert.True(Vec2.Distance(ghost.LastSeenAt, seenAt) < 1f,
                $"The marker belongs where they went out of sight ({seenAt}), not where they are " +
                $"now — it sat at {ghost.LastSeenAt}.");
        }

        [Fact]
        public void ARegimentDestroyedOutOfSightIsStillRememberedAsStanding()
        {
            Field field = Field.WithRidge();

            field.Theirs.Position = field.State.Terrain.Bounds.Centre - new Vec2(50f, 0f);
            field.State.Vision.Recompute(field.State, tick: 0);

            field.Theirs.Position = field.State.Terrain.Bounds.Centre + new Vec2(300f, 0f);
            field.Theirs.State = UnitState.Destroyed;
            field.State.Vision.Recompute(field.State, tick: 60);

            PlayerView view = PlayerViewProjector.Project(field.State, field.Ours.Owner, tick: 60);

            Assert.Single(view.Remembered);

            Assert.True(view.Remembered[0].EstimatedStrength > 0,
                "An enemy wiped out where nobody could see it must go on sitting in the plan as a " +
                "regiment still to be dealt with. Learning it is gone for free would be the most " +
                "useful leak of all.");
        }

        // ---- Both sides, symmetrically -----------------------------------------

        [Fact]
        public void EachCommanderGetsHisOwnPictureAndNobodyElsesUnits()
        {
            Field field = Field.InTheOpen();

            field.State.Vision.Recompute(field.State);

            PlayerView ours = PlayerViewProjector.Project(field.State, field.Ours.Owner);
            PlayerView theirs = PlayerViewProjector.Project(field.State, field.Theirs.Owner);

            Assert.All(ours.Own, unit => Assert.Equal(field.Ours.Id, unit.Id));
            Assert.All(theirs.Own, unit => Assert.Equal(field.Theirs.Id, unit.Id));

            Assert.All(ours.Sighted, unit => Assert.NotEqual(ours.Viewer, unit.Owner));
            Assert.All(theirs.Sighted, unit => Assert.NotEqual(theirs.Viewer, unit.Owner));
        }

        [Fact]
        public void AViewWithNoFogRunningStillShowsTheEnemyRatherThanBlindingEverybody()
        {
            // A battle run without a vision system has no sightings computed at
            // all. That must read as "no fog in this battle", not as "nobody can
            // see anything" — the second turns a missing system into what looks
            // like a broken shooting rule.
            var field = new Battlefield("plains", 13100, RuleSet.MeleeOnly);

            UnitInstance ours = field.Add(0, "swordsmen", field.Centre, Facing.East);
            field.Add(1, "swordsmen", field.Centre + new Vec2(900f, 0f), Facing.West);

            PlayerView view = PlayerViewProjector.Project(field.State, ours.Owner);

            Assert.Single(view.Sighted);
        }

        // ---- Known holes -------------------------------------------------------

        [Fact(Skip = "M8 — real, and not worth an opaque handle table until orders travel over a wire.")]
        public void UnitIdsDoNotBetrayHowManyRegimentsTheEnemyHas()
        {
            // A side channel rather than a leaked field, which is why the
            // reflective sweep above cannot see it. Ids are handed out in
            // deployment order across both armies, so spotting one enemy
            // numbered nine tells you ten regiments took the field — army size
            // is real intelligence and nobody paid for it.
            //
            // The fix is a per-viewer handle: a stable opaque number minted the
            // first time an army sees a regiment, so ids reveal only the order
            // in which you found things. Cheap to build, and pointless before
            // M8, because until orders travel over a wire the client is the
            // authority and can read the ids anyway.
        }

        // ---- The walker --------------------------------------------------------

        private static bool Near(float value, float target) => MathF.Abs(value - target) < 0.01f;

        private static IReadOnlyList<float> FloatsIn(object root)
        {
            var found = new List<float>();

            foreach (object node in Walk(root))
            {
                foreach (object? value in ValuesOf(node))
                {
                    switch (value)
                    {
                        case float f: found.Add(f); break;
                        case double d: found.Add((float)d); break;
                        case int i: found.Add(i); break;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// How deep the walk follows a view before giving up.
        /// </summary>
        /// <remarks>
        /// A real view is four or five deep — view, list, unit, position,
        /// number — so this is generous. It exists because structs cannot be
        /// tracked by reference: <c>Vec2.Perpendicular</c> hands back a fresh
        /// <c>Vec2</c> every time it is read, so a visited set keyed on object
        /// identity never recognises it and the walk runs for ever. Bounding
        /// the depth is simpler than trying to tell derived values from stored
        /// ones, and costs nothing a leak could hide behind.
        /// </remarks>
        private const int MaxDepth = 8;

        /// <summary>
        /// Every object reachable from a view, following public properties,
        /// public fields and anything enumerable.
        /// </summary>
        private static IEnumerable<object> Walk(object root)
        {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<(object Node, int Depth)>();

            pending.Push((root, 0));

            while (pending.Count > 0)
            {
                (object node, int depth) = pending.Pop();

                if (node is string) continue;
                if (!node.GetType().IsValueType && !seen.Add(node)) continue;

                yield return node;

                if (depth >= MaxDepth) continue;

                if (node is IEnumerable list)
                {
                    foreach (object? item in list)
                        if (item != null) pending.Push((item, depth + 1));
                }

                foreach (object? value in ValuesOf(node))
                {
                    if (value == null) continue;
                    if (value is string) continue;
                    if (value.GetType().IsPrimitive || value.GetType().IsEnum) continue;

                    pending.Push((value, depth + 1));
                }
            }
        }

        private static IEnumerable<object?> ValuesOf(object node)
        {
            Type type = node.GetType();

            if (type.IsPrimitive || type.IsEnum || node is string) yield break;

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0) continue;

                object? value;

                try { value = property.GetValue(node); }
                catch (TargetInvocationException) { continue; }

                yield return value;
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                yield return field.GetValue(node);
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object? a, object? b) => ReferenceEquals(a, b);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        // ---- Setup -------------------------------------------------------------

        /// <summary>
        /// Two regiments, ours and theirs, with or without a range between them.
        /// </summary>
        private sealed class Field
        {
            public Battlefield Battle { get; private init; } = null!;
            public UnitInstance Ours { get; private init; } = null!;
            public UnitInstance Theirs { get; private init; } = null!;

            public BattleState State => Battle.State;

            public static Field InTheOpen() => Build(null);

            public static Field WithRidge() => Build("mountain");

            private static Field Build(string? between)
            {
                var battle = new Battlefield("plains", 13000, RuleSet.Full, canvas =>
                {
                    if (between == null) return;

                    int middle = canvas.Columns / 2;
                    canvas.Band(middle - 1, middle + 1, between);
                });

                return new Field
                {
                    Battle = battle,

                    // Deliberately off the centre line. Sitting both regiments
                    // on the same y made every coordinate they shared look like
                    // a leak, and the sweep reported our own scout's position as
                    // the enemy's. A test for hidden information has to make the
                    // hidden numbers unmistakably theirs.
                    Ours = battle.Add(0, "scouts", battle.Centre - new Vec2(100f, 0f), Facing.East),
                    Theirs = battle.Add(1, "swordsmen", battle.Centre + new Vec2(100f, 63f), Facing.West),
                };
            }
        }
    }
}
