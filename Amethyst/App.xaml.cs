using Amethyst.Classes;
using Amethyst.Installer.ViewModels;
using Amethyst.Installer.Views;
using Amethyst.Plugins.Contract;
using Amethyst.Popups;
using Amethyst.Schedulers;
using Amethyst.Utils;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Storage;
using Windows.System.UserProfile;
using Windows.UI.StartScreen;
using Windows.UI.ViewManagement;
using Windows.Web.Http;
using LaunchActivatedEventArgs = Microsoft.UI.Xaml.LaunchActivatedEventArgs;

namespace Amethyst;

public partial class App : Application
{
    private readonly List<Host> _views = [];
    private bool _canCloseViews;
    private CrashWindow _crashWindow;
    private Host _mHostWindow;
    private MainWindow _mWindow;

    /// <summary>
    ///     Initializes the singleton application object.  This is the first line of authored code
    ///     executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        AsyncUtils.RunSync(PathsHandler.Setup);

        // Listen for and log all uncaught second-chance exceptions : XamlApp
        UnhandledException += (_, e) =>
        {
            var ex = e.Exception;
            Logger.Fatal($"Unhandled Exception: {ex.GetType().Name} in {ex.Source}: {ex.Message}\n{ex.StackTrace}");
            if (Interfacing.SuppressAllDomainExceptions || e.Exception is COMException) return; // Don't do anything

            var stc = $"{ex.GetType().Name} in {ex.Source}: {ex.Message}\n{ex.StackTrace}";
            var msg = Interfacing.LocalizedJsonString("/CrashHandler/Content/Crash/UnknownStack").Format(stc);
            Interfacing.Fail(msg != "/CrashHandler/Content/Crash/UnknownStack" ? msg : stc);

            Environment.Exit(0); // Simulate a standard application exit
        };

        // Listen for and log all uncaught second-chance exceptions : Domain
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = (Exception)e.ExceptionObject;
            Logger.Fatal($"Unhandled Exception: {ex.GetType().Name} in {ex.Source}: {ex.Message}\n{ex.StackTrace}");
            if (Interfacing.SuppressAllDomainExceptions || e.ExceptionObject is COMException)
                return; // Don't do anything

            var stc = $"{ex.GetType().Name} in {ex.Source}: {ex.Message}\n{ex.StackTrace}";
            var msg = Interfacing.LocalizedJsonString("/CrashHandler/Content/Crash/UnknownStack").Format(stc);
            Interfacing.Fail(msg != "/CrashHandler/Content/Crash/UnknownStack" ? msg : stc);

