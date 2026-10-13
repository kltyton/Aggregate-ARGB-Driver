#include "../native/compression.h"
#include "frame_player.h"
#include "player.h"
#include "../native/abi.h"
#include "../display/layers.h"
#include "../../third_party/miniz/miniz_tinfl.h"

#define WIDTH 720u
#define HEIGHT 720u
#define PIXEL_BYTES (WIDTH * HEIGHT * 4u)
#define KEY_FRAME 0x80000000u
#define RAW_FRAME 0x40000000u
#define DATA_SIZE 0x3fffffffu

struct frame_index { uint32_t time, offset, size_flags, checksum; };
struct track_info { uint32_t duration, count, index_offset, reserved; };
struct animation_header {
    uint32_t magic, version;
    uint16_t width, height;
    uint32_t fps, tracks, track_table, reserved[2];
};
_Static_assert(sizeof(struct animation_header) == 32, "HLA1 header");
_Static_assert(sizeof(struct frame_index) == 16, "HLA1 frame index");
_Static_assert(sizeof(struct track_info) == 16, "HLA1 track index");

struct frame_session {
    intptr_t file;
    void *display;
    unsigned char *pixels[2];
    unsigned char *work;
    unsigned char *compressed;
    tinfl_decompressor inflater;
    struct track_info track;
    uint32_t next_index, next_time, epoch, phase, last_position;
    uint32_t fps;
    uint64_t file_length;
    uint64_t last_slot;
    uint8_t front, submitted, scan_pending;
};

static struct frame_session *session;
static native_semaphore wake;
static volatile uint8_t stop_requested, running;
static volatile uint32_t rendered;
static volatile uint64_t pts;

static uint32_t tick(void) { return ((uint32_t (*)(void))0x400128a0)(); }
static void delay(uint32_t ticks) { ((int (*)(uint32_t))0x400135e0)(ticks); }
static int control(struct frame_session *state, int command, void *argument)
{ return ((int (*)(void *, int, void *))0x400ba820)(state->display, command, argument); }
static unsigned char *allocate_pixels(void)
{ return ((void *(*)(size_t, size_t, unsigned, void *))0x40053310)(PIXEL_BYTES, 64, 1, (void *)0x40053470); }
static void free_pixels(void *pixels)
{ if (pixels) ((void (*)(void *, unsigned, void *))0x40053380)(pixels, 1, (void *)0x400534d0); }

static int read_exact(intptr_t file, void *data, size_t bytes)
{
    size_t offset = 0;
    while (offset < bytes) {
        intptr_t count = native_read(file, (unsigned char *)data + offset, bytes - offset);
        if (count <= 0 || (size_t)count > bytes - offset) return -1;
        offset += (size_t)count;
    }
    return 0;
}

static int index_at(struct frame_session *state, uint32_t index, struct frame_index *frame)
{
    if (index >= state->track.count || native_seek(state->file,
        (int64_t)state->track.index_offset + (int64_t)index * sizeof(*frame), 0) < 0) return -1;
    if (read_exact(state->file, frame, sizeof(*frame)) != 0 || frame->time >= state->track.duration ||
        !(frame->size_flags & DATA_SIZE) || (frame->size_flags & DATA_SIZE) > PIXEL_BYTES ||
        (uint64_t)frame->offset + (frame->size_flags & DATA_SIZE) > state->file_length) return -1;
    return 0;
}

static void clean_pixels(unsigned char *pixels)
{
    __asm__ volatile("fence; .word 0x0180000b" ::: "memory");
    for (size_t offset = 0; offset < PIXEL_BYTES; offset += 64) {
        register uintptr_t line __asm__("a5") = (uintptr_t)(pixels + offset);
        __asm__ volatile(".word 0x02b7800b" : : "r"(line) : "memory");
    }
    __asm__ volatile(".word 0x01b0000b; fence; .word 0x0180000b" ::: "memory");
}

static int submit(struct frame_session *state)
{
    struct layer layer = { .enable = 1, .layer_id = 0, .rect_id = 0,
        .type = 1, .width = WIDTH, .height = HEIGHT, .scale_width = WIDTH, .scale_height = HEIGHT, .format = 1 };
    uint8_t next = state->front ^ 1;
    layer.address[0] = (uint32_t)(uintptr_t)state->pixels[next];
    layer.stride[0] = WIDTH * 4;
    clean_pixels(state->pixels[next]);
    if (control(state, 0x4624, &layer) != 0) return -1;
    state->submitted = next;
    state->scan_pending = 1;
    if (control(state, 0x4620, NULL) != 0) return -1;
    state->front = next;
    state->scan_pending = 0;
    ++rendered;
    state->last_slot = (uint64_t)tick() * state->fps / 1000;
    return 0;
}

