using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using Adamantium.Fonts.TextureGeneration;

namespace Adamantium.Graphics.Fonts
{
    /// <summary>Catches glyph corruption at the moment it is CAUSED, not when it reaches the screen - the corruption is
    /// intermittent, so watching for the symptom proves nothing either way.
    /// <para>Three traps, all on the upload path: two threads inside one atlas at once; one atlas cell claimed by two
    /// different glyphs; the same glyph uploaded twice with different pixels. Each prints once per distinct offender.</para></summary>
    public static class GlyphIntegrityProbe
    {
        private sealed class AtlasState
        {
            public int Inside;
            public int OwnerThread;
            // Keyed on WHERE ONLY - layer and top-left - and deliberately not on the glyph's size. Sizes differ from
            // glyph to glyph, so a cell overwritten by a glyph of another size never matched the old key and the trap
            // stayed silent through the very corruption it was built for.
            public readonly ConcurrentDictionary<(uint Layer, int X, int Y), (uint Glyph, int W, int H)> CellOwner = new();
            public readonly ConcurrentDictionary<uint, ulong> GlyphHash = new();
            public readonly ConcurrentDictionary<uint, (uint Layer, int X, int Y, int W, int H)> GlyphCell = new();
        }

        private static readonly ConditionalWeakTable<object, AtlasState> States = new();
        private static readonly ConcurrentDictionary<string, byte> Reported = new();
        private static volatile bool _isEnabled;

        /// <summary>Whether glyph uploads go through the traps; off by default, for a hunt only. Switch it on before the
        /// first text is drawn.</summary>
        public static bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        /// <summary>Everything this probe has said so far, newest last - so a run can be asked what it saw without
        /// hunting through the console.</summary>
        public static readonly ConcurrentQueue<string> Findings = new();

        public static void EnterUpload(object atlas)
        {
            var state = States.GetOrCreateValue(atlas);
            var thread = Environment.CurrentManagedThreadId;
            var depth = Interlocked.Increment(ref state.Inside);
            var previous = Interlocked.Exchange(ref state.OwnerThread, thread);

            if (depth > 1)
                Report($"CONCURRENT UPLOAD: thread {thread} entered while thread {previous} was still inside (depth {depth})");
        }

        public static void LeaveUpload(object atlas)
        {
            var state = States.GetOrCreateValue(atlas);
            Interlocked.Decrement(ref state.Inside);
        }

        public static void Inspect(object atlas, GlyphTextureData data)
        {
            if (data == null || data.IsEmpty) return;

            var state = States.GetOrCreateValue(atlas);
            var cell = (data.DepthLayer,
                data.BoundingRect.Left, data.BoundingRect.Top,
                (int)data.FullGlyphSize.Width, (int)data.FullGlyphSize.Height);

            var where = (data.DepthLayer, data.BoundingRect.Left, data.BoundingRect.Top);
            var owner = state.CellOwner.GetOrAdd(where, (data.GlyphIndex, cell.Item4, cell.Item5));
            if (owner.Glyph != data.GlyphIndex)
                Report($"CELL COLLISION: layer {data.DepthLayer} at {where.Left},{where.Top} held glyph " +
                       $"{owner.Glyph} ({owner.W}x{owner.H}), now glyph {data.GlyphIndex} ('{data.Character}') " +
                       $"({cell.Item4}x{cell.Item5})");

            // The glyph moved: the traps above watch writes and miss a re-pack, after which units on screen keep the old cell
            // and draw other letters.
            var placed = state.GlyphCell.GetOrAdd(data.GlyphIndex, _ => cell);
            if (!placed.Equals(cell))
                Report($"GLYPH MOVED: glyph {data.GlyphIndex} ('{data.Character}') was at layer {placed.Layer} " +
                       $"{placed.X},{placed.Y} {placed.W}x{placed.H}, now layer {data.DepthLayer} " +
                       $"{cell.Item2},{cell.Item3} {cell.Item4}x{cell.Item5} - anything already drawn from the old " +
                       $"rectangle now shows a different glyph");

            var hash = Hash(data.Pixels);
            var known = state.GlyphHash.GetOrAdd(data.GlyphIndex, _ => hash);
            if (known != hash)
                Report($"GLYPH REWRITTEN: glyph {data.GlyphIndex} ('{data.Character}') uploaded again with different " +
                       $"pixels (was {known:x}, now {hash:x})");
        }

        private static ulong Hash(byte[] pixels)
        {
            var hash = 14695981039346656037UL;
            foreach (var b in pixels)
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }

            return hash;
        }

        /// <summary>Where findings are written, next to the application, so they survive however the process was
        /// started - stdout is lost unless someone redirected it, and this bug is intermittent enough that losing one
        /// occurrence costs a day.</summary>
        public static readonly string LogPath =
            System.IO.Path.Combine(AppContext.BaseDirectory, "glyph-probe.log");

        private static readonly object FileGate = new();

        private static void Report(string message)
        {
            if (!Reported.TryAdd(message, 0)) return;
            Findings.Enqueue(message);

            var line = $"[GLYPH-PROBE] {DateTime.Now:HH:mm:ss} {message}";
            Console.WriteLine(line);

            try
            {
                lock (FileGate) System.IO.File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch
            {
                // A probe that throws is worse than a probe that misses a line.
            }
        }
    }
}
