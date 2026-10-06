using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace XDripWidget
{
    public static class DarkThemeHelper
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static void ApplyDarkTitleBar(Window window)
        {
            if (window == null) return;

            Action apply = () =>
            {
                var helper = new WindowInteropHelper(window);
                IntPtr hwnd = helper.Handle;
                if (hwnd == IntPtr.Zero) return;

                try
                {
                    int darkMode = 1;
                    int hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                    if (hr != 0)
                    {
                        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));
                    }
                }
                catch { }
            };

            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                apply();
            }
            else
            {
                window.SourceInitialized += (s, e) => apply();
            }
        }
    }

    public class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground
        {
            get { return Color.FromArgb(30, 41, 59); } // #1E293B
        }
        public override Color ImageMarginGradientBegin
        {
            get { return Color.FromArgb(30, 41, 59); }
        }
        public override Color ImageMarginGradientMiddle
        {
            get { return Color.FromArgb(30, 41, 59); }
        }
        public override Color ImageMarginGradientEnd
        {
            get { return Color.FromArgb(30, 41, 59); }
        }
        public override Color MenuBorder
        {
            get { return Color.FromArgb(51, 65, 85); } // #334155
        }
        public override Color MenuItemBorder
        {
            get { return Color.Transparent; }
        }
        public override Color MenuItemSelected
        {
            get { return Color.FromArgb(2, 132, 199); } // #0284C7 Action Blue
        }
        public override Color MenuItemSelectedGradientBegin
        {
            get { return Color.FromArgb(2, 132, 199); }
        }
        public override Color MenuItemSelectedGradientEnd
        {
            get { return Color.FromArgb(2, 132, 199); }
        }
        public override Color CheckBackground
        {
            get { return Color.FromArgb(15, 23, 42); } // #0F172A
        }
        public override Color CheckSelectedBackground
        {
            get { return Color.FromArgb(2, 132, 199); }
        }
        public override Color CheckPressedBackground
        {
            get { return Color.FromArgb(2, 132, 199); }
        }
        public override Color SeparatorDark
        {
            get { return Color.FromArgb(51, 65, 85); } // #334155
        }
        public override Color SeparatorLight
        {
            get { return Color.Transparent; }
        }
    }

    public class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected 
                ? Color.White 
                : Color.FromArgb(248, 250, 252);
            base.OnRenderItemText(e);
        }
    }
}
