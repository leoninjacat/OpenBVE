/* GTK shell hosts the X11 viewer and a dockable text editor.
 * UTF-8/base64 line protocol; the engine owns file encodings and disk writes. */
#include <adwaita.h>
#include <gdk/x11/gdkx.h>
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
static gboolean object_editor;
static GtkWindow *window,*host_window,*floating_window;
static GtkPaned *paned;
static GtkWidget *viewer_area;
static GtkWidget *editor_content;
static GtkTextBuffer *buffer;
static GtkWidget *mesh_panel;
static GtkLabel *mesh_heading,*mesh_info,*mesh_dimensions,*mesh_error;
static GtkSpinButton *mesh_values[9],*mesh_sizes[3],*mesh_pivots[3];
static GtkWidget *pivot_origin,*pivot_center;
static GtkLabel *pivot_status;
static GtkColorDialogButton *mesh_color;
static int mesh_edits;
static gboolean mesh_changing;
static int mesh_id;
static guint preview_timer,property_timer;
static int graph_requests;
static gboolean save_after_graph;
static GQueue graph_queue=G_QUEUE_INIT;
static void save(void);
static void drain_graph_queue(void);
static char *current_text(void) {
    GtkTextIter start,end;gtk_text_buffer_get_bounds(buffer,&start,&end);
    return gtk_text_buffer_get_text(buffer,&start,&end,FALSE);
}
static gboolean preview(gpointer data);
static gboolean apply_properties(gpointer data);

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
    g_autofree char *heading=g_strdup_printf("%s%s — %s",dirty?"● ":"",route_name?route_name:"Editor",object_editor?"OpenBVE Object Editor":"Route Viewer Native");
    gtk_window_set_title(host_window,heading);
    gtk_window_set_title(floating_window,dirty?"● Código — alterações não salvas":"Código");
}
static void changed(GtkTextBuffer *b,gpointer data){
    (void)b;(void)data;if(!changing){
        title();
        if(object_editor){if(preview_timer)g_source_remove(preview_timer);preview_timer=g_timeout_add(200,preview,NULL);}
    }
}
static gboolean preview(gpointer data) {
    (void)data;preview_timer=0;g_autofree char *text=current_text();emit("PREVIEW",text);return G_SOURCE_REMOVE;
}
static gboolean apply_properties(gpointer data) {
    (void)data;property_timer=0;if(!mesh_id||!mesh_edits||graph_requests)return G_SOURCE_REMOVE;
    GString *value=g_string_new(NULL);g_string_append_printf(value,"%d %d",mesh_id,mesh_edits);
    mesh_edits=0;mesh_changing=TRUE;
    for(int i=0;i<12;i++){
        GtkSpinButton *spin=i<9?mesh_values[i]:mesh_sizes[i-9];char number[G_ASCII_DTOSTR_BUF_SIZE];
        gtk_spin_button_update(spin);g_ascii_dtostr(number,sizeof(number),gtk_spin_button_get_value(spin));g_string_append_printf(value," %s",number);
    }
    mesh_changing=FALSE;
    const GdkRGBA *rgba=gtk_color_dialog_button_get_rgba(mesh_color);
    g_string_append_printf(value," %d %d %d %d",(int)(rgba->red*255+.5),(int)(rgba->green*255+.5),(int)(rgba->blue*255+.5),(int)(rgba->alpha*255+.5));
    for(int i=0;i<3;i++){char number[G_ASCII_DTOSTR_BUF_SIZE];mesh_changing=TRUE;gtk_spin_button_update(mesh_pivots[i]);mesh_changing=FALSE;g_ascii_dtostr(number,sizeof(number),gtk_spin_button_get_value(mesh_pivots[i]));g_string_append_printf(value," %s",number);}
    g_autofree char *text=current_text();g_string_append_c(value,'\n');g_string_append(value,text);graph_requests++;emit("GRAPH-SET",value->str);g_string_free(value,TRUE);return G_SOURCE_REMOVE;
}
static void queue_property(int edits) {
    if(mesh_changing||!mesh_id)return;
    mesh_edits|=edits;
    if(property_timer)g_source_remove(property_timer);
    property_timer=g_timeout_add(180,apply_properties,NULL);
}
static void property_changed(GtkSpinButton *spin,gpointer data) {
    (void)spin;queue_property(GPOINTER_TO_INT(data));
}
static void color_changed(GObject *object,GParamSpec *spec,gpointer data) {
    (void)object;(void)spec;(void)data;queue_property(4);
}

