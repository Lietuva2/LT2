using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Lt2.Usage.Counting;

/// <summary>
/// Design 8.27: a HyperLogLog sketch of how many distinct visitors (or accounts) a period had, without keeping any of them.
/// 2^14 registers of one byte (16 KB), a standard error of about 0.8 %; below about 40 000 it counts by the empty registers
/// (linear counting), which is close to exact at the trial's numbers. Each register holds only the longest run of leading
/// zeros among the hashes that fell into it, so nobody can be read back out of a sketch; merging two sketches of one
/// period (same salt) is the register-wise maximum.
/// </summary>
internal sealed class Sketch
{
    public const int Precision = 14;
    public const int Size = 1 << Precision;

    private readonly byte[] _registers;

    public Sketch() => _registers = new byte[Size];

    private Sketch(byte[] registers) => _registers = registers;

    /// <summary>A stored sketch; anything that is not one is an empty sketch.</summary>
    public static Sketch From(byte[]? registers) =>
        registers is { Length: Size } ? new Sketch((byte[])registers.Clone()) : new Sketch();

    public byte[] ToBytes() => (byte[])_registers.Clone();

    public bool IsEmpty => !_registers.AsSpan().ContainsAnyExcept((byte)0);

    /// <summary>Adds one 64-bit hash: its first bits choose the register, the rest give the run of leading zeros.</summary>
    public void Add(ulong hash)
    {
        var index = (int)(hash >> (64 - Precision));
        // The sentinel bit caps the run at 64 - Precision + 1 when the remaining bits are all zero.
        var rest = (hash << Precision) | (1UL << (Precision - 1));
        var rank = (byte)(BitOperations.LeadingZeroCount(rest) + 1);
        if (rank > _registers[index])
        {
            _registers[index] = rank;
        }
    }

    public void Merge(Sketch other)
    {
        ArgumentNullException.ThrowIfNull(other);
        for (var i = 0; i < Size; i++)
        {
            if (other._registers[i] > _registers[i])
            {
                _registers[i] = other._registers[i];
            }
        }
    }

    /// <summary>How many distinct hashes were added, approximately.</summary>
    public int Estimate()
    {
        double sum = 0;
        var empty = 0;
        foreach (var register in _registers)
        {
            sum += Math.ScaleB(1.0, -register);
            if (register == 0)
            {
                empty++;
            }
        }

        const double m = Size;
        var alpha = 0.7213 / (1 + 1.079 / m);
        var raw = alpha * m * m / sum;
        var estimate = raw <= 2.5 * m && empty > 0 ? m * Math.Log(m / empty) : raw;
        return (int)Math.Round(estimate, MidpointRounding.AwayFromZero);
    }

    /// <summary>The keyed hash of one visitor or account in one period: HMAC-SHA256 with the period's salt, its first 64 bits.</summary>
    public static ulong Hash(byte[] salt, ReadOnlySpan<byte> data)
    {
        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(salt, data, mac);
        return BinaryPrimitives.ReadUInt64BigEndian(mac);
    }

    /// <summary>What a visitor is hashed from: the network (<see cref="UsageRules.Network"/>) and the browser's user agent.</summary>
    public static byte[] VisitorData(byte[] network, string userAgent)
    {
        var agent = Encoding.UTF8.GetBytes(userAgent);
        var data = new byte[2 + network.Length + agent.Length];
        data[0] = (byte)'v';
        data[1] = (byte)network.Length;
        network.CopyTo(data, 2);
        agent.CopyTo(data, 2 + network.Length);
        return data;
    }

    /// <summary>What an account is hashed from: its id.</summary>
    public static byte[] MemberData(Guid account)
    {
        var data = new byte[17];
        data[0] = (byte)'m';
        account.TryWriteBytes(data.AsSpan(1));
        return data;
    }

    /// <summary>A new random salt (32 bytes). Never derived from a date or a key: a salt that can be made again links people.</summary>
    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(32);
}
