using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls.Models.TreeDataGrid;
using DynamicData;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Collections;
using NexusMods.Abstractions.Games;
using NexusMods.Abstractions.Library;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Abstractions.Loadouts.Extensions;
using NexusMods.Abstractions.NexusModsLibrary.Models;
using NexusMods.Abstractions.NexusWebApi;
using NexusMods.Abstractions.NexusWebApi.Types;
using NexusMods.Abstractions.Telemetry;
using NexusMods.App.UI.Controls;
using NexusMods.App.UI.Controls.Navigation;
using NexusMods.App.UI.Dialog;
using NexusMods.App.UI.Dialog.Enums;
using NexusMods.App.UI.Pages.LibraryPage;
using NexusMods.App.UI.Pages.LoadoutGroupFilesPage;
using NexusMods.App.UI.Pages.Sorting;
using NexusMods.App.UI.Resources;
using NexusMods.App.UI.Windows;
using NexusMods.App.UI.WorkspaceSystem;
using NexusMods.Collections;
using NexusMods.CrossPlatform.Process;
using NexusMods.UI.Sdk.Icons;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.ElementComparers;
using NexusMods.Networking.NexusWebApi;
using ObservableCollections;
using OneOf;
using R3;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveCommand = R3.ReactiveCommand;

namespace NexusMods.App.UI.Pages.LoadoutPage;

public class LoadoutViewModel : APageViewModel<ILoadoutViewModel>, ILoadoutViewModel
{
    public string EmptyStateTitleText { get; }
    public ReactiveCommand<NavigationInformation> ViewFilesCommand { get; }
    public ReactiveCommand<NavigationInformation> ViewLibraryCommand { get; }
    public ReactiveCommand<Unit> RemoveItemCommand { get; }
    public ReactiveCommand<Unit> CollectionToggleCommand { get; }
    public ReactiveCommand<Unit> DeselectItemsCommand { get; }

    public LoadoutTreeDataGridAdapter Adapter { get; }

    [Reactive] public string CollectionName { get; private set; }

    [Reactive] public int SelectionCount { get; private set; }
    [Reactive] public bool IsCollection { get; private set; }
    [Reactive] public bool IsCollectionEnabled { get; private set; }
    [Reactive] public bool IsCollectionUploaded { get; private set; }

    [Reactive] public ISortingSelectionViewModel RulesSectionViewModel { get; private set; }

    [Reactive] public int ItemCount { get; private set; }

    [Reactive] public bool HasRulesSection { get; private set; } = false;
    [Reactive] public LoadoutPageSubTabs SelectedSubTab { get; private set; }

    public ReactiveCommand<Unit> CommandUploadRevision { get; }
    public ReactiveCommand<Unit> CommandOpenRevisionUrl { get; }
    public ReactiveCommand<Unit> CommandRenameGroup { get; }

