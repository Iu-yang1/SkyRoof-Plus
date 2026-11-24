#include "rtc_rx_peer.h"
#include <rtc/rtc.h>
#include <atomic>
#include <cstring>
#include <memory>

namespace {
struct Peer {
    int pc = -1;
    std::atomic<int> dc{-1};
    std::atomic<bool> gathered{false};

    ~Peer() {
        if (pc >= 0) {
            // Delete the PC first, which drains its callbacks.
            rtcDeletePeerConnection(pc);
        }
        int channel = dc.load();
        if (channel >= 0) rtcDeleteDataChannel(channel);
    }
};

void gathering(int, rtcGatheringState state, void* user) {
    if (state == RTC_GATHERING_COMPLETE)
        static_cast<Peer*>(user)->gathered = true;
}

void discard(int, const char*, int, void*) {
    // This channel is RX-only. Do not buffer remote microphone data.
}

void channel(int, int dc, void* user) {
    auto* peer = static_cast<Peer*>(user);
    char label[64]{};
    if (rtcGetDataChannelLabel(dc, label, sizeof(label)) < 0 ||
        std::strcmp(label, "skyroof-rx-pcm") != 0) {
        rtcDeleteDataChannel(dc);
        return;
    }
    int expected = -1;
    if (!peer->dc.compare_exchange_strong(expected, dc)) {
        rtcDeleteDataChannel(dc);
        return;
    }
    rtcSetMessageCallback(dc, discard);
}
}

void* skyroof_rtc_create(const char* ice_server) {
    try {
        auto peer = std::make_unique<Peer>();
        rtcConfiguration config{};
        const char* servers[] = {ice_server};
        if (ice_server && *ice_server) {
            config.iceServers = servers;
            config.iceServersCount = 1;
        }
        peer->pc = rtcCreatePeerConnection(&config);
        if (peer->pc < 0) return nullptr;
        rtcSetUserPointer(peer->pc, peer.get());
        if (rtcSetGatheringStateChangeCallback(peer->pc, gathering) < 0 ||
            rtcSetDataChannelCallback(peer->pc, channel) < 0) return nullptr;
        return peer.release();
    } catch (...) { return nullptr; }
}

int skyroof_rtc_offer(void* handle, const char* sdp) {
    if (!handle || !sdp) return -1;
    return rtcSetRemoteDescription(static_cast<Peer*>(handle)->pc, sdp, "offer");
}

int skyroof_rtc_answer(void* handle, char* buffer, int capacity) {
    if (!handle) return -1;
    auto* peer = static_cast<Peer*>(handle);
    if (!peer->gathered) return 0;
    return rtcGetLocalDescription(peer->pc, buffer, capacity);
}

int skyroof_rtc_send(void* handle, const char* bytes, int size) {
    if (!handle || !bytes || size <= 0) return -1;
    int dc = static_cast<Peer*>(handle)->dc.load();
    if (dc < 0 || !rtcIsOpen(dc)) return 0;
    if (rtcGetBufferedAmount(dc) > 24000) return 0;
    return rtcSendMessage(dc, bytes, size) < 0 ? -1 : 1;
}

void skyroof_rtc_destroy(void* handle) { std::unique_ptr<Peer> peer(static_cast<Peer*>(handle)); }
