using System;
using System.Security.Cryptography;
using System.Text;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Security;

/// <summary>The standard security handler (ISO 32000-2, 7.6.4): checks a password and decrypts strings and streams.
/// Revisions 2 to 4 (RC4, AES-128) and 5 to 6 (AES-256).</summary>
public sealed class PdfStandardSecurityHandler
{
    private static readonly byte[] Padding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    private static readonly byte[] AesSalt = [0x73, 0x41, 0x6C, 0x54];

    private readonly PdfDictionary _encrypt;
    private readonly byte[] _owner;
    private readonly byte[] _user;
    private readonly byte[] _ownerKey;
    private readonly byte[] _userKey;
    private readonly byte[] _firstId;
    private readonly int _permissions;
    private readonly int _keyLength;
    private byte[] _fileKey;

    public PdfStandardSecurityHandler(PdfDictionary encrypt, byte[] firstId)
    {
        _encrypt = encrypt;
        _firstId = firstId ?? [];
        if (encrypt["Filter"] is PdfName { Value: not "Standard" } filter)
        {
            throw new PdfException("The security handler /" + filter.Value + " is not supported.");
        }

        Version = Integer(encrypt["V"], 0);
        Revision = Integer(encrypt["R"], 2);
        _owner = Bytes(encrypt["O"]);
        _user = Bytes(encrypt["U"]);
        _ownerKey = Bytes(encrypt["OE"]);
        _userKey = Bytes(encrypt["UE"]);
        _permissions = Integer(encrypt["P"], -1);
        EncryptsMetadata = encrypt["EncryptMetadata"] is not PdfBoolean { Value: false };
        StringMethod = MethodOf(encrypt["StrF"]);
        StreamMethod = MethodOf(encrypt["StmF"]);
        var aes128 = StringMethod == PdfCryptMethod.Aes128 || StreamMethod == PdfCryptMethod.Aes128;
        _keyLength = Revision >= 5 ? 32
            : Revision == 2 ? 5
            : aes128 ? 16
            : KeyBytes(Integer(encrypt["Length"], Version >= 4 ? 128 : 40));
        if (_owner.Length < 32 || _user.Length < 32 || Revision < 2 || Revision > 6)
        {
            throw new PdfException("The encryption dictionary is malformed.");
        }
    }

    public int Version { get; }

    public int Revision { get; }

    public bool EncryptsMetadata { get; }

    public PdfCryptMethod StringMethod { get; }

    public PdfCryptMethod StreamMethod { get; }

    /// <summary>Whether a password has opened the file.</summary>
    public bool IsAuthenticated => _fileKey != null;

    /// <summary>Whether the owner password opened it.</summary>
    public bool IsOwner { get; private set; }

    public PdfPermissions Permissions =>
        IsOwner ? PdfPermissions.All : (PdfPermissions)_permissions & PdfPermissions.All;

    /// <summary>Tries <paramref name="password"/> as the owner password, then as the user password. The empty string
    /// opens files that only restrict permissions. A password that fails leaves an earlier one's access as it was.
    /// </summary>
    public bool Authenticate(string password)
    {
        password ??= string.Empty;
        var fileKey = _fileKey;
        var isOwner = IsOwner;
        if (Revision >= 5 ? AuthenticateAes256(password) : AuthenticateRc4(password))
        {
            return true;
        }

        _fileKey = fileKey;
        IsOwner = isOwner;
        return false;
    }

    /// <summary>The crypt method of the named crypt filter, as a stream's <c>/Crypt</c> filter asks for it.</summary>
    public PdfCryptMethod MethodOf(string cryptFilter) => MethodOf(new PdfName(cryptFilter));

    public byte[] Decrypt(PdfObjectId id, ReadOnlySpan<byte> data, PdfCryptMethod method)
    {
        if (_fileKey == null)
        {
            throw new PdfException("The file is encrypted and no password has opened it.");
        }

        return method switch
        {
            PdfCryptMethod.Rc4 => PdfRc4.Transform(ObjectKey(id, false), data),
            PdfCryptMethod.Aes128 => DecryptAes(ObjectKey(id, true), data),
            PdfCryptMethod.Aes256 => DecryptAes(_fileKey, data),
            _ => data.ToArray(),
        };
    }

