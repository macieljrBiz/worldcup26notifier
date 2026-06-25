using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace WorldCupNotifier;

public static class DesktopNotificationService
{
    private const string AppId = "WorldCupNotifier.Desktop";
    private const string ShortcutName = "World Cup Notifier.lnk";

    public static void Initialize()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        SetCurrentProcessExplicitAppUserModelID(AppId);
        EnsureShortcut();
    }

    public static void ShowToast(string title, string message)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("Native toast notifications are only available on Windows.");
        }

        Initialize();

        var xml = new XmlDocument();
        xml.LoadXml($"""
            <toast>
              <visual>
                <binding template="ToastGeneric">
                  <text>{SecurityElement.Escape(title)}</text>
                  <text>{SecurityElement.Escape(message)}</text>
                </binding>
              </visual>
            </toast>
            """);

        ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml));
    }

    private static void EnsureShortcut()
    {
        var shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            ShortcutName);
        var executablePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Unable to resolve the application executable path.");

        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        IShellLinkW shortcut = (IShellLinkW)(object)new CShellLink();
        shortcut.SetPath(executablePath);
        shortcut.SetArguments("");

        var propertyStore = (IPropertyStore)shortcut;
        var key = AppUserModelIdPropertyKey;
        var appId = PropVariant.FromString(AppId);
        try
        {
            propertyStore.SetValue(ref key, ref appId);
            propertyStore.Commit();
        }
        finally
        {
            appId.Dispose();
        }

        var persistFile = (IPersistFile)shortcut;
        persistFile.Save(shortcutPath, true);
    }

    private static readonly PropertyKey AppUserModelIdPropertyKey = new()
    {
        FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        PropertyId = 5
    };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("Ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant propVariant);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class CShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(IntPtr pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(IntPtr pszName, int cchMaxName);
        void SetDescription(string pszName);
        void GetWorkingDirectory(IntPtr pszDir, int cchMaxPath);
        void SetWorkingDirectory(string pszDir);
        void GetArguments(IntPtr pszArgs, int cchMaxPath);
        void SetArguments(string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(IntPtr pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation(string pszIconPath, int iIcon);
        void SetRelativePath(string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath(string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, IntPtr pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant : IDisposable
    {
        private ushort _valueType;
        private ushort _reserved1;
        private ushort _reserved2;
        private ushort _reserved3;
        private IntPtr _value;
        private IntPtr _value2;

        public static PropVariant FromString(string value)
        {
            return new PropVariant
            {
                _valueType = 31,
                _value = Marshal.StringToCoTaskMemUni(value)
            };
        }

        public void Dispose()
        {
            PropVariantClear(ref this);
        }
    }
}
