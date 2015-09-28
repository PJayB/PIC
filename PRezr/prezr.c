#include <pebble.h>
#include "prezr.h"

typedef struct prezr_pack_header_s {
    uint32_t reserved;
    uint32_t numResources;
} prezr_pack_header_t;

void prezr_zero(prezr_pack_t* pack) {
    memset(pack, 0, sizeof(*pack));
}

int __prezr_init_pack(prezr_pack_t* pack, uint32_t rid, ResHandle h, size_t resource_size, void* blob) {
    if (resource_load(h, blob, resource_size) != resource_size) {
        APP_LOG(APP_LOG_LEVEL_DEBUG, "[PREZR] Failed to load resource %u", (size_t) rid);
        return PREZR_RESOURCE_LOAD_FAIL;
    }

    prezr_pack_header_t* pack_header = pack_header = (prezr_pack_header_t*) blob;

    // Fix up the header
    pack->header = pack_header;
    pack->numResources = pack_header->numResources;
    pack->resources = (prezr_bitmap_t*) (blob + sizeof(prezr_pack_header_t));

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

int prezr_placement_init(prezr_pack_t* pack, uint32_t rid, void* blob, size_t max_blob_size) {
    ResHandle h = resource_get_handle(rid);
    size_t blob_size = resource_size(h);
    if (blob_size == 0) {
        APP_LOG(APP_LOG_LEVEL_DEBUG, "[PREZR] zero size blob");
        return PREZR_ZERO_SIZE_BLOB;
    }
    if (blob_size > max_blob_size) {
        APP_LOG(APP_LOG_LEVEL_DEBUG, "[PREZR] container too small");
        return PREZR_CONTAINER_TOO_SMALL;
    }

    return __prezr_init_pack(pack, rid, h, blob_size, blob);
}

int prezr_init(prezr_pack_t* pack, uint32_t rid) {
    ResHandle h = resource_get_handle(rid);
    size_t blob_size = resource_size(h);
    uint8_t* blob = blob = (uint8_t*) malloc(blob_size);
    if (blob == NULL) {
        APP_LOG(APP_LOG_LEVEL_DEBUG, "[PREZR] OOM while trying to allocate %u bytes (%u available)", blob_size, heap_bytes_free());
        return PREZR_OUT_OF_MEMORY;
    }

    return __prezr_init_pack(pack, rid, h, blob_size, blob);
}

void __prezr_destroy_pack(prezr_pack_t* pack) {
    if (pack != NULL && pack->header != NULL) {
        for (uint32_t i = 0; i < pack->numResources; ++i) {
            if (pack->resources[i].bitmap != NULL) {
                gbitmap_destroy(pack->resources[i].bitmap);
                pack->resources[i].bitmap = NULL;
            }
        }
    }
}

void prezr_placement_destroy(prezr_pack_t* pack) {
    if (pack != NULL && pack->header != NULL) {
        __prezr_destroy_pack(pack);
        memset(pack, 0, sizeof(*pack));
    }
}

void prezr_destroy(prezr_pack_t* pack) {
    if (pack != NULL && pack->header != NULL) {
        __prezr_destroy_pack(pack);
        free(pack->header);
        memset(pack, 0, sizeof(*pack));
    }
}

