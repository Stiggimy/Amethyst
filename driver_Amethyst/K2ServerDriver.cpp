#include "pch.h"

#include "K2ServerDriver.h"
#include <thread>

int K2ServerDriver::init_server_driver(const int& port)
{
	// Add 1 tracker for each role
	for (uint32_t role = 0;
	     role <= static_cast<int>(ktvr::ITrackerType::Tracker_Keyboard); role++)
	{
		auto tracker_base = ktvr::K2TrackerBase();
		tracker_base.mutable_data()->set_serial(
			ITrackerType_Role_Serial.at(static_cast<ktvr::ITrackerType>(role)));
		tracker_base.mutable_data()->set_role(static_cast<ktvr::ITrackerType>(role));
		tracker_base.mutable_data()->set_isactive(false); // AutoAdd: false

		tracker_vector.emplace_back(tracker_base);
	}

	// Log the prepended trackers
	for (auto& tracker : tracker_vector)
		LOG(INFO) << "Registered a tracker: " << tracker.get_serial();

	try
	{
		// Start the RPC server
		grpc::ServerBuilder builder;

		// Listen on the given address without any authentication mechanism.
		// Register "service_" as the instance through which we'll communicate with
		// clients. In this case it corresponds to a *synchronous* service.
		builder.AddListeningPort(std::format("0.0.0.0:{}", port), grpc::InsecureServerCredentials());
		builder.RegisterService(&service_); // Register the synchronous service

		// Finally assemble the server.
		if (server_ = builder.BuildAndStart(); server_ == nullptr)
		{
			LOG(ERROR) << "Failed to build the RPC server due to unknown reasons!";
			return 1; // Anything besides 0 means a failure
		}
		LOG(INFO) << std::format("Server listening on 0.0.0.0:{}", port);

		// Wait for the server to exit
		std::thread([this]
		{
			LOG(INFO) << "Server thread started!";

			// Wait until the server thread ends
			server_->Wait(); // Note: blocking
			LOG(INFO) << "Server thread closed!";
		}).detach();
	}
	catch (...)
	{
		LOG(ERROR) << "Failed to set up the RPC service due to unknown reasons!";
		return -1; // Anything besides 0 means a failure again
	}

	// If everything went OK
	return 0;
}

void K2ServerDriver::kill_server_driver() const
{
	// Kill the RPC server
	server_->Shutdown();
}
