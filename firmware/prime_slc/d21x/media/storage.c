#include "storage.h"

#include "../native/abi.h"
#include "player.h"

#define STORAGE_FS_PATH "/data/lvgl_data"
#define STORAGE_TEMP_PATH "/data/lvgl_data/hardwarelite.upload"
#define STORAGE_FINAL_PATH "/data/lvgl_data/logo.mp4"
#define STORAGE_OPEN_FLAGS 0x601u
#define STORAGE_FS_MARGIN ((uint64_t)64u * 1024u)
static uint8_t last_stage;
static int32_t last_error;
uint8_t storage_last_stage(void) { return last_stage; }
int32_t storage_last_error(void) { return last_error; }

static uint32_t storage_crc32(uint32_t crc, const unsigned char *data, size_t count)
{
    size_t index;

    for (index = 0; index < count; index++) {
        uint32_t bit;

        crc ^= data[index];
        for (bit = 0; bit < 8; bit++)
            crc = (crc & 1u) ? (crc >> 1) ^ 0xEDB88320u : (crc >> 1);
    }
    return crc;
}

static enum storage_status storage_verify_path(const char *path, uint32_t size, uint32_t expected_crc)
{
    intptr_t file = native_open(path, 0);
    last_stage = 3;
    if (file < 0) return STORAGE_IO;
    unsigned char buffer[256];
    uint32_t crc = 0;
    uint64_t total = 0;
    enum storage_status result = STORAGE_OK;
    for (;;) {
        intptr_t count = native_read(file, buffer, sizeof(buffer));
        last_stage = 4;
        if (count < 0 || count > (intptr_t)sizeof(buffer)) { result = STORAGE_IO; break; }
        if (!count) break;
        total += (size_t)count;
        crc = storage_crc32(crc, buffer, (size_t)count);
    }
    int closed = native_close(file);
    if (closed != 0 || result == STORAGE_OK) last_stage = 5;
    if (closed != 0) result = STORAGE_IO;
    if (result == STORAGE_OK && (total != size || crc != expected_crc)) result = STORAGE_CHECKSUM;
    return result;
}

enum storage_status storage_verify(uint32_t size, uint32_t expected_crc)
{ return storage_verify_path(STORAGE_FINAL_PATH, size, expected_crc); }

static void storage_reset(struct media_upload *upload)
{
    upload->file = -1;
    upload->memory = NULL;
    upload->size = 0;
    upload->expected_crc = 0;
    upload->crc = 0;
    upload->offset = 0;
}

static void storage_discard(struct media_upload *upload, intptr_t file)
{
    if (file >= 0)
        native_close(file);
    if (upload->memory) native_free(upload->memory);
    native_unlink(STORAGE_TEMP_PATH);
    storage_reset(upload);
}

void storage_abort(struct media_upload *upload)
{
    intptr_t file = upload->file;

    upload->file = -1;
    storage_discard(upload, file);
}

enum storage_status storage_begin(struct media_upload *upload, uint32_t size, uint32_t crc)
{
    last_stage = 0;
    last_error = 0;
    uint64_t free_bytes;
    intptr_t file;

    if (upload->file != -1)
        return STORAGE_BUSY;
    if (size == 0)
        return STORAGE_SEQUENCE;
    if (native_free_space(STORAGE_FS_PATH, &free_bytes) != 0)
        return STORAGE_IO;
    if (free_bytes < (uint64_t)size + STORAGE_FS_MARGIN) {
        int64_t existing = native_file_size(STORAGE_FINAL_PATH);
        if (existing < 0) return STORAGE_IO;
        if (free_bytes + (uint64_t)existing < (uint64_t)size + STORAGE_FS_MARGIN) return STORAGE_SPACE;
        upload->memory = native_allocate(size);
        if (!upload->memory) return STORAGE_SPACE;
        upload->file = -2;
        upload->size = size;
        upload->expected_crc = crc;
        upload->crc = 0;
        upload->offset = 0;
        return STORAGE_OK;
    }

    file = native_open(STORAGE_TEMP_PATH, STORAGE_OPEN_FLAGS);
    if (file < 0)
        return STORAGE_IO;

    upload->file = file;
    upload->size = size;
    upload->expected_crc = crc;
    upload->crc = 0;
    upload->offset = 0;
    return STORAGE_OK;
}

enum storage_status storage_append(struct media_upload *upload, uint32_t offset, const void *data, size_t count)
{
    const unsigned char *bytes = (const unsigned char *)data;
    size_t written = 0;

