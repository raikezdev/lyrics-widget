using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LyricsWidget
{
    public enum EdgeLightingMode
    {
        AmbientAura,
        SoundWaveRibbon,
        CornerBassFlare,
        CyberNeonFrame,
        Off
    }

    public partial class DesktopEdgeLightingWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private EdgeLightingMode _mode = EdgeLightingMode.AmbientAura;
        private Color _currentColor = Color.FromRgb(0x00, 0xF5, 0xD4);

        private float _smoothPeak = 0f;
        private readonly float[] _smoothBands = new float[16];
        private double _wavePhase = 0;

        public EdgeLightingMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                ApplyModeVisibility();
            }
        }

        public DesktopEdgeLightingWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            ApplyModeVisibility();
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
        }

        public void CycleMode()
        {
            int next = ((int)_mode + 1) % 5;
            Mode = (EdgeLightingMode)next;
        }

        private void ApplyModeVisibility()
        {
            if (_mode == EdgeLightingMode.Off)
            {
                RootOverlayGrid.Visibility = Visibility.Collapsed;
                return;
            }

            RootOverlayGrid.Visibility = Visibility.Visible;

            switch (_mode)
            {
                case EdgeLightingMode.AmbientAura:
                    TopEdgeBorder.Visibility = Visibility.Visible;
                    BottomEdgeBorder.Visibility = Visibility.Visible;
                    LeftEdgeBorder.Visibility = Visibility.Visible;
                    RightEdgeBorder.Visibility = Visibility.Visible;
                    TopLeftBloom.Visibility = Visibility.Visible;
                    TopRightBloom.Visibility = Visibility.Visible;
                    BottomLeftBloom.Visibility = Visibility.Visible;
                    BottomRightBloom.Visibility = Visibility.Visible;
                    WaveCanvas.Visibility = Visibility.Collapsed;
                    break;

                case EdgeLightingMode.SoundWaveRibbon:
                    TopEdgeBorder.Visibility = Visibility.Visible;
                    BottomEdgeBorder.Visibility = Visibility.Visible;
                    LeftEdgeBorder.Visibility = Visibility.Visible;
                    RightEdgeBorder.Visibility = Visibility.Visible;
                    TopLeftBloom.Visibility = Visibility.Collapsed;
                    TopRightBloom.Visibility = Visibility.Collapsed;
                    BottomLeftBloom.Visibility = Visibility.Visible;
                    BottomRightBloom.Visibility = Visibility.Visible;
                    WaveCanvas.Visibility = Visibility.Visible;
                    break;

                case EdgeLightingMode.CornerBassFlare:
                    TopEdgeBorder.Visibility = Visibility.Collapsed;
                    BottomEdgeBorder.Visibility = Visibility.Collapsed;
                    LeftEdgeBorder.Visibility = Visibility.Collapsed;
                    RightEdgeBorder.Visibility = Visibility.Collapsed;
                    TopLeftBloom.Visibility = Visibility.Visible;
                    TopRightBloom.Visibility = Visibility.Visible;
                    BottomLeftBloom.Visibility = Visibility.Visible;
                    BottomRightBloom.Visibility = Visibility.Visible;
                    WaveCanvas.Visibility = Visibility.Collapsed;
                    break;

                case EdgeLightingMode.CyberNeonFrame:
                    TopEdgeBorder.Visibility = Visibility.Visible;
                    BottomEdgeBorder.Visibility = Visibility.Visible;
                    LeftEdgeBorder.Visibility = Visibility.Visible;
                    RightEdgeBorder.Visibility = Visibility.Visible;
                    TopLeftBloom.Visibility = Visibility.Collapsed;
                    TopRightBloom.Visibility = Visibility.Collapsed;
                    BottomLeftBloom.Visibility = Visibility.Collapsed;
                    BottomRightBloom.Visibility = Visibility.Collapsed;
                    WaveCanvas.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        public void UpdateAudio(float[] bands, float rawPeak, Color themeColor)
        {
            if (_mode == EdgeLightingMode.Off) return;

            _smoothPeak = (_smoothPeak * 0.72f) + (rawPeak * 0.28f);
            for (int i = 0; i < Math.Min(bands.Length, _smoothBands.Length); i++)
            {
                _smoothBands[i] = (_smoothBands[i] * 0.70f) + (bands[i] * 0.30f);
            }

            float bass = Math.Clamp((_smoothBands[0] + _smoothBands[1] + _smoothBands[2]) / 3f, 0f, 1f);
            float mids = Math.Clamp((_smoothBands[4] + _smoothBands[5] + _smoothBands[6]) / 3f, 0f, 1f);
            float highs = Math.Clamp((_smoothBands[10] + _smoothBands[11] + _smoothBands[12]) / 3f, 0f, 1f);

            _currentColor = themeColor;
            byte r = themeColor.R;
            byte g = themeColor.G;
            byte b = themeColor.B;

            if (r < 40 && g < 40 && b < 40)
            {
                r = 0x00; g = 0xF5; b = 0xD4;
            }

            double baseAlpha = (_mode == EdgeLightingMode.CyberNeonFrame) ? 0.90 : 0.65;
            double dynamicAlpha = Math.Clamp(baseAlpha + (_smoothPeak * 0.35), 0.2, 1.0);

            byte alpha0 = (byte)(dynamicAlpha * 210);
            byte alpha1 = (byte)(dynamicAlpha * 85);

            TopEdgeStop0.Color = Color.FromArgb(alpha0, r, g, b);
            TopEdgeStop1.Color = Color.FromArgb(alpha1, r, g, b);

            BottomEdgeStop0.Color = Color.FromArgb((byte)Math.Min(255, alpha0 + 35), r, g, b);
            BottomEdgeStop1.Color = Color.FromArgb(alpha1, r, g, b);

            LeftEdgeStop0.Color = Color.FromArgb(alpha0, r, g, b);
            LeftEdgeStop1.Color = Color.FromArgb(alpha1, r, g, b);

            RightEdgeStop0.Color = Color.FromArgb(alpha0, r, g, b);
            RightEdgeStop1.Color = Color.FromArgb(alpha1, r, g, b);

            if (_mode == EdgeLightingMode.CyberNeonFrame)
            {
                TopEdgeBorder.Height = 16 + (highs * 30);
                BottomEdgeBorder.Height = 18 + (bass * 40);
                LeftEdgeBorder.Width = 16 + (mids * 30);
                RightEdgeBorder.Width = 16 + (mids * 30);
            }
            else
            {
                TopEdgeBorder.Height = 50 + (highs * 70);
                BottomEdgeBorder.Height = 70 + (bass * 110);
                LeftEdgeBorder.Width = 50 + (mids * 70);
                RightEdgeBorder.Width = 50 + (mids * 70);
            }

            TopLeftBloomStop0.Color = Color.FromArgb(alpha0, r, g, b);
            TopLeftBloomStop1.Color = Color.FromArgb(alpha1, r, g, b);
            TopRightBloomStop0.Color = Color.FromArgb(alpha0, r, g, b);
            TopRightBloomStop1.Color = Color.FromArgb(alpha1, r, g, b);

            byte bassAlpha = (byte)Math.Clamp(alpha0 * 1.15, 0, 255);
            BottomLeftBloomStop0.Color = Color.FromArgb(bassAlpha, r, g, b);
            BottomLeftBloomStop1.Color = Color.FromArgb(alpha1, r, g, b);
            BottomRightBloomStop0.Color = Color.FromArgb(bassAlpha, r, g, b);
            BottomRightBloomStop1.Color = Color.FromArgb(alpha1, r, g, b);

            double cornerSize = 220 + (bass * 180);
            BottomLeftBloom.Width = cornerSize;
            BottomLeftBloom.Height = cornerSize;
            BottomRightBloom.Width = cornerSize;
            BottomRightBloom.Height = cornerSize;

            if (_mode == EdgeLightingMode.SoundWaveRibbon)
            {
                DrawWaveRibbon(r, g, b, bass, mids, highs);
            }
        }

        private void DrawWaveRibbon(byte r, byte g, byte b, float bass, float mids, float highs)
        {
            double w = ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth;
            double h = WaveCanvas.Height > 0 ? WaveCanvas.Height : 140;

            _wavePhase += 0.08 + (_smoothPeak * 0.12);
            if (_wavePhase > Math.PI * 2) _wavePhase -= Math.PI * 2;

            int pointsCount = 28;
            double step = w / (pointsCount - 1);

            var mainFig = new PathFigure { StartPoint = new Point(0, h) };
            var backFig = new PathFigure { StartPoint = new Point(0, h) };

            for (int i = 0; i < pointsCount; i++)
            {
                double x = i * step;
                int bandIdx = Math.Min(_smoothBands.Length - 1, (i * _smoothBands.Length) / pointsCount);
                float bandVal = _smoothBands[bandIdx];

                double wave = Math.Sin((i * 0.45) + _wavePhase) * (18.0 + (bandVal * 45.0));
                double y = h - 20.0 - (bandVal * 55.0) - wave;
                y = Math.Clamp(y, 10, h - 5);

                mainFig.Segments.Add(new LineSegment(new Point(x, y), isStroked: true));
                backFig.Segments.Add(new LineSegment(new Point(x, y), isStroked: true));
            }

            backFig.Segments.Add(new LineSegment(new Point(w, h), isStroked: true));
            backFig.Segments.Add(new LineSegment(new Point(0, h), isStroked: true));

            var mainGeo = new PathGeometry();
            mainGeo.Figures.Add(mainFig);
            WaveMainPath.Data = mainGeo;
            WaveMainPath.Stroke = new SolidColorBrush(Color.FromArgb(220, r, g, b));

            var backGeo = new PathGeometry();
            backGeo.Figures.Add(backFig);
            WaveBackdropPath.Data = backGeo;
            WaveBackdropPath.Fill = new SolidColorBrush(Color.FromArgb(45, r, g, b));

            WaveCrestPath.Data = mainGeo;
        }
    }
}
