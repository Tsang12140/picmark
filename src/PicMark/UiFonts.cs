using System;
using System.Windows.Media;

namespace PicMark
{
    internal static class UiFonts
    {
        private static readonly Uri ApplicationBaseUri = new Uri("pack://application:,,,/");

        public static FontFamily Family { get; } = new FontFamily(
            ApplicationBaseUri,
            "./Assets/Fonts/#PicMark PuHui UI, Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
    }
}