// Requests from the viewport wait for outstanding panel edits. Take the current
// text only when sending, so a drag never overwrites a preceding property patch.
static void drain_graph_queue(void) {
    if(graph_requests)return;
    if(mesh_edits){
        if(property_timer){g_source_remove(property_timer);property_timer=0;}
        apply_properties(NULL);if(graph_requests)return;
    }
    if(!g_queue_is_empty(&graph_queue)){
        g_autofree char *request=g_queue_pop_head(&graph_queue);char *value=strchr(request,'\n');if(!value)return;*value++=0;
        g_autofree char *text=current_text();g_autofree char *payload=g_strconcat(value,"\n",text,NULL);
        graph_requests++;emit(request,payload);return;
    }
    if(save_after_graph){save_after_graph=FALSE;save();}
}

static void pivot_clicked(GtkButton *button,gpointer data) {
    (void)button;if(!mesh_id)return;
    g_queue_push_tail(&graph_queue,g_strdup_printf("GRAPH-PIVOT\n%d %s",mesh_id,(const char *)data));drain_graph_queue();
}

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
    if(object_editor && mesh_edits){if(property_timer)g_source_remove(property_timer);property_timer=0;apply_properties(NULL);}
    if(object_editor && (graph_requests||!g_queue_is_empty(&graph_queue))){save_after_graph=TRUE;drain_graph_queue();return;}
    if(preview_timer){g_source_remove(preview_timer);preview_timer=0;}
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
    g_autoptr(GtkAlertDialog) dialog=gtk_alert_dialog_new("Salvar as alterações do arquivo?");
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
    // GLib may read several pipe lines into its own buffer in a single read.
    // Drain them now; the file descriptor may not become readable again.
    GIOFlags flags=g_io_channel_get_flags(channel);
    if(!(flags&G_IO_FLAG_NONBLOCK))g_io_channel_set_flags(channel,flags|G_IO_FLAG_NONBLOCK,NULL);
    for(;;){
    char *line=NULL;gsize length=0;
    GIOStatus result=g_io_channel_read_line(channel,&line,&length,NULL,NULL);
    if(result!=G_IO_STATUS_NORMAL){g_free(line);return G_SOURCE_CONTINUE;}
    g_strchomp(line);char *tab=strchr(line,'\t');if(tab)*tab++=0;
    gsize bytes=0;g_autofree guchar *decoded=tab?g_base64_decode(tab,&bytes):NULL;
    g_autofree char *value=decoded?g_strndup((char *)decoded,bytes):g_strdup("");
    if(testing&&g_str_equal(line,"TEST-THEME"))adw_style_manager_set_color_scheme(adw_style_manager_get_default(),g_str_equal(value,"dark")?ADW_COLOR_SCHEME_FORCE_DARK:g_str_equal(value,"light")?ADW_COLOR_SCHEME_FORCE_LIGHT:ADW_COLOR_SCHEME_DEFAULT);
    else if(testing&&g_str_equal(line,"TEST-LAYOUT"))gtk_drop_down_set_selected(g_object_get_data(G_OBJECT(window),"layout"),(guint)atoi(value));
    else if(testing&&object_editor&&g_str_equal(line,"TEST-PROPERTY")){for(int i=6;i<9;i++)gtk_spin_button_set_value(mesh_values[i],3);}
    else if(testing&&object_editor&&g_str_equal(line,"TEST-COLOR")){
        GdkRGBA rgba={.red=12/255.0f,.green=100/255.0f,.blue=210/255.0f,.alpha=g_str_equal(value,"solid")?1:180/255.0f};gtk_color_dialog_button_set_rgba(mesh_color,&rgba);
    }
    else if(testing&&object_editor&&g_str_equal(line,"TEST-SIZE")){
        g_auto(GStrv) sizes=g_strsplit(value," ",-1);if(g_strv_length(sizes)==3)for(int i=0;i<3;i++)gtk_spin_button_set_value(mesh_sizes[i],g_ascii_strtod(sizes[i],NULL));
    }
    else if(testing&&object_editor&&g_str_equal(line,"TEST-ROTATION")){
        gtk_spin_button_set_value(mesh_values[3],17);gtk_spin_button_set_value(mesh_values[4],-23);gtk_spin_button_set_value(mesh_values[5],41);
    }
    else if(testing&&object_editor&&g_str_equal(line,"TEST-READ-TRANSFORM")){
        GString *result=g_string_new(NULL);
        for(int i=0;i<9;i++){char number[G_ASCII_DTOSTR_BUF_SIZE];g_ascii_dtostr(number,sizeof(number),gtk_spin_button_get_value(mesh_values[i]));if(i)g_string_append_c(result,' ');g_string_append(result,number);}
        emit("TEST-TRANSFORM",result->str);g_string_free(result,TRUE);
    }
    else if(testing&&object_editor&&g_str_equal(line,"TEST-READ-PROPERTIES")){
        const GdkRGBA *rgba=gtk_color_dialog_button_get_rgba(mesh_color);
        char report[256];g_snprintf(report,sizeof(report),"%d %d %d %d",(int)(rgba->red*255+.5),(int)(rgba->green*255+.5),(int)(rgba->blue*255+.5),(int)(rgba->alpha*255+.5));
        GString *result=g_string_new(report);
        for(int i=0;i<3;i++){char number[G_ASCII_DTOSTR_BUF_SIZE];g_ascii_dtostr(number,sizeof(number),gtk_spin_button_get_value(mesh_sizes[i]));g_string_append_printf(result," %s",number);}
        emit("TEST-PROPERTIES",result->str);g_string_free(result,TRUE);
    }
    else if(testing&&object_editor&&g_str_equal(line,"TEST-PIVOT-SET")){
        g_auto(GStrv) fields=g_strsplit(value," ",-1);if(g_strv_length(fields)==3)for(int i=0;i<3;i++)gtk_spin_button_set_value(mesh_pivots[i],g_ascii_strtod(fields[i],NULL));
    }
    else if(testing&&object_editor&&g_str_equal(line,"TEST-PIVOT-ORIGIN")){g_signal_emit_by_name(pivot_origin,"clicked");}
    else if(testing&&object_editor&&g_str_equal(line,"TEST-PIVOT-CENTER")){g_signal_emit_by_name(pivot_center,"clicked");}
    else if(testing&&object_editor&&g_str_equal(line,"TEST-READ-PIVOT")){
        GString *result=g_string_new(NULL);for(int i=0;i<3;i++){char number[G_ASCII_DTOSTR_BUF_SIZE];g_ascii_dtostr(number,sizeof(number),gtk_spin_button_get_value(mesh_pivots[i]));if(i)g_string_append_c(result,' ');g_string_append(result,number);}
        g_string_append_printf(result," %d",gtk_text_buffer_get_modified(buffer)?1:0);
        emit("TEST-PIVOT",result->str);g_string_free(result,TRUE);
    }
    else if(testing&&object_editor&&g_str_equal(line,"TEST-UNDO")){gtk_text_buffer_undo(buffer);}
    else if(testing&&g_str_equal(line,"TEST-EDIT")){GtkTextIter end;gtk_text_buffer_get_end_iter(buffer,&end);gtk_text_buffer_insert(buffer,&end,value,-1);save();}
    else if(g_str_equal(line,"VISIBLE")){
        editor_visible=g_str_equal(value,"1");gtk_widget_set_visible(editor_content,editor_visible);arrange();emit("VISIBLE",editor_visible?"1":"0");
    }
    else if(g_str_equal(line,"RECT")){if(!received_size){sscanf(value,"%d %d",&width,&height);gtk_window_set_default_size(host_window,width,height);received_size=TRUE;}}
    else if(object_editor&&g_str_equal(line,"MESH")){
        if(property_timer && (!*value || atoi(value)!=mesh_id)){g_source_remove(property_timer);property_timer=0;apply_properties(NULL);}
        if(!*value){mesh_id=0;gtk_label_set_text(mesh_heading,"Selecione um mesh na vista 3D");gtk_label_set_text(mesh_info,"");gtk_label_set_text(mesh_dimensions,"");gtk_widget_set_sensitive(mesh_panel,FALSE);}
        else {
            g_auto(GStrv) fields=g_strsplit(value,"\t",-1);
            if(g_strv_length(fields)==20 && !((property_timer||graph_requests||mesh_edits) && mesh_id==atoi(fields[0]))){
                mesh_changing=TRUE;mesh_id=atoi(fields[0]);
                g_autofree char *heading=g_strdup_printf("Mesh %d",mesh_id);gtk_label_set_text(mesh_heading,heading);
                g_autofree char *info=g_strdup_printf("%s vértices · %s faces",fields[1],fields[2]);gtk_label_set_text(mesh_info,info);
                for(int i=0;i<9;i++)gtk_spin_button_set_value(mesh_values[i],g_ascii_strtod(fields[i+3],NULL));
                for(int i=0;i<3;i++){
                    double size=g_ascii_strtod(fields[12+i],NULL);gtk_spin_button_set_value(mesh_sizes[i],size);
                    gtk_widget_set_sensitive(GTK_WIDGET(mesh_sizes[i]),size>0.000001);
                }
                GdkRGBA rgba={atoi(fields[15])/255.0f,atoi(fields[16])/255.0f,atoi(fields[17])/255.0f,atoi(fields[18])/255.0f};
                gtk_color_dialog_button_set_rgba(mesh_color,&rgba);
                gtk_label_set_text(mesh_dimensions,atoi(fields[19])?"Dimensões nos eixos do modelo · cores variadas (a amostra mostra a primeira)":"Dimensões nos eixos do modelo, em metros");gtk_label_set_text(mesh_error,"");
                gtk_widget_set_sensitive(mesh_panel,TRUE);mesh_changing=FALSE;
            }
        }
    }
    else if(object_editor&&g_str_equal(line,"PIVOT")){
        if(!*value){gtk_label_set_text(pivot_status,"");}
        else{
            g_auto(GStrv) fields=g_strsplit(value,"\t",-1);
            if(g_strv_length(fields)==5&&atoi(fields[0])==mesh_id&&!(mesh_edits&8)){
                mesh_changing=TRUE;for(int i=0;i<3;i++)gtk_spin_button_set_value(mesh_pivots[i],g_ascii_strtod(fields[i+1],NULL));mesh_changing=FALSE;
                gtk_label_set_text(pivot_status,fields[4]);
            }
        }
    }
    else if(object_editor&&g_str_equal(line,"PIVOT-DONE")){if(graph_requests>0)graph_requests--;drain_graph_queue();}
    else if(object_editor&&g_str_equal(line,"PIVOT-ERROR")){gtk_label_set_text(mesh_error,value);}
    else if(object_editor&&(g_str_equal(line,"MOVE-REQUEST")||g_str_equal(line,"TOOL-REQUEST"))){
        g_queue_push_tail(&graph_queue,g_strconcat(g_str_equal(line,"TOOL-REQUEST")?"GRAPH-TOOL":"GRAPH-MOVE","\n",value,NULL));drain_graph_queue();
    }
    else if(object_editor&&g_str_equal(line,"GRAPH-ERROR")){if(graph_requests>0)graph_requests--;save_after_graph=FALSE;gtk_label_set_text(mesh_error,value);drain_graph_queue();}
    else if(object_editor&&g_str_equal(line,"PATCH")){
        if(graph_requests>0)graph_requests--;
        if(preview_timer){g_source_remove(preview_timer);preview_timer=0;}
        char *newline=strchr(value,'\n');
        if(newline){
            *newline=0;gsize original_size=0;g_autofree guchar *original=g_base64_decode(value,&original_size);
            g_autofree char *text=current_text();
            if(strlen(text)!=original_size||memcmp(text,original,original_size)!=0){
                save_after_graph=FALSE;gtk_label_set_text(mesh_error,"O código mudou durante a alteração gráfica. Tente novamente.");emit("PREVIEW",text);
            }else{
                changing=TRUE;GtkTextIter start,end;gtk_text_buffer_begin_user_action(buffer);gtk_text_buffer_get_bounds(buffer,&start,&end);gtk_text_buffer_delete(buffer,&start,&end);gtk_text_buffer_insert(buffer,&start,newline+1,-1);gtk_text_buffer_end_user_action(buffer);changing=FALSE;title();emit("PREVIEW",newline+1);
            }
        }
        drain_graph_queue();
    }
    else if(g_str_equal(line,"TEXT")){
        changing=TRUE;gtk_text_buffer_begin_irreversible_action(buffer);gtk_text_buffer_set_text(buffer,value,-1);gtk_text_buffer_end_irreversible_action(buffer);gtk_text_buffer_set_modified(buffer,FALSE);changing=FALSE;title();
    } else if(g_str_equal(line,"PATH")){gtk_label_set_text(status,value);g_free(route_name);route_name=g_path_get_basename(value);title();}
    else if(g_str_equal(line,"SAVE-REQUEST"))save();
    else if(g_str_equal(line,"ROUTE"))request("ROUTE",value);
    else if(g_str_equal(line,"QUIT"))request("QUIT","");
    else if(g_str_equal(line,"EXIT"))g_main_loop_quit(loop);
    else if(g_str_equal(line,"SAVED")||g_str_equal(line,"ERROR")){
        waiting=FALSE;gtk_text_view_set_editable(GTK_TEXT_VIEW(g_object_get_data(G_OBJECT(window),"text")),TRUE);
        if(g_str_equal(line,"SAVED")){gtk_text_buffer_set_modified(buffer,FALSE);title();finish_action();}
        else {g_clear_pointer(&pending_action,g_free);g_clear_pointer(&pending_value,g_free);g_autoptr(GtkAlertDialog) dialog=gtk_alert_dialog_new("%s",value);const char *buttons[]={"Fechar",NULL};gtk_alert_dialog_set_buttons(dialog,buttons);gtk_alert_dialog_show(dialog,window);}
    }
    g_free(line);
    }
}
static void theme_changed(AdwStyleManager *manager,GParamSpec *spec,gpointer data) {
    (void)spec;(void)data;emit("THEME",adw_style_manager_get_dark(manager)?"dark":"light");
}
int main(int argc,char **argv) {
    if(argc!=2 && !(argc==3&&g_str_equal(argv[2],"--test")))return 2;
    testing=argc==3;object_editor=g_getenv("OPENBVE_OBJECT_EDITOR")!=NULL;
    g_set_prgname(object_editor?"openbve-objecteditor":"openbve-routeviewer");g_set_application_name(object_editor?"OpenBVE Object Editor":"Route Viewer Native");
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
    status=GTK_LABEL(gtk_label_new(object_editor?"Abra um objeto B3D/CSV para editar":"Abra uma rota para editar"));gtk_label_set_ellipsize(status,PANGO_ELLIPSIZE_MIDDLE);gtk_box_append(GTK_BOX(box),GTK_WIDGET(status));
    if(object_editor){
        mesh_heading=GTK_LABEL(gtk_label_new("Selecione um mesh na vista 3D"));gtk_widget_add_css_class(GTK_WIDGET(mesh_heading),"title-3");gtk_box_append(GTK_BOX(box),GTK_WIDGET(mesh_heading));
        mesh_info=GTK_LABEL(gtk_label_new(""));gtk_box_append(GTK_BOX(box),GTK_WIDGET(mesh_info));
        mesh_panel=gtk_grid_new();gtk_grid_set_column_spacing(GTK_GRID(mesh_panel),6);gtk_grid_set_row_spacing(GTK_GRID(mesh_panel),6);gtk_box_append(GTK_BOX(box),mesh_panel);
        const char *groups[]={"Posição Δ (m)","Rotação Δ (°)","Escala ×"};const char *axes[]={"X","Y","Z"};
        for(int axis=0;axis<3;axis++)gtk_grid_attach(GTK_GRID(mesh_panel),gtk_label_new(axes[axis]),axis+1,0,1,1);
        for(int group=0;group<3;group++){
            gtk_grid_attach(GTK_GRID(mesh_panel),gtk_label_new(groups[group]),0,group+1,1,1);
            for(int axis=0;axis<3;axis++){
                int i=group*3+axis;mesh_values[i]=GTK_SPIN_BUTTON(gtk_spin_button_new_with_range(group==2?0.001:-1000000,1000000,group==1?1:0.1));
                gtk_spin_button_set_digits(mesh_values[i],3);gtk_widget_set_hexpand(GTK_WIDGET(mesh_values[i]),TRUE);gtk_editable_set_width_chars(GTK_EDITABLE(mesh_values[i]),6);
                gtk_grid_attach(GTK_GRID(mesh_panel),GTK_WIDGET(mesh_values[i]),axis+1,group+1,1,1);g_signal_connect(mesh_values[i],"value-changed",G_CALLBACK(property_changed),GINT_TO_POINTER(1));
            }
        }
        gtk_grid_attach(GTK_GRID(mesh_panel),gtk_label_new("Dimensões (m)"),0,4,1,1);
        for(int axis=0;axis<3;axis++){
            mesh_sizes[axis]=GTK_SPIN_BUTTON(gtk_spin_button_new_with_range(0,1000000,.1));gtk_spin_button_set_digits(mesh_sizes[axis],3);
            gtk_widget_set_hexpand(GTK_WIDGET(mesh_sizes[axis]),TRUE);gtk_editable_set_width_chars(GTK_EDITABLE(mesh_sizes[axis]),6);
            gtk_grid_attach(GTK_GRID(mesh_panel),GTK_WIDGET(mesh_sizes[axis]),axis+1,4,1,1);
            g_signal_connect(mesh_sizes[axis],"value-changed",G_CALLBACK(property_changed),GINT_TO_POINTER(2));
        }
        GtkColorDialog *color_dialog=gtk_color_dialog_new();gtk_color_dialog_set_title(color_dialog,"Cor do mesh");gtk_color_dialog_set_with_alpha(color_dialog,TRUE);
        mesh_color=GTK_COLOR_DIALOG_BUTTON(gtk_color_dialog_button_new(color_dialog));
        gtk_widget_set_tooltip_text(GTK_WIDGET(mesh_color),"Alterar cor e opacidade do mesh selecionado");
        gtk_grid_attach(GTK_GRID(mesh_panel),gtk_label_new("Cor"),0,5,1,1);gtk_grid_attach(GTK_GRID(mesh_panel),GTK_WIDGET(mesh_color),1,5,3,1);
        g_signal_connect(mesh_color,"notify::rgba",G_CALLBACK(color_changed),NULL);
        gtk_grid_attach(GTK_GRID(mesh_panel),gtk_label_new("Pivô (m)"),0,6,1,1);
        for(int axis=0;axis<3;axis++){
            mesh_pivots[axis]=GTK_SPIN_BUTTON(gtk_spin_button_new_with_range(-1000000,1000000,.1));gtk_spin_button_set_digits(mesh_pivots[axis],3);
            gtk_widget_set_hexpand(GTK_WIDGET(mesh_pivots[axis]),TRUE);gtk_editable_set_width_chars(GTK_EDITABLE(mesh_pivots[axis]),6);
            gtk_widget_set_tooltip_text(GTK_WIDGET(mesh_pivots[axis]),"Posição do pivô nos eixos do modelo. Alterar o pivô não move a geometria.");
            gtk_grid_attach(GTK_GRID(mesh_panel),GTK_WIDGET(mesh_pivots[axis]),axis+1,6,1,1);g_signal_connect(mesh_pivots[axis],"value-changed",G_CALLBACK(property_changed),GINT_TO_POINTER(8));
        }
        GtkWidget *pivot_buttons=gtk_box_new(GTK_ORIENTATION_HORIZONTAL,6);
        pivot_origin=gtk_button_new_with_label("Origem");pivot_center=gtk_button_new_with_label("Centro do objeto");
        gtk_widget_set_tooltip_text(pivot_origin,"Posicionar o pivô em X=0, Y=0, Z=0 do modelo");gtk_widget_set_tooltip_text(pivot_center,"Posicionar o pivô no centro da caixa delimitadora da mesh selecionada");
        gtk_box_append(GTK_BOX(pivot_buttons),pivot_origin);gtk_box_append(GTK_BOX(pivot_buttons),pivot_center);gtk_grid_attach(GTK_GRID(mesh_panel),pivot_buttons,1,7,3,1);
        g_signal_connect(pivot_origin,"clicked",G_CALLBACK(pivot_clicked),"origin");g_signal_connect(pivot_center,"clicked",G_CALLBACK(pivot_clicked),"center");
        pivot_status=GTK_LABEL(gtk_label_new(""));gtk_label_set_wrap(pivot_status,TRUE);gtk_grid_attach(GTK_GRID(mesh_panel),GTK_WIDGET(pivot_status),0,8,4,1);
        gtk_widget_set_sensitive(mesh_panel,FALSE);
        mesh_dimensions=GTK_LABEL(gtk_label_new(""));gtk_label_set_wrap(mesh_dimensions,TRUE);gtk_box_append(GTK_BOX(box),GTK_WIDGET(mesh_dimensions));
        mesh_error=GTK_LABEL(gtk_label_new(""));gtk_label_set_wrap(mesh_error,TRUE);gtk_widget_add_css_class(GTK_WIDGET(mesh_error),"error");gtk_box_append(GTK_BOX(box),GTK_WIDGET(mesh_error));
        GtkWidget *hint=gtk_label_new("Ferramenta + eixo + arraste · Ctrl+Z no código desfaz\nDefinir pivô não altera o modelo · rotação/escala usam o pivô");gtk_label_set_wrap(GTK_LABEL(hint),TRUE);gtk_widget_add_css_class(hint,"dim-label");gtk_box_append(GTK_BOX(box),hint);
    }
    GtkWidget *scroll=gtk_scrolled_window_new();gtk_scrolled_window_set_overlay_scrolling(GTK_SCROLLED_WINDOW(scroll),FALSE);gtk_scrolled_window_set_policy(GTK_SCROLLED_WINDOW(scroll),GTK_POLICY_ALWAYS,GTK_POLICY_ALWAYS);gtk_widget_set_vexpand(scroll,TRUE);gtk_box_append(GTK_BOX(box),scroll);
    GtkWidget *text=gtk_text_view_new();gtk_text_view_set_monospace(GTK_TEXT_VIEW(text),TRUE);gtk_text_view_set_left_margin(GTK_TEXT_VIEW(text),12);gtk_text_view_set_top_margin(GTK_TEXT_VIEW(text),10);gtk_text_view_set_right_margin(GTK_TEXT_VIEW(text),12);gtk_text_view_set_bottom_margin(GTK_TEXT_VIEW(text),10);
    gtk_text_view_set_wrap_mode(GTK_TEXT_VIEW(text),GTK_WRAP_NONE);gtk_scrolled_window_set_child(GTK_SCROLLED_WINDOW(scroll),text);buffer=gtk_text_view_get_buffer(GTK_TEXT_VIEW(text));gtk_text_buffer_set_enable_undo(buffer,TRUE);
    g_object_set_data(G_OBJECT(window),"scroll",scroll);
    g_object_set_data(G_OBJECT(window),"text",text);
    g_object_set_data(G_OBJECT(window),"layout",layout);
    g_object_set_data(G_OBJECT(floating_window),"scroll",scroll);
    g_object_set_data(G_OBJECT(floating_window),"text",text);
    g_object_set_data(G_OBJECT(floating_window),"layout",layout);
    g_signal_connect(buffer,"changed",G_CALLBACK(changed),NULL);g_signal_connect(button,"clicked",G_CALLBACK(saved_clicked),NULL);
    g_signal_connect(layout,"notify::selected",G_CALLBACK(layout_changed),NULL);g_signal_connect(window,"close-request",G_CALLBACK(close_requested),NULL);
    g_signal_connect(floating_window,"close-request",G_CALLBACK(close_requested),NULL);
    GtkGesture *click=gtk_gesture_click_new();gtk_gesture_single_set_button(GTK_GESTURE_SINGLE(click),0);
    gtk_event_controller_set_propagation_phase(GTK_EVENT_CONTROLLER(click),GTK_PHASE_CAPTURE);
    g_signal_connect(click,"pressed",G_CALLBACK(focus_editor),NULL);gtk_widget_add_controller(box,GTK_EVENT_CONTROLLER(click));
    GtkEventController *keys=gtk_event_controller_key_new();gtk_event_controller_set_propagation_phase(keys,GTK_PHASE_CAPTURE);g_signal_connect(keys,"key-pressed",G_CALLBACK(key_pressed),NULL);gtk_widget_add_controller(box,keys);
    gtk_window_present(host_window);GdkSurface *surface=gtk_native_get_surface(GTK_NATIVE(host_window));
    display=gdk_x11_display_get_xdisplay(gdk_surface_get_display(surface));xid=gdk_x11_surface_get_xid(surface);
    XClassHint identity={.res_name=object_editor?"openbve-objecteditor":"openbve-routeviewer",.res_class=object_editor?"OpenBVE-ObjectEditor":"OpenBVE-RouteViewer"};XSetClassHint(display,xid,&identity);
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
    g_queue_clear_full(&graph_queue,g_free);gtk_window_destroy(floating_window);gtk_window_destroy(host_window);g_io_channel_unref(channel);g_main_loop_unref(loop);g_free(pending_action);g_free(pending_value);g_free(route_name);return 0;
}
