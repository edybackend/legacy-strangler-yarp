using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Gateway.Migration;

/// <summary>Picks the backend for one request according to the route's mode.</summary>
public static class DestinationSelector
{
    public const string Legacy = "legacy";
    public const string Modern = "modern";

    /// <summary>
    /// Canary requests with an <c>X-Canary-Key</c> header (e.g. a customer id) always land on the same side, so one
    /// customer does not flip between old and new behaviour. Without it, each request is a fresh draw.
    /// </summary>
    public static string Choose(RouteMigration route, string? canaryKey, Func<int> randomPercent)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(randomPercent);
        return route.Mode switch
        {
            MigrationMode.Modern => Modern,
            MigrationMode.Canary => Bucket(canaryKey, randomPercent) < route.CanaryPercent ? Modern : Legacy,
            _ => Legacy, // Legacy and Shadow: the client always gets the legacy answer
        };
    }

    /// <summary>0–99. Stable for a given key.</summary>
    public static int Bucket(string? canaryKey, Func<int> randomPercent)
    {
        ArgumentNullException.ThrowIfNull(randomPercent);
        return string.IsNullOrEmpty(canaryKey)
            ? randomPercent()
            : (int)(BinaryPrimitives.ReadUInt32LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(canaryKey))) % 100);
    }
}
