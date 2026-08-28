using System;
using UnityEngine;

namespace BattleChess.Unity
{
    /// <summary>
    /// What the last few seconds of frames cost, as a readout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three numbers, not one.</b> A rate averaged over half a second is the
    /// only one of them that sits still enough to read, and on its own it is the
    /// least informative: this game's frames are not uniformly slow, they stop.
    /// One route plan allocates 205 kB and a wing ordered together plans several
    /// at once, so the frame that matters is the worst one in the last few
    /// seconds, and an average of thirty of its neighbours hides it completely.
    /// So the readout carries the smoothed rate, the frame time behind it, and
    /// <b>the worst frame still inside the window</b> - which is the one you
    /// actually felt, and it stays up long enough to look at after you feel it.
    /// </para>
    /// <para>
    /// <b>It is timed, not counted.</b> <c>1 / Time.deltaTime</c> is the frame
    /// Unity was paced to and not the work the frame did; vsync flattens it to
    /// the refresh rate and a frame that overran by a millisecond looks exactly
    /// like one that did not. This is fed the same whole-frame stopwatch the
    /// slow-frame line in the console is fed, so the counter on screen and the
    /// entry in the recording can never disagree about what a frame cost (W5).
    /// </para>
    /// <para>
    /// Kept out of the simulation entirely, like <see cref="DebugConsole"/>:
    /// a plain class the view layer owns and hands frame times to.
    /// </para>
    /// </remarks>
    public sealed class FrameRate
    {
        /// <summary>Seconds of frames the rate is averaged over.</summary>
        /// <remarks>
        /// Half a second. Shorter and the number flickers too fast to read a
        /// digit off; longer and a real change in load takes visibly too long
        /// to show up, which makes it useless for the thing a counter is for -
        /// toggling an option and seeing what it cost.
        /// </remarks>
        private const float SmoothingSeconds = 0.5f;

        /// <summary>How long the worst frame is held on screen.</summary>
        /// <remarks>
        /// Three seconds, because it has to outlive the reaction to it. A stall
        /// you notice is a stall you then look up at, and a worst-frame figure
        /// that has already aged out by the time your eyes arrive reports
        /// nothing about the thing that made you look.
        /// </remarks>
        private const float WorstSeconds = 3f;

        /// <summary>A frame at or over this is drawn as trouble.</summary>
        /// <remarks>
        /// The same 33 ms the console's slow-frame line uses. Two numbers for
        /// one idea would let the counter go amber while the recording stays
        /// silent, and then the two would have to be reconciled by hand.
        /// </remarks>
        public const float SlowMs = 33f;

        /// <summary>A frame at or over this has visibly stopped.</summary>
        private const float StalledMs = 100f;

        // A ring of the last few seconds. Fixed size and never reallocated -
        // a counter that litters is a counter that causes the collections it
        // is there to show you. A thousand frames covers the worst-frame window
        // down to about 340 a second, and above that the window quietly
        // shortens rather than the figure going wrong: a machine running that
        // fast is not one anybody is investigating a stall on.
        private const int Capacity = 1024;

        private readonly float[] _ms = new float[Capacity];
        private readonly float[] _at = new float[Capacity];
        private int _next;
        private int _held;

        // The split is smoothed but not kept per frame. A worst-frame figure
        // wants the ring; "where does the frame go" wants a steady average and
        // would be unreadable flickering at sixty a second. So these are a
        // running mean held beside the ring rather than four more arrays.
        private float _sim, _views, _tracking, _gui;

        private float _now;

        /// <summary>Frames a second, averaged over <see cref="SmoothingSeconds"/>.</summary>
        public float Fps { get; private set; }

        /// <summary>What the average frame in that window cost, in milliseconds.</summary>
        public float Ms { get; private set; }

        /// <summary>The slowest single frame in the last <see cref="WorstSeconds"/>.</summary>
        public float WorstMs { get; private set; }

        /// <summary>Where the smoothed frame went, in milliseconds.</summary>
        /// <remarks>
        /// Everything the frame did that none of the four clocks were watching:
        /// Unity's own rendering and, in the editor, the editor itself. It is a
        /// residual and not a measurement, which is exactly what makes it worth
        /// showing - a large rest says the cost is somewhere nobody has put a
        /// clock yet, and that is a different investigation from a large gui.
        /// </remarks>
        public float RestMs => MathF.Max(0f, Ms - _sim - _views - _tracking - _gui);

        /// <summary>The simulation's share of the smoothed frame.</summary>
        public float SimMs => _sim;

        /// <summary>Moving and drawing the unit views.</summary>
        public float ViewsMs => _views;

