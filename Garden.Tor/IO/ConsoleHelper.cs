using System;
using System.Runtime.InteropServices;

namespace Garden.Tor.IO
{
    //Enumerated control types for handlers
    internal enum ConsoleControlEvent
    {
        CtrlCEvent = 0,
        CtrlBreakEvent = 1,
        CtrlCloseEvent = 2,
        CtrlLogoffEvent = 5,
        CtrlShutdownEvent = 6
    }

    // This delegate type used as consoleHandler routine to SetConsoleControlHandler.
    internal delegate bool ConsoleHandlerRoutine(ConsoleControlEvent ctrlType);

    internal static class ConsoleHelper
    {
        public static bool IsConsoleApplication => GetConsoleWindow() != IntPtr.Zero;

        #region WinAPI
        // Hook console close for kill all Tors
        [DllImport("Kernel32")]
        public static extern bool SetConsoleCtrlHandler(ConsoleHandlerRoutine consoleHandler, bool add);

        // Detect if console app
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
        #endregion
    }
}