static int decode(struct frame_session *state, const struct frame_index *frame, const unsigned char *previous_pixels)
{
    if (state->scan_pending) {
        if (control(state, 0x4620, NULL) != 0) return -1;
        state->front = state->submitted;
        state->scan_pending = 0;
    }
    size_t bytes = frame->size_flags & DATA_SIZE;
    unsigned char *output = state->pixels[state->front ^ 1];
    if (native_seek(state->file, frame->offset, 0) < 0 || read_exact(state->file, state->compressed, bytes) != 0) return -1;
    if (frame->size_flags & RAW_FRAME) {
        if (bytes != PIXEL_BYTES) return -1;
        for (size_t index = 0; index < PIXEL_BYTES; ++index) output[index] = state->compressed[index];
    } else {
        tinfl_init(&state->inflater);
        size_t written = PIXEL_BYTES, consumed = bytes;
        if (tinfl_decompress(&state->inflater, state->compressed, &consumed, output, output, &written,
            TINFL_FLAG_PARSE_ZLIB_HEADER | TINFL_FLAG_USING_NON_WRAPPING_OUTPUT_BUF) != TINFL_STATUS_DONE ||
            written != PIXEL_BYTES || consumed != bytes) return -1;
    }
    if (native_checksum(output, PIXEL_BYTES) != frame->checksum) return -1;
    if (!(frame->size_flags & KEY_FRAME)) {
        const uint64_t *previous = (const uint64_t *)previous_pixels;
        uint64_t *current = (uint64_t *)output;
        for (size_t index = 0; index < PIXEL_BYTES / sizeof(uint64_t); ++index) current[index] ^= previous[index];
    }
    return 0;
}

static int seek_phase(struct frame_session *state, uint32_t phase)
{
    uint32_t target = 0, key = 0;
    struct frame_index frame;
    for (uint32_t index = 0; index < state->track.count; ++index) {
        if (index_at(state, index, &frame) != 0) return -1;
        if (index == 0 && (frame.time != 0 || !(frame.size_flags & KEY_FRAME))) return -1;
        if (frame.time > phase) break;
        target = index;
        if (frame.size_flags & KEY_FRAME) key = index;
    }
    for (uint32_t index = key; index <= target; ++index) {
        if (index_at(state, index, &frame) != 0 || decode(state, &frame, state->work) != 0) return -1;
        if (index != target) {
            const uint64_t *decoded = (const uint64_t *)state->pixels[state->front ^ 1];
            uint64_t *work = (uint64_t *)state->work;
            for (size_t word = 0; word < PIXEL_BYTES / sizeof(uint64_t); ++word) work[word] = decoded[word];
        }
    }
    if (submit(state) != 0) return -1;
    pts = (uint64_t)frame.time * 1000;
    state->next_index = target + 1;
    state->next_time = state->track.duration;
    if (state->next_index < state->track.count) {
        if (index_at(state, state->next_index, &frame) != 0) return -1;
        if (frame.time <= pts / 1000) return -1;
        state->next_time = frame.time;
    }
    return 0;
}

static void frame_thread(void *argument)
{
    (void)argument;
    for (;;) {
        if (native_semaphore_wait(wake) != 0) continue;
        struct frame_session *state = session;
        while (state && !stop_requested) {
            if (state->track.count == 1) { delay(50); continue; }
            if ((uint64_t)tick() * state->fps / 1000 == state->last_slot) { delay(1); continue; }
            uint32_t position = ((uint64_t)(uint32_t)(tick() - state->epoch) + state->phase) % state->track.duration;
            if (position < state->last_position) {
                state->phase = 0;
                state->epoch = tick() - position;
                if (seek_phase(state, position) != 0) break;
            }
            if (position >= state->next_time) {
                const unsigned char *previous = state->pixels[state->front];
                struct frame_index frame;
                uint32_t presented = 0;
                int failed = 0;
                do {
                    if (index_at(state, state->next_index++, &frame) != 0 || decode(state, &frame, previous) != 0) { failed = 1; break; }
                    presented = frame.time;
                    state->next_time = state->track.duration;
                    if (state->next_index < state->track.count) {
                        if (index_at(state, state->next_index, &frame) != 0 || frame.time <= presented) { failed = 1; break; }
                        state->next_time = frame.time;
                    }
                    if (state->next_time <= position) {
                        const uint64_t *decoded = (const uint64_t *)state->pixels[state->front ^ 1];
                        uint64_t *work = (uint64_t *)state->work;
                        for (size_t word = 0; word < PIXEL_BYTES / sizeof(uint64_t); ++word) work[word] = decoded[word];
                        previous = state->work;
                    }
                } while (state->next_time <= position && !stop_requested);
                if (failed || stop_requested || submit(state) != 0) break;
                pts = (uint64_t)presented * 1000;
            }
            state->last_position = position;
            delay(1);
        }
        running = 0;
        __asm__ volatile("fence rw, rw" ::: "memory");
    }
}