    private readonly IServiceProvider _serviceProvider;
    public LoadoutViewModel(
        IWindowManager windowManager,
        IServiceProvider serviceProvider,
        LoadoutId loadoutId,
        Optional<LoadoutItemGroupId> collectionGroupId = default,
        Optional<LoadoutPageSubTabs> selectedSubTab = default) : base(windowManager)
    {
        _serviceProvider = serviceProvider;

        var loadoutFilter = new LoadoutFilter
        {
            LoadoutId = loadoutId,
            CollectionGroupId = collectionGroupId,
        };

        Adapter = new LoadoutTreeDataGridAdapter(serviceProvider, loadoutFilter);

        var libraryService = serviceProvider.GetRequiredService<ILibraryService>();
        var connection = serviceProvider.GetRequiredService<IConnection>();
        var nexusModsLibrary = serviceProvider.GetRequiredService<NexusModsLibrary>();

        if (collectionGroupId.HasValue)
        {
            var collectionGroup = LoadoutItem.Load(connection.Db, collectionGroupId.Value);
            CollectionName = collectionGroup.Name;
            TabTitle = collectionGroup.Name;
            TabIcon = IconValues.CollectionsOutline;
            IsCollection = true;
            CollectionToggleCommand = new ReactiveCommand<Unit>(
                async (_, _) => await ToggleCollectionGroup(collectionGroupId, !IsCollectionEnabled, connection),
                configureAwait: false
            );

            IsCollectionUploaded = CollectionCreator.IsCollectionUploaded(connection, collectionGroupId.Value, out _);

            // If there are no other collections in the loadout, this is the `My Mods` collection and `All` view is hidden,
            // so we show the `sorting views here` view here instead
            var isSingleCollectionObservable = CollectionGroup.ObserveAll(connection)
                .Filter(collection => collection.AsLoadoutItemGroup().AsLoadoutItem().LoadoutId == loadoutId)
                .Transform(collection => collection.Id)
                .QueryWhenChanged(collections => collections.Count == 1)
                .ToObservable();

            RulesSectionViewModel = new SortingSelectionViewModel(serviceProvider, windowManager, loadoutId,
                canEditObservable: isSingleCollectionObservable
            );

            CommandUploadRevision = new ReactiveCommand<Unit>(async (unit, cancellationToken) =>
            {
                var shareDialog = IsCollectionUploaded ? LoadoutDialogs.UpdateCollection(CollectionName) : LoadoutDialogs.ShareCollection(CollectionName);
                var shareDialogResult = await windowManager.ShowDialog(shareDialog, DialogWindowType.Modal);
                if (shareDialogResult != ButtonDefinitionId.From("share")) return;

                var collection = await CollectionCreator.UploadDraftRevision(serviceProvider, collectionGroupId.Value, cancellationToken);
                var successDialog = IsCollectionUploaded ? LoadoutDialogs.UpdateCollectionSuccess(CollectionName) : LoadoutDialogs.ShareCollectionSuccess(CollectionName);

                var successDialogResult = await windowManager.ShowDialog(successDialog, DialogWindowType.Modal);

                IsCollectionUploaded = CollectionCreator.IsCollectionUploaded(connection, collectionGroupId.Value, out _);
                if (successDialogResult != ButtonDefinitionId.From("view-page")) return;

                var uri = GetCollectionUri(collection);
                await serviceProvider.GetRequiredService<IOSInterop>().OpenUrl(uri, cancellationToken: cancellationToken);
            }, maxSequential: 1, configureAwait: false);

            CommandOpenRevisionUrl = this.WhenAnyValue(vm => vm.IsCollectionUploaded).ToObservable().ToReactiveCommand<Unit>(async (_, cancellationToken) =>
            {
                var managedCollectionLoadoutGroup = ManagedCollectionLoadoutGroup.Load(connection.Db, collectionGroupId.Value);
                if (!managedCollectionLoadoutGroup.IsValid()) return;

                var uri = GetCollectionUri(managedCollectionLoadoutGroup.Collection);
                await serviceProvider.GetRequiredService<IOSInterop>().OpenUrl(uri, cancellationToken: cancellationToken);
            }, configureAwait: false);
            
            CommandRenameGroup = new ReactiveCommand<Unit>(async (_, cancellationToken) =>
            {
                var dialog = LoadoutDialogs.RenameCollection(CollectionName);
                var result = await windowManager.ShowDialog(dialog, DialogWindowType.Modal);
                if (result.ButtonId != ButtonDefinitionId.From("rename")) return;

                var newName = result.InputText;
                if (string.IsNullOrWhiteSpace(newName)) return;

                if (CollectionCreator.IsCollectionUploaded(connection, collectionGroupId.Value, out var collection))
                {
                    await nexusModsLibrary.EditCollectionName(collection, newName, CancellationToken.None);
                }

                using var tx = connection.BeginTransaction();
                tx.Add(collectionGroupId.Value, LoadoutItem.Name, newName);
                await tx.Commit();

                CollectionName = newName;
                TabTitle = CollectionName;
            });
        }
        else
        {
            CollectionName = string.Empty;
            TabTitle = Language.LoadoutViewPageTitle;
            TabIcon = IconValues.FormatAlignJustify;
            CollectionToggleCommand = new ReactiveCommand<Unit>(_ => { });
            RulesSectionViewModel = new SortingSelectionViewModel(serviceProvider, windowManager, loadoutId, Optional<Observable<bool>>.None);
            CommandUploadRevision = new ReactiveCommand();
            CommandOpenRevisionUrl = new ReactiveCommand();
            CommandRenameGroup = new ReactiveCommand();
        }

        DeselectItemsCommand = new ReactiveCommand<Unit>(_ => { Adapter.ClearSelection(); });

        var viewModFilesArgumentsSubject = new BehaviorSubject<Optional<LoadoutItemGroup.ReadOnly>>(Optional<LoadoutItemGroup.ReadOnly>.None);

        var loadout = Loadout.Load(connection.Db, loadoutId);
        EmptyStateTitleText = string.Format(Language.LoadoutGridViewModel_EmptyModlistTitleString, loadout.InstallationInstance.Game.Name);
        ViewLibraryCommand = new ReactiveCommand<NavigationInformation>(info =>
            {
                var pageData = new PageData
                {
                    FactoryId = LibraryPageFactory.StaticId,
                    Context = new LibraryPageContext
                    {
                        LoadoutId = loadoutId,
                    },
                };
                var workspaceController = GetWorkspaceController();
                var behavior = workspaceController.GetOpenPageBehavior(pageData, info);
                workspaceController.OpenPage(workspaceController.ActiveWorkspaceId, pageData, behavior);
            }
        );

        var numSortableItemProviders = loadout
            .InstallationInstance
            .GetGame()
            .SortableItemProviderFactories.Length;
        HasRulesSection = numSortableItemProviders > 0;

        SelectedSubTab = selectedSubTab switch
        {
            { HasValue: true, Value: LoadoutPageSubTabs.Rules } => HasRulesSection ? LoadoutPageSubTabs.Rules : LoadoutPageSubTabs.Mods,
            _ => LoadoutPageSubTabs.Mods,
        };

        ViewFilesCommand = viewModFilesArgumentsSubject
            .Select(viewModFilesArguments => viewModFilesArguments.HasValue)
            .ToReactiveCommand<NavigationInformation>(info =>
                {
                    var group = viewModFilesArgumentsSubject.Value;
                    if (!group.HasValue) return;

                    var isReadonly = group.Value.AsLoadoutItem()
                        .GetThisAndParents()
                        .Any(item => IsRequired(item.LoadoutItemId, connection));

                    var pageData = new PageData
                    {
                        FactoryId = LoadoutGroupFilesPageFactory.StaticId,
                        Context = new LoadoutGroupFilesPageContext
                        {
                            GroupIds = [group.Value.Id],
                            IsReadOnly = isReadonly,
                        },
                    };
                    var workspaceController = GetWorkspaceController();
                    var behavior = workspaceController.GetOpenPageBehavior(pageData, info);
                    workspaceController.OpenPage(workspaceController.ActiveWorkspaceId, pageData, behavior);
                },
                false
            );

        var hasValidRemoveSelection = Adapter.SelectedModels
            .ObserveChanged()
            .SelectMany(_ =>
                {
                    var observables = Adapter.SelectedModels.Select(model =>
                        model.GetObservable<LoadoutComponents.LockedEnabledState>(LoadoutColumns.EnabledState.LockedEnabledStateComponentKey)
                    );

                    return R3.Observable.CombineLatest(observables)
                        // if all items are readonly, or list is empty, no valid selection
                        .Select(list => !list.All(x => x.HasValue));
                }
            );


        RemoveItemCommand = hasValidRemoveSelection
            .ToReactiveCommand<Unit>(async (_, _) =>
                {
                    var ids = Adapter.SelectedModels
                        .SelectMany(static itemModel => GetLoadoutItemIds(itemModel))
                        .ToHashSet()
                        .Where(id => !IsRequired(id, connection))
                        .Select(x => (LibraryLinkedLoadoutItemId)x.Value)
                        .ToArray();

                    if (ids.Length == 0) return;

                    var dialog = DialogFactory.CreateMessageDialog(
                        title: "Uninstall mod(s)",
                        text: $"""
                        This will remove the selected mod(s) from:
                        
                        {CollectionName}
                        
                        ✓ The mod(s) will stay in your Library
                        ✓ You can reinstall anytime
                        """,
                        buttonDefinitions:
                        [
                            DialogStandardButtons.Cancel,
                            new DialogButtonDefinition("Uninstall",
                                ButtonDefinitionId.From("Uninstall"),
                                ButtonAction.Accept,
                                ButtonStyling.Default
                            )
                        ]
                    );

                    var result = await windowManager.ShowDialog(dialog, DialogWindowType.Modal);

                    if (result != ButtonDefinitionId.From("Uninstall")) return;

                    await libraryService.RemoveLinkedItemsFromLoadout(ids);
                },
                awaitOperation: AwaitOperation.Sequential,
                initialCanExecute: false,
                configureAwait: false
            );

        this.WhenActivated(disposables =>
            {
                Adapter.Activate().AddTo(disposables);

                Adapter.MessageSubject.SubscribeAwait(async (message, _) =>
                    {
                        // Toggle item state
                        if (message.IsT0)
                        {
                            await ToggleItemEnabledState(message.AsT0.Ids, connection);
                            return;
                        }

                        // Open collection
                        if (message.IsT1)
                        {
                            var data = message.AsT1;
                            OpenItemCollectionPage(data.Ids, data.NavigationInformation, loadoutId,
                                GetWorkspaceController(), connection
                            );
                            return;
                        }
                    }, awaitOperation: AwaitOperation.Parallel, configureAwait: false
                ).AddTo(disposables);

                // Update the selection count based on the selected models
                Adapter.SelectedModels
                    .ObserveChanged()
                    .Select(_ => Adapter.SelectedModels
                        .SelectMany(model => GetLoadoutItemIds(model))
                        .Distinct()
                        .Count()
                    )
                    .ObserveOnUIThreadDispatcher()
                    .Subscribe(count => SelectionCount = count);

                // Compute the target group for the ViewFilesCommand
                Adapter.SelectedModels.ObserveCountChanged(notifyCurrentCount: true)
                    .Select(this, static (count, vm) => count == 1 ? vm.Adapter.SelectedModels.First() : null)
                    .ObserveOnThreadPool()
                    .Select(connection, static (model, connection) =>
                        {
                            if (model is null) return Optional<LoadoutItemGroup.ReadOnly>.None;
                            return LoadoutGroupFilesViewModel.GetViewModFilesLoadoutItemGroup(GetLoadoutItemIds(model).ToArray(), connection);
                        }
                    )
                    .ObserveOnUIThreadDispatcher()
                    .Subscribe(viewModFilesArgumentsSubject.OnNext)
                    .AddTo(disposables);


                if (collectionGroupId.HasValue)
                {
                    LoadoutItem.Observe(connection, collectionGroupId.Value)
                        .Select(item => item.IsEnabled())
                        .OnUI()
                        .Subscribe(isEnabled => IsCollectionEnabled = isEnabled)
                        .AddTo(disposables);
                }

                LoadoutDataProviderHelper.CountAllLoadoutItems(serviceProvider, loadoutFilter)
                    .OnUI()
                    .Subscribe(count => ItemCount = count)
                    .DisposeWith(disposables);
            }
        );
    }

