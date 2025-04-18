using JetBrains.Annotations;
using NexusMods.Paths;

namespace NexusMods.Abstractions.GameLocators.Stores.Steam;

/// <summary>
/// Metadata for games found that implement <see cref="ISteamGame"/>.
/// </summary>
[PublicAPI]
public record SteamLocatorResultMetadata : IGameLocatorResultMetadata
{
    /// <summary>
    /// The specific ID of the found game.
    /// </summary>
    public required uint AppId { get; init; }

    /// <summary>
    /// Optional absolute path to the cloud saves directory for the game.
    /// </summary>
    public AbsolutePath? CloudSavesDirectory { get; init; }

    /// <summary>
    /// The `manifestIds` of the installed depots for a found game, according to Steam's associated `appmanifest` file
    /// </summary>
    public ulong[] ManifestIds { get; set; } = [];
    
    /// <inheritdoc />
    public IEnumerable<LocatorId> ToLocatorIds() => ManifestIds.Select(m => LocatorId.From(m.ToString()));

    /// <summary>
    /// Path to the proton prefix directory.
    /// </summary>
    /// <remarks>
    /// To get the WINE prefix directory, combine this path with `pfx`.
    /// </remarks>
    public AbsolutePath? ProtonPrefixDirectory { get; init; }

    /// <summary>
    /// Anonymous function that returns the current launch options of the game or null.
    /// </summary>
    public Func<string?>? GetLaunchOptions { get; init; }
}
