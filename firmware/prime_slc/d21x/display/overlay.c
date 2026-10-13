#include "overlay.h"
#include "layers.h"
#include "text.h"
#include "../native/abi.h"

#define WIDTH 720u
#define HEIGHT 720u
#define PIXEL_BYTES (WIDTH * HEIGHT * 4u)
#define MAX_SLOTS 256u

struct sprite { unsigned char *pixels; uint16_t width, height; };

extern void original_flush(void *display, const void *area, const void *pixels);
static native_semaphore display_gate;
static void *framebuffer;
static unsigned char *buffers[2];
static struct layer saved_layers[4];
static struct alpha saved_alpha;
static struct sprite sprites[MAX_SLOTS];
static uint8_t front;
static volatile uint8_t enabled;
static uint8_t restore_pending;
static uint8_t scan_pending;
static uint8_t submitted;

static int control(int command, void *argument)
{ return ((int (*)(void *, int, void *))0x400ba820)(framebuffer, command, argument); }

static void *allocate_pixels(void)
{ return ((void *(*)(size_t, size_t, unsigned, void *))0x40053310)(PIXEL_BYTES, 64, 1, (void *)0x40053470); }

static void free_pixels(void *pixels)
{ ((void (*)(void *, unsigned, void *))0x40053380)(pixels, 1, (void *)0x400534d0); }

static void clear_pixels(unsigned char *pixels)
{ for (size_t i = 0; i < PIXEL_BYTES; ++i) pixels[i] = 0; }

static void clean_pixels(unsigned char *pixels)
{
    __asm__ volatile("fence; .word 0x0180000b" ::: "memory");
    for (size_t i = 0; i < PIXEL_BYTES; i += 64) {
        register uintptr_t line __asm__("a5") = (uintptr_t)(pixels + i);
        __asm__ volatile(".word 0x02b7800b" : : "r"(line) : "memory");
    }
    __asm__ volatile(".word 0x01b0000b; fence; .word 0x0180000b" ::: "memory");
}

int overlay_initialize(void)
{
    display_gate = native_semaphore_create_count("hl_disp", 1);
    return display_gate ? 0 : -1;
}

void extension_flush(void *display, const void *area, const void *pixels)
{
    if (!display_gate) { original_flush(display, area, pixels); return; }
    if (native_semaphore_wait(display_gate) != 0) return;
    if (enabled) ((void (*)(void *))0x40082f60)(display);
    else original_flush(display, area, pixels);
    native_semaphore_signal(display_gate);
}

static int disable_rectangles(void)
{
    for (uint32_t i = 0; i < 4; ++i) {
        struct layer layer = saved_layers[i];
        layer.enable = 0;
        if (control(0x4624, &layer) != 0) return -1;
    }
    return 0;
}

static int restore_layers(void)
{
    int result = disable_rectangles();
    for (uint32_t i = 0; i < 4; ++i)
        if (control(0x4624, &saved_layers[i]) != 0) result = -1;
    if (control(0x4627, &saved_alpha) != 0) result = -1;
    if (control(0x4620, NULL) != 0) result = -1;
    return result;
}

static int show_buffer(uint8_t index)
{
    struct layer layer = { .enable = 1, .layer_id = 1, .rect_id = 0,
        .type = 1, .width = WIDTH, .height = HEIGHT, .format = 0 };
    layer.address[0] = (uint32_t)(uintptr_t)buffers[index];
    layer.stride[0] = WIDTH * 4;
    clean_pixels(buffers[index]);
    if (control(0x4624, &layer) != 0) return -1;
    submitted = index;
    scan_pending = 1;
    if (control(0x4620, NULL) != 0) return -1;
    front = submitted;
    scan_pending = 0;
    return 0;
}

enum storage_status overlay_enable(void)
{
    if (!display_gate) return STORAGE_BUSY;
    if (enabled) return restore_pending ? STORAGE_IO : STORAGE_OK;
    if (native_semaphore_wait(display_gate) != 0) return STORAGE_IO;
    enum storage_status result = STORAGE_IO;
    void *display = *(void **)0x40235af8;
    if (!display) goto done;
    unsigned char *userdata = ((void *(*)(void *))0x40083230)(display);
    if (!userdata) goto done;
    while (!*(volatile uint8_t *)(userdata + 0x49) && !*(volatile uint8_t *)(userdata + 0x48))
        ((void (*)(void *))0x4008ae60)(userdata + 0x40);
    framebuffer = ((void *(*)(void))0x400ba730)();
    if (!framebuffer) goto done;
    for (uint32_t i = 0; i < 4; ++i) {
        saved_layers[i].layer_id = 1;
        saved_layers[i].rect_id = i;
        if (control(0x4623, &saved_layers[i]) != 0) goto close;
    }
    saved_alpha.layer_id = 1;
    if (control(0x4626, &saved_alpha) != 0) goto close;
    buffers[0] = allocate_pixels();
    buffers[1] = allocate_pixels();
    if (!buffers[0] || !buffers[1]) { result = STORAGE_SPACE; goto release; }
    clear_pixels(buffers[0]);
    clear_pixels(buffers[1]);
    struct alpha alpha = { 1, 1, 0, 255 };
    enabled = 1;
    if (disable_rectangles() != 0 || control(0x4627, &alpha) != 0 || show_buffer(0) != 0) {
        if (restore_layers() != 0) { restore_pending = 1; goto done; }
        enabled = 0;
        scan_pending = 0;
        goto release;
    }
    front = 0;
    enabled = 1;
    result = STORAGE_OK;
    goto done;
release:
    if (buffers[0]) free_pixels(buffers[0]);
    if (buffers[1]) free_pixels(buffers[1]);
    buffers[0] = buffers[1] = NULL;
close:
    ((void (*)(void *))0x400ba800)(framebuffer);
    framebuffer = NULL;
done:
    native_semaphore_signal(display_gate);
    return result;
}

