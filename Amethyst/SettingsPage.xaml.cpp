﻿#include "pch.h"
#include "SettingsPage.xaml.h"

#if __has_include("SettingsPage.g.cpp")
#include "SettingsPage.g.cpp"
#endif

using namespace winrt;
using namespace winrt::Microsoft::UI::Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

void settings_safe_clear(const std::shared_ptr<Controls::StackPanel>& panel)
{
	[&]
	{
		__try
		{
			[&]
			{
				panel.get()->Children().Clear();
			}();
		}
		__except (EXCEPTION_EXECUTE_HANDLER)
		{
			[&]
			{
				LOG(WARNING) << "Couldn't clear a StackPanel. You better call an exorcist.";
			}();
		}
	}();
}

namespace winrt::KinectToVR::implementation
{
	SettingsPage::SettingsPage()
	{
		InitializeComponent();

		// Cache needed UI elements
		using namespace k2app::shared::settings;

		LOG(INFO) << "Appending settings' page elements to the shared context";

		restartButton = std::make_shared<Controls::Button>(RestartButton());

		externalFlipCheckBox = std::make_shared<Controls::CheckBox>(ExternalFlipCheckBox());
		autoSpawnCheckbox = std::make_shared<Controls::CheckBox>(AutoSpawnCheckBox());
		enableSoundsCheckbox = std::make_shared<Controls::CheckBox>(SoundsEnabledCheckBox());
		autoStartCheckBox = std::make_shared<Controls::CheckBox>(AutoStartCheckBox());

		flipDropDownGrid = std::make_shared<Controls::Grid>(FlipDropDownGrid());

		jointExpanderHostStackPanel = std::make_shared<Controls::StackPanel>(JointExpanderHostStackPanel());

		flipToggle = std::make_shared<Controls::ToggleSwitch>(FlipToggle());

		externalFlipCheckBoxLabel = std::make_shared<Controls::TextBlock>(ExternalFlipCheckBoxLabel());
		setErrorFlyoutText = std::make_shared<Controls::TextBlock>(SetErrorFlyoutText());

		flipDropDown = std::make_shared<Controls::Expander>(FlipDropDown());
		soundsVolumeSlider = std::make_shared<Controls::Slider>(SoundsVolumeSlider());
		externalFlipStackPanel = std::make_shared<Controls::StackPanel>(ExternalFlipStackPanel());

		LOG(INFO) << "Rebuilding joint expanders... this may take a while...";

		jointExpanderVector.clear();
		jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
			new Controls::JointExpander({ &k2app::K2Settings.K2TrackersVector[0] }))));

		if (k2app::K2Settings.useTrackerPairs)
		{
			LOG(INFO) << "UseTrackerPairs is set to true: Appending the default expanders as pairs...";

			jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
				new Controls::JointExpander(
					{ &k2app::K2Settings.K2TrackersVector[1], &k2app::K2Settings.K2TrackersVector[2] },
					k2app::interfacing::LocalizedResourceWString(
						L"SharedStrings", L"Joints/Pairs/Feet")))));

			jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
				new Controls::JointExpander(
					{ &k2app::K2Settings.K2TrackersVector[3], &k2app::K2Settings.K2TrackersVector[4] },
					k2app::interfacing::LocalizedResourceWString(
						L"SharedStrings", L"Joints/Pairs/Elbows")))));

			jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
				new Controls::JointExpander(
					{ &k2app::K2Settings.K2TrackersVector[5], &k2app::K2Settings.K2TrackersVector[6] },
					k2app::interfacing::LocalizedResourceWString(
						L"SharedStrings", L"Joints/Pairs/Knees")))));
		}

		LOG(INFO) << "Appending additional expanders (if they exist)...";

		// k2app::K2Settings.useTrackerPairs ? 7 : 1 means that if pairs have
		// already been appended, we'll start after them, and if not -
		// - we'll append them as individual tracker/joint expanders

		for (uint32_t index = (k2app::K2Settings.useTrackerPairs ? 7 : 1);
			index < k2app::K2Settings.K2TrackersVector.size(); index++)
			jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
				new Controls::JointExpander({ &k2app::K2Settings.K2TrackersVector[index] }))));

		LOG(INFO) << "Clearing the appended expanders (UI Node)";
		settings_safe_clear(jointExpanderHostStackPanel);

		LOG(INFO) << "Appending the new expanders to the UI Node";

		int _expander_number = (k2app::K2Settings.useTrackerPairs ? 1 : 2); // For separators
		for (auto expander : jointExpanderVector)
		{
			// Append the expander
			jointExpanderHostStackPanel->Children().Append(*expander->Container());

			// Append the separator (optionally)
			if (_expander_number >= 2 && jointExpanderVector.back() != expander)
			{
				auto separator = Shapes::Rectangle();
				separator.HorizontalAlignment(HorizontalAlignment::Stretch);
				separator.Height(1);
				separator.Margin({ 0, 10, 0, 0 });
				separator.Stroke(Media::SolidColorBrush(
					Windows::UI::ColorHelper::FromArgb(255, 59, 59, 59)));
				separator.Fill(Media::SolidColorBrush(
					Windows::UI::ColorHelper::FromArgb(255, 59, 59, 59)));

				jointExpanderHostStackPanel->Children().Append(separator);
				_expander_number = 1;
			}
			else _expander_number++;
		}
	}
}


