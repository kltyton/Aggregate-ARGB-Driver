#ifndef PRIME_SLC_LAYERS_H
#define PRIME_SLC_LAYERS_H
#include <stdint.h>

struct layer {
    uint32_t enable, layer_id, rect_id;
    int32_t scale_width, scale_height, x, y;
    uint32_t type, address[3], stride[3];
    int32_t width, height;
    uint32_t crop_enabled;
    int32_t crop_x, crop_y, crop_width, crop_height;
    uint32_t format, flags;
};
struct alpha { uint32_t layer_id, enable, mode, value; };
_Static_assert(sizeof(struct layer) == 92, "D21x display layer ABI");
_Static_assert(sizeof(struct alpha) == 16, "D21x display alpha ABI");
#endif
