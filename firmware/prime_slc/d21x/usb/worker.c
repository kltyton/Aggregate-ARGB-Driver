#include "protocol.h"
#include "../native/abi.h"
#include "../media/storage.h"
#include "../media/player.h"
#include "../media/frame_player.h"
#include "../media/video_stream.h"
#include "../display/overlay.h"
#include "../display/text.h"

extern void original_usb(uint64_t endpoint, uint64_t raw_length);
extern unsigned char __extension_end[];

static native_semaphore request_signal;
static unsigned char *request;
static volatile uint32_t request_size;
static volatile uint8_t ready;
static unsigned char reply[36];
static struct media_upload upload = { .file = -1 };
static uint32_t free_storage;
static uint32_t media_capacity;
static enum storage_status host_mode_error;
static const unsigned char *read_pixels;
static uint32_t read_size;

static uint32_t get32(const unsigned char *data)
{ return (uint32_t)data[0] << 24 | (uint32_t)data[1] << 16 | (uint32_t)data[2] << 8 | data[3]; }

static void put32(unsigned char *data, uint32_t value)
{
    data[0] = value >> 24;
    data[1] = value >> 16;
    data[2] = value >> 8;
    data[3] = value;
}

static void response(uint8_t command, enum storage_status status, uint32_t sequence)
{
    for (size_t i = 0; i < sizeof(reply); ++i) reply[i] = 0;
    reply[0] = 0xaa;
    reply[1] = 0x55;
    reply[2] = command == EXT_CAPABILITIES ? 0x6e : command;
    reply[3] = (uint8_t)status;
    reply[4] = 6;
    put32(reply + 5, sequence);
    put32(reply + 9, upload.offset);
    put32(reply + 13, upload.size);
    reply[17] = upload.file != -1;
    reply[18] = ready;
    reply[19] = media_is_playing();
    reply[31] = storage_last_stage();
    if (command == EXT_UPLOAD_COMMIT || command == EXT_STATUS)
        put32(reply + 24, (uint32_t)storage_last_error());
    if (command == EXT_UPLOAD_COMMIT) reply[30] = media_hold_flags();
    if (command == EXT_STATUS && status == STORAGE_OK && host_mode_error != STORAGE_OK)
        reply[3] = (uint8_t)host_mode_error;
    if (command == EXT_STATUS) put32(reply + 20, free_storage);
    if (command == EXT_OVERLAY && read_pixels) {
        put32(reply + 20, native_checksum(read_pixels, read_size));
        put32(reply + 24, read_size);
    }
    if (command == EXT_MEDIA_CAPACITY) put32(reply + 20, media_capacity);
    if (command == EXT_PLAYBACK_STATS) {
        put32(reply + 9, video_stream_errors());
        put32(reply + 13, video_stream_bytes());
        reply[18] = media_crop_errors() > 255 ? 255 : (uint8_t)media_crop_errors();
        uint64_t pts = media_frame_pts();
        put32(reply + 20, media_frame_count());
        put32(reply + 24, pts >> 32);
        put32(reply + 28, (uint32_t)pts);
    }
    if (command == EXT_CAPABILITIES) {
        reply[5] = 2; reply[6] = 0xd0;
        reply[7] = 2; reply[8] = 0xd0;
        reply[9] = 'H'; reply[10] = 'L'; reply[11] = '1';
        reply[12] = '0'; reply[13] = '4';
        put32(reply + 14, 0x40238cc8);
        put32(reply + 18, (uint32_t)(uintptr_t)__extension_end);
        put32(reply + 22, ready ? 63u : 0u);
        put32(reply + 26, EXT_MAX_DATA);
        reply[30] = 60;
    }
    put32(reply + 32, native_checksum(reply, 32));
    if (command == EXT_OVERLAY && read_pixels) {
        for (size_t i = 0; i < sizeof(reply); ++i) request[i] = reply[i];
        for (uint32_t i = 0; i < read_size; ++i) request[sizeof(reply) + i] = read_pixels[i];
        native_usb_write(request, sizeof(reply) + read_size);
    } else native_usb_write(reply, sizeof(reply));
}