    private Uri GetCollectionUri(CollectionMetadata.ReadOnly collection)
    {
        var mappingCache = _serviceProvider.GetRequiredService<IGameDomainToGameIdMappingCache>();
        var gameDomain = mappingCache[collection.GameId];
        var uri = NexusModsUrlBuilder.GetCollectionUri(gameDomain, collection.Slug, revisionNumber: new Optional<RevisionNumber>());
        return uri;
    }

    internal static async Task ToggleItemEnabledState(LoadoutItemId[] ids, IConnection connection)
    {
        var toggleableItems = ids
            .Select(loadoutItemId => LoadoutItem.Load(connection.Db, loadoutItemId))
            // Exclude collection required items
            .Where(item => !IsRequired(item.Id, connection))
            // Exclude items that are part of a collection that is disabled
            .Where(item => !(item.Parent.TryGetAsCollectionGroup(out var collectionGroup)
                             && collectionGroup.AsLoadoutItemGroup().AsLoadoutItem().IsDisabled)
            )
            .ToArray();

        if (toggleableItems.Length == 0) return;

        // We only enable if all items are disabled, otherwise we disable
        var shouldEnable = toggleableItems.All(loadoutItem => loadoutItem.IsDisabled);

        using var tx = connection.BeginTransaction();

        foreach (var id in toggleableItems)
        {
            if (shouldEnable)
            {
                tx.Retract(id, LoadoutItem.Disabled, Null.Instance);
            }
            else
            {
                tx.Add(id, LoadoutItem.Disabled, Null.Instance);
            }
        }

        await tx.Commit();
    }

