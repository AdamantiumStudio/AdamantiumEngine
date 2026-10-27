using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.EffectsCompiler;
using Serilog;

namespace Adamantium.Graphics.Core.EffectsFramework;

/// <summary>The live effects of a main device's devices. Recompiles an effect when its .fx or an included .fxh changes and
/// swaps it in at its device's next frame; a version that fails is reported and the running one stays. Only effects built
/// with AdamantiumShaderHotReload carry their source, so only they are recompiled.</summary>
public sealed class ShaderHotReload : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);
    private const int SaveAttempts = 20;

    private readonly object sync = new();
    private readonly List<Effect> effects = [];
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> changed = new(StringComparer.OrdinalIgnoreCase);
    // The hash of each file's content as last taken to compile. A save is told apart by what it says, not by when: two
    // saves within one tick of the file clock share a write time, and the second was lost.
    private readonly Dictionary<string, string> taken = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Effect Effect, EffectData Data)> pending = [];
    private readonly HashSet<string> compiling = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> compileAgain = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<TaskCompletionSource<ShaderCompileReport>>> awaiting = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer settle;
    private volatile ShaderReloadMode mode;
    private int pendingCount;
    private long reloadCount;

    /// <summary>How many times effects have taken a recompiled version, on any device.</summary>
    public long ReloadCount => Volatile.Read(ref reloadCount);

    public ShaderHotReload()
    {
        settle = new Timer(_ => CompileChanged(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Raised on the rendering thread of a device once recompiled versions are in place in its effects.</summary>
    public event Action<IGraphicsDevice, IReadOnlyList<Effect>> Reloaded;

    /// <summary>Raised on a background thread when a changed effect does not compile: its source and the compiler's messages.</summary>
    public event Action<string, string> Failed;

    /// <summary>When a saved change is compiled. Switching back to OnSave compiles what Manual gathered.</summary>
    public ShaderReloadMode Mode
    {
        get => mode;
        set
        {
            mode = value;
            if (value == ShaderReloadMode.OnSave)
            {
                settle.Change(Settle, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>Files changed on disk and not compiled yet; in Manual mode, everything saved since the last apply.</summary>
    public IReadOnlyList<string> ChangedFiles
    {
        get
        {
            lock (sync)
            {
                return changed.ToList();
            }
        }
    }

    /// <summary>Holds a new live effect, and follows its source when it carries one.</summary>
    public void Add(Effect effect)
    {
        lock (sync)
        {
            effects.Add(effect);
            var arguments = effect.Data.Description.Arguments;
            if (arguments?.FilePath == null)
            {
                return;
            }

            Watch(Path.GetDirectoryName(Path.GetFullPath(arguments.FilePath)));
            foreach (var directory in arguments.IncludeDirectoryList ?? [])
            {
                Watch(Path.GetFullPath(directory));
            }
        }
    }

    /// <summary>Lets go of a disposed effect.</summary>
    public void Remove(Effect effect)
    {
        lock (sync)
        {
            effects.Remove(effect);
        }
    }

    /// <summary>The live effects made on <paramref name="device"/>.</summary>
    public IReadOnlyList<Effect> GetEffects(IGraphicsDevice device)
    {
        lock (sync)
        {
            return effects.FindAll(effect => ReferenceEquals(effect.GraphicsDevice, device));
        }
    }

    /// <summary>Disposes every live effect left: the main device whose devices made them is going.</summary>
    public void DisposeEffects()
    {
        while (true)
        {
            Effect effect;
            lock (sync)
            {
                if (effects.Count == 0)
                {
                    return;
                }

                effect = effects[^1];
                effects.RemoveAt(effects.Count - 1);
            }

            // Takes itself off the list.
            effect.Dispose();
        }
    }

    /// <summary>Treats <paramref name="path"/> as changed on disk, as a watcher would; a save already compiled is skipped.</summary>
    public void NotifyChanged(string path)
    {
        var fullPath = Path.GetFullPath(path);

        // Read outside the lock, compared inside it; and what looks new is read again inside it, since the first read may
        // be older than a save taken to compile meanwhile - a write reports itself as soon as it empties the file. Unread
        // counts as new: a file the save still holds is looked at again once it settles.
        var content = ContentHash(fullPath);
        lock (sync)
        {
            if (taken.TryGetValue(fullPath, out var last) && last != null
                && (content == last || ContentHash(fullPath) == last))
            {
                return;
            }

            changed.Add(fullPath);
        }

        if (mode == ShaderReloadMode.OnSave)
        {
            settle.Change(Settle, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Compiles every effect touched by the gathered changes and by <paramref name="files"/>, in either mode. Pass the
    /// files just saved: the watcher may not have reported them yet. Compiled versions go in place from the next frame.
    /// </summary>
    /// <returns>One report per recompiled source.</returns>
    public async Task<IReadOnlyList<ShaderCompileReport>> ApplyAsync(IEnumerable<string> files = null)
    {
        var touched = SourcesTouchedBy(TakeChanged(files ?? []));
        var reports = new List<Task<ShaderCompileReport>>(touched.Count);
        foreach (var arguments in touched)
        {
            var report = new TaskCompletionSource<ShaderCompileReport>(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(arguments, report);
            reports.Add(report.Task);
        }

        return await Task.WhenAll(reports).ConfigureAwait(false);
    }

    /// <summary>
    /// Puts every recompiled version waiting for <paramref name="device"/> in place in its effects. Call it at the start
    /// of that device's frame, before anything records with them.
    /// </summary>
    public void ApplyTo(IGraphicsDevice device)
    {
        if (Volatile.Read(ref pendingCount) == 0)
        {
            return;
        }

        List<(Effect Effect, EffectData Data)> due = null;
        lock (sync)
        {
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var effect = pending[i].Effect;
                if (effect.IsDisposed)
                {
                    pending.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(effect.GraphicsDevice, device))
                {
                    (due ??= []).Add((effect, pending[i].Data));
                    pending.RemoveAt(i);
                }
            }

            pendingCount = pending.Count;
        }

        if (due == null)
        {
            return;
        }

        var reloaded = new List<Effect>(due.Count);
        foreach (var (effect, data) in due)
        {
            try
            {
                var refused = effect.Reload(data);
                if (refused == null)
                {
                    reloaded.Add(effect);
                }
                else
                {
                    Log.Logger.Warning("Shader hot reload: {Reason}", refused);
                }
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "Shader hot reload: {Effect} could not take its new version", effect.Name);
            }
        }

        if (reloaded.Count > 0)
        {
            Log.Logger.Information("Shader hot reload: {Count} effect(s) reloaded", reloaded.Count);
            Interlocked.Increment(ref reloadCount);
            Reloaded?.Invoke(device, reloaded);
        }
    }

    public void Dispose()
    {
        settle.Dispose();
        lock (sync)
        {
            foreach (var watcher in watchers.Values)
            {
                watcher.Dispose();
            }

            watchers.Clear();
        }
    }

    private void Watch(string directory)
    {
        if (watchers.ContainsKey(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };
        watcher.Changed += (_, e) => OnFileEvent(e.FullPath);
        watcher.Created += (_, e) => OnFileEvent(e.FullPath);
        watcher.Renamed += (_, e) => OnFileEvent(e.FullPath);
        watcher.EnableRaisingEvents = true;
        watchers[directory] = watcher;
    }

    private void OnFileEvent(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".fx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fxh", StringComparison.OrdinalIgnoreCase))
        {
            NotifyChanged(path);
        }
    }

    private List<string> TakeChanged(IEnumerable<string> alsoChanged)
    {
        lock (sync)
        {
            var files = changed.Union(alsoChanged.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase).ToList();
            changed.Clear();
            foreach (var file in files)
            {
                taken[file] = ContentHash(file);
            }

            return files;
        }
    }

    private static string ContentHash(string path)
    {
        try
        {
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void CompileChanged()
    {
        if (mode == ShaderReloadMode.Manual)
        {
            return;
        }

        var files = TakeChanged([]);
        if (files.Count == 0)
        {
            return;
        }

        var touched = SourcesTouchedBy(files);
        Log.Logger.Debug("Shader hot reload: {Files} changed, recompiling {Sources}", files,
            touched.Select(arguments => arguments.FilePath));
        foreach (var arguments in touched)
        {
            Enqueue(arguments, null);
        }
    }

    private void Enqueue(EffectData.CompilerArguments arguments, TaskCompletionSource<ShaderCompileReport> report)
    {
        var source = Path.GetFullPath(arguments.FilePath);
        lock (sync)
        {
            if (report != null)
            {
                if (!awaiting.TryGetValue(source, out var reports))
                {
                    awaiting[source] = reports = [];
                }

                reports.Add(report);
            }

            if (!compiling.Add(source))
            {
                compileAgain.Add(source);
                return;
            }
        }

        ThreadPool.QueueUserWorkItem(_ => CompileWhileChanged(source, arguments));
    }

    private void CompileWhileChanged(string source, EffectData.CompilerArguments arguments)
    {
        while (true)
        {
            List<TaskCompletionSource<ShaderCompileReport>> reports;
            lock (sync)
            {
                awaiting.Remove(source, out reports);
            }

            var report = Compile(source, arguments);
            foreach (var waiting in reports ?? [])
            {
                waiting.SetResult(report);
            }

            lock (sync)
            {
                if (!compileAgain.Remove(source))
                {
                    compiling.Remove(source);
                    return;
                }
            }
        }
    }

    private List<EffectData.CompilerArguments> SourcesTouchedBy(List<string> files)
    {
        var touched = new Dictionary<string, EffectData.CompilerArguments>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        lock (sync)
        {
            foreach (var effect in effects)
            {
                var arguments = effect.Data.Description.Arguments;
                if (arguments?.FilePath == null)
                {
                    continue;
                }

                var source = Path.GetFullPath(arguments.FilePath);
                if (touched.ContainsKey(source))
                {
                    continue;
                }

                if (names.Contains(source) || (arguments.Includes ?? []).Any(include => names.Contains(Path.GetFullPath(include))))
                {
                    touched[source] = arguments;
                }
            }
        }

        return touched.Values.ToList();
    }

    private ShaderCompileReport Compile(string source, EffectData.CompilerArguments arguments)
    {
        EffectCompilerResult result;
        var started = Stopwatch.GetTimestamp();
        try
        {
            result = CompileFromDisk(arguments);
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, "Shader hot reload: {Source} could not be compiled", arguments.FilePath);
            Failed?.Invoke(arguments.FilePath, exception.Message);
            return new ShaderCompileReport(source, false, exception.Message);
        }

        if (result.HasErrors)
        {
            var messages = string.Join(Environment.NewLine, result.Logger.Messages);
            Log.Logger.Error("Shader hot reload: {Source} does not compile ({Elapsed} ms){NewLine}{Messages}",
                arguments.FilePath, ElapsedMs(started), Environment.NewLine, messages);
            Failed?.Invoke(arguments.FilePath, messages);
            return new ShaderCompileReport(source, false, messages);
        }

        result.EffectData.Description.Arguments = new EffectData.CompilerArguments
        {
            FilePath = arguments.FilePath,
            Macros = arguments.Macros,
            IncludeDirectoryList = arguments.IncludeDirectoryList,
            Includes = result.Includes.ToList()
        };

        lock (sync)
        {
            pending.RemoveAll(waiting => string.Equals(Path.GetFullPath(waiting.Data.Description.Arguments.FilePath), source,
                StringComparison.OrdinalIgnoreCase));
            foreach (var effect in effects)
            {
                var path = effect.Data.Description.Arguments?.FilePath;
                if (path != null && string.Equals(Path.GetFullPath(path), source, StringComparison.OrdinalIgnoreCase))
                {
                    pending.Add((effect, result.EffectData));
                }
            }

            pendingCount = pending.Count;
        }

        Log.Logger.Information("Shader hot reload: {Source} compiled in {Elapsed} ms, taking effect from the next frame",
            arguments.FilePath, ElapsedMs(started));
        return new ShaderCompileReport(source, true, string.Empty);
    }

    private static EffectCompilerResult CompileFromDisk(EffectData.CompilerArguments arguments)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                List<string> directories = [Path.GetDirectoryName(Path.GetFullPath(arguments.FilePath)), .. arguments.IncludeDirectoryList ?? []];
                return EffectCompiler.Compile(File.ReadAllText(arguments.FilePath), arguments.FilePath,
                    HeadersIn(directories), arguments.Macros);
            }
            catch (IOException) when (attempt < SaveAttempts && File.Exists(arguments.FilePath))
            {
                Thread.Sleep(Settle);
            }
        }
    }

    private static long ElapsedMs(long started)
    {
        return (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private static ImmutableArray<ShaderFileInfo> HeadersIn(List<string> directories)
    {
        var headers = ImmutableArray.CreateBuilder<ShaderFileInfo>();
        foreach (var directory in directories.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.fxh"))
            {
                headers.Add(new ShaderFileInfo
                {
                    Content = File.ReadAllText(path),
                    Path = Path.GetFullPath(path),
                    FileName = Path.GetFileName(path)
                });
            }
        }

        return headers.ToImmutable();
    }
}
