using System;
using System.Runtime.InteropServices;

namespace PicMark
{
    /// <summary>
    /// Win7 兼容性检测工具。
    /// 在 Win7 无 DWM（经典主题）环境下，某些 WPF 特性需要降级处理。
    /// </summary>
    internal static class Win7Helper
    {
        private static bool? _isDwmEnabled;
        private static readonly bool? _isWin7OrOlder;

        static Win7Helper()
        {
            _isWin7OrOlder = Environment.OSVersion.Version.Major < 6
                         || (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor <= 1);
        }

        /// <summary>
        /// DWM（桌面窗口管理器）是否已启用。
        /// Win7 经典主题或远程桌面可能关闭 DWM，此时 WindowChrome/半透明效果不可用。
        /// </summary>
        public static bool IsDwmEnabled
        {
            get
            {
                if (_isDwmEnabled.HasValue) return _isDwmEnabled.Value;
                try
                {
                    int isEnabled = 0;
                    int hr = DwmIsCompositionEnabled(ref isEnabled);
                    _isDwmEnabled = (hr == 0 && isEnabled != 0);
                }
                catch
                {
                    _isDwmEnabled = false;
                }
                return _isDwmEnabled.Value;
            }
        }

        /// <summary>
        /// 是否为 Windows 7 或更早版本。
        /// </summary>
        public static bool IsWin7OrOlder => _isWin7OrOlder ?? true;

        /// <summary>
        /// WindowChrome 是否可用（需要 DWM 开启）。
        /// Win7 经典主题下应禁用 WindowChrome 的自定义标题栏。
        /// </summary>
        public static bool CanUseWindowChrome => !IsWin7OrOlder || IsDwmEnabled;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmIsCompositionEnabled(ref int pfEnabled);
    }
}