    internal static void OpenItemCollectionPage(
        LoadoutItemId[] ids,
        NavigationInformation navInfo,
        LoadoutId loadoutId,
        IWorkspaceController workspaceController,
        IConnection connection)
    {
        if (ids.Length == 0) return;

        // Open the collection page for the first item
        var firstItemId = ids.First();

        var parentGroup = LoadoutItem.Load(connection.Db, firstItemId).Parent;
        if (!parentGroup.TryGetAsCollectionGroup(out var collectionGroup)) return;

        if (collectionGroup.TryGetAsNexusCollectionLoadoutGroup(out var nexusCollectionGroup))
        {
            var nexusCollPageData = new PageData
            {
                FactoryId = CollectionLoadoutPageFactory.StaticId,
                Context = new CollectionLoadoutPageContext
                {
                    LoadoutId = loadoutId,
                    GroupId = nexusCollectionGroup.Id,
                },
            };
            var nexusPageBehavior = workspaceController.GetOpenPageBehavior(nexusCollPageData, navInfo);
            workspaceController.OpenPage(workspaceController.ActiveWorkspaceId, nexusCollPageData, nexusPageBehavior);

            return;
        }

        var pageData = new PageData
        {
            FactoryId = LoadoutPageFactory.StaticId,
            Context = new LoadoutPageContext
            {
                LoadoutId = loadoutId,
                GroupScope = collectionGroup.AsLoadoutItemGroup().LoadoutItemGroupId,
            },
        };
        var behavior = workspaceController.GetOpenPageBehavior(pageData, navInfo);
        workspaceController.OpenPage(workspaceController.ActiveWorkspaceId, pageData, behavior);

        return;
    }

