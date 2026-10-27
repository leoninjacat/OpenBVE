#if WINDOWS
using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Windows.Forms;
using OpenBveApi.Interface;
using LibRender2;
namespace OpenBve.Native;
internal static class ClassicUiSupport
{
    private static bool listing;
    internal static void ListLanguages(ComboBox combo)
    {
        listing = true;
        combo.DataSource = new BindingSource(Translations.AvailableLanguages, null);
        combo.DisplayMember = "Value"; combo.ValueMember = "Key";
        int index = Translations.AvailableLanguages.Keys.ToList().IndexOf(Translations.CurrentLanguageCode);
        if(index >= 0) combo.SelectedIndex = index;
        listing = false;
    }
    internal static bool SelectedLanguage(string flags, ref string code, ComboBox combo, out string image)
    {
        image = Path.Combine(flags, "unknown.png");
        if(combo.SelectedItem is not KeyValuePair<string, NewLanguage> item) return false;
        if(!listing) { Translations.CurrentLanguageCode = item.Value.LanguageCode; code = item.Value.LanguageCode; }
        string flag = Path.Combine(flags, item.Value.FlagFile);
        if(File.Exists(flag)) image = flag;
        return true;
    }
    internal static void ListCursors(ComboBox combo)
    {
        combo.Items.Clear();
        foreach(var cursor in AvailableCursors.CursorList) { int i = combo.Items.Add(cursor); if(cursor.FileName == Interface.CurrentOptions.CursorFileName) combo.SelectedIndex = i; }
    }
    internal static void SelectedCursor(ComboBox combo, PictureBox picture)
    {
        if(combo.SelectedItem is not MouseCursor cursor) return;
        Interface.CurrentOptions.CursorFileName = cursor.FileName;
        AvailableCursors.CurrentCursor = cursor.MyCursor; AvailableCursors.CurrentCursorPlus = cursor.MyCursorPlus; AvailableCursors.CurrentCursorMinus = cursor.MyCursorMinus;
        picture.Image?.Dispose(); picture.Image = WinFormsImages.Convert(cursor.Image);
    }
    internal static void CallPluginConfig(IWin32Window owner, int index)
    {
        if(index < 0 || index >= InputDevicePlugin.AvailablePlugins.Count) return;
        var plugin = InputDevicePlugin.AvailablePlugins[index];
        var method = plugin.GetType().GetMethod("Config", new[] { typeof(IWin32Window) });
        if(method == null) throw new NotSupportedException("Este plugin não oferece configuração Windows compatível.");
        method.Invoke(plugin, new object[] { owner });
    }
}
#endif
