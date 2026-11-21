#include "skyroof_media_native.h"
#include <new>
#include <opus/opus.h>

struct SrOpusEncoder { OpusEncoder *state; };
struct SrOpusDecoder { OpusDecoder *state; };

extern "C" SrOpusEncoder *sr_opus_encoder_create(
    int sample_rate, int channels, int bitrate) {
    int err = OPUS_OK;
    OpusEncoder *encoder = opus_encoder_create(
        sample_rate, channels, OPUS_APPLICATION_AUDIO, &err);
    if (err != OPUS_OK || !encoder) return nullptr;
    if (opus_encoder_ctl(encoder, OPUS_SET_BITRATE(bitrate)) != OPUS_OK) {
        opus_encoder_destroy(encoder);
        return nullptr;
    }
    auto *handle = new (std::nothrow) SrOpusEncoder{encoder};
    if (!handle) opus_encoder_destroy(encoder);
    return handle;
}

extern "C" int sr_opus_encoder_encode(SrOpusEncoder *encoder,
    const int16_t *samples, int frames, uint8_t *output, int output_capacity) {
    if (!encoder || !samples || !output) return OPUS_BAD_ARG;
    return opus_encode(encoder->state, samples, frames, output, output_capacity);
}

extern "C" void sr_opus_encoder_destroy(SrOpusEncoder *encoder) {
    if (!encoder) return;
    opus_encoder_destroy(encoder->state);
    delete encoder;
}

extern "C" SrOpusDecoder *sr_opus_decoder_create(int sample_rate, int channels) {
    int err = OPUS_OK;
    OpusDecoder *decoder = opus_decoder_create(sample_rate, channels, &err);
    if (err != OPUS_OK || !decoder) return nullptr;
    auto *handle = new (std::nothrow) SrOpusDecoder{decoder};
    if (!handle) opus_decoder_destroy(decoder);
    return handle;
}

extern "C" int sr_opus_decoder_decode(SrOpusDecoder *decoder,
    const uint8_t *packet, int packet_size,
    int16_t *samples, int sample_capacity_frames) {
    if (!decoder || !packet || !samples) return OPUS_BAD_ARG;
    return opus_decode(decoder->state, packet, packet_size,
                       samples, sample_capacity_frames, 0);
}

extern "C" void sr_opus_decoder_destroy(SrOpusDecoder *decoder) {
    if (!decoder) return;
    opus_decoder_destroy(decoder->state);
    delete decoder;
}
