#include "rtc_voice_track.h"
#include <rtc/rtc.h>
#include <rtc/description.hpp>
#include <algorithm>
#include <array>
#include <cctype>
#include <random>
#include <stdexcept>

namespace {
struct OfferedAudio {
    std::string mid;
    int payloadType = 0;
};

OfferedAudio FindOpus(const char* sdp) {
    rtc::Description offer(sdp, rtc::Description::Type::Offer);
    for (int i = 0; i < offer.mediaCount(); ++i) {
        auto entry = offer.media(i);
        auto media = std::get_if<rtc::Description::Media*>(&entry);
        if (!media || (*media)->type() != "audio" || (*media)->isRemoved()) continue;
        auto direction = (*media)->direction();
        if (direction != rtc::Description::Direction::RecvOnly &&
            direction != rtc::Description::Direction::SendRecv) continue;
        for (int pt : (*media)->payloadTypes()) {
            const auto* map = (*media)->rtpMap(pt);
            if (!map || map->clockRate != 48000) continue;
            std::string format = map->format;
            std::transform(format.begin(), format.end(), format.begin(),
                [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
            if (format == "opus") return {(*media)->mid(), pt};
        }
    }
    throw std::invalid_argument("Offer has no compatible Opus receive track");
}
}

VoiceTrack::VoiceTrack() {
    int error = 0;
    encoder_.reset(opus_encoder_create(48000, 1, OPUS_APPLICATION_AUDIO, &error));
    if (!encoder_ || error != OPUS_OK ||
        opus_encoder_ctl(encoder_.get(), OPUS_SET_BITRATE(48000)) != OPUS_OK)
        throw std::runtime_error("Opus encoder unavailable");
}

VoiceTrack::~VoiceTrack() {
    if (track_ >= 0) rtcDeleteTrack(track_);
}

bool VoiceTrack::Configure(int pc, const char* offer) {
    if (track_ >= 0) return false;
    const auto audio = FindOpus(offer);
    std::random_device random;
    uint32_t ssrc = static_cast<uint32_t>(random());
    timestamp_ = static_cast<uint32_t>(random());
    rtcTrackInit track{};
    track.direction = RTC_DIRECTION_SENDONLY;
    track.codec = RTC_CODEC_OPUS;
    track.payloadType = audio.payloadType;
    track.ssrc = ssrc;
    track.mid = audio.mid.c_str();
    track.name = "SkyRoof";
    track.msid = "skyroof-voice";
    track.trackId = "skyroof-rx";
    track_ = rtcAddTrackEx(pc, &track);
    if (track_ < 0) return false;

    rtcPacketizerInit packetizer{};
    packetizer.ssrc = ssrc;
    packetizer.cname = "skyroof";
    packetizer.payloadType = static_cast<uint8_t>(audio.payloadType);
    packetizer.clockRate = 48000;
    packetizer.sequenceNumber = static_cast<uint16_t>(random());
    packetizer.timestamp = timestamp_;
    return rtcSetOpusPacketizer(track_, &packetizer) >= 0 &&
        rtcChainRtcpSrReporter(track_) >= 0;
}

int VoiceTrack::Send(const int16_t* pcm, int samples) {
    if (track_ < 0 || !rtcIsOpen(track_)) return 0;
    std::array<unsigned char, 4000> packet{};
    int count = opus_encode(encoder_.get(), pcm, samples, packet.data(), static_cast<opus_int32>(packet.size()));
    if (count <= 0 || rtcSetTrackRtpTimestamp(track_, timestamp_) < 0) return -1;
    timestamp_ += static_cast<uint32_t>(samples);
    return rtcSendMessage(track_, reinterpret_cast<const char*>(packet.data()), count) < 0 ? -1 : 1;
}
