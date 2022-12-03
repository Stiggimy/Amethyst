﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Amethyst.Driver.API;
using Amethyst.Driver.Client;
using Amethyst.Utils;
using Grpc.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Windows.AppNotifications;
using Valve.VR;

namespace Amethyst.Classes;

public static class Interfacing
{
    public const uint MaxPingCheckingThreads = 3;

    public static bool
        IsExitingNow, // App closing check
        IsExitHandled; // If actions have been done

    public static readonly object UpdateLock = new();

    // App crash check
    public static FileInfo CrashFile;

    // Update check
    public static bool
        UpdateFound = false,
        UpdateOnClosed = false,
        CheckingUpdatesNow = false,
        UpdatingNow = false;

    // Position helpers for k2 devices -> GUID, Pose
    public static readonly SortedDictionary<string, Vector3>
        DeviceHookJointPosition = new(); // This one applies to both

    public static readonly SortedDictionary<string, Vector3>
        // For automatic calibration
        DeviceRelativeTransformOrigin = new(); // This one applies to both

    // OpenVR playspace position
    public static Vector3 VrPlayspaceTranslation = new(0);

    // OpenVR playspace rotation
    public static Quaternion VrPlayspaceOrientationQuaternion = new(0, 0, 0, 1);

    // Current page string
    public static string CurrentPageTag = "general";
    public static string CurrentPageClass = "Amethyst.Pages.General";

    // Current app state string (e.g. "general", "calibration_manual")
    public static string CurrentAppState = "general";

    // Currently available website language code
    public static string DocsLanguageCode = "en";

    // VR Overlay handle
    public static ulong VrOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
    public static uint VrNotificationId;

    // The actual app theme (ONLY dark/light)
    public static ElementTheme ActualTheme = ElementTheme.Dark;

    // Input actions' handler
    public static readonly EvrInput.SteamEvrInput EvrInput = new();

    // If trackers are added / initialized
    public static bool K2AppTrackersSpawned,
        AppTrackersInitialized;

    // Is the tracking paused
    public static bool IsTrackingFrozen = false;

    // Server checking threads number, max num of them
    public static uint PingCheckingThreadsNumber;

    // Server interfacing data
    public static int ServerDriverStatusCode;
    public static int ServerRpcStatusCode;

    public static long PingTime, ParsingTime;

    public static bool IsServerDriverPresent,
        ServerDriverFailure;

    public static string ServerStatusString = " \n \n ";

    // For manual calibration
    public static bool CalibrationConfirm,
        CalibrationModeSwap,
        CalibrationFineTune;

    // For manual calibration: L, R -> X, Y
    public static ((float X, float Y) LeftPosition,
        (float X, float Y) RightPosition)
        CalibrationJoystickPositions;

    // Check if we're currently scanning for trackers from other apps
    public static bool IsAlreadyAddedTrackersScanRunning = false;

    // If the already-added trackers check was requested
    public static bool AlreadyAddedTrackersScanRequested = false;

    // HMD pose in OpenVR
    public static (Vector3 Position, Quaternion Orientation)
        RawVrHmdPose = new(Vector3.Zero, Quaternion.Identity);

    // Amethyst language resource trees
    public static JsonObject
        LocalResources = new(), EnglishResources = new(), LanguageEnum = new();

    // Controllers' ID's (vr::k_unTrackedDeviceIndexInvalid for non-existent)
    public static (uint Left, uint Right) VrControllerIndexes;

    // Is NUX currently opened?
    public static bool IsNuxPending = false;

    // Flip defines for the base device - iteration persistent
    public static bool BaseFlip = false; // Assume non flipped

    // Flip defines for the override device - iteration persistent
    public static bool OverrideFlip = false; // Assume non flipped
    
    public static FileInfo GetProgramLocation()
    {
        return new FileInfo(Assembly.GetExecutingAssembly().Location);
    }

    public static DirectoryInfo GetK2AppDataTempDir()
    {
        return Directory.CreateDirectory(Path.GetTempPath() + "Amethyst");
    }

    public static string GetK2AppDataFileDir(string relativeFilePath)
    {
        Directory.CreateDirectory(Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Amethyst"));

        return Path.Join(Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData), "Amethyst", relativeFilePath);
    }

