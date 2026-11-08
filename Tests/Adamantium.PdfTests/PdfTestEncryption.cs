using System;
using System.Security.Cryptography;
using System.Text;

namespace Adamantium.PdfTests;

/// <summary>Encrypts test files with the standard security handler, written from ISO 32000-2, 7.6.4 apart from the
/// reader: revision 3 (RC4), 4 (AES-128) and 6 (AES-256).</summary>
internal sealed class PdfTestEncryption
{
    private static readonly byte[] Padding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    public PdfTestEncryption(int revision, string user, string owner, int permissions = -4)
    {
        Revision = revision;
        Permissions = permissions;
        Id = Enumerable(16, 0x11);
        if (revision == 6)
        {
            FileKey = Enumerable(32, 0x42);
            var userSalt = Enumerable(16, 0x21);
            var ownerSalt = Enumerable(16, 0x31);
            var userBytes = Encoding.UTF8.GetBytes(user);
            var ownerBytes = Encoding.UTF8.GetBytes(owner);
            User = Join(Hash6(userBytes, userSalt.AsSpan(0, 8), []), userSalt);
            UserKey = Wrap(Hash6(userBytes, userSalt.AsSpan(8, 8), []), FileKey);
            Owner = Join(Hash6(ownerBytes, ownerSalt.AsSpan(0, 8), User), ownerSalt);
            OwnerKey = Wrap(Hash6(ownerBytes, ownerSalt.AsSpan(8, 8), User), FileKey);
            return;
        }

        var keyLength = 16;
        var ownerKey = MD5.HashData(Pad(owner));
        for (var i = 0; i < 50; i++)
        {
            ownerKey = MD5.HashData(ownerKey.AsSpan(0, keyLength));
        }

        var o = Rc4(ownerKey.AsSpan(0, keyLength), Pad(user));
        for (var i = 1; i <= 19; i++)
        {
            o = Rc4(Xor(ownerKey.AsSpan(0, keyLength), i), o);
        }

        Owner = o;
        var keyInput = Join(Pad(user), Owner, BitConverter.GetBytes(permissions), Id);
        var key = MD5.HashData(keyInput);
        for (var i = 0; i < 50; i++)
        {
            key = MD5.HashData(key.AsSpan(0, keyLength));
        }

        FileKey = key.AsSpan(0, keyLength).ToArray();
        var u = Rc4(FileKey, MD5.HashData(Join(Padding, Id)));
        for (var i = 1; i <= 19; i++)
        {
            u = Rc4(Xor(FileKey, i), u);
        }

        User = Join(u, new byte[16]);
    }

    public int Revision { get; }

    public int Permissions { get; }

    public byte[] Id { get; }

    public byte[] FileKey { get; }

    public byte[] Owner { get; }

    public byte[] User { get; }

    public byte[] OwnerKey { get; }

    public byte[] UserKey { get; }

    public string Dictionary => Revision switch
    {
        3 => "<< /Filter /Standard /V 2 /R 3 /Length 128 /P " + Permissions + " /O <" + Hex(Owner) + "> /U <" +
             Hex(User) + "> >>",
        4 => "<< /Filter /Standard /V 4 /R 4 /Length 128 /P " + Permissions +
             " /CF << /StdCF << /CFM /AESV2 /Length 16 >> >> /StmF /StdCF /StrF /StdCF /O <" + Hex(Owner) +
             "> /U <" + Hex(User) + "> >>",
        _ => "<< /Filter /Standard /V 5 /R 6 /Length 256 /P " + Permissions +
             " /CF << /StdCF << /CFM /AESV3 /Length 32 >> >> /StmF /StdCF /StrF /StdCF /O <" + Hex(Owner) +
             "> /U <" + Hex(User) + "> /OE <" + Hex(OwnerKey) + "> /UE <" + Hex(UserKey) + "> /Perms <" +
             Hex(new byte[16]) + "> >>",
    };

    public string IdArray => "[<" + Hex(Id) + "> <" + Hex(Id) + ">]";

    public byte[] Encrypt(int number, byte[] data)
    {
        if (Revision == 3)
        {
            return Rc4(ObjectKey(number, false), data);
        }

        var key = Revision == 4 ? ObjectKey(number, true) : FileKey;
        using var aes = Aes.Create();
        aes.Key = key;
        var iv = Enumerable(16, (byte)(number + 3));
        return Join(iv, aes.EncryptCbc(data, iv, PaddingMode.PKCS7));
    }

    public string EncryptedString(int number, string text) => "<" + Hex(Encrypt(number, Encoding.Latin1.GetBytes(text))) + ">";

    public static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    private byte[] ObjectKey(int number, bool aes)
    {
        var input = Join(FileKey, [(byte)number, (byte)(number >> 8), (byte)(number >> 16), 0, 0],
            aes ? "sAlT"u8.ToArray() : []);
        return MD5.HashData(input).AsSpan(0, Math.Min(16, FileKey.Length + 5)).ToArray();
    }

    private static byte[] Hash6(byte[] password, ReadOnlySpan<byte> salt, byte[] userData)
    {
        var k = SHA256.HashData(Join(password, salt.ToArray(), userData));
        var round = 0;
        while (true)
        {
            var k1 = Join(password, k, userData);
            var repeated = new byte[k1.Length * 64];
            for (var i = 0; i < 64; i++)
            {
                k1.CopyTo(repeated, i * k1.Length);
            }

            using var aes = Aes.Create();
            aes.Key = k.AsSpan(0, 16).ToArray();
            var e = aes.EncryptCbc(repeated, k.AsSpan(16, 16), PaddingMode.None);
            var remainder = 0;
            foreach (var value in e.AsSpan(0, 16))
            {
                remainder = (remainder * 256 + value) % 3;
            }

            k = remainder switch
            {
                0 => SHA256.HashData(e),
                1 => SHA384.HashData(e),
                _ => SHA512.HashData(e),
            };
            round++;
            if (round >= 64 && e[^1] <= round - 32)
            {
                break;
            }
        }

        return k.AsSpan(0, 32).ToArray();
    }

    private static byte[] Wrap(byte[] key, byte[] fileKey)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(fileKey, new byte[16], PaddingMode.None);
    }

    private static byte[] Pad(string password)
    {
        var bytes = Encoding.Latin1.GetBytes(password);
        var padded = new byte[32];
        var count = Math.Min(32, bytes.Length);
        bytes.AsSpan(0, count).CopyTo(padded);
        Padding.AsSpan(0, 32 - count).CopyTo(padded.AsSpan(count));
        return padded;
    }

    private static byte[] Xor(ReadOnlySpan<byte> key, int value)
    {
        var result = key.ToArray();
        for (var i = 0; i < result.Length; i++)
        {
            result[i] ^= (byte)value;
        }

        return result;
    }

    internal static byte[] Rc4(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        var s = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            s[i] = (byte)i;
        }

        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) % 256;
            (s[i], s[j]) = (s[j], s[i]);
        }

        var output = new byte[data.Length];
        for (int n = 0, i = 0, j = 0; n < data.Length; n++)
        {
            i = (i + 1) % 256;
            j = (j + s[i]) % 256;
            (s[i], s[j]) = (s[j], s[i]);
            output[n] = (byte)(data[n] ^ s[(s[i] + s[j]) % 256]);
        }

        return output;
    }

    private static byte[] Enumerable(int count, byte start)
    {
        var bytes = new byte[count];
        for (var i = 0; i < count; i++)
        {
            bytes[i] = (byte)(start + i);
        }

        return bytes;
    }

    private static byte[] Join(params byte[][] parts) => PdfFileBuilder.Concat(parts);
}
