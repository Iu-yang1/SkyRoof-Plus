#include "rtc_rx_peer.h"
#include <rtc/rtc.h>
#include <atomic>
#include <chrono>
#include <cstring>
#include <iostream>
#include <thread>
#include <vector>
using namespace std::chrono_literals;
struct Test {
    std::atomic<bool> gathered{false};
    std::atomic<bool> received{false};
};
static void gathering(int, rtcGatheringState state, void* user) {
    if (state == RTC_GATHERING_COMPLETE) static_cast<Test*>(user)->gathered = true;
}
static void message(int, const char* data, int size, void* user) {
    if (size == 4 && std::memcmp(data, "SAP1", 4) == 0)
        static_cast<Test*>(user)->received = true;
}
int main() {
    Test state;
    rtcConfiguration config{};
    int client = rtcCreatePeerConnection(&config);
    if (client < 0) return 1;
    rtcSetUserPointer(client, &state);
    rtcSetGatheringStateChangeCallback(client, gathering);
    int dc = rtcCreateDataChannel(client, "skyroof-rx-pcm");
    if (dc < 0) return 2;
    rtcSetUserPointer(dc, &state);
    rtcSetMessageCallback(dc, message);
    void* server = skyroof_rtc_create(nullptr);
    if (!server) return 3;
    const auto deadline = std::chrono::steady_clock::now() + 25s;
    while (!state.gathered && std::chrono::steady_clock::now() < deadline)
        std::this_thread::sleep_for(20ms);
    std::vector<char> sdp(65536);
    if (!state.gathered || rtcGetLocalDescription(client, sdp.data(), static_cast<int>(sdp.size())) <= 0 ||
        skyroof_rtc_offer(server, sdp.data()) < 0) return 4;
    int answer = 0;
    while (answer == 0 && std::chrono::steady_clock::now() < deadline) {
        answer = skyroof_rtc_answer(server, sdp.data(), static_cast<int>(sdp.size()));
        std::this_thread::sleep_for(20ms);
    }
    if (answer <= 0 || rtcSetRemoteDescription(client, sdp.data(), "answer") < 0) return 5;
    while (!state.received && std::chrono::steady_clock::now() < deadline) {
        skyroof_rtc_send(server, "SAP1", 4);
        std::this_thread::sleep_for(20ms);
    }
    skyroof_rtc_destroy(server);
    rtcDeleteDataChannel(dc);
    rtcDeletePeerConnection(client);
    if (!state.received) return 6;
    std::cout << "PASS: deployed native RTC RX bridge, non-trickle SDP and binary DataChannel\n";
}
