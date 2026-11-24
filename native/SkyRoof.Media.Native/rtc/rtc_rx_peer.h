#pragma once
#include <stdint.h>
#if defined(_WIN32) && defined(SKYROOF_RTC_BUILD)
#define SKYROOF_RTC_EXPORT __declspec(dllexport)
#elif defined(_WIN32)
#define SKYROOF_RTC_EXPORT __declspec(dllimport)
#else
#define SKYROOF_RTC_EXPORT __attribute__((visibility("default")))
#endif
extern "C" {
SKYROOF_RTC_EXPORT void* skyroof_rtc_create(const char* ice_server);
SKYROOF_RTC_EXPORT void* skyroof_rtc_create_voice(const char* ice_server);
SKYROOF_RTC_EXPORT int skyroof_rtc_send_voice(void* peer, const int16_t* pcm, int samples);
SKYROOF_RTC_EXPORT int skyroof_rtc_offer(void* peer, const char* sdp);
SKYROOF_RTC_EXPORT int skyroof_rtc_answer(void* peer, char* buffer, int capacity);
SKYROOF_RTC_EXPORT int skyroof_rtc_send(void* peer, const char* bytes, int size);
SKYROOF_RTC_EXPORT void skyroof_rtc_destroy(void* peer);
}
