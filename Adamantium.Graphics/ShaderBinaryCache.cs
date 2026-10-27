using System;
using System.IO;
using System.Security.Cryptography;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

/// <summary>On-disk cache of driver-compiled shader-object binaries, keyed by GPU, driver version and descriptor model, so
/// later launches create shaders from the binary instead of SPIR-V. Any IO failure falls back to SPIR-V.</summary>
public static class ShaderBinaryCache
{
    /// <summary>Master switch (off = always compile from SPIR-V, the pre-cache behavior).</summary>
    public static bool Enabled = true;

    private const int Format = 2;

    private static string DeviceDir(GraphicsDevice device)
    {
        var props = device.MainDevice.GraphicsAdapter.AdapterProperties;
        var key = $"{props.DeviceName}|{props.VendorID:X}|{props.DeviceID:X}|{props.DriverVersion:X}|" +
                  $"{device.MainDevice.DescriptorModel}|{Format}";
        using var sha = SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Substring(0, 16);
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Adamantium", "ShaderCache", hash);
    }

    /// <summary>The per-device cache folder. Public so <see cref="ShaderPrecompiler"/> can stamp it as complete: the
    /// stamp then shares this folder's key (GPU, driver version, descriptor model) and so expires by itself when one
    /// changes.</summary>
    public static string DirectoryFor(GraphicsDevice device) => DeviceDir(device);

    private static string FileFor(GraphicsDevice device, ShaderCreateInfoEXT info, string name, ulong layout)
    {
        using var sha = SHA256.Create();
        var buf = new MemoryStream();
        if (!info.PCode.IsEmpty) buf.Write(info.PCode.Span);
        buf.Write(BitConverter.GetBytes((uint)info.Stage));
        buf.Write(BitConverter.GetBytes((uint)info.NextStage));
        buf.Write(BitConverter.GetBytes((uint)info.Flags));
        buf.Write(BitConverter.GetBytes(layout));
        if (!string.IsNullOrEmpty(info.PName)) buf.Write(System.Text.Encoding.UTF8.GetBytes(info.PName));
        var hash = Convert.ToHexString(sha.ComputeHash(buf.ToArray())).Substring(0, 16);
        var readable = Sanitize(name) ?? info.Stage.ToString();
        return Path.Combine(DeviceDir(device), $"{readable}_{hash}.shaderbin");
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsLetterOrDigit(chars[i]) || chars[i] is '.' or '-' or '_') continue;
            chars[i] = '-';
        }
        return new string(chars);
    }

    /// <summary>Try to read a cached driver binary for this shader. False = miss (compile from SPIR-V and then Save).
    /// <paramref name="layout"/> stands for how the shader reaches its resources (set layouts, heap mappings): a binary is
    /// only good for the layout it was made with.</summary>
    public static bool TryLoad(GraphicsDevice device, ShaderCreateInfoEXT info, string name, ulong layout, out byte[] binary)
    {
        binary = null;
        if (!Enabled) return false;
        try
        {
            var file = FileFor(device, info, name, layout);
            if (!File.Exists(file)) return false;
            binary = File.ReadAllBytes(file);
            return binary.Length > 0;
        }
        catch { return false; }
    }

    /// <summary>Persist the driver-compiled binary of a freshly created shader object, so later launches skip compiling
    /// from SPIR-V.</summary>
    public static void Save(GraphicsDevice device, ShaderCreateInfoEXT info, string name, ulong layout, ShaderEXT shader)
    {
        if (!Enabled) return;
        try
        {
            nuint size = 0;
            if (device.LogicalDevice.GetShaderBinaryDataEXT(shader, ref size, default) != Result.Success || size == 0) return;
            var bytes = new byte[(int)size];
            if (device.LogicalDevice.GetShaderBinaryDataEXT(shader, ref size, bytes) != Result.Success) return;

            var file = FileFor(device, info, name, layout);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var tmp = file + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, file, overwrite: true);   // atomic-ish: a partial write never leaves a half file as the cache
        }
        catch { /* best effort - a failed cache write just means we recompile next time */ }
    }

    /// <summary>A copy of <paramref name="info"/> that creates from a driver BINARY instead of SPIR-V (cache-hit path).</summary>
    public static ShaderCreateInfoEXT AsBinary(ShaderCreateInfoEXT info, byte[] binary) => new()
    {
        Flags = info.Flags,
        Stage = info.Stage,
        NextStage = info.NextStage,
        CodeType = ShaderCodeTypeEXT.BinaryExt,
        CodeSize = (nuint)binary.Length,
        PCode = binary,
        PName = info.PName,
        SetLayoutCount = info.SetLayoutCount,
        PSetLayouts = info.PSetLayouts,
        PushConstantRangeCount = info.PushConstantRangeCount,
        PushConstantRanges = info.PushConstantRanges,
        PSpecializationInfo = info.PSpecializationInfo
    };
}