int frame_player_initialize(void)
{
    wake = native_semaphore_create("hl_frames");
    if (!wake) return -1;
    native_thread thread = native_thread_create("hl_frames", frame_thread, NULL, 8192, 24, 10);
    return thread && native_thread_start(thread) == 0 ? 0 : -1;
}

int frame_player_stored(void)
{
    intptr_t file = native_open(storage_media_path(), 0);
    if (file < 0) return 0;
    unsigned char magic[4];
    int valid = read_exact(file, magic, sizeof(magic)) == 0 && magic[0] == 'H' && magic[1] == 'L' && magic[2] == 'A' && magic[3] == '1';
    if (native_close(file) != 0) return 0;
    return valid;
}

uint8_t frame_player_active(void) { return running; }
uint32_t frame_player_count(void) { return rendered; }
uint64_t frame_player_pts(void) { return pts; }

enum storage_status frame_player_stop(void)
{
    struct frame_session *state = session;
    if (!state) return STORAGE_OK;
    stop_requested = 1;
    uint32_t started = tick();
    while (running) {
        if (tick() - started >= 5000) return STORAGE_BUSY;
        delay(1);
    }
    struct layer layer = { .layer_id = 0, .rect_id = 0 };
    if (state->display && (control(state, 0x4624, &layer) != 0 || control(state, 0x4620, NULL) != 0)) return STORAGE_IO;
    if (state->file >= 0) {
        if (native_close(state->file) != 0) return STORAGE_IO;
        state->file = -1;
    }
    if (state->display) ((void (*)(void *))0x400ba800)(state->display);
    free_pixels(state->pixels[0]); free_pixels(state->pixels[1]); free_pixels(state->work);
    if (state->compressed) native_free(state->compressed);
    native_free(state);
    session = NULL;
    return STORAGE_OK;
}

enum storage_status frame_player_start(uint32_t track, uint64_t phase_ms)
{
    if (!wake) return STORAGE_BUSY;
    enum storage_status stopped = frame_player_stop();
    if (stopped != STORAGE_OK) return stopped;
    struct frame_session *state = native_allocate(sizeof(*state));
    if (!state) return STORAGE_SPACE;
    for (size_t index = 0; index < sizeof(*state); ++index) ((unsigned char *)state)[index] = 0;
    state->file = native_open(storage_media_path(), 0);
    session = state;
    struct animation_header header;
    if (state->file < 0 || read_exact(state->file, &header, sizeof(header)) != 0) goto invalid;
    if (header.magic != 0x31414c48 || header.version != 1 || header.width != WIDTH || header.height != HEIGHT ||
        !header.tracks || header.tracks > 1024 || track >= header.tracks || header.track_table != 32) goto invalid;
    state->fps = header.fps;
    if (!state->fps || state->fps > 60 || native_seek(state->file, 32 + (int64_t)track * 16, 0) < 0 ||
        read_exact(state->file, &state->track, sizeof(state->track)) != 0 || !state->track.duration || !state->track.count) goto invalid;
    int64_t length = native_seek(state->file, 0, 2);
    if (length < 0 || (uint64_t)state->track.index_offset + (uint64_t)state->track.count * 16 > (uint64_t)length) goto invalid;
    state->file_length = (uint64_t)length;
    state->pixels[0] = allocate_pixels(); state->pixels[1] = allocate_pixels();
    state->work = allocate_pixels();
    state->compressed = native_allocate(PIXEL_BYTES);
    state->display = ((void *(*)(void))0x400ba730)();
    if (!state->pixels[0] || !state->pixels[1] || !state->work || !state->compressed || !state->display) goto exhausted;
    rendered = 0; pts = 0; stop_requested = 0;
    state->phase = phase_ms % state->track.duration;
    state->last_position = state->phase;
    if (seek_phase(state, state->phase) != 0) goto invalid;
    state->epoch = tick();
    running = 1;
    __asm__ volatile("fence rw, rw" ::: "memory");
    native_semaphore_signal(wake);
    return STORAGE_OK;
exhausted:
    return frame_player_stop() == STORAGE_OK ? STORAGE_SPACE : STORAGE_IO;
invalid:
    return frame_player_stop() == STORAGE_OK ? STORAGE_CHECKSUM : STORAGE_IO;
}
