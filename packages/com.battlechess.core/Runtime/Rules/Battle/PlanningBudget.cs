using System;
using System.Collections.Generic;
using System.Diagnostics;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// How much route planning one drawn frame is allowed to do, and who gets
    /// to do it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured, on the Great Field, before any of this existed.</b>
    /// Planning was <b>87%</b> of all slow-frame time — drawing was 2%, the
    /// collector 4% — at a median of 22 ms a route with forty regiments on the
    /// field. The worst frame planned <b>41 routes and took 1 652 ms</b>.
    /// </para>
    /// <para>
    /// What made it that bad was not the cadence, which was working: about one
    /// re-plan per marching regiment every three or four ticks, exactly as
    /// <b>M39</b> intended. It was that a slow frame runs several ticks to
    /// catch up — the host caps it at eight — and every one of those ticks does
    /// its own share of re-planning. Fourteen regiments marching, one re-plan
    /// each per three or four ticks, eight ticks in the frame: thirty-odd
    /// routes, all inside one frame, and the frame's own slowness feeds the
    /// accumulator that makes the next one run eight ticks too. The catch-up
    /// cap was not a safety valve. It was the multiplier.
    /// </para>
    /// <para>
    /// So two rules, and they are deliberately about <i>frames</i> rather than
    /// ticks, because a frame is what a person waits for.
    /// </para>
    /// <para>
    /// <b>One re-plan per regiment per frame.</b> A regiment that re-plans
    /// three times across eight catch-up ticks has thrown two of those answers
    /// away unseen: nothing was drawn between them and nobody was shown the
    /// intermediate routes. This is pure waste and refusing it costs nothing at
    /// all.
    /// </para>
    /// <para>
    /// <b>A ceiling on the whole frame.</b> Past it, a regiment keeps the route
    /// it already has for another frame and asks again next time — which is
    /// what the cadence has it doing on most ticks anyway, so a deferred
    /// re-plan is an ordinary state rather than a degraded one.
    /// </para>
    /// <para>
    /// <b>Deferral is a queue, and it has to be.</b> Whoever was turned away
    /// waits in the order they were turned away in, and each frame's allowance
    /// is promised to the front of that queue before anyone else may have it.
    /// A <i>set</i> of deferred regiments was tried first and starved
    /// two-thirds of the field: the callers ask in a fixed order, so among
    /// equally-deferred regiments the ones early in that order won every time
    /// and the run settled into serving the same eight of forty. Measured, and
    /// it is what <c>NobodyIsPutOffForEver</c> exists to catch.
    /// </para>
    /// <para>
    /// <b>What this does not govern.</b> Orders a person just gave. Those go
    /// through <see cref="Marching.PlanTo"/> from the host directly and are
    /// never refused: a regiment that does not move when told to is a bug, and
    /// no frame budget is worth that. The two rules here bind the tick's own
    /// re-planning, which is where the measurement says the time actually was.
    /// </para>
    /// <para>
    /// <b>Off unless a host turns it on.</b> With no one calling
    /// <see cref="OpenFrame"/> every request is granted, so the CLI, the
    /// benches and the whole test suite behave exactly as they did. Only
    /// something drawing frames has a reason to ration by them.
    /// </para>
    /// </remarks>
    public sealed class PlanningBudget
    {
        /// <summary>Routes a frame may plan before the rest are put off.</summary>
        public const int DefaultRoutesPerFrame = 4;

        /// <summary>Milliseconds a frame may spend planning before the rest are put off.</summary>
        /// <remarks>
        /// Both ceilings apply and either one closes the frame. Routes alone
        /// would be the wrong ceiling on its own — the same measurement that
        /// found a 22 ms median also found an 81 ms worst case, so four routes
        /// is anywhere between a tenth of a frame and a third of a second.
        /// </remarks>
        public const float DefaultMillisecondsPerFrame = 8f;

        /// <summary>
        /// One lock over the whole of this, because a plan may now be worked
        /// out on a worker while the frame carries on.
        /// </summary>
        /// <remarks>
        /// The host works a player's order out off the drawing thread, and
        /// <see cref="Spent"/> is called from there while <see cref="OpenFrame"/>
        /// runs on the main thread - four collections written by one and
        /// cleared by the other. A read during another thread's resize does not
        /// reliably throw; it returns nonsense, which is how the last bug of
        /// this shape took four attempts to find. Contention is a handful of
        /// calls a frame, so one lock over the lot is the cheap answer and not
        /// a fine-grained one.
        /// </remarks>
        private readonly object _counting = new object();

        private readonly HashSet<UnitId> _plannedThisFrame = new HashSet<UnitId>();

        /// <summary>Who is waiting, oldest deferral first.</summary>
        private readonly List<UnitId> _queue = new List<UnitId>();

        private readonly HashSet<UnitId> _inQueue = new HashSet<UnitId>();

        /// <summary>Who this frame's allowance is promised to, from the front of the queue.</summary>
        private readonly HashSet<UnitId> _promised = new HashSet<UnitId>();

        private bool _rationing;
        private int _routes;
        private long _spentTicks;
        private int _maxRoutes = DefaultRoutesPerFrame;
        private float _maxMilliseconds = DefaultMillisecondsPerFrame;

        /// <summary>Whether a host is rationing planning by frame at all.</summary>
        public bool IsRationing => _rationing;

        /// <summary>Routes planned since the current frame opened.</summary>
        public int RoutesThisFrame { get { lock (_counting) return _routes; } }

        /// <summary>Milliseconds spent planning since the current frame opened.</summary>
        public float MillisecondsThisFrame
        {
            get { lock (_counting) return (float)(_spentTicks * 1000.0 / Stopwatch.Frequency); }
        }

        /// <summary>Regiments put off to a later frame and still waiting.</summary>
        public int Waiting { get { lock (_counting) return _queue.Count; } }

        /// <summary>Regiments put off in this run of the battle, ever.</summary>
        public int DeferralsEver { get; private set; }

        /// <summary>
        /// Starts a new frame's allowance, and turns rationing on if it was
        /// not already.
        /// </summary>
        public void OpenFrame(
            int routesPerFrame = DefaultRoutesPerFrame,
            float millisecondsPerFrame = DefaultMillisecondsPerFrame)
        {
            lock (_counting)
            {
                if (routesPerFrame < 1)
                    throw new ArgumentOutOfRangeException(nameof(routesPerFrame), "A frame must be allowed at least one route.");

                if (millisecondsPerFrame <= 0f)
                    throw new ArgumentOutOfRangeException(nameof(millisecondsPerFrame), "A frame must be allowed some time to plan in.");

                _rationing = true;
                _maxRoutes = routesPerFrame;
                _maxMilliseconds = millisecondsPerFrame;

                _routes = 0;
                _spentTicks = 0;

                _plannedThisFrame.Clear();

                // This frame's allowance is promised to the front of the queue.
                // Not the whole queue — only as many as the frame could serve —
                // so that a long queue does not lock out every regiment that has
                // not yet been deferred at all.
                _promised.Clear();

                for (int i = 0; i < _queue.Count && i < _maxRoutes; i++)
                    _promised.Add(_queue[i]);
                    }
        }

        /// <summary>Gives up rationing, so every request is granted again.</summary>
        public void Stop()
        {
            lock (_counting)
            {
                _rationing = false;
                _plannedThisFrame.Clear();
                _queue.Clear();
                _inQueue.Clear();
                _promised.Clear();
                    }
        }

        /// <summary>
        /// Whether <paramref name="unit"/> may work out a new route now, or
        /// should keep the one it has and ask again next frame.
        /// </summary>
        public bool MayPlan(UnitId unit)
        {
            lock (_counting)
            {
                if (!_rationing) return true;

                // Already answered this frame. Nothing has been drawn since, so a
                // second answer could not be seen even if it differed.
                if (_plannedThisFrame.Contains(unit)) return false;

                if (_routes >= _maxRoutes || MillisecondsThisFrame >= _maxMilliseconds)
                    return TurnAway(unit);

                // Promised regiments always get through. Anyone else may only have
                // what is left over once every promise this frame could still be
                // kept — otherwise a regiment that has never waited takes the place
                // of one that has been waiting for frames.
                if (_promised.Contains(unit)) return true;

                int left = _maxRoutes - _routes;

                if (_promised.Count > 0 && left <= _promised.Count)
                    return TurnAway(unit);

                return true;
                    }
        }

        private bool TurnAway(UnitId unit)
        {
            if (_inQueue.Add(unit))
            {
                _queue.Add(unit);
                DeferralsEver++;
            }

            return false;
        }

        /// <summary>
        /// Records that <paramref name="unit"/> has just planned, and what it
        /// cost. Call this whether the plan succeeded or failed — a route that
        /// could not be found cost the same search as one that could.
        /// </summary>
        /// <summary>
        /// Records time spent on planning work that produced no route — the
        /// placement search that found nowhere to stand being the one case.
        /// </summary>
        /// <remarks>
        /// <b>M93.</b> Charging this through <see cref="Spent"/> would be wrong
        /// twice over: it would spend one of the frame's <i>routes</i> on a
        /// route that does not exist, and it would mark the regiment as having
        /// planned this frame when it has not. Only the milliseconds are real,
        /// so only the milliseconds are charged.
        /// </remarks>
        public void SpentWithoutPlanning(long stopwatchTicks)
        {
            lock (_counting)
            {
                if (!_rationing) return;

                _spentTicks += stopwatchTicks;
            }
        }

        public void Spent(UnitId unit, long stopwatchTicks)
        {
            lock (_counting)
            {
                if (!_rationing) return;

                _plannedThisFrame.Add(unit);

                if (_inQueue.Remove(unit)) _queue.Remove(unit);
                _promised.Remove(unit);

                _routes++;
                _spentTicks += stopwatchTicks;
                    }
        }
    }
}
