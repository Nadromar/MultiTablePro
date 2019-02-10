﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Interop;

namespace BetterPokerTableManager
{
    internal class HotKeyHandler
    {
        [DllImport("user32", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public HotKeyHandler(Config activeConfig)
        {
            ActiveConfig = activeConfig;
            ActiveConfig.PropertyChanged += ActiveConfig_PropertyChanged;
        }

        private int lastId = 0;
        public const int WM_HOTKEY = 0x0312; // Definition for hotkey MSG
        private static List<Tuple<int, HotKey, IntPtr>> idMemory = new List<Tuple<int, HotKey, IntPtr>>();
        private HotKey _asideHotkey;

        public Config ActiveConfig { get; set; }
        public HotKey AsideHotkey
        {
            get { return new HotKey(Keys.B); }
            set
            {
                // unhook old hotkey
                // hook new one
                _asideHotkey = value;
            }
        }

        public void RegisterHotKey(HotKey hotKey, IntPtr windowHandle)
        {
            int newId = ++lastId;
            RegisterHotKey(windowHandle, newId, (uint)hotKey.Modifiers, (uint)hotKey.Key);
            ComponentDispatcher.ThreadFilterMessage += new ThreadMessageEventHandler(HotkeyPressed);

            // Remember the registered hotkey
            idMemory.Add(new Tuple<int, HotKey, IntPtr>(newId, hotKey, windowHandle));
        }

        private void HotkeyPressed(ref MSG m, ref bool handled)
        {
            if (m.message == WM_HOTKEY)
            {
                System.Diagnostics.Debug.WriteLine("Hotkey pressed");


                // Hotkey press was irrelevant for us. Redirect it to foreground window
                SendKeys.SendWait(AsideHotkey.ToString());
            }
        }

        public void UnregisterHotKey(HotKey hotKey, IntPtr windowHandle)
        {
            int id = 0;
            try
            {
                // Find the ID of the hotkey
                id = idMemory.Where(t => t.Item2.Equals(hotKey) && t.Item3 == windowHandle).First().Item1;
            }
            catch
            {
                Logger.Log("Attempting to unregister a hotkey that was never registered.", Logger.Status.Warning);
                return;
            }

            // Unregister the hotkey
            if (UnregisterHotKey(windowHandle, id))
                Logger.Log($"Unregistered hotkey {hotKey} from table {windowHandle}");
            else Logger.Log($"Failed to unregister hotkey {hotKey} from table {windowHandle}");

            // Remove the hotkey from idMemory
            idMemory.RemoveAll(h => h.Item2.Equals(hotKey) && h.Item3 == windowHandle);
        }

        // Used to register and unregister hotkeys to all known tables when a hotkey is changed
        private void ActiveConfig_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // if property was AsideHotkey
            // else if some other hotkey
        }
    }
}
