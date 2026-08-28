using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The collection every test that writes a global planner setting belongs
    /// to, and which never runs beside anything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is M79's shared state, found.</b> That entry recorded a suite
    /// that failed two or three tests it passed in isolation, with the failing
    /// variants moving between consecutive runs of identical code, and narrowed
    /// the cause to "something shared with the rest of the suite, not within the
    /// planning path". It is this: the planner's levers —
    /// <c>RouteSearch.MostPlaces</c>, <c>StagedRoutePlanner.AcceptBentLadder</c>,
    /// <c>RegimentGrid.SpacingMultiple</c> and a dozen more — are plain statics,
    /// and xUnit runs test <i>classes</i> in parallel. A dozen classes write
    /// them. Every one of those writes lands on whatever else is planning a
    /// route at that instant.
    /// </para>
    /// <para>
    /// <b>Which is exactly the shape of the symptom.</b>
    /// <c>WingOrderTests</c> plans the same wing twice and compares the two
    /// sets of routes; a lever flipped by another class between the halves makes
    /// them disagree about a route neither of them planned wrongly. Nothing is
    /// broken in the planner, so it passes alone; which pair of variants loses
    /// the race is down to thread scheduling, so it moves run to run.
    /// </para>
    /// <para>
    /// <b>Why a collection rather than a lock.</b> A lock around each lever
    /// would serialise the writes without making a read see a consistent set,
    /// and the levers are read millions of times a plan where they are written
    /// once a test — the wrong side to pay on. <c>DisableParallelization</c>
    /// keeps this collection from running beside any other, so the mutators run
    /// alone and everything else keeps its parallelism.
    /// </para>
    /// <para>
    /// <b>Joining is not optional.</b> A test that writes a lever and is not in
    /// this collection reintroduces the fault, and it will show up somewhere
    /// else entirely — which is what made it cost three attempts to find. Every
    /// class that assigns to a static under <c>Rules.Battle</c> belongs here,
    /// skipped or not: a skipped test today is an un-skipped one tomorrow.
    /// </para>
    /// </remarks>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class PlannerLevers
    {
        public const string Name = "planner levers";
    }
}
