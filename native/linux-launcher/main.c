#include <adwaita.h>
#include "preview.h"
#include "content-path.h"

typedef struct {
    GtkWidget *route;
    GtkWidget *train;
    GtkWidget *status;
    GtkWidget *game_mode;
    GtkWindow *window;
    guint generation[2];
    GtkWidget *description[2], *image[2], *notice[2];
} Launcher;

typedef struct {
    GtkWindow *window;
    char *path;
    gboolean train;
    guint generation;
} PreviewRequest;

static void request_free(gpointer data)
{
    PreviewRequest *r = data;
    g_object_unref(r->window);
    g_free(r->path);
    g_free(r);
}
static void result_free(gpointer data)
{
    preview_clear(data);
    g_free(data);
}
static void preview_worker(GTask *task, gpointer source, gpointer data, GCancellable *cancel)
{
    (void)source; (void)cancel;
    PreviewRequest *r = data;
    Preview *p = g_new0(Preview, 1);
    *p = preview_read(r->path, r->train);
    g_task_return_pointer(task, p, result_free);
}
static void preview_ready(GObject *source, GAsyncResult *result, gpointer data)
{
    (void)source; (void)data;
    GTask *task = G_TASK(result);
    PreviewRequest *r = g_task_get_task_data(task);
    Launcher *ui = g_object_get_data(G_OBJECT(r->window), "launcher");
    Preview *p = g_task_propagate_pointer(task, NULL);
    if (r->generation == ui->generation[r->train] && gtk_widget_get_visible(GTK_WIDGET(r->window))) {
        gtk_label_set_text(GTK_LABEL(ui->description[r->train]), p->description ? p->description : "Sem descrição.");
        g_autoptr(GError) error = NULL;
        g_autoptr(GdkTexture) texture = p->image ? gdk_texture_new_from_filename(p->image, &error) : NULL;
        gtk_picture_set_paintable(GTK_PICTURE(ui->image[r->train]), texture ? GDK_PAINTABLE(texture) : NULL);
        g_autofree char *notice = g_strjoin("\n", p->notice ? p->notice : "",
            texture ? "" : (error ? "Imagem inválida ou formato não suportado." : "Sem imagem de prévia."), NULL);
        gtk_label_set_text(GTK_LABEL(ui->notice[r->train]), notice);
    }
    result_free(p);
}
static void update_preview(Launcher *ui, gboolean train)
{
    PreviewRequest *r = g_new0(PreviewRequest, 1);
    r->window = g_object_ref(ui->window);
    r->path = g_strdup(gtk_editable_get_text(GTK_EDITABLE(train ? ui->train : ui->route)));
    r->train = train;
    r->generation = ++ui->generation[train];
    gtk_label_set_text(GTK_LABEL(ui->description[train]), "Carregando prévia…");
    g_autoptr(GTask) task = g_task_new(NULL, NULL, preview_ready, NULL);
    g_task_set_task_data(task, r, request_free);
    g_task_run_in_thread(task, preview_worker);
}

static void entry_activate(GtkEntry *entry, gpointer data)
{
    Launcher *ui = data;
    update_preview(ui, GTK_WIDGET(entry) == ui->train);
}

static void entry_changed(GtkEditable *entry, gpointer data)
{
    Launcher *ui = data;
    gboolean train = GTK_WIDGET(entry) == ui->train;
    ui->generation[train]++;
    gtk_picture_set_paintable(GTK_PICTURE(ui->image[train]), NULL);
    gtk_label_set_text(GTK_LABEL(ui->description[train]), "Pressione Enter para atualizar a prévia.");
    gtk_label_set_text(GTK_LABEL(ui->notice[train]), "");
}

typedef struct { GtkWindow *window; gboolean train; } PickerRequest;

static void picker_response(GObject *source, GAsyncResult *result, gpointer data)
{
    PickerRequest *request = data;
    Launcher *ui = g_object_get_data(G_OBJECT(request->window), "launcher");
    g_autoptr(GError) error = NULL;
    g_autoptr(GFile) file = request->train
        ? gtk_file_dialog_select_folder_finish(GTK_FILE_DIALOG(source), result, &error)
        : gtk_file_dialog_open_finish(GTK_FILE_DIALOG(source), result, &error);
    if (!gtk_widget_get_visible(GTK_WIDGET(request->window))) {
        g_object_unref(request->window);
        g_free(request);
        return;
    }
    if (file) {
        g_autofree char *selected = g_file_get_path(file);
        g_autofree char *path = selected ? content_path_resolve(selected, &error) : NULL;
        if (path) {
            gtk_editable_set_text(GTK_EDITABLE(request->train ? ui->train : ui->route), path);
            update_preview(ui, request->train);
        } else gtk_label_set_text(GTK_LABEL(ui->status), error ? error->message : "Selecione um caminho local.");
    } else if (error && !g_error_matches(error, GTK_DIALOG_ERROR, GTK_DIALOG_ERROR_DISMISSED)
                        && !g_error_matches(error, GTK_DIALOG_ERROR, GTK_DIALOG_ERROR_CANCELLED)) {
        gtk_label_set_text(GTK_LABEL(ui->status), error->message);
    }
    g_object_unref(request->window);
    g_free(request);
}

