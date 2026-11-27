#pragma once
#include <stdint.h>

#if defined(_WIN32) && defined(SKYROOF_MEDIA_BUILD)
#define SKYROOF_MEDIA_API __declspec(dllexport)
#elif defined(_WIN32)
#define SKYROOF_MEDIA_API __declspec(dllimport)
#else
#define SKYROOF_MEDIA_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct SrOpusEncoder SrOpusEncoder;
typedef struct SrOpusDecoder SrOpusDecoder;

/* Mono/dual-channel PCM16 at an Opus-supported sample rate.
 * Returns NULL on codec initialization failure, never throws across C ABI.
 * On successful encode/decode returns byte length or PCM frame count;
 * negative values are native Opus error codes. */
SKYROOF_MEDIA_API SrOpusEncoder *sr_opus_encoder_create(int sample_rate,
                                                        int channels, int bitrate);
SKYROOF_MEDIA_API int sr_opus_encoder_encode(SrOpusEncoder *encoder,
                                              const int16_t *samples,
                                              int frames,
                                              uint8_t *output, int output_capacity);
SKYROOF_MEDIA_API void sr_opus_encoder_destroy(SrOpusEncoder *encoder);

SKYROOF_MEDIA_API SrOpusDecoder *sr_opus_decoder_create(int sample_rate,
                                                        int channels);
SKYROOF_MEDIA_API int sr_opus_decoder_decode(SrOpusDecoder *decoder,
                                              const uint8_t *packet, int packet_size,
                                              int16_t *samples, int sample_capacity_frames);
SKYROOF_MEDIA_API void sr_opus_decoder_destroy(SrOpusDecoder *decoder);

#ifdef __cplusplus
}
#endif
