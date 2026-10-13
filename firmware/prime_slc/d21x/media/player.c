#include "player.h"
#include "frame_player.h"
#include "video_stream.h"
#include "../native/abi.h"
#include "../display/overlay.h"

static void *player;
static volatile uint8_t active;
static volatile uint8_t ended;
static native_semaphore default_gate;
static volatile uint8_t default_running;
static volatile uint8_t default_local;
static volatile uint8_t held;
static uint8_t hold_flags;
static volatile uint8_t host_requested;
static volatile uint32_t frame_count;
static volatile uint64_t frame_pts;
static uint32_t crop_errors;
uint8_t media_hold_flags(void) { return hold_flags; }
extern void original_default_start(void);

int media_initialize(void)
{
    default_gate = native_semaphore_create_count("hl_start", 1);
    return default_gate ? 0 : -1;
}

void extension_default_start(void)
{
    if (!default_gate) { original_default_start(); return; }
    if (native_semaphore_wait(default_gate) != 0) return;
    if (held || active) { native_semaphore_signal(default_gate); return; }
    default_running = 1;
    native_semaphore_signal(default_gate);
    if (frame_player_stored()) {
        if (native_semaphore_wait(default_gate) != 0) return;
        if (held || active) {
            default_running = 0;
            native_semaphore_signal(default_gate);
            return;
        }
        default_local = 1;
        native_semaphore_signal(default_gate);
        active = 1;
        extension_file_clock();
        enum storage_status result = frame_player_start(0, 0);
        if (result == STORAGE_OK) result = media_configure(*(volatile uint8_t *)0x401027c0,
            *(volatile uint8_t *)0x4022e83a, 0);
        if (result == STORAGE_OK) result = overlay_enable();
        if (result != STORAGE_OK) { frame_player_stop(); active = 0; }
    } else {
        if (video_stream_stored()) { active = 1; extension_file_clock(); }
        original_default_start();
    }
    if (native_semaphore_wait(default_gate) != 0) return;
    default_local = 0;
    default_running = 0;
    if (!held && !player && !frame_player_active() && !video_stream_active()) active = 0;
    native_semaphore_signal(default_gate);
}
extern int original_video_render(void *renderer, const void *frame);

int extension_video_render(void *renderer, const void *frame)
{
    unsigned char copy[80] __attribute__((aligned(8)));
    int cropped = video_stream_crop(frame, copy);
    if (cropped < 0) { ++crop_errors; return -1; }
    int result = original_video_render(renderer, cropped ? copy : frame);
    if (!result && active && (player || video_stream_active())) {
        frame_pts = *(const uint64_t *)((const unsigned char *)frame + 64);
        ++frame_count;
    }
    return result;
}

uint32_t media_frame_count(void) { return frame_player_active() ? frame_player_count() : frame_count; }
uint64_t media_frame_pts(void) { return frame_player_active() ? frame_player_pts() : frame_pts; }
uint32_t media_crop_errors(void) { return crop_errors; }

void media_request_host_mode(void)
{
    if (!active) return;
    host_requested = 1;
    media_notify();
}

enum storage_status media_process_host_request(void)
{
    if (!host_requested) return STORAGE_OK;
    host_requested = 0;
    enum storage_status result = overlay_disable();
    if (result == STORAGE_OK) result = media_hold_default();
    if (result == STORAGE_OK) media_release_default();
    return result;
}

static uint32_t native_tick(void)
{ return ((uint32_t (*)(void))0x400128a0)(); }

static int player_event(void *context, int event, int data1, int data2)
{
    (void)context; (void)data1; (void)data2;
    if (event == 0) { ended = 1; media_notify(); }
    return 0;
}

uint32_t extension_file_clock(void)
{
    uint32_t now = native_tick();
    if (active) {
        *(volatile uint32_t *)0x4022e840 = now;
        *(volatile uint8_t *)0x4022e83b = 0;
        *(volatile uint8_t *)0x4022e83c = 1;
    }
    return now;
}

uint8_t media_is_playing(void)
{ return active && (player != NULL || frame_player_active() || video_stream_active()); }

enum storage_status media_stop(void)
{
    enum storage_status frames = frame_player_stop();
    if (frames != STORAGE_OK) return frames;
    int result = 0;
    if (player) {
        result = ((int (*)(void *))0x400a2230)(player);
        if (result == 0) {
            result = ((int (*)(void *))0x400a2360)(player);
            player = NULL;
        }
    }
    if (result != 0) return STORAGE_IO;
    active = 0;
    ended = 0;
    return STORAGE_OK;
}