static void browse_clicked(GtkButton *button, gpointer data)
{
    Launcher *ui = data;
    gboolean train = GPOINTER_TO_INT(g_object_get_data(G_OBJECT(button), "train"));
    g_autoptr(GtkFileDialog) dialog = gtk_file_dialog_new();
    gtk_file_dialog_set_title(dialog, train ? "Selecionar pasta do trem" : "Selecionar arquivo de rota");
    gtk_file_dialog_set_modal(dialog, TRUE);
    if (!train) {
        g_autoptr(GListStore) filters = g_list_store_new(GTK_TYPE_FILE_FILTER);
        g_autoptr(GtkFileFilter) filter = gtk_file_filter_new();
        gtk_file_filter_set_name(filter, "Rotas BVE (*.csv, *.rw)");
        gtk_file_filter_add_suffix(filter, "csv");
        gtk_file_filter_add_suffix(filter, "rw");
        g_list_store_append(filters, filter);
        g_autoptr(GtkFileFilter) all = gtk_file_filter_new();
        gtk_file_filter_set_name(all, "Todos os arquivos");
        gtk_file_filter_add_pattern(all, "*");
        g_list_store_append(filters, all);
        gtk_file_dialog_set_filters(dialog, G_LIST_MODEL(filters));
        gtk_file_dialog_set_default_filter(dialog, filter);
    }
    PickerRequest *request = g_new0(PickerRequest, 1);
    request->window = g_object_ref(ui->window);
    request->train = train;
    if (train) gtk_file_dialog_select_folder(dialog, ui->window, NULL, picker_response, request);
    else gtk_file_dialog_open(dialog, ui->window, NULL, picker_response, request);
}

typedef struct { GtkWindow *window; GtkWidget *button; } EngineRequest;

static void engine_finished(GObject *source, GAsyncResult *result, gpointer data)
{
    EngineRequest *request = data;
    g_autoptr(GError) error = NULL;
    gboolean ok = g_subprocess_wait_check_finish(G_SUBPROCESS(source), result, &error);
    Launcher *ui = g_object_get_data(G_OBJECT(request->window), "launcher");
    if (gtk_widget_get_visible(GTK_WIDGET(request->window))) {
        gtk_widget_set_sensitive(request->button, TRUE);
        gtk_label_set_text(GTK_LABEL(ui->status), ok ? "Viagem encerrada." :
            "O simulador encerrou com erro. Consulte OpenBVE-Native/Settings/log.txt na pasta de configurações do aplicativo.");
    }
    g_object_unref(request->window);
    g_free(request);
}

