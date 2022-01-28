#pragma once
#include "pch.h"

#include "K2EVRInput.h"
#include "K2Shared.h"

namespace k2app
{
	namespace interfacing
	{
		inline std::vector<K2AppTracker> K2TrackersVector{
			K2AppTracker("LHR-CB9AD1T0", ktvr::Tracker_Waist),
			K2AppTracker("LHR-CB9AD1T1", ktvr::Tracker_LeftFoot),
			K2AppTracker("LHR-CB9AD1T2", ktvr::Tracker_RightFoot)
		};

		inline void ShowToast(std::string const& header, std::string const& text)
		{
			// Unsupported in AppSDK 1.0

			//using namespace winrt::Windows::UI::Notifications;
			//using namespace winrt::Windows::Data::Xml::Dom;

			//// Construct the XML toast template
			//XmlDocument document;
			//document.LoadXml(L"\
			//	<toast>\
			//		<visual>\
			//	        <binding template=\"ToastGeneric\">\
			//	            <text></text>\
			//	            <text></text>\
			//	        </binding>\
			//	    </visual>\
			//	</toast>");

			//// Populate with text and values
			//document.SelectSingleNode(L"//text[1]").InnerText(wstring_cast(header));
			//document.SelectSingleNode(L"//text[2]").InnerText(wstring_cast(text));

			//// Construct the notification
			//ToastNotification notification{ document };
			//ToastNotifier toastNotifier{ ToastNotificationManager::CreateToastNotifier() };

			//// And show it!
			//toastNotifier.Show(notification);
		}

		inline bool SpawnDefaultEnabledTrackers()
		{
			return false; // TODO
		}

		/**
		 * \brief This will init OpenVR
		 * \return Success?
		 */
		inline bool OpenVRStartup()
		{
			LOG(INFO) << "Attempting connection to VRSystem... ";

			vr::EVRInitError eError = vr::VRInitError_None;
			vr::IVRSystem* m_VRSystem = VR_Init(&eError, vr::VRApplication_Overlay);

			if (eError != vr::VRInitError_None)
			{
				LOG(ERROR) << "IVRSystem could not be initialized: EVRInitError Code " << static_cast<int>(eError);
				MessageBoxA(nullptr,
				            std::string(
					            "Couldn't initialise VR system. (Code " + std::to_string(eError) +
					            ")\n\nPlease check if SteamVR is installed (or running) and try again."
				            ).c_str(),
				            "IVRSystem Init Failure!",
				            MB_OK);

				return false; // Fail
			}
			else return true; // OK
		}

		/**
		 * \brief This will init VR Input Actions
		 * \return Success?
		 */
		inline bool EVRActionsStartup()
		{
			LOG(INFO) << "Attempting to set up EVR Input Actions...";

			K2EVRInput::SteamEVRInput evr_input;

			if (!evr_input.InitInputActions())
			{
				LOG(ERROR) << "Could not set up Input Actions. Please check the upper log for further information.";
				/*MessageBoxA(nullptr,
				            std::string(
					            "Couldn't set up Input Actions.\n\nPlease check the log file for further information."
				            ).c_str(),
				            "EVR Input Actions Init Failure!",
				            MB_OK);*/

				return false;
			}

			LOG(INFO) << "EVR Input Actions set up OK";
			return true;
		}

		// Server checking threads number, max num of them
		inline uint32_t pingCheckingThreadsNumber = 0,
		                maxPingCheckingThreads = 3;

		// Server interfacing data
		inline int serverDriverStatusCode = 0;
		inline uint32_t pingTime = 0, parsingTime = 0;
		inline bool isServerDriverPresent = false,
		            serverDriverFailure = false;
		inline std::string serverStatusString = " \n \n ";

