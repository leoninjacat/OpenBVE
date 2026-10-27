#include <adwaita.h>
#include "content-path.h"
static GMainLoop *loop;
static int exit_code = 1;
static gboolean output_path(GFile *file)
{
    g_autofree char *selected = g_file_get_path(file);
    g_autoptr(GError) error = NULL;
    g_autofree char *path = selected ? content_path_resolve(selected, &error) : NULL;
    if (!path) { g_printerr("%s\n", error ? error->message : "Selecione um arquivo local."); exit_code = 2; return FALSE; }
    g_print("%s", path);
    exit_code = 0;
    return TRUE;
}
static void picked(GObject *source, GAsyncResult *result, gpointer data)
{
    (void)data;
    g_autoptr(GError) error = NULL;
    g_autoptr(GFile) file = gtk_file_dialog_open_finish(GTK_FILE_DIALOG(source), result, &error);
    if (file) output_path(file);
    else if (error && !g_error_matches(error, GTK_DIALOG_ERROR, GTK_DIALOG_ERROR_DISMISSED)
                   && !g_error_matches(error, GTK_DIALOG_ERROR, GTK_DIALOG_ERROR_CANCELLED)) {
        g_printerr("%s\n", error->message); exit_code = 2;
    }
    g_main_loop_quit(loop);
}
int main(int argc, char **argv)
{
    if (argc == 3 && g_str_equal(argv[1], "--resolve")) {
        g_autoptr(GFile) file = g_file_new_for_commandline_arg(argv[2]);
        output_path(file);
        return exit_code;
    }
    gboolean object_editor=g_getenv("OPENBVE_OBJECT_EDITOR")!=NULL;
    adw_init();
    g_set_application_name("Route Viewer Native");
    g_autoptr(GtkFileDialog) dialog = gtk_file_dialog_new();
    gtk_file_dialog_set_title(dialog, object_editor?"Abrir objeto — OpenBVE Object Editor":"Abrir rota — Route Viewer Native");
    g_autoptr(GListStore) filters = g_list_store_new(GTK_TYPE_FILE_FILTER);
    g_autoptr(GtkFileFilter) route = gtk_file_filter_new();
    gtk_file_filter_set_name(route, object_editor?"Objetos BVE (*.b3d, *.csv)":"Rotas BVE (*.csv, *.rw)");
    gtk_file_filter_add_suffix(route, "csv"); gtk_file_filter_add_suffix(route, object_editor?"b3d":"rw");
    g_list_store_append(filters, route);
    g_autoptr(GtkFileFilter) all = gtk_file_filter_new();
    gtk_file_filter_set_name(all, "Todos os arquivos"); gtk_file_filter_add_pattern(all, "*");
    g_list_store_append(filters, all);
    gtk_file_dialog_set_filters(dialog, G_LIST_MODEL(filters));
    gtk_file_dialog_set_default_filter(dialog, route);
    loop = g_main_loop_new(NULL, FALSE);
    gtk_file_dialog_open(dialog, NULL, NULL, picked, NULL);
    g_main_loop_run(loop);
    g_main_loop_unref(loop);
    return exit_code;
}
