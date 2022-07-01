﻿#pragma once

#include "SettingsPage.g.h"
#include "TrackingDevices.h"
#include "JointExpander.h"
#include "LocalizedServerStatuses.h"

namespace winrt::Amethyst::implementation
{
	struct SettingsPage : SettingsPageT<SettingsPage>
	{
		SettingsPage();

		void ExternalFlipCheckBox_Checked(const Windows::Foundation::IInspectable& sender,
		                                  const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void ExternalFlipCheckBox_Unchecked(const Windows::Foundation::IInspectable& sender,
		                                    const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void RestartButton_Click(const Windows::Foundation::IInspectable& sender,
		                         const Microsoft::UI::Xaml::RoutedEventArgs& e);
		Windows::Foundation::IAsyncAction ResetButton_Click(
			const Windows::Foundation::IInspectable& sender,
			const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void SettingsPage_Loaded(const Windows::Foundation::IInspectable& sender,
		                         const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void AutoSpawn_Checked(const Windows::Foundation::IInspectable& sender,
		                       const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void AutoSpawn_Unchecked(const Windows::Foundation::IInspectable& sender,
		                         const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void EnableSounds_Checked(const Windows::Foundation::IInspectable& sender,
		                          const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void EnableSounds_Unchecked(const Windows::Foundation::IInspectable& sender,
		                            const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void SoundsVolumeSlider_ValueChanged(const Windows::Foundation::IInspectable& sender,
		                                     const
		                                     Microsoft::UI::Xaml::Controls::Primitives::RangeBaseValueChangedEventArgs
		                                     & e);
		void CalibrateExternalFlipMenuFlyoutItem_Click(const Windows::Foundation::IInspectable& sender,
		                                               const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void FlipDropDown_Expanding(const Microsoft::UI::Xaml::Controls::Expander& sender,
		                            const Microsoft::UI::Xaml::Controls::ExpanderExpandingEventArgs& args);
		void FlipToggle_Toggled(const Windows::Foundation::IInspectable& sender,
		                        const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void AutoStartFlyout_Opening(const Windows::Foundation::IInspectable& sender,
		                             const Windows::Foundation::IInspectable& e);
		void AutoStartCheckBox_Checked(const Windows::Foundation::IInspectable& sender,
		                               const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void AutoStartCheckBox_Unchecked(const Windows::Foundation::IInspectable& sender,
		                                 const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void ReManifestButton_Click(const Microsoft::UI::Xaml::Controls::SplitButton& sender,
		                            const Microsoft::UI::Xaml::Controls::SplitButtonClickEventArgs& args);
		void ReRegisterButton_Click(const Windows::Foundation::IInspectable& sender,
		                            const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void DismissSetErrorButton_Click(const Windows::Foundation::IInspectable& sender,
		                                 const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void LearnAboutFiltersButton_Click(const Windows::Foundation::IInspectable& sender,
		                                   const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void LearnAboutFiltersFlyout_Closed(const Windows::Foundation::IInspectable& sender,
		                                    const Windows::Foundation::IInspectable& e);
		void TrackerConfigButton_Click(const Windows::Foundation::IInspectable& sender,
		                               const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void CheckOverlapsCheckBox_Checked(const Windows::Foundation::IInspectable& sender,
		                                   const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void CheckOverlapsCheckBox_Unchecked(const Windows::Foundation::IInspectable& sender,
		                                     const Microsoft::UI::Xaml::RoutedEventArgs& e);
		void ViewLogsButton_Click(const Windows::Foundation::IInspectable& sender,
		                          const Microsoft::UI::Xaml::RoutedEventArgs& e);
	};
}

namespace winrt::Amethyst::factory_implementation
{
	struct SettingsPage : SettingsPageT<SettingsPage, implementation::SettingsPage>
	{
	};
}