    public static string GetK2AppDataLogFileDir(string relativeFolderName, string relativeFilePath)
    {
        Directory.CreateDirectory(Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Amethyst", "logs", relativeFolderName));

        return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Amethyst", "logs", relativeFolderName, relativeFilePath);
    }

    // Fail with an exit code (don't delete .crash)
    public static void Fail(int code)
    {
        IsExitHandled = true;
        Environment.Exit(code);
    }

    // Show SteamVR toast / notification
    public static void ShowVrToast(string header, string text)
    {
        if (VrOverlayHandle == OpenVR.k_ulOverlayHandleInvalid ||
            string.IsNullOrEmpty(header) ||
            string.IsNullOrEmpty(text)) return;

        // Hide the current notification (if being shown)
        if (VrNotificationId != 0) // If valid
            OpenVR.Notifications.RemoveNotification(VrNotificationId);

        // Empty image data
        var notificationBitmap = new NotificationBitmap_t();

        // null is the icon/image texture
        OpenVR.Notifications.CreateNotification(
            VrOverlayHandle, 0, EVRNotificationType.Transient,
            header + '\n' + text, EVRNotificationStyle.Application,
            ref notificationBitmap, ref VrNotificationId);
    }

    // Show an app toast / notification
    public static void ShowToast(string header, string text,
        bool highPriority = false, string action = "none")
    {
        if (string.IsNullOrEmpty(header) ||
            string.IsNullOrEmpty(text)) return;

        var payload =
            $"<toast launch=\"action={action}&amp;actionId=00000\">" +
            "<visual><binding template = \"ToastGeneric\">" +
            $"<text>{header}</text>" +
            $"<text>{text}</text>" +
            "</binding></visual></toast>";

        AppNotification toast = new(payload)
        {
            Tag = "Tag_AmethystNotifications",
            Group = "Group_AmethystNotifications",
            Priority = highPriority
                ? AppNotificationPriority.High
                : AppNotificationPriority.Default
        };

        Shared.Main.NotificationManager.Show(toast);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    public static void ProcessToastArguments(
        AppNotificationActivatedEventArgs eventArgs)
    {
        // When a tracker's been auto-disabled
        if (eventArgs.Argument.Contains("focus_trackers"))
            Shared.Main.DispatcherQueue.TryEnqueue(
                async () =>
                {
                    // Bring Amethyst to front
                    SetForegroundWindow(Shared.Main.AppWindowId);

                    if (CurrentPageTag != "settings")
                    {
                        // Navigate to the settings page
                        Shared.Main.MainNavigationView.SelectedItem =
                            Shared.Main.MainNavigationView.MenuItems[1];

                        Shared.Main.NavigateToPage("settings",
                            new EntranceNavigationTransitionInfo());

                        await Task.Delay(500);
                    }

                    Shared.Settings.PageMainScrollViewer.UpdateLayout();
                    Shared.Settings.PageMainScrollViewer.ChangeView(null,
                        Shared.Settings.PageMainScrollViewer.ExtentHeight / 2.0, null);

                    await Task.Delay(500);

                    // Focus on the restart button
                    Shared.Settings.CheckOverlapsCheckBox.Focus(FocusState.Keyboard);
                });

        // When you need to restart OpenVR
        if (eventArgs.Argument.Contains("focus_restart"))
            Shared.Main.DispatcherQueue.TryEnqueue(
                async () =>
                {
                    // Bring Amethyst to front
                    SetForegroundWindow(Shared.Main.AppWindowId);

                    if (CurrentPageTag != "settings")
                    {
                        // Navigate to the settings page
                        Shared.Main.MainNavigationView.SelectedItem =
                            Shared.Main.MainNavigationView.MenuItems[1];

                        Shared.Main.NavigateToPage("settings",
                            new EntranceNavigationTransitionInfo());

                        await Task.Delay(500);
                    }

                    Shared.Settings.PageMainScrollViewer.UpdateLayout();
                    Shared.Settings.PageMainScrollViewer.ChangeView(null,
                        Shared.Settings.PageMainScrollViewer.ExtentHeight, null);

                    await Task.Delay(500);

                    // Focus on the restart button
                    Shared.Settings.RestartButton.Focus(FocusState.Keyboard);
                });

        // Else no click action requested ("none")
    }

    public static async Task HandleAppExit(int sleepMillis)
    {
        // Mark exiting as true
        IsExitingNow = true;
        Logger.Info("AppWindow.Closing handler called, starting the shutdown routine...");

        // Mark trackers as inactive
        AppTrackersInitialized = false;

        // Wait a moment & exit
        Logger.Info($"Shutdown actions completed, disconnecting devices and exiting in {sleepMillis}ms...");
        await Task.Delay(sleepMillis); // Sleep a bit for a proper server disconnect

        try
        {
            // Close the multi-process mutex
            Shared.Main.ApplicationMultiInstanceMutex.ReleaseMutex();
            Shared.Main.ApplicationMultiInstanceMutex.Dispose();
        }
        catch (Exception)
        {
            // ignored
        }

        try
        {
            // Unlock the crash file
            File.Delete(CrashFile.FullName);
        }
        catch (Exception)
        {
            // ignored
        }

        // We've (mostly) done what we had to
        IsExitHandled = true;

        try
        {
            // Disconnect all loaded devices
            TrackingDevices.TrackingDevicesList.Values
                .ToList().ForEach(device => device.Shutdown());
        }
        catch (Exception)
        {
            // ignored
        }
    }

    // Function to spawn default' enabled trackers
    public static async Task<bool> SpawnEnabledTrackers()
    {
        if (!K2AppTrackersSpawned)
        {
            Logger.Info("[K2Interfacing] Registering trackers now...");

            // K2Driver is now auto-adding default lower body trackers.
            // That means that ids are: W-0 L-1 R-2
            // We may skip downloading them then ^_~

            Logger.Info("[K2Interfacing] App will be using K2Driver's default prepended trackers!");

            // Helper bool array
            List<bool> spawned = new();

            // Create a dummy update vector
            List<(TrackerType Role, bool State)> trackerStatuses =
                (from tracker in AppData.Settings.TrackersVector
                    where tracker.IsActive
                    select (tracker.Role, true)).ToList();

            // Try 3 times (cause why not)
            if (trackerStatuses.Count > 0)
                for (var i = 0; i < 3; i++)
                {
                    // Update tracker statuses in the server
                    spawned.AddRange((await DriverClient.UpdateTrackerStates(trackerStatuses))!.Select(x => x.State));
                    await Task.Delay(15);
                }

            // If one or more trackers failed to spawn
            if (spawned.Count > 0 && spawned.Contains(false))
            {
                Logger.Info("One or more trackers couldn't be spawned after 3 tries. Giving up...");

                // Cause not checking anymore
                ServerDriverFailure = true;
                K2AppTrackersSpawned = false;
                AppTrackersInitialized = false;

                return false;
            }
        }

        // Notify that we're good now
        K2AppTrackersSpawned = true;
        AppTrackersInitialized = true;

        /*
         * Trackers are stealing input from controllers when first added,
         * due to some weird wonky stuff happening and OpenVR not expecting them.
         * We're gonna de-spawn them for 8 frames (100ms) and re-spawn after another
         */

        await Task.Delay(100);
        AppTrackersInitialized = false;
        await Task.Delay(500);
        AppTrackersInitialized = true;

        return true;
    }

    public static bool OpenVrStartup()
    {
        Logger.Info("Attempting connection to VRSystem... ");

        try
        {
            Logger.Info("Creating a cancellation token...");
            using var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(3));

            Logger.Info("Waiting for the VR System to initialize...");
            var eError = EVRInitError.None;
            OpenVR.Init(ref eError, EVRApplicationType.VRApplication_Overlay);

            Logger.Info("The VRSystem finished initializing...");
            if (eError != EVRInitError.None)
            {
                Logger.Error($"IVRSystem could not be initialized: EVRInitError Code {eError}");
                return false; // Catastrophic failure!
            }
        }
        catch (Exception e)
        {
            Logger.Error($"The VR System took too long to initialize ({e.Message}), giving up!");
            Environment.FailFast("The VR System took too long to initialize");
        }

        // We're good to go!
        Logger.Info("Looks like the VR System is ready to go!");

        // Initialize the overlay
        OpenVR.Overlay.CreateOverlay("k2vr.amethyst.desktop", "Amethyst", ref VrOverlayHandle);

        // Since we're ok, capture playspace details
        var trackingOrigin = OpenVR.System.GetRawZeroPoseToStandingAbsoluteTrackingPose();
        VrPlayspaceTranslation = trackingOrigin.GetPosition();
        VrPlayspaceOrientationQuaternion = trackingOrigin.GetOrientation();

        // Rescan controller ids
        VrControllerIndexes = (
            OpenVR.System.GetTrackedDeviceIndexForControllerRole(
                ETrackedControllerRole.LeftHand),
            OpenVR.System.GetTrackedDeviceIndexForControllerRole(
                ETrackedControllerRole.RightHand)
        );

        Logger.Info($"VR Playspace translation: \n{VrPlayspaceTranslation}");
        Logger.Info($"VR Playspace orientation: \n{VrPlayspaceOrientationQuaternion}");
        return true; // OK
    }

    public static bool EvrActionsStartup()
    {
        Logger.Info("Attempting to set up EVR Input Actions...");

        if (!EvrInput.InitInputActions())
        {
            Logger.Error("Could not set up Input Actions. Please check the upper log for further information.");
            ShowVrToast("EVR Input Actions Init Failure!",
                "Couldn't set up Input Actions. Please check the log file for further information.");

            return false;
        }

        Logger.Info("EVR Input Actions set up OK");
        return true;
    }

    public static uint InstallVrApplicationManifest()
    {
        if (OpenVR.Applications.IsApplicationInstalled("K2VR.Amethyst"))
        {
            Logger.Info("Amethyst manifest is already installed");
            return 1;
        }

        if (File.Exists(Path.Join(GetProgramLocation().DirectoryName, "Amethyst.vrmanifest")))
        {
            var appError = OpenVR.Applications.AddApplicationManifest(
                Path.Join(GetProgramLocation().DirectoryName, "Amethyst.vrmanifest"), false);

            if (appError != EVRApplicationError.None)
            {
                Logger.Warn($"Amethyst manifest not installed! Error: {appError}");
                return 2;
            }

            Logger.Info(
                $"Amethyst manifest installed at: {Path.Join(GetProgramLocation().DirectoryName, "Amethyst.vrmanifest")}");
            return 1;
        }

        Logger.Warn("Amethyst vr manifest (./Amethyst.vrmanifest) not found!");
        return 0;
    }

    public static void UninstallApplicationManifest()
    {
        if (OpenVR.Applications.IsApplicationInstalled("K2VR.Amethyst"))
        {
            OpenVR.Applications.RemoveApplicationManifest(
                Path.Join(GetProgramLocation().DirectoryName, "Amethyst.vrmanifest"));

            Logger.Info(
                $"ttempted to remove Amethyst manifest at: {Path.Join(GetProgramLocation().DirectoryName, "Amethyst.vrmanifest")}");
        }

        if (OpenVR.Applications.IsApplicationInstalled("K2VR.Amethyst"))
            Logger.Warn("Amethyst manifest removal failed! It may have been installed from somewhere else too");
        else
            Logger.Info("Amethyst manifest removal succeed");
    }

    public static async Task<Status> TestK2ServerConnection()

    {
        // Do not spawn 1000 voids, check how many do we have
        if (PingCheckingThreadsNumber <= MaxPingCheckingThreads)
        {
            // Add a new worker
            PingCheckingThreadsNumber += 1; // May be ++ too

            try
            {
                // Send a ping message and capture the data
                var result = await DriverClient.TestConnection();

                // Dump data to variables
                PingTime = result.ElpasedTime;
                ParsingTime = result.ReceiveTimestamp - result.SendTimestamp;

                // Log ?success
                Logger.Info(
                    $"Connection test has ended, [result: {(result.Status.StatusCode == StatusCode.OK ? "success" : "fail")}]");

                // Log some data if needed
                Logger.Info($"\nTested ping time: {PingTime} [ticks], " +
                            $"call/parsing time: {result.ReceiveTimestamp} [ticks], " +
                            $"flight-back time: {DateTime.Now.Ticks - result.ReceiveTimestamp} [ticks]");

                // Release
                PingCheckingThreadsNumber = Math.Clamp(
                    PingCheckingThreadsNumber - 1, 0, MaxPingCheckingThreads + 1);

                // Return the result
                return result.Status;
            }
            catch (Exception e)
            {
                // Log ?success
                Logger.Info($"Connection test has ended, [result: fail], got an exception: {e.Message}");

                // Release
                PingCheckingThreadsNumber = Math.Clamp(
                    PingCheckingThreadsNumber - 1, 0, MaxPingCheckingThreads + 1);

                return new Status(StatusCode.Unknown, "An exception occurred.");
            }
        }

        // else
        Logger.Error("Connection checking threads exceeds 3, aborting...");
        return new Status(StatusCode.Unavailable, "Too many simultaneous checking threads.");
    }

    public static async Task<(int ServerStatus, int APIStatus)> CheckK2ServerStatus()
    {
        // Don't check if already ok
        if (IsServerDriverPresent) return (1, (int)StatusCode.OK);

        try
        {
            /* Initialize the port */
            Logger.Info("Initializing the server IPC...");
            ;
            var initCode = DriverClient.InitAmethystServer();
            Status serverStatus = new();

            Logger.Info($"Server IPC initialization {(initCode == 0 ? "succeed" : "failed")}, exit code: {initCode}");

            /* Connection test and display ping */
            // We may wait a bit for it though...
            // ReSharper disable once InvertIf
            if (initCode == 0)
            {
                Logger.Info("Testing the connection...");

                for (var i = 0; i < 3; i++)
                {
                    Logger.Info($"Starting the test no {i + 1}...");
                    serverStatus = await TestK2ServerConnection();

                    // Not direct assignment since it's only a one-way check
                    if (serverStatus.StatusCode == StatusCode.OK)
                        IsServerDriverPresent = true;

                    else
                        Logger.Warn("Server status check failed! " +
                                    $"Code: {serverStatus.StatusCode}, " +
                                    $"Details: {serverStatus.Detail}");
                }
            }

            return initCode == 0
                // If the API is ok
                ? serverStatus.StatusCode == StatusCode.OK
                    // If the server is/isn't ok
                    ? (1, (int)serverStatus.StatusCode)
                    : (-1, (int)serverStatus.StatusCode)
                // If the API is not ok
                : (initCode, (int)StatusCode.Unknown);
        }
        catch (Exception e)
        {
            Logger.Warn("Server status check failed! " +
                        $"Exception: {e.Message}");

            return (-10, (int)StatusCode.Unknown);
        }

        /*
         * codes:
            all ok: 1
            server could not be reached: -1
            exception when trying to reach: -10
            could not create rpc channel: -2
            could not create rpc stub: -3

            fatal run-time failure: 10
         */
    }

    public static async Task K2ServerDriverRefresh()
    {
        if (!ServerDriverFailure)
            (ServerDriverStatusCode, ServerRpcStatusCode) = await CheckK2ServerStatus();
        else // Overwrite the status
            ServerDriverStatusCode = 10; // Fatal

        IsServerDriverPresent = false; // Assume fail
        ServerStatusString = LocalizedJsonString("/ServerStatuses/WTF");
        //"COULD NOT CHECK STATUS (\u15dc\u02ec\u15dc)\nE_WTF\nSomething's fucked a really big time.";

        switch (ServerDriverStatusCode)
        {
            case 1:
                ServerStatusString = LocalizedJsonString("/ServerStatuses/Success");
                //"Success! (Code 1)\nI_OK\nEverything's good!";

                IsServerDriverPresent = true;
                break; // Change to success

            case -1:
                ServerStatusString = LocalizedJsonString("/ServerStatuses/ConnectionError")
                    .Replace("{0}", ServerRpcStatusCode.ToString());
                //"SERVER CONNECTION ERROR (Code -1:{0})\nE_CONNECTION_ERROR\nCheck SteamVR add-ons (NOT overlays) and enable Amethyst.";
                break;

            case -10:
                ServerStatusString = LocalizedJsonString("/ServerStatuses/Exception")
                    .Replace("{0}", ServerRpcStatusCode.ToString());
                //"EXCEPTION WHILE CHECKING (Code -10)\nE_EXCEPTION_WHILE_CHECKING\nCheck SteamVR add-ons (NOT overlays) and enable Amethyst.";
                break;

            case -2:
                ServerStatusString = LocalizedJsonString("/ServerStatuses/RPCChannelFailure")
                    .Replace("{0}", ServerRpcStatusCode.ToString());
                //"RPC CHANNEL FAILURE (Code -2:{0})\nE_RPC_CHAN_FAILURE\nCould not connect to localhost:7135, is it already taken?";
                break;

            case -3:
                ServerStatusString = LocalizedJsonString("/ServerStatuses/RPCStubFailure")
                    .Replace("{0}", ServerRpcStatusCode.ToString());
                //"RPC/API STUB FAILURE (Code -3:{0})\nE_RPC_STUB_FAILURE\nCould not derive IK2DriverService! Is the protocol valid?";
                break;

            case 10:
                ServerStatusString = LocalizedJsonString("/ServerStatuses/ServerFailure");
                //"FATAL SERVER FAILURE (Code 10)\nE_FATAL_SERVER_FAILURE\nPlease restart, check logs and write to us on Discord.";
                break;

            default:
                ServerStatusString = LocalizedJsonString("/ServerStatuses/WTF");
                //"COULD NOT CHECK STATUS (\u15dc\u02ec\u15dc)\nE_WTF\nSomething's fucked a really big time.";
                break;
        }
    }

    public static async void K2ServerDriverSetup()
    {
        // Refresh the server driver status
        await K2ServerDriverRefresh();

        // Play an error sound if smth's wrong
        if (ServerDriverStatusCode != 1)
            AppSounds.PlayAppSound(AppSounds.AppSoundType.Error);

        else
            Shared.Main.DispatcherQueue.TryEnqueue(async () =>
            {
                // Sleep a bit before checking
                await Task.Delay(1000);

                if (Shared.General.ErrorWhatText is not null &&
                    Shared.General.ErrorWhatText.Visibility == Visibility.Visible)
                    AppSounds.PlayAppSound(AppSounds.AppSoundType.Error);
            });

        // LOG the status
        Logger.Info($"Current K2 Server status: {ServerStatusString}");
    }

    public static (bool Found, uint Index) FindVrTracker(
        string role, bool canBeAme = true, bool log = true)
    {
        // Loop through all devices
        for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
        {
            StringBuilder roleStringBuilder = new(1024);
            var roleError = ETrackedPropertyError.TrackedProp_Success;
            OpenVR.System.GetStringTrackedDeviceProperty(
                i, ETrackedDeviceProperty.Prop_ControllerType_String,
                roleStringBuilder, (uint)roleStringBuilder.Capacity, ref roleError);

            if (roleStringBuilder.Length <= 0)
                continue; // Don't waste our time

            // If we've found anything
            if (log) Logger.Info($"Found a device with roleHint: {roleStringBuilder}");

            // If we've actually found the one
            if (roleStringBuilder.ToString().IndexOf(role, StringComparison.OrdinalIgnoreCase) < 0) continue;

            var status = OpenVR.System.GetTrackedDeviceActivityLevel(i);
            if (status != EDeviceActivityLevel.k_EDeviceActivityLevel_UserInteraction &&
                status != EDeviceActivityLevel.k_EDeviceActivityLevel_UserInteraction_Timeout)
                continue;

            StringBuilder serialStringBuilder = new(1024);
            var serialError = ETrackedPropertyError.TrackedProp_Success;
            OpenVR.System.GetStringTrackedDeviceProperty(i, ETrackedDeviceProperty.Prop_SerialNumber_String,
                serialStringBuilder, (uint)serialStringBuilder.Capacity, ref serialError);

            // Log that we're finished
            if (log)
                Logger.Info($"Found an active {role} tracker with:\n    " +
                            $"hint: {roleStringBuilder}\n    " +
                            $"serial: {serialStringBuilder}\n    index: {i}");

            // Check if it's not ame
            var canReturn = true;
            if (!canBeAme) // If requested
                AppData.Settings.TrackersVector.Where(
                        tracker => serialStringBuilder.ToString() == tracker.Serial).ToList()
                    .ForEach(_ =>
                    {
                        if (log) Logger.Info("Skipping the latest found tracker because it's been added from Amethyst");
                        canReturn = false; // Maybe next time, bud
                    });

            // Return what we've got
            if (canReturn) return (true, i);
        }

        if (log)
            Logger.Warn($"Didn't find any {role} tracker in SteamVR " +
                        "with a proper role hint (Prop_ControllerType_String)");

        // We've failed if the loop's finished
        return (false, OpenVR.k_unTrackedDeviceIndexInvalid);
    }

    public static (Vector3 Position, Quaternion Orientation)
        GetVrTrackerPoseCalibrated(string nameContains, bool log = false)
    {
        var devicePose = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        OpenVR.System.GetDeviceToAbsoluteTrackingPose(
            ETrackingUniverseOrigin.TrackingUniverseStanding, 0, devicePose);

        var waistPair = FindVrTracker(nameContains, false, log);
        if (waistPair.Found)
        {
            // Extract pose from the returns
            // We don't care if it's invalid by any chance
            var waistPose = devicePose[waistPair.Index];

            // Get pos & rot
            return (Vector3.Transform(
                    waistPose.mDeviceToAbsoluteTracking.GetPosition() - VrPlayspaceTranslation,
                    Quaternion.Inverse(VrPlayspaceOrientationQuaternion)),
                Quaternion.Inverse(VrPlayspaceOrientationQuaternion) *
                waistPose.mDeviceToAbsoluteTracking.GetOrientation());
        }

        if (log)
            Logger.Warn("Either waist tracker doesn't exist or its role hint (Prop_ControllerType_String) was invalid");

        // We've failed if the executor got here
        return (Vector3.Zero, Plugins.GetHmdPoseCalibrated.Orientation);
    }

    public static void UpdateServerStatus()
    {
        // Check with this one, should be the same for all anyway
        if (Shared.General.ServerErrorWhatText is not null)
        {
            Shared.General.ServerErrorWhatText.Visibility =
                IsServerDriverPresent ? Visibility.Collapsed : Visibility.Visible;
            Shared.General.ServerErrorWhatGrid.Visibility =
                IsServerDriverPresent ? Visibility.Collapsed : Visibility.Visible;
            Shared.General.ServerErrorButtonsGrid.Visibility =
                IsServerDriverPresent ? Visibility.Collapsed : Visibility.Visible;
            Shared.General.ServerErrorLabel.Visibility =
                IsServerDriverPresent ? Visibility.Collapsed : Visibility.Visible;

            // Split status and message by \n
            Shared.General.ServerStatusLabel.Text =
                StringUtils.SplitStatusString(ServerStatusString)[0];
            Shared.General.ServerErrorLabel.Text =
                StringUtils.SplitStatusString(ServerStatusString)[1];
            Shared.General.ServerErrorWhatText.Text =
                StringUtils.SplitStatusString(ServerStatusString)[2];

            // Optionally setup & show the re-register button
            Shared.General.ReRegisterButton.Visibility =
                ServerDriverStatusCode == -1
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            Shared.General.ServerOpenDiscordButton.Height =
                ServerDriverStatusCode == -1 ? 40 : 65;
        }

        // Block some things if server isn't working properly
        if (IsServerDriverPresent) return;
        Logger.Error("An error occurred and the app couldn't connect to K2 Server. " +
                     "Please check the upper message for more info.");

        if (Shared.General.ErrorWhatText is null) return;
        Logger.Info("[Server Error] Entering the server error state...");

        // Hide device error labels (if any)
        Shared.General.ErrorWhatText.Visibility = Visibility.Collapsed;
        Shared.General.ErrorWhatGrid.Visibility = Visibility.Collapsed;
        Shared.General.ErrorButtonsGrid.Visibility = Visibility.Collapsed;
        Shared.General.TrackingDeviceErrorLabel.Visibility = Visibility.Collapsed;

        // Block spawn|offsets|calibration buttons
        Shared.General.ToggleTrackersButton.IsEnabled = false;
        Shared.General.CalibrationButton.IsEnabled = false;
        Shared.General.OffsetsButton.IsEnabled = false;
    }

    // Update HMD pose from OpenVR -> called in K2Main
    public static void UpdateHMDPosAndRot()
    {
        // Capture RAW HMD pose
        var devicePose = new TrackedDevicePose_t[1]; // HMD only
        OpenVR.System.GetDeviceToAbsoluteTrackingPose(
            ETrackingUniverseOrigin.TrackingUniverseStanding, 0, devicePose);

        // Assert that HMD is at index 0
        if (OpenVR.System.GetTrackedDeviceClass(0) == ETrackedDeviceClass.HMD)
            RawVrHmdPose = (devicePose[0].mDeviceToAbsoluteTracking.GetPosition(),
                devicePose[0].mDeviceToAbsoluteTracking.GetOrientation());

        // Capture play-space details
        var trackingOrigin = OpenVR.System.GetRawZeroPoseToStandingAbsoluteTrackingPose();
        VrPlayspaceTranslation = trackingOrigin.GetPosition();
        VrPlayspaceOrientationQuaternion = trackingOrigin.GetOrientation();
    }

    [DllImport("user32.dll")]
    public static extern nint GetActiveWindow();

    public static bool IsCurrentWindowActive()
    {
        if (Shared.Main.AppWindow is null)
            return true; // Give up k?

        return GetActiveWindow() == Shared.Main.AppWindowId;
    }

    public static bool IsDashboardOpen()
    {
        // Check if we're running on null
        StringBuilder systemStringBuilder = new(1024);
        var propertyError = ETrackedPropertyError.TrackedProp_Success;
        OpenVR.System.GetStringTrackedDeviceProperty(
            OpenVR.k_unTrackedDeviceIndex_Hmd, ETrackedDeviceProperty.Prop_TrackingSystemName_String,
            systemStringBuilder, 1024, ref propertyError);

        // Just return true for debug reasons
        if (systemStringBuilder.ToString().Contains("null") ||
            propertyError != ETrackedPropertyError.TrackedProp_Success)
            return true;

        // Also check if we're not idle / standby
        var status = OpenVR.System.GetTrackedDeviceActivityLevel(OpenVR.k_unTrackedDeviceIndex_Hmd);
        if (status != EDeviceActivityLevel.k_EDeviceActivityLevel_UserInteraction &&
            status != EDeviceActivityLevel.k_EDeviceActivityLevel_UserInteraction_Timeout)
            return false; // Standby - hide

        // Check if the dashboard is open
        return OpenVR.Overlay.IsDashboardVisible();
    }

    // Return a language name by code
    // Input: The current (or deduced) language key / en
    // Returns: LANG_NATIVE (LANG_LOCALIZED) / Nihongo (Japanese)
    // https://stackoverflow.com/a/10607146/13934610
    // https://stackoverflow.com/a/51867679/13934610
    public static string GetLocalizedLanguageName(string languageKey)
    {
        try
        {
            // Load the locales.json from Assets/Strings/
            var resourcePath = Path.Join(
                GetProgramLocation().DirectoryName,
                "Assets", "Strings", "locales.json");

            // If the specified language doesn't exist somehow, fallback to 'en'
            if (!File.Exists(resourcePath))
            {
                Logger.Error("Could not load language enumeration resources at " +
                             $"\"{resourcePath}\", app interface will be broken!");
                return languageKey; // Give up on trying
            }

            // Parse the loaded json
            var jsonObject = JsonObject.Parse(File.ReadAllText(resourcePath));

            // Check if the resource root is fine
            if (jsonObject is null || jsonObject.Count <= 0)
            {
                Logger.Error("The current language enumeration resource root is empty! " +
                             "App interface will be broken!");
                return languageKey; // Give up on trying
            }

            // If the language key is the current language, don't split the name
            if (AppData.Settings.AppLanguage == languageKey)
                return jsonObject.GetNamedObject(AppData.Settings.AppLanguage)
                    .GetNamedString(AppData.Settings.AppLanguage);

            // Else split the same way as in docs
            return jsonObject.GetNamedObject(languageKey).GetNamedString(languageKey) +
                   " (" + jsonObject.GetNamedObject(AppData.Settings.AppLanguage).GetNamedString(languageKey) + ")";
        }
        catch (Exception e)
        {
            Logger.Error($"JSON error at key: \"{languageKey}\"! Message: {e.Message}");

            // Else return they key alone
            return languageKey;
        }
    }

    // Load the current desired resource JSON into app memory
    public static void LoadJsonStringResources(string languageKey)
    {
        try
        {
            Logger.Info($"Searching for language resources with key \"{languageKey}\"...");

            var resourcePath = Path.Join(
                GetProgramLocation().DirectoryName,
                "Assets", "Strings", languageKey + ".json");

            // If the specified language doesn't exist somehow, fallback to 'en'
            if (!File.Exists(resourcePath))
            {
                Logger.Warn("Could not load language resources at " +
                            $"\"{resourcePath}\", falling back to 'en' (en.json)!");

                resourcePath = Path.Join(
                    GetProgramLocation().DirectoryName,
                    "Assets", "Strings", "en.json");
            }

            // If failed again, just give up
            if (!File.Exists(resourcePath))
            {
                Logger.Warn("Could not load language resources at " +
                            $"\"{resourcePath}\", the app interface will be broken!");
                return; // Just give up
            }

            // If everything's ok, load the resources into the current resource tree

            // Parse the loaded json
            LocalResources = JsonObject.Parse(File.ReadAllText(resourcePath));

            // Check if the resource root is fine
            if (LocalResources is null || LocalResources.Count <= 0)
                Logger.Error("The current resource root is empty! App interface will be broken!");
            else
                Logger.Info($"Successfully loaded language resources with key \"{languageKey}\"!");
        }
        catch (Exception e)
        {
            Logger.Error($"JSON error at key: \"{languageKey}\"! Message: {e.Message}");
        }
    }

    // Load the current desired resource JSON into app memory
    public static void LoadJsonStringResourcesEnglish()
    {
        try
        {
            Logger.Info("Searching for shared (English) language resources...");

            var resourcePath = Path.Join(
                GetProgramLocation().DirectoryName,
                "Assets", "Strings", "en.json");

            // If failed again, just give up
            if (!File.Exists(resourcePath))
            {
                Logger.Warn("Could not load language resources at \"{resourcePath}\", " +
                            "falling back to the current one! The app interface may be broken!");

                // Override the current english resource tree
                EnglishResources = LocalResources;
                return; // Just give up
            }

            // If everything's ok, load the resources into the current resource tree

            // Parse the loaded json
            EnglishResources = JsonObject.Parse(File.ReadAllText(resourcePath));

            // Check if the resource root is fine
            if (EnglishResources is null || EnglishResources.Count <= 0)
                Logger.Error("The current resource root is empty! App interface will be broken!");
            else
                Logger.Info("Successfully loaded language resources with key \"en\"!");
        }
        catch (Exception e)
        {
            Logger.Error($"JSON error at key: \"en\"! Message: {e.Message}");
        }
    }

    // Get a string from runtime JSON resources, language from settings
    public static string LocalizedJsonString(string resourceKey,
        [CallerLineNumber] int lineNumber = 0, [CallerFilePath] string filePath = "",
        [CallerMemberName] string memberName = "")
    {
        try
        {
            // Check if the resource root is fine
            if (LocalResources is not null && LocalResources.Count > 0)
                return LocalResources.GetNamedString(resourceKey);

            Logger.Error("The current resource root is empty! App interface will be broken!");
            return resourceKey; // Just give up
        }
        catch (Exception e)
        {
            Logger.Error($"JSON error at key: \"{resourceKey}\"! Message: {e.Message}\n" +
                         "Path of the local caller that requested the localized resource string: " +
                         $"{Path.GetFileName(filePath)}::{memberName}:{lineNumber}");

            // Else return they key alone
            return resourceKey;
        }
    }

    public static class Plugins
    {
        public static (Vector3 Position, Quaternion Orientation) GetHmdPose => RawVrHmdPose;

        public static (Vector3 Position, Quaternion Orientation) GetHmdPoseCalibrated => (
            Vector3.Transform(RawVrHmdPose.Position - VrPlayspaceTranslation,
                Quaternion.Inverse(VrPlayspaceOrientationQuaternion)),
            Quaternion.Inverse(VrPlayspaceOrientationQuaternion) * RawVrHmdPose.Orientation);

        public static (Vector3 Position, Quaternion Orientation) GetLeftControllerPose()
        {
            var devicePose = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
            OpenVR.System.GetDeviceToAbsoluteTrackingPose(
                ETrackingUniverseOrigin.TrackingUniverseStanding, 0, devicePose);

            // Get pos & rot -> EigenUtils' gonna do this stuff for us
            if (VrControllerIndexes.Left != OpenVR.k_unTrackedDeviceIndexInvalid)
                return (devicePose[VrControllerIndexes.Left].mDeviceToAbsoluteTracking.GetPosition(),
                    devicePose[VrControllerIndexes.Left].mDeviceToAbsoluteTracking.GetOrientation());

            return (Vector3.Zero, Quaternion.Identity); // else
        }

        public static (Vector3 Position, Quaternion Orientation) GetLeftControllerPoseCalibrated()
        {
            var (position, orientation) = GetLeftControllerPose();
            return (Vector3.Transform(position - VrPlayspaceTranslation,
                    Quaternion.Inverse(VrPlayspaceOrientationQuaternion)),
                Quaternion.Inverse(VrPlayspaceOrientationQuaternion) * orientation);
        }

        public static (Vector3 Position, Quaternion Orientation) GetRightControllerPose()
        {
            var devicePose = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
            OpenVR.System.GetDeviceToAbsoluteTrackingPose(
                ETrackingUniverseOrigin.TrackingUniverseStanding, 0, devicePose);

            // Get pos & rot -> EigenUtils' gonna do this stuff for us
            if (VrControllerIndexes.Right != OpenVR.k_unTrackedDeviceIndexInvalid)
                return (devicePose[VrControllerIndexes.Right].mDeviceToAbsoluteTracking.GetPosition(),
                    devicePose[VrControllerIndexes.Right].mDeviceToAbsoluteTracking.GetOrientation());

            return (Vector3.Zero, Quaternion.Identity); // else
        }

        public static (Vector3 Position, Quaternion Orientation) GetRightControllerPoseCalibrated()
        {
            var (position, orientation) = GetRightControllerPose();
            return (Vector3.Transform(position - VrPlayspaceTranslation,
                    Quaternion.Inverse(VrPlayspaceOrientationQuaternion)),
                Quaternion.Inverse(VrPlayspaceOrientationQuaternion) * orientation);
        }

        public static string RequestLocalizedString(string key, string guid)
        {
            try
            {
                if (string.IsNullOrEmpty(guid) || !TrackingDevices.TrackingDevicesList.ContainsKey(guid))
                {
                    Logger.Info("[Requested by UNKNOWN DEVICE CALLER] " +
                                "Null, empty or invalid GUID was passed to SetLocalizationResourcesRoot, aborting!");
                    return LocalizedJsonString(key); // Just give up
                }

                // Check if the resource root is fine
                var resourceRoot = TrackingDevices.GetDevice(guid).Device.LocalizationResourcesRoot.Root;
                if (resourceRoot is not null && resourceRoot.Count > 0)
                    return resourceRoot.GetNamedString(key);

                Logger.Error($"The resource root of device {guid} is empty! Its interface will be broken!");
                return LocalizedJsonString(key); // Just give up
            }
            catch (Exception e)
            {
                Logger.Error($"JSON error at key: \"{key}\"! Message: {e.Message}\n" +
                             $"GUID of the {{ get; }} caller device: {guid}");

                // Else return they key alone
                return key;
            }
        }

        public static bool SetLocalizationResourcesRoot(string path, string guid)
        {
            try
            {
                Logger.Info($"[Requested by device with GUID {guid}] " +
                            $"Searching for language resources with key \"{AppData.Settings.AppLanguage}\"...");

                if (string.IsNullOrEmpty(guid) || !TrackingDevices.TrackingDevicesList.ContainsKey(guid))
                {
                    Logger.Info("[Requested by UNKNOWN DEVICE CALLER] " +
                                "Null, empty or invalid GUID was passed to SetLocalizationResourcesRoot, aborting!");
                    return false; // Just give up
                }

                if (!Directory.Exists(path))
                {
                    Logger.Info($"[Requested by device with GUID {guid}] " +
                                $"Could not find any language enumeration resources in \"{path}\"! " +
                                $"Interface of device {guid} will be broken!");
                    return false; // Just give up
                }

                var resourcePath = Path.Join(path, AppData.Settings.AppLanguage + ".json");

                // If the specified language doesn't exist somehow, fallback to 'en'
                if (!File.Exists(resourcePath))
                {
                    Logger.Warn($"[Requested by device with GUID {guid}] " +
                                "Could not load language resources at " +
                                $"\"{resourcePath}\", falling back to 'en' (en.json)!");

                    resourcePath = Path.Join(path, "en.json");
                }

                // If failed again, just give up
                if (!File.Exists(resourcePath))
                {
                    Logger.Error($"[Requested by device with GUID {guid}] " +
                                 $"Could not load language resources at \"{resourcePath}\"," +
                                 $"for device {guid}! Its interface will be broken!");
                    return false; // Just give up
                }

                // If everything's ok, load the resources into the current resource tree

                // Parse the loaded json
                TrackingDevices.GetDevice(guid).Device.LocalizationResourcesRoot =
                    (JsonObject.Parse(File.ReadAllText(resourcePath)), resourcePath);

                // Check if the resource root is fine
                var resourceRoot = TrackingDevices.GetDevice(guid).Device.LocalizationResourcesRoot.Root;
                if (resourceRoot is null || resourceRoot.Count <= 0)
                {
                    Logger.Error($"[Requested by device with GUID {guid}] " +
                                 $"Could not load language resources at \"{resourcePath}\"," +
                                 $"for device {guid}! Its interface will be broken!");
                    return false; // Just give up
                }

                // Still here? 
                Logger.Info($"[Requested by device with GUID {guid}] " +
                            $"Successfully loaded language resources with key \"{AppData.Settings.AppLanguage}\"!");
                return true; // Winning it, yay!
            }
            catch (Exception e)
            {
                Logger.Error($"[Requested by device with GUID {guid}] " +
                             $"JSON error at key: \"{AppData.Settings.AppLanguage}\"! Message: {e.Message}");
                return false; // Just give up
            }
        }

        public static void RefreshApplicationInterface()
        {
            // Parse the request - update
            Shared.Main.DispatcherQueue.TryEnqueue(() =>
            {
                // Force refresh all the valid pages
                Shared.Events.RequestInterfaceReload(false);

                // Update other components (may be moved to MVVM)
                TrackingDevices.HandleDeviceRefresh(false);
                TrackingDevices.UpdateTrackingDevicesInterface();
            });
        }
    }
}