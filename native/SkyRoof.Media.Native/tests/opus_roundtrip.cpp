#include "skyroof_media_native.h"
#include <cmath>
#include <cstdint>
#include <iostream>
#include <vector>

int main() {
    constexpr int rate = 48000;
    constexpr int frames = 960;
    SrOpusEncoder *encoder = sr_opus_encoder_create(rate, 1, 96000);
    SrOpusDecoder *decoder = sr_opus_decoder_create(rate, 1);
    if (!encoder || !decoder) return 2;

    std::vector<int16_t> samples(frames);
    std::vector<int16_t> reconstructed(frames);
    std::vector<uint8_t> compressed(4000);
    long long total_energy = 0;
    for (int block = 0; block < 8; block++) {
        for (int i = 0; i < frames; ++i) {
            float phase = static_cast<float>((block * frames + i) * 2.0 * 3.141592653589793 * 1200.0 / rate);
            samples[i] = static_cast<int16_t>(std::sin(phase) * 10000);
        }
        int encoded = sr_opus_encoder_encode(encoder, samples.data(), frames,
                                             compressed.data(), static_cast<int>(compressed.size()));
        if (encoded <= 0) return 3;
        int decoded = sr_opus_decoder_decode(decoder, compressed.data(), encoded,
                                             reconstructed.data(), frames);
        if (decoded != frames) return 4;
        for (int16_t sample : reconstructed)
            total_energy += static_cast<long long>(sample) * sample;
    }
    sr_opus_encoder_destroy(encoder);
    sr_opus_decoder_destroy(decoder);
    if (total_energy < 1000000) return 5;
    std::cout << "PASS: eight 48kHz mono 20ms Opus encode/decode frames, persistent C ABI state" << std::endl;
    return 0;
}