		/**
		 * \brief This will init K2API and server driver
		 * \return Success?
		 */
		inline bool TestK2ServerConnection()
		{
			// Do not spawn 1000 voids, check how many do we have
			if (pingCheckingThreadsNumber <= maxPingCheckingThreads)
			{
				// Add a new worker
				pingCheckingThreadsNumber += 1; // May be ++ too

				try
				{
					// Send a ping message and capture the data
					const auto [test_response, send_time, full_time] = ktvr::test_connection();

					// Dump data to variables
					pingTime = full_time;
					parsingTime = std::clamp( // Subtract message creation (got) time and send time
						test_response.messageTimestamp - test_response.messageManualTimestamp,
						static_cast<long long>(1), LLONG_MAX);

					// Log ?success
					LOG(INFO) <<
						"Connection test has ended, [result: " <<
						(test_response.success ? "success" : "fail") <<
						"], response code: " << test_response.result;

					// Log some data if needed
					LOG(INFO) <<
						"\nTested ping time: " << full_time << " [micros], " <<

						"call time: " <<
						std::clamp( // Subtract message creation (got) time and send time
							send_time - test_response.messageManualTimestamp,
							static_cast<long long>(0), LLONG_MAX) <<
						" [micros], " <<

						"\nparsing time: " <<
						parsingTime << // Just look at the k2api
						" [micros], "

						"flight-back time: " <<
						std::clamp( // Subtract message creation (got) time and send time
							K2API_GET_TIMESTAMP_NOW - test_response.messageManualTimestamp,
							static_cast<long long>(1), LLONG_MAX) <<
						" [micros]";

					// Release
					pingCheckingThreadsNumber = std::clamp(
						int(pingCheckingThreadsNumber) - 1, 0,
						int(maxPingCheckingThreads) + 1);

					// Return the result
					return test_response.success;
				}
				catch (const std::exception& e)
				{
					// Log ?success
					LOG(INFO) <<
						"Connection test has ended, [result: fail], got an exception";

					// Release
					pingCheckingThreadsNumber = std::clamp(
						int(pingCheckingThreadsNumber) - 1, 0,
						int(maxPingCheckingThreads) + 1);
					return false;
				}
			}

			// else
			LOG(ERROR) << "Connection checking threads exceeds 3, aborting...";
			return false;
		}

		/**
		 * \brief This will check K2API and server driver
		 * \return Success?
		 */
		inline int CheckK2ServerStatus()
		{
			if (!isServerDriverPresent)
			{
				try
				{
					/* Initialize the port */
					LOG(INFO) << "Initializing the server IPC...";
					const auto init_code = ktvr::init_k2api();
					bool server_connected = false;

					LOG(INFO) << "Server IPC initialization " <<
						(init_code == 0 ? "succeed" : "failed") << ", exit code: " << init_code;

					/* Connection test and display ping */
					// We may wait
					LOG(INFO) << "Testing the connection...";

					for (int i = 0; i < 3; i++)
					{
						LOG(INFO) << "Starting the test no " << i + 1 << "...";
						server_connected = true; // TestK2ServerConnection();
						// Not direct assignment since it's only a one-way check
						if (server_connected)isServerDriverPresent = true;
					}

					return init_code == 0
						       ? (server_connected ? 1 : -1)
						       : -10;
				}
				catch (const std::exception& e) { return -10; }
			}

			/*
			 * codes:
			 codes:
				-10: driver is disabled
				-1: driver is workin but outdated or doomed
				10: ur pc brokey, cry about it
				1: ok
			 */
			return 1; //don't check if it was already working
		}

