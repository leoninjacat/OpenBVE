#include "preview.h"
#include <libxml/parser.h>
#include <libxml/xpath.h>
#include <string.h>

static char *child(const char *folder, const char *name)
{
    g_auto(GStrv) parts = g_strsplit_set(name, "/\\", -1);
    char *path = g_strdup(folder);
    for (int i = 0; parts[i]; i++) {
        char *next = g_build_filename(path, parts[i], NULL);
        if (!g_file_test(next, G_FILE_TEST_EXISTS)) {
            GDir *dir = g_dir_open(path, 0, NULL);
            const char *entry;
            while (dir && (entry = g_dir_read_name(dir))) {
                if (!g_ascii_strcasecmp(entry, parts[i])) {
                    g_free(next); next = g_build_filename(path, entry, NULL); break;
                }
            }
            if (dir) g_dir_close(dir);
        }
        g_free(path); path = next;
    }
    return path;
}
static char *read_text(const char *path, GError **error)
{
    g_autofree char *bytes = NULL;
    gsize size;
    if (!g_file_get_contents(path, &bytes, &size, error)) return NULL;
    const char *encoding = NULL;
    gsize offset = 0;
    if (size >= 2 && (guchar)bytes[0] == 0xff && (guchar)bytes[1] == 0xfe)
        encoding = "UTF-16LE", offset = 2;
    else if (size >= 2 && (guchar)bytes[0] == 0xfe && (guchar)bytes[1] == 0xff)
        encoding = "UTF-16BE", offset = 2;
    else if (size >= 3 && !memcmp(bytes, "\xef\xbb\xbf", 3)) offset = 3;
    if (!encoding && g_utf8_validate(bytes + offset, size - offset, NULL))
        return g_strndup(bytes + offset, size - offset);
    return g_convert(bytes + offset, size - offset, "UTF-8",
                     encoding ? encoding : "WINDOWS-1252", NULL, NULL, error);
}
static char *first_file(const char *folder, const char *const *names)
{
    for (int i = 0; names[i]; i++) {
        char *path = child(folder, names[i]);
        if (g_file_test(path, G_FILE_TEST_IS_REGULAR)) return path;
        g_free(path);
    }
    return NULL;
}
void preview_clear(Preview *p)
{
    g_free(p->description); g_free(p->image); g_free(p->notice);
    memset(p, 0, sizeof(*p));
}
Preview preview_read(const char *path, gboolean train)
{
    Preview p = {0};
    if (!g_file_test(path, train ? G_FILE_TEST_IS_DIR : G_FILE_TEST_IS_REGULAR)) {
        p.notice = g_strdup(train ? "Pasta de trem não encontrada." : "Arquivo de rota não encontrado.");
        return p;
    }
    if (train) {
        const char *texts[] = {"train.txt", "readme.txt", "read me.txt", NULL};
        const char *images[] = {"train.png", "train.gif", "train.bmp", NULL};
        g_autofree char *xml = child(path, "train.xml");
        if (g_file_test(xml, G_FILE_TEST_IS_REGULAR)) {
            xmlDoc *doc = xmlReadFile(xml, NULL, XML_PARSE_NONET | XML_PARSE_NOERROR | XML_PARSE_NOWARNING);
            if (doc) {
                xmlXPathContext *ctx = xmlXPathNewContext(doc);
                xmlXPathObject *result = ctx ? xmlXPathEvalExpression(BAD_CAST "/openBVE/Train/Description", ctx) : NULL;
                if (result && result->nodesetval) {
                    for (int i = 0; i < result->nodesetval->nodeNr; i++) {
                        xmlChar *text = xmlNodeGetContent(result->nodesetval->nodeTab[i]);
                        if (text && *text) p.description = g_strdup((char *)text);
                        xmlFree(text);
                        if (p.description) break;
                    }
                }
                if (result) xmlXPathFreeObject(result);
                if (ctx) xmlXPathFreeContext(ctx);
                xmlFreeDoc(doc);
            } else p.notice = g_strdup("train.xml inválido; usando texto quando disponível.");
        }
        if (!p.description) {
            g_autofree char *file = first_file(path, texts);
            if (file) p.description = read_text(file, NULL);
        }
        p.image = first_file(path, images);
        const char *definitions[] = {"train.dat", "train.xml", NULL};
        g_autofree char *definition = first_file(path, definitions);
        if (!definition && !p.notice) p.notice = g_strdup("Pasta sem train.dat/train.xml; compatibilidade não verificada.");
    } else {
        g_autoptr(GError) error = NULL;
        g_autofree char *text = read_text(path, &error);
        if (!text) { p.notice = g_strdup(error->message); return p; }
        g_autofree char *folder = g_path_get_dirname(path);
        g_autofree char *base = g_path_get_basename(path);
        char *ext = strrchr(base, '.');
        gboolean rw = ext && !g_ascii_strcasecmp(ext, ".rw");
        if (ext) *ext = 0;
        g_auto(GStrv) lines = g_strsplit(text, "\n", -1);
        gboolean section = FALSE, header = rw;
        GString *description = g_string_new(NULL);
        for (int i = 0; lines[i]; i++) {
            char *line = g_strstrip(lines[i]);
            if (header && (*line == '[' || *line == '$')) header = FALSE;
            if (header) { g_string_append_printf(description, "%s\n", line); continue; }
            if (!g_ascii_strcasecmp(line, "With Route") || !g_ascii_strcasecmp(line, "[Route]")) {
                section = TRUE; continue;
            }
            if (!g_ascii_strncasecmp(line, "With ", 5) || *line == '[') section = FALSE;
            char *command = line;
            if (!g_ascii_strncasecmp(command, "Route.", 6)) command += 6;
            else if (section && *command == '.') command++;
            else if (!section) continue;
            char *end = command;
            while (g_ascii_isalpha(*end)) end++;
            if (*end != ' ' && *end != '\t' && *end != '=') continue;
            *end++ = 0;
            char *value = g_strstrip(end);
            if (*value == '=') value = g_strstrip(value + 1);
            char *semicolon = strchr(value, ';');
            if (semicolon) *semicolon = 0;
            g_strstrip(value);
            if (!g_ascii_strcasecmp(command, "Comment")) {
                g_free(p.description); p.description = g_strdup(value);
            } else if (!g_ascii_strcasecmp(command, "Image")) {
                g_free(p.image); p.image = child(folder, value);
            }
        }
        if (!p.description && description->len) p.description = g_strdup(g_strstrip(description->str));
        g_string_free(description, TRUE);
        if (!p.description || !*p.description) { g_free(p.description); p.description = g_strdup(base); }
        if (!p.image) {
            const char *extensions[] = {".png", ".bmp", ".gif", ".tiff", ".tif", ".jpeg", ".jpg", NULL};
            for (int i = 0; extensions[i]; i++) {
                g_autofree char *name = g_strconcat(base, extensions[i], NULL);
                char *candidate = child(folder, name);
                if (g_file_test(candidate, G_FILE_TEST_IS_REGULAR)) { p.image = candidate; break; }
                g_free(candidate);
            }
        }
        p.notice = g_strdup("Prévia básica CSV/RW: includes, expressões, mapas e altitude requerem integração com o parser .NET.");
    }
    if (!p.description || !*p.description) {
        g_free(p.description); p.description = g_strdup("Sem descrição disponível.");
    }
    return p;
}