    private static async Task ToggleCollectionGroup(Optional<LoadoutItemGroupId> collectionGroupId, bool shouldEnable, IConnection connection)
    {
        if (!collectionGroupId.HasValue) return;
        using var tx = connection.BeginTransaction();
        if (shouldEnable)
        {
            tx.Retract(collectionGroupId.Value, LoadoutItem.Disabled, Null.Instance);
        }
        else
        {
            tx.Add(collectionGroupId.Value, LoadoutItem.Disabled, Null.Instance);
        }

        await tx.Commit();
    }

    private static IEnumerable<LoadoutItemId> GetLoadoutItemIds(CompositeItemModel<EntityId> itemModel)
    {
        return itemModel.Get<LoadoutComponents.LoadoutItemIds>(LoadoutColumns.EnabledState.LoadoutItemIdsComponentKey).ItemIds;
    }

    private static bool IsRequired(LoadoutItemId id, IConnection connection)
    {
        return NexusCollectionItemLoadoutGroup.IsRequired.TryGetValue(LoadoutItem.Load(connection.Db, id), out var isRequired) && isRequired;
    }
}

public readonly record struct ToggleEnableStateMessage(LoadoutItemId[] Ids);

public readonly record struct OpenCollectionMessage(LoadoutItemId[] Ids, NavigationInformation NavigationInformation);

