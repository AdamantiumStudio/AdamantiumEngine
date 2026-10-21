using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Adamantium.Vulkan.Slang;
using Adamantium.Vulkan.Spirv.Reflection;

namespace Adamantium.Engine.Generators;

/// <summary>
/// Loads the native libraries the effect compiler needs into the generator (analyzer) process.
/// The libraries are shipped as embedded resources and extracted next to the (possibly shadow-copied)
/// generator assembly before loading — analyzers receive neither NuGet runtime/native assets nor a
/// search-path entry, and netstandard2.0 has no <c>NativeLibrary</c> API, so this is the robust route.
/// </summary>
public class NativeLibraryLoader
{
    private static readonly object LoadingLock = new();

    private static volatile bool IsLibraryLoaded;

    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    // Loaded in order; a shim must come after the runtime it imports
    // (slang-c-shared.dll imports slang.dll), so list dependencies first. On Linux libslang.so IS the compiler, which
    // LoadSlangRuntime has already loaded, and the shim finds it there by its soname.
    private static readonly string[] NativeLibraries = IsWindows
        ? new[] { "spirv-cross-c-shared.dll", "slang.dll", "slang-c-shared.dll" }
        : new[] { "libspirv-cross-c-shared.so", "libslang-c-shared.so" };

    /// <summary>The folder of the Slang package that holds this platform's libraries: runtimes/&lt;rid&gt;/native.</summary>
    private static string RuntimeIdentifier
    {
        get
        {
            var os = IsWindows ? "win" : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "linux";
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            return $"{os}-{arch}";
        }
    }

    /// <summary>Null when it worked, the reason when it did not: throwing would only be a warning to Roslyn, and the
    /// build would "succeed" with an empty assembly.</summary>
    public static string LoadNativeLibraries(string slangPackagePath = null)
    {
        static string ExtractLibrary(string @namespace, string dstPath, string dllName)
        {
            using Stream sourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{@namespace}.{dllName}");

            if (sourceStream == null)
            {
                throw new FileNotFoundException($"{dllName} is not embedded for {RuntimeIdentifier}.");
            }

            string finalPath = Path.Combine(dstPath, dllName);

            try
            {
                using Stream destinationStream = File.Open(finalPath, FileMode.OpenOrCreate, FileAccess.Write);

                sourceStream.CopyTo(destinationStream);

                sourceStream.Close();
                sourceStream.Dispose();
                destinationStream.Dispose();

            }
            catch (IOException)
            {
            }

            return finalPath;
        }

        if (IsLibraryLoaded)
        {
            return null;
        }

        lock (LoadingLock)
        {
            if (IsLibraryLoaded)
            {
                return null;
            }

            try
            {
                var directory = ExtractionDirectory();

                LoadSlangRuntime(slangPackagePath);

                foreach (var library in NativeLibraries)
                {
                    var path = ExtractLibrary("Adamantium.Engine.Generators", directory, library);
                    LoadedLibraries[ImportName(library)] = LoadLibraryByPath(path);
                }

                ResolveImportsToLoadedLibraries(typeof(SpirvReflection).Assembly, typeof(SlangCompiler).Assembly);
            }
            catch (Exception exception)
            {
                return exception.Message;
            }

            IsLibraryLoaded = true;
            return null;
        }
    }

    /// <summary>What was loaded, by the name the bindings import it under: spirv-cross-c-shared, slang-c-shared.</summary>
    private static readonly Dictionary<string, IntPtr> LoadedLibraries = new();

