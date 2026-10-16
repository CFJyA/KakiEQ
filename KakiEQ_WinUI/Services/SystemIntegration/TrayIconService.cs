using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace KakiEQ_WinUI.Services.SystemIntegration
{
    public class TrayIconService : IDisposable
    {
        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 101;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;

        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;

        private const uint TPM_LEFTBUTTON = 0x0000;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;

        private const int CMD_OPEN = 1001;
        private const int CMD_PLAYPAUSE = 1002;
        private const int CMD_NEXT = 1003;
        private const int CMD_PREV = 1004;
        private const int CMD_EXIT = 1005;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lpTPMParams);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly IntPtr _hwnd;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly SubclassProc _subclassProc;
        private NOTIFYICONDATA _nid;
        private bool _isCreated = false;
        private IntPtr _hIcon = IntPtr.Zero;

        public event Action? OpenRequested;
        public event Action? PlayPauseRequested;
        public event Action? NextRequested;
        public event Action? PreviousRequested;
        public event Action? ExitRequested;

        public TrayIconService(IntPtr hwnd)
        {
            _hwnd = hwnd;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _subclassProc = WndProcSubclass;

            SetWindowSubclass(_hwnd, _subclassProc, (UIntPtr)101, UIntPtr.Zero);
            LoadAppIcon();
        }

        private void LoadAppIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Kaki_Oficial_Blanco.ico");
                if (File.Exists(iconPath))
                {
                    _hIcon = LoadImage(IntPtr.Zero, iconPath, 1 /* IMAGE_ICON */, 16, 16, 0x00000010 /* LR_LOADFROMFILE */);
                }
            }
            catch { }
        }

        public void EnsureCreated()
        {
            if (_isCreated) return;

            _nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = "Kaki EQ - Reproductor de Música"
            };

            _isCreated = Shell_NotifyIcon(NIM_ADD, ref _nid);
        }

        public void UpdateTooltip(string text)
        {
            if (!_isCreated) return;
            string tip = string.IsNullOrWhiteSpace(text) ? "Kaki EQ" : text;
            if (tip.Length > 120) tip = tip.Substring(0, 117) + "...";

            _nid.szTip = tip;
            _nid.uFlags = NIF_TIP;
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        }

        public void Remove()
        {
            if (_isCreated)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _nid);
                _isCreated = false;
            }
        }

        private IntPtr WndProcSubclass(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
        {
            if (uMsg == WM_TRAYICON)
            {
                int msg = (int)lParam;
                if (msg == WM_LBUTTONDBLCLK || msg == WM_LBUTTONUP)
                {
                    _dispatcherQueue.TryEnqueue(() => OpenRequested?.Invoke());
                }
                else if (msg == WM_RBUTTONUP)
                {
                    ShowContextMenu();
                }
                return IntPtr.Zero;
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private void ShowContextMenu()
        {
            GetCursorPos(out POINT pt);
            IntPtr hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            AppendMenu(hMenu, MF_STRING, (UIntPtr)CMD_OPEN, "Abrir Kaki EQ");
            AppendMenu(hMenu, MF_STRING, (UIntPtr)CMD_PLAYPAUSE, "Reproducir / Pausar");
            AppendMenu(hMenu, MF_STRING, (UIntPtr)CMD_NEXT, "Siguiente");
            AppendMenu(hMenu, MF_STRING, (UIntPtr)CMD_PREV, "Anterior");
            AppendMenu(hMenu, MF_SEPARATOR, UIntPtr.Zero, string.Empty);
            AppendMenu(hMenu, MF_STRING, (UIntPtr)CMD_EXIT, "Salir de Kaki EQ");

            SetForegroundWindow(_hwnd);
            int cmd = TrackPopupMenuEx(hMenu, TPM_LEFTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, _hwnd, IntPtr.Zero);
            DestroyMenu(hMenu);

            _dispatcherQueue.TryEnqueue(() =>
            {
                switch (cmd)
                {
                    case CMD_OPEN:
                        OpenRequested?.Invoke();
                        break;
                    case CMD_PLAYPAUSE:
                        PlayPauseRequested?.Invoke();
                        break;
                    case CMD_NEXT:
                        NextRequested?.Invoke();
                        break;
                    case CMD_PREV:
                        PreviousRequested?.Invoke();
                        break;
                    case CMD_EXIT:
                        ExitRequested?.Invoke();
                        break;
                }
            });
        }

        public void Dispose()
        {
            Remove();
            RemoveWindowSubclass(_hwnd, _subclassProc, (UIntPtr)101);
            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
        }
    }
}
