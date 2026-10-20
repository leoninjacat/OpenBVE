#include <string.h>
#include "content-path.h"
int main(int argc, char **argv)
{
    g_autoptr(GError) error = NULL;
    g_autofree char *ordinary = content_path_resolve("/tmp/route with spaces;$.csv", &error);
    g_assert_no_error(error);
    g_assert_cmpstr(ordinary, ==, "/tmp/route with spaces;$.csv");
    g_autofree char *invalid = g_build_filename(g_get_user_runtime_dir(), "doc", "invalid", NULL);
    g_autofree char *missing = content_path_resolve(invalid, &error);
    g_assert_null(missing);
    g_assert_error(error, G_IO_ERROR, G_IO_ERROR_INVALID_ARGUMENT);
    g_clear_error(&error);
    for (int i = 1; i + 1 < argc; i += 2) {
        g_autofree char *resolved = content_path_resolve(argv[i], &error);
        g_assert_no_error(error);
        g_assert_cmpstr(resolved, ==, argv[i + 1]);
    }
    g_print("Content paths passed (ordinary, invalid, %d portal cases).\n", (argc - 1) / 2);
    return 0;
}
