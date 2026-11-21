#include <rtc/rtc.h>
#include <atomic>
#include <chrono>
#include <cstring>
#include <deque>
#include <iostream>
#include <mutex>
#include <string>
#include <thread>

struct Description { int from; std::string sdp, type; };
struct Candidate { int from; std::string cand, mid; };
struct Hub {
    int caller = -1, receiver = -1, callerDc = -1, receiverDc = -1;
    std::mutex gate;
    std::deque<Description> descriptions;
    std::deque<Candidate> candidates;
    bool callerRemote = false, receiverRemote = false;
    std::atomic<bool> received{false}, echoed{false}, opened{false};
};
static void onDescription(int pc, const char *sdp, const char *type, void *ptr) {
    auto *hub = static_cast<Hub *>(ptr);
    std::lock_guard lock(hub->gate);
    hub->descriptions.push_back({pc, sdp ? sdp : "", type ? type : ""});
}
static void onCandidate(int pc, const char *cand, const char *mid, void *ptr) {
    if (!cand || !mid) return;
    auto *hub = static_cast<Hub *>(ptr);
    std::lock_guard lock(hub->gate);
    hub->candidates.push_back({pc, cand, mid});
}
static void onReceiverMessage(int, const char *data, int size, void *ptr) {
    auto *hub = static_cast<Hub *>(ptr);
    if (size == 7 && std::memcmp(data, "skyroof", 7) == 0) {
        hub->received = true;
        rtcSendMessage(hub->receiverDc, data, size);
    }
}
static void onCallerMessage(int, const char *data, int size, void *ptr) {
    auto *hub = static_cast<Hub *>(ptr);
    if (size == 7 && std::memcmp(data, "skyroof", 7) == 0) hub->echoed = true;
}
static void onCallerOpen(int dc, void *ptr) {
    auto *hub = static_cast<Hub *>(ptr);
    hub->opened = true;
    rtcSendMessage(dc, "skyroof", 7);
}
static void onChannel(int, int dc, void *ptr) {
    auto *hub = static_cast<Hub *>(ptr);
    hub->receiverDc = dc;
    rtcSetUserPointer(dc, hub);
    rtcSetMessageCallback(dc, onReceiverMessage);
}
int main() {
    rtcConfiguration config{};
    config.enableIceTcp = false;
    Hub hub;
    hub.caller = rtcCreatePeerConnection(&config);
    hub.receiver = rtcCreatePeerConnection(&config);
    if (hub.caller < 0 || hub.receiver < 0) return 2;
    rtcSetUserPointer(hub.caller, &hub);
    rtcSetUserPointer(hub.receiver, &hub);
    rtcSetLocalDescriptionCallback(hub.caller, onDescription);
    rtcSetLocalDescriptionCallback(hub.receiver, onDescription);
    rtcSetLocalCandidateCallback(hub.caller, onCandidate);
    rtcSetLocalCandidateCallback(hub.receiver, onCandidate);
    rtcSetDataChannelCallback(hub.receiver, onChannel);
    hub.callerDc = rtcCreateDataChannel(hub.caller, "skyroof-raw-pcm");
    if (hub.callerDc < 0) return 3;
    rtcSetUserPointer(hub.callerDc, &hub);
    rtcSetMessageCallback(hub.callerDc, onCallerMessage);
    rtcSetOpenCallback(hub.callerDc, onCallerOpen);
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(30);
    while (!hub.echoed && std::chrono::steady_clock::now() < deadline) {
        std::deque<Description> descriptions;
        std::deque<Candidate> candidates;
        {
            std::lock_guard lock(hub.gate);
            descriptions.swap(hub.descriptions);
            candidates.swap(hub.candidates);
        }
        for (auto &desc : descriptions) {
            const int to = desc.from == hub.caller ? hub.receiver : hub.caller;
            if (rtcSetRemoteDescription(to, desc.sdp.c_str(), desc.type.c_str()) < 0)
                return 4;
            if (to == hub.caller) hub.callerRemote = true;
            else hub.receiverRemote = true;
        }
        std::deque<Candidate> delayed;
        for (auto &cand : candidates) {
            const int to = cand.from == hub.caller ? hub.receiver : hub.caller;
            if (!(to == hub.caller ? hub.callerRemote : hub.receiverRemote)) {
                delayed.push_back(cand);
                continue;
            }
            if (rtcAddRemoteCandidate(to, cand.cand.c_str(), cand.mid.c_str()) < 0)
                return 5;
        }
        if (!delayed.empty()) {
            std::lock_guard lock(hub.gate);
            hub.candidates.insert(hub.candidates.begin(), delayed.begin(), delayed.end());
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    bool success = hub.opened && hub.received && hub.echoed;
    rtcClosePeerConnection(hub.caller);
    rtcClosePeerConnection(hub.receiver);
    if (hub.callerDc >= 0) rtcDeleteDataChannel(hub.callerDc);
    if (hub.receiverDc >= 0) rtcDeleteDataChannel(hub.receiverDc);
    rtcDeletePeerConnection(hub.caller);
    rtcDeletePeerConnection(hub.receiver);
    rtcCleanup();
    if (!success) {
        std::cerr << "No ICE+SCTP DataChannel echo within 30 seconds" << std::endl;
        return 6;
    }
    std::cout << "PASS: libdatachannel SDP, ICE, DTLS/SCTP and two-way data" << std::endl;
    return 0;
}