public class LoadoutTreeDataGridAdapter :
    TreeDataGridAdapter<CompositeItemModel<EntityId>, EntityId>,
    ITreeDataGirdMessageAdapter<OneOf<ToggleEnableStateMessage, OpenCollectionMessage>>
{
    public Subject<OneOf<ToggleEnableStateMessage, OpenCollectionMessage>> MessageSubject { get; } = new();

    private readonly ILoadoutDataProvider[] _loadoutDataProviders;
    private readonly LoadoutFilter _loadoutFilter;

    public LoadoutTreeDataGridAdapter(IServiceProvider serviceProvider, LoadoutFilter loadoutFilter)
    {
        _loadoutDataProviders = serviceProvider.GetServices<ILoadoutDataProvider>().ToArray();
        _loadoutFilter = loadoutFilter;
    }

    protected override IObservable<IChangeSet<CompositeItemModel<EntityId>, EntityId>> GetRootsObservable(bool viewHierarchical)
    {
        return _loadoutDataProviders.Select(x => x.ObserveLoadoutItems(_loadoutFilter)).MergeChangeSets();
    }

    protected override void BeforeModelActivationHook(CompositeItemModel<EntityId> model)
    {
        base.BeforeModelActivationHook(model);

        model.SubscribeToComponentAndTrack<LoadoutComponents.EnabledStateToggle, LoadoutTreeDataGridAdapter>(
            key: LoadoutColumns.EnabledState.EnabledStateToggleComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.CommandToggle.Subscribe((self, itemModel, component), static (_, tuple) =>
                {
                    var (self, itemModel, component) = tuple;
                    var ids = GetLoadoutItemIds(itemModel).ToArray();

                    self.MessageSubject.OnNext(new ToggleEnableStateMessage(ids));
                }
            )
        );

        model.SubscribeToComponentAndTrack<LoadoutComponents.ParentCollectionDisabled, LoadoutTreeDataGridAdapter>(
            key: LoadoutColumns.EnabledState.ParentCollectionDisabledComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.ButtonCommand.Subscribe((self, itemModel, component), static (navInfo, tuple) =>
                {
                    var (self, itemModel, component) = tuple;
                    var ids = GetLoadoutItemIds(itemModel).ToArray();

                    self.MessageSubject.OnNext(new OpenCollectionMessage(ids, navInfo));
                }
            )
        );

        model.SubscribeToComponentAndTrack<LoadoutComponents.LockedEnabledState, LoadoutTreeDataGridAdapter>(
            key: LoadoutColumns.EnabledState.LockedEnabledStateComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.ButtonCommand.Subscribe((self, itemModel, component), static (navInfo, tuple) =>
                {
                    var (self, itemModel, component) = tuple;
                    var ids = GetLoadoutItemIds(itemModel).ToArray();

                    self.MessageSubject.OnNext(new OpenCollectionMessage(ids, navInfo));
                }
            )
        );

        model.SubscribeToComponentAndTrack<LoadoutComponents.MixLockedAndParentDisabled, LoadoutTreeDataGridAdapter>(
            key: LoadoutColumns.EnabledState.MixLockedAndParentDisabledComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.ButtonCommand.Subscribe((self, itemModel, component), static (navInfo, tuple) =>
                {
                    var (self, itemModel, component) = tuple;
                    var ids = GetLoadoutItemIds(itemModel).ToArray();

                    self.MessageSubject.OnNext(new OpenCollectionMessage(ids, navInfo));
                }
            )
        );
    }

    private static IEnumerable<LoadoutItemId> GetLoadoutItemIds(CompositeItemModel<EntityId> itemModel)
    {
        return itemModel.Get<LoadoutComponents.LoadoutItemIds>(LoadoutColumns.EnabledState.LoadoutItemIdsComponentKey).ItemIds;
    }

    protected override IColumn<CompositeItemModel<EntityId>>[] CreateColumns(bool viewHierarchical)
    {
        var nameColumn = ColumnCreator.Create<EntityId, SharedColumns.Name>();

        return
        [
            viewHierarchical ? ITreeDataGridItemModel<CompositeItemModel<EntityId>, EntityId>.CreateExpanderColumn(nameColumn) : nameColumn,
            ColumnCreator.Create<EntityId, SharedColumns.InstalledDate>(sortDirection: ListSortDirection.Descending),
            ColumnCreator.Create<EntityId, LoadoutColumns.Collections>(),
            ColumnCreator.Create<EntityId, LoadoutColumns.EnabledState>(),
        ];
    }

    private bool _isDisposed;

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_isDisposed)
        {
            MessageSubject.Dispose();
            _isDisposed = true;
        }

        base.Dispose(disposing);
    }
}
