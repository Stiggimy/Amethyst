// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.Web.Http;
using Amethyst.Classes;
using Amethyst.MVVM;
using Amethyst.Pages;
using Amethyst.Plugins.Contract;
using Amethyst.Schedulers;
using Amethyst.Utils;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.AppNotifications;
using RestSharp;
using WinRT;
using WinRT.Interop;
using WinUI.Fluent.Icons;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Amethyst;

/// <summary>
///     An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    public delegate Task RequestApplicationUpdate(object sender, EventArgs e);

    public static RequestApplicationUpdate RequestUpdateEvent;

    private bool _mainPageInitFinished;

    private SystemBackdropConfiguration _configurationSource;
    private MicaController _micaController;
    private DesktopAcrylicController _acrylicController;

    private bool _mainPageLoadedOnce;
    private string _remoteVersionString = AppData.VersionString.Display;
    private Shared.Events.RequestEvent _updateBrushesEvent;

    private WindowsSystemDispatcherQueueHelper _wsdqHelper; // See separate sample below for implementation

    public MainWindow()
    {
        Logger.Info($"Constructing a new {GetType()}...");
        Logger.Info("Initializing shared XAML components...");
        InitializeComponent();

        Logger.Info("Applying available window backdrops...");
        TrySetMicaBackdrop();

        // Set up the shutdown handler
        Logger.Info("Setting up the close handler...");
        Closed += Window_Closed;

        // Cache needed UI elements
        Logger.Info("Making shared elements available for children views...");
        Shared.TeachingTips.MainPage.InitializerTeachingTip = InitializerTeachingTip;
        Shared.TeachingTips.MainPage.EndingTeachingTip = EndingTeachingTip;
        Shared.TeachingTips.MainPage.ReloadInfoBar = ReloadInfoBar;

        Shared.Main.MainNavigationView = NavView;
        Shared.Main.AppTitleLabel = AppTitleLabel;

        Shared.Main.InterfaceBlockerGrid = InterfaceBlockerGrid;
        Shared.Main.NavigationBlockerGrid = NavigationBlockerGrid;
        Shared.Main.MainContentFrame = ContentFrame;
        Shared.Main.PluginsUpdatePendingProgressBar = PluginsUpdatePendingProgressBar;
        Shared.Main.PluginsUpdatePendingInfoBar = PluginsUpdatePendingInfoBar;
        Shared.Main.PluginsInstallInfoBar = PluginsInstallInfoBar;
        Shared.Main.PluginsUninstallInfoBar = PluginsUninstallInfoBar;
        Shared.Main.PluginsUpdateInfoBar = PluginsUpdateInfoBar;

        Shared.Main.NavigationItems.NavViewGeneralButtonIcon = NavViewGeneralButtonIcon;
        Shared.Main.NavigationItems.NavViewSettingsButtonIcon = NavViewSettingsButtonIcon;
        Shared.Main.NavigationItems.NavViewDevicesButtonIcon = NavViewDevicesButtonIcon;
        Shared.Main.NavigationItems.NavViewInfoButtonIcon = NavViewInfoButtonIcon;
        Shared.Main.NavigationItems.NavViewPluginsButtonIcon = NavViewPluginsButtonIcon;

        Shared.Main.NavigationItems.NavViewGeneralButtonLabel = NavViewGeneralButtonLabel;
        Shared.Main.NavigationItems.NavViewSettingsButtonLabel = NavViewSettingsButtonLabel;
        Shared.Main.NavigationItems.NavViewDevicesButtonLabel = NavViewDevicesButtonLabel;
        Shared.Main.NavigationItems.NavViewInfoButtonLabel = NavViewInfoButtonLabel;
        Shared.Main.NavigationItems.NavViewPluginsButtonLabel = NavViewPluginsButtonLabel;

        // Set up
        Logger.Info("Setting up the window decoration data...");
        Title = "Amethyst";

        Logger.Info("Making the app window available for children views... (Window Handle)");
        Shared.Main.AppWindowId = WindowNative.GetWindowHandle(this);

        Logger.Info("Making the app window available for children views... (XAML UI Window)");
        Shared.Main.Window = this.As<Window>();

        Logger.Info("Making the app window available for children views... (Shared App Window)");
        Shared.Main.AppWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(
            WindowNative.GetWindowHandle(this)));

        // Set titlebar/taskview icon
        Logger.Info("Setting the App Window icon...");
        Shared.Main.AppWindow.SetIcon(Path.Combine(
            Interfacing.ProgramLocation.DirectoryName!, "Assets", "ktvr.ico"));

        Logger.Info("Extending the window titlebar...");
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            // Chad Windows 11
            Shared.Main.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            Shared.Main.AppWindow.TitleBar.SetDragRectangles(new RectInt32[]
            {
                new(0, 0, 10000000, 30)
            });

            Shared.Main.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            Shared.Main.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            Shared.Main.AppWindow.TitleBar.ButtonHoverBackgroundColor =
                Shared.Main.AppWindow.TitleBar.ButtonPressedBackgroundColor;
        }
        else
            // Poor ass Windows 10 <1809
        {
            Logger.Warn("Time to get some updates for your archaic Windows install, man!");
            ExtendsContentIntoTitleBar = true; // Use the UWP titlebar extension method
            SetTitleBar(DragElement); // Use the UWP titlebar extension method: set drag zones
        }

        Logger.Info("Making the app dispatcher available for children views...");
        Shared.Main.DispatcherQueue = DispatcherQueue;

        try
        {
            Logger.Info("Registering for NotificationInvoked WinRT event...");
            if (!AppNotificationManager.IsSupported()) // Check for compatibility first
                throw new NotSupportedException("AppNotificationManager is not supported on this system!");

            // To ensure all Notification handling happens in this process instance, register for
            // NotificationInvoked before calling Register(). Without this a new process will
            // be launched to handle the notification.
            AppNotificationManager.Default.NotificationInvoked +=
                (_, notificationActivatedEventArgs) =>
                {
                    Interfacing.ProcessToastArguments(notificationActivatedEventArgs);
                };

            Logger.Info("Creating the default notification manager...");
            Shared.Main.NotificationManager = AppNotificationManager.Default;

            Logger.Info("Registering the notification manager...");
            Shared.Main.NotificationManager.Register(); // Try registering
        }
        catch (Exception e)
        {
            Logger.Error(e); // We couldn't set the manager up, sorry...
            Logger.Info("Resetting the notification manager...");
            Shared.Main.NotificationManager = null; // Not using it!
        }

        // Start the main program loop
        _ = Task.Run(Main.MainLoop);

        // Search the "Plugins" sub-directory for assemblies that match the imports.
        // Iterate over all directories in .\Plugins dir and add all * dirs to catalogs
        Logger.Info("Searching for local plugins now...");
        var pluginDirectoryList = Directory.EnumerateDirectories(
            Path.Combine(Interfacing.ProgramLocation.DirectoryName!, "Plugins"),
            "*", SearchOption.TopDirectoryOnly).ToList();

        // Search the "Plugins" AppData directory for assemblies that match the imports.
        // Iterate over all directories in Plugins dir and add all * dirs to catalogs
        Logger.Info("Searching for shared plugins now...");
        pluginDirectoryList.AddRange(Directory.EnumerateDirectories(
            Interfacing.GetAppDataPluginFolderDir(""),
            "*", SearchOption.TopDirectoryOnly));

        // Search for external plugins
        try
        {
            // Load the JSON source into buffer, parse
            Logger.Info("Searching for external plugins now...");
            var jsonRoot = JsonObject.Parse(
                File.ReadAllText(Interfacing.GetAppDataFileDir("amethystpaths.k2path")));

            // Loop over all the external plugins and append
            if (jsonRoot.ContainsKey("external_plugins"))
            {
                // Try loading all path-valid plugin entries
                jsonRoot.GetNamedArray("external_plugins").ToList()
                    .Where(pluginEntry => Directory.Exists(pluginEntry.GetString())).ToList()
                    .ForEach(pluginPath => pluginDirectoryList.Add(pluginPath.GetString()));

                // Write out all invalid ones
                jsonRoot.GetNamedArray("external_plugins").ToList()
                    .Where(pluginEntry => !Directory.Exists(pluginEntry.GetString())).ToList()
                    .ForEach(pluginPath =>
                    {
                        // Add the plugin to the 'attempted' list
                        AppPlugins.LoadAttemptedPluginsList.Add(new LoadAttemptedPlugin
                        {
                            Name = pluginPath.GetString(),
                            Status = AppPlugins.PluginLoadError.NoPluginFolder
                        });

                        Logger.Error(new DirectoryNotFoundException(
                            $"Plugin hint directory \"{pluginPath.GetString()}\" doesn't exist!"));
                    });
            }

            else
            {
                Logger.Info("No external plugins found! Loading the local ones now...");
            }
        }
        catch (FileNotFoundException e)
        {
            Logger.Error($"Checking for external plugins has failed, an exception occurred. Message: {e.Message}");
            Logger.Error($"Creating new {Interfacing.GetAppDataFileDir("amethystpaths.k2path")} config...");
            File.Create(Interfacing.GetAppDataFileDir("amethystpaths.k2path")); // Create a placeholder file
        }
        catch (Exception e)
        {
            Logger.Error($"Checking for external plugins has failed, an exception occurred. Message: {e.Message}");
        }

        // Add the current assembly to support invoke method exports
        AssemblyLoadContext.Default.LoadFromAssemblyPath(
            Assembly.GetAssembly(typeof(ITrackingDevice))!.Location);

        Logger.Info("Enumerating all plugins now...");
        var catalog = new AggregateCatalog();

        pluginDirectoryList.ForEach(pluginPath =>
            catalog.Catalogs.AddPlugin(new DirectoryInfo(pluginPath)));

        if (catalog.Catalogs.Count < 1)
        {
            Logger.Fatal(new CompositionException("No plugins directories found! Shutting down..."));
            Interfacing.Fail(Interfacing.LocalizedJsonString("/CrashHandler/Content/Crash/NoPlugins"));
        }

        Logger.Info($"Found {pluginDirectoryList.Count} potentially valid plugin directories...");
        try
        {
            // Loop over device catalog parts and compose
            foreach (var pluginCatalog in catalog.Catalogs)
            {
                var pCatalog = new AggregateCatalog();
                pCatalog.Catalogs.Add(pluginCatalog);

                using var container = new CompositionContainer(pCatalog,
                    CompositionOptions.DisableSilentRejection);

                Logger.Info($"Searching for tracking devices (providers) plugins within {pluginCatalog}...");
                var devicePlugins = container.GetExports<ITrackingDevice, IPluginMetadata>().ToList();

                Logger.Info($"Found {devicePlugins.Count} potentially valid exported plugin parts...");
                if (devicePlugins.Count <= 0)
                {
                    Logger.Info("Skipping this catalog part as it doesn't contain any usable plugins!");
                    continue; // Skip composing this plugin, it won't do any good!
                }

                var plugin = devicePlugins.First();
                Logger.Info($"Preparing exports for ({plugin.Metadata.Name}, {plugin.Metadata.Guid})...");

                try
                {
                    AmethystPluginHost = new PluginHost(plugin.Metadata.Guid);
                    container.ComposeParts(this);

                    Logger.Info($"Parsing ({plugin.Metadata.Name}, {plugin.Metadata.Guid})...");

                    // Check the plugin GUID against others loaded, INVALID and null
                    if (string.IsNullOrEmpty(plugin.Metadata.Guid) || plugin.Metadata.Guid == "INVALID" ||
                        AppPlugins.TrackingDevicesList.ContainsKey(plugin.Metadata.Guid))
                    {
                        // Add the device to the 'attempted' list, mark as duplicate
                        AppPlugins.LoadAttemptedPluginsList.Add(new LoadAttemptedPlugin
                        {
                            Name = plugin.Metadata.Name,
                            Guid = plugin.Metadata.Guid,
                            PluginType = AppPlugins.PluginType.TrackingDevice,
                            Publisher = plugin.Metadata.Publisher,
                            Website = plugin.Metadata.Website,
                            UpdateEndpoint = plugin.Metadata.UpdateEndpoint,
                            Version = new Version(plugin.Metadata.Version),
                            Status = AppPlugins.PluginLoadError.BadOrDuplicateGuid
                        });

                        Logger.Error(new DuplicateNameException($"({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                                                "has a duplicate GUID value to another plugin, discarding it!"));
                        continue; // Give up on this one :(
                    }

                    // Check the plugin GUID against the ones we need to skip
                    if (AppData.Settings.DisabledPluginsGuidSet.Contains(plugin.Metadata.Guid))
                    {
                        // Add the device to the 'attempted' list, mark as duplicate
                        AppPlugins.LoadAttemptedPluginsList.Add(new LoadAttemptedPlugin
                        {
                            Name = plugin.Metadata.Name,
                            Guid = plugin.Metadata.Guid,
                            PluginType = AppPlugins.PluginType.TrackingDevice,
                            Publisher = plugin.Metadata.Publisher,
                            Website = plugin.Metadata.Website,
                            UpdateEndpoint = plugin.Metadata.UpdateEndpoint,
                            Version = new Version(plugin.Metadata.Version),
                            Status = AppPlugins.PluginLoadError.LoadingSkipped
                        });

                        Logger.Error($"({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                     "is requested to be skipped (via GUID), discarding it!");
                        continue; // Give up on this one :(
                    }

                    try
                    {
                        Logger.Info($"Trying to load ({plugin.Metadata.Name}, {plugin.Metadata.Guid})...");
                        Logger.Info($"Result: {plugin.Value}"); // Load the plugin
                    }
                    catch (CompositionException e)
                    {
                        Crashes.TrackError(e); // Composition exception
                        Logger.Error($"Loading plugin ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                     "failed with a local (plugin-wise) MEF exception: " +
                                     $"Message: {e.Message}\nErrors occurred: {e.Errors}\n" +
                                     $"Possible causes: {e.RootCauses}\nTrace: {e.StackTrace}");

                        // Add the device to the 'attempted' list, mark as possibly missing deps
                        AppPlugins.LoadAttemptedPluginsList.Add(new LoadAttemptedPlugin
                        {
                            Name = plugin.Metadata.Name,
                            Guid = plugin.Metadata.Guid,
                            PluginType = AppPlugins.PluginType.TrackingDevice,
                            Publisher = plugin.Metadata.Publisher,
                            Website = plugin.Metadata.Website,
                            UpdateEndpoint = plugin.Metadata.UpdateEndpoint,
                            Version = new Version(plugin.Metadata.Version),
                            Error = e.UnwrapCompositionException().Message,
                            Status = AppPlugins.PluginLoadError.NoPluginDependencyDll
                        });
                        continue; // Give up on this one :(
                    }
                    catch (Exception e)
                    {
                        Crashes.TrackError(e); // Other exception
                        Logger.Error($"Loading plugin ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                     "failed with an exception, probably some of its dependencies are missing. " +
                                     $"Message: {e.Message}, Trace: {e.StackTrace}");

                        // Add the device to the 'attempted' list, mark as unknown
                        AppPlugins.LoadAttemptedPluginsList.Add(new LoadAttemptedPlugin
                        {
                            Name = plugin.Metadata.Name,
                            Guid = plugin.Metadata.Guid,
                            PluginType = AppPlugins.PluginType.TrackingDevice,
                            Publisher = plugin.Metadata.Publisher,
                            Website = plugin.Metadata.Website,
                            UpdateEndpoint = plugin.Metadata.UpdateEndpoint,
                            Version = new Version(plugin.Metadata.Version),
                            Error = e.Message,
                            Status = AppPlugins.PluginLoadError.Other
                        });
                        continue; // Give up on this one :(
                    }

                    // It must be good if we're somehow still here
                    var pluginLocation = Assembly.GetAssembly(plugin.Value.GetType()).Location;
                    var pluginFolder = Directory.GetParent(pluginLocation).FullName;

                    // Add the device to the 'attempted' list, mark as all fine
                    Logger.Info($"Adding ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                "to the load-attempted device plugins list (AppPlugins)...");
                    AppPlugins.LoadAttemptedPluginsList.Add(new LoadAttemptedPlugin
                    {
                        Name = plugin.Metadata.Name,
                        Guid = plugin.Metadata.Guid,
                        PluginType = AppPlugins.PluginType.TrackingDevice,
                        Publisher = plugin.Metadata.Publisher,
                        Website = plugin.Metadata.Website,
                        UpdateEndpoint = plugin.Metadata.UpdateEndpoint,
                        Version = new Version(plugin.Metadata.Version),
                        Folder = pluginFolder,
                        Status = AppPlugins.PluginLoadError.NoError
                    });

                    // Add the device to the global device list, add the plugin folder path
                    Logger.Info($"Adding ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                "to the global tracking device plugins list (AppPlugins)...");
                    AppPlugins.TrackingDevicesList.Add(plugin.Metadata.Guid, new TrackingDevice(
                        plugin.Metadata.Name, plugin.Metadata.Guid, pluginLocation,
                        new Version(plugin.Metadata.Version), plugin.Value)
                    {
                        LocalizationResourcesRoot = (new LocalisationFileJson(),
                            Path.Join(pluginFolder, "Assets", "Strings"))
                    });

                    // Set the device's string resources root to its provided folder
                    // (If it wants to change it, it's gonna need to do that after OnLoad anyway)
                    Logger.Info($"Registering ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                "default root language resource context (AppPlugins)...");
                    Interfacing.Plugins.SetLocalizationResourcesRoot(
                        Path.Join(pluginFolder, "Assets", "Strings"), plugin.Metadata.Guid);

                    Logger.Info($"Telling ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                "this it's just been loaded into the core app domain...");
                    plugin.Value.OnLoad(); // Call the OnLoad handler for the first time

                    Logger.Info($"Device ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) \n" +
                                $"with the device class library dll at: {pluginLocation}\n" +
                                $"provides discoverable properties:\nStatus: {plugin.Value.DeviceStatus}\n" +
                                $"- Status String: {plugin.Value.DeviceStatusString}\n" +
                                $"- Supports Flip: {plugin.Value.IsFlipSupported}\n" +
                                $"- Initialized: {plugin.Value.IsInitialized}\n" +
                                $"- Overrides Physics: {plugin.Value.IsPhysicsOverrideEnabled}\n" +
                                $"- Blocks Filtering: {plugin.Value.IsPositionFilterBlockingEnabled}\n" +
                                $"- Updates By Itself: {plugin.Value.IsSelfUpdateEnabled}\n" +
                                $"- Supports Settings: {plugin.Value.IsSettingsDaemonSupported}\n" +
                                $"- Provides Prepended Joints: {plugin.Value.TrackedJoints.Count}");

                    // Check if the loaded device is used as anything (AppData.Settings)
                    Logger.Info($"Checking if ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) has any roles set...");
                    if (AppPlugins.IsBase(plugin.Metadata.Guid))
                        Logger.Info($"({plugin.Metadata.Name}, {plugin.Metadata.Guid}) is a Base device!");
                    else if (AppPlugins.IsOverride(plugin.Metadata.Guid))
                        Logger.Info($"({plugin.Metadata.Name}, {plugin.Metadata.Guid}) is an Override device!");
                    else
                        Logger.Info($"({plugin.Metadata.Name}, {plugin.Metadata.Guid}) does not serve any purpose :/");
                }
                catch (Exception e)
                {
                    // Add the device to the 'attempted' list, mark as unknown
                    Logger.Error($"Loading plugin ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                 "failed with a global outer caught exception. " +
                                 $"Provided exception Message: {e.Message}, Trace: {e.StackTrace}");

                    AppPlugins.LoadAttemptedPluginsList.Add(new LoadAttemptedPlugin
                    {
                        Name = plugin.Metadata.Name,
                        Guid = plugin.Metadata.Guid,
                        PluginType = AppPlugins.PluginType.TrackingDevice,
                        Publisher = plugin.Metadata.Publisher,
                        Website = plugin.Metadata.Website,
                        UpdateEndpoint = plugin.Metadata.UpdateEndpoint,
                        Version = new Version(plugin.Metadata.Version),
                        Error = $"{e.Message}\n\n{e.StackTrace}",
                        Status = AppPlugins.PluginLoadError.Other
                    });
                }
            }

            // Check if we have enough plugins to run the app
            if (AppPlugins.TrackingDevicesList.Count < 1)
            {
                Logger.Fatal(new CompositionException("No plugins (tracking devices) loaded! Shutting down..."));
                Interfacing.Fail(Interfacing.LocalizedJsonString("/CrashHandler/Content/Crash/NoDevices"));
            }

            Logger.Info("Registration of tracking device plugins has ended, there are " +
                        $"[{AppPlugins.TrackingDevicesList.Count}] valid plugins in total.");

            AppPlugins.TrackingDevicesList.ToList().ForEach(x => Logger.Info(
                $"Loaded a valid tracking provider ({{{x.Key}}}, \"{x.Value.Name}\")"));

            // Loop over service catalog parts and compose
            foreach (var pluginCatalog in catalog.Catalogs)
            {
                var pCatalog = new AggregateCatalog();
                pCatalog.Catalogs.Add(pluginCatalog);

                using var container = new CompositionContainer(pCatalog,
                    CompositionOptions.DisableSilentRejection);

                Logger.Info($"Searching for tracking services (endpoint) plugins within {pluginCatalog}...");
                var devicePlugins = container.GetExports<IServiceEndpoint, IPluginMetadata>().ToList();

                Logger.Info($"Found {devicePlugins.Count} potentially valid exported plugin parts...");
                if (devicePlugins.Count <= 0)
                {
                    Logger.Info("Skipping this catalog part as it doesn't contain any usable plugins!");
                    continue; // Skip composing this plugin, it won't do any good!
                }

                var plugin = devicePlugins.First();
                Logger.Info($"Preparing exports for ({plugin.Metadata.Name}, {plugin.Metadata.Guid})...");

                try
                {
                    AmethystPluginHost = new PluginHost(plugin.Metadata.Guid);
                    container.ComposeParts(this);

                    Logger.Info($"Parsing ({plugin.Metadata.Name}, {plugin.Metadata.Guid})...");

                    // Check the plugin GUID against others loaded, INVALID and null
                    if (string.IsNullOrEmpty(plugin.Metadata.Guid) || plugin.Metadata.Guid == "INVALID" ||
                        AppPlugins.ServiceEndpointsList.ContainsKey(plugin.Metadata.Guid))
                    {
                        // Add the device to the 'attempted' list, mark as duplicate
                        AppPlugins.LoadAttemptedPluginsList.Add(new LoadAttemptedPlugin
                        {
                            Name = plugin.Metadata.Name,
                            Guid = plugin.Metadata.Guid,
                            PluginType = AppPlugins.PluginType.ServiceEndpoint,
                            Publisher = plugin.Metadata.Publisher,
                            Website = plugin.Metadata.Website,
                            UpdateEndpoint = plugin.Metadata.UpdateEndpoint,
                            Version = new Version(plugin.Metadata.Version),
                            Status = AppPlugins.PluginLoadError.BadOrDuplicateGuid
                        });

                        Logger.Error(new DuplicateNameException($"({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                                                "has a duplicate GUID value to another plugin, discarding it!"));
                        continue; // Give up on this one :(
                    }

                    // Check the plugin GUID against the ones we need to skip
                    if (AppData.Settings.DisabledPluginsGuidSet.Contains(plugin.Metadata.Guid))
                    {
                        // Add the device to the 'attempted' list, mark as duplicate
                        AppPlugins.LoadAttemptedPluginsList.Add(new LoadAttemptedPlugin
                        {
                            Name = plugin.Metadata.Name,
                            Guid = plugin.Metadata.Guid,
                            PluginType = AppPlugins.PluginType.ServiceEndpoint,
                            Publisher = plugin.Metadata.Publisher,
                            Website = plugin.Metadata.Website,
                            UpdateEndpoint = plugin.Metadata.UpdateEndpoint,
                            Version = new Version(plugin.Metadata.Version),
                            Status = AppPlugins.PluginLoadError.LoadingSkipped
                        });

                        Logger.Error($"({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                     "is requested to be skipped (via GUID), discarding it!");
                        continue; // Give up on this one :(
                    }

                    try
                    {
                        Logger.Info($"Trying to load ({plugin.Metadata.Name}, {plugin.Metadata.Guid})...");
                        Logger.Info($"Result: {plugin.Value}"); // Load the plugin
                    }
                    catch (CompositionException e)
                    {
                        Crashes.TrackError(e); // Composition exception
                        Logger.Error($"Loading plugin ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                     "failed with a local (plugin-wise) MEF exception: " +
                                     $"Message: {e.Message}\nErrors occurred: {e.Errors}\n" +
                                     $"Possible causes: {e.RootCauses}\nTrace: {e.StackTrace}");

                        // Add the device to the 'attempted' list, mark as possibly missing deps
                        AppPlugins.LoadAttemptedPluginsList.Add(new LoadAttemptedPlugin
                        {
                            Name = plugin.Metadata.Name,
                            Guid = plugin.Metadata.Guid,
                            PluginType = AppPlugins.PluginType.ServiceEndpoint,
                            Publisher = plugin.Metadata.Publisher,
                            Website = plugin.Metadata.Website,
                            UpdateEndpoint = plugin.Metadata.UpdateEndpoint,
                            Version = new Version(plugin.Metadata.Version),
                            Error = e.UnwrapCompositionException().Message,
                            Status = AppPlugins.PluginLoadError.NoPluginDependencyDll
                        });
                        continue; // Give up on this one :(
                    }
                    catch (Exception e)
                    {
                        Crashes.TrackError(e); // Other exception
                        Logger.Error($"Loading plugin ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                     "failed with an exception, probably some of its dependencies are missing. " +
                                     $"Message: {e.Message}, Trace: {e.StackTrace}");

                        // Add the device to the 'attempted' list, mark as unknown
                        AppPlugins.LoadAttemptedPluginsList.Add(new LoadAttemptedPlugin
                        {
                            Name = plugin.Metadata.Name,
                            Guid = plugin.Metadata.Guid,
                            PluginType = AppPlugins.PluginType.ServiceEndpoint,
                            Publisher = plugin.Metadata.Publisher,
                            Website = plugin.Metadata.Website,
                            UpdateEndpoint = plugin.Metadata.UpdateEndpoint,
                            Version = new Version(plugin.Metadata.Version),
                            Error = e.Message,
                            Status = AppPlugins.PluginLoadError.Other
                        });
                        continue; // Give up on this one :(
                    }

                    // It must be good if we're somehow still here
                    var pluginLocation = Assembly.GetAssembly(plugin.Value.GetType()).Location;
                    var pluginFolder = Directory.GetParent(pluginLocation).FullName;

                    // Add the device to the global device list, add the plugin folder path
                    Logger.Info($"Adding ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                "to the global service endpoints plugins list (AppPlugins)...");
                    AppPlugins.ServiceEndpointsList.Add(plugin.Metadata.Guid, new ServiceEndpoint(
                        plugin.Metadata.Name, plugin.Metadata.Guid, pluginLocation,
                        new Version(plugin.Metadata.Version), plugin.Value)
                    {
                        LocalizationResourcesRoot = (new LocalisationFileJson(),
                            Path.Join(pluginFolder, "Assets", "Strings"))
                    });

                    // Set the device's string resources root to its provided folder
                    // (If it wants to change it, it's gonna need to do that after OnLoad anyway)
                    Logger.Info($"Registering ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                "default root language resource context (AppPlugins)...");
                    Interfacing.Plugins.SetLocalizationResourcesRoot(
                        Path.Join(pluginFolder, "Assets", "Strings"), plugin.Metadata.Guid);

                    Logger.Info($"Telling ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                "this it's just been loaded into the core app domain...");
                    plugin.Value.OnLoad(); // Call the OnLoad handler for the first time

                    Logger.Info($"Service ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) \n" +
                                $"with the device class library dll at: {pluginLocation}\n" +
                                $"provides discoverable properties:\nStatus: {plugin.Value.ServiceStatus}\n" +
                                $"- Status String: {plugin.Value.ServiceStatusString}\n" +
                                $"- Supports manual calibration: {plugin.Value.ControllerInputActions != null}\n" +
                                $"- Supports automatic calibration: {plugin.Value.HeadsetPose != null}\n" +
                                $"- Needs to be restarted on changed: {plugin.Value.IsRestartOnChangesNeeded}\n" +
                                $"- Supports Settings: {plugin.Value.IsSettingsDaemonSupported}\n" +
                                $"- Additional Supported Trackers: {plugin.Value.AdditionalSupportedTrackerTypes.ToList()}");

                    // Add the device to the 'attempted' list, mark as all fine
                    Logger.Info($"Adding ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                "to the load-attempted device plugins list (AppPlugins)...");
                    AppPlugins.LoadAttemptedPluginsList.Add(new LoadAttemptedPlugin
                    {
                        Name = plugin.Metadata.Name,
                        Guid = plugin.Metadata.Guid,
                        PluginType = AppPlugins.PluginType.ServiceEndpoint,
                        Publisher = plugin.Metadata.Publisher,
                        Website = plugin.Metadata.Website,
                        UpdateEndpoint = plugin.Metadata.UpdateEndpoint,
                        Version = new Version(plugin.Metadata.Version),
                        Folder = pluginFolder,
                        Status = AppPlugins.PluginLoadError.NoError
                    });

                    // Check if the loaded device is used as anything (AppData.Settings)
                    Logger.Info($"Checking if ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) has any roles set...");
                    Logger.Info($"({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                $"{(AppData.Settings.ServiceEndpointGuid == plugin.Metadata.Guid ? "is the selected service!" : "does not serve any purpose :/")}");

                    // Check and use service's provided [freeze] action handlers
                    if (AppData.Settings.ServiceEndpointGuid == plugin.Metadata.Guid &&
                        plugin.Value.ControllerInputActions?.TrackingFreezeToggled is not null)
                        plugin.Value.ControllerInputActions.TrackingFreezeToggled += Main.FreezeActionToggled;

                    // Check and use service's provided [flip] action handlers
                    if (AppData.Settings.ServiceEndpointGuid == plugin.Metadata.Guid &&
                        plugin.Value.ControllerInputActions?.SkeletonFlipToggled is not null)
                        plugin.Value.ControllerInputActions.SkeletonFlipToggled += Main.FlipActionToggled;
                }
                catch (Exception e)
                {
                    // Add the device to the 'attempted' list, mark as unknown
                    Crashes.TrackError(e); // Other, outer MEF container exception
                    Logger.Error($"Loading plugin ({plugin.Metadata.Name}, {plugin.Metadata.Guid}) " +
                                 "failed with a global outer caught exception. " +
                                 $"Provided exception Message: {e.Message}, Trace: {e.StackTrace}");

                    AppPlugins.LoadAttemptedPluginsList.Add(new LoadAttemptedPlugin
                    {
                        Name = plugin.Metadata.Name,
                        Guid = plugin.Metadata.Guid,
                        PluginType = AppPlugins.PluginType.ServiceEndpoint,
                        Publisher = plugin.Metadata.Publisher,
                        Website = plugin.Metadata.Website,
                        UpdateEndpoint = plugin.Metadata.UpdateEndpoint,
                        Version = new Version(plugin.Metadata.Version),
                        Error = $"{e.Message}\n\n{e.StackTrace}",
                        Status = AppPlugins.PluginLoadError.Other
                    });
                }
            }

            // Check if we have enough plugins to run the app
            if (AppPlugins.ServiceEndpointsList.Count < 1)
            {
                Logger.Fatal(new CompositionException("No plugins (service endpoints) loaded! Shutting down..."));
                Interfacing.Fail(Interfacing.LocalizedJsonString("/CrashHandler/Content/Crash/NoServices"));
            }

            Logger.Info("Registration of service endpoint plugins has ended, there are " +
                        $"[{AppPlugins.ServiceEndpointsList.Count}] valid plugins in total.");

            AppPlugins.ServiceEndpointsList.ToList().ForEach(x => Logger.Info(
                $"Loaded a valid service endpoint ({{{x.Key}}}, \"{x.Value.Name}\")"));
        }
        catch (CompositionException e)
        {
            Crashes.TrackError(e); // Other, outer MEF builder exception
            Logger.Error("Loading plugins failed with a global MEF exception: " +
                         $"Message: {e.Message}\nErrors occurred: {e.Errors}\n" +
                         $"Possible causes: {e.RootCauses}\nTrace: {e.StackTrace}");
        }

        // Try reading the default config
        Logger.Info("Checking out the default configuration settings...");
        (string DeviceGuid, string ServiceGuid) defaultSettings = (null, null); // Invalid!

        if (File.Exists(Path.Join(Interfacing.ProgramLocation.DirectoryName, "defaults.json")))
            try
            {
                // Parse the loaded json
                var jsonHead = JsonObject.Parse(File.ReadAllText(
                    Path.Join(Interfacing.ProgramLocation.DirectoryName, "defaults.json")));

                // Check the device guid
                if (!jsonHead.ContainsKey("TrackingDevice"))
                    // Invalid configuration file, don't proceed further!
                    Logger.Error("The default configuration json file was (partially) invalid!");
                else
                    defaultSettings = (
                        jsonHead.GetNamedString("TrackingDevice"),
                        defaultSettings.ServiceGuid); // Keep the last one

                // Check the service guid
                if (!jsonHead.ContainsKey("ServiceEndpoint"))
                    // Invalid configuration file, don't proceed further!
                    Logger.Error("The default configuration json file was (partially) invalid!");
                else
                    defaultSettings = (
                        defaultSettings.DeviceGuid, // Keep the last one
                        jsonHead.GetNamedString("ServiceEndpoint"));
            }
            catch (Exception e)
            {
                Logger.Info($"Default settings checkout failed! Message: {e.Message}");
            }
        else Logger.Info("No default configuration found! [defaults.json]");

        // Validate the saved base plugin guid
        Logger.Info("Checking if the saved base device exists in loaded plugins...");
        if (!AppPlugins.TrackingDevicesList.ContainsKey(AppData.Settings.TrackingDeviceGuid))
        {
            // Check against the defaults
            var firstValidDevice = string.IsNullOrEmpty(defaultSettings.DeviceGuid)
                ? null // Default to null for an empty default guid passed
                : AppPlugins.GetDevice(defaultSettings.DeviceGuid).Device;

            // Check the device now
            if (firstValidDevice is null || firstValidDevice.TrackedJoints.Count <= 0)
            {
                Logger.Warn($"The requested default tracking device ({defaultSettings.DeviceGuid}) " +
                            "was invalid! Searching for any non-disabled suitable device now...");

                // Find the first device that provides any joints
                firstValidDevice = AppPlugins.TrackingDevicesList.Values
                    .FirstOrDefault(device => device.TrackedJoints.Count > 0, null);
            }

            // Check if we've found such a device
            if (firstValidDevice is null)
            {
                Logger.Fatal(new CompositionException("No plugins valid (tracking devices) which provide " +
                                                      "at lest 1 tracked joint have been found! Shutting down..."));
                Interfacing.Fail(Interfacing.LocalizedJsonString("/CrashHandler/Content/Crash/NoJoints"));
            }

            Logger.Info($"The saved base device ({AppData.Settings.TrackingDeviceGuid}) is invalid! " +
                        $"Resetting it to the first valid tracking device: ({firstValidDevice.Guid})!");
            AppData.Settings.TrackingDeviceGuid = firstValidDevice.Guid;
        }

        Logger.Info("Updating app settings for the selected base device...");

        // Initialize the loaded base device now
        Logger.Info("Initializing the selected base device...");
        AppPlugins.BaseTrackingDevice.Initialize();

        // Validate and initialize the loaded override devices now
        Logger.Info("Checking if saved override devices exist in loaded plugins...");
        AppData.Settings.OverrideDevicesGuidMap.RemoveWhere(overrideGuid =>
        {
            Logger.Info($"Checking if override ({overrideGuid}) exists in loaded plugins...");
            if (!AppPlugins.TrackingDevicesList.ContainsKey(overrideGuid))
            {
                // This override guid is invalid or missing
                Logger.Info($"The saved override device ({overrideGuid}) is invalid! Resetting it to NONE!");
                return true; // Remove this invalid override!
            }

            Logger.Info($"Checking if override ({overrideGuid}) doesn't derive from the base device...");
            if (AppData.Settings.TrackingDeviceGuid == overrideGuid)
            {
                // Already being used as the base device
                Logger.Info($"({overrideGuid}) is already set as Base! Resetting it to NONE!");
                return true; // Remove this invalid override!
            }

            // Still here? We must be fine then, initialize the device
            Logger.Info($"Initializing the selected override device ({overrideGuid})...");
            AppPlugins.GetDevice(overrideGuid).Device.Initialize();
            return false; // This override is OK, let's keep it
        });

        foreach (var overrideGuid in AppData.Settings.OverrideDevicesGuidMap)
        {
            Logger.Info($"Checking if override ({overrideGuid}) exists in loaded plugins...");
            if (!AppPlugins.TrackingDevicesList.ContainsKey(overrideGuid))
            {
                // This override guid is invalid or missing
                Logger.Info($"The saved override device ({overrideGuid}) is invalid! Resetting it to NONE!");
                AppData.Settings.OverrideDevicesGuidMap.Remove(overrideGuid);
                continue; // Skip this one then...
            }

            Logger.Info($"Checking if override ({overrideGuid}) doesn't derive from the base device...");
            if (AppData.Settings.TrackingDeviceGuid == overrideGuid)
            {
                // Already being used as the base device
                Logger.Info($"({overrideGuid}) is already set as Base! Resetting it to NONE!");
                AppData.Settings.OverrideDevicesGuidMap.Remove(overrideGuid);
                continue; // Skip this one then...
            }

            // Still here? We must be fine then, initialize the device
            Logger.Info($"Initializing the selected override device ({overrideGuid})...");
            AppPlugins.GetDevice(overrideGuid).Device.Initialize();
        }

        // Validate the saved service plugin guid
        Logger.Info("Checking if the saved service endpoint exists in loaded plugins...");
        if (!AppPlugins.ServiceEndpointsList.ContainsKey(AppData.Settings.ServiceEndpointGuid))
        {
            if (!string.IsNullOrEmpty(defaultSettings.ServiceGuid) && // Check the guid first
                AppPlugins.ServiceEndpointsList.ContainsKey(defaultSettings.ServiceGuid))
            {
                Logger.Info($"The selected service endpoint ({AppData.Settings.ServiceEndpointGuid}) is invalid! " +
                            $"Resetting it to the default one selected in defaults: ({defaultSettings.ServiceGuid})!");
                AppData.Settings.ServiceEndpointGuid = defaultSettings.ServiceGuid;
            }
            else
            {
                Logger.Info($"The default service endpoint ({AppData.Settings.ServiceEndpointGuid}) is invalid! " +
                            $"Resetting it to the first one: ({AppPlugins.ServiceEndpointsList.First().Key})!");
                AppData.Settings.ServiceEndpointGuid = AppPlugins.ServiceEndpointsList.First().Key;
            }
        }

        // Priority: Connect to the tracking service
        Logger.Info("Initializing the selected service endpoint...");
        AppPlugins.CurrentServiceEndpoint.Initialize();

        Logger.Info("Checking the selected service endpoint...");
        Interfacing.ServiceEndpointSetup();

        Logger.Info("Checking application settings config for loaded plugins...");
        AppData.Settings.CheckSettings();

        // Second check and try after 3 seconds
        _ = Task.Run(() =>
        {
            // The Base device
            if (!AppPlugins.BaseTrackingDevice.IsInitialized)
                AppPlugins.BaseTrackingDevice.Initialize();

            // All valid override devices
            AppData.Settings.OverrideDevicesGuidMap
                .Where(x => !AppPlugins.GetDevice(x).Device.IsInitialized).ToList()
                .ForEach(device => AppPlugins.GetDevice(device).Device.Initialize());
        });

        Logger.Info("Pushing control pages the global collection...");
        Shared.Main.Pages = new List<(string Tag, Type Page)>
        {
            ("general", typeof(General)),
            ("settings", typeof(Settings)),
            ("devices", typeof(Devices)),
            ("info", typeof(Info)),
            ("plugins", typeof(Pages.Plugins))
        };

        Logger.Info($"Setting up shared events for '{GetType().FullName}'...");
        RequestUpdateEvent += (_, _) => DownloadUpdates();

        Logger.Info("Registering a detached binary semaphore " +
                    $"reload handler for '{GetType().FullName}'...");

        // Reload watchdog
        _ = Task.Run(() =>
        {
            Shared.Events.ReloadMainWindowEvent =
                new ManualResetEvent(false);

            while (true)
            {
                // Wait for a reload signal (blocking)
                Shared.Events.ReloadMainWindowEvent.WaitOne();

                // Reload & restart the waiting loop
                if (_mainPageLoadedOnce)
                    Shared.Main.DispatcherQueue.TryEnqueue(MainGrid_LoadedHandler);

                // Rebuild devices' settings
                // (Trick the device into rebuilding its interface)
                Shared.Main.DispatcherQueue.TryEnqueue(() =>
                    AppPlugins.TrackingDevicesList.Values.ToList()
                        .ForEach(plugin => plugin.OnLoad()));

                // Reset the event
                Shared.Events.ReloadMainWindowEvent.Reset();
            }
        });

        // Refresh watchdog
        _ = Task.Run(() =>
        {
            Shared.Events.RefreshMainWindowEvent =
                new ManualResetEvent(false);

            while (true)
            {
                // Wait for a reload signal (blocking)
                Shared.Events.RefreshMainWindowEvent.WaitOne();

                // Reload & restart the waiting loop
                if (_mainPageLoadedOnce)
                    Shared.Main.DispatcherQueue.TryEnqueue(() => OnPropertyChanged());

                // Reset the event
                Shared.Events.RefreshMainWindowEvent.Reset();
            }
        });

        // Update the UI
        AppPlugins.UpdateTrackingDevicesInterface();

        // Log our used device to the telemetry module
        Analytics.TrackEvent("TrackingDevice", new Dictionary<string, string>
            { { "Guid", AppData.Settings.TrackingDeviceGuid } });

        // Log our used service to the telemetry module
        Analytics.TrackEvent("ServiceEndpoint", new Dictionary<string, string>
            { { "Guid", AppData.Settings.ServiceEndpointGuid } });

        // Log our used overrides to the telemetry module
        Analytics.TrackEvent("OverrideDevices", new Dictionary<string, string>(
            AppData.Settings.OverrideDevicesGuidMap
                .Select(x => new KeyValuePair<string, string>(
                    $"Guid{AppData.Settings.OverrideDevicesGuidMap.ToList().IndexOf(x)}", x))));

        // Setup device change watchdog : local devices
        var localWatcher = new FileSystemWatcher
        {
            Path = Path.Combine(Interfacing.ProgramLocation.DirectoryName, "Plugins"),
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
            Filter = "*.dll", IncludeSubdirectories = true, EnableRaisingEvents = true
        };

        // Setup device change watchdog : external devices
        var externalWatcher = new FileSystemWatcher
        {
            Path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Amethyst"),
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.Size,
            Filter = "amethystpaths.k2path", IncludeSubdirectories = true, EnableRaisingEvents = true
        };

        // Send a non-dismissible tip about reloading the app
        static void OnWatcherOnChanged(object o, FileSystemEventArgs fileSystemEventArgs)
        {
            Shared.Main.DispatcherQueue.TryEnqueue(() =>
            {
                Logger.Info("Device plugin tree has changed, you need to reload!");
                Logger.Info($"What happened: {fileSystemEventArgs.ChangeType}");
                Logger.Info($"Where: {fileSystemEventArgs.FullPath} ({fileSystemEventArgs.Name})");

                Shared.TeachingTips.MainPage.ReloadInfoBar.IsOpen = true;
                Shared.TeachingTips.MainPage.ReloadInfoBar.Opacity = 1.0;
            });
        }

        // Add event handlers : local
        localWatcher.Changed += OnWatcherOnChanged;
        localWatcher.Created += OnWatcherOnChanged;
        localWatcher.Deleted += OnWatcherOnChanged;
        localWatcher.Renamed += OnWatcherOnChanged;

        // Add event handlers : external
        externalWatcher.Changed += OnWatcherOnChanged;
        externalWatcher.Created += OnWatcherOnChanged;
        externalWatcher.Deleted += OnWatcherOnChanged;
        externalWatcher.Renamed += OnWatcherOnChanged;

        // Check settings once again and save
        AppSettings.DoNotSaveSettings = false;
        AppData.Settings.CheckSettings();
        AppData.Settings.SaveSettings();

        // Notify of the setup end
        _mainPageInitFinished = true;
        Shared.Main.MainWindowLoaded = true;
    }

    [Export(typeof(IAmethystHost))] private IAmethystHost AmethystPluginHost { get; set; }

    private bool CanShowPluginsUpdatePendingBar => !PluginsUpdateInfoBar.IsOpen;

    private bool CanShowUpdateBar =>
        !PluginsUpdatePendingInfoBar.IsOpen && !PluginsUpdateInfoBar.IsOpen;

    private bool CanShowPluginsInstallBar =>
        !UpdateInfoBar.IsOpen && !PluginsUpdatePendingInfoBar.IsOpen && !PluginsUpdateInfoBar.IsOpen;

    private bool CanShowPluginsUninstallBar =>
        !PluginsInstallInfoBar.IsOpen && !UpdateInfoBar.IsOpen &&
        !PluginsUpdatePendingInfoBar.IsOpen && !PluginsUpdateInfoBar.IsOpen;

    private bool CanShowReloadBar =>
        !PluginsUninstallInfoBar.IsOpen && !PluginsInstallInfoBar.IsOpen && !UpdateInfoBar.IsOpen &&
        !PluginsUpdatePendingInfoBar.IsOpen && !PluginsUpdateInfoBar.IsOpen;

    // MVVM stuff
    public event PropertyChangedEventHandler PropertyChanged;

    public double BoolToOpacity(bool v)
    {
        return v ? 1.0 : 0.0;
    }

    private void MainGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_mainPageInitFinished) return; // Don't care

        // Load theme config
        Shared.Main.MainNavigationView.XamlRoot.Content.As<Grid>().RequestedTheme =
            AppData.Settings.AppTheme switch
            {
                2 => ElementTheme.Light,
                1 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

        // Execute the handler
        MainGrid_LoadedHandler();

        // Register a theme watchdog
        NavView.XamlRoot.Content.As<Grid>().ActualThemeChanged += MainWindow_ActualThemeChanged;

        // Mark as loaded
        _mainPageLoadedOnce = true;
    }

    private void MainWindow_ActualThemeChanged(FrameworkElement sender, object args)
    {
        Interfacing.ActualTheme = NavView.ActualTheme;

        Shared.Main.AttentionBrush =
            Interfacing.ActualTheme == ElementTheme.Dark
                ? Application.Current.Resources["AttentionBrush_Dark"].As<SolidColorBrush>()
                : Application.Current.Resources["AttentionBrush_Light"].As<SolidColorBrush>();

        Shared.Main.NeutralBrush =
            Interfacing.ActualTheme == ElementTheme.Dark
                ? Application.Current.Resources["NeutralBrush_Dark"].As<SolidColorBrush>()
                : Application.Current.Resources["NeutralBrush_Light"].As<SolidColorBrush>();

        Application.Current.Resources["WindowCaptionForeground"] =
            Interfacing.ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;

        // Overwrite titlebar colors
        Shared.Main.AppWindow.TitleBar.ButtonForegroundColor =
            Interfacing.ActualTheme == ElementTheme.Dark
                ? Colors.White
                : Colors.Black;

        Shared.Main.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        Shared.Main.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        Shared.Main.AppWindow.TitleBar.ButtonHoverBackgroundColor =
            Shared.Main.AppWindow.TitleBar.ButtonPressedBackgroundColor;

        // Refresh other stuff
        _updateBrushesEvent?.Invoke(this, EventArgs.Empty);

        // Request page reloads
        Shared.Events.RequestInterfaceReload();

        OnPropertyChanged(); // Reload all
        ReloadNavigationIcons();
    }

    private void MainGrid_LoadedHandler()
    {
        OnPropertyChanged(); // All
        ReloadNavigationIcons();
    }

    private void ReloadNavigationIcons()
    {
        if (Interfacing.CurrentPageClass == "Amethyst.Pages.General")
        {
            Shared.Main.NavigationItems.NavViewGeneralButtonIcon.Symbol = FluentSymbol.Home24Filled;
            Shared.Main.NavigationItems.NavViewGeneralButtonIcon.Foreground = Shared.Main.AttentionBrush;

            Shared.Main.NavigationItems.NavViewGeneralButtonLabel.Opacity = 0.0;
            Shared.Main.NavigationItems.NavViewGeneralButtonIcon.Translation = Vector3.Zero;
        }
        else
        {
            Shared.Main.NavigationItems.NavViewGeneralButtonIcon.Translation = new Vector3(0, -8, 0);
            Shared.Main.NavigationItems.NavViewGeneralButtonLabel.Opacity = 1.0;

            Shared.Main.NavigationItems.NavViewGeneralButtonIcon.Foreground = Shared.Main.NeutralBrush;
            Shared.Main.NavigationItems.NavViewGeneralButtonIcon.Symbol = FluentSymbol.Home24;
        }

        if (Interfacing.CurrentPageClass == "Amethyst.Pages.Settings")
        {
            Shared.Main.NavigationItems.NavViewSettingsButtonIcon.Symbol = FluentSymbol.Settings24Filled;
            Shared.Main.NavigationItems.NavViewSettingsButtonIcon.Foreground = Shared.Main.AttentionBrush;

            Shared.Main.NavigationItems.NavViewSettingsButtonLabel.Opacity = 0.0;
            Shared.Main.NavigationItems.NavViewSettingsButtonIcon.Translation = Vector3.Zero;
        }
        else
        {
            Shared.Main.NavigationItems.NavViewSettingsButtonIcon.Translation = new Vector3(0, -8, 0);
            Shared.Main.NavigationItems.NavViewSettingsButtonLabel.Opacity = 1.0;

            Shared.Main.NavigationItems.NavViewSettingsButtonIcon.Foreground = Shared.Main.NeutralBrush;
            Shared.Main.NavigationItems.NavViewSettingsButtonIcon.Symbol = FluentSymbol.Settings24;
        }

        if (Interfacing.CurrentPageClass == "Amethyst.Pages.Devices")
        {
            Shared.Main.NavigationItems.NavViewDevicesButtonLabel.Opacity = 0.0;
            Shared.Main.NavigationItems.NavViewDevicesButtonIcon.Translation = Vector3.Zero;

            Shared.Main.NavigationItems.NavViewDevicesButtonIcon.Foreground = Shared.Main.AttentionBrush;
            Shared.Main.NavigationItems.NavViewDevicesButtonIcon.Symbol = FluentSymbol.PlugConnected24Filled;
        }
        else
        {
            Shared.Main.NavigationItems.NavViewDevicesButtonIcon.Translation = new Vector3(0, -8, 0);
            Shared.Main.NavigationItems.NavViewDevicesButtonLabel.Opacity = 1.0;

            Shared.Main.NavigationItems.NavViewDevicesButtonIcon.Foreground = Shared.Main.NeutralBrush;
            Shared.Main.NavigationItems.NavViewDevicesButtonIcon.Symbol = FluentSymbol.PlugDisconnected24;
        }

        if (Interfacing.CurrentPageClass == "Amethyst.Pages.Info")
        {
            Shared.Main.NavigationItems.NavViewInfoButtonIcon.Symbol = FluentSymbol.Info24Filled;
            Shared.Main.NavigationItems.NavViewInfoButtonIcon.Foreground = Shared.Main.AttentionBrush;

            Shared.Main.NavigationItems.NavViewInfoButtonLabel.Opacity = 0.0;
            Shared.Main.NavigationItems.NavViewInfoButtonIcon.Translation = Vector3.Zero;
        }
        else
        {
            Shared.Main.NavigationItems.NavViewInfoButtonIcon.Translation = new Vector3(0, -8, 0);
            Shared.Main.NavigationItems.NavViewInfoButtonLabel.Opacity = 1.0;

            Shared.Main.NavigationItems.NavViewInfoButtonIcon.Foreground = Shared.Main.NeutralBrush;
            Shared.Main.NavigationItems.NavViewInfoButtonIcon.Symbol = FluentSymbol.Info24;
        }

        if (Interfacing.CurrentPageClass == "Amethyst.Pages.Plugins")
        {
            Shared.Main.NavigationItems.NavViewPluginsButtonLabel.Opacity = 0.0;
            Shared.Main.NavigationItems.NavViewPluginsButtonIcon.Translation = Vector3.Zero;

            Shared.Main.NavigationItems.NavViewPluginsButtonIcon.Foreground = Shared.Main.AttentionBrush;
            Shared.Main.NavigationItems.NavViewPluginsButtonIcon.Symbol = FluentSymbol.Puzzlepiece24Filled;
        }
        else
        {
            Shared.Main.NavigationItems.NavViewPluginsButtonIcon.Translation = new Vector3(0, -8, 0);
            Shared.Main.NavigationItems.NavViewPluginsButtonLabel.Opacity = 1.0;

            Shared.Main.NavigationItems.NavViewPluginsButtonIcon.Foreground = Shared.Main.NeutralBrush;
            Shared.Main.NavigationItems.NavViewPluginsButtonIcon.Symbol = FluentSymbol.Puzzlepiece24;
        }

        HelpIcon.Foreground = Shared.Main.NeutralBrush;
        HelpIcon.Symbol = FluentSymbol.QuestionCircle24;
    }

    private bool InstallUpdates(bool reopen = false)
    {
        try
        {
            // Optional auto-restart scenario "-o" (happens when the user clicks 'update now')
            Process.Start(new ProcessStartInfo
            {
                UseShellExecute = true,
                Verb = "runas",
                FileName = Path.Combine(Interfacing.AppDataTempDir.FullName, "Amethyst-Installer.exe"),
                Arguments =
                    $"--update {(reopen ? "-o" : "")} -path \"{Interfacing.ProgramLocation.DirectoryName}\""
            });
        }
        catch (Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223)
                Logger.Warn($"You need to pass UAC to run the installer! Message: {ex.Message}");
            return false; // Keep the update banner available
        }
        catch (Exception ex)
        {
            Logger.Error(new Exception($"Update failed, an exception occurred. Message: {ex.Message}"));
            return false; // Keep the update banner available
        }

        // Exit, cleanup should be automatic
        Interfacing.ManualUpdate = true;
        Environment.Exit(0);
        return true; // Will exit before
    }

    private async Task CheckUpdates(uint delay = 0)
    {
        // Attempt only after init
        if (!_mainPageInitFinished) return;

        // Check if we're midway updating
        if (Interfacing.UpdatingNow)
            return; // Don't proceed further

        await Task.Delay((int)delay);

        // Don't check if found
        if (!Interfacing.UpdateFound)
        {
            // Check now
            Interfacing.UpdateFound = false;

            // Check for updates
            try
            {
                using var client = new HttpClient();
                string getReleaseVersion = "", getDocsLanguages = "en";

                // Release
                try
                {
                    Logger.Info("Checking for updates... [GET]");
                    using var response =
                        await client.GetAsync(new Uri($"https://api.k2vr.tech/v{AppData.ApiVersion}/update"));
                    getReleaseVersion = await response.Content.ReadAsStringAsync();
                }
                catch (Exception e)
                {
                    Logger.Error(new Exception($"Error getting the release info! Message: {e.Message}"));
                }

                // Language
                try
                {
                    Logger.Info("Checking available languages... [GET]");
                    using var response =
                        await client.GetAsync(new Uri("https://docs.k2vr.tech/shared/locales.json"));
                    getDocsLanguages = await response.Content.ReadAsStringAsync();
                }
                catch (Exception e)
                {
                    Logger.Error(new Exception($"Error getting the language info! Message: {e.Message}"));
                }

                // If the read string isn't empty, proceed to checking for updates
                if (!string.IsNullOrEmpty(getReleaseVersion))
                {
                    Logger.Info($"Update-check successful, string:\n{getReleaseVersion}");

                    // Parse the loaded json
                    var jsonHead = JsonObject.Parse(getReleaseVersion);

                    if (!jsonHead.ContainsKey("amethyst"))
                        Logger.Error(new InvalidDataException("The latest release's manifest was invalid!"));

                    // Parse the amethyst entry
                    var jsonRoot = jsonHead.GetNamedObject("amethyst");

                    if (!jsonRoot.ContainsKey("version") ||
                        !jsonRoot.ContainsKey("version_string"))
                    {
                        Logger.Error("The latest release's manifest was invalid!");
                    }

                    else
                    {
                        // Get the version tag (uint, fallback to latest)
                        var remoteVersion = (int)jsonRoot.GetNamedNumber("version", AppData.InternalVersion);

                        // Get the remote version name
                        _remoteVersionString = jsonRoot.GetNamedString("version_string");

                        Logger.Info($"Local version: {AppData.InternalVersion}");
                        Logger.Info($"Remote version: {remoteVersion}");

                        Logger.Info($"Local version string: {AppData.VersionString.Display}");
                        Logger.Info($"Remote version string: {_remoteVersionString}");

                        // Check the version
                        if (AppData.InternalVersion < remoteVersion)
                            Interfacing.UpdateFound = true;
                    }
                }
                else
                {
                    Logger.Error(new NoNullAllowedException("Update-check failed, the string was empty."));
                }

                if (!string.IsNullOrEmpty(getDocsLanguages))
                {
                    // Parse the loaded json
                    var jsonRoot = JsonObject.Parse(getDocsLanguages);

                    // Check if the resource root is fine & the language code exists
                    Interfacing.DocsLanguageCode = AppData.Settings.AppLanguage;

                    if (jsonRoot is null || !jsonRoot.ContainsKey(Interfacing.DocsLanguageCode))
                    {
                        Logger.Info("Docs do not contain a language with code " +
                                    $"\"{Interfacing.DocsLanguageCode}\", falling back to \"en\" (English)!");
                        Interfacing.DocsLanguageCode = "en";
                    }
                }
                else
                {
                    Logger.Error(new NoNullAllowedException("Language-check failed, the string was empty."));
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Update failed, an exception occurred. Message: {e.Message}");
            }

            // Download the update and show the notice
            if (Interfacing.UpdateFound)
                await DownloadUpdates();
        }

        // Check for plugin updates
        await Parallel.ForEachAsync(AppPlugins.LoadAttemptedPluginsList,
            async (x, _) => await x.CheckUpdates());
    }

    private async Task DownloadUpdates()
    {
        Interfacing.UpdatingNow = true;
        await Task.Delay(500);

        // Success? ...or nah?
        var updateError = false;

        // Download the latest installer/updater
        // https://github.com/KinectToVR/Amethyst-Installer-Releases/releases/latest/download/Amethyst-Installer.exe
        try
        {
            using var client = new RestClient();
            var installerUri = "";

            Logger.Info("Checking out the updater version... [GET]");

            // Get the installer download Uri
            try
            {
                var response = await client.ExecuteGetAsync(
                    "https://api.k2vr.tech/v0/installer/version", new RestRequest());

                if (!string.IsNullOrEmpty(response.Content))
                {
                    var jsonRoot = JsonObject.Parse(response.Content);

                    // Parse the loaded json
                    if (jsonRoot.ContainsKey("download"))
                    {
                        installerUri = jsonRoot.GetNamedString("download");
                    }
                    else
                    {
                        Logger.Error(new KeyNotFoundException(
                            "Installer-uri-check failed, the \"download\" key wasn't found."));
                        updateError = true; // Skill issue
                    }
                }
                else
                {
                    Logger.Error(new NoNullAllowedException("Installer-uri-check failed, the string was empty."));
                    updateError = true; // What happened?
                }
            }
            catch (Exception e)
            {
                Logger.Error(new Exception($"Error checking the updater download Uri! Message: {e.Message}"));
                updateError = true; // Something else
            }

            // Download if we're ok
            if (!updateError)
                try
                {
                    // Create a stream reader using the received Installer Uri
                    await using var stream = await client.ExecuteDownloadStreamAsync(installerUri, new RestRequest());

                    var installerFile = // Replace or create our installer file
                        await (await StorageFolder.GetFolderFromPathAsync(Interfacing.AppDataTempDir.FullName))
                            .CreateFileAsync("Amethyst-Installer.exe", CreationCollisionOption.ReplaceExisting);

                    // Create an output stream and push all the available data to it
                    await using var fsInstallerFile = await installerFile.OpenStreamForWriteAsync();
                    await stream.CopyToAsync(fsInstallerFile); // The runtime will do the rest for us
                }
                catch (Exception e)
                {
                    Logger.Error(new Exception($"Error downloading the updater! Message: {e.Message}"));
                    updateError = true;
                }
        }
        catch (Exception e)
        {
            Logger.Error(new Exception($"Update failed, an exception occurred. Message: {e.Message}"));
            updateError = true;
        }

        // Check the file result and the DL result
        if (updateError) return;

        // If found and downloaded properly
        UpdateInfoBar.Message = string.Format(Interfacing.LocalizedJsonString(
            "/SharedStrings/Updates/NewUpdateMessage"), _remoteVersionString);

        UpdateInfoBar.IsOpen = true;
        UpdateInfoBar.Opacity = 1.0;
        OnPropertyChanged(); // Visibility

        // Enqueue an update task to be run at shutdown
        ShutdownController.ShutdownTasks.Add(new ShutdownTask
        {
            Name = "Update Amethyst using the Installer",
            Priority = false, // First download plugin updates, then everything else
            Action = () => Task.FromResult(Interfacing.ManualUpdate || InstallUpdates())
        });
    }

    private void InitializerTeachingTip_ActionButtonClick(TeachingTip sender, object args)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);

        // Dismiss the current tip
        Shared.TeachingTips.MainPage.InitializerTeachingTip.IsOpen = false;

        // Just dismiss the tip
        Shared.Main.InterfaceBlockerGrid.Opacity = 0.0;
        Shared.Main.InterfaceBlockerGrid.IsHitTestVisible = false;
        Interfacing.IsNuxPending = false;
    }

    private void InitializerTeachingTip_CloseButtonClick(TeachingTip sender, object args)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);

        // Dismiss the current tip
        Shared.TeachingTips.MainPage.InitializerTeachingTip.IsOpen = false;

        // Navigate to the general page
        Shared.Main.MainNavigationView.SelectedItem =
            Shared.Main.MainNavigationView.MenuItems[0];

        Shared.Main.NavigateToPage("general",
            new EntranceNavigationTransitionInfo());

        // Show the next tip (general page)
        Shared.TeachingTips.GeneralPage.ToggleTrackersTeachingTip.TailVisibility = TeachingTipTailVisibility.Collapsed;
        Shared.TeachingTips.GeneralPage.ToggleTrackersTeachingTip.IsOpen = true;
    }

    private async void ReloadInfoBar_CloseButtonClick(object sender, RoutedEventArgs args)
    {
        Logger.Info("Reload has been invoked: turning trackers off...");

        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);

        // Mark trackers as inactive
        Interfacing.AppTrackersInitialized = false;
        if (Shared.General.ToggleTrackersButton is not null)
            Shared.General.ToggleTrackersButton.IsChecked = false;

        Logger.Info("Reload has been invoked: setting up exit flags...");

        // Mark exiting as true
        Interfacing.IsExitingNow = true;
        await Task.Delay(50);

        /* Restart app */

        // Literals
        Logger.Info("Reload has been invoked: trying to restart the app...");

        // If we've found who asked
        if (File.Exists(Interfacing.ProgramLocation.ToString()))
        {
            // Log the caller
            Logger.Info($"The current caller process is: {Interfacing.ProgramLocation}");

            // Exit the app
            Logger.Info("Exiting in 500ms...");

            // Don't execute the exit routine
            Interfacing.IsExitHandled = true;

            // Handle a typical app exit
            await Interfacing.HandleAppExit(500);

            // Restart and exit with code 0
            Process.Start(Interfacing.ProgramLocation
                .FullName.Replace(".dll", ".exe"));

            // Exit without re-handling everything
            Environment.Exit(0);
        }

        // Still here?
        Logger.Fatal(new InvalidDataException("App will not be restarted due to caller process identification error."));

        Interfacing.ShowToast(
            Interfacing.LocalizedJsonString("/SharedStrings/Toasts/RestartFailed/Title"),
            Interfacing.LocalizedJsonString("/SharedStrings/Toasts/RestartFailed"));

        Interfacing.ShowServiceToast(
            Interfacing.LocalizedJsonString("/SharedStrings/Toasts/RestartFailed/Title"),
            Interfacing.LocalizedJsonString("/SharedStrings/Toasts/RestartFailed"));
    }

    private async void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_mainPageInitFinished) return; // Don't care
        Interfacing.ActualTheme = NavView.ActualTheme;

        Shared.Main.AppWindow.TitleBar.ButtonForegroundColor =
            Interfacing.ActualTheme == ElementTheme.Dark
                ? Colors.White
                : Colors.Black;

        Shared.Main.AttentionBrush =
            Interfacing.ActualTheme == ElementTheme.Dark
                ? Application.Current.Resources["AttentionBrush_Dark"].As<SolidColorBrush>()
                : Application.Current.Resources["AttentionBrush_Light"].As<SolidColorBrush>();

        Shared.Main.NeutralBrush =
            Interfacing.ActualTheme == ElementTheme.Dark
                ? Application.Current.Resources["NeutralBrush_Dark"].As<SolidColorBrush>()
                : Application.Current.Resources["NeutralBrush_Light"].As<SolidColorBrush>();

        // NavView doesn't load any page by default, so load home page
        NavView.SelectedItem = NavView.MenuItems[0];

        // If navigation occurs on SelectionChanged, then this isn't needed.
        // Because we use ItemInvoked to navigate, we need to call Navigate
        // here to load the home page.
        Shared.Main.NavigateToPage("general", new EntranceNavigationTransitionInfo());

        // Show the startup tour teachingtip
        if (!AppData.Settings.FirstTimeTourShown)
        {
            // Play a sound
            AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);

            // Show the first tip
            Shared.Main.InterfaceBlockerGrid.Opacity = 0.35;
            Shared.Main.InterfaceBlockerGrid.IsHitTestVisible = true;

            Shared.TeachingTips.MainPage.InitializerTeachingTip.IsOpen = true;
            Interfacing.IsNuxPending = true;
        }

        // Check for updates (and show)
        await CheckUpdates(2000);
    }

    private void NavView_ItemInvoked(NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        Shared.Main.NavigateToPage(
            args.InvokedItemContainer.Tag.ToString(),
            new EntranceNavigationTransitionInfo());
    }

    private void NavView_BackRequested(NavigationView sender,
        NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack && (!NavView.IsPaneOpen || NavView.DisplayMode is not
                (NavigationViewDisplayMode.Compact or NavigationViewDisplayMode.Minimal)))
            ContentFrame.GoBack();
    }

    private void HelpButton_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Change the docs button's text
        HelpFlyoutDocsButton.Text = Interfacing.CurrentAppState switch
        {
            "general" => Interfacing.LocalizedJsonString(
                "/SharedStrings/Buttons/Help/Docs/GeneralPage/Overview"),
            "calibration" => Interfacing.LocalizedJsonString(
                "/SharedStrings/Buttons/Help/Docs/GeneralPage/Calibration/Main"),
            "calibration_auto" => Interfacing.LocalizedJsonString(
                "/SharedStrings/Buttons/Help/Docs/GeneralPage/Calibration/Automatic"),
            "calibration_manual" => Interfacing.LocalizedJsonString(
                "/SharedStrings/Buttons/Help/Docs/GeneralPage/Calibration/Manual"),
            "offsets" => Interfacing.LocalizedJsonString(
                "/SharedStrings/Buttons/Help/Docs/GeneralPage/Offsets"),
            "settings" => Interfacing.LocalizedJsonString(
                "/SharedStrings/Buttons/Help/Docs/SettingsPage/Overview"),
            "devices" => Interfacing.LocalizedJsonString(
                "/SharedStrings/Buttons/Help/Docs/DevicesPage/Overview"),
            "overrides" => Interfacing.LocalizedJsonString(
                "/SharedStrings/Buttons/Help/Docs/DevicesPage/Overrides"),
            "info" or _ => Interfacing.LocalizedJsonString(
                "/SharedStrings/Buttons/Help/Docs/InfoPage/OpenCollective")
        };

        // Show the help flyout
        HelpFlyout.ShowAt(PluginsItem, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.RightEdgeAlignedBottom,
            ShowMode = FlyoutShowMode.Transient
        });
    }

    private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new Exception($"Failed to load page{e.SourcePageType.AssemblyQualifiedName}");
    }

    private void ButtonFlyout_Opening(object sender, object e)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Show);
    }

    private void ButtonFlyout_Closing(FlyoutBase sender,
        FlyoutBaseClosingEventArgs args)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Hide);
    }

    private void InstallNowButton_Click(object sender, RoutedEventArgs e)
    {
        // Try installing
        InstallUpdates(true);
    }

    private async void PluginsUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        PluginsUpdateInfoBar.IsOpen = false;
        PluginsUpdateInfoBar.Opacity = 0.0;
        OnPropertyChanged(); // Visibility

        // Mark as handled
        Interfacing.IsExitPending = true;

        // Run shutdown tasks
        await ShutdownController.ExecuteAllTasks();

        // Restart Amethyst now
        Logger.Info("Update invoked: trying to restart the app...");
        await Interfacing.ExecuteAppRestart(); // Try restarting ame
    }

    private async void PluginsInstallButton_Click(object sender, RoutedEventArgs e)
    {
        PluginsInstallInfoBar.IsOpen = false;
        PluginsInstallInfoBar.Opacity = 0.0;
        OnPropertyChanged(); // Visibility

        // Mark as handled
        Interfacing.IsExitPending = true;

        // Run shutdown tasks
        await ShutdownController.ExecuteAllTasks();

        // Restart Amethyst now
        Logger.Info("Update invoked: trying to restart the app...");
        await Interfacing.ExecuteAppRestart(); // Try restarting ame
    }

    private async void HelpFlyoutDocsButton_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri(Interfacing.CurrentAppState switch
        {
            "calibration" => $"https://docs.k2vr.tech/{Interfacing.DocsLanguageCode}/calibration/",
            "calibration_auto" => $"https://docs.k2vr.tech/{Interfacing.DocsLanguageCode}/calibration/#3",
            "calibration_manual" => $"https://docs.k2vr.tech/{Interfacing.DocsLanguageCode}/calibration/#6",
            "devices" or "offsets" or "settings" => $"https://docs.k2vr.tech/{Interfacing.DocsLanguageCode}/",
            "overrides" => $"https://docs.k2vr.tech/{Interfacing.DocsLanguageCode}/overrides/",
            "info" => "https://opencollective.com/k2vr",
            "general" or _ => $"https://docs.k2vr.tech/{Interfacing.DocsLanguageCode}/"
        }));
    }

    private async void HelpFlyoutDiscordButton_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri("https://discord.gg/YBQCRDG"));
    }

    private async void HelpFlyoutDevButton_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri($"https://docs.k2vr.tech/{Interfacing.DocsLanguageCode}/dev/overview/"));
    }

    private async void HelpFlyoutLicensesButton_Click(object sender, RoutedEventArgs e)
    {
        await Task.Delay(500);
        LicensesFlyout.ShowAt(MainGrid);
    }

    private void LicensesFlyout_Opening(object sender, object e)
    {
        Shared.Main.InterfaceBlockerGrid.Opacity = 0.35;
        Shared.Main.InterfaceBlockerGrid.IsHitTestVisible = true;

        Interfacing.IsNuxPending = true;

        // Load the license text
        if (File.Exists(Path.Combine(Interfacing.ProgramLocation.DirectoryName, "Assets", "Licenses.txt")))
            LicensesText.Text = File.ReadAllText(Path.Combine(
                Interfacing.ProgramLocation.DirectoryName, "Assets", "Licenses.txt"));

        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Show);
    }

    private void LicensesFlyout_Closed(object sender, object e)
    {
        Shared.Main.InterfaceBlockerGrid.Opacity = 0.0;
        Shared.Main.InterfaceBlockerGrid.IsHitTestVisible = false;

        Interfacing.IsNuxPending = false;
    }

    private void TrySetMicaBackdrop()
    {
        Logger.Info("Searching for supported backdrop systems...");
        if (!MicaController.IsSupported() && !DesktopAcrylicController.IsSupported())
        {
            Logger.Info("Mica and acrylic are not supported! Time to update Windows, man!");
            return; // Mica/acrylic is not supported on this system
        }

        Logger.Info("Creating a new system dispatcher helper...");
        _wsdqHelper = new WindowsSystemDispatcherQueueHelper();

        Logger.Info("Setting up the system dispatcher helper...");
        _wsdqHelper.EnsureWindowsSystemDispatcherQueueController();

        // Hooking up the policy object
        Logger.Info("Hooking up system backdrop policies...");
        _configurationSource = new SystemBackdropConfiguration();

        Logger.Info("Setting up activation and theme handlers...");
        Activated += Window_Activated;
        ((FrameworkElement)Content).ActualThemeChanged += Window_ThemeChanged;

        // Initial configuration state.
        Logger.Info("Initializing the configuration source...");
        _configurationSource.IsInputActive = true;
        SetConfigurationSourceTheme();

        if (MicaController.IsSupported())
        {
            Logger.Info("Creating a new shared mica backdrop controller...");
            _micaController = new MicaController();

            // Enable the system backdrop.
            // Note: Be sure to have "using WinRT;" to support the Window.As<...>() call.
            Logger.Info("Registering the generated backdrop within the controller...");
            _micaController.AddSystemBackdropTarget(this
                .As<ICompositionSupportsSystemBackdrop>());

            Logger.Info("Configuring the backdrop within the controller...");
            _micaController.SetSystemBackdropConfiguration(_configurationSource);

            // Change the window background to support mica
            Logger.Info("Sharing the set up backdrop with the host element...");
            Logger.Info("Clearing the host element background for the backdrop...");
            MainGrid.Background = new SolidColorBrush(Colors.Transparent);
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            Logger.Info("Creating a new shared acrylic backdrop controller...");
            _acrylicController = new DesktopAcrylicController();

            // Enable the system backdrop.
            // Note: Be sure to have "using WinRT;" to support the Window.As<...>() call.
            Logger.Info("Registering the generated backdrop within the controller...");
            _acrylicController.AddSystemBackdropTarget(this
                .As<ICompositionSupportsSystemBackdrop>());

            Logger.Info("Configuring the backdrop within the controller...");
            _acrylicController.SetSystemBackdropConfiguration(_configurationSource);

            // Change the window background to support acrylic
            Logger.Info("Clearing the outer host element background for the backdrop...");
            MainGrid.Background = Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? Application.Current.Resources["AcrylicBrush_Dark"].As<AcrylicBrush>()
                : Application.Current.Resources["AcrylicBrush_Light"].As<AcrylicBrush>();

            Logger.Info("Clearing the host frame element background for the backdrop...");
            ContentFrame.Background = Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? Application.Current.Resources["AcrylicBrush_Darker"].As<AcrylicBrush>()
                : Application.Current.Resources["AcrylicBrush_Lighter"].As<AcrylicBrush>();

            Logger.Info("Registering an update event for brush updates...");
            _updateBrushesEvent += (_, _) =>
            {
                // Change the window background to support acrylic
                Logger.Info("Clearing the outer host element background for the backdrop...");
                MainGrid.Background = Interfacing.ActualTheme == ElementTheme.Dark
                    ? Application.Current.Resources["AcrylicBrush_Dark"].As<AcrylicBrush>()
                    : Application.Current.Resources["AcrylicBrush_Light"].As<AcrylicBrush>();

                Logger.Info("Clearing the host frame element background for the backdrop...");
                ContentFrame.Background = Interfacing.ActualTheme == ElementTheme.Dark
                    ? Application.Current.Resources["AcrylicBrush_Darker"].As<AcrylicBrush>()
                    : Application.Current.Resources["AcrylicBrush_Lighter"].As<AcrylicBrush>();
                return Task.CompletedTask;
            };
        }
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        _configurationSource.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
    }

    private async void Window_Closed(object sender, WindowEventArgs args)
    {
        // Handled(true) means Cancel()
        // and Handled(false) means Continue()
        // -> Block exiting until we're done
        args.Handled = true;

        if (Interfacing.IsExitPending) return;
        switch (Interfacing.IsExitHandled)
        {
            // Show the close tip (if not shown yet)
            case false when
                !AppData.Settings.FirstShutdownTipShown:
                // Bring up the window
                Shared.Main.Window.Activate();

                // Show the tip
                ShutdownTeachingTip.IsOpen = true;

                AppData.Settings.FirstShutdownTipShown = true;
                AppData.Settings.SaveSettings(); // Save settings
                return;

            // Handle all the exit actions (if needed)
            case false:
            {
                // Mark as handled
                Interfacing.IsExitPending = true;

                // Hide the update bar now
                UpdateInfoBar.IsOpen = false;
                UpdateInfoBar.Opacity = 0.0;
                OnPropertyChanged(); // Visibility

                // Run shutdown tasks
                await ShutdownController.ExecuteAllTasks();

                // Mark as mostly done
                Interfacing.IsExitPending = true;

                // Make sure any Mica/Acrylic controller is disposed so it doesn't try to
                // use this closed window.
                if (_micaController is not null)
                {
                    _micaController.Dispose();
                    _micaController = null;
                }

                if (_acrylicController is not null)
                {
                    _acrylicController.Dispose();
                    _acrylicController = null;
                }

                Activated -= Window_Activated;
                _configurationSource = null;
                break;
            }
        }

        try
        {
            // Call before exiting for subsequent invocations to launch a new process
            Shared.Main.NotificationManager?.Unregister();
        }
        catch (Exception)
        {
            // ignored
        }

        // Finally allow exits
        args.Handled = false;
        Environment.Exit(0);
    }

    private void Window_ThemeChanged(FrameworkElement sender, object args)
    {
        if (_configurationSource is not null) SetConfigurationSourceTheme();
    }

    private void SetConfigurationSourceTheme()
    {
        _configurationSource.Theme = ((FrameworkElement)Content).ActualTheme switch
        {
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            ElementTheme.Light => SystemBackdropTheme.Light,
            ElementTheme.Default => SystemBackdropTheme.Default,
            _ => _configurationSource.Theme
        };
    }

    public void OnPropertyChanged(string propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    private void HelpFlyout_Opening(object sender, object e)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Show);

        HelpIcon.Foreground = Shared.Main.AttentionBrush;
        HelpIcon.Symbol = FluentSymbol.QuestionCircle24Filled;
        HelpIconGrid.Translation = Vector3.Zero;
        HelpIconText.Opacity = 0.0;
    }

    private void HelpFlyout_Closing(FlyoutBase sender, FlyoutBaseClosingEventArgs args)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Hide);

        HelpIcon.Foreground = Shared.Main.NeutralBrush;
        HelpIcon.Symbol = FluentSymbol.QuestionCircle24;
        HelpIconGrid.Translation = new Vector3(0, -8, 0);
        HelpIconText.Opacity = 1.0;
    }

    private void InterfaceBlockerGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!InitializerTeachingTip.IsOpen) return;

        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Focus);

        // Dismiss the current tip
        Shared.TeachingTips.MainPage.InitializerTeachingTip.IsOpen = false;

        // Just dismiss the tip
        Shared.Main.InterfaceBlockerGrid.Opacity = 0.0;
        Shared.Main.InterfaceBlockerGrid.IsHitTestVisible = false;
        Interfacing.IsNuxPending = false;

        // We're done
        AppData.Settings.FirstTimeTourShown = true;
        AppData.Settings.SaveSettings();
    }

    private async void EndingTeachingTip_CloseButtonClick(TeachingTip sender, object args)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);

        // Dismiss the current tip
        EndingTeachingTip.IsOpen = false;
        await Task.Delay(200);

        // Unblock the interface
        Shared.Main.InterfaceBlockerGrid.Opacity = 0.0;
        Shared.Main.InterfaceBlockerGrid.IsHitTestVisible = false;
        Interfacing.IsNuxPending = false;

        // We're done
        AppData.Settings.FirstTimeTourShown = true;
        AppData.Settings.SaveSettings();
    }
}