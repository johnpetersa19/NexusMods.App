using System.Text.Json.Serialization;
using JetBrains.Annotations;
using NexusMods.Abstractions.NexusWebApi.Types.V2;
using NexusMods.Abstractions.Settings;
using NexusMods.Sdk;

namespace NexusMods.App.UI.Settings;

/// <summary>
/// Settings that give access to experimental features in the UI.
/// </summary>
public record ExperimentalSettings : ISettings
{
    /// <summary>
    /// Enables games that are not enabled by default.
    /// </summary>
    public bool EnableAllGames { get; [UsedImplicitly] set; } = ApplicationConstants.IsDebug;

    // TODO: remove for GA
    public bool EnableCollectionSharing { get; [UsedImplicitly] set; }

    [JsonIgnore]
    public readonly GameId[] SupportedGames =
    [
        GameId.From(1303), // Stardew Valley 
        GameId.From(3333) //  Cyberpunk
    ];

    public static ISettingsBuilder Configure(ISettingsBuilder settingsBuilder)
    {
        return settingsBuilder
            .ConfigureStorageBackend<ExperimentalSettings>(builder => builder.UseJson())
            .AddToUI<ExperimentalSettings>(builder => builder
                .AddPropertyToUI(x => x.EnableAllGames, propertyBuilder => propertyBuilder
                    .AddToSection(Sections.Experimental)
                    .WithDisplayName("Enable unsupported games")
                    .WithDescription("Allows you to manage unsupported games.")
                    .UseBooleanContainer()
                    .RequiresRestart()
                )
                .AddPropertyToUI(x => x.EnableCollectionSharing, b => b
                    .AddToSection(Sections.Experimental)
                    .WithDisplayName("Enable sharing collections")
                    .WithDescription("Allows uploading of collections")
                    .UseBooleanContainer()
                )
            );
    }
}
