#define main launcher_application_main
#include "main.c"
#undef main
#include <glib/gstdio.h>

int main(int argc, char **argv)
{
    gtk_init();
    g_autoptr(AdwApplication) app = adw_application_new("org.openbve.NativeLauncher.Test", G_APPLICATION_NON_UNIQUE);
    g_assert_true(g_application_register(G_APPLICATION(app), NULL, NULL));
    activate(GTK_APPLICATION(app), NULL);
    GtkWindow *window = gtk_application_get_windows(GTK_APPLICATION(app))->data;
    Launcher *ui = g_object_get_data(G_OBJECT(window), "launcher");
    g_autofree char *dir = g_dir_make_tmp("openbve-start-XXXXXX", NULL);
    g_autofree char *engine = g_build_filename(dir, "fake-engine", NULL);
    g_autofree char *output = g_build_filename(dir, "arguments", NULL);
    g_autofree char *route = g_build_filename(dir, "route with spaces;$.csv", NULL);
    g_autofree char *train = g_build_filename(dir, "train with spaces;$.dir", NULL);
    g_assert_true(g_file_set_contents(route, "", 0, NULL));
    g_assert_cmpint(g_mkdir(train,0700), ==, 0);
    g_autofree char *quoted = g_shell_quote(output);
    g_autofree char *script = g_strdup_printf("#!/bin/sh\nprintf '%%s\\n' \"$@\" > %s\n", quoted);
    g_assert_true(g_file_set_contents(engine, script, -1, NULL));
    g_assert_cmpint(g_chmod(engine,0700), ==, 0);
    g_setenv("OPENBVE_NATIVE_ENGINE", engine, TRUE);
    GtkWidget *button = gtk_button_new();
    g_object_ref_sink(button);
    start_clicked(GTK_BUTTON(button), ui);
    g_assert_true(gtk_widget_get_sensitive(button));
    gtk_editable_set_text(GTK_EDITABLE(ui->route), route);
    gtk_editable_set_text(GTK_EDITABLE(ui->train), train);
    start_clicked(GTK_BUTTON(button), ui);
    g_assert_false(gtk_widget_get_sensitive(button));
    gint64 deadline = g_get_monotonic_time() + 5000000;
    while (!gtk_widget_get_sensitive(button) && g_get_monotonic_time() < deadline) {
        while (g_main_context_iteration(NULL, FALSE));
        g_usleep(1000);
    }
    g_assert_true(gtk_widget_get_sensitive(button));
    g_autofree char *actual = NULL;
    g_assert_true(g_file_get_contents(output, &actual, NULL, NULL));
    g_autofree char *expected = g_strdup_printf("/route=%s\n/train=%s\n", route, train);
    g_assert_cmpstr(actual, ==, expected);
    g_assert_cmpstr(gtk_label_get_text(GTK_LABEL(ui->status)), ==, "Viagem encerrada.");
    if (argc == 5) {
        gtk_editable_set_text(GTK_EDITABLE(ui->route), argv[1]);
        gtk_editable_set_text(GTK_EDITABLE(ui->train), argv[3]);
        start_clicked(GTK_BUTTON(button), ui);
        g_assert_false(gtk_widget_get_sensitive(button));
        deadline = g_get_monotonic_time() + 5000000;
        while (!gtk_widget_get_sensitive(button) && g_get_monotonic_time() < deadline) {
            while (g_main_context_iteration(NULL, FALSE));
            g_usleep(1000);
        }
        g_assert_true(gtk_widget_get_sensitive(button));
        g_clear_pointer(&actual, g_free);
        g_assert_true(g_file_get_contents(output, &actual, NULL, NULL));
        g_autofree char *portal_expected = g_strdup_printf("/route=%s\n/train=%s\n", argv[2], argv[4]);
        g_assert_cmpstr(actual, ==, portal_expected);
        g_print("Portal selection handoff passed (original route and train paths).\n");
    }
    g_assert_true(g_file_set_contents(engine, "#!/bin/sh\nexit 7\n", -1, NULL));
    start_clicked(GTK_BUTTON(button), ui);
    deadline = g_get_monotonic_time() + 5000000;
    while (!gtk_widget_get_sensitive(button) && g_get_monotonic_time() < deadline) {
        while (g_main_context_iteration(NULL, FALSE));
        g_usleep(1000);
    }
    g_assert_true(gtk_widget_get_sensitive(button));
    g_assert_nonnull(strstr(gtk_label_get_text(GTK_LABEL(ui->status)), "encerrou com erro"));
    gtk_window_destroy(window);
    g_object_unref(button);
    g_remove(engine); g_remove(output); g_remove(route); g_rmdir(train); g_rmdir(dir);
    g_print("Launcher process handoff passed (arguments, validation, success, failure).\n");
    return 0;
}
