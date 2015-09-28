#include <pebble.h>
#include "prezr.h"

typedef struct prezr_pack_header_s {
    uint32_t reserved;
    uint32_t numResources;
} prezr_pack_header_t;

int prezr_init(prezr_pack_t* pack, uint32_t rid) {
    ResHandle h = resource_get_handle(rid);
    uint8_t* blob = NULL;
    prezr_pack_header_t* pack_header = NULL;

    memset(pack, 0, sizeof(*pack));

    size_t blob_size = resource_size(h);
    if (blob_size == 0) {
        APP_LOG(APP_LOG_LEVEL_DEBUG, "[PREZR] zero size blob");
        return PREZR_ZERO_SIZE_BLOB;
    }

    blob = (uint8_t*) malloc(blob_size);
    if (blob == NULL) {
        APP_LOG(APP_LOG_LEVEL_DEBUG, "[PREZR] OOM while trying to allocate %u bytes (%u available)", blob_size, heap_bytes_free());
        return PREZR_OUT_OF_MEMORY;
    }

    if (resource_load(h, blob, blob_size) != blob_size) {
        APP_LOG(APP_LOG_LEVEL_DEBUG, "[PREZR] Failed to load resource %u", (size_t) rid);
        return PREZR_RESOURCE_LOAD_FAIL;
    }

    pack_header = (prezr_pack_header_t*) blob;

    // Fix up the header
    pack->header = pack_header;
    pack->numResources = pack_header->numResources;
    pack->resources = (const prezr_bitmap_t*) (blob + sizeof(prezr_pack_header_t));

    // Fix up the pointers to the resources
    for (uint32_t i = 0; i < pack->numResources; ++i) {
        prezr_bitmap_t* res = (prezr_bitmap_t*) &pack->resources[i];
        size_t offset = (size_t) res->bitmap;
        const uint8_t* data = blob + offset;

        res->bitmap = gbitmap_create_with_data(data);
        if (res->bitmap == NULL) {
            APP_LOG(APP_LOG_LEVEL_DEBUG, "[PREZR] Failed to create image %u at offset %u", (size_t) i, offset);
            return (i + 1);
        }
    }

    return PREZR_OK;
}

void prezr_destroy(prezr_pack_t* pack) {
    if (pack != NULL && pack->header != NULL) {
        for (uint32_t i = 0; i < pack->numResources; ++i) {
            if (pack->resources[i].bitmap != NULL) {
                gbitmap_destroy(pack->resources[i].bitmap);
                pack->resources[i].bitmap = NULL;
            }
        }
        free(pack->header);
        memset(pack, 0, sizeof(*pack));
    }
}
