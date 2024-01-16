﻿using System;
using System.Threading;
using WindowsInput.Events;
using WindowsInput.Events.Sources;

namespace BetterJoy
{
    public class InputCapture : IDisposable
    {
        private static readonly Lazy<InputCapture> _instance = new(() => new InputCapture());
        public static InputCapture Global => _instance.Value;

        private readonly IKeyboardEventSource Keyboard;
        private readonly IMouseEventSource Mouse;

        private int _NbKeyboardEvents = 0;
        private int _NbMouseEvents = 0;

        bool disposed = false;

        public InputCapture()
        {
            Keyboard = WindowsInput.Capture.Global.KeyboardAsync(false);
            Mouse = WindowsInput.Capture.Global.MouseAsync(false);
        }

        public void RegisterEvent(EventHandler<EventSourceEventArgs<KeyboardEvent>> ev)
        {
            Keyboard.KeyEvent += ev;
            KeyboardEventCountChange(true);
        }

        public void UnregisterEvent(EventHandler<EventSourceEventArgs<KeyboardEvent>> ev)
        {
            Keyboard.KeyEvent -= ev;
            KeyboardEventCountChange(false);
        }

        public void RegisterEvent(EventHandler<EventSourceEventArgs<MouseEvent>> ev)
        {
            Mouse.MouseEvent += ev;
            MouseEventCountChange(true);
        }

        public void UnregisterEvent(EventHandler<EventSourceEventArgs<MouseEvent>> ev)
        {
            Mouse.MouseEvent -= ev;
            MouseEventCountChange(false);
        }

        private void KeyboardEventCountChange(bool newEvent)
        {
            int count = newEvent ? Interlocked.Increment(ref _NbKeyboardEvents) : Interlocked.Decrement(ref _NbKeyboardEvents);
            
            // The property calls invoke, so only do it if necessary
            if (count == 0)
            {
                Keyboard.Enabled = false;
            }
            else if (count == 1)
            {
                Keyboard.Enabled = true;
            }
        }

        private void MouseEventCountChange(bool newEvent)
        {
            int count = newEvent ? Interlocked.Increment(ref _NbMouseEvents) : Interlocked.Decrement(ref _NbMouseEvents);
            
            // The property calls invoke, so only do it if necessary
            if (count == 0)
            {
                Mouse.Enabled = false;
            }
            else if (count == 1)
            {
                Mouse.Enabled = true;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Keyboard.Dispose();
            Mouse.Dispose();
            disposed = true;
        }
    }
}