            Environment.Exit(0); // Simulate a standard application exit
        };

        // Set default window launch size (WinUI)
        ApplicationView.PreferredLaunchViewSize = new Size(1000, 700);
        ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;

        // Initialize the logger
        Logger.Init(Interfacing.GetAppDataLogFilePath(
            $"Amethyst_{DateTime.Now:yyyyMMdd-HHmmss.ffffff}.log"));

        // Create an empty file for checking for crashes
        try
        {
            if (!Interfacing.SuppressAllDomainExceptions &&
                (string.IsNullOrEmpty(Environment.GetCommandLineArgs().ElementAtOrDefault(1)) ||
                 Environment.GetCommandLineArgs().ElementAtOrDefault(1) is "amethyst-app:"))
            {
                Interfacing.CrashFile =
                    new FileInfo(Path.Join(Interfacing.TemporaryFolder.Path, "Crash",
                        Guid.NewGuid().ToString().ToUpper()));

                Interfacing.CrashFile.Directory?.Create(); // Create the directory too...
                Interfacing.CrashFile.Create(); // Create the file if not running as slave
            }
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }

        try
        {
            // Try deleting the "latest" log file
            File.Delete(Interfacing.GetAppDataLogFilePath("_latest.log"));
        }
        catch (Exception e)
        {
            Logger.Info(e);
        }

        // Log status information
        Logger.Info($"CLR Runtime version: {typeof(string).Assembly.ImageRuntimeVersion}");
        Logger.Info($"Framework build number: {Environment.Version} (.NET Core)");
        Logger.Info($"Running on {Environment.OSVersion}");

        Logger.Info($"Amethyst version: {AppData.VersionString}");
        Logger.Info($"Amethyst web API version: {AppData.ApiVersion}");
        Logger.Info("Amethyst build commit: AZ_COMMIT_SHA");

        // Read saved settings
        Logger.Info("Reading base app settings...");
        AppSettings.DoNotSaveSettings = true;
        AppData.Settings.ReadSettings(); // Now read

        // Read plugin settings
        Logger.Info("Reading custom plugin settings...");
        AppPlugins.PluginSettings.ReadSettings();

        // Run detached to allow for async calls
        var stringsFolder = Path.Join(Interfacing.AppDirectoryName, "Assets", "Strings");
        if (File.Exists(Interfacing.GetAppDataFilePath("Localization.json")))
            try
            {
                // Parse the loaded json
                var defaults = JsonConvert.DeserializeObject<LocalizationSettings>(
                                   File.ReadAllText(Interfacing.GetAppDataFilePath("Localization.json"))) ??
                               new LocalizationSettings();

                if (defaults.AmethystStringsFolder is not null &&
                    Directory.Exists(defaults.AmethystStringsFolder))
                    stringsFolder = defaults.AmethystStringsFolder;
            }
            catch (Exception e)
            {
                Logger.Info($"Localization settings checkout failed! Message: {e.Message}");
            }
        else Logger.Info("No default localization settings found! [Localization.json]");

        if (!Directory.Exists(stringsFolder)) return;

        // Load language resources
        Interfacing.LoadJsonStringResourcesEnglish();
        Interfacing.LoadJsonStringResources(AppData.Settings.AppLanguage);

        // Setup string hot reload watchdog
        ResourceWatcher = new FileSystemWatcher
        {
            Path = stringsFolder,
            NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName |
                           NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
            Filter = "*.json",
            EnableRaisingEvents = true
        };

        // Add event handlers : local
        ResourceWatcher.Changed += OnWatcherOnChanged;
        ResourceWatcher.Created += OnWatcherOnChanged;
        ResourceWatcher.Deleted += OnWatcherOnChanged;
        ResourceWatcher.Renamed += OnWatcherOnChanged;
    }

    private FileSystemWatcher ResourceWatcher { get; }

    /// <summary>
    ///     Invoked when the application is launched normally by the end user.  Other entry points
    ///     will be used such as when the application is launched to open a specific file.
    /// </summary>
    /// <param name="eventArgs">Details about the launch request and process.</param>
    protected override async void OnLaunched(LaunchActivatedEventArgs eventArgs)
    {
        // Check if there's any argv[1]
        var args = Environment.GetCommandLineArgs();
        Logger.Info($"Received launch arguments: {string.Join(", ", args)}");

        // Check if this startup isn't a request
        if (args.Length > 1)
            if (args[1] == "Kill" && args.Length > 2)
            {
                try
                {
                    Logger.Info($"Amethyst running in process kill slave mode! ID: {args[2]}");
                    var processToKill = Process.GetProcessById(int.Parse(args[2]));

                    // If we want to kill server, shut down monitor first
                    if (processToKill.ProcessName == "vrserver")
                        Process.GetProcessesByName("vrmonitor").ToList().ForEach(x => x.Kill());

                    processToKill.Kill(); // Now kill the actual process
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }

                Logger.Info("That's all! Shutting down now...");
                Environment.Exit(0); // Cancel further application startup
            }
            else if (args[1].StartsWith("amethyst-app:crash"))
            {
                Logger.Info("Amethyst running in crash handler mode!");
                Interfacing.SuppressAllDomainExceptions = true;

                // Disable internal sounds
                ElementSoundPlayer.State = ElementSoundPlayerState.Off;

                Logger.Info("Creating a new CrashWindow view...");
                _crashWindow = new CrashWindow(args); // Create a new window

                Logger.Info($"Activating {_crashWindow.GetType()}...");
                _crashWindow.Activate(); // Activate the main window

                // Do this in the meantime
                _ = Task.Factory.StartNew(async () =>
                {
                    try
                    {
                        var client = new HttpClient();

                        using var response =
                            await client.GetAsync(new Uri("https://docs.k2vr.tech/shared/locales.json"));
                        using var content = response.Content;
                        var json = await content.ReadAsStringAsync();

                        // Optionally fall back to English
                        if (JObject.Parse(json)[Interfacing.DocsLanguageCode] == null)
                            Interfacing.DocsLanguageCode = "en";
                    }
                    catch (Exception)
                    {
                        Interfacing.DocsLanguageCode = "en";
                    }
                });

                // That's all!
                return;
            }

        // Try to register jump list entries if packaged
        if (PathsHandler.IsAmethystPackaged)
            try
            {
                if (JumpList.IsSupported())
                {
                    Logger.Info("Registering jump list entries...");

                    var jumpList = await JumpList.LoadCurrentAsync();

                    jumpList.SystemGroupKind = JumpListSystemGroupKind.None;
                    jumpList.Items.Clear();

                    void AddItem(string id)
                    {
                        var taskItem = JumpListItem.CreateWithArguments(id,
                            Interfacing.LocalizedJsonString($"/JumpList/{id}"));

                        taskItem.Logo = new Uri($"ms-appx:///Assets/Jumps/{id}.png");
                        jumpList.Items.Add(taskItem);
                    }

                    AddItem("logs");
                    AddItem("report");
                    AddItem("local-folder");
                    AddItem("localize");
                   
                    await jumpList.SaveAsync();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }

        // Check if activated via uri
        var activationUri = PathsHandler.IsAmethystPackaged
            ? (AppInstance.GetCurrent().GetActivatedEventArgs().Data as
                ProtocolActivatedEventArgs)?.Uri
            : args.Length > 1 && args[1].StartsWith("amethyst-app:") &&
              Uri.TryCreate(args[1], UriKind.RelativeOrAbsolute, out var au)
                ? au
                : null;

        if (AppInstance.GetCurrent()?.GetActivatedEventArgs()?.Data is
            Windows.ApplicationModel.Activation.LaunchActivatedEventArgs launchArgs)
            activationUri = new Uri($"amethyst-app:{launchArgs.Arguments}");

        // Check if there's any launch arguments
        if (activationUri is not null && activationUri.Segments.Length > 0)
        {
            Logger.Info($"Activated via uri of: {activationUri}");
            switch (activationUri.Segments.First())
            {
                case "logs":
                {
                    SystemShell.OpenFolderAndSelectItem(
                        new DirectoryInfo(Interfacing.GetAppDataLogFilePath(""))
                            .GetFiles().OrderByDescending(x => x.LastWriteTime)
                            .ToList().ElementAtOrDefault(1)?.FullName
                        ?? Interfacing.GetAppDataLogFilePath(""));

                    Logger.Info("That's all! Shutting down now...");
                    Environment.Exit(0); // Cancel further application startup
                    break;
                }
                case "data-folder":
                {
                    SystemShell.OpenFolderAndSelectItem(
                        Directory.GetParent(Interfacing.LocalFolder.Path)?.FullName);

                    Logger.Info("That's all! Shutting down now...");
                    Environment.Exit(0); // Cancel further application startup
                    break;
                }
                case "local-folder":
                {
                    SystemShell.OpenFolderAndSelectItem(Interfacing.LocalFolder.Path);

                    Logger.Info("That's all! Shutting down now...");
                    Environment.Exit(0); // Cancel further application startup
                    break;
                }
                case "make-local":
                {
                    try
                    {
                        // Read the query string
                        var queryDictionary = HttpUtility
                            .ParseQueryString(activationUri!.Query.TrimStart('?'));

                        // Read all needed query parameters
                        var sourcePath = queryDictionary["source"];
                        var targetFolderPath = queryDictionary["target"];

                        // Validate our parameters
                        if (sourcePath is not null)
                        {
                            // Modify our parameters
                            sourcePath = Path.Join(Interfacing.ProgramLocation.DirectoryName!, sourcePath);
                            var targetFolder = string.IsNullOrEmpty(targetFolderPath)
                                ? Interfacing.LocalFolder // Copy to LocalState if not passed
                                : await Interfacing.LocalFolder.CreateFolderAsync(targetFolderPath);

                            if (File.Exists(sourcePath))
                                await (await StorageFile.GetFileFromPathAsync(sourcePath)).CopyAsync(targetFolder);

                            else if (Directory.Exists(sourcePath))
                                new DirectoryInfo(sourcePath).CopyToFolder(
                                    (await targetFolder.CreateFolderAsync(new DirectoryInfo(sourcePath).Name,
                                        CreationCollisionOption.OpenIfExists)).Path);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }

                    Logger.Info("That's all! Shutting down now...");
                    Environment.Exit(0); // Cancel further application startup
                    break;
                }
                case "set-defaults":
                {
                    try
                    {
                        // Read the query string
                        var queryDictionary = HttpUtility
                            .ParseQueryString(activationUri!.Query.TrimStart('?'));

                        // Read all needed query parameters
                        var trackingDevice = queryDictionary["TrackingDevice"];
                        var serviceEndpoint = queryDictionary["ServiceEndpoint"];
                        var extraTrackersValid = bool.TryParse(queryDictionary["ExtraTrackers"], out var extraTrackers);

                        Logger.Info($"Received defaults: TrackingDevice{{{trackingDevice}}}, " +
                                    $"ServiceEndpoint{{{serviceEndpoint}}}, ExtraTrackers{{{extraTrackers}}}");

                        // Create a new default config
                        DefaultSettings.TrackingDevice = trackingDevice;
                        DefaultSettings.ServiceEndpoint = serviceEndpoint;
                        DefaultSettings.ExtraTrackers = extraTrackersValid ? extraTrackers : null;
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }

                    Logger.Info("That's all! Shutting down now...");
                    Environment.Exit(0); // Cancel further application startup
                    break;
                }
                case "report":
                {
                    try
                    {
                        Logger.Info("Creating a data file list base...");
                        var fileList = new List<AppDataFile>();

                        Logger.Info("Searching for recent log files...");
                        fileList.AddRange(await new DirectoryInfo(Interfacing.GetAppDataLogFilePath(""))
                            .GetFiles().OrderByDescending(x => x.LastWriteTime).ToList()
                            .Where(x => File.ReadAllText(x.FullName).Contains("com_k2vr_amethyst"))
                            .Take(3)
                            .Select(async x => new AppDataFile(await StorageFile.GetFileFromPathAsync(x.FullName)))
                            .WhenAll()); // Collect the last 3 log files and wait for them

                        try
                        {
                            //if (AppData.Settings.ServiceEndpointGuid is "K2VRTEAM-AME2-APII-SNDP-SENDPTOPENVR")
                            //{

                            Logger.Info("Trying to collect SteamVR server logs...");
                            var vrServerLog = new FileInfo(Path.Join(
                                FileUtils.GetSteamInstallDirectory(), @"logs\vrserver.txt"));

                            if (vrServerLog.Exists)
                            {
                                if (fileList.Count > 3) fileList.RemoveAt(1); // Remove the first (current) log
                                fileList.Add(new AppDataFile(
                                    await StorageFile.GetFileFromPathAsync(vrServerLog.FullName)));
                            }

                            //}
                            //else
                            //{
                            //    Logger.Info("Skipping collection of SteamVR server logs...");
                            //}
                        }
                        catch (Exception e)
                        {
                            Logger.Warn(e);
                        }

                        Logger.Info("Creating a new Host:Report view...");
                        var window = new Host
                        {
                            Content = new Report(fileList)
                        };

                        Logger.Info($"Activating {window.GetType()}...");
                        window.Activate(); // Activate the main window

                        return; // That's all for now!
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }

                    Logger.Info("That's all! Shutting down now...");
                    Environment.Exit(0); // Cancel further application startup
                    break;
                }
                case "localize":
                {
                    await SetupLocalizationResources();
                    break;
                }
                case "localize-force":
                {
                    await SetupLocalizationResources(true);
                    break;
                }
                case "localize-tidy":
                {
                    await CleanupLocalizationResources();
                    break;
                }
                case "apply-fix":
                {
                    try
                    {
                        string pluginGuid, fixName, param;
                        if (!string.IsNullOrEmpty(activationUri!.Fragment.TrimStart('#')))
                        {
                            // "amethyst-app:apply-fix#GuidK2VRTEAM-AME2-APII-DVCE-DVCEKINECTV1FixNameNotPoweredFixParam0End"
                            pluginGuid = activationUri.Fragment.Substring("Guid", "FixName");
                            fixName = activationUri.Fragment.Substring("FixName", "Param");
                            param = activationUri.Fragment.Substring("Param", "End");
                        }
                        else
                        {
                            // Read the query string
                            var queryDictionary = HttpUtility
                                .ParseQueryString(activationUri!.Query.TrimStart('?'));

                            // Read all needed query parameters
                            pluginGuid = queryDictionary["Guid"];
                            fixName = queryDictionary["FixName"];
                            param = queryDictionary["Param"];
                        }

                        Logger.Info($"Received fix application request: " +
                                    $"Plugin{{{pluginGuid}}}, Fix Name{{{fixName}}}, Param{{{param}}}");

                        // Scan for all loadable plugins

                        // Search the "Plugins" subdirectory for assemblies that match the imports.
                        // Iterate over all directories in .\Plugins dir and add all * dirs to catalogs
                        Logger.Info("Searching for local plugins now...");
                        var localPluginsFolder = Path.Combine(Interfacing.ProgramLocation.DirectoryName!, "Plugins");

                        var pluginDirectoryList = Directory.Exists(localPluginsFolder)
                            ? Directory.EnumerateDirectories(localPluginsFolder, "*", SearchOption.TopDirectoryOnly).ToList()
                            : []; // In case the folder doesn't exist, create an empty directory list

                        // Search the "Plugins" AppData directory for assemblies that match the imports.
                        // Iterate over all directories in Plugins dir and add all * dirs to catalogs
                        Logger.Info("Searching for shared plugins now...");
                        pluginDirectoryList.AddRange(Directory.EnumerateDirectories(
                            (await Interfacing.GetAppDataPluginFolder("")).Path,
                            "*", SearchOption.TopDirectoryOnly));

                        pluginDirectoryList.RemoveAll(x => Path
                            .GetFileName(x)?.StartsWith("_IGNORE_") ?? false);

                        // Add the current assembly to support invoke method exports
                        AssemblyLoadContext.Default.LoadFromAssemblyPath(
                            Assembly.GetAssembly(typeof(ITrackingDevice))!.Location);

                        // Compose a list of tracking provider (device) plugins
                        Logger.Info("Searching for setup-able device plugins...");
                        var corePlugins = pluginDirectoryList
                            .Select(folder => Directory.GetFiles(folder, "plugin*.dll"))
                            .Select(assembly => SetupPlugin.CreateFrom(assembly.First()))
                            .Where(device => device.CoreSetupData is not null).ToList();

                        // Validate there are enough plugins
                        Logger.Info($"Found: {corePlugins.Count} of {nameof(SetupPlugin)}");
                        if (!corePlugins.Any() || string.IsNullOrEmpty(pluginGuid))
                        {
                            Logger.Info("Not enough plugins to complete set-up!");
                            Logger.Info("That's all! Shutting down now...");
                            Interfacing.Fail(Interfacing.LocalizedJsonString("/SharedStrings/Plugins/Fix/Uri/NotFound"));
                            Environment.Exit(0); // Cancel further application startup
                        }

                        // Find the requested plugin
                        var plugin = corePlugins.FirstOrDefault(x =>
                            x.GuidValid && x.Guid == (pluginGuid.EndsWith(":SETUP") ? pluginGuid : $"{pluginGuid}:SETUP"));
                        if (plugin is null)
                        {
                            Logger.Info($"Plugin with Guid of {{{pluginGuid}}} was not found!");
                            Logger.Info("That's all! Shutting down now...");
                            Interfacing.Fail(Interfacing.LocalizedJsonString("/SharedStrings/Plugins/Fix/Uri/FixName").Format(pluginGuid));
                            Environment.Exit(0); // Cancel further application startup
                        }

                        // Find the requested fix (either by name or class)
                        Logger.Info($"Found plugin with Guid of {{{pluginGuid}}}, now searching for fixes...");
                        var fix = plugin.DependencyInstaller?.ListFixes().FirstOrDefault(x => x.Name == fixName) ??
                                  plugin.DependencyInstaller?.ListFixes().FirstOrDefault(x => x.GetType().Name == fixName);
                        if (fix is null)
                        {
                            Logger.Info($"Fix with name {{{fixName}}} under plugin with Guid of {{{pluginGuid}}} was not found!");
                            Logger.Info("That's all! Shutting down now...");
                            Interfacing.Fail(Interfacing.LocalizedJsonString("/SharedStrings/Plugins/Fix/Uri/Error").Format(fixName, pluginGuid));
                            Environment.Exit(0); // Cancel further application startup
                        }

                        // Try applying the fix
                        Logger.Info($"Found fix with name {{{fixName}}} under plugin with Guid of {{{pluginGuid}}}, now applying...");
                        var tokenSource = new CancellationTokenSource();
                        tokenSource.CancelAfter(TimeSpan.FromMinutes(1));

                        if (!FileUtils.IsCurrentProcessElevated() && pluginGuid is "K2VRTEAM-AME2-APII-DVCE-DVCEKINECTV1")
                        {
                            // "amethyst-app:apply-fix#GuidK2VRTEAM-AME2-APII-DVCE-DVCEKINECTV1FixNameNotPoweredFixParam0End"
                            Logger.Warn("Amethyst is not elevated, but the fix will likely require administrative privileges! Restarting...");
                            await Interfacing.ExecuteAppRestart(admin: true, filenameOverride: "powershell.exe",
                                parameters: $"-command \"Start-Process -FilePath 'amethyst-app:apply-fix#Guid{pluginGuid}FixName{fixName}Param{param}End'\"");
                        }

                        // Loop over all dependencies and install them, give up on failures
                        try
                        {
                            // Prepare the progress update handler
                            var progress = new Progress<InstallationProgress>();
                            progress.ProgressChanged += (_, installationProgress) =>
                                Logger.Info($"Applying fix \"{fixName}\" from {{{pluginGuid}}}: " +
                                            $"{installationProgress.StageTitle} - {installationProgress.OverallProgress}");

                            // Capture the installation thread
                            var installationWorker = fix.Apply(progress, tokenSource.Token, param);
                            if (!await installationWorker) // Actually start the installation now
                            {
                                Logger.Info($"Fix ({{{pluginGuid}}}, \"{fixName}\") failed due to unknown reasons.");
                                Interfacing.Fail(Interfacing.LocalizedJsonString(
                                    "/SharedStrings/Plugins/Fix/Contents/Failure").Format($"({{{pluginGuid}}}, \"{fixName}\")"));
                            }
                            else
                            {
                                // Show a toast instead of the crash window, we don't need *that* much attention here
                                Logger.Info($"Fix ({{{pluginGuid}}}, \"{fixName}\") applied successfully.");

                                Interfacing.SetupNotificationManager();
                                Interfacing.ShowToast($"{fix.Name} fix applied successfully!", $"Reference: \"{fix.GetType().Name}\" from {{{pluginGuid}}}");
                            }

                            tokenSource.Dispose();
                            Logger.Info("That's all! Shutting down now...");
                            Environment.Exit(0); // Cancel further application startup
                        }
                        catch (OperationCanceledException e)
                        {
                            Logger.Info($"Fix ({{{pluginGuid}}}, \"{fixName}\") failed due to timeout.");
                            Interfacing.Fail(Interfacing.LocalizedJsonString(
                                    "/SharedStrings/Plugins/Fix/Contents/Failure").Format($"({{{pluginGuid}}}, \"{fixName}\")") + $"\n\n{e.Message}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Info($"Fix ({{{pluginGuid}}}, \"{fixName}\") failed due to an exception: {ex.Message}.");
                            Interfacing.Fail(Interfacing.LocalizedJsonString(
                                    "/SharedStrings/Plugins/Fix/Contents/Failure").Format($"({{{pluginGuid}}}, \"{fixName}\")") + $"\n\n{ex.Message}");
                        }

                        tokenSource.Dispose(); // Clean up after installation
                        Logger.Info("That's all! Shutting down now...");
                        Environment.Exit(0); // Cancel further application startup
                        break;
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }

                    Logger.Info("That's all! Shutting down now...");
                    Environment.Exit(0); // Cancel further application startup
                    break;
                }
            }
        }

        Logger.Info("Registering a named mutex for com_k2vr_amethyst...");
        try
        {
            Shared.Main.ApplicationMultiInstanceMutex = new Mutex(
                true, "com_k2vr_amethyst", out var needToCreateNew);

            if (!needToCreateNew)
            {
                Logger.Fatal(new AbandonedMutexException("Startup failed! The app is already running."));
                await "amethyst-app:crash-already-running".Launch();

                await Task.Delay(3000);
                Environment.Exit(0); // Exit peacefully
            }
        }
        catch (Exception e)
        {
            Logger.Fatal(new AbandonedMutexException($"Startup failed! Mutex creation error: {e.Message}"));
            await "amethyst-app:crash-already-running".Launch();

            await Task.Delay(3000);
            Environment.Exit(0); // Exit peacefully
        }

        Logger.Info("Starting the crash handler passing the app PID...");
        await ($"amethyst-app:crash-watchdog?pid={Environment.ProcessId}&log={Logger.LogFilePath}" +
               $"&crash={Interfacing.CrashFile?.FullName}").Launch();

        // Disable internal sounds
        ElementSoundPlayer.State = ElementSoundPlayerState.Off;

        // Try reading the startup task config
        try
        {
            Logger.Info("Searching for scheduled startup tasks...");
            StartupController.Controller.ReadTasks();
        }
        catch (Exception e)
        {
            Logger.Error($"Reading the startup scheduler configuration failed. Message: {e.Message}");
        }

        // Execute plugin uninstalls: delete plugin files
        foreach (var action in StartupController.Controller.DeleteTasks.ToList())
            try
            {
                Logger.Info($"Parsing a startup {action.GetType()} task with name \"{action.Name}\"...");
                if (!Directory.Exists(action.PluginFolder) ||
                    action.PluginFolder == Interfacing.ProgramLocation.DirectoryName || Directory
                        .EnumerateFiles(action.PluginFolder)
                        .Any(x => x == Interfacing.ProgramLocation.FullName))
                {
                    StartupController.Controller.StartupTasks.Remove(action);
                    continue; // Remove the action as it was invalid at this time
                }

                Logger.Info("Cleaning the plugin folder now...");
                Directory.Delete(action.PluginFolder, true);

                Logger.Info("Deleting attempted scheduled startup " +
                            $"{action.GetType()} task with name \"{action.Name}\"...");

                StartupController.Controller.StartupTasks.Remove(action);
                Logger.Info($"Looks like a startup {action.GetType()} task with " +
                            $"name \"{action.Name}\" has been executed successfully!");
            }
            catch (Exception e)
            {
                Logger.Warn(e);
            }

        // Execute plugin updates: replace plugin files
        foreach (var action in StartupController.Controller.UpdateTasks.ToList())
            try
            {
                Logger.Info($"Parsing a startup {action.GetType()} task with name \"{action.Name}\"...");
                if (!Directory.Exists(action.PluginFolder) || !File.Exists(action.UpdatePackage))
                {
                    StartupController.Controller.StartupTasks.Remove(action);
                    continue; // Remove the action as it was invalid at this time
                }

                Logger.Info($"Found a plugin update package in folder \"{action.PluginFolder}\"");
                Logger.Info("Checking if the plugin folder is not locked...");
                if (FileUtils.WhoIsLocking(action.PluginFolder).Any())
                {
                    Logger.Info("Some plugin files are still locked! Showing the update dialog...");
                    Logger.Info($"Creating a new {typeof(Host)}...");
                    var dialogWindow = new Host();

                    Logger.Info("Preparing the data regarding the blocking process...");
                    dialogWindow.Content = new Blocked(new DirectoryInfo(action.PluginFolder).Name,
                        FileUtils.WhoIsLocking(action.PluginFolder)
                            .Select(x => new BlockingProcess
                            {
                                ProcessPath = new FileInfo(FileUtils.GetProcessFilename(x) ?? "tmp"),
                                Process = x,
                                IsElevated = FileUtils.IsProcessElevated(x) && !FileUtils.IsCurrentProcessElevated()
                            }).ToList())
                    {
                        // Also set the parent to close
                        ParentWindow = dialogWindow
                    };

                    Logger.Info($"Activating {dialogWindow.GetType()} now...");
                    dialogWindow.Activate(); // Activate the window now

                    var continueEvent = new SemaphoreSlim(0);
                    var result = false; // Prepare [out] results for the event handler

                    // Wait for the dialog window to close
                    dialogWindow.Closed += (sender, arg) =>
                    {
                        arg.Handled = !_canCloseViews; // Mark as handled (or not)
                        if (_canCloseViews) return; // Close if already done...

                        _views.Add(sender as Host); // Add this sender to closable list
                        result = (sender as Host)?.Result ?? false; // Grab the result
                        Logger.Info($"The dialog was closed with result {result}!");
                        continueEvent.Release(); // Unlock further program execution
                    };

                    await continueEvent.WaitAsync(); // Wait for the closed event
                    if (!result) continue; // Don't update if not resolved properly (yet)
                }

                Logger.Info("Cleaning the plugin folder now...");
                Directory.GetDirectories(action.PluginFolder).ToList().ForEach(x => Directory.Delete(x, true));
                Directory.GetFiles(action.PluginFolder, "*.*", SearchOption.AllDirectories)
                    .Where(x => x != action.UpdatePackage).ToList().ForEach(File.Delete);

                Logger.Info("Unpacking the new plugin from its archive...");
                ZipFile.ExtractToDirectory(action.UpdatePackage, action.PluginFolder, true);

                Logger.Info("Deleting the plugin update package...");
                File.Delete(action.UpdatePackage); // Cleanup after the update

                Logger.Info("Deleting attempted scheduled startup " +
                            $"{action.GetType()} task with name \"{action.Name}\"...");

                StartupController.Controller.StartupTasks.Remove(action);
                Logger.Info($"Looks like a startup {action.GetType()} task with " +
                            $"name \"{action.Name}\" has been executed successfully!");
            }
            catch (Exception e)
            {
                Logger.Warn(e);
            }


        //// Check for updates : Installer
        //try
        //{
        //    using var client = new RestClient();
        //    var endpointData = string.Empty;

        //    // Data
        //    try
        //    {
        //        Logger.Info("Checking available configuration... [GET]");
        //        var response = await client.ExecuteGetAsync(
        //            "https://github.com/KinectToVR/Amethyst/releases/download/latest/EndpointData.json",
        //            new RestRequest());

        //        endpointData = response.Content;
        //    }
        //    catch (Exception e)
        //    {
        //        Logger.Error(new Exception($"Error getting the configuration info! Message: {e.Message}"));
        //        Logger.Error(e); // Log the actual exception in the next message
        //    }

        //    // Parse
        //    try
        //    {
        //        if (!string.IsNullOrEmpty(endpointData))
        //        {
        //            // Parse the loaded json
        //            var jsonRoot = JsonObject.Parse(endpointData);

        //            // Check if the resource root is fine
        //            if (jsonRoot?.ContainsKey("C291EA26-9A68-4FA7-8571-477D4F7CB168") ?? false)
        //            {
        //                SetupData.LimitedHide = jsonRoot.GetNamedBoolean("C291EA26-9A68-4FA7-8571-477D4F7CB168");
        //                SetupData.LimitedSetup = jsonRoot.GetNamedBoolean("C291EA26-9A68-4FA7-8571-477D4F7CB168");
        //            }
        //        }
        //        else
        //        {
        //            Logger.Error(new NoNullAllowedException("Configuration-check failed, the string was empty."));
        //        }
        //    }
        //    catch (Exception e)
        //    {
        //        Logger.Error(new Exception($"Error updating the configuration info! Message: {e.Message}"));
        //        Logger.Error(e); // Log the actual exception in the next message
        //    }
        //}
        //catch (Exception e)
        //{
        //    Logger.Error($"Update failed, an exception occurred. Message: {e.Message}");
        //    Logger.Error(e); // Log the actual exception in the next message
        //}

        // Check whether the first-time setup is complete
        if (!DefaultSettings.SetupFinished)
        {
            // Update 1.2.13.0: reset configuration
            try
            {
                if (PathsHandler.LocalSettings["SetupFinished"] as bool? ?? false)
                {
                    PathsHandler.LocalSettings.Remove("SetupFinished");
                    AppData.Settings = new AppSettings(); // Reset application settings
                    AppData.Settings.SaveSettings(); // Save empty settings to file

                    Interfacing.SetupNotificationManager();
                    Interfacing.ShowToast("Looking for your old settings?",
                        "They're not here anymore! Amethyst has undergone " +
                        "a critical update, so you'll need to reconfigure it.");

                    // If there's an OVR driver folder, try to delete it if possible
                    await (await PathsHandler.LocalFolder.GetFolderAsync("Amethyst"))?.DeleteAsync();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex);
            }

            // Scan for all loadable plugins

            // Search the "Plugins" subdirectory for assemblies that match the imports.
            // Iterate over all directories in .\Plugins dir and add all * dirs to catalogs
            Logger.Info("Searching for local plugins now...");
            var localPluginsFolder = Path.Combine(Interfacing.ProgramLocation.DirectoryName!, "Plugins");

            var pluginDirectoryList = Directory.Exists(localPluginsFolder)
                ? Directory.EnumerateDirectories(localPluginsFolder, "*", SearchOption.TopDirectoryOnly).ToList()
                : []; // In case the folder doesn't exist, create an empty directory list

            // Search the "Plugins" AppData directory for assemblies that match the imports.
            // Iterate over all directories in Plugins dir and add all * dirs to catalogs
            Logger.Info("Searching for shared plugins now...");
            pluginDirectoryList.AddRange(Directory.EnumerateDirectories(
                (await Interfacing.GetAppDataPluginFolder("")).Path,
                "*", SearchOption.TopDirectoryOnly));

            pluginDirectoryList.RemoveAll(x => Path
                .GetFileName(x)?.StartsWith("_IGNORE_") ?? false);

            // Add the current assembly to support invoke method exports
            AssemblyLoadContext.Default.LoadFromAssemblyPath(
                Assembly.GetAssembly(typeof(ITrackingDevice))!.Location);

            // Compose a list of tracking provider (device) plugins
            Logger.Info("Searching for setup-able device plugins...");
            var coreDevicePlugins = pluginDirectoryList
                .Select(folder => Directory.GetFiles(folder, "plugin*.dll"))
                .Select(assembly => SetupPlugin.CreateFrom(assembly.First()))
                .Where(plugin => plugin?.PluginType == typeof(ITrackingDevice))
                .Where(device => device.CoreSetupData is not null).ToList();

            // Compose a list of service endpoint (service) plugins
            Logger.Info("Searching for setup-able service plugins...");
            var coreServicePlugins = pluginDirectoryList
                .Select(folder => Directory.GetFiles(folder, "plugin*.dll"))
                .Select(assembly => SetupPlugin.CreateFrom(assembly.First()))
                .Where(plugin => plugin?.PluginType == typeof(IServiceEndpoint))
                .Where(device => device.CoreSetupData is not null).ToList();

            // Validate there are enough plugins
            Logger.Info($"Found: {coreDevicePlugins.Count} of {nameof(ITrackingDevice)}");
            Logger.Info($"Found: {coreServicePlugins.Count} of {nameof(IServiceEndpoint)}");
            if (!coreDevicePlugins.Any() || !coreServicePlugins.Any())
            {
                Logger.Info("Not enough plugins to complete set-up!");

                // PLUGINS:
                new Host(height: 700, width: 1200)
                {
                    Single = true,
                    Content = new SetupError
                    {
                        Error = new PluginsError()
                    }
                }.Activate();
                return; // That's all
            }

            // Create a new host windows for the rest of the setup
            Logger.Info($"Creating a new {typeof(Host)}...");
            _mHostWindow = new Host(height: 700, width: 1200) { Single = true };

            // Still here? Let's begin the setup
            Logger.Info("Starting the main setup now...");
            var continueEvent = new SemaphoreSlim(0);
            _mHostWindow.Content = new SetupSplash
            {
                ShowWait = TimeSpan.FromMilliseconds(300),
                Splash = new WelcomeSplash
                {
                    Action = () =>
                    {
                        // Unlock further program execution
                        continueEvent.Release();
                        return Task.CompletedTask;
                    }
                }
            };
            _mHostWindow.Activate();

            Logger.Info("Waiting for user input...");
            await continueEvent.WaitAsync(); // Wait for the 'continue' event

            // Verify we're running as admin
            Logger.Info("Verifying the elevation status...");
            if (!FileUtils.IsCurrentProcessElevated() && !SetupData.LimitedSetup)
            {
                _mHostWindow.Content = new SetupError
                {
                    Error = new PermissionsError
                    {
                        Action = async () =>
                        {
                            await Interfacing.ExecuteAppRestart(admin: true); // Try restarting ame
                        }
                    }
                };
                _mHostWindow.Activate();

                (_mHostWindow.Content as SetupError).ContinueEvent += (_, _) =>
                {
                    Logger.Info("The user has decided to continue with limited setup!");
                    SetupData.LimitedSetup = true; // Mark our further setup as limited
                    continueEvent.Release(); // Unlock further program execution
                };
                await continueEvent.WaitAsync(); // Wait for the 'continue' event
            }

            // Set up device plugins
            Logger.Info($"Switching the view to {typeof(SetupDevices)}...");
            _mHostWindow.Content = new SetupDevices
            {
                Devices = coreDevicePlugins
            };
            _mHostWindow.Activate();

            (_mHostWindow.Content as SetupDevices).ContinueEvent += (_, _) =>
            {
                Logger.Info("Devices should have been set up by now!");
                continueEvent.Release(); // Unlock further program execution
            };

            Logger.Info("Waiting for user input...");
            await continueEvent.WaitAsync(); // Wait for the 'continue' event

            // Set up service plugins
            Logger.Info($"Switching the view to {typeof(SetupServices)}...");
            _mHostWindow.Content = new SetupServices
            {
                Services = coreServicePlugins
            };
            _mHostWindow.Activate();

            (_mHostWindow.Content as SetupServices).ContinueEvent += (_, _) =>
            {
                Logger.Info("Services should have been set up by now!");
                continueEvent.Release(); // Unlock further program execution
            };

            Logger.Info("Waiting for user input...");
            await continueEvent.WaitAsync(); // Wait for the 'continue' event
            DefaultSettings.SetupFinished = true; // That's all for the setup

            // Show the ending splash
            Logger.Info($"Switching the view to {typeof(SetupServices)}...");
            _mHostWindow.Content = new SetupSplash
            {
                AnimateEnding = false,
                ShowWait = TimeSpan.FromMilliseconds(250),
                Splash = new EndingSplash
                {
                    Action = async () =>
                    {
                        await Interfacing.ExecuteAppRestart(); // Try restarting ame
                    }
                }
            };
            _mHostWindow.Activate();
            return; // That's all!
        }

        Logger.Info("Creating a new MainWindow view...");
        _mWindow = new MainWindow(); // Create a new window

        Logger.Info($"Activating {_mWindow.GetType()}...");
        _mWindow.Activate(); // Activate the main window

        // Check for any additional work
        if (!_views.Any()) return;

        Logger.Info($"Waiting for {_mWindow.GetType()} to show up...");
        await Task.Delay(1000); // Assume this will be enough

        Logger.Info("Closing all other views now...");
        _canCloseViews = true; // Tell other views they can close
        _views.ForEach(x => x.Close()); // Try closing each
    }

    private async Task SetupLocalizationResources(bool overwrite = false)
    {
        try
        {
            var stringsFolder = await Interfacing.LocalFolder
                .CreateFolderAsync("Strings", CreationCollisionOption.OpenIfExists);

            /* Copy everything to the backup folder */
            var backupFolder = await (await Interfacing.LocalFolder
                    .CreateFolderAsync("StringsBackup", CreationCollisionOption.OpenIfExists))
                .CreateFolderAsync(DateTime.Now.ToString("dd　MM　yyyy　HH：mm：ss"),
                    CreationCollisionOption.OpenIfExists);

            new DirectoryInfo(stringsFolder.Path).CopyToFolder(backupFolder.Path);

            /* Continue execution */
            var appStringsFolder = await stringsFolder.CreateFolderAsync(
                "Amethyst", CreationCollisionOption.OpenIfExists);

            var pluginStringsFolders = new SortedDictionary<string, string>();

            // Copy all app and plugin strings to a shared folder
            new DirectoryInfo(Path.Join(Interfacing.ProgramLocation.DirectoryName!, "Assets", "Strings"))
                .CopyToFolder(appStringsFolder.Path, overwrite: overwrite);

            var localPluginsFolder = Path.Combine(Interfacing.ProgramLocation.DirectoryName!, "Plugins");
            if (Directory.Exists(localPluginsFolder))
                foreach (var pluginFolder in Directory.EnumerateDirectories(
                                 localPluginsFolder, "*", SearchOption.TopDirectoryOnly)
                             .Where(x => Directory.Exists(Path.Join(x, "Assets", "Strings"))).ToList())
                {
                    // Copy to the shared folder, creating a random name
                    var outputFolder = await stringsFolder.CreateFolderAsync(
                        new DirectoryInfo(pluginFolder).Name,
                        CreationCollisionOption.OpenIfExists);

                    new DirectoryInfo(Path.Join(pluginFolder, "Assets", "Strings"))
                        .CopyToFolder(outputFolder.Path, overwrite: overwrite);

                    pluginStringsFolders.Add(Path.Join(pluginFolder, "Assets", "Strings"),
                        outputFolder.Path);
                }

            // Create a new localization config
            await File.WriteAllTextAsync(Interfacing.GetAppDataFilePath("Localization.json"),
                JsonConvert.SerializeObject(new LocalizationSettings
                {
                    AmethystStringsFolder = appStringsFolder.Path,
                    PluginStringFolders = pluginStringsFolders
                }, Formatting.Indented));

            SystemShell.OpenFolderAndSelectItem(stringsFolder.Path);
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }

        Logger.Info("That's all! Shutting down now...");
        Environment.Exit(0); // Cancel further application startup
    }

    // Move everything to a new backup (date) folder,
    // then sync everything with english strings,
    // read and re-write all files to reorder strings
    private async Task CleanupLocalizationResources()
    {
        try
        {
            /* Folder definitions - root, app, plugins */
            var stringsFolder = await Interfacing.LocalFolder
                .CreateFolderAsync("Strings", CreationCollisionOption.OpenIfExists);

            /* Copy everything to the backup folder */
            var backupFolder = await (await Interfacing.LocalFolder
                    .CreateFolderAsync("StringsBackup", CreationCollisionOption.OpenIfExists))
                .CreateFolderAsync(DateTime.Now.ToString("dd　MM　yyyy　HH：mm：ss"),
                    CreationCollisionOption.OpenIfExists);

            new DirectoryInfo(stringsFolder.Path).CopyToFolder(backupFolder.Path);

            /* Go over all subfolders in the strings folder, sync */
            foreach (var folder in await stringsFolder.GetFoldersAsync())
                try
                {
                    var hasEnglishResources = await folder.FileExistsAsync("en.json");
                    if (!hasEnglishResources) Logger.Warn($"{folder.Path} doesn't contain en.json, skipping synchronization!");

                    var englishResources = hasEnglishResources
                        ? JsonConvert.DeserializeObject<LocalisationFileJson>(
                            await File.ReadAllTextAsync((await folder.GetFileAsync("en.json")).Path))
                        : null;

                    // Go over all localizable string folders we have
                    foreach (var file in await folder.GetFilesAsync())
                        try
                        {
                            if (file.Name is "locales.json") continue; // Don't touch our localization enum
                            var resourceJson = JsonConvert.DeserializeObject<LocalisationFileJson>(await File.ReadAllTextAsync(file.Path));

                            if (file.Name is not "en.json" and not "locales.json")
                            {
                                englishResources?.Messages.Where(x => resourceJson.Messages.All(message => message.Id != x.Id)).ToList()
                                    .ForEach(message => resourceJson.Messages.Add(new LocalizedMessage
                                    {
                                        Comment = message.Comment,
                                        Id = message.Id,
                                        Translation = $"TODO: {message.Translation}"
                                    })); // Synchronize by pulling missing strings from the EN json file

                                if (englishResources?.Messages.Any() ?? false)
                                    resourceJson?.Messages.Where(x => englishResources.Messages.All(message => message.Id != x.Id)).ToList()
                                        .ForEach(message => resourceJson.Messages.Remove(message)); // Remove any stale ones
                            }

                            // Write to reorder messages and (if applicable) add synchronized english JSON messages
                            await File.WriteAllTextAsync(file.Path, JsonConvert.SerializeObject(resourceJson, Formatting.Indented));
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex);
                        }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                }

            // And we're done - open the tidied up string folder
            SystemShell.OpenFolderAndSelectItem(stringsFolder.Path);
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }

        Logger.Info("That's all! Shutting down now...");
        Environment.Exit(0); // Cancel further application startup
    }

    // Send a non-dismissible tip about reloading the app
    private static void OnWatcherOnChanged(object o, FileSystemEventArgs fileSystemEventArgs)
    {
        Shared.Main.DispatcherQueue?.TryEnqueue(() =>
        {
            Logger.Info("String resource files have changed, reloading!");
            Logger.Info($"What happened: {fileSystemEventArgs.ChangeType}");
            Logger.Info($"Where: {fileSystemEventArgs.FullPath} ({fileSystemEventArgs.Name})");

            // Reload language resources
            Interfacing.LoadJsonStringResourcesEnglish();
            Interfacing.LoadJsonStringResources(AppData.Settings.AppLanguage);

            // Sanity check
            if (Shared.Main.MainWindowLoaded)
            {
                // Reload plugins' language resources
                foreach (var plugin in AppPlugins.TrackingDevicesList.Values)
                    Interfacing.Plugins.SetLocalizationResourcesRoot(
                        plugin.LocalizationResourcesRoot.Directory, plugin.Guid);

                foreach (var plugin in AppPlugins.ServiceEndpointsList.Values)
                    Interfacing.Plugins.SetLocalizationResourcesRoot(
                        plugin.LocalizationResourcesRoot.Directory, plugin.Guid);

                // Reload everything we can
                Shared.Devices.DevicesJointsValid = false;

                // Reload plugins' interfaces
                AppPlugins.TrackingDevicesList.Values.ToList().ForEach(x => x.OnLoad());
                AppPlugins.ServiceEndpointsList.Values.ToList().ForEach(x => x.OnLoad());
            }

            // Request page reloads
            Translator.Get.OnPropertyChanged();
            Shared.Events.RequestInterfaceReload();

            // Sanity check
            if (!Shared.Main.MainWindowLoaded) return;

            // Request manager reloads
            AppData.Settings.OnPropertyChanged();
            AppData.Settings.TrackersVector.ToList()
                .ForEach(x => x.OnPropertyChanged());

            // We're done with our changes now!
            Shared.Devices.DevicesJointsValid = true;
        });
    }
}

public static class ExtensionMethods
{
    public static async Task<Dictionary<TKey, TValue>> ToDictionaryAsync<TInput, TKey, TValue>(
        this IEnumerable<TInput> enumerable,
        Func<TInput, TKey> syncKeySelector,
        Func<TInput, Task<TValue>> asyncValueSelector)
    {
        var dictionary = new Dictionary<TKey, TValue>();

        foreach (var item in enumerable)
        {
            var key = syncKeySelector(item);

            var value = await asyncValueSelector(item);

            dictionary.Add(key, value);
        }

        return dictionary;
    }
}

public static class StringExtensions
{
    /// <summary>
    /// takes a substring between two anchor strings (or the end of the string if that anchor is null)
    /// </summary>
    /// <param name="this">a string</param>
    /// <param name="from">an optional string to search after</param>
    /// <param name="until">an optional string to search before</param>
    /// <param name="comparison">an optional comparison for the search</param>
    /// <returns>a substring based on the search</returns>
    public static string Substring(this string @this, string from = null, string until = null, StringComparison comparison = StringComparison.InvariantCulture)
    {
        var fromLength = (from ?? string.Empty).Length;
        var startIndex = !string.IsNullOrEmpty(from)
            ? @this.IndexOf(from, comparison) + fromLength
            : 0;

        if (startIndex < fromLength) throw new ArgumentException("from: Failed to find an instance of the first anchor");

        var endIndex = !string.IsNullOrEmpty(until)
            ? @this.IndexOf(until, startIndex, comparison)
            : @this.Length;

        if (endIndex < 0) throw new ArgumentException("until: Failed to find an instance of the last anchor");

        var subString = @this.Substring(startIndex, endIndex - startIndex);
        return subString;
    }
}