    private static string ImportName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return !IsWindows && name.StartsWith("lib", StringComparison.Ordinal) ? name.Substring(3) : name;
    }

    /// <summary>
    /// Points the bindings' imports at the libraries loaded here. Windows finds an already loaded module by its file
    /// name, so a later import by name gets it; Linux matches a loaded library only by its path or soname, and
    /// SPIRV-Cross's soname is libspirv-cross-c-shared.so.0, which the import's libspirv-cross-c-shared.so is not.
    /// NativeLibrary is not in netstandard2.0, so it is reached by reflection - where the compiler runs on .NET; on
    /// .NET Framework (Visual Studio's compiler) it is absent, and Windows needs none of it anyway.
    /// </summary>
    private static void ResolveImportsToLoadedLibraries(params Assembly[] bindings)
    {
        var nativeLibrary = Type.GetType("System.Runtime.InteropServices.NativeLibrary");
        var resolverType = Type.GetType("System.Runtime.InteropServices.DllImportResolver");
        var setResolver = nativeLibrary?.GetMethod("SetDllImportResolver", new[] { typeof(Assembly), resolverType });

        if (setResolver == null) return;

        var resolver = Delegate.CreateDelegate(resolverType,
            typeof(NativeLibraryLoader).GetMethod(nameof(ResolveImport), BindingFlags.NonPublic | BindingFlags.Static));

        foreach (var assembly in bindings)
        {
            try
            {
                setResolver.Invoke(null, new object[] { assembly, resolver });
            }
            catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
            {
                // A resolver is set already - by an earlier generator in the same load context.
            }
        }
    }

    /// <summary>Zero lets the runtime resolve the name itself.</summary>
    private static IntPtr ResolveImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) =>
        LoadedLibraries.TryGetValue(libraryName, out var handle) ? handle : IntPtr.Zero;

    /// <summary>Beside the generator assembly when it has a location; a compiler that loads analyzers from memory (as on
    /// Linux) gives none, and then a folder of this generator's version in the temp directory.</summary>
    private static string ExtractionDirectory()
    {
        var assembly = Assembly.GetExecutingAssembly();

        if (!string.IsNullOrEmpty(assembly.Location))
        {
            return Path.GetDirectoryName(assembly.Location);
        }

        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version.ToString();
        var directory = Path.Combine(Path.GetTempPath(), "Adamantium.Engine.Generators", version);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>slang.dll is a 75 KB front for slang-compiler.dll, which it finds by NAME - so on the PATH, which an
    /// IDE started before an SDK upgrade no longer has. Loaded here by full path instead; a later load by name then
    /// finds the module already in the process. The PACKAGE comes first, so a machine without the Vulkan SDK still
    /// builds shaders.</summary>
    private static void LoadSlangRuntime(string slangPackagePath)
    {
        foreach (var directory in RuntimeDirectories(slangPackagePath))
        {
            if (!Directory.Exists(directory)) continue;

            var loaded = false;

            foreach (var path in SlangRuntime(directory))
            {
                try
                {
                    LoadLibraryByPath(path);
                    loaded = true;
                }
                catch (Win32Exception)
                {
                    // Not every layout needs every piece; slang.dll says so itself if something it needs is missing.
                }
            }

            if (loaded) return;
        }
    }

    private static IEnumerable<string> RuntimeDirectories(string slangPackagePath)
    {
        if (!string.IsNullOrEmpty(slangPackagePath))
        {
            yield return Path.Combine(slangPackagePath, "runtimes", RuntimeIdentifier, "native");
            // Adamantium.Vulkan.Slang before 1.1 carried the Windows libraries flat in contentFiles.
            yield return Path.Combine(slangPackagePath, "contentFiles", "any", "netstandard2.0");
            yield return Path.Combine(slangPackagePath, "content");
        }

        var sdk = Environment.GetEnvironmentVariable("VULKAN_SDK");

        if (IsWindows && (string.IsNullOrEmpty(sdk) || !Directory.Exists(sdk)))
        {
            sdk = Environment.GetEnvironmentVariable("VULKAN_SDK", EnvironmentVariableTarget.Machine);
        }

        if (!string.IsNullOrEmpty(sdk) && Directory.Exists(sdk)) yield return Path.Combine(sdk, IsWindows ? "Bin" : "lib");
    }

    // Measured: only the compiler is needed for HLSL to SPIR-V. The others load when present and cost nothing when not.
    // On Linux their names carry the Slang version (libslang-compiler.so.0.2026.17, libslang-glslang-2026.17.so), so
    // they are found by pattern.
    private static IEnumerable<string> SlangRuntime(string directory)
    {
        if (IsWindows)
        {
            return new[] { "slang-rt.dll", "slang-glslang.dll", "slang-glsl-module.dll", "slang-compiler.dll" }
                .Select(name => Path.Combine(directory, name))
                .Where(File.Exists);
        }

        return new[] { "libslang-compiler.so*", "libslang-glslang*.so" }
            .SelectMany(pattern => Directory.GetFiles(directory, pattern));
    }

    private static IntPtr LoadLibraryByPath(string filename) =>
        IsWindows ? LoadWindowsLibrary(filename) : LoadUnixLibrary(filename);

    private static unsafe IntPtr LoadWindowsLibrary(string filename)
    {
        [DllImport("kernel32", ExactSpelling = true, SetLastError = true)]
        static extern void* LoadLibraryW(ushort* lpLibFileName);

        fixed (char* p = filename)
        {
            var handle = LoadLibraryW((ushort*)p);

            if (handle is null)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to load {Path.GetFileName(filename)}.");
            }

            return (IntPtr)handle;
        }
    }

    // A library loaded here satisfies a later one that needs it by soname - the shim needs the compiler that way - which
    // matters because the Slang package folder is on no search path in this process.
    private const int RTLD_NOW = 0x002;
    private const int RTLD_GLOBAL = 0x100;

    private static IntPtr LoadUnixLibrary(string filename)
    {
        [DllImport("libdl.so.2", EntryPoint = "dlopen")]
        static extern IntPtr DlOpen(string fileName, int flags);

        [DllImport("libdl.so.2", EntryPoint = "dlerror")]
        static extern IntPtr DlError();

        var handle = DlOpen(filename, RTLD_NOW | RTLD_GLOBAL);

        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception($"Failed to load {Path.GetFileName(filename)}: {Marshal.PtrToStringAnsi(DlError())}");
        }

        return handle;
    }
}
