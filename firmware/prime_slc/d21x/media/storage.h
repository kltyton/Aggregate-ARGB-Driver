#ifndef PRIME_SLC_STORAGE_H
#define PRIME_SLC_STORAGE_H

#include <stddef.h>
#include <stdint.h>

enum storage_status {
    STORAGE_OK = 0,
    STORAGE_SEQUENCE = 1,
    STORAGE_SPACE = 2,
    STORAGE_IO = 3,
    STORAGE_CHECKSUM = 4,
    STORAGE_BUSY = 5
};

struct media_upload {
    intptr_t file;
    unsigned char *memory;
    uint32_t size;
    uint32_t expected_crc;
    uint32_t crc;
    uint32_t offset;
};

enum storage_status storage_begin(struct media_upload *upload, uint32_t size, uint32_t crc);
enum storage_status storage_append(struct media_upload *upload, uint32_t offset, const void *data, size_t count);
enum storage_status storage_commit(struct media_upload *upload);
enum storage_status storage_verify(uint32_t size, uint32_t expected_crc);
void storage_abort(struct media_upload *upload);
const char *storage_media_path(void);
uint8_t storage_last_stage(void);
int32_t storage_last_error(void);

#endif
