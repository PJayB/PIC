#pragma once

typedef struct prezr_bitmap_s {
    uint16_t width;
    uint16_t height;
    GBitmap* bitmap;
} prezr_bitmap_t;

typedef struct prezr_pack_s {
    struct prezr_pack_header_s* header;
    uint32_t numResources;
    const prezr_bitmap_t* resources;
} prezr_pack_t;

#define PREZR_OK 0
#define PREZR_RESOURCE_LOAD_FAIL -1
#define PREZR_VERSION_FAIL -2
#define PREZR_OUT_OF_MEMORY -3
#define PREZR_ZERO_SIZE_BLOB -4

#define PREZR_NO_CHECKSUM 0

int prezr_init(prezr_pack_t* pack, uint32_t h, uint32_t checksum);
void prezr_destroy(prezr_pack_t* pack);

