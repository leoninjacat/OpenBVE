using System;
using System.Collections.Generic;
using OpenTK.Input;

namespace RouteViewer;

// OpenTK's raw-input snapshot can lose keyboard state when its window is
// reparented into another process. Retain the window's key messages and use
// native held-key state on Windows; keep the existing OpenTK backend on Linux.
internal sealed class ViewerKeyboard
{
    readonly HashSet<Key> pressed=new();
    KeyboardState snapshot;
    bool focused;
    internal bool Press(Key key)=>pressed.Add(key);
    internal void Release(Key key)=>pressed.Remove(key);
    internal void Capture(bool hasFocus)
    {
        focused=hasFocus;
        if(!hasFocus)pressed.Clear();
        if(!OperatingSystem.IsWindows())snapshot=Keyboard.GetState();
    }
    internal bool IsKeyDown(Key key)
    {
        if(!focused)return false;
        if(!OperatingSystem.IsWindows())return snapshot.IsKeyDown(key);
        int vk=key switch {
            Key.W=>0x57,Key.A=>0x41,Key.S=>0x53,Key.D=>0x44,Key.Q=>0x51,Key.E=>0x45,
            Key.Left=>0x25,Key.Up=>0x26,Key.Right=>0x27,Key.Down=>0x28,
            Key.ShiftLeft=>0xA0,Key.ShiftRight=>0xA1,Key.ControlLeft=>0xA2,Key.ControlRight=>0xA3,
            Key.Keypad2=>0x62,Key.Keypad3=>0x63,Key.Keypad4=>0x64,
            Key.Keypad6=>0x66,Key.Keypad8=>0x68,Key.Keypad9=>0x69,_=>0
        };
        return pressed.Contains(key)||(vk!=0&&WindowIdentity.KeyDown(vk));
    }
}
