/* GTK shell hosts the X11 viewer and a dockable text editor.
 * UTF-8/base64 line protocol; the engine owns file encodings and disk writes. */
#include <adwaita.h>
#include <gdk/x11/gdkx.h>
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
static GtkWindow *window,*host_window,*floating_window;
static GtkPaned *paned;
static GtkWidget *viewer_area;
static GtkWidget *editor_content;
static GtkTextBuffer *buffer;
static GtkLabel *status;
static GMainLoop *loop;
static Display *display;
static Window parent, xid;
static gboolean received_size;
static gboolean editor_visible=TRUE;
static int reported_mode=-1,reported_width,reported_height;
static int width=1280,height=800,mode=0;
static gboolean changing,waiting,testing,viewer_closing;
static char *pending_action,*pending_value,*route_name;
static const char *layouts[]={"Lateral","Abaixo","Janela independente",NULL};
static void emit(const char *verb,const char *value) {
    g_autofree char *encoded=g_base64_encode((const guchar *)(value?value:""),value?strlen(value):0);
    g_print("%s\t%s\n",verb,encoded);fflush(stdout);
}
static void title(void) {
    gboolean dirty=gtk_text_buffer_get_modified(buffer);
    g_autofree char *heading=g_strdup_printf("%s%s — Route Viewer Native",dirty?"● ":"",route_name?route_name:"Editor de rotas");
    gtk_window_set_title(host_window,heading);
    gtk_window_set_title(floating_window,dirty?"● Editor da rota — alterações não salvas":"Editor da rota");
}
static void changed(GtkTextBuffer *b,gpointer data){(void)b;(void)data;if(!changing)title();}
// GTK owns the outer window and text controls. The existing OpenTK X11
// window is a native child over an empty GTK allocation, so neither toolkit
// attempts to position the other's toplevel window.
static gboolean place_viewer(gpointer data) {
    (void)data;if(!xid||viewer_closing)return G_SOURCE_CONTINUE;
    graphene_rect_t bounds;
    if(!gtk_widget_compute_bounds(viewer_area,GTK_WIDGET(host_window),&bounds))return G_SOURCE_CONTINUE;
    int x=(int)bounds.origin.x,y=(int)bounds.origin.y;
    int w=MAX(1,(int)bounds.size.width),h=MAX(1,(int)bounds.size.height);
    Window root,owner,*children=NULL;unsigned count;
    XQueryTree(display,parent,&root,&owner,&children,&count);if(children)XFree(children);
    if(owner!=xid){XSetWindowAttributes attributes={0};attributes.override_redirect=TRUE;XChangeWindowAttributes(display,parent,CWOverrideRedirect,&attributes);XReparentWindow(display,parent,xid,x,y);XMapWindow(display,parent);}
    XWindowAttributes a;XGetWindowAttributes(display,parent,&a);
    if(a.x!=x||a.y!=y||a.width!=w||a.height!=h)XMoveResizeWindow(display,parent,x,y,w,h);
    XFlush(display);
    if(reported_mode!=mode||reported_width!=w||reported_height!=h) {
        char geometry[96];snprintf(geometry,sizeof(geometry),"%d %d %d",mode,w,h);emit("CANVAS",geometry);
        reported_mode=mode;reported_width=w;reported_height=h;
    }
    return G_SOURCE_CONTINUE;
}
static gboolean size_panel(gpointer data) {
    (void)data;if(mode!=2) {
        int size=mode==0?gtk_widget_get_width(GTK_WIDGET(paned)):gtk_widget_get_height(GTK_WIDGET(paned));
        gtk_paned_set_position(paned,MAX(size/2,size-(mode==0?440:300)));
    }
    return G_SOURCE_REMOVE;
}
static void arrange(void) {
    if(!xid)return;
    GtkWindow *target=mode==2?floating_window:host_window;
    if(window!=target) {
        g_object_ref(editor_content);
        if(window==host_window)gtk_paned_set_end_child(paned,NULL);else gtk_window_set_child(floating_window,NULL);
        if(target==host_window){gtk_paned_set_end_child(paned,editor_content);gtk_widget_set_visible(GTK_WIDGET(floating_window),FALSE);}
        else gtk_window_set_child(floating_window,editor_content);
        g_object_unref(editor_content);window=target;title();
    }
    if(mode==2){if(editor_visible)gtk_window_present(floating_window);else gtk_widget_set_visible(GTK_WIDGET(floating_window),FALSE);}
    else {
        gtk_orientable_set_orientation(GTK_ORIENTABLE(paned),mode==0?GTK_ORIENTATION_HORIZONTAL:GTK_ORIENTATION_VERTICAL);
        g_timeout_add(80,size_panel,NULL);
    }
    place_viewer(NULL);
}
static void layout_changed(GObject *object,GParamSpec *spec,gpointer data) {
    (void)spec;(void)data;mode=(int)gtk_drop_down_get_selected(GTK_DROP_DOWN(object));
    arrange();emit("LAYOUT",mode==0?"side":mode==1?"bottom":"float");
}
static void save(void) {
    if(waiting)return;
    GtkTextIter start,end;gtk_text_buffer_get_bounds(buffer,&start,&end);
    g_autofree char *text=gtk_text_buffer_get_text(buffer,&start,&end,FALSE);
    waiting=TRUE;gtk_text_view_set_editable(GTK_TEXT_VIEW(g_object_get_data(G_OBJECT(window),"text")),FALSE);
    emit("SAVE",text);
}
static void saved_clicked(GtkButton *button,gpointer data){(void)button;(void)data;save();}
static void finish_action(void) {
    if(pending_action){if(g_str_equal(pending_action,"QUIT"))viewer_closing=TRUE;emit(pending_action,pending_value);g_clear_pointer(&pending_action,g_free);g_clear_pointer(&pending_value,g_free);}
}
static void choice(GObject *source,GAsyncResult *result,gpointer data) {
    (void)data;g_autoptr(GError) error=NULL;
    int answer=gtk_alert_dialog_choose_finish(GTK_ALERT_DIALOG(source),result,&error);
    if(answer==2)save();else if(answer==1)finish_action();else {g_clear_pointer(&pending_action,g_free);g_clear_pointer(&pending_value,g_free);}
}
static void request(const char *action,const char *value) {
    if(pending_action||waiting)return;
    pending_action=g_strdup(action);pending_value=g_strdup(value);
    if(!gtk_text_buffer_get_modified(buffer)){finish_action();return;}
    g_autoptr(GtkAlertDialog) dialog=gtk_alert_dialog_new("Salvar as alterações da rota?");
    const char *buttons[]={"Cancelar","Descartar","Salvar e continuar",NULL};
    gtk_alert_dialog_set_buttons(dialog,buttons);gtk_alert_dialog_set_cancel_button(dialog,0);gtk_alert_dialog_set_default_button(dialog,2);
    gtk_alert_dialog_choose(dialog,window,NULL,choice,NULL);
}
static gboolean close_requested(GtkWindow *w,gpointer data){(void)w;(void)data;request("QUIT","");return TRUE;}
static void focus_editor(GtkGestureClick *gesture,int presses,double x,double y,gpointer data) {
    (void)gesture;(void)presses;(void)x;(void)y;(void)data;
    if(xid){Window surface=gdk_x11_surface_get_xid(gtk_native_get_surface(GTK_NATIVE(window)));XSetInputFocus(display,surface,RevertToParent,CurrentTime);XFlush(display);}
}
static gboolean key_pressed(GtkEventControllerKey *controller,guint key,guint code,GdkModifierType state,gpointer data){
    (void)controller;(void)code;(void)data;
    if((state&GDK_CONTROL_MASK)&&(key==GDK_KEY_s||key==GDK_KEY_S)){save();return TRUE;}
    if(key==GDK_KEY_F10){emit("TOGGLE-UI","");return TRUE;}
    if(key==GDK_KEY_F8){emit("TOGGLE-EDITOR","");return TRUE;}
    if(key==GDK_KEY_F5){emit("RELOAD","");return TRUE;}
    if((state&GDK_CONTROL_MASK)&&(key==GDK_KEY_o||key==GDK_KEY_O)){emit("OPEN","");return TRUE;}
    return FALSE;
}
static gboolean input(GIOChannel *channel,GIOCondition condition,gpointer data) {
    (void)data;if(condition&(G_IO_HUP|G_IO_ERR)){g_main_loop_quit(loop);return G_SOURCE_REMOVE;}
    char *line=NULL;gsize length=0;
    if(g_io_channel_read_line(channel,&line,&length,NULL,NULL)!=G_IO_STATUS_NORMAL)return G_SOURCE_CONTINUE;
    g_strchomp(line);char *tab=strchr(line,'\t');if(tab)*tab++=0;
    gsize bytes=0;g_autofree guchar *decoded=tab?g_base64_decode(tab,&bytes):NULL;
    g_autofree char *value=decoded?g_strndup((char *)decoded,bytes):g_strdup("");
    if(testing&&g_str_equal(line,"TEST-THEME"))adw_style_manager_set_color_scheme(adw_style_manager_get_default(),g_str_equal(value,"dark")?ADW_COLOR_SCHEME_FORCE_DARK:g_str_equal(value,"light")?ADW_COLOR_SCHEME_FORCE_LIGHT:ADW_COLOR_SCHEME_DEFAULT);
    else if(testing&&g_str_equal(line,"TEST-LAYOUT"))gtk_drop_down_set_selected(g_object_get_data(G_OBJECT(window),"layout"),(guint)atoi(value));
    else if(testing&&g_str_equal(line,"TEST-EDIT")){GtkTextIter end;gtk_text_buffer_get_end_iter(buffer,&end);gtk_text_buffer_insert(buffer,&end,value,-1);save();}
    else if(g_str_equal(line,"VISIBLE")){
        editor_visible=g_str_equal(value,"1");gtk_widget_set_visible(editor_content,editor_visible);arrange();emit("VISIBLE",editor_visible?"1":"0");
    }
    else if(g_str_equal(line,"RECT")){if(!received_size){sscanf(value,"%d %d",&width,&height);gtk_window_set_default_size(host_window,width,height);received_size=TRUE;}}
    else if(g_str_equal(line,"TEXT")){
        changing=TRUE;gtk_text_buffer_begin_irreversible_action(buffer);gtk_text_buffer_set_text(buffer,value,-1);gtk_text_buffer_end_irreversible_action(buffer);gtk_text_buffer_set_modified(buffer,FALSE);changing=FALSE;title();
    } else if(g_str_equal(line,"PATH")){gtk_label_set_text(status,value);g_free(route_name);route_name=g_path_get_basename(value);title();}
    else if(g_str_equal(line,"ROUTE"))request("ROUTE",value);
    else if(g_str_equal(line,"QUIT"))request("QUIT","");
    else if(g_str_equal(line,"EXIT"))g_main_loop_quit(loop);
    else if(g_str_equal(line,"SAVED")||g_str_equal(line,"ERROR")){
        waiting=FALSE;gtk_text_view_set_editable(GTK_TEXT_VIEW(g_object_get_data(G_OBJECT(window),"text")),TRUE);
        if(g_str_equal(line,"SAVED")){gtk_text_buffer_set_modified(buffer,FALSE);title();finish_action();}
        else {g_clear_pointer(&pending_action,g_free);g_clear_pointer(&pending_value,g_free);g_autoptr(GtkAlertDialog) dialog=gtk_alert_dialog_new("%s",value);const char *buttons[]={"Fechar",NULL};gtk_alert_dialog_set_buttons(dialog,buttons);gtk_alert_dialog_show(dialog,window);}
    }
    g_free(line);return G_SOURCE_CONTINUE;
}
static void theme_changed(AdwStyleManager *manager,GParamSpec *spec,gpointer data) {
    (void)spec;(void)data;emit("THEME",adw_style_manager_get_dark(manager)?"dark":"light");
}
int main(int argc,char **argv) {
    if(argc!=2 && !(argc==3&&g_str_equal(argv[2],"--test")))return 2;
    testing=argc==3;
    g_set_prgname("openbve-routeviewer");g_set_application_name("Route Viewer Native");
    g_setenv("GDK_BACKEND","x11",TRUE);adw_init();parent=strtoul(argv[1],NULL,10);
    host_window=GTK_WINDOW(gtk_window_new());window=host_window;
    floating_window=GTK_WINDOW(gtk_window_new());gtk_window_set_default_size(floating_window,760,680);
    gtk_window_set_default_size(host_window,width,height);
    paned=GTK_PANED(gtk_paned_new(GTK_ORIENTATION_HORIZONTAL));gtk_window_set_child(host_window,GTK_WIDGET(paned));
    viewer_area=gtk_drawing_area_new();gtk_widget_set_size_request(viewer_area,320,240);
    gtk_widget_set_hexpand(viewer_area,TRUE);gtk_widget_set_vexpand(viewer_area,TRUE);
    gtk_paned_set_start_child(paned,viewer_area);gtk_paned_set_resize_start_child(paned,TRUE);gtk_paned_set_shrink_start_child(paned,FALSE);
    GtkWidget *box=gtk_box_new(GTK_ORIENTATION_VERTICAL,8);editor_content=box;gtk_widget_set_size_request(box,280,140);
    gtk_paned_set_end_child(paned,box);gtk_paned_set_resize_end_child(paned,FALSE);gtk_paned_set_shrink_end_child(paned,FALSE);
    GtkWidget *bar=gtk_box_new(GTK_ORIENTATION_HORIZONTAL,8);gtk_box_append(GTK_BOX(box),bar);
    GtkWidget *layout=gtk_drop_down_new_from_strings(layouts);gtk_widget_set_hexpand(layout,TRUE);gtk_box_append(GTK_BOX(bar),layout);
    GtkWidget *button=gtk_button_new_with_label("Salvar e atualizar");gtk_box_append(GTK_BOX(bar),button);
    status=GTK_LABEL(gtk_label_new("Abra uma rota para editar"));gtk_label_set_ellipsize(status,PANGO_ELLIPSIZE_MIDDLE);gtk_box_append(GTK_BOX(box),GTK_WIDGET(status));
    GtkWidget *scroll=gtk_scrolled_window_new();gtk_scrolled_window_set_overlay_scrolling(GTK_SCROLLED_WINDOW(scroll),FALSE);gtk_scrolled_window_set_policy(GTK_SCROLLED_WINDOW(scroll),GTK_POLICY_ALWAYS,GTK_POLICY_ALWAYS);gtk_widget_set_vexpand(scroll,TRUE);gtk_box_append(GTK_BOX(box),scroll);
    GtkWidget *text=gtk_text_view_new();gtk_text_view_set_monospace(GTK_TEXT_VIEW(text),TRUE);gtk_text_view_set_left_margin(GTK_TEXT_VIEW(text),12);gtk_text_view_set_top_margin(GTK_TEXT_VIEW(text),10);gtk_text_view_set_right_margin(GTK_TEXT_VIEW(text),12);gtk_text_view_set_bottom_margin(GTK_TEXT_VIEW(text),10);
    gtk_text_view_set_wrap_mode(GTK_TEXT_VIEW(text),GTK_WRAP_NONE);gtk_scrolled_window_set_child(GTK_SCROLLED_WINDOW(scroll),text);buffer=gtk_text_view_get_buffer(GTK_TEXT_VIEW(text));gtk_text_buffer_set_enable_undo(buffer,TRUE);
    g_object_set_data(G_OBJECT(window),"scroll",scroll);
    g_object_set_data(G_OBJECT(window),"text",text);
    g_object_set_data(G_OBJECT(window),"layout",layout);
    g_object_set_data(G_OBJECT(floating_window),"scroll",scroll);
    g_object_set_data(G_OBJECT(floating_window),"text",text);
    g_object_set_data(G_OBJECT(floating_window),"layout",layout);
    g_signal_connect(buffer,"modified-changed",G_CALLBACK(changed),NULL);g_signal_connect(button,"clicked",G_CALLBACK(saved_clicked),NULL);
    g_signal_connect(layout,"notify::selected",G_CALLBACK(layout_changed),NULL);g_signal_connect(window,"close-request",G_CALLBACK(close_requested),NULL);
    g_signal_connect(floating_window,"close-request",G_CALLBACK(close_requested),NULL);
    GtkGesture *click=gtk_gesture_click_new();gtk_gesture_single_set_button(GTK_GESTURE_SINGLE(click),0);
    gtk_event_controller_set_propagation_phase(GTK_EVENT_CONTROLLER(click),GTK_PHASE_CAPTURE);
    g_signal_connect(click,"pressed",G_CALLBACK(focus_editor),NULL);gtk_widget_add_controller(box,GTK_EVENT_CONTROLLER(click));
    GtkEventController *keys=gtk_event_controller_key_new();gtk_event_controller_set_propagation_phase(keys,GTK_PHASE_CAPTURE);g_signal_connect(keys,"key-pressed",G_CALLBACK(key_pressed),NULL);gtk_widget_add_controller(box,keys);
    gtk_window_present(host_window);GdkSurface *surface=gtk_native_get_surface(GTK_NATIVE(host_window));
    display=gdk_x11_display_get_xdisplay(gdk_surface_get_display(surface));xid=gdk_x11_surface_get_xid(surface);
    XClassHint identity={.res_name="openbve-routeviewer",.res_class="OpenBVE-RouteViewer"};XSetClassHint(display,xid,&identity);
    gtk_widget_realize(GTK_WIDGET(floating_window));XSetClassHint(display,gdk_x11_surface_get_xid(gtk_native_get_surface(GTK_NATIVE(floating_window))),&identity);
    XAddToSaveSet(display,parent);arrange();title();g_timeout_add(30,place_viewer,NULL);
    loop=g_main_loop_new(NULL,FALSE);GIOChannel *channel=g_io_channel_unix_new(STDIN_FILENO);g_io_add_watch(channel,G_IO_IN|G_IO_HUP|G_IO_ERR,input,NULL);
    char handle[32];snprintf(handle,sizeof(handle),"%lu",xid);emit("HOST",handle);
    AdwStyleManager *style=adw_style_manager_get_default();adw_style_manager_set_color_scheme(style,ADW_COLOR_SCHEME_DEFAULT);
    g_signal_connect(style,"notify::dark",G_CALLBACK(theme_changed),NULL);theme_changed(style,NULL,NULL);
    emit("LAYOUT","side");g_main_loop_run(loop);
    gdk_x11_display_error_trap_push(gdk_display_get_default());
    XUnmapWindow(display,parent);XReparentWindow(display,parent,DefaultRootWindow(display),0,0);XRemoveFromSaveSet(display,parent);XSync(display,FALSE);
    gdk_x11_display_error_trap_pop_ignored(gdk_display_get_default());
    gtk_window_destroy(floating_window);gtk_window_destroy(host_window);g_io_channel_unref(channel);g_main_loop_unref(loop);g_free(pending_action);g_free(pending_value);g_free(route_name);return 0;
}
