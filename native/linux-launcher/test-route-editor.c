#define main route_editor_main
#include "route-editor.c"
#undef main
static int phase;
static Display *test_display;
static Window test_parent;
static void check(gboolean pass,const char *message){if(!pass)g_error("Editor test: %s",message);}
static void geometry(int expected_mode) {
    place_viewer(NULL);XSync(display,FALSE);
    Window root,owner,*children=NULL;unsigned count;
    XQueryTree(test_display,test_parent,&root,&owner,&children,&count);if(children)XFree(children);
    check(owner==xid,"viewer is embedded in GTK host");
    graphene_rect_t bounds;check(gtk_widget_compute_bounds(viewer_area,GTK_WIDGET(host_window),&bounds),"viewer allocation");
    XWindowAttributes a;XGetWindowAttributes(test_display,test_parent,&a);
    check(a.x==(int)bounds.origin.x&&a.y==(int)bounds.origin.y&&a.width==(int)bounds.size.width&&a.height==(int)bounds.size.height,"viewer matches GTK pane allocation");
    check(window==(expected_mode==2?floating_window:host_window),"editor dock target");
    g_print("GEOMETRY: mode=%d viewer=%dx%d\n",expected_mode,a.width,a.height);
}
static GtkWidget *find_button(GtkWidget *widget,const char *label) {
    if(GTK_IS_BUTTON(widget)&&g_strcmp0(gtk_button_get_label(GTK_BUTTON(widget)),label)==0)return widget;
    for(GtkWidget *child=gtk_widget_get_first_child(widget);child;child=gtk_widget_get_next_sibling(child)) {
        GtkWidget *found=find_button(child,label);if(found)return found;
    }
    return NULL;
}
static void click_dialog(const char *label) {
    GListModel *tops=gtk_window_get_toplevels();gboolean found=FALSE;
    for(guint i=0;i<g_list_model_get_n_items(tops);i++) {
        g_autoptr(GtkWindow) top=g_list_model_get_item(tops,i);GtkWidget *button=find_button(GTK_WIDGET(top),label);
        if(button){g_signal_emit_by_name(button,"clicked");found=TRUE;break;}
    }
    check(found,"unsaved changes dialog button exists");
}
static void commands(const char *lines) {
    int fds[2];check(pipe(fds)==0,"test pipe");
    check(write(fds[1],lines,strlen(lines))==(ssize_t)strlen(lines),"test command write");
    GIOChannel *channel=g_io_channel_unix_new(fds[0]);input(channel,G_IO_IN,NULL);g_io_channel_unref(channel);close(fds[0]);close(fds[1]);
}
static void command(const char *verb,const char *value) {
    g_autofree char *encoded=g_base64_encode((const guchar *)value,strlen(value));
    g_autofree char *line=g_strdup_printf("%s\t%s\n",verb,encoded);commands(line);
}
static gboolean tick(gpointer data) {
    (void)data;
    switch(phase++) {
        case 0:geometry(0);
            GtkScrolledWindow *scroll=g_object_get_data(G_OBJECT(window),"scroll");
            check(!gtk_scrolled_window_get_overlay_scrolling(scroll),"scrollbars reserve their own space");
            GtkPolicyType horizontal,vertical;gtk_scrolled_window_get_policy(scroll,&horizontal,&vertical);
            check(horizontal==GTK_POLICY_ALWAYS&&vertical==GTK_POLICY_ALWAYS,"both scrollbars stay accessible");
            AdwStyleManager *style=adw_style_manager_get_default();
            adw_style_manager_set_color_scheme(style,ADW_COLOR_SCHEME_FORCE_DARK);check(adw_style_manager_get_dark(style),"dark theme");
            adw_style_manager_set_color_scheme(style,ADW_COLOR_SCHEME_FORCE_LIGHT);check(!adw_style_manager_get_dark(style),"light theme");
            adw_style_manager_set_color_scheme(style,ADW_COLOR_SCHEME_DEFAULT);
            // Both lines arrive in one pipe write while its writer stays open.
            commands("TEXT\tZmlyc3Q=\nTEXT\tc2Vjb25k\n");
            char *batched=current_text();check(g_str_equal(batched,"second"),"all buffered IPC lines processed without a later write");g_free(batched);
            command("TEXT","initial");gtk_text_buffer_insert_at_cursor(buffer,"edit",-1);gtk_drop_down_set_selected(g_object_get_data(G_OBJECT(window),"layout"),1);break;
        case 1:geometry(1);command("VISIBLE","0");check(!gtk_widget_get_visible(editor_content),"editor hides without disposing buffer");command("VISIBLE","1");check(gtk_text_buffer_get_can_undo(buffer),"dock preserves undo");gtk_drop_down_set_selected(g_object_get_data(G_OBJECT(window),"layout"),2);break;
        case 2:geometry(2);check(gtk_text_buffer_get_can_undo(buffer),"detach preserves undo");gtk_drop_down_set_selected(g_object_get_data(G_OBJECT(window),"layout"),0);break;
        case 3:
            geometry(0);command("TEXT","; Estação\r\n0, .Pitch 0\r\n");
            check(!gtk_text_buffer_get_modified(buffer),"load is clean");
            check(!gtk_text_buffer_get_can_undo(buffer),"opening another document clears previous undo history");
            gtk_widget_grab_focus(g_object_get_data(G_OBJECT(window),"text"));break;
        case 4: {
            // Exercise GtkTextView's input action; XTest injection is restricted
            // by some Wayland compositors, so do not use it as a keyboard oracle.
            g_signal_emit_by_name(g_object_get_data(G_OBJECT(window),"text"),"insert-at-cursor","x");break;
        }
        case 5: {
            GtkTextIter start,end;gtk_text_buffer_get_bounds(buffer,&start,&end);g_autofree char *text=gtk_text_buffer_get_text(buffer,&start,&end,FALSE);
            check(strchr(text,'x')!=NULL,"GTK input action edits text");
            gtk_text_buffer_insert_at_cursor(buffer,"; Edição\n",-1);check(gtk_text_buffer_get_modified(buffer),"typing marks dirty");
            request("ROUTE","next.csv");break;
        }
        case 6:click_dialog("Cancelar");break;
        case 7:check(!pending_action&&gtk_text_buffer_get_modified(buffer),"cancel preserves editor");request("ROUTE","next.csv");break;
        case 8:click_dialog("Salvar e continuar");break;
        case 9:check(waiting&&pending_action,"save waits for disk acknowledgment");command("ERROR","Falha de gravação simulada");break;
        case 10:check(!waiting&&!pending_action&&gtk_text_buffer_get_modified(buffer),"save error preserves edits");click_dialog("Fechar");break;
        case 11:save();command("SAVED","");check(!gtk_text_buffer_get_modified(buffer),"successful save marks clean");request("ROUTE","next.csv");break;
        case 12:check(!pending_action,"clean route switch accepted");
            gtk_text_buffer_insert_at_cursor(buffer,"; pending\n",-1);request("QUIT","");break;
        case 13:click_dialog("Descartar");break;
        case 14:check(!pending_action,"discard accepted");
            g_print("EDITOR_UI_OK: side, bottom, independent window, GTK input action, reserved scrollbars, theme changes, editor visibility, UTF-8, dirty tracking, cancel, save acknowledgment, save error, discard\n");
            g_main_loop_quit(loop);return G_SOURCE_REMOVE;
    }
    return G_SOURCE_CONTINUE;
}
int main(void) {
    int input_pipe[2];check(pipe(input_pipe)==0,"stdin pipe");dup2(input_pipe[0],STDIN_FILENO);
    test_display=XOpenDisplay(NULL);check(test_display!=NULL,"X11 available");
    test_parent=XCreateSimpleWindow(test_display,DefaultRootWindow(test_display),0,0,1280,800,0,0,0);
    XMapWindow(test_display,test_parent);XFlush(test_display);
    char handle[32];snprintf(handle,sizeof(handle),"%lu",test_parent);char *argv[]={"route-editor",handle,NULL};
    g_timeout_add(250,tick,NULL);int result=route_editor_main(2,argv);
    XDestroyWindow(test_display,test_parent);XCloseDisplay(test_display);return result;
}
