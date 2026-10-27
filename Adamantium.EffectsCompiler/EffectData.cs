using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using MessagePack;

namespace Adamantium.EffectsCompiler
{
    [MessagePackObject]
    public sealed partial class EffectData
    {
        public static readonly string CompiledExtension = "fx.compiled";

        public EffectData() { }

        /// <summary>
        /// List of compiled shaders.
        /// </summary>
        [Key(0)]
        public List<Shader> Shaders;

        /// <summary>
        /// Complete Effect description
        /// </summary>
        [Key(1)]
        public Effect Description;

        // "AEFX" plus a version. A compiled effect travels as a base64 string baked into generated source, so a
        // stale one meets a newer loader as a wall of characters: without a mark of its own it fails as garbage.
        private static readonly byte[] Magic = [(byte)'A', (byte)'E', (byte)'F', (byte)'X'];

        private const int Version = 4;

        /// <summary>
        /// Saves this <see cref="EffectData"/> instance to the specified stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <remarks>LZ4, then deflate: deflate's entropy coding shrinks what LZ4 leaves (largest effect: 1786 KB to 179 KB).</remarks>
        public void Save(Stream stream)
        {
            stream.Write(Magic, 0, Magic.Length);
            var version = BitConverter.GetBytes(Version);
            stream.Write(version, 0, version.Length);

            using var deflate = new DeflateStream(stream, CompressionLevel.Optimal, leaveOpen: true);
            MessagePackSerializer.Serialize(deflate, this, Packed);
        }

        private static readonly MessagePackSerializerOptions Packed = MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.Lz4BlockArray);

        /// <summary>
        /// Saves this <see cref="EffectData"/> instance to the specified file.
        /// </summary>
        /// <param name="fileName">The output filename.</param>
        public void Save(string fileName)
        {
            using var stream = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.Write);
            Save(stream);
        }

        /// <summary>
        /// Loads an <see cref="EffectData"/> from the specified stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <returns>An <see cref="EffectData"/>. Throws if the stream is not one.</returns>
        public static EffectData Load(Stream stream)
        {
            var header = new byte[Magic.Length + sizeof(int)];
            if (ReadExactly(stream, header) != header.Length)
            {
                throw new InvalidDataException("This is too short to be a compiled effect.");
            }

            for (var i = 0; i < Magic.Length; i++)
            {
                if (header[i] != Magic[i])
                {
                    throw new InvalidDataException(
                       "This is not a compiled effect, or it was built by an engine from before the format had a mark.");
                }
            }

            var version = BitConverter.ToInt32(header, Magic.Length);
            if (version != Version)
            {
                throw new InvalidDataException(
                   $"This compiled effect is version {version}, and this engine reads version {Version}. Rebuild the effects.");
            }

            using var inflate = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true);
            return MessagePackSerializer.Deserialize<EffectData>(inflate, Packed);
        }

        //Streams are allowed to hand back less than asked for, and a header read short is a header misread
        private static int ReadExactly(Stream stream, byte[] buffer)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var step = stream.Read(buffer, read, buffer.Length - read);
                if (step == 0) break;
                read += step;
            }
            return read;
        }

        /// <summary>
        /// Loads an <see cref="EffectData"/> from the specified buffer.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <returns>An <see cref="EffectData"/> </returns>
        public static EffectData Load(byte[] buffer)
        {
            return Load(new MemoryStream(buffer));
        }

        /// <summary>
        /// Loads an <see cref="EffectData"/> from the specified file.
        /// </summary>
        /// <param name="fileName">The filename.</param>
        /// <returns>An <see cref="EffectData"/> </returns>
        public static EffectData Load(string fileName)
        {
            using (var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read))
                return Load(stream);
        }

    }
}
