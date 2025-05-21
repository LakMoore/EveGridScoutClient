using System.Runtime.InteropServices;

namespace GridScout2
{
    internal static partial class WinApi
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            public int x;
            public int y;

            public Point(int x, int y)
            {
                this.x = x;
                this.y = y;
            }
        }

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetProcessDPIAware();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        /*
        https://stackoverflow.com/questions/19867402/how-can-i-use-enumwindows-to-find-windows-with-a-specific-caption-title/20276701#20276701
        https://stackoverflow.com/questions/295996/is-the-order-in-which-handles-are-returned-by-enumwindows-meaningful/296014#296014
        */
        internal static System.Collections.Generic.IReadOnlyList<IntPtr> ListWindowHandlesInZOrder()
        {
            IntPtr found = IntPtr.Zero;
            System.Collections.Generic.List<IntPtr> windowHandles = new System.Collections.Generic.List<IntPtr>();

            EnumWindows(delegate (IntPtr wnd, IntPtr param)
            {
                windowHandles.Add(wnd);

                // return true here so that we iterate all windows
                return true;
            }, IntPtr.Zero);

            return windowHandles;
        }

        [LibraryImport("user32.dll")]
        internal static partial IntPtr ShowWindow(IntPtr hWnd, int nCmdShow);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetWindowRect(IntPtr hWnd, ref Rect rect);

        [LibraryImport("user32.dll", SetLastError = false)]
        internal static partial IntPtr GetDesktopWindow();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetCursorPos(int x, int y);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
