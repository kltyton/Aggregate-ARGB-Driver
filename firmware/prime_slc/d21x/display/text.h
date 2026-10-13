#ifndef PRIME_SLC_TEXT_H
#define PRIME_SLC_TEXT_H
#include "../media/storage.h"
void ui_clear(void);
enum storage_status ui_begin(uint32_t size, uint32_t checksum);
enum storage_status ui_append(uint32_t offset, const unsigned char *data, size_t count);
enum storage_status ui_commit(void);
enum storage_status ui_values(const unsigned char *data, size_t count);
enum storage_status ui_draw(unsigned char *pixels);
#endif
