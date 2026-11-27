#pragma once
#include <stdint.h>
#include <memory>
#include <opus/opus.h>

class VoiceTrack {
public:
    VoiceTrack();
    ~VoiceTrack();
    bool Configure(int pc, const char* offer);
    int Send(const int16_t* pcm, int samples);

private:
    int track_ = -1;
    uint32_t timestamp_ = 0;
    std::unique_ptr<OpusEncoder, decltype(&opus_encoder_destroy)> encoder_{nullptr, opus_encoder_destroy};
};
