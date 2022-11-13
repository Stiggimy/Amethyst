﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Windows.Globalization;
using Windows.System.UserProfile;
using Amethyst.Utils;
using System.ComponentModel;
using Amethyst.Driver.API;
using Amethyst.Plugins.Contract;

namespace Amethyst.Classes;

public class AppSettings : INotifyPropertyChanged
{
    // Current language & theme
    public string AppLanguage { get; set; } = "en";

    // 0:system, 1:dark, 2:light
    public uint AppTheme { get; set; }

    // Current joints
    public List<AppTracker> TrackersVector { get; set; } = new();
    public bool UseTrackerPairs { get; set; } = true; // Pair feet, elbows and knees
    public bool CheckForOverlappingTrackers { get; set; } = true; // Check for overlapping roles

    // Current tracking device: 0 is the default base device
    // First: Device's GUID / saved, Second: Index ID / generated
    public string TrackingDeviceGuid { get; set; } = ""; // -> Always set
    public SortedSet<string> OverrideDevicesGuidMap { get; set; } = new();

    // Skeleton flip when facing away: One-For-All and on is the default
    public bool IsFlipEnabled { get; set; } = true;

    // Skeleton flip based on non-flip override devices' waist tracker
    public bool IsExternalFlipEnabled { get; set; } = false;

    // Automatically spawn enabled trackers on startup and off is the default
    public bool AutoSpawnEnabledJoints { get; set; } = false;

    // Enable application sounds and on is the default
    public bool EnableAppSounds { get; set; } = true;

    // App sounds' volume and *nice* is the default
    public uint AppSoundsVolume { get; set; } = 69; // Always 0<x<100

    // Calibration - if we're calibrated
    public SortedDictionary<string, bool> DeviceMatricesCalibrated { get; set; } = new();

    // Calibration helpers - calibration method: auto? : GUID/Data
    public SortedDictionary<string, bool> DeviceAutoCalibration { get; set; } = new();

    // Calibration matrices : GUID/Data
    public SortedDictionary<string, Quaternion> DeviceCalibrationRotationMatrices { get; set; } = new();
    public SortedDictionary<string, Vector3> DeviceCalibrationTranslationVectors { get; set; } = new();
    public SortedDictionary<string, Vector3> DeviceCalibrationOrigins { get; set; } = new();

    // Calibration helpers - points number
    public uint CalibrationPointsNumber { get; set; } = 3; // Always 3<=x<=5

    // Save the skeleton preview state
    public bool SkeletonPreviewEnabled { get; set; } = true;

    // If we wanna dismiss all warnings during the preview
    public bool ForceSkeletonPreview { get; set; } = false;

    // External flip device's calibration rotation
    public Quaternion ExternalFlipCalibrationMatrix = new();

    // If we wanna freeze only lower body trackers or all
    public bool FreezeLowerBodyOnly { get; set; } = false;

    // If the freeze bindings teaching tip has been shown
    public bool TeachingTipShownFreeze { get; set; } = false;

    // If the flip bindings teaching tip has been shown
    public bool TeachingTipShownFlip { get; set; } = false;

    // Already shown toasts vector
    public List<string> ShownToastsGuidVector = new();

    // Disabled (by the user) devices set
    public SortedSet<string> DisabledDevicesGuidSet = new();

    // If the first-launch guide's been shown
    public bool FirstTimeTourShown { get; set; } = false;

    // If the shutdown warning has been shown
    public bool FirstShutdownTipShown { get; set; } = false;

    // Save settings
    public void SaveSettings()
    {
        // TODO IMPL
    }

