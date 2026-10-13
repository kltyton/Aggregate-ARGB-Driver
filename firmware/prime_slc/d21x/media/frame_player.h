#ifndef PRIME_SLC_FRAME_PLAYER_H
#define PRIME_SLC_FRAME_PLAYER_H
#include "storage.h"

int frame_player_initialize(void);
int frame_player_stored(void);
enum storage_status frame_player_start(uint32_t track, uint64_t phase_ms);
enum storage_status frame_player_stop(void);
uint8_t frame_player_active(void);
uint32_t frame_player_count(void);
uint64_t frame_player_pts(void);

#endif
