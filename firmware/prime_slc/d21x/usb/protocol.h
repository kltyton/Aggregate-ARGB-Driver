#ifndef PRIME_SLC_PROTOCOL_H
#define PRIME_SLC_PROTOCOL_H

#include <stdint.h>

enum extension_command {
    EXT_CAPABILITIES = 0x70,
    EXT_UPLOAD_BEGIN = 0x71,
    EXT_UPLOAD_DATA = 0x72,
    EXT_UPLOAD_COMMIT = 0x73,
    EXT_UPLOAD_ABORT = 0x74,
    EXT_PLAY = 0x75,
    EXT_STOP = 0x76,
    EXT_STATUS = 0x77,
    EXT_OVERLAY = 0x78,
    EXT_CLEAR_UPGRADE = 0x79,
    EXT_VERIFY_MEDIA = 0x7a,
    EXT_DISPLAY_SETTINGS = 0x7b,
    EXT_PLAYBACK_STATS = 0x7c,
    EXT_MEDIA_CAPACITY = 0x7d,
    EXT_SELECT_TRACK = 0x7e
};

#define EXT_PACKET_HEADER 32u
#define EXT_PACKET_CRC 4u
#define EXT_MAX_DATA 0x32000u

void extension_initialize(void);
void extension_usb(uint64_t endpoint, uint64_t raw_length);

#endif