    // Re/Load settings
    public void ReadSettings()
    {
        // TODO IMPL

        // Check if the trackers vector is broken
        var vectorBroken = TrackersVector.Count < 7;

        // Optionally fix the trackers vector
        while (TrackersVector.Count < 7)
            TrackersVector.Add(new AppTracker());

        // Force the first 7 trackers to be the default ones : roles
        TrackersVector[0].Role = TrackerType.TrackerWaist;
        TrackersVector[1].Role = TrackerType.TrackerLeftFoot;
        TrackersVector[2].Role = TrackerType.TrackerRightFoot;
        TrackersVector[3].Role = TrackerType.TrackerLeftElbow;
        TrackersVector[4].Role = TrackerType.TrackerRightElbow;
        TrackersVector[5].Role = TrackerType.TrackerLeftKnee;
        TrackersVector[6].Role = TrackerType.TrackerRightKnee;

        foreach (var tracker in TrackersVector)
        {
            // Force the first 7 trackers to be the default ones : serials
            tracker.Serial = TypeUtils.TrackerTypeRoleSerialDictionary[tracker.Role];

            // Force disable software orientation if used by a non-foot
            if (tracker.Role != TrackerType.TrackerLeftFoot &&
                tracker.Role != TrackerType.TrackerRightFoot &&
                tracker.OrientationTrackingOption is JointRotationTrackingOption.SoftwareCalculatedRotation
                    or JointRotationTrackingOption.SoftwareCalculatedRotationV2)
                tracker.OrientationTrackingOption = JointRotationTrackingOption.DeviceInferredRotation;
        }

        // If the vector was broken, override waist & feet statuses
        if (vectorBroken)
        {
            TrackersVector[0].IsActive = true;
            TrackersVector[1].IsActive = true;
            TrackersVector[2].IsActive = true;
        }

        // Scan for duplicate trackers
        foreach (var tracker in TrackersVector.GroupBy(x => x.Role)
                     .Where(g => g.Count() > 1)
                     .Select(y => y.First()).ToList())
        {
            Logger.Warn("A duplicate tracker was found in the trackers vector! Removing it...");
            TrackersVector.Remove(tracker); // Remove the duplicate tracker
        }

        // Check if any trackers are enabled
        // -> No trackers are enabled, force-enable the waist tracker
        if (!TrackersVector.Any(x => x.IsActive))
        {
            Logger.Warn("All trackers were disabled, force-enabling the waist tracker!");
            TrackersVector[0].IsActive = true; // Enable the waist tracker
        }

        // Fix statuses (optional)
        if (UseTrackerPairs)
        {
            TrackersVector[2].IsActive = TrackersVector[1].IsActive;
            TrackersVector[4].IsActive = TrackersVector[3].IsActive;
            TrackersVector[6].IsActive = TrackersVector[5].IsActive;

            TrackersVector[2].OrientationTrackingOption =
                TrackersVector[1].OrientationTrackingOption;
            TrackersVector[4].OrientationTrackingOption =
                TrackersVector[3].OrientationTrackingOption;
            TrackersVector[6].OrientationTrackingOption =
                TrackersVector[5].OrientationTrackingOption;

            TrackersVector[2].PositionTrackingFilterOption =
                TrackersVector[1].PositionTrackingFilterOption;
            TrackersVector[4].PositionTrackingFilterOption =
                TrackersVector[3].PositionTrackingFilterOption;
            TrackersVector[6].PositionTrackingFilterOption =
                TrackersVector[5].PositionTrackingFilterOption;

            TrackersVector[2].OrientationTrackingFilterOption =
                TrackersVector[1].OrientationTrackingFilterOption;
            TrackersVector[4].OrientationTrackingFilterOption =
                TrackersVector[3].OrientationTrackingFilterOption;
            TrackersVector[6].OrientationTrackingFilterOption =
                TrackersVector[5].OrientationTrackingFilterOption;
        }

        // Optionally fix volume if too big somehow
        AppSoundsVolume = Math.Clamp(AppSoundsVolume, 0, 100);

        // Optionally fix calibration points
        CalibrationPointsNumber = Math.Clamp(CalibrationPointsNumber, 3, 5);

        // Optionally fix the app theme value
        AppTheme = Math.Clamp(AppTheme, 0, 2);

        // Optionally fix the selected language / select a new one
        var resourcePath = Path.Join(
            Interfacing.GetProgramLocation().DirectoryName,
            "Assets", "Strings", AppLanguage + ".json");

        // If there's no specified language, fallback to {system}
        if (string.IsNullOrEmpty(AppLanguage))
        {
            AppLanguage = new Language(
                GlobalizationPreferences.Languages[0]).LanguageTag[..2];

            Logger.Warn($"No language specified! Trying with the system one: \"{AppLanguage}\"!");
            resourcePath = Path.Join(
                Interfacing.GetProgramLocation().DirectoryName, "Assets", "Strings", AppLanguage + ".json");
        }

        // If the specified language doesn't exist somehow, fallback to 'en'
        if (!File.Exists(resourcePath))
        {
            Logger.Warn($"Could not load language resources at \"{resourcePath}\", falling back to 'en' (en.json)!");

            AppLanguage = "en"; // Change to english
            resourcePath = Path.Join(
                Interfacing.GetProgramLocation().DirectoryName,
                "Assets", "Strings", AppLanguage + ".json");
        }

        // If failed again, just give up
        if (!File.Exists(resourcePath))
            Logger.Warn($"Could not load language resources at \"{resourcePath}\", the app interface will be broken!");
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public void OnPropertyChanged(string propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}