void KinectToVR::implementation::SettingsPage::ExternalFlipCheckBox_Checked(
	const Windows::Foundation::IInspectable& sender, const RoutedEventArgs& e)
{
	// Don't react to pre-init signals
	if (!k2app::shared::settings::settings_localInitFinished)return;

	// Cache flip to settings and save
	k2app::K2Settings.isExternalFlipEnabled = true; // Checked
	k2app::K2Settings.saveSettings();
}


void KinectToVR::implementation::SettingsPage::ExternalFlipCheckBox_Unchecked(
	const Windows::Foundation::IInspectable& sender, const RoutedEventArgs& e)
{
	// Don't react to pre-init signals
	if (!k2app::shared::settings::settings_localInitFinished)return;

	// Cache flip to settings and save
	k2app::K2Settings.isExternalFlipEnabled = false; // Unchecked
	k2app::K2Settings.saveSettings();
}


void KinectToVR::implementation::SettingsPage::RestartButton_Click(
	const Windows::Foundation::IInspectable& sender, const RoutedEventArgs& e)
{
	ktvr::request_vr_restart<false>("SteamVR needs to be restarted to enable/disable trackers properly.");
}


void KinectToVR::implementation::SettingsPage::ResetButton_Click(
	const Windows::Foundation::IInspectable& sender, const RoutedEventArgs& e)
{
	// Mark trackers as inactive
	k2app::interfacing::K2AppTrackersInitialized = false;
	if (k2app::shared::general::toggleTrackersButton.get() != nullptr)
		k2app::shared::general::toggleTrackersButton->IsChecked(false);

	// Read settings after reset
	k2app::K2Settings = k2app::K2AppSettings(); // Reset settings
	k2app::K2Settings.saveSettings(); // Save empty settings

	/* Restart app */

	// Literals
	using namespace std::string_literals;

	// Get current caller path
	const auto fileName = new CHAR[MAX_PATH + 1];
	const DWORD charsWritten = GetModuleFileNameA(nullptr, fileName, MAX_PATH + 1);

	// If we've found who asked
	if (charsWritten != 0)
	{
		// Compose the restart command: sleep 3 seconds and start the same process
		const std::string _cmd =
			"powershell Start-Process powershell -ArgumentList 'Start-Sleep -Seconds 3; " +
			"Start-Process -WorkingDirectory (Split-Path -Path (Resolve-Path \""s +
			fileName +
			"\")) -filepath \"" +
			fileName +
			"\"' -WindowStyle hidden";

		// Log the caller
		LOG(INFO) << "The current caller process is: "s + fileName;
		LOG(INFO) << "Restart command used: "s + _cmd;


		// Restart the app
		if (WinExec(_cmd.c_str(), SW_HIDE) != NO_ERROR)
		{
			LOG(ERROR) << "App will not be restarted due to new process creation error.";
			k2app::interfacing::ShowToast(
				k2app::interfacing::LocalizedResourceWString(L"SharedStrings", L"Toasts/RestartFailed/Title"),
				k2app::interfacing::LocalizedResourceWString(L"SharedStrings", L"Toasts/RestartFailed/Content"));
			return;
		}

		// Mark exiting as true
		k2app::interfacing::isExitingNow = true;

		// Exit the app
		LOG(INFO) << "Configuration has been reset, exiting...";
		Application::Current().Exit();
	}
	else
	{
		LOG(ERROR) << "App will not be restarted due to caller process identification error.";
		k2app::interfacing::ShowToast(
			k2app::interfacing::LocalizedResourceWString(L"SharedStrings", L"Toasts/RestartFailed/Title"),
			k2app::interfacing::LocalizedResourceWString(L"SharedStrings", L"Toasts/RestartFailed/Content"));
	}
}