        /// <summary>Fog, sighting and selection tracking.</summary>
        public float TrackingMs => _tracking;

        /// <summary>Every IMGUI pass in the frame together - panels, labels, console.</summary>
        public float GuiMs => _gui;

        /// <summary>Adds a frame, timed across the whole frame rather than counted.</summary>
        /// <param name="ms">What that frame cost, wall clock.</param>
        /// <param name="sim">Of that, the simulation.</param>
        /// <param name="views">Of that, the unit views.</param>
        /// <param name="tracking">Of that, fog and sighting.</param>
        /// <param name="gui">Of that, every IMGUI pass together.</param>
        public void Record(float ms, float sim, float views, float tracking, float gui)
        {
            // Unscaled, and it has to be: the harness runs the battle at x1 to
            // x64 and pauses it outright, and none of that changes how long a
            // frame took to draw.
            _now += Time.unscaledDeltaTime;

            _ms[_next] = ms;
            _at[_next] = _now;
            _next = (_next + 1) % Capacity;
            if (_held < Capacity) _held++;

            float total = 0f;
            int counted = 0;
            float worst = 0f;

            for (int i = 0; i < _held; i++)
            {
                float age = _now - _at[i];

                if (age <= WorstSeconds && _ms[i] > worst) worst = _ms[i];

                if (age > SmoothingSeconds) continue;

                total += _ms[i];
                counted++;
            }

            WorstMs = worst;

            if (counted == 0) return;

            Ms = total / counted;
            Fps = Ms > 0f ? 1000f / Ms : 0f;

            // An exponential mean rather than a windowed one: the split does
            // not need the ring to answer the only question asked of it. The
            // weight is taken from how many frames the window is holding, so
            // the split settles at about the rate the rate does.
            float weight = MathF.Min(1f, 2f / MathF.Max(1f, counted));

            _sim += (sim - _sim) * weight;
            _views += (views - _views) * weight;
            _tracking += (tracking - _tracking) * weight;
            _gui += (gui - _gui) * weight;
        }

        /// <summary>How wide the readout needs to be drawn.</summary>
        public const float Width = 210f;

        /// <summary>
        /// Draws the readout right-aligned in <paramref name="area"/>.
        /// </summary>
        /// <remarks>
        /// Its own style rather than the shared skin, because every panel in the
        /// harness sets <c>GUI.skin.label</c> and the counter must not be one
        /// more thing that moves when somebody tunes those. Right-aligned, and
        /// every figure padded to a fixed number of columns, so a rate crossing
        /// from 99 to 100 does not shove the two numbers beside it sideways -
        /// which is what makes a jittering readout unreadable.
        /// </remarks>
        public void Draw(Rect area)
        {
            _style ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold,
                fontSize = 13,
            };

            Color original = GUI.color;

            GUI.color = WorstMs >= StalledMs
                ? new Color(1f, 0.45f, 0.40f)
                : WorstMs >= SlowMs
                    ? new Color(1f, 0.82f, 0.35f)
                    : new Color(0.55f, 1f, 0.70f);

            // The worst frame is named as a worst frame and not left as a bare
            // third number. Somebody reading "60  16.7  102" has to be told
            // once what the third column is; "worst 102" tells them.
            GUI.Label(area, $"{Fps,3:0} fps   {Ms,4:0.0} ms   worst {WorstMs,3:0}", _style);

            GUI.color = original;
        }

        /// <summary>How wide the split needs to be drawn.</summary>
        public const float SplitWidth = 430f;

        /// <summary>
        /// Draws where the frame went, under the counter.
        /// </summary>
        /// <remarks>
        /// Behind its own toggle rather than always up. The rate is for
        /// watching; this is for asking a question, and four more numbers in
        /// the corner of every session are four more things to read past.
        /// </remarks>
        public void DrawSplit(Rect area)
        {
            _split ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Normal,
                fontSize = 12,
            };

            GUI.Box(area, string.Empty);

            Color original = GUI.color;
            GUI.color = new Color(0.85f, 0.88f, 0.95f, 0.95f);

            // Named in the order they run, so the row reads as a frame does.
            GUI.Label(
                new Rect(area.x, area.y, area.width - 8f, area.height),
                $"sim {SimMs,5:0.0}   views {ViewsMs,5:0.0}   tracking {TrackingMs,5:0.0}   " +
                $"gui {GuiMs,5:0.0}   rest {RestMs,5:0.0}",
                _split);

            GUI.color = original;
        }

        private GUIStyle _style;
        private GUIStyle _split;
    }
}
