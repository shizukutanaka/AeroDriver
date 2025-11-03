using AeroDriver.Core;
using System;

namespace AeroDriver.UI.Services
{
    public class UILogger : IUILogger
    {
        public event Action<string> MessageLogged;

        public void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}";
            MessageLogged?.Invoke(logEntry);
        }
    }
}