void KinectToVR::implementation::SettingsPage::SettingsPage_Loaded(
	const Windows::Foundation::IInspectable& sender, const RoutedEventArgs& e)
{
	using namespace k2app::shared::settings;

	// Notify of the setup end
	k2app::shared::settings::settings_localInitFinished = false;
	CheckOverlapsCheckBox().IsChecked(k2app::K2Settings.checkForOverlappingTrackers);

	// Optionally show the foreign language grid
	if (!status_ok_map.contains(GetUserLocale()))
		ForeignLangGrid().Visibility(Visibility::Visible);

	// Select saved flip, position and rotation options
	flipToggle.get()->IsOn(k2app::K2Settings.isFlipEnabled);
	externalFlipCheckBox.get()->IsChecked(k2app::K2Settings.isExternalFlipEnabled);

	if (const auto& trackingDevice = TrackingDevices::getCurrentDevice();
		trackingDevice.index() == 0)
	{
		// Kinect Basis
		const bool _sup = std::get<ktvr::K2TrackingDeviceBase_KinectBasis*>(trackingDevice)->
			isAppOrientationSupported();

		for (auto expander : jointExpanderVector)
			expander->EnableSoftwareOrientation(_sup);

		flipToggle.get()->IsEnabled(
			std::get<ktvr::K2TrackingDeviceBase_KinectBasis*>(trackingDevice)->isFlipSupported());
		flipDropDown.get()->IsEnabled(
			std::get<ktvr::K2TrackingDeviceBase_KinectBasis*>(trackingDevice)->isFlipSupported());
		flipDropDownGrid.get()->Opacity(flipToggle.get()->IsEnabled() ? 1 : 0.5);
		TrackingDevices::settings_set_external_flip_is_enabled();
	}
	else if (trackingDevice.index() == 1)
	{
		// Joints Basis
		for (auto expander : jointExpanderVector)
			expander->EnableSoftwareOrientation(false);

		flipToggle.get()->IsEnabled(false);
		flipDropDown.get()->IsEnabled(false);
		flipDropDownGrid.get()->Opacity(0.5);
		TrackingDevices::settings_set_external_flip_is_enabled(false);
	}

	// Load the tracker configuration
	for (auto expander : jointExpanderVector)
		expander->UpdateIsActive();

	// Load auto-spawn and sounds config
	autoSpawnCheckbox->IsChecked(k2app::K2Settings.autoSpawnEnabledJoints);
	enableSoundsCheckbox->IsChecked(k2app::K2Settings.enableAppSounds);
	soundsVolumeSlider.get()->Value(k2app::K2Settings.appSoundsVolume);

	// Load tracker settings/enabled
	TrackingDevices::settings_trackersConfig_UpdateIsEnabled();
	
	// Notify of the setup end
	k2app::shared::settings::settings_localInitFinished = true;
}

void KinectToVR::implementation::SettingsPage::AutoSpawn_Checked(
	const Windows::Foundation::IInspectable& sender, const RoutedEventArgs& e)
{
	// Don't react to pre-init signals
	if (!k2app::shared::settings::settings_localInitFinished)return;

	k2app::K2Settings.autoSpawnEnabledJoints = true;
	// Save settings
	k2app::K2Settings.saveSettings();
}


void KinectToVR::implementation::SettingsPage::AutoSpawn_Unchecked(
	const Windows::Foundation::IInspectable& sender, const RoutedEventArgs& e)
{
	// Don't react to pre-init signals
	if (!k2app::shared::settings::settings_localInitFinished)return;

	k2app::K2Settings.autoSpawnEnabledJoints = false;
	// Save settings
	k2app::K2Settings.saveSettings();
}


void KinectToVR::implementation::SettingsPage::EnableSounds_Checked(
	const Windows::Foundation::IInspectable& sender, const RoutedEventArgs& e)
{
	// Don't react to pre-init signals
	if (!k2app::shared::settings::settings_localInitFinished)return;

	// Turn sounds on
	k2app::K2Settings.enableAppSounds = true;
	ElementSoundPlayer::State(ElementSoundPlayerState::On);

	// Save settings
	k2app::K2Settings.saveSettings();
}


void KinectToVR::implementation::SettingsPage::EnableSounds_Unchecked(
	const Windows::Foundation::IInspectable& sender, const RoutedEventArgs& e)
{
	// Don't react to pre-init signals
	if (!k2app::shared::settings::settings_localInitFinished)return;

	// Turn sounds on
	k2app::K2Settings.enableAppSounds = false;
	ElementSoundPlayer::State(ElementSoundPlayerState::Off);

	// Save settings
	k2app::K2Settings.saveSettings();
}


void KinectToVR::implementation::SettingsPage::SoundsVolumeSlider_ValueChanged(
	const Windows::Foundation::IInspectable& sender,
	const Controls::Primitives::RangeBaseValueChangedEventArgs& e)
{
	// Don't react to pre-init signals
	if (!k2app::shared::settings::settings_localInitFinished)return;

	// Change sounds level
	k2app::K2Settings.appSoundsVolume = k2app::shared::settings::soundsVolumeSlider.get()->Value();
	ElementSoundPlayer::Volume(std::clamp(
		static_cast<double>(k2app::K2Settings.appSoundsVolume) / 100.0, 0.0, 100.0));

	// Save settings
	k2app::K2Settings.saveSettings();
}


void KinectToVR::implementation::SettingsPage::CalibrateExternalFlipMenuFlyoutItem_Click(
	const Windows::Foundation::IInspectable& sender,
	const RoutedEventArgs& e)
{
	k2app::K2Settings.externalFlipCalibrationYaw =
		EigenUtils::QuatToEulers(
			k2app::K2Settings.K2TrackersVector.at(0).pose.orientation).y();

	LOG(INFO) << "Captured yaw for external flip: " <<
		radiansToDegrees(k2app::K2Settings.externalFlipCalibrationYaw) << "deg";
	k2app::K2Settings.saveSettings();
}


