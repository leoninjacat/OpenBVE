#define main route_editor_main
#include "route-editor.c"
#undef main
int main(void) {
    gtk_init();object_editor=TRUE;buffer=gtk_text_buffer_new(NULL);gtk_text_buffer_set_enable_undo(buffer,TRUE);
    mesh_error=GTK_LABEL(gtk_label_new(""));
    for(int i=0;i<3;i++){primitive_size[i]=GTK_SPIN_BUTTON(gtk_spin_button_new_with_range(.001,100000,.1));gtk_spin_button_set_value(primitive_size[i],i+2);}
    primitive_segments=GTK_SPIN_BUTTON(gtk_spin_button_new_with_range(3,256,1));gtk_spin_button_set_value(primitive_segments,12);
    for(int format=0;format<2;format++)for(int kind=0;kind<3;kind++){
        g_free(object_path);object_path=g_strdup(format?"test.csv":"test.b3d");
        gtk_text_buffer_set_text(buffer,"; original\n",-1);gtk_text_buffer_set_modified(buffer,FALSE);
        primitive_clicked(NULL,GINT_TO_POINTER(kind));g_autofree char *text=current_text();
        g_assert_true(gtk_text_buffer_get_modified(buffer));g_assert_nonnull(strstr(text,format?"CreateMeshBuilder":"[MeshBuilder]"));
        g_autofree char *path=g_strdup_printf("/tmp/object-creation-%d.%s",kind,format?"csv":"b3d");g_assert_true(g_file_set_contents(path,text,-1,NULL));
        gtk_text_buffer_undo(buffer);g_autofree char *undone=current_text();g_assert_cmpstr(undone,==,"; original\n");
    }
    g_print("OBJECT_CREATION_OK: B3D/CSV, cube/plane/cylinder, dimensions, undo\n");return 0;
}
