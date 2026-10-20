#include "preview.h"
#include <glib/gstdio.h>
#include <string.h>
static void write_file(const char *dir, const char *name, const char *text)
{
    g_autofree char *path = g_build_filename(dir, name, NULL);
    g_assert_true(g_file_set_contents(path, text, -1, NULL));
}
int main(int argc, char **argv)
{
    if (argc == 3) {
        Preview p = preview_read(argv[2], !strcmp(argv[1], "train"));
        g_print("Description: %s\nImage: %s\nNotice: %s\n",
                p.description ? p.description : "", p.image ? p.image : "", p.notice ? p.notice : "");
        preview_clear(&p);
        return 0;
    }
    g_autofree char *dir = g_dir_make_tmp("openbve-preview-XXXXXX", NULL);
    g_assert_nonnull(dir);
    write_file(dir, "Train.TXT", "Descrição do trem");
    write_file(dir, "train.dat", "");
    write_file(dir, "train.png", "invalid image");
    Preview p = preview_read(dir, TRUE);
    g_assert_cmpstr(p.description, ==, "Descrição do trem");
    g_assert_true(g_str_has_suffix(p.image, "train.png"));
    preview_clear(&p);
    write_file(dir, "train.xml", "<openBVE><Train><Description>Descrição XML</Description></Train></openBVE>");
    p = preview_read(dir, TRUE);
    g_assert_cmpstr(p.description, ==, "Descrição XML");
    preview_clear(&p);
    write_file(dir, "route.csv", "With Route\n.Comment Minha rota\n.Image TRAIN.PNG\nWith Train\n.Comment Ignorar\n");
    g_autofree char *route = g_build_filename(dir, "route.csv", NULL);
    p = preview_read(route, FALSE);
    g_assert_cmpstr(p.description, ==, "Minha rota");
    g_assert_true(g_str_has_suffix(p.image, "train.png"));
    preview_clear(&p);
    write_file(dir, "route.csv", "Route.Comment Rota direta\n");
    write_file(dir, "route.jpg", "image");
    p = preview_read(route, FALSE);
    g_assert_cmpstr(p.description, ==, "Rota direta");
    g_assert_true(g_str_has_suffix(p.image, "route.jpg"));
    preview_clear(&p);
    write_file(dir, "legacy.rw", "Descrição RW\nSegunda linha\n[Railway]\n");
    g_autofree char *rw = g_build_filename(dir, "legacy.rw", NULL);
    p = preview_read(rw, FALSE);
    g_assert_cmpstr(p.description, ==, "Descrição RW\nSegunda linha");
    preview_clear(&p);
    p = preview_read("/nonexistent/openbve-route", FALSE);
    g_assert_nonnull(p.notice);
    g_assert_null(p.image);
    preview_clear(&p);
    write_file(dir, "train.xml", "broken xml");
    write_file(dir, "Train.TXT", "Ol\xe1");
    p = preview_read(dir, TRUE);
    g_assert_cmpstr(p.description, ==, "Olá");
    g_assert_nonnull(p.notice);
    preview_clear(&p);
    GDir *files = g_dir_open(dir, 0, NULL);
    const char *name;
    while ((name = g_dir_read_name(files))) {
        g_autofree char *path = g_build_filename(dir, name, NULL);
        g_remove(path);
    }
    g_dir_close(files); g_rmdir(dir);
    g_print("Preview fixtures passed.\n");
    return 0;
}