void KinectToVR::implementation::SettingsPage::FlipDropDown_Expanding(
	const winrt::Microsoft::UI::Xaml::Controls::Expander& sender,
	const winrt::Microsoft::UI::Xaml::Controls::ExpanderExpandingEventArgs& args)
{
	if (!k2app::shared::settings::settings_localInitFinished)return; // Don't even try if we're not set up yet

	// Enable/Disable ExtFlip
	TrackingDevices::settings_set_external_flip_is_enabled();
	TrackingDevices::settings_trackersConfig_UpdateIsEnabled();
}


void KinectToVR::implementation::SettingsPage::FlipToggle_Toggled(
	const Windows::Foundation::IInspectable& sender, const winrt::Microsoft::UI::Xaml::RoutedEventArgs& e)
{
	// Don't react to pre-init signals
	if (!k2app::shared::settings::settings_localInitFinished)return;

	// Cache flip to settings and save
	k2app::K2Settings.isFlipEnabled =
		k2app::shared::settings::flipToggle->IsOn(); // Checked?

	TrackingDevices::settings_set_external_flip_is_enabled();
	TrackingDevices::settings_trackersConfig_UpdateIsEnabled();

	k2app::K2Settings.saveSettings();
}


void KinectToVR::implementation::SettingsPage::AutoStartFlyout_Opening(
	const Windows::Foundation::IInspectable& sender, const Windows::Foundation::IInspectable& e)
{
	k2app::shared::settings::autoStartCheckBox->IsChecked(
		vr::VRApplications()->GetApplicationAutoLaunch("KinectToVR.Amethyst"));
}


void KinectToVR::implementation::SettingsPage::AutoStartCheckBox_Checked(
	const Windows::Foundation::IInspectable& sender, const winrt::Microsoft::UI::Xaml::RoutedEventArgs& e)
{
	k2app::interfacing::installApplicationManifest(); // Just in case

	const auto app_error = vr::VRApplications()->
		SetApplicationAutoLaunch("KinectToVR.Amethyst", true);

	if (app_error != vr::VRApplicationError_None)
		LOG(WARNING) << "Amethyst manifest not installed! Error:  " <<
			vr::VRApplications()->GetApplicationsErrorNameFromEnum(app_error);
}


void KinectToVR::implementation::SettingsPage::AutoStartCheckBox_Unchecked(
	const Windows::Foundation::IInspectable& sender, const winrt::Microsoft::UI::Xaml::RoutedEventArgs& e)
{
	k2app::interfacing::installApplicationManifest(); // Just in case

	const auto app_error = vr::VRApplications()->
		SetApplicationAutoLaunch("KinectToVR.Amethyst", false);

	if (app_error != vr::VRApplicationError_None)
		LOG(WARNING) << "Amethyst manifest not installed! Error:  " <<
			vr::VRApplications()->GetApplicationsErrorNameFromEnum(app_error);
}


void KinectToVR::implementation::SettingsPage::ReManifestButton_Click(
	const winrt::Microsoft::UI::Xaml::Controls::SplitButton& sender,
	const winrt::Microsoft::UI::Xaml::Controls::SplitButtonClickEventArgs& args)
{
	switch (k2app::interfacing::installApplicationManifest())
	{
	// Not found failure
	case 0:
		{
			k2app::shared::settings::setErrorFlyoutText->Text(
				k2app::interfacing::LocalizedResourceWString(L"SettingsPage", L"ReManifest/Error/NotFound"));

			Controls::Primitives::FlyoutShowOptions _opt;
			_opt.Placement(Controls::Primitives::FlyoutPlacementMode::RightEdgeAlignedBottom);
			SetErrorFlyout().ShowAt(ReManifestButton(), _opt);
			break;
		}
	// Generic success
	case 1:
		break;
	// SteamVR failure
	case 2:
		{
			k2app::shared::settings::setErrorFlyoutText->Text(
				k2app::interfacing::LocalizedResourceWString(L"SettingsPage", L"ReManifest/Error/Other"));

			Controls::Primitives::FlyoutShowOptions _opt;
			_opt.Placement(Controls::Primitives::FlyoutPlacementMode::RightEdgeAlignedBottom);
			SetErrorFlyout().ShowAt(ReManifestButton(), _opt);
			break;
		}
	}
}


void KinectToVR::implementation::SettingsPage::ReRegisterButton_Click(
	const Windows::Foundation::IInspectable& sender, const winrt::Microsoft::UI::Xaml::RoutedEventArgs& e)
{
	if (exists(boost::dll::program_location().parent_path() / "K2CrashHandler" / "K2CrashHandler.exe"))
	{
		std::thread([]
		{
			ShellExecuteA(nullptr, "open",
			              (boost::dll::program_location().parent_path() / "K2CrashHandler" / "K2CrashHandler.exe ")
			              .string().c_str(), nullptr, nullptr, SW_SHOWDEFAULT);
		}).detach();
	}
	else
	{
		LOG(WARNING) << "Crash handler exe (./K2CrashHandler/K2CrashHandler.exe) not found!";

		k2app::shared::settings::setErrorFlyoutText->Text(
			k2app::interfacing::LocalizedResourceWString(L"SettingsPage", L"ReRegister/Error/NotFound"));

		Controls::Primitives::FlyoutShowOptions _opt;
		_opt.Placement(Controls::Primitives::FlyoutPlacementMode::RightEdgeAlignedBottom);
		SetErrorFlyout().ShowAt(ReRegisterButton(), _opt);
	}
}


