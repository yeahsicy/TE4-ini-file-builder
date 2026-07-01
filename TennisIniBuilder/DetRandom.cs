namespace TennisIniBuilder;

/// <summary>
/// Deterministic, reproducible per-player PRNG (mulberry32 seeded by an FNV-1a hash
/// of the player id). Mirrors the Python helpers used by the reference pipeline
/// (randint/random/uniform/randrange). Values are NOT identical to Python's
/// Mersenne Twister — only the derivation *rules* are — but they are stable across
/// runs, so the same roster always produces the same file.
/// </summary>
public sealed class DetRandom
{
    private uint _s;

    public DetRandom(string seed)
    {
        _s = Fnv1a(seed);
        if (_s == 0) _s = 0x9E3779B9;
    }

    private static uint Fnv1a(string t)
    {
        uint h = 2166136261;
        foreach (char c in t) { h ^= c; h *= 16777619; }
        return h;
    }

    private uint NextU()
    {
        _s += 0x6D2B79F5;
        uint z = _s;
        z = (z ^ (z >> 15)) * (z | 1u);
        z ^= z + (z ^ (z >> 7)) * (z | 61u);
        return z ^ (z >> 14);
    }

    /// <summary>[0,1) with 24 bits of precision.</summary>
    public double NextDouble() => (NextU() >> 8) * (1.0 / 16777216.0);

    /// <summary>Inclusive on both ends, like Python's random.randint(a, b).</summary>
    public int RandInt(int a, int b) => a + (int)(NextDouble() * (b - a + 1));

    /// <summary>0..n-1, like Python's random.randrange(n).</summary>
    public int RandRange(int n) => (int)(NextDouble() * n);

    /// <summary>Uniform double in [a, b), like Python's random.uniform.</summary>
    public double Uniform(double a, double b) => a + NextDouble() * (b - a);
}
