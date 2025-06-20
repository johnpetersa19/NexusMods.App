using JetBrains.Annotations;
using OneOf;

namespace NexusMods.Abstractions.Settings;

/// <summary>
/// Value container for a settings property.
/// </summary>
[PublicAPI]
public class SettingsPropertyValueContainer : OneOfBase<BooleanContainer, SingleValueMultipleChoiceContainer, ConfigurablePathsContainer>
{
    /// <summary>
    /// Constructor.
    /// </summary>
    public SettingsPropertyValueContainer(OneOf<
        BooleanContainer,
        SingleValueMultipleChoiceContainer,
        ConfigurablePathsContainer> input) : base(input) { }
}
