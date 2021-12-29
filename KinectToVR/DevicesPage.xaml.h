﻿#pragma once
#include "DevicesPage.g.h"
#include "TrackingDevicesView.h"

#include "TrackingDevices.h"

namespace winrt::KinectToVR::implementation
{
	struct DevicesPage : DevicesPageT<DevicesPage>
	{
		DevicesPage();

		void TrackingDeviceListView_SelectionChanged(winrt::Windows::Foundation::IInspectable const& sender,
		                                             winrt::Microsoft::UI::Xaml::Controls::SelectionChangedEventArgs
		                                             const& e);
		void ReconnectDeviceButton_Click(winrt::Windows::Foundation::IInspectable const& sender, winrt::Microsoft::UI::Xaml::RoutedEventArgs const& e);
	};
}

namespace winrt::KinectToVR::factory_implementation
{
	struct DevicesPage : DevicesPageT<DevicesPage, implementation::DevicesPage>
	{
	};
}