    private PdfCryptMethod MethodOf(PdfObject filterName)
    {
        if (Version < 4)
        {
            return PdfCryptMethod.Rc4;
        }

        var name = (filterName as PdfName)?.Value ?? "Identity";
        if (name == "Identity")
        {
            return PdfCryptMethod.None;
        }

        var filters = _encrypt["CF"] as PdfDictionary;
        var filter = filters?[name] as PdfDictionary;
        return (filter?["CFM"] as PdfName)?.Value switch
        {
            "V2" => PdfCryptMethod.Rc4,
            "AESV2" => PdfCryptMethod.Aes128,
            "AESV3" => PdfCryptMethod.Aes256,
            _ => PdfCryptMethod.None,
        };
    }

    private bool AuthenticateRc4(string password)
    {
        var bytes = PadPassword(password);
        if (TryOwnerPassword(bytes))
        {
            return true;
        }

        IsOwner = false;
        return TryUserPassword(bytes);
    }

    private bool TryOwnerPassword(byte[] padded)
    {
        var hash = MD5.HashData(padded);
        if (Revision >= 3)
        {
            for (var i = 0; i < 50; i++)
            {
                hash = MD5.HashData(hash.AsSpan(0, _keyLength));
            }
        }

        var key = hash.AsSpan(0, _keyLength).ToArray();
        var userPassword = _owner.AsSpan(0, 32).ToArray();
        if (Revision == 2)
        {
            userPassword = PdfRc4.Transform(key, userPassword);
        }
        else
        {
            var round = new byte[key.Length];
            for (var i = 19; i >= 0; i--)
            {
                for (var k = 0; k < key.Length; k++)
                {
                    round[k] = (byte)(key[k] ^ i);
                }

                userPassword = PdfRc4.Transform(round, userPassword);
            }
        }

        if (!TryUserPassword(userPassword))
        {
            return false;
        }

        IsOwner = true;
        return true;
    }

    private bool TryUserPassword(byte[] padded)
    {
        var key = ComputeRc4FileKey(padded);
        byte[] check;
        if (Revision == 2)
        {
            check = PdfRc4.Transform(key, Padding);
            if (!check.AsSpan().SequenceEqual(_user.AsSpan(0, 32)))
            {
                return false;
            }
        }
        else
        {
            var input = new byte[32 + _firstId.Length];
            Padding.CopyTo(input, 0);
            _firstId.CopyTo(input, 32);
            check = MD5.HashData(input);
            var round = new byte[key.Length];
            for (var i = 0; i < 20; i++)
            {
                for (var k = 0; k < key.Length; k++)
                {
                    round[k] = (byte)(key[k] ^ i);
                }

                check = PdfRc4.Transform(round, check);
            }

            if (!check.AsSpan(0, 16).SequenceEqual(_user.AsSpan(0, 16)))
            {
                return false;
            }
        }

        _fileKey = key;
        return true;
    }

    private byte[] ComputeRc4FileKey(byte[] padded)
    {
        var metadata = Revision >= 4 && !EncryptsMetadata;
        var input = new byte[32 + 32 + 4 + _firstId.Length + (metadata ? 4 : 0)];
        padded.CopyTo(input, 0);
        _owner.AsSpan(0, 32).CopyTo(input.AsSpan(32));
        input[64] = (byte)_permissions;
        input[65] = (byte)(_permissions >> 8);
        input[66] = (byte)(_permissions >> 16);
        input[67] = (byte)(_permissions >> 24);
        _firstId.CopyTo(input, 68);
        if (metadata)
        {
            input.AsSpan(68 + _firstId.Length, 4).Fill(0xFF);
        }

        var hash = MD5.HashData(input);
        if (Revision >= 3)
        {
            for (var i = 0; i < 50; i++)
            {
                hash = MD5.HashData(hash.AsSpan(0, _keyLength));
            }
        }

        return hash.AsSpan(0, _keyLength).ToArray();
    }

    private static byte[] PadPassword(string password)
    {
        var bytes = Encoding.Latin1.GetBytes(password);
        var padded = new byte[32];
        var count = Math.Min(32, bytes.Length);
        bytes.AsSpan(0, count).CopyTo(padded);
        Padding.AsSpan(0, 32 - count).CopyTo(padded.AsSpan(count));
        return padded;
    }

