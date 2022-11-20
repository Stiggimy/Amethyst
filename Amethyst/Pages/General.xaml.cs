// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using Amethyst.Classes;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;
using Windows.Media.Core;
using Amethyst.Plugins.Contract;
using Amethyst.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Valve.VR;
using Windows.System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Microsoft.UI.Xaml.Controls.Primitives;
using Amethyst.MVVM;
using static Amethyst.Classes.Shared.TeachingTips;
using System.Xml.Linq;
using System.Threading;
using Microsoft.UI.Xaml.Markup;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Amethyst.Pages;

/// <summary>
///     An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class General : Page, INotifyPropertyChanged
{
    private bool _showSkeletonPrevious = true;
    private bool _generalPageLoadedOnce = false;
    private static string _calibratingDeviceGuid = "";

    private bool _calibrationPending = false;
    private bool _autoCalibration_StillPending = false;

    private IEnumerable<AppTracker> EnabledTrackers =>
        AppData.Settings.TrackersVector.Where(x => x.IsActive);

    public General()
    {
        InitializeComponent();

        Logger.Info($"Constructing page: '{GetType().FullName}'...");

        Shared.General.ToggleTrackersButton = ToggleTrackersButton;
        Shared.General.SkeletonToggleButton = SkeletonToggleButton;
        Shared.General.ForceRenderCheckBox = ForceRenderCheckBox;
        Shared.General.OffsetsButton = OffsetsButton;
        Shared.General.CalibrationButton = CalibrationButton;
        Shared.General.ReRegisterButton = ReRegisterButton;
        Shared.General.ServerOpenDiscordButton = ServerOpenDiscordButton;
        Shared.General.VersionLabel = VersionLabel;
        Shared.General.DeviceNameLabel = SelectedDeviceNameLabel;
        Shared.General.DeviceStatusLabel = TrackingDeviceStatusLabel;
        Shared.General.ErrorWhatText = ErrorWhatText;
        Shared.General.TrackingDeviceErrorLabel = TrackingDeviceErrorLabel;
        Shared.General.ServerStatusLabel = ServerStatusLabel;
        Shared.General.ServerErrorLabel = ServerErrorLabel;
        Shared.General.ServerErrorWhatText = ServerErrorWhatText;
        Shared.General.ForceRenderText = ForceRenderText;
        Shared.General.OffsetsControlHostGrid = OffsetsControlHostGrid;
        Shared.General.ErrorButtonsGrid = ErrorButtonsGrid;
        Shared.General.ErrorWhatGrid = ErrorWhatGrid;
        Shared.General.ServerErrorWhatGrid = ServerErrorWhatGrid;
        Shared.General.ServerErrorButtonsGrid = ServerErrorButtonsGrid;
        Shared.General.ToggleFreezeButton = ToggleFreezeButton;
        Shared.General.FreezeOnlyLowerToggle = FreezeOnlyLowerToggle;
        Shared.General.AdditionalDeviceErrorsHyperlink = AdditionalDeviceErrorsHyperlink;

        Shared.TeachingTips.GeneralPage.ToggleTrackersTeachingTip = ToggleTrackersTeachingTip;
        Shared.TeachingTips.GeneralPage.StatusTeachingTip = StatusTeachingTip;

        Logger.Info($"Registering devices MVVM for page: '{GetType().FullName}'...");
        TrackingDeviceTreeView.ItemsSource = TrackingDevices.TrackingDevicesList.Values;

        Logger.Info($"Setting graphical resources for: '{CalibrationPreviewMediaElement.GetType().FullName}'...");
        CalibrationPreviewMediaElement.Source = MediaSource.CreateFromUri(
            new Uri(Path.Join(Interfacing.GetProgramLocation().DirectoryName, "Assets", "CalibrationDirections.mp4")));

        Logger.Info("Registering a detached binary semaphore " +
                    $"reload handler for '{GetType().FullName}'...");

        Task.Run(() =>
        {
            Shared.Semaphores.ReloadGeneralPageSemaphore =
                new Semaphore(0, 1);

            while (true)
            {
                // Wait for a reload signal (blocking)
                Shared.Semaphores.ReloadGeneralPageSemaphore.WaitOne();

                // Reload & restart the waiting loop
                if (_generalPageLoadedOnce)
                    Shared.Main.DispatcherQueue.TryEnqueue(
                        async () => { await Page_LoadedHandler(); });

                Task.Delay(100); // Sleep a bit
            }
        });
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Logger.Info($"Re/Loading page: '{GetType().FullName}'...");
        Interfacing.CurrentAppState = "general";

        // Execute the handler
        await Page_LoadedHandler();
        OnPropertyChanged(); // All

        // Mark as loaded
        _generalPageLoadedOnce = true;
    }

    private async Task Page_LoadedHandler()
    {
        // Start the main loop since we're done with basic setup
        if (!_generalPageLoadedOnce)
        {
            Logger.Info("Basic setup done! Starting the main loop now...");
            Shared.Semaphores.SmphSignalStartMain.Release();
        }

        // Update the internal version
        VersionLabel.Text = $"v{AppData.K2InternalVersion}";

        // Try auto-spawning trackers if stated so
        if (!Shared.General.GeneralTabSetupFinished && // If first-time
            Interfacing.IsServerDriverPresent && // If the driver's ok
            AppData.Settings.AutoSpawnEnabledJoints) // If autospawn
        {
            if (await Interfacing.SpawnEnabledTrackers())
            {
                // Mark as spawned
                ToggleTrackersButton.IsChecked = true;
                CalibrationButton.IsEnabled = true;
            }

            // Cry about it
            else
            {
                Interfacing.ServerDriverFailure = true; // WAAAAAAA
                Interfacing.K2ServerDriverSetup(); // Refresh
                Interfacing.ShowToast(
                    Interfacing.LocalizedJsonString("/SharedStrings/Toasts/AutoSpawnFailed/Title"),
                    Interfacing.LocalizedJsonString("/SharedStrings/Toasts/AutoSpawnFailed"),
                    true); // High priority - it's probably a server failure

                Interfacing.ShowVrToast(
                    Interfacing.LocalizedJsonString("/SharedStrings/Toasts/AutoSpawnFailed/Title"),
                    Interfacing.LocalizedJsonString("/SharedStrings/Toasts/AutoSpawnFailed"));
            }
        }

        // Refresh the server status
        await Interfacing.K2ServerDriverRefresh();

        // Update things
        Interfacing.UpdateServerStatus();
        TrackingDevices.UpdateTrackingDevicesInterface();

        // Reload offset values
        Logger.Info($"Force refreshing offsets MVVM for page: '{GetType().FullName}'...");
        AppData.Settings.TrackersVector.ForEach(x => x.OnPropertyChanged());

        // Reload tracking devices
        Logger.Info($"Force refreshing devices MVVM for page: '{GetType().FullName}'...");
        TrackingDevices.TrackingDevicesList.Values.ToList().ForEach(x => x.OnPropertyChanged());

        // Notify of the setup's end
        Shared.General.GeneralTabSetupFinished = true;

        // Setup the preview button
        SetSkeletonVisibility(AppData.Settings.SkeletonPreviewEnabled);
        SetSkeletonForce(AppData.Settings.ForceSkeletonPreview);

        // Setup the freeze button
        ToggleFreezeButton.IsChecked = Interfacing.IsTrackingFrozen;
        FreezeOnlyLowerToggle.IsChecked = AppData.Settings.FreezeLowerBodyOnly;
        ToggleFreezeButton.Content = Interfacing.LocalizedJsonString(
            Interfacing.IsTrackingFrozen
                ? "/GeneralPage/Buttons/Skeleton/Unfreeze"
                : "/GeneralPage/Buttons/Skeleton/Freeze");

        // Set up the co/re/disconnect button
        if (!Interfacing.K2AppTrackersSpawned)
        {
            ToggleTrackersButton.IsChecked = false;
            ToggleTrackersButton.Content =
                Interfacing.LocalizedJsonString("/GeneralPage/Buttons/TrackersToggle/Connect");
        }
        else
        {
            ToggleTrackersButton.IsChecked = Interfacing.K2AppTrackersInitialized;
            ToggleTrackersButton.Content = Interfacing.LocalizedJsonString(
                Interfacing.K2AppTrackersInitialized
                    ? "/GeneralPage/Buttons/TrackersToggle/Disconnect"
                    : "/GeneralPage/Buttons/TrackersToggle/Reconnect");
        }

        // Set uop the skeleton toggle button
        SkeletonToggleButton.Content = Interfacing.LocalizedJsonString(
            AppData.Settings.SkeletonPreviewEnabled
                ? "/GeneralPage/Buttons/Skeleton/Hide"
                : "/GeneralPage/Buttons/Skeleton/Show");
    }

    private void NoCalibrationTeachingTip_Closed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        Shared.Main.InterfaceBlockerGrid.IsHitTestVisible = false;
    }

    private void CancelPaneClosing(SplitView sender, SplitViewPaneClosingEventArgs args)
    {
        args.Cancel = true;
    }

    private void DiscardOffsetsButton_Click(object sender, RoutedEventArgs e)
    {
        // Discard backend offsets' values by re-reading them from settings
        AppData.Settings.ReadSettings();

        // Reload offset values
        Logger.Info($"Force refreshing offsets MVVM for page: '{GetType().FullName}'...");
        AppData.Settings.TrackersVector.ForEach(x => x.OnPropertyChanged());

        // Close the pane now
        OffsetsView.DisplayMode = SplitViewDisplayMode.Overlay;
        OffsetsView.IsPaneOpen = false;

        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.GoBack);

        AllowNavigation(true);
        Interfacing.CurrentAppState = "general";
    }

    private void SaveOffsetsButton_Click(object sender, RoutedEventArgs e)
    {
        // Close the pane now
        OffsetsView.DisplayMode = SplitViewDisplayMode.Overlay;
        OffsetsView.IsPaneOpen = false;

        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Hide);

        AllowNavigation(true);
        Interfacing.CurrentAppState = "general";

        // Save backend offsets' values to settings/file
        AppData.Settings.SaveSettings();
    }

    private async void TrackingDeviceTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        // Don't even care if we're not set up yet
        if (!Shared.General.GeneralTabSetupFinished) return;

        var node = args.InvokedItem as TrackingDevice;

        // Block erred device selects
        if (node.StatusError)
        {
            // Set the correct target
            NoCalibrationTeachingTip.Target =
                (FrameworkElement)TrackingDeviceTreeView.ContainerFromItem(args.InvokedItem);

            // Hide the tail and open the tip
            NoCalibrationTeachingTip.TailVisibility = TeachingTipTailVisibility.Collapsed;
            NoCalibrationTeachingTip.PreferredPlacement = TeachingTipPlacementMode.Bottom;

            Shared.Main.InterfaceBlockerGrid.IsHitTestVisible = true;
            NoCalibrationTeachingTip.IsOpen = true;

            await Task.Delay(300);
            return; // Give up
        }

        // Show the calibration choose pane / calibration
        AutoCalibrationPane.Visibility = Visibility.Collapsed;
        ManualCalibrationPane.Visibility = Visibility.Collapsed;

        CalibrationModeSelectView.DisplayMode = SplitViewDisplayMode.Inline;
        CalibrationModeSelectView.IsPaneOpen = true;

        CalibrationRunningView.DisplayMode = SplitViewDisplayMode.Overlay;
        CalibrationRunningView.IsPaneOpen = false;

        AllowNavigation(false);

        // Set the app state
        Interfacing.CurrentAppState = "calibration";

        // Set the calibration device
        _calibratingDeviceGuid = node.Guid;

        _showSkeletonPrevious = AppData.Settings.SkeletonPreviewEnabled; // Back up
        AppData.Settings.SkeletonPreviewEnabled = true; // Change to show
        SetSkeletonVisibility(true); // Change to show

        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Show);

        // If auto-calibration is not supported, proceed straight to manual
        // Test: supports if the device provides a head joint / otherwise not
        if (node.TrackedJoints.Any(x => x.Role != TrackedJointType.JointHead)) return;

        // Still here? the test must have failed then
        Logger.Info($"Device ({node.Name}, {node.Guid}) does not provide a {TrackedJointType.JointHead}" +
                    "and can't be calibrated with automatic calibration! Proceeding to manual now...");

        // Open the pane and start the calibration
        await ExecuteManualCalibration();
    }

    private void AutoCalibrationButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteAutomaticCalibration();
    }

    private async void ManualCalibrationButton_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteManualCalibration();
    }

    private async void StartAutoCalibrationButton_Click(object sender, RoutedEventArgs e)
    {
        // Setup the calibration image : start
        CalibrationPreviewMediaElement.MediaPlayer.Play();

        // Set the [calibration pending] bool
        _calibrationPending = true;
        _autoCalibration_StillPending = true;

        // Play a nice sound - starting
        AppSounds.PlayAppSound(AppSounds.AppSoundType.CalibrationStart);

        // Disable the start button and change [cancel]'s text
        StartAutoCalibrationButton.IsEnabled = false;
        CalibrationPointsNumberBox.IsEnabled = false;

        DiscardAutoCalibrationButton.Content =
            Interfacing.LocalizedJsonString("/GeneralPage/Buttons/Abort");

        // Mark what are we doing
        AppData.Settings.DeviceAutoCalibration[_calibratingDeviceGuid] = true;

        // Mark as calibrated for no preview
        AppData.Settings.DeviceMatricesCalibrated[_calibratingDeviceGuid] = false;

        // Reset the origin
        AppData.Settings.DeviceCalibrationOrigins[_calibratingDeviceGuid] = Vector3.Zero;

        // Setup helper variables
        List<Vector3> hmdPositions = new(), headPositions = new();
        await Task.Delay(1000);

        // Loop over total 3 points (by default)
        for (var point = 0; point < AppData.Settings.CalibrationPointsNumber; point++)
        {
            // Wait for the user to move
            CalibrationInstructionsLabel.Text = CalibrationPointsFormat(
                Interfacing.LocalizedJsonString("/GeneralPage/Calibration/Captions/Move"),
                point + 1, AppData.Settings.CalibrationPointsNumber);

            for (var i = 3; i >= 0; i--)
            {
                if (!_calibrationPending) break; // Check for exiting

                // Update the countdown label
                CalibrationCountdownLabel.Text = i.ToString();

                // Exit if aborted
                if (!_calibrationPending) break;

                // Play a nice sound - tick / move
                if (i > 0) // Don't play the last one!
                    AppSounds.PlayAppSound(AppSounds.AppSoundType.CalibrationTick);

                await Task.Delay(1000);
                if (!_calibrationPending) break; // Check for exiting
            }

            CalibrationInstructionsLabel.Text = CalibrationPointsFormat(
                Interfacing.LocalizedJsonString("/GeneralPage/Calibration/Captions/Stand"),
                point + 1, AppData.Settings.CalibrationPointsNumber);

            for (var i = 3; i >= 0; i--)
            {
                if (!_calibrationPending) break; // Check for exiting

                // Update the countdown label
                CalibrationCountdownLabel.Text = i.ToString();

                // Play a nice sound - tick / stand
                if (i > 0) // Don't play the last one!
                    AppSounds.PlayAppSound(AppSounds.AppSoundType.CalibrationTick);

                // Capture user's position at t_end-1
                switch (i)
                {
                    case 1:
                        // Capture positions
                        hmdPositions.Add(Interfacing.Plugins.GetHmdPoseCalibrated.Position);
                        headPositions.Add(Interfacing.DeviceHookJointPosition.ValueOr(_calibratingDeviceGuid));
                        break;

                    case 0:
                        CalibrationInstructionsLabel.Text = Interfacing.LocalizedJsonString(
                            "/GeneralPage/Calibration/Captions/Captured");
                        break;
                }

                await Task.Delay(1000);
                if (!_calibrationPending) break; // Check for exiting
            }

            // Exit if aborted
            if (!_calibrationPending) break;

            // Play a nice sound - tick / captured
            AppSounds.PlayAppSound(AppSounds.AppSoundType.CalibrationPointCaptured);

            await Task.Delay(1000);
            if (!_calibrationPending) break; // Check for exiting
        }

        // Do the actual calibration after capturing points
        if (_calibrationPending)
        {
            // Calibrate (AmethystSupport/CLI)
            var (translation, rotation) =
                AmethystSupport.Calibration.SVD(headPositions, hmdPositions);

            Logger.Info("Automatic calibration concluded!\n" +
                        $"Head positions: {headPositions}\n" +
                        $"HMD positions: {hmdPositions}\n" +
                        $"Recovered t: {translation}\n" +
                        $"Recovered R: {rotation}");

            AppData.Settings.DeviceCalibrationRotationMatrices[_calibratingDeviceGuid] = rotation;
            AppData.Settings.DeviceCalibrationTranslationVectors[_calibratingDeviceGuid] = translation;

            AppData.Settings.DeviceCalibrationOrigins[_calibratingDeviceGuid] = Vector3.Zero;
            AppData.Settings.DeviceMatricesCalibrated[_calibratingDeviceGuid] = true;
        }

        // Reset by re-reading the settings if aborted
        if (!_calibrationPending)
        {
            AppData.Settings.DeviceMatricesCalibrated[_calibratingDeviceGuid] = false;
            AppData.Settings.ReadSettings();

            AppSounds.PlayAppSound(AppSounds.AppSoundType.CalibrationAborted);
        }
        // Else save I guess
        else
        {
            AppData.Settings.SaveSettings();
            AppSounds.PlayAppSound(AppSounds.AppSoundType.CalibrationComplete);
        }

        // Notify that we're finished
        CalibrationCountdownLabel.Text = "~";
        CalibrationInstructionsLabel.Text =
            Interfacing.LocalizedJsonString(_calibrationPending
                ? "/GeneralPage/Calibration/Captions/Done"
                : "/GeneralPage/Calibration/Captions/Aborted");

        await Task.Delay(2200);

        // Exit the pane
        CalibrationDeviceSelectView.DisplayMode = SplitViewDisplayMode.Overlay;
        CalibrationDeviceSelectView.IsPaneOpen = false;

        CalibrationModeSelectView.DisplayMode = SplitViewDisplayMode.Overlay;
        CalibrationModeSelectView.IsPaneOpen = false;

        CalibrationRunningView.DisplayMode = SplitViewDisplayMode.Overlay;
        CalibrationRunningView.IsPaneOpen = false;

        AllowNavigation(true);
        Interfacing.CurrentAppState = "general";

        NoSkeletonTextNotice.Text = Interfacing.LocalizedJsonString("/GeneralPage/Captions/Preview/NoSkeletonText");

        _calibrationPending = false; // We're finished
        _autoCalibration_StillPending = false;

        AppData.Settings.SkeletonPreviewEnabled = _showSkeletonPrevious; // Change to whatever
        SetSkeletonVisibility(_showSkeletonPrevious); // Change to whatever
    }

    private void CalibrationPointsNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        // Don't even care if we're not set up yet
        if (!Shared.General.GeneralTabSetupFinished) return;

        // Attempt automatic fixes
        if (double.IsNaN(CalibrationPointsNumberBox.Value))
            CalibrationPointsNumberBox.Value = AppData.Settings.CalibrationPointsNumber;

        AppData.Settings.CalibrationPointsNumber = (uint)CalibrationPointsNumberBox.Value;
        AppData.Settings.SaveSettings(); // Save it

        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);
    }

    private void DiscardCalibrationButton_Click(object sender, RoutedEventArgs e)
    {
        // Just exit
        if (!_calibrationPending && !_autoCalibration_StillPending)
        {
            CalibrationDeviceSelectView.DisplayMode = SplitViewDisplayMode.Overlay;
            CalibrationDeviceSelectView.IsPaneOpen = false;

            CalibrationModeSelectView.DisplayMode = SplitViewDisplayMode.Overlay;
            CalibrationModeSelectView.IsPaneOpen = false;

            CalibrationRunningView.DisplayMode = SplitViewDisplayMode.Overlay;
            CalibrationRunningView.IsPaneOpen = false;

            AllowNavigation(true);

            // Play a sound
            AppSounds.PlayAppSound(AppSounds.AppSoundType.Hide);
            Interfacing.CurrentAppState = "general";

            NoSkeletonTextNotice.Text = Interfacing.LocalizedJsonString("/GeneralPage/Captions/Preview/NoSkeletonText");

            AppData.Settings.SkeletonPreviewEnabled = _showSkeletonPrevious; // Change to whatever
            SetSkeletonVisibility(_showSkeletonPrevious); // Change to whatever
        }
        // Begin abort
        else
        {
            _calibrationPending = false;
        }
    }

    private void CalibrationTeachingTip_ActionButtonClick(TeachingTip sender, object args)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);

        // Close the current tip
        CalibrationTeachingTip.IsOpen = false;

        // Show the previous one
        ToggleTrackersTeachingTip.TailVisibility = TeachingTipTailVisibility.Collapsed;
        ToggleTrackersTeachingTip.IsOpen = true;
    }

    private void CalibrationTeachingTip_CloseButtonClick(TeachingTip sender, object args)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);

        StatusTeachingTip.TailVisibility = TeachingTipTailVisibility.Collapsed;
        StatusTeachingTip.IsOpen = true;
    }

    private async void CalibrationButton_Click(object sender, RoutedEventArgs e)
    {
        // Capture playspace details one more time before the calibration
        var trackingOrigin = OpenVR.System.GetRawZeroPoseToStandingAbsoluteTrackingPose();
        Interfacing.VrPlayspaceTranslation = TypeUtils.ExtractVrPosition(ref trackingOrigin);
        Interfacing.VrPlayspaceOrientationQuaternion = TypeUtils.ExtractVrRotation(ref trackingOrigin);

        // If no overrides
        if (AppData.Settings.OverrideDevicesGuidMap.Count < 1)
        {
            // Get our current device
            var trackingDevice = TrackingDevices.GetTrackingDevice();

            // If the status isn't OK, cry about it
            if (trackingDevice.StatusError)
            {
                // Set the correct target
                NoCalibrationTeachingTip.Target = DeviceTitleContainer;

                // Hide the tail and open the tip
                NoCalibrationTeachingTip.TailVisibility = TeachingTipTailVisibility.Collapsed;
                NoCalibrationTeachingTip.PreferredPlacement = TeachingTipPlacementMode.Top;

                Shared.Main.InterfaceBlockerGrid.IsHitTestVisible = true;
                NoCalibrationTeachingTip.IsOpen = true;

                await Task.Delay(300);
                return; // Give up
            }

            // Show the calibration choose pane / calibration
            AutoCalibrationPane.Visibility = Visibility.Collapsed;
            ManualCalibrationPane.Visibility = Visibility.Collapsed;

            CalibrationDeviceSelectView.DisplayMode = SplitViewDisplayMode.Overlay;
            CalibrationDeviceSelectView.IsPaneOpen = false;

            CalibrationModeSelectView.DisplayMode = SplitViewDisplayMode.Inline;
            CalibrationModeSelectView.IsPaneOpen = true;

            CalibrationRunningView.DisplayMode = SplitViewDisplayMode.Overlay;
            CalibrationRunningView.IsPaneOpen = false;

            AllowNavigation(false);

            // Set the app state
            Interfacing.CurrentAppState = "calibration";

            // Set the calibration device
            _calibratingDeviceGuid = AppData.Settings.TrackingDeviceGuid;

            _showSkeletonPrevious = AppData.Settings.SkeletonPreviewEnabled; // Back up
            AppData.Settings.SkeletonPreviewEnabled = true; // Change to show
            SetSkeletonVisibility(true); // Change to show

            // Play a sound
            AppSounds.PlayAppSound(AppSounds.AppSoundType.Show);

            // If auto-calibration is not supported, proceed straight to manual
            // Test: supports if the device provides a head joint / otherwise not
            if (trackingDevice.TrackedJoints.Any(x => x.Role == TrackedJointType.JointHead)) return;

            // Still here? the test must have failed then
            Logger.Info($"Device ({trackingDevice.Name}, {trackingDevice.Guid}) " +
                        $"does not provide a {TrackedJointType.JointHead}" +
                        "and can't be calibrated with automatic calibration! Proceeding to manual now...");

            // Open the pane and start the calibration
            await ExecuteManualCalibration();
        }
        else
        {
            // Show the device selector pane
            AutoCalibrationPane.Visibility = Visibility.Collapsed;
            ManualCalibrationPane.Visibility = Visibility.Collapsed;


            // If all used devices are erred: cry about it
            if (TrackingDevices.TrackingDevicesList.Values
                .Where(plugin => plugin.IsBase || plugin.IsOverride)
                .All(device => device.StatusError))
            {
                // Set the correct target
                NoCalibrationTeachingTip.Target = DeviceTitleContainer;
                NoCalibrationTeachingTip.PreferredPlacement = TeachingTipPlacementMode.Top;

                // Hide the tail and open the tip
                NoCalibrationTeachingTip.TailVisibility = TeachingTipTailVisibility.Collapsed;

                Shared.Main.InterfaceBlockerGrid.IsHitTestVisible = true;
                NoCalibrationTeachingTip.IsOpen = true;

                await Task.Delay(300);
                return; // Give up
            }

            // Else proceed to calibration device pick
            CalibrationDeviceSelectView.DisplayMode = SplitViewDisplayMode.Inline;
            CalibrationDeviceSelectView.IsPaneOpen = true;

            CalibrationModeSelectView.DisplayMode = SplitViewDisplayMode.Overlay;
            CalibrationModeSelectView.IsPaneOpen = false;

            CalibrationRunningView.DisplayMode = SplitViewDisplayMode.Overlay;
            CalibrationRunningView.IsPaneOpen = false;

            AllowNavigation(false);
            Interfacing.CurrentAppState = "calibration";

            _showSkeletonPrevious = AppData.Settings.SkeletonPreviewEnabled; // Back up
            AppData.Settings.SkeletonPreviewEnabled = true; // Change to show
            SetSkeletonVisibility(true); // Change to show

            // Play a sound
            AppSounds.PlayAppSound(AppSounds.AppSoundType.Show);
        }
    }

    private void ToggleFreezeButton_Click(object sender, RoutedEventArgs e)
    {
        Interfacing.IsTrackingFrozen = !Interfacing.IsTrackingFrozen;

        ToggleFreezeButton.IsChecked = Interfacing.IsTrackingFrozen;
        ToggleFreezeButton.Content = Interfacing.LocalizedJsonString(
            Interfacing.IsTrackingFrozen
                ? "/GeneralPage/Buttons/Skeleton/Unfreeze"
                : "/GeneralPage/Buttons/Skeleton/Freeze");

        // Play a sound
        AppSounds.PlayAppSound(Interfacing.IsTrackingFrozen
            ? AppSounds.AppSoundType.ToggleOff
            : AppSounds.AppSoundType.ToggleOn);

        // Optionally show the binding teaching tip
        if (AppData.Settings.TeachingTipShownFreeze ||
            Interfacing.CurrentPageTag != "general") return;

        var header = Interfacing.LocalizedJsonString("/GeneralPage/Tips/TrackingFreeze/Header");

        // Change the tip depending on the currently connected controllers
        var controllerModel = new StringBuilder(1024);
        var error = ETrackedPropertyError.TrackedProp_Success;

        OpenVR.System.GetStringTrackedDeviceProperty(
            OpenVR.System.GetTrackedDeviceIndexForControllerRole(
                ETrackedControllerRole.LeftHand),
            ETrackedDeviceProperty.Prop_ModelNumber_String,
            controllerModel, 1024, ref error);

        if (controllerModel.ToString().Contains("knuckles", StringComparison.OrdinalIgnoreCase) ||
            controllerModel.ToString().Contains("index", StringComparison.OrdinalIgnoreCase))
            header = header.Replace("{0}",
                Interfacing.LocalizedJsonString("/GeneralPage/Tips/TrackingFreeze/Buttons/Index"));

        else if (controllerModel.ToString().Contains("vive", StringComparison.OrdinalIgnoreCase))
            header = header.Replace("{0}",
                Interfacing.LocalizedJsonString("/GeneralPage/Tips/TrackingFreeze/Buttons/VIVE"));

        else if (controllerModel.ToString().Contains("mr", StringComparison.OrdinalIgnoreCase))
            header = header.Replace("{0}",
                Interfacing.LocalizedJsonString("/GeneralPage/Tips/TrackingFreeze/Buttons/WMR"));

        else
            header = header.Replace("{0}",
                Interfacing.LocalizedJsonString("/GeneralPage/Tips/TrackingFreeze/Buttons/Oculus"));

        FreezeTrackingTeachingTip.Title = header;
        FreezeTrackingTeachingTip.Subtitle =
            Interfacing.LocalizedJsonString("/GeneralPage/Tips/TrackingFreeze/Footer");
        FreezeTrackingTeachingTip.TailVisibility = TeachingTipTailVisibility.Collapsed;

        Shared.Main.InterfaceBlockerGrid.IsHitTestVisible = true;
        FreezeTrackingTeachingTip.IsOpen = true;

        AppData.Settings.TeachingTipShownFreeze = true;
        AppData.Settings.SaveSettings();
    }

    private void OffsetsButton_Click(object sender, RoutedEventArgs e)
    {
        // Push saved offsets' by reading them from settings
        AppData.Settings.ReadSettings();

        // Reload offset values
        Logger.Info($"Force refreshing offsets MVVM for page: '{GetType().FullName}'...");
        AppData.Settings.TrackersVector.ForEach(x => x.OnPropertyChanged());

        // Open the pane now
        OffsetsView.DisplayMode = SplitViewDisplayMode.Inline;
        OffsetsView.IsPaneOpen = true;
        AllowNavigation(false);

        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Show);
        Interfacing.CurrentAppState = "offsets";
    }

    private void FreezeOnlyLowerToggle_Click(object sender, RoutedEventArgs e)
    {
        AppData.Settings.FreezeLowerBodyOnly = FreezeOnlyLowerToggle.IsChecked;
        AppData.Settings.SaveSettings();

        // Play a sound
        AppSounds.PlayAppSound(FreezeOnlyLowerToggle.IsChecked
            ? AppSounds.AppSoundType.ToggleOn
            : AppSounds.AppSoundType.ToggleOff);
    }

    private async void ToggleTrackersButton_Checked(object sender, RoutedEventArgs e)
    {
        // Don't check if setup's finished since we're gonna emulate a click rather than change the state only
        ToggleTrackersButton.Content = Interfacing.LocalizedJsonString(
            "/GeneralPage/Buttons/TrackersToggle/Disconnect");

        // Optionally spawn trackers
        if (!Interfacing.K2AppTrackersSpawned)
        {
            if (!await Interfacing.SpawnEnabledTrackers()) // Mark as spawned
            {
                Interfacing.ServerDriverFailure = true; // WAAAAAAA
                Interfacing.K2ServerDriverSetup(); // Refresh
                Interfacing.ShowToast(
                    Interfacing.LocalizedJsonString("/SharedStrings/Toasts/AutoSpawnFailed/Title"),
                    Interfacing.LocalizedJsonString("/SharedStrings/Toasts/AutoSpawnFailed"),
                    true); // High priority - it's probably a server failure

                Interfacing.ShowVrToast(
                    Interfacing.LocalizedJsonString("/SharedStrings/Toasts/AutoSpawnFailed/Title"),
                    Interfacing.LocalizedJsonString("/SharedStrings/Toasts/AutoSpawnFailed"));
            }

            // Update things
            Interfacing.UpdateServerStatus();
        }

        // Give up if failed
        if (Interfacing.ServerDriverFailure) return;

        // Mark trackers as active
        Interfacing.K2AppTrackersInitialized = true;

        // Request a check for already-added trackers
        Interfacing.AlreadyAddedTrackersScanRequested = true;

        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.TrackersConnected);

        // Show additional controls
        CalibrationButton.IsEnabled = true;
    }

    private void ToggleTrackersButton_Unchecked(object sender, RoutedEventArgs e)
    {
        // Don't check if setup's finished since we're gonna emulate a click rather than change the state only
        ToggleTrackersButton.Content = Interfacing.LocalizedJsonString(
            "/GeneralPage/Buttons/TrackersToggle/Reconnect");

        // Mark trackers as inactive
        Interfacing.K2AppTrackersInitialized = false;

        // Request a check for already-added trackers
        Interfacing.AlreadyAddedTrackersScanRequested = true;

        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.TrackersDisconnected);

        // Hide additional controls
        CalibrationButton.IsEnabled = false;
    }

    private void ToggleTrackersTeachingTip_ActionButtonClick(TeachingTip sender, object args)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);

        // Close the current tip
        ToggleTrackersTeachingTip.IsOpen = false;

        // Show the previous one
        Shared.TeachingTips.MainPage.InitializerTeachingTip.IsOpen = true;
    }

    private void ToggleTrackersTeachingTip_CloseButtonClick(TeachingTip sender, object args)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);

        CalibrationTeachingTip.TailVisibility = TeachingTipTailVisibility.Collapsed;
        CalibrationTeachingTip.IsOpen = true;
    }

    private void StatusTeachingTip_ActionButtonClick(TeachingTip sender, object args)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);

        // Close the current tip
        StatusTeachingTip.IsOpen = false;

        // Show the previous one
        CalibrationTeachingTip.TailVisibility = TeachingTipTailVisibility.Collapsed;
        CalibrationTeachingTip.IsOpen = true;
    }

    private async void StatusTeachingTip_CloseButtonClick(TeachingTip sender, object args)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);
        await Task.Delay(200);

        // Reset the next page layout (if ever changed)
        Shared.Settings.PageMainScrollViewer?.ScrollToVerticalOffset(0);

        // Navigate to the settings page
        Shared.Main.MainNavigationView.SelectedItem =
            Shared.Main.MainNavigationView.MenuItems[1];

        Shared.Main.NavigateToPage("settings",
            new EntranceNavigationTransitionInfo());

        await Task.Delay(500);

        // Show the next tip
        Shared.TeachingTips.SettingsPage.ManageTrackersTeachingTip.TailVisibility = TeachingTipTailVisibility.Collapsed;
        Shared.TeachingTips.SettingsPage.ManageTrackersTeachingTip.IsOpen = true;
    }

    private async void OpenDiscordButton_Click(object sender, RoutedEventArgs e)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);

        await Launcher.LaunchUriAsync(new Uri("https://discord.gg/YBQCRDG"));
    }

    private async void OpenDocsButton_Click(object sender, RoutedEventArgs e)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);

        var deviceErrorString = TrackingDeviceErrorLabel.Text;
        var deviceName = SelectedDeviceNameLabel.Text;

        switch (deviceName)
        {
            case "Xbox 360 Kinect":
                await Launcher.LaunchUriAsync(new Uri(deviceErrorString switch
                {
                    "E_NUI_NOTPOWERED" =>
                        $"https://docs.k2vr.tech/{AppData.Settings.AppLanguage}/360/troubleshooting/notpowered/",
                    "E_NUI_NOTREADY" =>
                        $"https://docs.k2vr.tech/{AppData.Settings.AppLanguage}/360/troubleshooting/notready/",
                    "E_NUI_NOTGENUINE" =>
                        $"https://docs.k2vr.tech/{AppData.Settings.AppLanguage}/360/troubleshooting/notgenuine/",
                    "E_NUI_INSUFFICIENTBANDWIDTH" =>
                        $"https://docs.k2vr.tech/{AppData.Settings.AppLanguage}/360/troubleshooting/insufficientbandwidth",
                    _ => $"https://docs.k2vr.tech/{AppData.Settings.AppLanguage}/app/help/"
                }));
                break;

            case "Xbox One Kinect":
                await Launcher.LaunchUriAsync(new Uri(deviceErrorString switch
                {
                    "E_NOTAVAILABLE" => $"https://docs.k2vr.tech/{AppData.Settings.AppLanguage}/one/troubleshooting/",
                    _ => $"https://docs.k2vr.tech/{AppData.Settings.AppLanguage}/app/help/"
                }));
                break;

            case "PSMove Service":
                await Launcher.LaunchUriAsync(new Uri(deviceErrorString switch
                {
                    "E_PSMS_NOT_RUNNING" =>
                        $"https://docs.k2vr.tech/{AppData.Settings.AppLanguage}/psmove/troubleshooting/",
                    "E_PSMS_NO_JOINTS" =>
                        $"https://docs.k2vr.tech/{AppData.Settings.AppLanguage}/psmove/troubleshooting/",
                    _ => $"https://docs.k2vr.tech/{AppData.Settings.AppLanguage}/app/help/"
                }));
                break;

            default:
                await Launcher.LaunchUriAsync(
                    new Uri($"https://docs.k2vr.tech/{AppData.Settings.AppLanguage}/app/help/"));
                break;
        }
    }

    private void AdditionalDeviceErrorsHyperlink_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        Shared.General.AdditionalDeviceErrorsHyperlinkTappedEvent.Start();
    }

    private async void ServerOpenDocsButton_Click(object sender, RoutedEventArgs e)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);

        await Launcher.LaunchUriAsync(new Uri(Interfacing.ServerDriverStatusCode switch
        {
            -10 => $"https://docs.k2vr.tech/{AppData.Settings.AppLanguage}/app/steamvr-driver-codes/#2",
            -1 => $"https://docs.k2vr.tech/{AppData.Settings.AppLanguage}/app/steamvr-driver-codes/#3",
            _ => $"https://docs.k2vr.tech/{AppData.Settings.AppLanguage}/app/steamvr-driver-codes/#5"
        }));
    }

    private void ReRegisterButton_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(Path.Combine(Interfacing.GetProgramLocation().DirectoryName,
                "K2CrashHandler", "K2CrashHandler.exe")))
        {
            Process.Start(Path.Combine(Interfacing.GetProgramLocation().DirectoryName,
                "K2CrashHandler", "K2CrashHandler.exe"));
        }
        else
        {
            Logger.Warn("Crash handler exe (./K2CrashHandler/K2CrashHandler.exe) not found!");
            SetErrorFlyoutText.Text = Interfacing.LocalizedJsonString("/SettingsPage/ReRegister/Error/NotFound");
            SetErrorFlyout.ShowAt(ReRegisterButton, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.RightEdgeAlignedBottom
            });
        }
    }

    private void SkeletonDrawingCanvas_Loaded(object sender, RoutedEventArgs e)
    {
        // TODO RENDER SKELETON
    }

    private void SkeletonToggleButton_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        // Don't even care if we're not set up yet
        if (!Shared.General.GeneralTabSetupFinished) return;

        AppData.Settings.SkeletonPreviewEnabled = SkeletonToggleButton.IsChecked;
        AppData.Settings.SaveSettings();

        SkeletonToggleButton.Content = Interfacing.LocalizedJsonString(
            AppData.Settings.SkeletonPreviewEnabled
                ? "/GeneralPage/Buttons/Skeleton/Hide"
                : "/GeneralPage/Buttons/Skeleton/Show");

        // Play a sound
        AppSounds.PlayAppSound(AppData.Settings.SkeletonPreviewEnabled
            ? AppSounds.AppSoundType.ToggleOn
            : AppSounds.AppSoundType.ToggleOff);

        ForceRenderCheckBox.IsEnabled =
            SkeletonToggleButton.IsChecked;
        ForceRenderText.Opacity =
            SkeletonToggleButton.IsChecked ? 1.0 : 0.5;
    }

    private void ToggleButtonFlyout_Opening(object sender, object e)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Show);
    }

    private void ToggleButtonFlyout_Closing(FlyoutBase sender, FlyoutBaseClosingEventArgs args)
    {
        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Hide);
    }

    private void ForceRenderCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        // Don't even care if we're not set up yet
        if (!Shared.General.GeneralTabSetupFinished) return;

        AppData.Settings.ForceSkeletonPreview = true;
        SetSkeletonForce(AppData.Settings.ForceSkeletonPreview);
        AppData.Settings.SaveSettings();

        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.ToggleOn);
    }

    private void ForceRenderCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        // Don't even care if we're not set up yet
        if (!Shared.General.GeneralTabSetupFinished) return;

        AppData.Settings.ForceSkeletonPreview = false;
        SetSkeletonForce(AppData.Settings.ForceSkeletonPreview);
        AppData.Settings.SaveSettings();

        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.ToggleOff);
    }

    private void DismissSetErrorButton_Click(object sender, RoutedEventArgs e)
    {
        SetErrorFlyout.Hide();
    }

    private void OffsetsValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        // Don't even care if we're not set up yet
        if (!sender.IsLoaded) return;

        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);

        // Fix and reload the offset value
        if (double.IsNaN(sender.Value))
            sender.Value = 0; // Reset to 0, auto-updates

        // Round the value (floats are kinda bad...)
        sender.Value = sender.GetValue(AttachedString
                .AttachedStringProperty).ToString()![0] switch
            {
                'P' => Math.Round(sender.Value, 2),
                'O' or _ => Math.Round(sender.Value, 0)
            };

        var tracker = AppData.Settings.TrackersVector.Where(
            x => x.Role == (sender.DataContext as AppTracker)!.Role).ToList()[0];

        // Manually overwrite the offset, use the custom property
        switch (sender.GetValue(AttachedString.AttachedStringProperty))
        {
            case "OZ":
                tracker.OrientationOffset.Z = (float)sender.Value;
                break;
            case "OY":
                tracker.OrientationOffset.Y = (float)sender.Value;
                break;
            case "OX":
                tracker.OrientationOffset.X = (float)sender.Value;
                break;
            case "PX":
                tracker.PositionOffset.X = (float)sender.Value;
                break;
            case "PY":
                tracker.PositionOffset.Y = (float)sender.Value;
                break;
            case "PZ":
                tracker.PositionOffset.Z = (float)sender.Value;
                break;
        }
    }

    private void SkPoint(Microsoft.UI.Xaml.Shapes.Ellipse ellipse,
        TrackedJoint joint, (bool Position, bool Orientation) overrideStatus)
    {
        const double matWidthDefault = 700, matHeightDefault = 600;
        double matWidth = SkeletonDrawingCanvas.ActualWidth,
            matHeight = SkeletonDrawingCanvas.ActualHeight;

        // Eventually fix sizes
        if (matWidth < 1) matWidth = matWidthDefault;
        if (matHeight < 1) matHeight = matHeightDefault;

        // Where to scale by 1.0 in perspective
        const double normalDistance = 3;
        const double normalEllipseSize = 12,
            normalEllipseStrokeSize = 2;

        // Compose perspective constants, make it 70%
        var multiply = .7 * (normalDistance /
                             (joint.JointPosition.Z > 0.0
                                 ? joint.JointPosition.Z
                                 : 3.0));

        var a = new AcrylicBrush
        {
            TintColor = new Windows.UI.ViewManagement.UISettings()
                .GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent)
        };

        ellipse.StrokeThickness = normalEllipseStrokeSize;
        ellipse.Width = normalEllipseSize;
        ellipse.Height = normalEllipseSize;

        if (joint.TrackingState != TrackedJointState.StateTracked)
        {
            ellipse.Stroke = a;
            ellipse.Fill = a;
        }
        else
        {
            ellipse.Stroke = new SolidColorBrush(Microsoft.UI.Colors.White);
            ellipse.Fill = new SolidColorBrush(Microsoft.UI.Colors.White);
        }

        // Change the stroke based on overrides
        ellipse.Stroke = overrideStatus.Position switch
        {
            // Both
            true when overrideStatus.Orientation => new SolidColorBrush(Microsoft.UI.Colors.BlueViolet),
            // Rotation
            true => new SolidColorBrush(Microsoft.UI.Colors.DarkOliveGreen),
            // Position
            false => new SolidColorBrush(Microsoft.UI.Colors.IndianRed)
        };

        // Select the smaller scale to preserve somewhat uniform skeleton scaling
        double sScaleW = matWidth / matWidthDefault,
            sScaleH = matHeight / matHeightDefault;

        // Move the ellipse to the appropriate point
        ellipse.Margin = new Thickness(
            // Left
            joint.JointPosition.X * 300.0 *
            Math.Min(sScaleW, sScaleH) * multiply +
            matWidth / 2.0 - (normalEllipseSize + normalEllipseStrokeSize) / 2.0,

            // Top
            joint.JointPosition.Y * -300.0 *
            Math.Min(sScaleW, sScaleH) * multiply +
            matHeight / 3.0 - (normalEllipseSize + normalEllipseStrokeSize) / 2.0,

            // Not used
            0, 0
        );

        ellipse.Visibility = Visibility.Visible;
    }

    private void ExecuteAutomaticCalibration()
    {
        // Setup the calibration image : reset and stop
        CalibrationPreviewMediaElement.MediaPlayer.Position = TimeSpan.Zero;
        CalibrationPreviewMediaElement.MediaPlayer.Pause();

        AutoCalibrationPane.Visibility = Visibility.Visible;
        ManualCalibrationPane.Visibility = Visibility.Collapsed;

        CalibrationRunningView.DisplayMode = SplitViewDisplayMode.Inline;
        CalibrationRunningView.IsPaneOpen = true;

        // Play a sound
        AppSounds.PlayAppSound(AppSounds.AppSoundType.Show);
        Interfacing.CurrentAppState = "calibration_auto";

        CalibrationPointsNumberBox.IsEnabled = true;
        CalibrationPointsNumberBox.Value = AppData.Settings.CalibrationPointsNumber;

        CalibrationInstructionsLabel.Text = Interfacing.LocalizedJsonString(
            "/GeneralPage/Calibration/Captions/Start");
        NoSkeletonTextNotice.Text = Interfacing.LocalizedJsonString(
            "/GeneralPage/Captions/Preview/NoSkeletonTextCalibrating");
        DiscardAutoCalibrationButton.Content = Interfacing.LocalizedJsonString(
            "/GeneralPage/Buttons/Cancel");

        CalibrationCountdownLabel.Text = "~";
    }

    private async Task ExecuteManualCalibration()
    {
        // Swap trigger/grip if we're on index or vive
        var controllerModel = new StringBuilder(1024);
        var error = ETrackedPropertyError.TrackedProp_Success;

        OpenVR.System.GetStringTrackedDeviceProperty(
            OpenVR.System.GetTrackedDeviceIndexForControllerRole(
                ETrackedControllerRole.LeftHand),
            ETrackedDeviceProperty.Prop_ModelNumber_String,
            controllerModel, 1024, ref error);

        // Set up as default (just in case)
        LabelFineTuneVive.Visibility = Visibility.Collapsed;
        LabelFineTuneNormal.Visibility = Visibility.Visible;

        // Swap (optionally)
        if (controllerModel.ToString().Contains("knuckles", StringComparison.OrdinalIgnoreCase) ||
            controllerModel.ToString().Contains("index", StringComparison.OrdinalIgnoreCase) ||
            controllerModel.ToString().Contains("vive", StringComparison.OrdinalIgnoreCase))
        {
            LabelFineTuneVive.Visibility = Visibility.Visible;
            LabelFineTuneNormal.Visibility = Visibility.Collapsed;
        }

        // Set up panels
        AutoCalibrationPane.Visibility = Visibility.Collapsed;
        ManualCalibrationPane.Visibility = Visibility.Visible;

        CalibrationRunningView.DisplayMode = SplitViewDisplayMode.Inline;
        CalibrationRunningView.IsPaneOpen = true;

        Interfacing.CurrentAppState = "calibration_manual";

        // Set the [calibration pending] bool
        _calibrationPending = true;

        // Play a nice sound - starting
        AppSounds.PlayAppSound(AppSounds.AppSoundType.CalibrationStart);

        // Which calibration method did we use
        AppData.Settings.DeviceAutoCalibration[_calibratingDeviceGuid] = false;

        // Mark as calibrated for the preview
        AppData.Settings.DeviceMatricesCalibrated[_calibratingDeviceGuid] = true;

        // Set up (a lot of) helper variables
        var calibrationFirstTime = true;
        float tempYaw = 0, tempPitch = 0;

        // pitch, yaw, roll
        var anglesVector3 = Vector3.Zero;
        var rotationQuaternion = Quaternion.CreateFromYawPitchRoll(
            anglesVector3.Y, anglesVector3.X, anglesVector3.Z);

        // Copy the empty matrices to settings
        AppData.Settings.DeviceCalibrationRotationMatrices[_calibratingDeviceGuid] = rotationQuaternion;
        AppData.Settings.DeviceCalibrationTranslationVectors[_calibratingDeviceGuid] = Vector3.Zero;

        // Loop over until finished
        while (!Interfacing.CalibrationConfirm)
        {
            // Wait for a mode switch
            while (!Interfacing.CalibrationModeSwap && !Interfacing.CalibrationConfirm)
            {
                var multiplexer = Interfacing.CalibrationFineTune ? .0015f : .015f;

                // Compute the translation delta for the current calibration frame
                var currentCalibrationTranslationNew = new Vector3(
                    Interfacing.CalibrationJoystickPositions.LeftPosition.X, // Left X
                    Interfacing.CalibrationJoystickPositions.RightPosition.Y, // Right Y
                    -Interfacing.CalibrationJoystickPositions.LeftPosition.Y // Left Y (inv)
                );

                // Apply the multiplexer
                currentCalibrationTranslationNew *= multiplexer;

                // Un-rotate the translation (sometimes broken due to SteamVR playspace)
                currentCalibrationTranslationNew = Vector3.Transform(currentCalibrationTranslationNew,
                    Quaternion.Inverse(Interfacing.VrPlayspaceOrientationQuaternion));

                // Apply to the global base
                AppData.Settings.DeviceCalibrationTranslationVectors[_calibratingDeviceGuid] +=
                    currentCalibrationTranslationNew;

                await Task.Delay(5);

                // Exit if aborted
                if (!_calibrationPending) break;
            }

            // Play mode swap sound
            if (_calibrationPending && !Interfacing.CalibrationConfirm)
                AppSounds.PlayAppSound(AppSounds.AppSoundType.CalibrationPointCaptured);

            // Set up the calibration origin
            if (calibrationFirstTime)
                AppData.Settings.DeviceCalibrationOrigins[_calibratingDeviceGuid] =
                    Interfacing.DeviceRelativeTransformOrigin.ValueOr(_calibratingDeviceGuid);

            // Cache the calibration first_time
            calibrationFirstTime = false;
            await Task.Delay(300);

            // Wait for a mode switch
            while (!Interfacing.CalibrationModeSwap && !Interfacing.CalibrationConfirm)
            {
                var multiplexer = Interfacing.CalibrationFineTune ? .1f : 1f;

                tempYaw += Interfacing.CalibrationJoystickPositions.LeftPosition.X *
                    float.Pi / 280f * multiplexer; // Left X
                tempPitch += Interfacing.CalibrationJoystickPositions.RightPosition.Y *
                    float.Pi / 280f * multiplexer; // Right Y

                anglesVector3 = new Vector3(tempPitch, tempYaw, 0f);
                rotationQuaternion = Quaternion.CreateFromYawPitchRoll(
                    anglesVector3.Y, anglesVector3.X, anglesVector3.Z);

                // Copy the empty matrices to settings
                AppData.Settings.DeviceCalibrationRotationMatrices[_calibratingDeviceGuid] = rotationQuaternion;

                await Task.Delay(5);

                // Exit if aborted
                if (!_calibrationPending) break;
            }

            await Task.Delay(300);

            // Play mode swap sound
            if (_calibrationPending && !Interfacing.CalibrationConfirm)
                AppSounds.PlayAppSound(AppSounds.AppSoundType.CalibrationPointCaptured);

            // Exit if aborted
            if (!_calibrationPending) break;
        }

        // Reset by re-reading the settings if aborted
        if (!_calibrationPending)
        {
            AppData.Settings.DeviceMatricesCalibrated[_calibratingDeviceGuid] = false;
            AppData.Settings.ReadSettings();

            AppSounds.PlayAppSound(AppSounds.AppSoundType.CalibrationAborted);
        }
        // Else save I guess
        else
        {
            AppData.Settings.SaveSettings();
            AppSounds.PlayAppSound(AppSounds.AppSoundType.CalibrationComplete);
            await Task.Delay(1000);
        }

        // Exit the pane and reset
        CalibrationDeviceSelectView.DisplayMode = SplitViewDisplayMode.Overlay;
        CalibrationDeviceSelectView.IsPaneOpen = false;

        CalibrationModeSelectView.DisplayMode = SplitViewDisplayMode.Overlay;
        CalibrationModeSelectView.IsPaneOpen = false;

        CalibrationRunningView.DisplayMode = SplitViewDisplayMode.Overlay;
        CalibrationRunningView.IsPaneOpen = false;

        AllowNavigation(true);
        Interfacing.CurrentAppState = "general";
        Interfacing.CalibrationConfirm = false;

        _calibrationPending = false; // We're finished
        AppData.Settings.SkeletonPreviewEnabled = _showSkeletonPrevious;
        SetSkeletonVisibility(_showSkeletonPrevious); // Change to whatever
    }

    private void Button_ClickSound(object sender, RoutedEventArgs e)
    {
        // Don't even care if we're not set up yet
        if (!Shared.General.GeneralTabSetupFinished) return;

        AppSounds.PlayAppSound(AppSounds.AppSoundType.Invoke);
    }

    private void SetSkeletonVisibility(bool visibility)
    {
        // Don't even care if we're not set up yet
        if (!Shared.General.GeneralTabSetupFinished) return;

        Shared.General.ForceRenderCheckBox.IsEnabled = visibility;
        Shared.General.SkeletonToggleButton.IsChecked = visibility;
        Shared.General.ForceRenderText.Opacity = visibility ? 1.0 : 0.5;
        Shared.General.SkeletonToggleButton.Content = Interfacing.LocalizedJsonString(
            visibility ? "/GeneralPage/Buttons/Skeleton/Hide" : "/GeneralPage/Buttons/Skeleton/Show");
    }

    private void SetSkeletonForce(bool visibility)
    {
        // Don't even care if we're not set up yet
        if (!Shared.General.GeneralTabSetupFinished) return;
        Shared.General.ForceRenderCheckBox.IsChecked = visibility;
    }

    private string CalibrationPointsFormat(string format, int p1, uint p2)
    {
        return format.Replace("{1}", p1.ToString())
            .Replace("{2}", p2.ToString());
    }

    private void AllowNavigation(bool allow)
    {
        Shared.Main.NavigationBlockerGrid.IsHitTestVisible = !allow;
    }

    private int _previousOffsetPageIndex = 0;
    private bool _offsetsPageNavigated = false;

    private void OffsetsControlPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Don't even care if we're not set up yet
        if (!(sender as Pivot).IsLoaded) return;

        if (_offsetsPageNavigated)
        {
            // The last item
            if ((sender as Pivot).SelectedIndex == (sender as Pivot).Items.Count - 1)
                AppSounds.PlayAppSound(_previousOffsetPageIndex == 0
                    ? AppSounds.AppSoundType.MovePrevious
                    : AppSounds.AppSoundType.MoveNext);

            // The first item
            else if ((sender as Pivot).SelectedIndex == 0)
                AppSounds.PlayAppSound(_previousOffsetPageIndex == (sender as Pivot).Items.Count - 1
                    ? AppSounds.AppSoundType.MoveNext
                    : AppSounds.AppSoundType.MovePrevious);

            // Default
            else
                AppSounds.PlayAppSound((sender as Pivot).SelectedIndex > _previousOffsetPageIndex
                    ? AppSounds.AppSoundType.MoveNext
                    : AppSounds.AppSoundType.MovePrevious);
        }

        // Cache
        _previousOffsetPageIndex = (sender as Pivot).SelectedIndex;
        _offsetsPageNavigated = true;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public void OnPropertyChanged(string propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}

public class AttachedString : DependencyObject
{
    public static readonly DependencyProperty AttachedStringProperty =
        DependencyProperty.RegisterAttached(
            "AttachedString",
            typeof(string),
            typeof(AttachedString),
            new PropertyMetadata(false)
        );

    public static void SetAttachedString(UIElement element, string value)
    {
        element.SetValue(AttachedStringProperty, value);
    }

    public static string GetAttachedString(UIElement element)
    {
        return (string)element.GetValue(AttachedStringProperty);
    }
}