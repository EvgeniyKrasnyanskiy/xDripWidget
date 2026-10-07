using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
            get { return Color.FromArgb(71, 85, 105); } // #475569
        }
        public override Color MenuItemSelected
        {
            get { return Color.FromArgb(51, 65, 85); } // #334155 Soft Dark Slate hover
        }
        public override Color MenuItemSelectedGradientBegin
        {
            get { return Color.FromArgb(51, 65, 85); }
        }
        public override Color MenuItemSelectedGradientEnd
        {
            get { return Color.FromArgb(51, 65, 85); }
        }
        public override Color CheckBackground
        {
            get { return Color.FromArgb(15, 23, 42); } // #0F172A
        }
        public override Color CheckSelectedBackground
        {
            get { return Color.FromArgb(51, 65, 85); } // #334155
        }
        public override Color CheckPressedBackground
        {
            get { return Color.FromArgb(51, 65, 85); } // #334155
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

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected)
            {
                var rect = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
                using (var brush = new SolidBrush(Color.FromArgb(51, 65, 85))) // #334155
                {
                    e.Graphics.FillRectangle(brush, rect);
                }
                using (var pen = new Pen(Color.FromArgb(71, 85, 105))) // #475569
                {
                    e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                }
            }
            else
            {
                var rect = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
                using (var brush = new SolidBrush(Color.FromArgb(30, 41, 59))) // #1E293B
                {
                    e.Graphics.FillRectangle(brush, rect);
                }
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var rect = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(56, 189, 248), 2)) // #38BDF8 Sky Blue
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                e.Graphics.DrawLines(pen, new System.Drawing.Point[]
                {
                    new System.Drawing.Point(rect.Left + 3, rect.Top + rect.Height / 2),
                    new System.Drawing.Point(rect.Left + rect.Width / 2 - 1, rect.Bottom - 4),
                    new System.Drawing.Point(rect.Right - 3, rect.Top + 3)
                });
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(Color.FromArgb(51, 65, 85))) // #334155
            {
                e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Color.FromArgb(51, 65, 85))) // #334155
            {
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected 
                ? Color.White 
                : Color.FromArgb(248, 250, 252);
            base.OnRenderItemText(e);
        }
    }
}