void KinectToVR::implementation::SettingsPage::DismissSetErrorButton_Click(
	const Windows::Foundation::IInspectable& sender, const winrt::Microsoft::UI::Xaml::RoutedEventArgs& e)
{
	SetErrorFlyout().Hide();
}


void winrt::KinectToVR::implementation::SettingsPage::LearnAboutFiltersButton_Click(
	const winrt::Windows::Foundation::IInspectable& sender, const winrt::Microsoft::UI::Xaml::RoutedEventArgs& e)
{
	Controls::Primitives::FlyoutShowOptions options;
	options.Placement(Controls::Primitives::FlyoutPlacementMode::Full);
	options.ShowMode(Controls::Primitives::FlyoutShowMode::Transient);

	LearnAboutFiltersFlyout().ShowAt(LearnAboutFiltersButton(), options);
	DimGrid().Opacity(0.5);
	DimGrid().IsHitTestVisible(true);
}


void winrt::KinectToVR::implementation::SettingsPage::LearnAboutFiltersFlyout_Closed(
	winrt::Windows::Foundation::IInspectable const& sender, winrt::Windows::Foundation::IInspectable const& e)
{
	DimGrid().Opacity(0.0);
	DimGrid().IsHitTestVisible(false);
}


void winrt::KinectToVR::implementation::SettingsPage::TrackerConfigButton_Click(
	winrt::Windows::Foundation::IInspectable const& sender, winrt::Microsoft::UI::Xaml::RoutedEventArgs const& e)
{
	auto trackerConfigFlyout = Controls::MenuFlyout();

	enum i_tracker_list
	{
		Tracker_Waist,
		Tracker_LeftFoot,
		Tracker_RightFoot,
		Tracker_LeftElbow,
		Tracker_RightElbow,
		Tracker_LeftKnee,
		Tracker_RightKnee,

		Tracker_Chest,
		Tracker_LeftShoulder,
		Tracker_RightShoulder,
		Tracker_Handed,
		Tracker_Camera,
		Tracker_Keyboard
	};

	std::map<i_tracker_list, ktvr::ITrackerType> tracker_map
	{
		{i_tracker_list::Tracker_Handed, ktvr::ITrackerType::Tracker_Handed},
		{i_tracker_list::Tracker_LeftFoot, ktvr::ITrackerType::Tracker_LeftFoot},
		{i_tracker_list::Tracker_RightFoot, ktvr::ITrackerType::Tracker_RightFoot},
		{i_tracker_list::Tracker_LeftShoulder, ktvr::ITrackerType::Tracker_LeftShoulder},
		{i_tracker_list::Tracker_RightShoulder, ktvr::ITrackerType::Tracker_RightShoulder},
		{i_tracker_list::Tracker_LeftElbow, ktvr::ITrackerType::Tracker_LeftElbow},
		{i_tracker_list::Tracker_RightElbow, ktvr::ITrackerType::Tracker_RightElbow},
		{i_tracker_list::Tracker_LeftKnee, ktvr::ITrackerType::Tracker_LeftKnee},
		{i_tracker_list::Tracker_RightKnee, ktvr::ITrackerType::Tracker_RightKnee},
		{i_tracker_list::Tracker_Waist, ktvr::ITrackerType::Tracker_Waist},
		{i_tracker_list::Tracker_Chest, ktvr::ITrackerType::Tracker_Chest},
		{i_tracker_list::Tracker_Camera, ktvr::ITrackerType::Tracker_Camera},
		{i_tracker_list::Tracker_Keyboard, ktvr::ITrackerType::Tracker_Keyboard}
	};

	for (uint32_t index = i_tracker_list::Tracker_Chest;
	     index <= static_cast<int>(i_tracker_list::Tracker_Keyboard); index++)
	{
		// Back the current tracker's role up
		ktvr::ITrackerType current_tracker =
			tracker_map[static_cast<i_tracker_list>(index)];

		auto menuTrackerToggleItem = Controls::ToggleMenuFlyoutItem();

		menuTrackerToggleItem.Text(
			k2app::interfacing::LocalizedResourceWString(
				L"SharedStrings", L"Joints/Enum/" +
				std::to_wstring(static_cast<int>(current_tracker))));

		bool isEnabled = (index >= static_cast<int>(
			     i_tracker_list::Tracker_Chest)),
		     isChecked = (index < static_cast<int>(
			     i_tracker_list::Tracker_Chest));

		for (const auto& tracker : k2app::K2Settings.K2TrackersVector)
			if (tracker.tracker == tracker_map[static_cast<i_tracker_list>(index)])
				isChecked = true; // Tracker is enabled

		menuTrackerToggleItem.IsEnabled(isEnabled);
		menuTrackerToggleItem.IsChecked(isChecked);

		menuTrackerToggleItem.Click(
			[&, index, tracker_map, current_tracker, this]
		(const winrt::Windows::Foundation::IInspectable& sender, const RoutedEventArgs& e) 
			-> winrt::Windows::Foundation::IAsyncAction
			{
				// Notify of the setup end
				k2app::shared::settings::settings_localInitFinished = false;

				// Create a new tracker / Remove the unchecked one
				if (sender.as<Controls::ToggleMenuFlyoutItem>().IsChecked())
				{
					// If not checked, add a new tracker
					k2app::K2Settings.K2TrackersVector.push_back(k2app::K2AppTracker());

					// Set the newly created tracker up
					k2app::K2Settings.K2TrackersVector.back().tracker = current_tracker;
					k2app::K2Settings.K2TrackersVector.back().data.serial =
						k2app::ITrackerType_Role_Serial[k2app::K2Settings.K2TrackersVector.back().tracker];
				}
				else
				// If the tracker was unchecked
					for (uint32_t _t = 0; _t < k2app::K2Settings.K2TrackersVector.size(); _t++)
						if (k2app::K2Settings.K2TrackersVector[_t].tracker == current_tracker) {
							
							// Make actual changes
							ktvr::set_tracker_state<false>(
								k2app::K2Settings.K2TrackersVector.at(_t).tracker, false);

							// Sleep on UI's background
							apartment_context _ui_thread;
							co_await resume_background();
							Sleep(20);
							co_await _ui_thread;

							k2app::K2Settings.K2TrackersVector.erase(
								k2app::K2Settings.K2TrackersVector.begin() + _t);

							// Check if we've disabled any joints from spawning and disable their mods
							k2app::interfacing::devices_check_disabled_joints();
							TrackingDevices::settings_trackersConfigChanged();
							
							// Save settings
							k2app::K2Settings.saveSettings();
						}

				// Rebuild joint the expander stack
				using namespace k2app::shared::settings;

				LOG(INFO) << "Rebuilding joint expanders... this may take a while...";
				jointExpanderHostStackPanel->Transitions().Append(Media::Animation::ContentThemeTransition());

				jointExpanderVector.clear();
				jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
					new Controls::JointExpander({&k2app::K2Settings.K2TrackersVector[0]}))));

				if (k2app::K2Settings.useTrackerPairs)
				{
					LOG(INFO) << "UseTrackerPairs is set to true: Appending the default expanders as pairs...";

					jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
						new Controls::JointExpander(
							{&k2app::K2Settings.K2TrackersVector[1], &k2app::K2Settings.K2TrackersVector[2]},
							k2app::interfacing::LocalizedResourceWString(
								L"SharedStrings", L"Joints/Pairs/Feet")))));

					jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
						new Controls::JointExpander(
							{&k2app::K2Settings.K2TrackersVector[3], &k2app::K2Settings.K2TrackersVector[4]},
							k2app::interfacing::LocalizedResourceWString(
								L"SharedStrings", L"Joints/Pairs/Elbows")))));

					jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
						new Controls::JointExpander(
							{&k2app::K2Settings.K2TrackersVector[5], &k2app::K2Settings.K2TrackersVector[6]},
							k2app::interfacing::LocalizedResourceWString(
								L"SharedStrings", L"Joints/Pairs/Knees")))));
				}

				LOG(INFO) << "Appending additional expanders (if they exist)...";

				// k2app::K2Settings.useTrackerPairs ? 7 : 1 means that if pairs have
				// already been appended, we'll start after them, and if not -
				// - we'll append them as individual tracker/joint expanders

				for (uint32_t ind = (k2app::K2Settings.useTrackerPairs ? 7 : 1);
				     ind < k2app::K2Settings.K2TrackersVector.size(); ind++)
					jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
						new Controls::JointExpander({&k2app::K2Settings.K2TrackersVector[ind]}))));

				LOG(INFO) << "Clearing the appended expanders (UI Node)";
				settings_safe_clear(jointExpanderHostStackPanel);

				LOG(INFO) << "Appending the new expanders to the UI Node";

				int _expander_number = (k2app::K2Settings.useTrackerPairs ? 1 : 2); // For separators
				for (auto expander : jointExpanderVector)
				{
					// Append the expander
					jointExpanderHostStackPanel->Children().Append(*expander->Container());

					// Append the separator (optionally)
					if (_expander_number >= 2 && jointExpanderVector.back() != expander)
					{
						auto separator = Shapes::Rectangle();
						separator.HorizontalAlignment(HorizontalAlignment::Stretch);
						separator.Height(1);
						separator.Margin({0, 10, 0, 0});
						separator.Stroke(Media::SolidColorBrush(
							Windows::UI::ColorHelper::FromArgb(255, 59, 59, 59)));
						separator.Fill(Media::SolidColorBrush(
							Windows::UI::ColorHelper::FromArgb(255, 59, 59, 59)));

						jointExpanderHostStackPanel->Children().Append(separator);
						_expander_number = 1;
					}
					else _expander_number++;
				}

				if (const auto& trackingDevice = TrackingDevices::getCurrentDevice();
					trackingDevice.index() == 0)
				{
					// Kinect Basis
					const bool _sup = std::get<ktvr::K2TrackingDeviceBase_KinectBasis*>(trackingDevice)->
						isAppOrientationSupported();

					for (auto expander : jointExpanderVector)
						expander->EnableSoftwareOrientation(_sup);

					flipToggle.get()->IsEnabled(
						std::get<ktvr::K2TrackingDeviceBase_KinectBasis*>(trackingDevice)->isFlipSupported());
					flipDropDown.get()->IsEnabled(
						std::get<ktvr::K2TrackingDeviceBase_KinectBasis*>(trackingDevice)->isFlipSupported());
					flipDropDownGrid.get()->Opacity(flipToggle.get()->IsEnabled() ? 1 : 0.5);
					TrackingDevices::settings_set_external_flip_is_enabled();
				}
				else if (trackingDevice.index() == 1)
				{
					// Joints Basis
					for (auto expander : jointExpanderVector)
						expander->EnableSoftwareOrientation(false);

					flipToggle.get()->IsEnabled(false);
					flipDropDown.get()->IsEnabled(false);
					flipDropDownGrid.get()->Opacity(0.5);
					TrackingDevices::settings_set_external_flip_is_enabled(false);
				}

				// Load the tracker configuration
				for (auto expander : jointExpanderVector)
					expander->UpdateIsActive();

				// Enable/Disable combos
				TrackingDevices::settings_trackersConfig_UpdateIsEnabled();

				// Enable/Disable ExtFlip
				TrackingDevices::settings_set_external_flip_is_enabled();

				// Notify of the setup end
				k2app::shared::settings::settings_localInitFinished = true;
				k2app::K2Settings.saveSettings();

				{
					// Sleep on UI
					apartment_context ui_thread;
					co_await resume_background();
					Sleep(50);
					co_await ui_thread;
				}
				jointExpanderHostStackPanel->Transitions().RemoveAtEnd();
				co_return;
			});

		// Append the item
		trackerConfigFlyout.Items().Append(menuTrackerToggleItem);
	}

	auto menuPairsToggleItem = Controls::ToggleMenuFlyoutItem();
	menuPairsToggleItem.Text(
		k2app::interfacing::LocalizedResourceWString(
			L"SettingsPage", L"Captions/TrackerPairs"));

	menuPairsToggleItem.IsChecked(k2app::K2Settings.useTrackerPairs);
	menuPairsToggleItem.Click(
		[&, this](const winrt::Windows::Foundation::IInspectable& sender, 
			const RoutedEventArgs& e) -> Windows::Foundation::IAsyncAction
		{
			// Notify of the setup end
			k2app::shared::settings::settings_localInitFinished = false;

			k2app::K2Settings.useTrackerPairs = sender.as<Controls::ToggleMenuFlyoutItem>().IsChecked();

			// Rebuild joint the expander stack
			using namespace k2app::shared::settings;

			LOG(INFO) << "Rebuilding joint expanders... this may take a while...";
			jointExpanderHostStackPanel->Transitions().Append(Media::Animation::ContentThemeTransition());

			jointExpanderVector.clear();
			jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
				new Controls::JointExpander({&k2app::K2Settings.K2TrackersVector[0]}))));

			if (k2app::K2Settings.useTrackerPairs)
			{
				LOG(INFO) << "UseTrackerPairs is set to true: Appending the default expanders as pairs...";

				jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
					new Controls::JointExpander(
						{&k2app::K2Settings.K2TrackersVector[1], &k2app::K2Settings.K2TrackersVector[2]},
						k2app::interfacing::LocalizedResourceWString(
							L"SharedStrings", L"Joints/Pairs/Feet")))));

				jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
					new Controls::JointExpander(
						{&k2app::K2Settings.K2TrackersVector[3], &k2app::K2Settings.K2TrackersVector[4]},
						k2app::interfacing::LocalizedResourceWString(
							L"SharedStrings", L"Joints/Pairs/Elbows")))));

				jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
					new Controls::JointExpander(
						{&k2app::K2Settings.K2TrackersVector[5], &k2app::K2Settings.K2TrackersVector[6]},
						k2app::interfacing::LocalizedResourceWString(
							L"SharedStrings", L"Joints/Pairs/Knees")))));
			}

			LOG(INFO) << "Appending additional expanders (if they exist)...";

			// k2app::K2Settings.useTrackerPairs ? 7 : 1 means that if pairs have
			// already been appended, we'll start after them, and if not -
			// - we'll append them as individual tracker/joint expanders

			for (uint32_t ind = (k2app::K2Settings.useTrackerPairs ? 7 : 1);
			     ind < k2app::K2Settings.K2TrackersVector.size(); ind++)
				jointExpanderVector.push_back(std::move(std::shared_ptr<Controls::JointExpander>(
					new Controls::JointExpander({&k2app::K2Settings.K2TrackersVector[ind]}))));

			LOG(INFO) << "Clearing the appended expanders (UI Node)";
			settings_safe_clear(jointExpanderHostStackPanel);

			LOG(INFO) << "Appending the new expanders to the UI Node";

			int _expander_number = (k2app::K2Settings.useTrackerPairs ? 1 : 2); // For separators
			for (auto expander : jointExpanderVector)
			{
				// Append the expander
				jointExpanderHostStackPanel->Children().Append(*expander->Container());

				// Append the separator (optionally)
				if (_expander_number >= 2 && jointExpanderVector.back() != expander)
				{
					auto separator = Shapes::Rectangle();
					separator.HorizontalAlignment(HorizontalAlignment::Stretch);
					separator.Height(1);
					separator.Margin({0, 10, 0, 0});
					separator.Stroke(Media::SolidColorBrush(
						Windows::UI::ColorHelper::FromArgb(255, 59, 59, 59)));
					separator.Fill(Media::SolidColorBrush(
						Windows::UI::ColorHelper::FromArgb(255, 59, 59, 59)));

					jointExpanderHostStackPanel->Children().Append(separator);
					_expander_number = 1;
				}
				else _expander_number++;
			}

			if (const auto& trackingDevice = TrackingDevices::getCurrentDevice();
				trackingDevice.index() == 0)
			{
				// Kinect Basis
				const bool _sup = std::get<ktvr::K2TrackingDeviceBase_KinectBasis*>(trackingDevice)->
					isAppOrientationSupported();

				for (auto expander : jointExpanderVector)
					expander->EnableSoftwareOrientation(_sup);

				flipToggle.get()->IsEnabled(
					std::get<ktvr::K2TrackingDeviceBase_KinectBasis*>(trackingDevice)->isFlipSupported());
				flipDropDown.get()->IsEnabled(
					std::get<ktvr::K2TrackingDeviceBase_KinectBasis*>(trackingDevice)->isFlipSupported());
				flipDropDownGrid.get()->Opacity(flipToggle.get()->IsEnabled() ? 1 : 0.5);
				TrackingDevices::settings_set_external_flip_is_enabled();
			}
			else if (trackingDevice.index() == 1)
			{
				// Joints Basis
				for (auto expander : jointExpanderVector)
					expander->EnableSoftwareOrientation(false);

				flipToggle.get()->IsEnabled(false);
				flipDropDown.get()->IsEnabled(false);
				flipDropDownGrid.get()->Opacity(0.5);
				TrackingDevices::settings_set_external_flip_is_enabled(false);
			}

			// Load the tracker configuration
			for (auto expander : jointExpanderVector)
				expander->UpdateIsActive();

			// Enable/Disable combos
			TrackingDevices::settings_trackersConfig_UpdateIsEnabled();

			// Enable/Disable ExtFlip
			TrackingDevices::settings_set_external_flip_is_enabled();

			// Notify of the setup end
			k2app::shared::settings::settings_localInitFinished = true;
			k2app::K2Settings.saveSettings();
			k2app::K2Settings.readSettings(); // Calls config check

			{
				// Sleep on UI
				apartment_context ui_thread;
				co_await resume_background();
				Sleep(100);
				co_await ui_thread;
			}
			jointExpanderHostStackPanel->Transitions().RemoveAtEnd();
			co_return;
		});

	// Append the item
	trackerConfigFlyout.Items().Append(Controls::MenuFlyoutSeparator());
	trackerConfigFlyout.Items().Append(menuPairsToggleItem);

	trackerConfigFlyout.Placement(Controls::Primitives::FlyoutPlacementMode::LeftEdgeAlignedBottom);
	trackerConfigFlyout.ShowMode(Controls::Primitives::FlyoutShowMode::Transient);
	trackerConfigFlyout.ShowAt(TrackerConfigButton());
}


void winrt::KinectToVR::implementation::SettingsPage::CheckOverlapsCheckBox_Checked(
	winrt::Windows::Foundation::IInspectable const& sender, winrt::Microsoft::UI::Xaml::RoutedEventArgs const& e)
{
	// Don't react to pre-init signals
	if (!k2app::shared::settings::settings_localInitFinished)return;

	k2app::K2Settings.checkForOverlappingTrackers = true;
	k2app::K2Settings.saveSettings();
}


void winrt::KinectToVR::implementation::SettingsPage::CheckOverlapsCheckBox_Unchecked(
	winrt::Windows::Foundation::IInspectable const& sender, winrt::Microsoft::UI::Xaml::RoutedEventArgs const& e)
{
	// Don't react to pre-init signals
	if (!k2app::shared::settings::settings_localInitFinished)return;

	k2app::K2Settings.checkForOverlappingTrackers = false;
	k2app::K2Settings.saveSettings();
}
