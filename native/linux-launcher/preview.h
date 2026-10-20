#pragma once
#include <glib.h>
typedef struct { char *description, *image, *notice; } Preview;
Preview preview_read(const char *path, gboolean train);
void preview_clear(Preview *p);