    private bool AuthenticateAes256(string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormKC));
        if (bytes.Length > 127)
        {
            bytes = bytes.AsSpan(0, 127).ToArray();
        }

        if (_owner.Length >= 48 && _user.Length >= 48)
        {
            var user = _user.AsSpan(0, 48).ToArray();
            if (Hash(bytes, _owner.AsSpan(32, 8), user).AsSpan().SequenceEqual(_owner.AsSpan(0, 32)))
            {
                _fileKey = UnwrapKey(Hash(bytes, _owner.AsSpan(40, 8), user), _ownerKey);
                IsOwner = _fileKey != null;
                return IsOwner;
            }

            if (Hash(bytes, _user.AsSpan(32, 8), []).AsSpan().SequenceEqual(_user.AsSpan(0, 32)))
            {
                _fileKey = UnwrapKey(Hash(bytes, _user.AsSpan(40, 8), []), _userKey);
                IsOwner = false;
                return _fileKey != null;
            }
        }

        return false;
    }

    private static byte[] UnwrapKey(byte[] key, byte[] wrapped)
    {
        if (wrapped.Length < 32)
        {
            return null;
        }

        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptCbc(wrapped.AsSpan(0, 32), new byte[16], PaddingMode.None);
    }

    /// <summary>Algorithm 2.A's hash for revision 5, algorithm 2.B's for revision 6.</summary>
    private byte[] Hash(byte[] password, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> userData)
    {
        var input = new byte[password.Length + salt.Length + userData.Length];
        password.CopyTo(input, 0);
        salt.CopyTo(input.AsSpan(password.Length));
        userData.CopyTo(input.AsSpan(password.Length + salt.Length));
        var hash = SHA256.HashData(input);
        if (Revision == 5)
        {
            return hash;
        }

        using var aes = Aes.Create();
        for (var round = 0; ; round++)
        {
            var chunk = new byte[password.Length + hash.Length + userData.Length];
            password.CopyTo(chunk, 0);
            hash.CopyTo(chunk, password.Length);
            userData.CopyTo(chunk.AsSpan(password.Length + hash.Length));
            var repeated = new byte[chunk.Length * 64];
            for (var i = 0; i < 64; i++)
            {
                chunk.CopyTo(repeated, i * chunk.Length);
            }

            aes.Key = hash.AsSpan(0, 16).ToArray();
            var encrypted = aes.EncryptCbc(repeated, hash.AsSpan(16, 16), PaddingMode.None);
            var sum = 0;
            for (var i = 0; i < 16; i++)
            {
                sum += encrypted[i];
            }

            hash = (sum % 3) switch
            {
                0 => SHA256.HashData(encrypted),
                1 => SHA384.HashData(encrypted),
                _ => SHA512.HashData(encrypted),
            };
            if (round >= 63 && encrypted[^1] <= round - 31)
            {
                break;
            }
        }

        return hash.AsSpan(0, 32).ToArray();
    }

    private byte[] ObjectKey(PdfObjectId id, bool aes)
    {
        var input = new byte[_fileKey.Length + 5 + (aes ? 4 : 0)];
        _fileKey.CopyTo(input, 0);
        input[_fileKey.Length] = (byte)id.Number;
        input[_fileKey.Length + 1] = (byte)(id.Number >> 8);
        input[_fileKey.Length + 2] = (byte)(id.Number >> 16);
        input[_fileKey.Length + 3] = (byte)id.Generation;
        input[_fileKey.Length + 4] = (byte)(id.Generation >> 8);
        if (aes)
        {
            AesSalt.CopyTo(input, _fileKey.Length + 5);
        }

        return MD5.HashData(input).AsSpan(0, Math.Min(_fileKey.Length + 5, 16)).ToArray();
    }

    private static byte[] DecryptAes(byte[] key, ReadOnlySpan<byte> data)
    {
        if (data.Length < 32)
        {
            return [];
        }

        using var aes = Aes.Create();
        aes.Key = key;
        var iv = data[..16];
        var cipher = data[16..];
        cipher = cipher[..(cipher.Length - cipher.Length % 16)];
        try
        {
            return aes.DecryptCbc(cipher, iv, PaddingMode.PKCS7);
        }
        catch (CryptographicException)
        {
            return aes.DecryptCbc(cipher, iv, PaddingMode.None);
        }
    }

    private static int KeyBytes(int length) => length is >= 5 and <= 16 ? length : Math.Clamp(length, 40, 128) / 8;

    private static int Integer(PdfObject value, int fallback) => value switch
    {
        PdfInteger integer => (int)integer.Value,
        PdfReal real => (int)real.Value,
        _ => fallback,
    };

    private static byte[] Bytes(PdfObject value) => (value as PdfString)?.Bytes ?? [];
}
