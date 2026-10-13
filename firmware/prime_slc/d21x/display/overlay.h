#ifndef PRIME_SLC_OVERLAY_H
#define PRIME_SLC_OVERLAY_H

#include "../media/storage.h"

int overlay_initialize(void);
enum storage_status overlay_enable(void);
enum storage_status overlay_disable(void);
enum storage_status overlay_store(uint16_t slot, uint16_t width, uint16_t height, const void *pixels, size_t bytes);
enum storage_status overlay_render(const unsigned char *instances, size_t bytes);
enum storage_status overlay_render_ui(void);
const unsigned char *overlay_read(uint32_t offset, uint32_t bytes);
void overlay_clear_slots(void);
void extension_flush(void *display, const void *area, const void *pixels);

#endif