static void start_clicked(GtkButton *button, gpointer data)
{
    Launcher *ui = data;
    g_autoptr(GError) path_error = NULL;
    g_autofree char *route = content_path_resolve(gtk_editable_get_text(GTK_EDITABLE(ui->route)), &path_error);
    g_autofree char *train = route ? content_path_resolve(gtk_editable_get_text(GTK_EDITABLE(ui->train)), &path_error) : NULL;
    if (!route || !train) {
        gtk_label_set_text(GTK_LABEL(ui->status), path_error ? path_error->message : "Caminho de conteúdo inválido.");
        return;
    }
    if (!g_file_test(route, G_FILE_TEST_IS_REGULAR) || !g_file_test(train, G_FILE_TEST_IS_DIR)) {
        gtk_label_set_text(GTK_LABEL(ui->status), "Selecione um arquivo de rota e uma pasta de trem existentes.");
        return;
    }
    g_autofree char *executable = g_file_read_link("/proc/self/exe", NULL);
    g_autofree char *folder = executable ? g_path_get_dirname(executable) : g_get_current_dir();
    g_autofree char *candidate = g_build_filename(folder, "engine/OpenBve", NULL);
    g_autofree char *engine = g_canonicalize_filename(candidate, NULL);
    if (!g_file_test(engine, G_FILE_TEST_IS_EXECUTABLE)) {
        g_free(engine);
        g_free(candidate);
        candidate = g_build_filename(folder, "../../dotnet10/runtime/OpenBve", NULL);
        engine = g_canonicalize_filename(candidate, NULL);
    }
    if (!g_file_test(engine, G_FILE_TEST_IS_EXECUTABLE)) {
        g_free(engine);
        g_free(candidate);
        candidate = g_build_filename(folder, "../dotnet10/runtime/OpenBve", NULL);
        engine = g_canonicalize_filename(candidate, NULL);
    }
    const char *override = g_getenv("OPENBVE_NATIVE_ENGINE");
    if (override && *override) { g_free(engine); engine = g_canonicalize_filename(override, NULL); }
    if (!g_file_test(engine, G_FILE_TEST_IS_EXECUTABLE)) {
        gtk_label_set_text(GTK_LABEL(ui->status), "Motor nativo não encontrado. Execute native/dotnet10/build.sh primeiro.");
        return;
    }
    g_autofree char *route_path = g_canonicalize_filename(route, NULL);
    g_autofree char *train_path = g_canonicalize_filename(train, NULL);
    g_autofree char *route_arg = g_strconcat("/route=", route_path, NULL);
    g_autofree char *train_arg = g_strconcat("/train=", train_path, NULL);
    g_autofree char *working_dir = g_path_get_dirname(engine);
    g_autoptr(GSubprocessLauncher) launcher = g_subprocess_launcher_new(G_SUBPROCESS_FLAGS_NONE);
    g_subprocess_launcher_set_cwd(launcher, working_dir);
    g_autoptr(GError) error = NULL;
    g_autoptr(GSubprocess) process = g_subprocess_launcher_spawn(launcher, &error, engine, route_arg, train_arg, gtk_drop_down_get_selected(GTK_DROP_DOWN(ui->game_mode)) == 1 ? "/mode=cco" : "/mode=normal", NULL);
    if (!process) { gtk_label_set_text(GTK_LABEL(ui->status), error->message); return; }
    gtk_widget_set_sensitive(GTK_WIDGET(button), FALSE);
    gtk_label_set_text(GTK_LABEL(ui->status), "Simulador nativo em execução — carregando a viagem…");
    EngineRequest *request = g_new0(EngineRequest, 1);
    request->window = g_object_ref(ui->window);
    request->button = GTK_WIDGET(button);
    g_subprocess_wait_check_async(process, NULL, engine_finished, request);
}