static void worker(void *argument)
{
    (void)argument;
    for (;;) {
        if (native_semaphore_wait(request_signal) != 0) continue;
        host_mode_error = media_process_host_request();
        media_process_events();
        uint32_t count = request_size;
        if (!count) continue;
        uint8_t command = request[2];
        uint32_t sequence = get32(request + 4);
        enum storage_status result = STORAGE_SEQUENCE;
        read_pixels = NULL; read_size = 0;
        switch (command) {
        case EXT_MEDIA_CAPACITY:
            if (count == 36) {
                uint64_t free_bytes;
                result = native_free_space("/data/lvgl_data", &free_bytes) == 0 ? STORAGE_OK : STORAGE_IO;
                if (result == STORAGE_OK) {
                    int64_t existing = native_file_size(storage_media_path());
                    uint64_t available = free_bytes + (existing > 0 ? (uint64_t)existing : 0);
                    media_capacity = available > 65536 ? (uint32_t)(available - 65536) : 0;
                }
            }
            break;
        case EXT_SELECT_TRACK:
            if (count == 36) result = media_select_track(get32(request + 8),
                (uint64_t)get32(request + 16) << 32 | get32(request + 12));
            break;
        case EXT_PLAYBACK_STATS:
            if (count == 36) result = STORAGE_OK;
            break;
        case EXT_UPLOAD_BEGIN:
            if (count == 36) result = storage_begin(&upload, get32(request + 8), get32(request + 12));
            break;
        case EXT_UPLOAD_DATA:
            if (count > 36) result = storage_append(&upload, get32(request + 8), request + 32, count - 36);
            break;
        case EXT_UPLOAD_COMMIT:
            if (count == 36) result = storage_commit(&upload);
            break;
        case EXT_UPLOAD_ABORT:
            if (count == 36) { storage_abort(&upload); result = STORAGE_OK; }
            break;
        case EXT_STATUS:
            if (count == 36) {
                uint64_t free_bytes;
                result = native_free_space("/data/lvgl_data", &free_bytes) == 0 ? STORAGE_OK : STORAGE_IO;
                if (result == STORAGE_OK) free_storage = (uint32_t)free_bytes;
            }
            break;
        case EXT_CLEAR_UPGRADE:
            if (count == 36) {
                if (upload.file != -1 || *(volatile uint8_t *)0x4022e838)
                    result = STORAGE_BUSY;
                else result = native_unlink("/data/lvgl_data/ota.cpio") == 0 ? STORAGE_OK : STORAGE_IO;
            }
            break;
        case EXT_PLAY:
            if (count == 36) {
                result = overlay_disable();
                if (result == STORAGE_OK) {
                    if (request[11] == 1) {
                        result = media_hold_default();
                        if (result == STORAGE_OK) result = media_configure(request[8], request[9], request[10]);
                    } else if (!request[11]) result = media_play(request[8], request[9], request[10]);
                    else result = STORAGE_SEQUENCE;
                }
            }
            break;
        case EXT_VERIFY_MEDIA:
            if (count == 36) result = upload.file == -1 ? storage_verify(get32(request + 8), get32(request + 12)) : STORAGE_BUSY;
            break;
        case EXT_DISPLAY_SETTINGS:
            if (count == 36) result = media_configure(request[8], request[9], request[10]);
            break;
        case EXT_STOP:
            if (count == 36) {
                result = overlay_disable();
                if (result == STORAGE_OK) {
                    result = media_stop();
                    if (result == STORAGE_OK) media_release_default();
                }
            }
            break;
        case EXT_OVERLAY:
            switch (request[8]) {
            case 0: if (count == 36) result = overlay_enable(); break;
            case 1: if (count == 36) result = overlay_disable(); break;
            case 2: if (count == 36) { overlay_clear_slots(); result = STORAGE_OK; } break;
            case 3:
                if (get32(request + 12) < 256 && get32(request + 16) <= 720 && get32(request + 24) <= 720)
                    result = overlay_store((uint16_t)get32(request + 12),
                        (uint16_t)get32(request + 16), (uint16_t)get32(request + 24), request + 32, count - 36);
                break;
            case 4: result = overlay_render(request + 32, count - 36); break;
            case 5: if (count == 36) result = ui_begin(get32(request + 12), get32(request + 16)); break;
            case 6: result = ui_append(get32(request + 12), request + 32, count - 36); break;
            case 7: if (count == 36) result = ui_commit(); break;
            case 8:
                result = ui_values(request + 32, count - 36);
                if (result == STORAGE_OK) result = overlay_render_ui();
                break;
            case 9: if (count == 36) { ui_clear(); result = STORAGE_OK; } break;
            case 10:
                if (count == 36 && get32(request + 16) > 0 && get32(request + 16) <= EXT_MAX_DATA) {
                    read_size = get32(request + 16);
                    read_pixels = overlay_read(get32(request + 12), read_size);
                    result = read_pixels ? STORAGE_OK : STORAGE_SEQUENCE;
                }
                break;
            default: break;
            }
            break;
        default:
            break;
        }
        request_size = 0;
        __asm__ volatile("fence rw, rw" ::: "memory");
        response(command, result, sequence);
        read_pixels = NULL; read_size = 0;
        native_usb_receive((void *)0x40102800);
    }
}

