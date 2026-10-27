using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Serilog;

namespace Adamantium.Graphics;

/// <summary>Compiles every shader the application owns in a throwaway child process before startup, restarting it when it
/// dies; each attempt persists its binaries (<see cref="ShaderBinaryCache"/>), and a stamp per GPU and driver skips it next time.</summary>
public static class ShaderPrecompiler
{
    /// <summary>Master switch (off = the earlier behavior: cold launches die until the cache happens to fill).</summary>
    public static bool Enabled = true;

    /// <summary>Marks the child process whose whole job is to compile and persist, then exit.</summary>
    public const string PassArgument = "--precompile-shaders";

    private const int MaxAttempts = 25;
    private const int MaxAttemptsWithoutProgress = 3;   // beyond this, something is permanently wrong
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(2);

    private static readonly object BackgroundGate = new();
    private static readonly HashSet<string> BackgroundTried = new(StringComparer.Ordinal);

    private static string _inFlightPath;
    private static string _inFlightEffect;

    /// <summary>True in the child: this process must compile the shaders and exit, not run an application. Read once -
    /// the command line cannot change, and this is asked per shader.</summary>
    public static bool IsCompilePass { get; } = Environment.GetCommandLineArgs().Contains(PassArgument);

    // The one effect this child was asked for, or null for all of them. A named run ignores the standing verdict -
    // something decided to ask again.
    private static string RequestedEffect
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            var at = Array.IndexOf(args, PassArgument);
            return at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[at + 1]
                : null;
        }
    }

    /// <summary>Called once the graphics device exists and BEFORE anything is shown. In the child this compiles and
    /// terminates the process; in a normal launch it returns as soon as the cache is known complete.</summary>
    public static void EnsureCompiled(GraphicsDevice device)
    {
        if (device == null || !Enabled || !ShaderBinaryCache.Enabled) return;

        if (IsCompilePass)
        {
            RunCompilePass(device);   // exits the process
            return;
        }

        var stamp = StampFile(device);
        if (stamp == null || File.Exists(stamp)) return;

        var folder = Path.GetDirectoryName(stamp);
        var compiled = CachedCount(folder);
        var withoutProgress = 0;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            Log.Logger.Information($"Shader cache is cold for this driver - precompile attempt {attempt} ({compiled} shaders cached)");
            if (RunChild() && File.Exists(stamp))
            {
                Log.Logger.Information($"Shaders precompiled in {attempt} attempt(s): {CachedCount(folder)} shaders");
                return;
            }

            // The child left the name of what it was building when it died; recording it lets the next attempt skip it, or
            // one such effect would keep the whole cache cold.
            if (TakePoisoned(device) is { } poisoned)
            {
                Log.Logger.Warning($"Shader precompile: {poisoned} took the process down - skipping it from now on");
                withoutProgress = 0;
                continue;
            }

            // A dead child still made progress if it persisted something new; only a run that adds nothing counts as a
            // failure.
            var now = CachedCount(folder);
            if (now > compiled)
            {
                compiled = now;
                withoutProgress = 0;
                continue;
            }

            if (++withoutProgress >= MaxAttemptsWithoutProgress) break;
        }

        Log.Logger.Warning(
            $"Shader precompile gave up after {compiled} shaders - starting anyway; the first launches may still fail while creating shaders");
    }

    /// <summary>The child: create every effect there is, so every shader object it holds is compiled and persisted.
    /// Exit code 0 means this process REACHED THE END of the list - which is the whole question, since the fault we are
    /// working around kills the process outright rather than throwing.</summary>
    private static void RunCompilePass(GraphicsDevice device)
    {
        var created = 0;
        var skipped = 0;

        // Effects belong to a RENDER device - it is the only kind that carries an effect pool (a resource-loading
        // device has none, and constructing an effect against one just throws). This is a throwaway process, so an
        // extra render device costs nothing.
        var target = device.MainDevice.CreateRenderDevice();

        var inFlightPath = InFlightFile(device);
        var stats = ShaderCompileStats.Load(device);
        var clock = new Stopwatch();
        var only = RequestedEffect;

        foreach (var type in EffectTypes())
        {
            var name = type.FullName ?? type.Name;

            if (only != null)
            {
                if (name != only) continue;
            }
            else if (stats.ShouldSkip(name))
            {
                skipped++;
                continue;
            }

            // Written BEFORE the constructor, deleted after it: if the process does not come back, the parent reads
            // this and knows which effect took it down - and, once the device starts reporting, which shader of it.
            _inFlightPath = inFlightPath;
            _inFlightEffect = name;
            TryWrite(inFlightPath, name);
            stats.RecordAttempt(name);
            // Saved before each build, not at the end: a pass that dies leaves the attempt counted anyway.
            stats.Save();

            try
            {
                // Generated effects all take an IGraphicsDevice and compile their shaders in the ctor.
                clock.Restart();
                (Activator.CreateInstance(type, target) as IDisposable)?.Dispose();
                created++;
                stats.RecordSuccess(name, (int)clock.ElapsedMilliseconds);
            }
            catch (Exception e)
            {
                // A managed exception is deterministic (a native fault kills the process instead), so count the effect
                // as uncompilable rather than retrying it on every launch.
                skipped++;
                // Reflection wraps whatever the ctor threw; the wrapper's message says nothing about the real fault.
                var cause = (e as TargetInvocationException)?.InnerException ?? e;
                stats.RecordSkipped(name, cause.GetType().Name);
                Log.Logger.Warning($"Shader precompile skipped {type.Name}: {cause.GetType().Name}: {cause.Message}");
            }

            TryDelete(inFlightPath);
        }

        stats.Save();

        // No stamp for a named run: one effect proves nothing about the rest.
        if (only != null) Environment.Exit(created > 0 ? 0 : 1);

        var stamped = false;
        if (created > 0)
        {
            try
            {
                var stamp = StampFile(device);
                Directory.CreateDirectory(Path.GetDirectoryName(stamp));
                File.WriteAllText(stamp, $"{created} effects precompiled ({skipped} skipped) at {DateTime.UtcNow:O}");
                stamped = true;
            }
            catch
            {
                // No stamp = not compiled, whatever went through: the parent must not be told otherwise.
            }
        }

        Environment.Exit(stamped ? 0 : 1);
    }

    /// <summary>Builds ONE effect in a child process while the application keeps running - the fault being recovered
    /// from kills whatever process attempts it, so the attempt cannot be made here. Once per effect per run.</summary>
    public static void TryCompileInBackground(GraphicsDevice device, string shaderName)
    {
        if (IsCompilePass || device == null || !Enabled || !ShaderBinaryCache.Enabled) return;

        var effect = ShaderCompileStats.ForDevice(device).EffectOfShader(shaderName);
        if (effect == null || !ShaderCompileStats.ForDevice(device).CanRetry(effect)) return;

        lock (BackgroundTried)
        {
            if (!BackgroundTried.Add(effect)) return;
        }

        Task.Run(() =>
        {
            // One at a time: each child holds a Vulkan device of its own, and several at once is pressure for nothing.
            lock (BackgroundGate)
            {
                Log.Logger.Information($"Shader precompile: trying {effect} again in the background");

                if (RunChild(effect))
                {
                    ShaderCompileStats.Refresh(device);
                    Log.Logger.Information($"Shader precompile: {effect} built - it will be used from the next draw");
                    return;
                }

                // Writing the death down is what stops this being tried forever.
                TakePoisoned(device);
                ShaderCompileStats.Refresh(device);
                Log.Logger.Warning($"Shader precompile: {effect} took the child down again");
            }
        });
    }

    private static bool RunChild(string onlyEffect = null)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            var info = new ProcessStartInfo { FileName = exe, UseShellExecute = false };
            // Launched through the SDK host (dotnet run / dotnet app.dll), the process path is dotnet itself - it needs
            // the assembly back before any argument of ours.
            if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                var entry = Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrEmpty(entry)) return false;
                info.ArgumentList.Add(entry);
            }
            info.ArgumentList.Add(PassArgument);
            if (!string.IsNullOrEmpty(onlyEffect)) info.ArgumentList.Add(onlyEffect);

            using var child = Process.Start(info);
            if (child == null) return false;

            if (!child.WaitForExit((int)AttemptTimeout.TotalMilliseconds))
            {
                child.Kill(entireProcessTree: true);
                return false;
            }

            return child.ExitCode == 0;
        }
        catch (Exception e)
        {
            Log.Logger.Warning($"Shader precompile could not start a child process: {e.Message}");
            return false;
        }
    }

    // Every generated effect, from every Adamantium assembly the application pulls in. Enumerated rather than listed:
    // effects are code-generated per .fx file, so a written-down list would go stale the moment one is added.
    private static IEnumerable<Type> EffectTypes()
    {
        LoadAdamantiumAssemblies();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name?.StartsWith("Adamantium", StringComparison.Ordinal) != true) continue;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }

            foreach (var type in types)
            {
                // Type only, NO namespace test: one for ".Effects.Generated" went stale when the generator moved its
                // output, and the pass then found no effects at all - silently, every launch.
                if (type.IsAbstract || type == typeof(Effect) || !typeof(Effect).IsAssignableFrom(type)) continue;
                yield return type;
            }
        }
    }

    // An assembly is only loaded once something touches it, and the point of precompiling is that nothing has yet.
    private static void LoadAdamantiumAssemblies()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<Assembly>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            queue.Enqueue(assembly);
        }

        while (queue.Count > 0)
        {
            foreach (var reference in queue.Dequeue().GetReferencedAssemblies())
            {
                if (reference.Name?.StartsWith("Adamantium", StringComparison.Ordinal) != true) continue;
                if (!seen.Add(reference.FullName)) continue;

                try
                {
                    queue.Enqueue(Assembly.Load(reference));
                }
                catch
                {
                    // A reference that cannot be loaded holds no effects we could compile either.
                }
            }
        }
    }

    private static string StampFile(GraphicsDevice device) => SideFile(device, "precompiled.stamp");

    // What the child is building right now. It survives the process that wrote it, which is the point: a fault leaves
    // no exception to catch, so this is the only thing that says what was being built.
    private static string InFlightFile(GraphicsDevice device) => SideFile(device, "compiling.txt");

    /// <summary>Called by the device just before a shader is created. Writes only inside the compile pass.</summary>
    internal static void NoteShaderInFlight(string shaderName)
    {
        if (_inFlightPath == null || _inFlightEffect == null) return;
        TryWrite(_inFlightPath, _inFlightEffect + Environment.NewLine + shaderName);
    }

    private static string SideFile(GraphicsDevice device, string name)
    {
        try
        {
            return Path.Combine(ShaderBinaryCache.DirectoryFor(device), name);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Writes whatever the dead child was building into the statistics as a death, and returns it. Null when
    /// the child died somewhere other than a constructor - there is nothing to blame then.</summary>
    private static string TakePoisoned(GraphicsDevice device)
    {
        var inFlight = InFlightFile(device);
        if (inFlight == null || !File.Exists(inFlight)) return null;

        try
        {
            // The effect, then the shader being created - a child that died before any shader leaves just the first.
            var lines = File.ReadAllLines(inFlight);
            File.Delete(inFlight);

            var name = lines.Length > 0 ? lines[0].Trim() : "";
            if (name.Length == 0) return null;

            var stats = ShaderCompileStats.Load(device);
            stats.RecordDeath(name, lines.Length > 1 ? lines[1].Trim() : null);
            stats.Save();
            return name;
        }
        catch
        {
            return null;
        }
    }

    private static void TryWrite(string path, string text)
    {
        try
        {
            if (path == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }
        catch
        {
            // Housekeeping. A pass that cannot leave notes still compiles shaders.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (path != null && File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private static int CachedCount(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.shaderbin").Length : 0;
        }
        catch
        {
            return 0;
        }
    }
}