		/**
		 * \brief This will init K2API and server driver
		 * \return Success?
		 */
		inline void K2ServerDriverSetup()
		{
			if (!serverDriverFailure)
			{
				// Backup the status
				serverDriverStatusCode = CheckK2ServerStatus();
			}
			else
			{
				// Overwrite the status
				serverDriverStatusCode = 10; // Fatal
			}

			isServerDriverPresent = false; // Assume fail
			std::string server_status =
				"COULD NOT CHECK STATUS (Code -12)\nE_WTF\nSomething's fucked a really big time.";

			switch (serverDriverStatusCode)
			{
			case -10:
				server_status =
					"EXCEPTION WHILE CHECKING (Code -10)\nE_EXCEPTION_WHILE_CHECKING\nCheck SteamVR add-ons (NOT overlays) and enable KinectToVR.";
				break;
			case -1:
				server_status =
					"SERVER CONNECTION ERROR (Code -1)\nE_CONNECTION_ERROR\nYour KinectToVR SteamVR driver may be broken or outdated.";
				break;
			case 10:
				server_status =
					"FATAL SERVER FAILURE (Code 10)\nE_FATAL_SERVER_FAILURE\nPlease restart, check logs and write to us on Discord.";
				break;
			case 1:
				server_status = "Success! (Code 1)\nI_OK\nEverything's good!";
				isServerDriverPresent = true; // Change to success
				break;
			default:
				server_status =
					"COULD NOT CONNECT TO K2API (Code -11)\nE_K2API_FAILURE\nThis error shouldn't occur, actually. Something's wrong a big part.";
				break;
			}

			// LOG the status
			LOG(INFO) << "Current K2 Server status: " << server_status;
			serverStatusString = server_status;
		}

		inline void UpdateServerStatusUI()
		{
			// Update the status here
			using namespace winrt::Microsoft::UI::Xaml;
			
			// Disable UI (partially) if we've encountered an error
			if (::k2app::shared::main::devicesItem.get() != nullptr)
			{
				//::k2app::shared::main::settingsItem.get()->IsEnabled(isServerDriverPresent);
				::k2app::shared::main::devicesItem.get()->IsEnabled(isServerDriverPresent);
			}
			
			// Check with this one, should be the same for all anyway
			if (::k2app::shared::general::serverErrorWhatText.get() != nullptr)
			{
				::k2app::shared::general::serverErrorWhatText.get()->Visibility(
					isServerDriverPresent ? Visibility::Collapsed : Visibility::Visible);
				::k2app::shared::general::serverErrorWhatGrid.get()->Visibility(
					isServerDriverPresent ? Visibility::Collapsed : Visibility::Visible);
				::k2app::shared::general::serverErrorButtonsGrid.get()->Visibility(
					isServerDriverPresent ? Visibility::Collapsed : Visibility::Visible);
				::k2app::shared::general::serverErrorLabel.get()->Visibility(
					isServerDriverPresent ? Visibility::Collapsed : Visibility::Visible);

				// Split status and message by \n
				::k2app::shared::general::serverStatusLabel.get()->Text(
					wstring_cast(split_status(serverStatusString)[0]));
				::k2app::shared::general::serverErrorLabel.get()->Text(
					wstring_cast(split_status(serverStatusString)[1]));
				::k2app::shared::general::serverErrorWhatText.get()->Text(
					wstring_cast(split_status(serverStatusString)[2]));
			}

			// Block some things if server isn't working properly
			if (!isServerDriverPresent)
			{
				LOG(ERROR) <<
					"An error occurred and the app couldn't connect to K2 Server. Please check the upper message for more info.";

				if (::k2app::shared::general::errorWhatText.get() != nullptr)
				{
					LOG(INFO) << "[Server Error] Entering the server error state...";

					// Hide device error labels (if any)
					::k2app::shared::general::errorWhatText.get()->Visibility(Visibility::Collapsed);
					::k2app::shared::general::errorWhatGrid.get()->Visibility(Visibility::Collapsed);
					::k2app::shared::general::errorButtonsGrid.get()->Visibility(Visibility::Collapsed);
					::k2app::shared::general::trackingDeviceErrorLabel.get()->Visibility(
						Visibility::Collapsed);

					// Block spawn|offsets|calibration buttons, //disable autospawn for session (just don't save)
					::k2app::shared::general::toggleTrackersButton.get()->IsEnabled(false);
					::k2app::shared::general::calibrationButton.get()->IsEnabled(false);
					::k2app::shared::general::offsetsButton.get()->IsEnabled(false);
					//::k2app::K2Settings.autoSpawnEnabledJoints = false;
				}
			}
		}
	}
}
