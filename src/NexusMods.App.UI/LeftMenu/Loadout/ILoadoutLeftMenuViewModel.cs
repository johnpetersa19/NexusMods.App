using System.Collections.ObjectModel;
using NexusMods.App.UI.LeftMenu.Items;

namespace NexusMods.App.UI.LeftMenu.Loadout;

public interface ILoadoutLeftMenuViewModel : ILeftMenuViewModel
{
    public ReadOnlyObservableCollection<ILeftMenuItemViewModel> LeftMenuCollectionItems { get; }
    
    public bool HasSingleCollection { get; }
    
    public IApplyControlViewModel ApplyControlViewModel { get; }
    
    public ILeftMenuItemViewModel LeftMenuItemLibrary { get; }
    
    public ILeftMenuItemViewModel LeftMenuItemLoadout { get; }
    public ILeftMenuItemViewModel LeftMenuItemNewCollection { get; }

    public ILeftMenuItemViewModel LeftMenuItemHealthCheck { get; }
    
    public ILeftMenuItemViewModel? LeftMenuItemExternalChanges { get; }
}
