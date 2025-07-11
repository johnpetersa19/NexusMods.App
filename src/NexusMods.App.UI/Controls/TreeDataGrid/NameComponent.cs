using JetBrains.Annotations;
using NexusMods.App.UI.Controls.Filters;
using NexusMods.App.UI.Controls.TreeDataGrid.Filters;
using R3;

namespace NexusMods.App.UI.Controls;

/// <summary>
/// Component for name fields that supports name-specific filtering.
/// </summary>
[PublicAPI]
public sealed class NameComponent : AValueComponent<string>, IItemModelComponent<NameComponent>, IComparable<NameComponent>
{
    /// <inheritdoc/>
    public NameComponent(
        string initialValue,
        IObservable<string> valueObservable,
        bool subscribeWhenCreated = false,
        bool observeOutsideUiThread = false) : base(initialValue, valueObservable, subscribeWhenCreated, observeOutsideUiThread) { }

    /// <inheritdoc/>
    public NameComponent(
        string initialValue,
        Observable<string> valueObservable,
        bool subscribeWhenCreated = false,
        bool observeOutsideUiThread = false) : base(initialValue, valueObservable, subscribeWhenCreated, observeOutsideUiThread) { }

    /// <inheritdoc/>
    public NameComponent(string value) : base(value) { }

    /// <inheritdoc/>
    public int CompareTo(NameComponent? other) => string.CompareOrdinal(Value.Value, other?.Value.Value);

    /// <inheritdoc/>
    public FilterResult MatchesFilter(Filter filter)
    {
        return filter switch
        {
            Filter.NameFilter nameFilter => Value.Value.Contains(
                nameFilter.SearchText, 
                nameFilter.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
                ? FilterResult.Pass : FilterResult.Fail,
            Filter.TextFilter textFilter => Value.Value.Contains(
                textFilter.SearchText, 
                textFilter.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
                ? FilterResult.Pass : FilterResult.Fail,
            _ => FilterResult.Indeterminate // Default: no opinion
        };
    }
} 