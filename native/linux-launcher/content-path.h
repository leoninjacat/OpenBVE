#ifndef OPENBVE_CONTENT_PATH_H
#define OPENBVE_CONTENT_PATH_H
#include <gio/gio.h>

/* A document portal file grant exposes only that file, not the Railway tree.
 * Resolve its original location, using only the access already granted by the
 * sandbox. Never fall back to the isolated alias when resolution fails. */
static char *content_path_resolve(const char *path, GError **error)
{
    g_autofree char *mount = g_build_filename(g_get_user_runtime_dir(), "doc", NULL);
    g_autofree char *prefix = g_strconcat(mount, "/", NULL);
    if (!g_str_has_prefix(path, prefix)) return g_canonicalize_filename(path, NULL);
    const char *document = path + strlen(prefix);
    const char *separator = strchr(document, '/');
    if (!separator || separator == document) {
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_INVALID_ARGUMENT, "Caminho de documento inválido.");
        return NULL;
    }
    g_autofree char *id = g_strndup(document, separator - document);
    const char *ids[] = { id, NULL };
    g_autoptr(GDBusConnection) bus = g_bus_get_sync(G_BUS_TYPE_SESSION, NULL, error);
    if (!bus) return NULL;
    g_autoptr(GVariant) reply = g_dbus_connection_call_sync(bus,
        "org.freedesktop.portal.Documents", "/org/freedesktop/portal/documents",
        "org.freedesktop.portal.Documents", "GetHostPaths",
        g_variant_new("(^as)", ids), G_VARIANT_TYPE("(a{say})"),
        G_DBUS_CALL_FLAGS_NONE, 3000, NULL, error);
    if (!reply) return NULL;
    g_autoptr(GVariant) paths = g_variant_get_child_value(reply, 0);
    g_autoptr(GVariant) value = g_variant_lookup_value(paths, id, G_VARIANT_TYPE_BYTESTRING);
    if (!value) {
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_NOT_FOUND, "O seletor não informou a pasta original do conteúdo.");
        return NULL;
    }
    const char *host = g_variant_get_bytestring(value);
    /* Directory grants can reference descendants; the portal alias begins with
     * the granted file/directory's basename, followed by any relative suffix. */
    const char *suffix = strchr(separator + 1, '/');
    g_autofree char *resolved = suffix ? g_strconcat(host, suffix, NULL) : g_strdup(host);
    if (!g_path_is_absolute(resolved) || !g_file_test(resolved, G_FILE_TEST_EXISTS)) {
        g_set_error_literal(error, G_IO_ERROR, G_IO_ERROR_PERMISSION_DENIED,
            "A pasta original não está acessível. Selecione conteúdo na sua pasta pessoal ou conceda acesso à pasta do pacote completo.");
        return NULL;
    }
    return g_steal_pointer(&resolved);
}
#endif
