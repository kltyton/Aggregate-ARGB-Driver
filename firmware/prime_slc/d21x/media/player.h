#ifndef PRIME_SLC_PLAYER_H
#define PRIME_SLC_PLAYER_H

#include "storage.h"

enum storage_status media_play(uint8_t brightness, uint8_t sleep_with_display, uint8_t rotation);
int media_initialize(void);
void extension_default_start(void);
enum storage_status media_configure(uint8_t brightness, uint8_t sleep_with_display, uint8_t rotation);
enum storage_status media_stop(void);
enum storage_status media_select_track(uint32_t track, uint64_t phase_ms);
enum storage_status media_hold_default(void);
void media_release_default(void);
void media_process_events(void);
uint32_t extension_file_clock(void);
uint8_t media_is_playing(void);
uint32_t media_frame_count(void);
uint64_t media_frame_pts(void);
uint32_t media_crop_errors(void);
uint8_t media_hold_flags(void);
void media_request_host_mode(void);
enum storage_status media_process_host_request(void);
void media_notify(void);

#endif