enum storage_status media_hold_default(void)
{
    if (!default_gate || native_semaphore_wait(default_gate) != 0) return STORAGE_BUSY;
    held = 1;
    native_semaphore_signal(default_gate);
    uint32_t initializing = ((uint32_t (*)(void))0x400128a0)();
    while (default_local) {
        if (((uint32_t (*)(void))0x400128a0)() - initializing >= 5000) { media_release_default(); return STORAGE_BUSY; }
        ((int (*)(uint32_t))0x400135e0)(50);
    }
    if (media_stop() != STORAGE_OK) { media_release_default(); return STORAGE_IO; }
    active = 1;
    extension_file_clock();
    uint8_t exit_sent = 0;
    uint32_t started = native_tick();
    for (;;) {
        uintptr_t context = *(volatile uintptr_t *)0x40238710;
        hold_flags = (default_running ? 1u : 0u) | (context ? 2u : 0u) |
            (native_file_is_open(storage_media_path()) ? 4u : 0u);
        if (!hold_flags) break;
        if (!exit_sent && context) {
            ((void (*)(void))0x4009b270)();
            exit_sent = 1;
        }
        if (native_tick() - started >= 5000) { media_release_default(); return STORAGE_BUSY; }
        ((int (*)(uint32_t))0x400135e0)(50);
    }
    return STORAGE_OK;
}

void media_release_default(void)
{
    if (!default_gate || native_semaphore_wait(default_gate) != 0) return;
    if (!player && !frame_player_active()) active = 0;
    held = 0;
    native_semaphore_signal(default_gate);
}

enum storage_status media_play(uint8_t brightness, uint8_t sleep_with_display, uint8_t rotation)
{
    if (brightness > 100 || sleep_with_display > 1 || rotation > 3) return STORAGE_SEQUENCE;
    intptr_t file = native_open(storage_media_path(), 0);
    if (file < 0) return STORAGE_IO;
    if (native_close(file) != 0) return STORAGE_IO;
    enum storage_status held = media_hold_default();
    if (held != STORAGE_OK) return held;
    if (frame_player_stored()) {
        enum storage_status result = frame_player_start(0, 0);
        if (result == STORAGE_OK) result = media_configure(brightness, sleep_with_display, 0);
        if (result != STORAGE_OK) media_stop();
        media_release_default();
        return result;
    }
    frame_count = 0;
    frame_pts = 0;
    crop_errors = 0;
    player = ((void *(*)(const char *))0x400a15c0)(NULL);
    if (!player) { media_release_default(); return STORAGE_SPACE; }
    if (((int (*)(void *, void *, int (*)(void *, int, int, int)))0x400a23b0)(player, NULL, player_event) != 0 ||
        ((int (*)(void *, const char *))0x400a14d0)(player, storage_media_path()) != 0 ||
        ((int (*)(void *))0x400a1750)(player) != 0) {
        media_stop();
        media_release_default();
        return STORAGE_IO;
    }
    int32_t rectangle[4] = { 0, 0, 720, 720 };
    if (((int (*)(void *, const int32_t *))0x400a2480)(player, rectangle) != 0 ||
        ((int (*)(void *, int))0x400a2580)(player, rotation) != 0 ||
        ((int (*)(void *))0x400a1a80)(player) != 0) {
        media_stop();
        media_release_default();
        return STORAGE_IO;
    }
    enum storage_status configured = media_configure(brightness, sleep_with_display, rotation);
    if (configured != STORAGE_OK) media_stop();
    media_release_default();
    return configured;
}

enum storage_status media_select_track(uint32_t track, uint64_t phase_ms)
{
    if (frame_player_stored()) return frame_player_start(track, phase_ms);
    if (track != 0 || !player || !video_stream_active() || !video_stream_duration()) return STORAGE_SEQUENCE;
    if (*((volatile uint8_t *)player + 0xf9)) return STORAGE_BUSY;
    uint64_t phase_us = phase_ms % video_stream_duration() * 1000u;
    return ((int (*)(void *, uint64_t))0x400a1e20)(player, phase_us) == 0 ? STORAGE_OK : STORAGE_IO;
}

enum storage_status media_configure(uint8_t brightness, uint8_t sleep_with_display, uint8_t rotation)
{
    if (brightness > 100 || sleep_with_display > 1 || rotation > 3) return STORAGE_SEQUENCE;
    if (player && ((int (*)(void *, int))0x400a2580)(player, rotation) != 0) return STORAGE_IO;
    if (((int (*)(uint32_t, uint32_t, uint32_t, uint32_t))0x4004e820)(3,
        (uint32_t)brightness * 10000u, 1000000, 1) != 0 ||
        ((int (*)(uint32_t))0x4004ebc0)(3) != 0) {
        return STORAGE_IO;
    }
    *(volatile uint8_t *)0x400fca71 = 1;
    if (*(volatile uint8_t *)0x401027c0 != brightness ||
        *(volatile uint8_t *)0x4022e83a != sleep_with_display) {
        *(volatile uint8_t *)0x401027c0 = brightness;
        *(volatile uint8_t *)0x4022e83a = sleep_with_display;
        *(volatile uint8_t *)0x4022e839 = 1;
    }
    return STORAGE_OK;
}

void media_process_events(void)
{
    if (ended && player && active) {
        ended = 0;
        if (((int (*)(void *, uint64_t))0x400a1e20)(player, 0) != 0) media_stop();
    }
}
