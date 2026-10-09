using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace XDripWidget
{
    public class GlucoseCanvas : FrameworkElement
    {
        private CurrentGlucoseData _data;
        private List<HistoryPoint> _history;
        private string _errorMessage;
        private bool _isLoading;

        public bool IsCompact { get; set; }
        public bool IsAcrylic { get; set; }

        private DispatcherTimer _pulseTimer;
        private double _pulsePhase;
        private bool _isAlertActive;
        private Color _alertColor = Constants.ColorVeryLow;

        private readonly Typeface _typefaceBig = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private readonly Typeface _typefaceMed = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private readonly Typeface _typefaceSml = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        public GlucoseCanvas()
        {
            _history = new List<HistoryPoint>();
            _isLoading = true;
        }

        public void UpdateData(CurrentGlucoseData data, List<HistoryPoint> history)
        {
            _data = data;
            _history = history ?? new List<HistoryPoint>();
            _errorMessage = null;
            _isLoading = false;
            InvalidateVisual();
        }

        public void SetError(string error)
        {
            _errorMessage = error;
            _isLoading = false;
            InvalidateVisual();
        }

        public void SetAlert(bool isActive, Color? color = null)
        {
            _isAlertActive = isActive;
            if (color.HasValue)
            {
                _alertColor = color.Value;
            }

            if (isActive)
            {
                if (_pulseTimer == null)
                {
                    _pulseTimer = new DispatcherTimer();
                    _pulseTimer.Interval = TimeSpan.FromMilliseconds(50);
                    _pulseTimer.Tick += (s, e) =>
                    {
                        _pulsePhase += 0.15;
                        if (_pulsePhase > Math.PI * 2) _pulsePhase -= Math.PI * 2;
                        InvalidateVisual();
                    };
                }
                if (!_pulseTimer.IsEnabled)
                {
                    _pulseTimer.Start();
                }
            }
            else
            {
                if (_pulseTimer != null && _pulseTimer.IsEnabled)
                {
                    _pulseTimer.Stop();
                }
                _pulsePhase = 0;
                InvalidateVisual();
            }
        }

        private Pen GetBorderPen()
        {
            if (_isAlertActive)
            {
                double sinVal = 0.5 + 0.5 * Math.Sin(_pulsePhase);
                byte alpha = (byte)(80 + 175 * sinVal);
                double thickness = 1.0 + 1.8 * sinVal;
                var alertBrush = new SolidColorBrush(Color.FromArgb(alpha, _alertColor.R, _alertColor.G, _alertColor.B));
                return new Pen(alertBrush, thickness);
            }
            return new Pen(new SolidColorBrush(Constants.ColorBorder), 1.0);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            // Compact Pill Mode rendering
            if (IsCompact || h <= 45)
            {
                RenderCompactPill(dc, w, h);
                return;
            }

            // 1. Background rounded rectangle
            var bgBrush = IsAcrylic ? new SolidColorBrush(Color.FromArgb(210, 15, 23, 42)) : new SolidColorBrush(Constants.ColorBg);
            var borderPen = GetBorderPen();
            dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(0.5, 0.5, w - 1.0, h - 1.0), 14, 14);

            // 2. Loading state
            if (_isLoading && _data == null && string.IsNullOrEmpty(_errorMessage))
            {
                DrawTopLine(dc, Constants.ColorGray, w);
                DrawCenteredText(dc, "Загрузка…", 13, Constants.ColorSub, w, h / 2 - 8);
                return;
            }

            // 3. Error state
            if (!string.IsNullOrEmpty(_errorMessage) || _data == null)
            {
                DrawTopLine(dc, Constants.ColorVeryLow, w);
                DrawCenteredText(dc, "📡❌", 18, Constants.ColorVeryLow, w, 22);
                DrawCenteredText(dc, _errorMessage ?? "Нет связи с сервером", 11, Constants.ColorVeryLow, w, 50);
                DrawCenteredText(dc, "Проверьте интернет или адрес", 9, Constants.ColorGray, w, 76);
                return;
            }

            // 4. Normal state
            Color statusColor = Constants.GetGlucoseColor(_data.Mmol, _data.IsStale);

            // Top accent line
            DrawTopLine(dc, statusColor, w);

            // Glucose + Arrow (Right aligned, e.g. "5.9 ↗")
            string arrow = Constants.GetTrendArrow(_data.Direction);
            string glucoseText = string.Format(CultureInfo.InvariantCulture, "{0:F1} {1}", _data.Mmol, arrow);
            var glucoseFt = CreateFormattedText(glucoseText, _typefaceBig, 32, statusColor);
            dc.DrawText(glucoseFt, new Point(w - glucoseFt.Width - 10, 8));

            // Delta (Left aligned, e.g. "Δ +0.4")
            string deltaIcon = _data.MinutesAgo > 1 ? "🔄" : "Δ";
            string deltaText = string.Format("{0} {1}", deltaIcon, _data.Delta);
            var deltaFt = CreateFormattedText(deltaText, _typefaceMed, 14, Constants.ColorSub);
            dc.DrawText(deltaFt, new Point(10, 16));

            // Battery bar
            DrawBatteryBar(dc, _data.Battery, _data.IsStale);

            // Time ago (e.g. "1 м назад")
            string timeText = Constants.FormatTimeAgo(_data.MinutesAgo);
            Color timeColor = _data.IsStale ? Constants.ColorGray : Constants.ColorSub;
            var timeFt = CreateFormattedText(timeText, _typefaceSml, 11, timeColor);
            dc.DrawText(timeFt, new Point(w - timeFt.Width - 10, 52));

            // 4-Hour Sparkline graph
            DrawSparkline(dc, w);
        }

        private void DrawTopLine(DrawingContext dc, Color color, double w)
        {
            var pen = new Pen(new SolidColorBrush(color), 2.0);
            dc.DrawLine(pen, new Point(14, 2), new Point(w - 14, 2));
        }

        private void DrawBatteryBar(DrawingContext dc, int pct, bool stale)
        {
            const double barX = 10;
            const double barY = 53;
            const double barW = 34;
            const double barH = 13;
            const double capW = 3;
            const double capH = 6;

            Color bColor = Constants.GetBatteryColor(pct, stale);
            var borderPen = new Pen(new SolidColorBrush(Constants.ColorBorder), 1.0);

            // Body outline
            dc.DrawRoundedRectangle(null, borderPen, new Rect(barX, barY, barW, barH), 2, 2);

            // Body fill
            if (pct > 0)
            {
                double fillW = Math.Max(2, (barW - 4) * Math.Min(pct, 100) / 100.0);
                dc.DrawRoundedRectangle(new SolidColorBrush(bColor), null, new Rect(barX + 2, barY + 2, fillW, barH - 4), 1, 1);
            }

            // Cap
            double capX = barX + barW + 1;
            double capY = barY + (barH - capH) / 2.0;
            dc.DrawRoundedRectangle(new SolidColorBrush(Constants.ColorBorder), null, new Rect(capX, capY, capW, capH), 1, 1);

            // Label
            string label = pct >= 0 ? string.Format("{0}%", pct) : "—";
            var pctFt = CreateFormattedText(label, _typefaceSml, 10, bColor);
            dc.DrawText(pctFt, new Point(capX + capW + 4, barY));
        }

        private void DrawSparkline(DrawingContext dc, double w)
        {
            if (_history == null || _history.Count < 2) return;

            const double gx = 10;
            const double gy = 74;
            double gw = Math.Max(100, w - 20);
            const double gh = 42;

            double minVal = 2.5;
            double maxVal = 14.0;
            foreach (var p in _history)
            {
                if (p.Mmol < minVal) minVal = Math.Max(1.5, p.Mmol - 0.5);
                if (p.Mmol > maxVal) maxVal = Math.Min(22.0, p.Mmol + 1.0);
            }

            double valRange = Math.Max(0.1, maxVal - minVal);
            Func<double, double> valToY = v => gy + gh - ((v - minVal) / valRange * gh);

            double yLo = valToY(3.9);
            double yHi = valToY(7.8);

            // TIR Target Corridor (3.9 - 7.8 mmol/L)
            double topCorridor = Math.Max(gy, Math.Min(gy + gh, yHi));
            double botCorridor = Math.Max(gy, Math.Min(gy + gh, yLo));
            if (botCorridor > topCorridor)
            {
                var corridorBrush = new SolidColorBrush(Color.FromArgb(30, 74, 222, 128)); // #4ADE80
                dc.DrawRectangle(corridorBrush, null, new Rect(gx, topCorridor, gw, botCorridor - topCorridor));
            }

            // Target dashed lines
            var corridorPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 74, 222, 128)), 1.0);
            corridorPen.DashStyle = DashStyles.Dash;
            if (yLo >= gy && yLo <= gy + gh) dc.DrawLine(corridorPen, new Point(gx, yLo), new Point(gx + gw, yLo));
            if (yHi >= gy && yHi <= gy + gh) dc.DrawLine(corridorPen, new Point(gx, yHi), new Point(gx + gw, yHi));

            long tStart = _history[0].Timestamp;
            long tEnd = _history[_history.Count - 1].Timestamp;
            long tSpan = Math.Max(1, tEnd - tStart);

            var points = new List<Tuple<Point, Color>>();
            foreach (var h in _history)
            {
                double px = gx + ((double)(h.Timestamp - tStart) / tSpan * gw);
                double py = valToY(h.Mmol);
                Color c = Constants.GetGlucoseColor(h.Mmol, false);
                points.Add(Tuple.Create(new Point(px, py), c));
            }

            // Connecting lines
            var linePen = new Pen(new SolidColorBrush(Color.FromArgb(120, 74, 222, 128)), 1.4);
            for (int i = 0; i < points.Count - 1; i++)
            {
                dc.DrawLine(linePen, points[i].Item1, points[i + 1].Item1);
            }

            // Dots
            foreach (var pt in points)
            {
                var dotBrush = new SolidColorBrush(pt.Item2);
                dc.DrawEllipse(dotBrush, null, pt.Item1, 2.5, 2.5);
            }

            // Time axis line
            double axisY = gy + gh + 4;
            var axisPen = new Pen(new SolidColorBrush(Color.FromArgb(100, 51, 65, 85)), 1.0);
            dc.DrawLine(axisPen, new Point(gx, axisY), new Point(gx + gw, axisY));

            // Tick marks
            dc.DrawLine(axisPen, new Point(gx, axisY), new Point(gx, axisY + 2));
            dc.DrawLine(axisPen, new Point(gx + gw / 2.0, axisY), new Point(gx + gw / 2.0, axisY + 2));
            dc.DrawLine(axisPen, new Point(gx + gw, axisY), new Point(gx + gw, axisY + 2));

            // Time labels
            var timeBrush = new SolidColorBrush(Color.FromArgb(160, 148, 163, 184));
            var ftStart = CreateFormattedText("-4ч", _typefaceSml, 9.5, timeBrush);
            dc.DrawText(ftStart, new Point(gx, axisY + 2));

            var ftMid = CreateFormattedText("-2ч", _typefaceSml, 9.5, timeBrush);
            dc.DrawText(ftMid, new Point(gx + gw / 2.0 - ftMid.Width / 2.0, axisY + 2));

            var ftEnd = CreateFormattedText("сейчас", _typefaceSml, 9.5, timeBrush);
            dc.DrawText(ftEnd, new Point(gx + gw - ftEnd.Width, axisY + 2));
        }

        private void DrawCenteredText(DrawingContext dc, string text, double size, Color color, double w, double y)
        {
            var ft = CreateFormattedText(text, _typefaceMed, size, color);
            dc.DrawText(ft, new Point((w - ft.Width) / 2.0, y));
        }

        private FormattedText CreateFormattedText(string text, Typeface typeface, double size, Color color)
        {
            return new FormattedText(
                text ?? "",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                size,
                new SolidColorBrush(color)
            );
        }

        private FormattedText CreateFormattedText(string text, Typeface typeface, double size, SolidColorBrush brush)
        {
            return new FormattedText(
                text ?? "",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                size,
                brush
            );
        }

        private void RenderCompactPill(DrawingContext dc, double w, double h)
        {
            double radius = h / 2.0;
            var pillBgBrush = IsAcrylic ? new SolidColorBrush(Color.FromArgb(200, 15, 23, 42)) : new SolidColorBrush(Constants.ColorBg);
            var pillBorderPen = GetBorderPen();
            dc.DrawRoundedRectangle(pillBgBrush, pillBorderPen, new Rect(0.5, 0.5, w - 1.0, h - 1.0), radius, radius);

            Color pillStatusColor = _data != null ? Constants.GetGlucoseColor(_data.Mmol, _data.IsStale) : Constants.ColorGray;
            dc.DrawEllipse(new SolidColorBrush(pillStatusColor), null, new Point(11.5, h / 2.0), 3.5, 3.5);

            if (_isLoading && _data == null && string.IsNullOrEmpty(_errorMessage))
            {
                DrawCenteredText(dc, "Загрузка…", 11, Constants.ColorSub, w, (h - 14) / 2.0);
                return;
            }

            if (!string.IsNullOrEmpty(_errorMessage) || _data == null)
            {
                DrawCenteredText(dc, "📡 Ошибка", 11, Constants.ColorVeryLow, w, (h - 14) / 2.0);
                return;
            }

            string arrow = Constants.GetTrendArrow(_data.Direction);
            string glucoseText = string.Format(CultureInfo.InvariantCulture, "{0:F1} {1}", _data.Mmol, arrow);
            var glucoseFt = CreateFormattedText(glucoseText, _typefaceBig, 15.5, pillStatusColor);

            string deltaText = string.Format("({0})", _data.Delta);
            var deltaFt = CreateFormattedText(deltaText, _typefaceMed, 10.5, Constants.ColorSub);

            double textStartX = 18.5;
            dc.DrawText(glucoseFt, new Point(textStartX, (h - glucoseFt.Height) / 2.0));
            dc.DrawText(deltaFt, new Point(textStartX + glucoseFt.Width + 3.0, (h - deltaFt.Height) / 2.0 + 1));
            double deltaEndX = textStartX + glucoseFt.Width + 3.0 + deltaFt.Width;

            // Battery display on the right
            if (_data.Battery >= 0)
            {
                int pct = _data.Battery;
                Color bColor = (pct <= 5) ? Constants.ColorVeryLow : Constants.GetBatteryColor(pct, _data.IsStale);
                var borderPen = new Pen(new SolidColorBrush(Constants.ColorBorder), 1.0);

                string bText = string.Format("{0}%", pct);
                var pctFt = CreateFormattedText(bText, _typefaceSml, 9.0, bColor);

                const double iconWidth = 9.5;
                const double iconHeight = 6.0;
                const double capW = 1.5;
                const double capH = 3.0;
                const double textGap = 2.0;

                double totalBW = iconWidth + capW + textGap + pctFt.Width;
                double rightEdge = w - 8.0;
                double bStartX = rightEdge - totalBW;
                double spaceToDelta = bStartX - deltaEndX;

                if (spaceToDelta >= 3.5)
                {
                    // Draw horizontal battery icon on the left, percentage on the right
                    double bStartY = (h - iconHeight) / 2.0;

                    // Battery body
                    dc.DrawRoundedRectangle(null, borderPen, new Rect(bStartX, bStartY, iconWidth, iconHeight), 1.0, 1.0);

                    // Battery fill
                    if (pct > 0)
                    {
                        double innerMaxW = iconWidth - 2.0;
                        double innerH = iconHeight - 2.0;
                        double fillW = Math.Max(1.0, innerMaxW * Math.Min(pct, 100) / 100.0);
                        dc.DrawRoundedRectangle(new SolidColorBrush(bColor), null, new Rect(bStartX + 1.0, bStartY + 1.0, fillW, innerH), 0.5, 0.5);
                    }

                    // Battery cap (on the right side of the icon)
                    double capX = bStartX + iconWidth;
                    double capY = bStartY + (iconHeight - capH) / 2.0;
                    dc.DrawRoundedRectangle(new SolidColorBrush(Constants.ColorBorder), null, new Rect(capX, capY, capW, capH), 0.5, 0.5);

                    // Percentage text
                    double textX = capX + capW + textGap;
                    dc.DrawText(pctFt, new Point(textX, (h - pctFt.Height) / 2.0));
                }
                else
                {
                    // Space constrained: draw horizontal battery icon centered in remaining space
                    const double soloWidth = 11.0;
                    const double soloHeight = 6.5;
                    const double soloCapW = 1.5;
                    const double soloCapH = 3.5;
                    double totalSoloW = soloWidth + soloCapW;

                    double availCenter = (deltaEndX + rightEdge) / 2.0;
                    double soloStartX = Math.Max(deltaEndX + 2.0, availCenter - (totalSoloW / 2.0));
                    double soloStartY = (h - soloHeight) / 2.0;

                    // Battery body
                    dc.DrawRoundedRectangle(null, borderPen, new Rect(soloStartX, soloStartY, soloWidth, soloHeight), 1.0, 1.0);

                    // Battery fill
                    if (pct > 0)
                    {
                        double innerMaxW = soloWidth - 2.0;
                        double innerH = soloHeight - 2.0;
                        double fillW = Math.Max(1.0, innerMaxW * Math.Min(pct, 100) / 100.0);
                        dc.DrawRoundedRectangle(new SolidColorBrush(bColor), null, new Rect(soloStartX + 1.0, soloStartY + 1.0, fillW, innerH), 0.5, 0.5);
                    }

                    // Battery cap
                    double capX = soloStartX + soloWidth;
                    double capY = soloStartY + (soloHeight - soloCapH) / 2.0;
                    dc.DrawRoundedRectangle(new SolidColorBrush(Constants.ColorBorder), null, new Rect(capX, capY, soloCapW, soloCapH), 0.5, 0.5);
                }
            }
        }
    }
}