static void activate(GtkApplication *app, gpointer data)
{
    (void)data;
    Launcher *ui = g_new0(Launcher, 1);

    GtkWidget *window = adw_application_window_new(app);
    ui->window = GTK_WINDOW(window);
    gtk_window_set_title(GTK_WINDOW(window), "OpenBVE");
    gtk_window_set_default_size(GTK_WINDOW(window), 860, 760);
    g_object_set_data_full(G_OBJECT(window), "launcher", ui, g_free);

    GtkWidget *toolbar = adw_toolbar_view_new();
    GtkWidget *header = adw_header_bar_new();
    adw_toolbar_view_add_top_bar(ADW_TOOLBAR_VIEW(toolbar), header);

    GtkWidget *content = gtk_box_new(GTK_ORIENTATION_VERTICAL, 20);
    gtk_widget_set_margin_top(content, 32);
    gtk_widget_set_margin_bottom(content, 32);
    gtk_widget_set_margin_start(content, 32);
    gtk_widget_set_margin_end(content, 32);

    GtkWidget *title = gtk_label_new("OpenBVE");
    gtk_widget_add_css_class(title, "title-1");
    gtk_box_append(GTK_BOX(content), title);

    GtkWidget *subtitle = gtk_label_new(
        "Simulador ferroviário — Linux Native");
    gtk_widget_add_css_class(subtitle, "dim-label");
    gtk_box_append(GTK_BOX(content), subtitle);

    AdwPreferencesGroup *group = ADW_PREFERENCES_GROUP(
        adw_preferences_group_new());
    adw_preferences_group_set_title(group, "Nova viagem");

    AdwActionRow *route_row = ADW_ACTION_ROW(adw_action_row_new());
    adw_preferences_row_set_title(ADW_PREFERENCES_ROW(route_row), "Rota");
    ui->route = gtk_entry_new();
    gtk_entry_set_placeholder_text(GTK_ENTRY(ui->route),
                                   "Caminho do arquivo da rota");
    gtk_widget_set_size_request(ui->route, 280, -1);
    adw_action_row_add_suffix(route_row, ui->route);
    adw_preferences_group_add(group, GTK_WIDGET(route_row));

    AdwActionRow *train_row = ADW_ACTION_ROW(adw_action_row_new());
    adw_preferences_row_set_title(ADW_PREFERENCES_ROW(train_row), "Trem");
    ui->train = gtk_entry_new();
    gtk_entry_set_placeholder_text(GTK_ENTRY(ui->train),
                                   "Caminho da pasta do trem");
    gtk_widget_set_size_request(ui->train, 280, -1);
    adw_action_row_add_suffix(train_row, ui->train);
    adw_preferences_group_add(group, GTK_WIDGET(train_row));

    gtk_box_append(GTK_BOX(content), GTK_WIDGET(group));
    for (int i = 0; i < 2; i++) {
        GtkWidget *button = gtk_button_new_with_label("Selecionar…");
        gtk_widget_set_valign(button, GTK_ALIGN_CENTER);
        g_object_set_data(G_OBJECT(button), "train", GINT_TO_POINTER(i));
        g_signal_connect(button, "clicked", G_CALLBACK(browse_clicked), ui);
        adw_action_row_add_suffix(i ? train_row : route_row, button);
        GtkWidget *entry = i ? ui->train : ui->route;
        g_signal_connect(entry, "activate", G_CALLBACK(entry_activate), ui);
        g_signal_connect(entry, "changed", G_CALLBACK(entry_changed), ui);
        GtkWidget *heading = gtk_label_new(i ? "Prévia do trem" : "Prévia da rota");
        gtk_widget_add_css_class(heading, "heading");
        gtk_box_append(GTK_BOX(content), heading);
        GtkWidget *row = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 16);
        ui->image[i] = gtk_picture_new();
        gtk_picture_set_content_fit(GTK_PICTURE(ui->image[i]), GTK_CONTENT_FIT_CONTAIN);
        gtk_widget_set_size_request(ui->image[i], 200, 140);
        gtk_box_append(GTK_BOX(row), ui->image[i]);
        ui->description[i] = gtk_label_new("Selecione para ver a descrição e a imagem.");
        gtk_label_set_wrap(GTK_LABEL(ui->description[i]), TRUE);
        gtk_label_set_selectable(GTK_LABEL(ui->description[i]), TRUE);
        gtk_label_set_xalign(GTK_LABEL(ui->description[i]), 0);
        gtk_widget_set_hexpand(ui->description[i], TRUE);
        gtk_box_append(GTK_BOX(row), ui->description[i]);
        gtk_box_append(GTK_BOX(content), row);
        ui->notice[i] = gtk_label_new("");
        gtk_label_set_wrap(GTK_LABEL(ui->notice[i]), TRUE);
        gtk_widget_add_css_class(ui->notice[i], "dim-label");
        gtk_box_append(GTK_BOX(content), ui->notice[i]);
    }

    AdwActionRow *mode_row = ADW_ACTION_ROW(adw_action_row_new());
    adw_preferences_row_set_title(ADW_PREFERENCES_ROW(mode_row), "Modo de jogo");
    const char *game_modes[] = { "Normal", "Modo CCO (Beta)", NULL };
    ui->game_mode = gtk_drop_down_new_from_strings(game_modes);
    adw_action_row_add_suffix(mode_row, ui->game_mode);
    adw_preferences_group_add(group, GTK_WIDGET(mode_row));
    GtkWidget *start = gtk_button_new_with_label("Iniciar viagem");
    gtk_widget_add_css_class(start, "suggested-action");
    gtk_widget_add_css_class(start, "pill");
    gtk_widget_set_halign(start, GTK_ALIGN_CENTER);
    g_signal_connect(start, "clicked",
                     G_CALLBACK(start_clicked), ui);
    gtk_box_append(GTK_BOX(content), start);

    ui->status = gtk_label_new("Motor .NET nativo — sem Mono");
    gtk_widget_add_css_class(ui->status, "dim-label");
    gtk_box_append(GTK_BOX(content), ui->status);

    GtkWidget *scroll = gtk_scrolled_window_new();
    gtk_scrolled_window_set_policy(GTK_SCROLLED_WINDOW(scroll), GTK_POLICY_NEVER, GTK_POLICY_AUTOMATIC);
    gtk_scrolled_window_set_child(GTK_SCROLLED_WINDOW(scroll), content);
    adw_toolbar_view_set_content(ADW_TOOLBAR_VIEW(toolbar), scroll);
    adw_application_window_set_content(
        ADW_APPLICATION_WINDOW(window), toolbar);

    gtk_window_present(GTK_WINDOW(window));
}

int main(int argc, char **argv)
{
    g_autoptr(AdwApplication) app = adw_application_new(
        "org.openbve.OpenBVE.Native",
        G_APPLICATION_DEFAULT_FLAGS);

    g_signal_connect(app, "activate", G_CALLBACK(activate), NULL);
    return g_application_run(G_APPLICATION(app), argc, argv);
}
