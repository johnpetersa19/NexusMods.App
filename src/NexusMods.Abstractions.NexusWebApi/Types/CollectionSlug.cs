using TransparentValueObjects;

namespace NexusMods.Abstractions.NexusWebApi.Types;

/// <summary>
/// Unique identifier for a collection hosted on Nexus.
/// </summary>
[ValueObject<string>]
public readonly partial struct CollectionSlug : IAugmentWith<DefaultValueAugment>
{
    public static CollectionSlug DefaultValue { get; } = From(string.Empty);
}