void media_notify(void)
{ if (request_signal) native_semaphore_signal(request_signal); }

void extension_initialize(void)
{
    if (request_signal) return;
    request = native_allocate(EXT_PACKET_HEADER + EXT_MAX_DATA + EXT_PACKET_CRC);
    if (!request) return;
    request_signal = native_semaphore_create("hl_usb");
    if (!request_signal) { native_free(request); request = NULL; return; }
    if (overlay_initialize() != 0 || media_initialize() != 0 || frame_player_initialize() != 0) return;
    native_thread thread = native_thread_create("hl_media", worker, NULL, 4096, 24, 10);
    if (thread && native_thread_start(thread) == 0) ready = 1;
}

void extension_usb(uint64_t endpoint, uint64_t raw_length)
{
    unsigned char *input = (unsigned char *)0x40102800;
    /* The controller reports its unmasked request minus a 19-bit remainder. */
    uint32_t received = (uint32_t)raw_length & 0x7ffff;
    if (received < 3 || input[0] != 0xaa || input[1] != 0x55 ||
        input[2] < EXT_CAPABILITIES || input[2] > EXT_SELECT_TRACK) {
        if (received >= 2 && input[0] == 0x5a && input[1] == 0xa5) media_request_host_mode();
        original_usb(endpoint, raw_length);
        return;
    }
    uint8_t command = input[2];
    uint32_t sequence = received >= 8 ? get32(input + 4) : 0;
    uint32_t count = received >= 24 ? get32(input + 20) + 36u : 0;
    if (received < 36 || count < 36 || count > EXT_PACKET_HEADER + EXT_MAX_DATA + EXT_PACKET_CRC ||
        input[3] > 1 || received != count + input[3] || (input[3] && input[count] != 0) ||
        native_checksum(input, count - 4) != get32(input + count - 4)) {
        response(command, STORAGE_CHECKSUM, sequence);
        native_usb_receive(input);
        return;
    }
    if (command == EXT_CAPABILITIES) {
        response(command, count == 36 ? STORAGE_OK : STORAGE_SEQUENCE, sequence);
        native_usb_receive(input);
        return;
    }
    if (!ready || request_size) {
        response(command, STORAGE_BUSY, sequence);
        native_usb_receive(input);
        return;
    }
    for (uint32_t i = 0; i < count; ++i) request[i] = input[i];
    __asm__ volatile("fence rw, rw" ::: "memory");
    request_size = count;
    native_semaphore_signal(request_signal);
}