    if (upload->file == -1)
        return STORAGE_SEQUENCE;
    if (offset != upload->offset)
        return STORAGE_SEQUENCE;
    if (count == 0)
        return STORAGE_SEQUENCE;
    if ((uint64_t)offset + (uint64_t)count > (uint64_t)upload->size)
        return STORAGE_SEQUENCE;

    if (upload->memory) {
        for (size_t i = 0; i < count; ++i) upload->memory[offset + i] = bytes[i];
        upload->crc = storage_crc32(upload->crc, bytes, count);
        upload->offset += (uint32_t)count;
        return STORAGE_OK;
    }

    while (written < count) {
        intptr_t chunk = native_write(upload->file, bytes + written, count - written);

        if (chunk <= 0 || (size_t)chunk > count - written) {
            storage_abort(upload);
            return STORAGE_IO;
        }
        upload->crc = storage_crc32(upload->crc, bytes + written, (size_t)chunk);
        upload->offset += (uint32_t)chunk;
        written += (size_t)chunk;
    }
    return STORAGE_OK;
}

enum storage_status storage_commit(struct media_upload *upload)
{
    if (upload->file == -1)
        return STORAGE_SEQUENCE;
    if (upload->offset != upload->size) {
        storage_abort(upload);
        return STORAGE_SEQUENCE;
    }
    if (upload->crc != upload->expected_crc) {
        storage_abort(upload);
        return STORAGE_CHECKSUM;
    }
    if (upload->memory) {
        int frames = upload->size >= 32 && upload->memory[0] == 'H' && upload->memory[1] == 'L' &&
            upload->memory[2] == 'A' && upload->memory[3] == '1';
        int mp4 = upload->size >= 12 && upload->memory[4] == 'f' && upload->memory[5] == 't' &&
            upload->memory[6] == 'y' && upload->memory[7] == 'p';
        int video = upload->size >= 64 && upload->memory[0] == 'H' && upload->memory[1] == 'L' &&
            upload->memory[2] == 'V' && upload->memory[3] == '1';
        if (!frames && !mp4 && !video) {
            storage_abort(upload);
            return STORAGE_SEQUENCE;
        }
        enum storage_status held = media_hold_default();
        if (held != STORAGE_OK) return held;
        intptr_t file = native_open(STORAGE_FINAL_PATH, STORAGE_OPEN_FLAGS);
        if (file < 0) { media_release_default(); storage_abort(upload); return STORAGE_IO; }
        upload->file = file;
        size_t offset = 0;
        while (offset < upload->size) {
            intptr_t count = native_write(file, upload->memory + offset, upload->size - offset);
            if (count <= 0 || (size_t)count > upload->size - offset) {
                media_release_default(); storage_abort(upload); return STORAGE_IO;
            }
            offset += (size_t)count;
        }
        int synced = native_sync(file);
        int closed = native_close(file);
        enum storage_status verified = synced == 0 && closed == 0
            ? storage_verify(upload->size, upload->expected_crc) : STORAGE_IO;
        upload->file = -1;
        native_free(upload->memory);
        storage_reset(upload);
        media_release_default();
        return verified;
    }
    last_stage = 1;
    if (native_sync(upload->file) != 0) {
        storage_abort(upload);
        return STORAGE_IO;
    }
    last_stage = 2;
    if (native_close(upload->file) != 0) {
        storage_discard(upload, -1);
        return STORAGE_IO;
    }

    upload->file = -1;
    enum storage_status staged = storage_verify_path(STORAGE_TEMP_PATH, upload->size, upload->expected_crc);
    if (staged != STORAGE_OK) { storage_discard(upload, -1); return staged; }
    last_stage = 6;
    enum storage_status held = media_hold_default();
    if (held != STORAGE_OK) return held;
    /* FatFs refuses rename over an existing name; staged bytes are already verified. */
    last_stage = 7;
    if (native_file_size(STORAGE_FINAL_PATH) >= 0) {
        last_error = native_unlink(STORAGE_FINAL_PATH);
        if (last_error != 0) {
            media_release_default();
            return STORAGE_IO;
        }
    }
    last_stage = 8;
    if (native_rename(STORAGE_TEMP_PATH, STORAGE_FINAL_PATH) != 0) {
        media_release_default();
        storage_discard(upload, -1);
        return STORAGE_IO;
    }
    enum storage_status verified = storage_verify(upload->size, upload->expected_crc);
    storage_reset(upload);
    media_release_default();
    return verified;
}

const char *storage_media_path(void)
{
    return STORAGE_FINAL_PATH;
}
