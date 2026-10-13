#ifndef PRIME_SLC_VIDEO_STREAM_H
#define PRIME_SLC_VIDEO_STREAM_H
#include "storage.h"

int32_t extension_stream_open(const char *uri, void **out, int32_t flags);
uint8_t video_stream_stored(void);
uint8_t video_stream_active(void);
uint32_t video_stream_duration(void);
uint32_t video_stream_errors(void);
uint32_t video_stream_bytes(void);
int video_stream_crop(const void *frame, unsigned char copy[80]);

#endif