enum storage_status overlay_disable(void)
{
    if (!enabled) return STORAGE_OK;
    if (native_semaphore_wait(display_gate) != 0) return STORAGE_IO;
    enum storage_status result = STORAGE_IO;
    if (restore_layers() == 0) {
        enabled = 0;
        restore_pending = 0;
        scan_pending = 0;
        free_pixels(buffers[0]);
        free_pixels(buffers[1]);
        buffers[0] = buffers[1] = NULL;
        ((void (*)(void *))0x400ba800)(framebuffer);
        framebuffer = NULL;
        result = STORAGE_OK;
    } else restore_pending = 1;
    native_semaphore_signal(display_gate);
    return result;
}

void overlay_clear_slots(void)
{
    ui_clear();
    for (size_t i = 0; i < MAX_SLOTS; ++i) {
        if (sprites[i].pixels) native_free(sprites[i].pixels);
        sprites[i] = (struct sprite){ 0 };
    }
}

enum storage_status overlay_store(uint16_t slot, uint16_t width, uint16_t height, const void *pixels, size_t bytes)
{
    if (slot >= MAX_SLOTS || !width || !height || width > WIDTH || height > HEIGHT ||
        bytes != (size_t)width * height * 4) return STORAGE_SEQUENCE;
    unsigned char *copy = native_allocate(bytes);
    if (!copy) return STORAGE_SPACE;
    for (size_t i = 0; i < bytes; ++i) copy[i] = ((const unsigned char *)pixels)[i];
    if (sprites[slot].pixels) native_free(sprites[slot].pixels);
    sprites[slot] = (struct sprite){ copy, width, height };
    return STORAGE_OK;
}

static uint16_t get16(const unsigned char *data)
{ return (uint16_t)data[0] << 8 | data[1]; }

static void blend(unsigned char *target, const unsigned char *source)
{
    uint32_t alpha = source[3], old_alpha = target[3];
    if (!alpha) return;
    uint32_t combined = alpha * 255 + old_alpha * (255 - alpha);
    for (size_t i = 0; i < 3; ++i)
        target[i] = (source[i] * alpha * 255 + target[i] * old_alpha * (255 - alpha) + combined / 2) / combined;
    target[3] = (combined + 127) / 255;
}

enum storage_status overlay_render(const unsigned char *instances, size_t bytes)
{
    if (!enabled) return STORAGE_BUSY;
    if (restore_pending) return STORAGE_IO;
    if (scan_pending) {
        if (control(0x4620, NULL) != 0) return STORAGE_IO;
        front = submitted;
        scan_pending = 0;
    }
    if (bytes % 6 != 0 || bytes / 6 > 512) return STORAGE_SEQUENCE;
    for (size_t i = 0; i < bytes; i += 6) {
        uint16_t slot = get16(instances + i);
        if (slot >= MAX_SLOTS || !sprites[slot].pixels) return STORAGE_SEQUENCE;
    }
    unsigned char *target = buffers[front ^ 1];
    clear_pixels(target);
    for (size_t i = 0; i < bytes; i += 6) {
        struct sprite sprite = sprites[get16(instances + i)];
        int32_t x = (int16_t)get16(instances + i + 2), y = (int16_t)get16(instances + i + 4);
        for (int32_t row = 0; row < sprite.height; ++row) {
            if (y + row < 0 || y + row >= (int32_t)HEIGHT) continue;
            for (int32_t col = 0; col < sprite.width; ++col) {
                if (x + col < 0 || x + col >= (int32_t)WIDTH) continue;
                blend(target + ((y + row) * WIDTH + x + col) * 4,
                    sprite.pixels + (row * sprite.width + col) * 4);
            }
        }
    }
    if (show_buffer(front ^ 1) != 0) return STORAGE_IO;
    return STORAGE_OK;
}

enum storage_status overlay_render_ui(void)
{
    if (!enabled) return STORAGE_BUSY;
    if (restore_pending) return STORAGE_IO;
    if (scan_pending) {
        if (control(0x4620, NULL) != 0) return STORAGE_IO;
        front = submitted; scan_pending = 0;
    }
    enum storage_status result = ui_draw(buffers[front ^ 1]);
    if (result != STORAGE_OK) return result;
    return show_buffer(front ^ 1) == 0 ? STORAGE_OK : STORAGE_IO;
}

const unsigned char *overlay_read(uint32_t offset, uint32_t bytes)
{
    if (!enabled || restore_pending || scan_pending || offset > PIXEL_BYTES || bytes > PIXEL_BYTES - offset) return NULL;
    return buffers[front] + offset;
}
