using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LyricsWidget.Models;
using LyricsWidget.Services;

namespace LyricsWidget
{
    public enum LyricAnimationMode
    {
        ZoomPop,
        KineticFlip,
        JellyMorph,
        CyberGlitch,
        SmoothDrift
    }

    public enum RollerMode
    {
        Words,
        ShortChunks,
        Lines
    }

    public partial class MainWindow : Window
    {
        private class RollerItem
        {
            public string Text { get; set; } = string.Empty;
            public string Emoji { get; set; } = string.Empty;
            public TimeSpan Timestamp { get; set; }
        }

        private readonly MediaSessionService _mediaService = new();
        private readonly WasapiAudioService _audioService = new(16);
        private readonly LyricsService _lyricsService = new();
        private readonly DesktopEdgeLightingWindow _edgeWindow = new();

        private readonly DispatcherTimer _syncTimer = new();
        private readonly float[] _audioBandsBuffer = new float[16];

        private List<LyricLine> _currentLyrics = new();
        private List<RollerItem> _rollerItems = new();
        private int _lastRolledIndex = -999999;
        private CancellationTokenSource? _lyricsFetchCts;
        private string _lastTrackKey = string.Empty;
        private bool _isManualMode = false;
        private DateTime _manualStartTime = DateTime.UtcNow;

        private Color _primaryColor = Color.FromRgb(0xFD, 0xBA, 0x25);
        private Color _accentColor = Color.FromRgb(0xF5, 0x9E, 0x0B);
        private Color _currentColor = Color.FromRgb(0xFD, 0xBA, 0x25);
        private Color? _customColorOverride = null;
        private bool _isAlbumSync = false;
        private bool _isTransparentBadge = false;
        private bool _userCustomSize = false;

        private LyricPreset _currentPreset = LyricPreset.Amber;
        private LyricAnimationMode _currentAnimMode = LyricAnimationMode.ZoomPop;
        private RollerMode _rollerMode = RollerMode.Words;
        private bool _isCardMode = false;

        private double _presetBaselineSkewX = 0;
        private double _presetBaselineRotate = 0;
        private double _presetBaselineScaleX = 1.0;
        private double _presetBaselineScaleY = 1.0;

        private Brush _activeBoxBrush = new SolidColorBrush(Color.FromRgb(0xFD, 0xBA, 0x25));
        private Brush _activeTextBrush = new SolidColorBrush(Color.FromRgb(0x05, 0x05, 0x08));
        private Brush _sungTextBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xEA));
        private Brush _upcomingTextBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        private CornerRadius _activeBoxRadius = new(6);
        private Thickness _activeBoxPadding = new(18, 4, 18, 4);
        private FontFamily _presetFont = new(new Uri("pack://application:,,,/"), "./Fonts/#Outfit, Outfit, Segoe UI");
        private double _activeFontSize = 56;
        private double _slotFontSize = 30;

        public MainWindow()
        {
            InitializeComponent();

            _syncTimer.Interval = TimeSpan.FromMilliseconds(33);
            _syncTimer.Tick += OnSyncTimerTick;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var workArea = SystemParameters.WorkArea;
            Width = Math.Min(1500, Math.Max(1280, workArea.Width * 0.82));
            Left = (workArea.Width - Width) / 2.0;
            Top = workArea.Bottom - Height - 70;

            ApplyPresetStyles();
            ApplyFontSize();
            UpdateAnimButtonText();
            UpdateRollerButtonText();
            UpdateEdgeLightingButtonText();

            _edgeWindow.Show();
            _edgeWindow.Owner = this;

            _mediaService.TrackChanged += OnTrackChanged;

            await _mediaService.InitializeAsync();
            _audioService.Start();
            _syncTimer.Start();
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            _syncTimer.Stop();
            _lyricsFetchCts?.Cancel();
            _edgeWindow.Close();
            _audioService.Dispose();
            _mediaService.Dispose();
        }

        private void ApplyPresetStyles()
        {
            if (CenterSlotBorder == null || CenterSlotText == null) return;

            _presetFont = new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Outfit, Outfit, Segoe UI");
            CenterSlotBorder.BorderThickness = new Thickness(0);
            CenterSlotBorder.BorderBrush = Brushes.Transparent;
            CenterSlotText.FontStyle = FontStyles.Normal;
            TopSlotText.FontStyle = FontStyles.Normal;
            BottomSlotText.FontStyle = FontStyles.Normal;
            _presetBaselineSkewX = 0;
            _presetBaselineRotate = 0;
            _presetBaselineScaleX = 1.0;
            _presetBaselineScaleY = 1.0;

            switch (_currentPreset)
            {
                case LyricPreset.Amber:
                    if (PresetButtonText != null)
                    {
                        PresetButtonText.Text = "🔥 Amber";
                        PresetButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0xFD, 0xBA, 0x25));
                    }

                    Color amberColor = _customColorOverride ?? Color.FromRgb(0xFD, 0xBA, 0x25);
                    _activeBoxBrush = new SolidColorBrush(amberColor);
                    double amberLum = 0.299 * amberColor.R + 0.587 * amberColor.G + 0.114 * amberColor.B;
                    _activeTextBrush = amberLum > 140 ? new SolidColorBrush(Color.FromRgb(0x05, 0x05, 0x08)) : Brushes.White;
                    _sungTextBrush = Brushes.White;
                    _upcomingTextBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                    _activeBoxRadius = new CornerRadius(8);
                    _activeBoxPadding = new Thickness(18, 5, 18, 5);
                    _activeFontSize = 56;
                    _slotFontSize = 30;

                    CenterSlotBorder.Background = _activeBoxBrush;
                    CenterSlotBorder.CornerRadius = _activeBoxRadius;
                    CenterSlotBorder.Padding = _activeBoxPadding;
                    CenterSlotText.Foreground = _activeTextBrush;
                    CenterSlotText.FontFamily = _presetFont;
                    CenterSlotText.FontSize = _activeFontSize;
                    CenterSlotText.FontWeight = FontWeights.Black;
                    CenterSlotBorder.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 22, ShadowDepth = 3, Opacity = 0.95 };

                    TopSlotText.FontFamily = _presetFont;
                    TopSlotText.FontSize = _slotFontSize;
                    TopSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xD4, 0xC0));

                    BottomSlotText.FontFamily = _presetFont;
                    BottomSlotText.FontSize = _slotFontSize;
                    BottomSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x99));
                    break;

                case LyricPreset.CyberNeon:
                    if (PresetButtonText != null)
                    {
                        PresetButtonText.Text = "⚡ Cyber Neon";
                        PresetButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4));
                    }

                    Color neonColor = _customColorOverride ?? Color.FromRgb(0x00, 0xF5, 0xD4);
                    _activeBoxBrush = Brushes.Transparent;
                    _activeTextBrush = new SolidColorBrush(neonColor);
                    _sungTextBrush = new SolidColorBrush(neonColor);
                    _upcomingTextBrush = new SolidColorBrush(Color.FromArgb(0x55, neonColor.R, neonColor.G, neonColor.B));
                    _activeBoxRadius = new CornerRadius(0);
                    _activeBoxPadding = new Thickness(14, 4, 14, 4);
                    _activeFontSize = 54;
                    _slotFontSize = 28;

                    CenterSlotBorder.Background = _activeBoxBrush;
                    CenterSlotBorder.CornerRadius = _activeBoxRadius;
                    CenterSlotBorder.Padding = _activeBoxPadding;
                    CenterSlotText.Foreground = _activeTextBrush;
                    CenterSlotText.FontFamily = _presetFont;
                    CenterSlotText.FontSize = _activeFontSize;
                    CenterSlotText.FontWeight = FontWeights.ExtraBold;
                    CenterSlotBorder.Effect = new DropShadowEffect { Color = neonColor, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.95 };

                    TopSlotText.FontFamily = _presetFont;
                    TopSlotText.FontSize = _slotFontSize;
                    TopSlotText.Foreground = new SolidColorBrush(Color.FromArgb(0xDD, neonColor.R, neonColor.G, neonColor.B));

                    BottomSlotText.FontFamily = _presetFont;
                    BottomSlotText.FontSize = _slotFontSize;
                    BottomSlotText.Foreground = new SolidColorBrush(Color.FromArgb(0x88, neonColor.R, neonColor.G, neonColor.B));
                    break;

                case LyricPreset.KineticSlant:
                    if (PresetButtonText != null)
                    {
                        PresetButtonText.Text = "🏎️ Speed Slant";
                        PresetButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x2A, 0x55));
                    }

                    Color slantColor = _customColorOverride ?? Color.FromRgb(0xFF, 0x2A, 0x55);
                    _activeBoxBrush = new SolidColorBrush(slantColor);
                    double slantLum = 0.299 * slantColor.R + 0.587 * slantColor.G + 0.114 * slantColor.B;
                    _activeTextBrush = slantLum > 140 ? new SolidColorBrush(Color.FromRgb(0x05, 0x05, 0x08)) : Brushes.White;
                    _sungTextBrush = Brushes.White;
                    _upcomingTextBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                    _activeBoxRadius = new CornerRadius(3);
                    _activeBoxPadding = new Thickness(22, 6, 22, 6);
                    _activeFontSize = 54;
                    _slotFontSize = 28;
                    _presetBaselineSkewX = -16.0;
                    CenterSlotSkew.AngleX = -16.0;
                    CenterSlotText.FontStyle = FontStyles.Italic;
                    TopSlotText.FontStyle = FontStyles.Italic;
                    BottomSlotText.FontStyle = FontStyles.Italic;

                    CenterSlotBorder.Background = _activeBoxBrush;
                    CenterSlotBorder.CornerRadius = _activeBoxRadius;
                    CenterSlotBorder.Padding = _activeBoxPadding;
                    CenterSlotText.Foreground = _activeTextBrush;
                    CenterSlotText.FontFamily = _presetFont;
                    CenterSlotText.FontSize = _activeFontSize;
                    CenterSlotText.FontWeight = FontWeights.Black;
                    CenterSlotBorder.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 4, Opacity = 0.90 };

                    TopSlotText.FontFamily = _presetFont;
                    TopSlotText.FontSize = _slotFontSize;
                    TopSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0xDD));

                    BottomSlotText.FontFamily = _presetFont;
                    BottomSlotText.FontSize = _slotFontSize;
                    BottomSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x77, 0x88));
                    break;

                case LyricPreset.FisheyeBulge:
                    if (PresetButtonText != null)
                    {
                        PresetButtonText.Text = "🐟 Fisheye Bulge";
                        PresetButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x03));
                    }

                    Color fishColor = _customColorOverride ?? Color.FromRgb(0xFF, 0xB7, 0x03);
                    _activeBoxBrush = new SolidColorBrush(fishColor);
                    double fishLum = 0.299 * fishColor.R + 0.587 * fishColor.G + 0.114 * fishColor.B;
                    _activeTextBrush = fishLum > 140 ? new SolidColorBrush(Color.FromRgb(0x05, 0x05, 0x08)) : Brushes.White;
                    _sungTextBrush = Brushes.White;
                    _upcomingTextBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                    _activeBoxRadius = new CornerRadius(26);
                    _activeBoxPadding = new Thickness(24, 7, 24, 7);
                    _activeFontSize = 56;
                    _slotFontSize = 26;
                    _presetBaselineScaleX = 1.15;
                    _presetBaselineScaleY = 1.25;
                    CenterSlotScale.ScaleX = 1.15;
                    CenterSlotScale.ScaleY = 1.25;

                    CenterSlotBorder.Background = _activeBoxBrush;
                    CenterSlotBorder.CornerRadius = _activeBoxRadius;
                    CenterSlotBorder.Padding = _activeBoxPadding;
                    CenterSlotText.Foreground = _activeTextBrush;
                    CenterSlotText.FontFamily = _presetFont;
                    CenterSlotText.FontSize = _activeFontSize;
                    CenterSlotText.FontWeight = FontWeights.Black;
                    CenterSlotBorder.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 4, Opacity = 0.95 };

                    TopSlotText.FontFamily = _presetFont;
                    TopSlotText.FontSize = _slotFontSize;
                    TopSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xDD, 0xBB));

                    BottomSlotText.FontFamily = _presetFont;
                    BottomSlotText.FontSize = _slotFontSize;
                    BottomSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x99));
                    break;

                case LyricPreset.Isometric3D:
                    if (PresetButtonText != null)
                    {
                        PresetButtonText.Text = "📐 3D Isometric";
                        PresetButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));
                    }

                    Color isoColor = _customColorOverride ?? Color.FromRgb(0x63, 0x66, 0xF1);
                    _activeBoxBrush = new SolidColorBrush(isoColor);
                    _activeTextBrush = Brushes.White;
                    _sungTextBrush = Brushes.White;
                    _upcomingTextBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                    _activeBoxRadius = new CornerRadius(6);
                    _activeBoxPadding = new Thickness(20, 6, 20, 6);
                    _activeFontSize = 52;
                    _slotFontSize = 28;
                    _presetBaselineRotate = -4.0;
                    _presetBaselineSkewX = 8.0;
                    CenterSlotRotate.Angle = -4.0;
                    CenterSlotSkew.AngleX = 8.0;

                    CenterSlotBorder.Background = _activeBoxBrush;
                    CenterSlotBorder.CornerRadius = _activeBoxRadius;
                    CenterSlotBorder.Padding = _activeBoxPadding;
                    CenterSlotText.Foreground = _activeTextBrush;
                    CenterSlotText.FontFamily = _presetFont;
                    CenterSlotText.FontSize = _activeFontSize;
                    CenterSlotText.FontWeight = FontWeights.ExtraBold;
                    CenterSlotBorder.Effect = new DropShadowEffect { Color = Color.FromRgb(0x18, 0x18, 0x30), BlurRadius = 3, ShadowDepth = 9, Direction = 315, Opacity = 0.95 };

                    TopSlotText.FontFamily = _presetFont;
                    TopSlotText.FontSize = _slotFontSize;
                    TopSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0xC7, 0xD2, 0xFE));

                    BottomSlotText.FontFamily = _presetFont;
                    BottomSlotText.FontSize = _slotFontSize;
                    BottomSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0x90));
                    break;

                case LyricPreset.SunsetGradient:
                    if (PresetButtonText != null)
                    {
                        PresetButtonText.Text = "🌅 Sunset Fire";
                        PresetButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35));
                    }

                    Color s2 = _customColorOverride ?? Color.FromRgb(0xFF, 0x6B, 0x35);
                    Color s1 = Color.FromRgb((byte)Math.Min(255, s2.R + 40), (byte)Math.Max(0, s2.G - 60), (byte)Math.Min(255, s2.B + 80));
                    var sunsetGrad = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 0),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(s1, 0.0),
                            new GradientStop(s2, 1.0)
                        }
                    };
                    _activeBoxBrush = sunsetGrad;
                    _activeTextBrush = Brushes.White;
                    _sungTextBrush = Brushes.White;
                    _upcomingTextBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
                    _activeBoxRadius = new CornerRadius(10);
                    _activeBoxPadding = new Thickness(20, 6, 20, 6);
                    _activeFontSize = 52;
                    _slotFontSize = 28;

                    CenterSlotBorder.Background = _activeBoxBrush;
                    CenterSlotBorder.CornerRadius = _activeBoxRadius;
                    CenterSlotBorder.Padding = _activeBoxPadding;
                    CenterSlotText.Foreground = _activeTextBrush;
                    CenterSlotText.FontFamily = _presetFont;
                    CenterSlotText.FontSize = _activeFontSize;
                    CenterSlotText.FontWeight = FontWeights.Black;
                    CenterSlotBorder.Effect = new DropShadowEffect { Color = s1, BlurRadius = 22, ShadowDepth = 2, Opacity = 0.70 };

                    TopSlotText.FontFamily = _presetFont;
                    TopSlotText.FontSize = _slotFontSize;
                    TopSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x88));

                    BottomSlotText.FontFamily = _presetFont;
                    BottomSlotText.FontSize = _slotFontSize;
                    BottomSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x55, 0x66));
                    break;

                case LyricPreset.WavyRibbon:
                    if (PresetButtonText != null)
                    {
                        PresetButtonText.Text = "〰️ Wavy Ribbon";
                        PresetButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0xD6, 0xA0));
                    }

                    Color waveColor = _customColorOverride ?? Color.FromRgb(0x06, 0xD6, 0xA0);
                    _activeBoxBrush = new SolidColorBrush(waveColor);
                    double waveLum = 0.299 * waveColor.R + 0.587 * waveColor.G + 0.114 * waveColor.B;
                    _activeTextBrush = waveLum > 140 ? new SolidColorBrush(Color.FromRgb(0x05, 0x05, 0x08)) : Brushes.White;
                    _sungTextBrush = Brushes.White;
                    _upcomingTextBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                    _activeBoxRadius = new CornerRadius(12);
                    _activeBoxPadding = new Thickness(20, 6, 20, 6);
                    _activeFontSize = 54;
                    _slotFontSize = 28;
                    _presetBaselineSkewX = -7.0;
                    _presetBaselineRotate = 2.0;
                    CenterSlotSkew.AngleX = -7.0;
                    CenterSlotRotate.Angle = 2.0;

                    CenterSlotBorder.Background = _activeBoxBrush;
                    CenterSlotBorder.CornerRadius = _activeBoxRadius;
                    CenterSlotBorder.Padding = _activeBoxPadding;
                    CenterSlotText.Foreground = _activeTextBrush;
                    CenterSlotText.FontFamily = _presetFont;
                    CenterSlotText.FontSize = _activeFontSize;
                    CenterSlotText.FontWeight = FontWeights.Black;
                    CenterSlotBorder.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 3, Opacity = 0.90 };

                    TopSlotText.FontFamily = _presetFont;
                    TopSlotText.FontSize = _slotFontSize;
                    TopSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xEE, 0xDD));

                    BottomSlotText.FontFamily = _presetFont;
                    BottomSlotText.FontSize = _slotFontSize;
                    BottomSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x88, 0x77));
                    break;

                case LyricPreset.GlitchAberration:
                    if (PresetButtonText != null)
                    {
                        PresetButtonText.Text = "📺 Glitch Aberration";
                        PresetButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0xEA));
                    }

                    Color gText = _customColorOverride ?? Color.FromRgb(0x00, 0xFF, 0xEA);
                    _activeBoxBrush = new SolidColorBrush(Color.FromArgb(0xEE, 0x0D, 0x0D, 0x14));
                    _activeTextBrush = new SolidColorBrush(gText);
                    _sungTextBrush = new SolidColorBrush(gText);
                    _upcomingTextBrush = new SolidColorBrush(Color.FromArgb(0x50, gText.R, gText.G, gText.B));
                    _activeBoxRadius = new CornerRadius(2);
                    _activeBoxPadding = new Thickness(18, 5, 18, 5);
                    _activeFontSize = 52;
                    _slotFontSize = 28;
                    _presetBaselineSkewX = 4.0;
                    CenterSlotSkew.AngleX = 4.0;

                    CenterSlotBorder.Background = _activeBoxBrush;
                    CenterSlotBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x55));
                    CenterSlotBorder.BorderThickness = new Thickness(2.5, 0, 2.5, 0);
                    CenterSlotBorder.CornerRadius = _activeBoxRadius;
                    CenterSlotBorder.Padding = _activeBoxPadding;
                    CenterSlotText.Foreground = _activeTextBrush;
                    CenterSlotText.FontFamily = _presetFont;
                    CenterSlotText.FontSize = _activeFontSize;
                    CenterSlotText.FontWeight = FontWeights.Black;
                    CenterSlotBorder.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x00, 0x55), BlurRadius = 0, ShadowDepth = 4, Direction = 0, Opacity = 1.0 };

                    TopSlotText.FontFamily = _presetFont;
                    TopSlotText.FontSize = _slotFontSize;
                    TopSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xEE, 0xEE));

                    BottomSlotText.FontFamily = _presetFont;
                    BottomSlotText.FontSize = _slotFontSize;
                    BottomSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0x88));
                    break;

                case LyricPreset.TerminalMatrix:
                    if (PresetButtonText != null)
                    {
                        PresetButtonText.Text = "💻 Matrix CRT";
                        PresetButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x66));
                    }

                    _presetFont = new FontFamily("Consolas, Courier New, monospace");
                    Color tColor = _customColorOverride ?? Color.FromRgb(0x00, 0xFF, 0x66);
                    _activeBoxBrush = new SolidColorBrush(Color.FromArgb(0xD0, (byte)(tColor.R / 12), (byte)(tColor.G / 12), (byte)(tColor.B / 12)));
                    _activeTextBrush = new SolidColorBrush(tColor);
                    _sungTextBrush = new SolidColorBrush(tColor);
                    _upcomingTextBrush = new SolidColorBrush(Color.FromArgb(0x50, tColor.R, tColor.G, tColor.B));
                    _activeBoxRadius = new CornerRadius(3);
                    _activeBoxPadding = new Thickness(14, 4, 14, 4);
                    _activeFontSize = 46;
                    _slotFontSize = 26;

                    CenterSlotBorder.Background = _activeBoxBrush;
                    CenterSlotBorder.BorderBrush = new SolidColorBrush(tColor);
                    CenterSlotBorder.BorderThickness = new Thickness(1.5);
                    CenterSlotBorder.CornerRadius = _activeBoxRadius;
                    CenterSlotBorder.Padding = _activeBoxPadding;
                    CenterSlotText.Foreground = _activeTextBrush;
                    CenterSlotText.FontFamily = _presetFont;
                    CenterSlotText.FontSize = _activeFontSize;
                    CenterSlotText.FontWeight = FontWeights.Bold;
                    CenterSlotBorder.Effect = new DropShadowEffect { Color = tColor, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.85 };

                    TopSlotText.FontFamily = _presetFont;
                    TopSlotText.FontSize = _slotFontSize;
                    TopSlotText.Foreground = new SolidColorBrush(Color.FromArgb(0xDD, tColor.R, tColor.G, tColor.B));

                    BottomSlotText.FontFamily = _presetFont;
                    BottomSlotText.FontSize = _slotFontSize;
                    BottomSlotText.Foreground = new SolidColorBrush(Color.FromArgb(0x77, tColor.R, tColor.G, tColor.B));
                    break;

                case LyricPreset.Glassmorphism:
                    if (PresetButtonText != null)
                    {
                        PresetButtonText.Text = "🧊 Frosted Glass";
                        PresetButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE7, 0xFF));
                    }

                    Color gTint = _customColorOverride ?? Colors.White;
                    _activeBoxBrush = new SolidColorBrush(Color.FromArgb(0x2E, gTint.R, gTint.G, gTint.B));
                    _activeTextBrush = Brushes.White;
                    _sungTextBrush = Brushes.White;
                    _upcomingTextBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
                    _activeBoxRadius = new CornerRadius(12);
                    _activeBoxPadding = new Thickness(18, 5, 18, 5);
                    _activeFontSize = 52;
                    _slotFontSize = 28;

                    CenterSlotBorder.Background = _activeBoxBrush;
                    CenterSlotBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, gTint.R, gTint.G, gTint.B));
                    CenterSlotBorder.BorderThickness = new Thickness(1.2);
                    CenterSlotBorder.CornerRadius = _activeBoxRadius;
                    CenterSlotBorder.Padding = _activeBoxPadding;
                    CenterSlotText.Foreground = _activeTextBrush;
                    CenterSlotText.FontFamily = _presetFont;
                    CenterSlotText.FontSize = _activeFontSize;
                    CenterSlotText.FontWeight = FontWeights.Bold;
                    CenterSlotBorder.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 25, ShadowDepth = 4, Opacity = 0.60 };

                    TopSlotText.FontFamily = _presetFont;
                    TopSlotText.FontSize = _slotFontSize;
                    TopSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xE0));

                    BottomSlotText.FontFamily = _presetFont;
                    BottomSlotText.FontSize = _slotFontSize;
                    BottomSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x80));
                    break;

                case LyricPreset.VaporwavePink:
                    if (PresetButtonText != null)
                    {
                        PresetButtonText.Text = "🌸 Vaporwave";
                        PresetButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x7F));
                    }

                    Color vPink = _customColorOverride ?? Color.FromRgb(0xFF, 0x00, 0x80);
                    Color vPurple = Color.FromRgb(0x79, 0x28, 0xCA);
                    var vaporGrad = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(vPurple, 0.0),
                            new GradientStop(vPink, 1.0)
                        }
                    };
                    _activeBoxBrush = vaporGrad;
                    _activeTextBrush = Brushes.White;
                    _sungTextBrush = Brushes.White;
                    _upcomingTextBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                    _activeBoxRadius = new CornerRadius(8);
                    _activeBoxPadding = new Thickness(18, 5, 18, 5);
                    _activeFontSize = 52;
                    _slotFontSize = 28;

                    CenterSlotBorder.Background = _activeBoxBrush;
                    CenterSlotBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, vPink.R, vPink.G, vPink.B));
                    CenterSlotBorder.BorderThickness = new Thickness(1.0);
                    CenterSlotBorder.CornerRadius = _activeBoxRadius;
                    CenterSlotBorder.Padding = _activeBoxPadding;
                    CenterSlotText.Foreground = _activeTextBrush;
                    CenterSlotText.FontFamily = _presetFont;
                    CenterSlotText.FontSize = _activeFontSize;
                    CenterSlotText.FontWeight = FontWeights.Black;
                    CenterSlotBorder.Effect = new DropShadowEffect { Color = vPink, BlurRadius = 24, ShadowDepth = 2, Opacity = 0.75 };

                    TopSlotText.FontFamily = _presetFont;
                    TopSlotText.FontSize = _slotFontSize;
                    TopSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x79, 0xF9));

                    BottomSlotText.FontFamily = _presetFont;
                    BottomSlotText.FontSize = _slotFontSize;
                    BottomSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8));
                    break;

                case LyricPreset.AlbumArtSync:
                    if (PresetButtonText != null)
                    {
                        PresetButtonText.Text = "✨ Album Art Sync";
                        PresetButtonText.Foreground = new SolidColorBrush(_primaryColor);
                    }

                    Color aColor = _primaryColor;
                    _activeBoxBrush = new SolidColorBrush(aColor);
                    _activeBoxRadius = new CornerRadius(8);
                    _activeBoxPadding = new Thickness(18, 5, 18, 5);
                    _activeFontSize = 54;
                    _slotFontSize = 28;

                    double aLum = 0.299 * aColor.R + 0.587 * aColor.G + 0.114 * aColor.B;
                    _activeTextBrush = aLum > 140
                        ? new SolidColorBrush(Color.FromRgb(0x05, 0x05, 0x08))
                        : Brushes.White;
                    _sungTextBrush = Brushes.White;
                    _upcomingTextBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));

                    CenterSlotBorder.Background = _activeBoxBrush;
                    CenterSlotBorder.CornerRadius = _activeBoxRadius;
                    CenterSlotBorder.Padding = _activeBoxPadding;
                    CenterSlotText.Foreground = _activeTextBrush;
                    CenterSlotText.FontFamily = _presetFont;
                    CenterSlotText.FontSize = _activeFontSize;
                    CenterSlotText.FontWeight = FontWeights.Black;
                    CenterSlotBorder.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 22, ShadowDepth = 3, Opacity = 0.95 };

                    TopSlotText.FontFamily = _presetFont;
                    TopSlotText.FontSize = _slotFontSize;
                    TopSlotText.Foreground = new SolidColorBrush(Color.FromArgb(0xEE, aColor.R, aColor.G, aColor.B));

                    BottomSlotText.FontFamily = _presetFont;
                    BottomSlotText.FontSize = _slotFontSize;
                    BottomSlotText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x99));
                    break;
            }

            if (_isTransparentBadge)
            {
                CenterSlotBorder.Background = Brushes.Transparent;
                CenterSlotBorder.BorderThickness = new Thickness(0);
            }

            if (_userCustomSize)
            {
                ApplyFontSize();
            }
            else if (SizeLabelText != null)
            {
                SizeLabelText.Text = $"{_activeFontSize}px";
            }

            if (CurrentColorDot != null)
            {
                CurrentColorDot.Fill = _activeBoxBrush;
            }

            _lastRolledIndex = -999999;
        }

        private void UpdateAnimButtonText()
        {
            if (AnimButtonText == null) return;

            switch (_currentAnimMode)
            {
                case LyricAnimationMode.ZoomPop:
                    AnimButtonText.Text = "⚡ Pop: Zoom";
                    AnimButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4));
                    break;
                case LyricAnimationMode.KineticFlip:
                    AnimButtonText.Text = "🔄 Pop: 3D Flip";
                    AnimButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0xFD, 0xBA, 0x25));
                    break;
                case LyricAnimationMode.JellyMorph:
                    AnimButtonText.Text = "🌊 Pop: Jelly";
                    AnimButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x7F));
                    break;
                case LyricAnimationMode.CyberGlitch:
                    AnimButtonText.Text = "💥 Pop: Glitch";
                    AnimButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x66));
                    break;
                case LyricAnimationMode.SmoothDrift:
                    AnimButtonText.Text = "🍃 Pop: Drift";
                    AnimButtonText.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA));
                    break;
            }
        }

        private void UpdateRollerButtonText()
        {
            switch (_rollerMode)
            {
                case RollerMode.Words:
                    RollModeText.Text = "🎰 Roll: Words";
                    RollModeText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0x66));
                    break;
                case RollerMode.ShortChunks:
                    RollModeText.Text = "🎰 Roll: 1-2 Words";
                    RollModeText.Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0xD6, 0xA0));
                    break;
                case RollerMode.Lines:
                    RollModeText.Text = "🎰 Roll: Lines";
                    RollModeText.Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x8A, 0xB2));
                    break;
            }
        }

        private void OnTrackChanged(TrackInfo track)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                if (track.IsEmpty)
                {
                    MiniTrackInfoText.Text = "Waiting for music...";
                    MiniAlbumArtImage.Source = null;
                    CardAlbumArtImage.Source = null;
                    CardTrackTitle.Text = "Waiting for music...";
                    CardArtistName.Text = "Play Spotify or YouTube";
                    ShowSingleMessage("Play Spotify or YouTube");
                    _lastTrackKey = string.Empty;
                    return;
                }

                var (prim, acc) = ColorExtractor.ExtractVibrantColors(track.AlbumArtBytes);
                _primaryColor = prim;
                _accentColor = acc;

                if (track.AlbumArtBytes != null && track.AlbumArtBytes.Length > 0)
                {
                    try
                    {
                        using var ms = new MemoryStream(track.AlbumArtBytes);
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        MiniAlbumArtImage.Source = bmp;
                        CardAlbumArtImage.Source = bmp;
                    }
                    catch { }
                }
                else
                {
                    MiniAlbumArtImage.Source = null;
                    CardAlbumArtImage.Source = null;
                }

                if (_isAlbumSync)
                {
                    SetCustomColor(_primaryColor, isTransparent: false);
                }
                else
                {
                    ApplyPresetStyles();
                }

                var (cleanTitle, cleanArtist) = LyricsService.ParseTrackAndArtist(track.Title, track.Artist);
                MiniTrackInfoText.Text = $"{cleanTitle} • {cleanArtist}";
                CardTrackTitle.Text = cleanTitle;
                CardArtistName.Text = cleanArtist;

                string trackKey = $"{cleanArtist.ToLowerInvariant()}_{cleanTitle.ToLowerInvariant()}";
                if (trackKey != _lastTrackKey)
                {
                    _lastTrackKey = trackKey;
                    await LoadLyricsForTrackAsync(track.Title, track.Artist, track.Duration);
                }
            });
        }

        private async Task LoadLyricsForTrackAsync(string rawTitle, string rawArtist, TimeSpan duration)
        {
            _lyricsFetchCts?.Cancel();
            _lyricsFetchCts = new CancellationTokenSource();
            var ct = _lyricsFetchCts.Token;

            var (cleanTitle, _) = LyricsService.ParseTrackAndArtist(rawTitle, rawArtist);
            ShowSingleMessage($"Finding: {cleanTitle}...");

            try
            {
                var lyrics = await _lyricsService.GetLyricsAsync(rawTitle, rawArtist, duration, ct);
                if (ct.IsCancellationRequested) return;

                if (lyrics != null && lyrics.Count > 0)
                {
                    _currentLyrics = lyrics;
                    BuildRollerItems();
                    _lastRolledIndex = -999999;
                    PopulateCardLyrics(lyrics);

                    OnSyncTimerTick(this, EventArgs.Empty);
                }
                else
                {
                    _lastTrackKey = string.Empty;
                    _currentLyrics = new List<LyricLine>();
                    _rollerItems.Clear();
                    _lastRolledIndex = -999999;
                    ShowSingleMessage("♪ (No synced lyrics found - click to retry)");
                    CardLyricsStackPanel.Children.Clear();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _lastTrackKey = string.Empty;
                ShowSingleMessage("Lyrics offline (click to retry)");
                System.Diagnostics.Debug.WriteLine($"Lyrics fetch error: {ex.Message}");
            }
        }

        private void BuildRollerItems()
        {
            _rollerItems.Clear();

            switch (_rollerMode)
            {
                case RollerMode.Words:
                    foreach (var line in _currentLyrics)
                    {
                        var words = line.Words;
                        if (words.Count == 0)
                        {
                            _rollerItems.Add(new RollerItem
                            {
                                Text = line.Text,
                                Emoji = CapCutFormatter.FindWordEmoji(line.Text),
                                Timestamp = line.Timestamp
                            });
                        }
                        else
                        {
                            foreach (var w in words)
                            {
                                _rollerItems.Add(new RollerItem
                                {
                                    Text = w.Word,
                                    Emoji = CapCutFormatter.FindWordEmoji(w.Word),
                                    Timestamp = w.Timestamp
                                });
                            }
                        }
                    }
                    break;

                case RollerMode.ShortChunks:
                    foreach (var line in _currentLyrics)
                    {
                        var words = line.Words;
                        if (words.Count <= 2)
                        {
                            _rollerItems.Add(new RollerItem
                            {
                                Text = line.Text,
                                Emoji = CapCutFormatter.FormatCapCutStack(line.Text).TopEmoji,
                                Timestamp = line.Timestamp
                            });
                        }
                        else
                        {
                            for (int i = 0; i < words.Count; i += 2)
                            {
                                int take = Math.Min(2, words.Count - i);
                                var chunkWords = words.Skip(i).Take(take).ToList();
                                string chunkText = string.Join(" ", chunkWords.Select(x => x.Word));
                                _rollerItems.Add(new RollerItem
                                {
                                    Text = chunkText,
                                    Emoji = CapCutFormatter.FindWordEmoji(chunkWords[^1].Word),
                                    Timestamp = chunkWords[0].Timestamp
                                });
                            }
                        }
                    }
                    break;

                case RollerMode.Lines:
                    foreach (var line in _currentLyrics)
                    {
                        _rollerItems.Add(new RollerItem
                        {
                            Text = line.Text,
                            Emoji = CapCutFormatter.FormatCapCutStack(line.Text).TopEmoji,
                            Timestamp = line.Timestamp
                        });
                    }
                    break;
            }
        }

        private void ShowSingleMessage(string text)
        {
            TopSlotText.Text = "";
            TopSlotEmoji.Text = "";
            CenterSlotText.Text = text;
            CenterSlotEmoji.Text = "";
            BottomSlotText.Text = "";
            BottomSlotEmoji.Text = "";
        }

        private void PopulateCardLyrics(List<LyricLine> lyrics)
        {
            CardLyricsStackPanel.Children.Clear();
            for (int i = 0; i < lyrics.Count; i++)
            {
                var line = lyrics[i];
                var tb = new TextBlock
                {
                    Text = line.Text,
                    FontSize = 14,
                    FontFamily = _presetFont,
                    FontWeight = FontWeights.Normal,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0xA0)),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10, 8, 10, 8),
                    Tag = i
                };
                CardLyricsStackPanel.Children.Add(tb);
            }
            CardLyricsScrollViewer.ScrollToVerticalOffset(0);
        }

        private void OnSyncTimerTick(object? sender, EventArgs e)
        {
            _audioService.GetLatestAudioData(_audioBandsBuffer, out float peak);
            if (peak > 0.18f)
            {
                double bassScale = _presetBaselineScaleX * (1.0 + (peak * 0.08));
                CenterSlotScale.ScaleX = bassScale;
                CenterSlotScale.ScaleY = bassScale;
            }
            _edgeWindow.UpdateAudio(_audioBandsBuffer, peak, GetEffectiveThemeColor());

            TimeSpan pos = _isManualMode
                ? (DateTime.UtcNow - _manualStartTime)
                : _mediaService.GetEstimatedPosition();

            if (_rollerItems.Count == 0) return;
            int activeIndex = -1;
            for (int i = 0; i < _rollerItems.Count; i++)
            {
                if (_rollerItems[i].Timestamp <= pos)
                {
                    activeIndex = i;
                }
                else
                {
                    break;
                }
            }

            if (activeIndex < 0)
            {
                ShowSingleMessage("♪ (Music playing)");
                return;
            }
            if (activeIndex != _lastRolledIndex)
            {
                _lastRolledIndex = activeIndex;
                RollToItem(activeIndex);
            }
            if (_isCardMode && _currentLyrics.Count > 0)
            {
                int lineIndex = -1;
                for (int i = 0; i < _currentLyrics.Count; i++)
                {
                    if (_currentLyrics[i].Timestamp <= pos) lineIndex = i;
                    else break;
                }
                if (lineIndex >= 0) UpdateCardModeHighlight(lineIndex);
            }
        }

        private void RollToItem(int activeIndex)
        {
            if (activeIndex < 0 || activeIndex >= _rollerItems.Count) return;

            var centerItem = _rollerItems[activeIndex];
            RollerItem? topItem = (activeIndex > 0) ? _rollerItems[activeIndex - 1] : null;
            RollerItem? bottomItem = (activeIndex + 1 < _rollerItems.Count) ? _rollerItems[activeIndex + 1] : null;
            double availWidth = Math.Max(360, ActualWidth - 140);
            if (CenterSlotBorder != null) CenterSlotBorder.MaxWidth = availWidth + 60;
            if (TopSlotBorder != null) TopSlotBorder.MaxWidth = availWidth + 60;
            if (BottomSlotBorder != null) BottomSlotBorder.MaxWidth = availWidth + 60;
            if (CenterSlotGrid != null) CenterSlotGrid.MaxWidth = availWidth;
            if (TopSlotGrid != null) TopSlotGrid.MaxWidth = availWidth;
            if (BottomSlotGrid != null) BottomSlotGrid.MaxWidth = availWidth;

            double currentActiveFont = _activeFontSize;
            try
            {
                var formatted = new FormattedText(
                    centerItem.Text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(_presetFont ?? CenterSlotText.FontFamily, CenterSlotText.FontStyle, CenterSlotText.FontWeight, CenterSlotText.FontStretch),
                    _activeFontSize,
                    Brushes.Black,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                if (formatted.Width > availWidth)
                {
                    double ratio = availWidth / formatted.Width;
                    currentActiveFont = Math.Max(22, Math.Floor(_activeFontSize * ratio * 0.96));
                }
            }
            catch
            {
                if (centerItem.Text.Length > 24)
                {
                    double scaleFactor = Math.Min(1.0, 32.0 / Math.Max(32, centerItem.Text.Length));
                    currentActiveFont = Math.Max(22, Math.Round(_activeFontSize * scaleFactor));
                }
            }

            CenterSlotText.FontSize = currentActiveFont;
            double currentSlotFont = Math.Max(16, Math.Round(currentActiveFont * 0.52));
            if (TopSlotText != null) TopSlotText.FontSize = currentSlotFont;
            if (BottomSlotText != null) BottomSlotText.FontSize = currentSlotFont;
            if (topItem != null)
            {
                if (TopSlotText != null) TopSlotText.Text = topItem.Text;
                if (TopSlotEmoji != null) TopSlotEmoji.Text = topItem.Emoji;
                if (TopSlotBorder != null) TopSlotBorder.Visibility = Visibility.Visible;
            }
            else
            {
                if (TopSlotText != null) TopSlotText.Text = "";
                if (TopSlotEmoji != null) TopSlotEmoji.Text = "";
                if (TopSlotBorder != null) TopSlotBorder.Visibility = Visibility.Hidden;
            }

            CenterSlotText.Text = centerItem.Text;
            if (CenterSlotEmoji != null) CenterSlotEmoji.Text = centerItem.Emoji;

            if (bottomItem != null)
            {
                if (BottomSlotText != null) BottomSlotText.Text = bottomItem.Text;
                if (BottomSlotEmoji != null) BottomSlotEmoji.Text = bottomItem.Emoji;
                if (BottomSlotBorder != null) BottomSlotBorder.Visibility = Visibility.Visible;
            }
            else
            {
                if (BottomSlotText != null) BottomSlotText.Text = "";
                if (BottomSlotEmoji != null) BottomSlotEmoji.Text = "";
                if (BottomSlotBorder != null) BottomSlotBorder.Visibility = Visibility.Hidden;
            }
            CenterSlotTranslate.X = 0;
            CenterSlotSkew.AngleX = _presetBaselineSkewX;
            CenterSlotSkew.AngleY = 0;
            CenterSlotRotate.Angle = _presetBaselineRotate;
            if (_currentPreset == LyricPreset.WavyRibbon)
            {
                TopSlotRotate.Angle = -4.5;
                BottomSlotRotate.Angle = 4.5;
            }
            else if (_currentPreset == LyricPreset.Isometric3D)
            {
                TopSlotRotate.Angle = -4.0;
                BottomSlotRotate.Angle = -4.0;
            }
            else
            {
                TopSlotRotate.Angle = -2.5;
                BottomSlotRotate.Angle = 2.5;
            }

            if (_currentPreset == LyricPreset.FisheyeBulge)
            {
                TopSlotScale.ScaleX = 0.60;
                TopSlotScale.ScaleY = 0.60;
                BottomSlotScale.ScaleX = 0.60;
                BottomSlotScale.ScaleY = 0.60;
            }
            else
            {
                TopSlotScale.ScaleX = 0.75;
                TopSlotScale.ScaleY = 0.75;
                BottomSlotScale.ScaleX = 0.75;
                BottomSlotScale.ScaleY = 0.75;
            }
            switch (_currentAnimMode)
            {
                case LyricAnimationMode.ZoomPop:
                    var rollUp = new DoubleAnimation
                    {
                        From = 36,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(190),
                        EasingFunction = new BackEase { Amplitude = 0.30, EasingMode = EasingMode.EaseOut }
                    };
                    RollerContainerTranslate.BeginAnimation(TranslateTransform.YProperty, rollUp);

                    var centerPop = new DoubleAnimation
                    {
                        From = _presetBaselineScaleX * 0.85,
                        To = _presetBaselineScaleX,
                        Duration = TimeSpan.FromMilliseconds(190),
                        EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
                    };
                    CenterSlotScale.BeginAnimation(ScaleTransform.ScaleXProperty, centerPop);

                    var centerPopY = new DoubleAnimation
                    {
                        From = _presetBaselineScaleY * 0.85,
                        To = _presetBaselineScaleY,
                        Duration = TimeSpan.FromMilliseconds(190),
                        EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
                    };
                    CenterSlotScale.BeginAnimation(ScaleTransform.ScaleYProperty, centerPopY);

                    var centerSlide = new DoubleAnimation
                    {
                        From = 6,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(160),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    CenterSlotTranslate.BeginAnimation(TranslateTransform.YProperty, centerSlide);
                    break;

                case LyricAnimationMode.KineticFlip:
                    var flipRoll = new DoubleAnimation
                    {
                        From = 40,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(220),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    RollerContainerTranslate.BeginAnimation(TranslateTransform.YProperty, flipRoll);

                    var flipY = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(250) };
                    flipY.KeyFrames.Add(new DiscreteDoubleKeyFrame(-0.25 * _presetBaselineScaleY, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
                    flipY.KeyFrames.Add(new SplineDoubleKeyFrame(0.40 * _presetBaselineScaleY, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
                    flipY.KeyFrames.Add(new SplineDoubleKeyFrame(1.15 * _presetBaselineScaleY, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
                    flipY.KeyFrames.Add(new SplineDoubleKeyFrame(_presetBaselineScaleY, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))));
                    CenterSlotScale.BeginAnimation(ScaleTransform.ScaleYProperty, flipY);

                    var flipX = new DoubleAnimation
                    {
                        From = _presetBaselineScaleX * 0.85,
                        To = _presetBaselineScaleX,
                        Duration = TimeSpan.FromMilliseconds(250),
                        EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }
                    };
                    CenterSlotScale.BeginAnimation(ScaleTransform.ScaleXProperty, flipX);

                    var flipTilt = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(250) };
                    flipTilt.KeyFrames.Add(new DiscreteDoubleKeyFrame(_presetBaselineRotate - 8.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
                    flipTilt.KeyFrames.Add(new SplineDoubleKeyFrame(_presetBaselineRotate + 3.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))));
                    flipTilt.KeyFrames.Add(new SplineDoubleKeyFrame(_presetBaselineRotate, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))));
                    CenterSlotRotate.BeginAnimation(RotateTransform.AngleProperty, flipTilt);
                    break;

                case LyricAnimationMode.JellyMorph:
                    var jellyRoll = new DoubleAnimation
                    {
                        From = 45,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(220),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    RollerContainerTranslate.BeginAnimation(TranslateTransform.YProperty, jellyRoll);

                    var jellyX = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(320) };
                    jellyX.KeyFrames.Add(new DiscreteDoubleKeyFrame(_presetBaselineScaleX * 1.38, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
                    jellyX.KeyFrames.Add(new SplineDoubleKeyFrame(_presetBaselineScaleX * 0.82, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100))));
                    jellyX.KeyFrames.Add(new SplineDoubleKeyFrame(_presetBaselineScaleX * 1.12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(190))));
                    jellyX.KeyFrames.Add(new SplineDoubleKeyFrame(_presetBaselineScaleX * 0.96, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260))));
                    jellyX.KeyFrames.Add(new SplineDoubleKeyFrame(_presetBaselineScaleX, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320))));
                    CenterSlotScale.BeginAnimation(ScaleTransform.ScaleXProperty, jellyX);

                    var jellyY = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(320) };
                    jellyY.KeyFrames.Add(new DiscreteDoubleKeyFrame(_presetBaselineScaleY * 0.62, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
                    jellyY.KeyFrames.Add(new SplineDoubleKeyFrame(_presetBaselineScaleY * 1.28, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100))));
                    jellyY.KeyFrames.Add(new SplineDoubleKeyFrame(_presetBaselineScaleY * 0.90, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(190))));
                    jellyY.KeyFrames.Add(new SplineDoubleKeyFrame(_presetBaselineScaleY * 1.04, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260))));
                    jellyY.KeyFrames.Add(new SplineDoubleKeyFrame(_presetBaselineScaleY, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320))));
                    CenterSlotScale.BeginAnimation(ScaleTransform.ScaleYProperty, jellyY);

                    var jellySlide = new DoubleAnimation
                    {
                        From = 14,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(220),
                        EasingFunction = new BounceEase { Bounces = 1, Bounciness = 2, EasingMode = EasingMode.EaseOut }
                    };
                    CenterSlotTranslate.BeginAnimation(TranslateTransform.YProperty, jellySlide);
                    break;

                case LyricAnimationMode.CyberGlitch:
                    var glitchRoll = new DoubleAnimation
                    {
                        From = 25,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(140),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    RollerContainerTranslate.BeginAnimation(TranslateTransform.YProperty, glitchRoll);

                    var glitchX = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(180) };
                    glitchX.KeyFrames.Add(new DiscreteDoubleKeyFrame(-16.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
                    glitchX.KeyFrames.Add(new DiscreteDoubleKeyFrame(18.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(35))));
                    glitchX.KeyFrames.Add(new DiscreteDoubleKeyFrame(-10.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
                    glitchX.KeyFrames.Add(new DiscreteDoubleKeyFrame(6.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110))));
                    glitchX.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
                    CenterSlotTranslate.BeginAnimation(TranslateTransform.XProperty, glitchX);

                    var glitchSkew = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(180) };
                    glitchSkew.KeyFrames.Add(new DiscreteDoubleKeyFrame(_presetBaselineSkewX + 14.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
                    glitchSkew.KeyFrames.Add(new DiscreteDoubleKeyFrame(_presetBaselineSkewX - 12.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(45))));
                    glitchSkew.KeyFrames.Add(new DiscreteDoubleKeyFrame(_presetBaselineSkewX + 6.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(95))));
                    glitchSkew.KeyFrames.Add(new DiscreteDoubleKeyFrame(_presetBaselineSkewX, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
                    CenterSlotSkew.BeginAnimation(SkewTransform.AngleXProperty, glitchSkew);

                    var glitchScale = new DoubleAnimation
                    {
                        From = _presetBaselineScaleX * 1.20,
                        To = _presetBaselineScaleX,
                        Duration = TimeSpan.FromMilliseconds(160),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    CenterSlotScale.BeginAnimation(ScaleTransform.ScaleXProperty, glitchScale);
                    CenterSlotScale.BeginAnimation(ScaleTransform.ScaleYProperty, glitchScale);
                    break;

                case LyricAnimationMode.SmoothDrift:
                    var driftRoll = new DoubleAnimation
                    {
                        From = 32,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(320),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    RollerContainerTranslate.BeginAnimation(TranslateTransform.YProperty, driftRoll);

                    var driftSlide = new DoubleAnimation
                    {
                        From = 18,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(320),
                        EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
                    };
                    CenterSlotTranslate.BeginAnimation(TranslateTransform.YProperty, driftSlide);

                    var driftScale = new DoubleAnimation
                    {
                        From = _presetBaselineScaleX * 0.88,
                        To = _presetBaselineScaleX,
                        Duration = TimeSpan.FromMilliseconds(320),
                        EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
                    };
                    CenterSlotScale.BeginAnimation(ScaleTransform.ScaleXProperty, driftScale);
                    CenterSlotScale.BeginAnimation(ScaleTransform.ScaleYProperty, driftScale);
                    break;
            }
            TopSlotRotate.Angle = -2.5;
            CenterSlotRotate.Angle = 0.0;
            BottomSlotRotate.Angle = 2.5;
        }

        private void UpdateCardModeHighlight(int lineIndex)
        {
            for (int i = 0; i < CardLyricsStackPanel.Children.Count; i++)
            {
                if (CardLyricsStackPanel.Children[i] is TextBlock tb)
                {
                    if (i == lineIndex)
                    {
                        tb.FontSize = 18;
                        tb.FontWeight = FontWeights.Bold;
                        tb.Foreground = new SolidColorBrush(_primaryColor);
                        tb.Opacity = 1.0;
                        ScrollCardLineIntoCenter(tb);
                    }
                    else if (Math.Abs(i - lineIndex) <= 2)
                    {
                        tb.FontSize = 14;
                        tb.FontWeight = FontWeights.Normal;
                        tb.Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xCC));
                        tb.Opacity = 0.75;
                    }
                    else
                    {
                        tb.FontSize = 13;
                        tb.FontWeight = FontWeights.Normal;
                        tb.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66));
                        tb.Opacity = 0.4;
                    }
                }
            }
        }

        private void ScrollCardLineIntoCenter(FrameworkElement activeElement)
        {
            try
            {
                GeneralTransform transform = activeElement.TransformToAncestor(CardLyricsStackPanel);
                Point point = transform.Transform(new Point(0, 0));
                double elementCenter = point.Y + (activeElement.ActualHeight / 2.0);
                double targetOffset = elementCenter - (CardLyricsScrollViewer.ActualHeight / 2.0);
                if (targetOffset < 0) targetOffset = 0;
                CardLyricsScrollViewer.ScrollToVerticalOffset(targetOffset);
            }
            catch { }
        }

        private void PresetButton_Click(object sender, RoutedEventArgs e)
        {
            int next = ((int)_currentPreset + 1) % 12;
            _currentPreset = (LyricPreset)next;
            ApplyPresetStyles();
            OnSyncTimerTick(this, EventArgs.Empty);
        }

        private void RollModeButton_Click(object sender, RoutedEventArgs e)
        {
            int next = ((int)_rollerMode + 1) % 3;
            _rollerMode = (RollerMode)next;
            UpdateRollerButtonText();
            BuildRollerItems();
            _lastRolledIndex = -999999;
            OnSyncTimerTick(this, EventArgs.Empty);
        }

        private void AnimButton_Click(object sender, RoutedEventArgs e)
        {
            int next = ((int)_currentAnimMode + 1) % 5;
            _currentAnimMode = (LyricAnimationMode)next;
            UpdateAnimButtonText();
            if (_lastRolledIndex >= 0 && _lastRolledIndex < _rollerItems.Count)
            {
                RollToItem(_lastRolledIndex);
            }
        }

        private void EdgeLightingButton_Click(object sender, RoutedEventArgs e)
        {
            _edgeWindow.CycleMode();
            UpdateEdgeLightingButtonText();
        }

        private void UpdateEdgeLightingButtonText()
        {
            if (EdgeLightingText == null) return;

            switch (_edgeWindow.Mode)
            {
                case EdgeLightingMode.AmbientAura:
                    EdgeLightingText.Text = "Edge: Aura";
                    EdgeLightingText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4));
                    break;
                case EdgeLightingMode.SoundWaveRibbon:
                    EdgeLightingText.Text = "Edge: Waves";
                    EdgeLightingText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35));
                    break;
                case EdgeLightingMode.CornerBassFlare:
                    EdgeLightingText.Text = "Edge: Bass";
                    EdgeLightingText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x7F));
                    break;
                case EdgeLightingMode.CyberNeonFrame:
                    EdgeLightingText.Text = "Edge: Neon";
                    EdgeLightingText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x66));
                    break;
                case EdgeLightingMode.Off:
                    EdgeLightingText.Text = "Edge: Off";
                    EdgeLightingText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                    break;
            }
        }

        private Color GetEffectiveThemeColor()
        {
            if (_customColorOverride.HasValue) return _customColorOverride.Value;
            if (_isAlbumSync) return _primaryColor;

            return _currentPreset switch
            {
                LyricPreset.Amber => Color.FromRgb(0xFD, 0xBA, 0x25),
                LyricPreset.CyberNeon => Color.FromRgb(0x00, 0xF5, 0xD4),
                LyricPreset.KineticSlant => Color.FromRgb(0xFF, 0x2A, 0x55),
                LyricPreset.FisheyeBulge => Color.FromRgb(0xFF, 0xB7, 0x03),
                LyricPreset.Isometric3D => Color.FromRgb(0x63, 0x66, 0xF1),
                LyricPreset.SunsetGradient => Color.FromRgb(0xFF, 0x6B, 0x35),
                LyricPreset.WavyRibbon => Color.FromRgb(0x06, 0xD6, 0xA0),
                LyricPreset.GlitchAberration => Color.FromRgb(0x00, 0xFF, 0xEA),
                LyricPreset.TerminalMatrix => Color.FromRgb(0x00, 0xFF, 0x66),
                LyricPreset.Glassmorphism => Color.FromRgb(0xE0, 0xE7, 0xFF),
                LyricPreset.VaporwavePink => Color.FromRgb(0xFF, 0x00, 0x80),
                LyricPreset.AlbumArtSync => _primaryColor,
                _ => _primaryColor
            };
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            FadeElement(HoverToolbar, 1.0);
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            FadeElement(HoverToolbar, 0.0);
            ColorPickerPopup.Visibility = Visibility.Collapsed;
        }

        private void ColorPickerButton_Click(object sender, RoutedEventArgs e)
        {
            ColorPickerPopup.Visibility = (ColorPickerPopup.Visibility == Visibility.Visible)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void CloseColorPicker_Click(object sender, RoutedEventArgs e)
        {
            ColorPickerPopup.Visibility = Visibility.Collapsed;
        }

        private async void RescanButton_Click(object sender, RoutedEventArgs e)
        {
            _isManualMode = false;
            ShowSingleMessage("Scanning media players...");
            _lastTrackKey = string.Empty;

            try
            {
                await _mediaService.ForceRescanSessionAsync();
                var track = _mediaService.CurrentTrack;
                if (track != null && !string.IsNullOrWhiteSpace(track.Title))
                {
                    await LoadLyricsForTrackAsync(track.Title, track.Artist, track.Duration);
                }
                else
                {
                    ShowSingleMessage("No player detected • Click 🔍 Find to search manually");
                }
            }
            catch (Exception ex)
            {
                ShowSingleMessage("Rescan error • Try manual 🔍 Find");
                System.Diagnostics.Debug.WriteLine($"Rescan error: {ex.Message}");
            }
        }

        private void ManualSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (ManualSearchPopup.Visibility == Visibility.Visible)
            {
                ManualSearchPopup.Visibility = Visibility.Collapsed;
                return;
            }

            ColorPickerPopup.Visibility = Visibility.Collapsed;
            ManualSearchPopup.Visibility = Visibility.Visible;

            if (_mediaService.CurrentTrack != null && !string.IsNullOrWhiteSpace(_mediaService.CurrentTrack.Title))
            {
                var (cleanTitle, cleanArtist) = LyricsService.ParseTrackAndArtist(_mediaService.CurrentTrack.Title, _mediaService.CurrentTrack.Artist);
                ManualSearchInput.Text = string.IsNullOrWhiteSpace(cleanArtist) ? cleanTitle : $"{cleanArtist} {cleanTitle}";
            }

            ManualSearchInput.Focus();
            ManualSearchInput.SelectAll();
        }

        private void CloseManualSearch_Click(object sender, RoutedEventArgs e)
        {
            ManualSearchPopup.Visibility = Visibility.Collapsed;
        }

        private void ManualSearchInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = DoManualSearchAsync();
            }
            else if (e.Key == Key.Escape)
            {
                ManualSearchPopup.Visibility = Visibility.Collapsed;
            }
        }

        private void DoSearchButton_Click(object sender, RoutedEventArgs e)
        {
            _ = DoManualSearchAsync();
        }

        private async Task DoManualSearchAsync()
        {
            string query = ManualSearchInput.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                ManualSearchStatus.Text = "Please enter song title or artist.";
                ManualSearchStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                return;
            }

            ManualSearchStatus.Text = $"Searching for \"{query}\"...";
            ManualSearchStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
            ManualSearchResultsPanel.Children.Clear();

            try
            {
                var results = await _lyricsService.SearchSongsDetailedAsync(query);
                if (results.Count == 0)
                {
                    ManualSearchStatus.Text = "No lyrics found. Try another query.";
                    ManualSearchStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
                    return;
                }

                ManualSearchStatus.Text = $"Found {results.Count} results. Click one to load lyrics:";
                ManualSearchStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));

                foreach (var item in results)
                {
                    var card = CreateSearchResultCard(item);
                    ManualSearchResultsPanel.Children.Add(card);
                }
            }
            catch (Exception ex)
            {
                ManualSearchStatus.Text = $"Search failed: {ex.Message}";
                ManualSearchStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            }
        }

        private Border CreateSearchResultCard(LyricSearchResult item)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 5),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF))
            };

            card.MouseEnter += (s, e) =>
            {
                card.Background = new SolidColorBrush(Color.FromArgb(0x44, 0x4F, 0x46, 0xE5));
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8));
            };
            card.MouseLeave += (s, e) =>
            {
                card.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sp = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            var titleBlock = new TextBlock
            {
                Text = item.Title,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var artistBlock = new TextBlock
            {
                Text = item.Artist,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xCC)),
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            sp.Children.Add(titleBlock);
            sp.Children.Add(artistBlock);
            Grid.SetColumn(sp, 0);
            grid.Children.Add(sp);

            var badgeSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (item.HasSyncedLyrics)
            {
                var syncedBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x35, 0x00, 0xF5, 0xD4)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 2, 5, 2),
                    Margin = new Thickness(6, 0, 0, 0)
                };
                syncedBadge.Child = new TextBlock
                {
                    Text = "SYNCED",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4))
                };
                badgeSp.Children.Add(syncedBadge);
            }
            else
            {
                var plainBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 2, 5, 2),
                    Margin = new Thickness(6, 0, 0, 0)
                };
                plainBadge.Child = new TextBlock
                {
                    Text = "PLAIN",
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA))
                };
                badgeSp.Children.Add(plainBadge);
            }

            Grid.SetColumn(badgeSp, 1);
            grid.Children.Add(badgeSp);

            card.Child = grid;

            card.MouseDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    SelectManualSearchResult(item);
                }
            };

            return card;
        }

        private void SelectManualSearchResult(LyricSearchResult item)
        {
            ManualSearchPopup.Visibility = Visibility.Collapsed;

            List<LyricLine> lyrics;
            if (!string.IsNullOrWhiteSpace(item.SyncedLyrics))
            {
                lyrics = LyricsService.ParseLrc(item.SyncedLyrics);
            }
            else if (!string.IsNullOrWhiteSpace(item.PlainLyrics))
            {
                lyrics = LyricsService.ParsePlainLyrics(item.PlainLyrics, item.Duration);
            }
            else
            {
                lyrics = new List<LyricLine>();
            }

            if (lyrics.Count > 0)
            {
                _isManualMode = true;
                _manualStartTime = DateTime.UtcNow;

                _currentLyrics = lyrics;
                BuildRollerItems();
                _lastRolledIndex = -999999;
                PopulateCardLyrics(lyrics);

                MiniTrackInfoText.Text = $"{item.Title} • {item.Artist}";
                CardTrackTitle.Text = item.Title;
                CardArtistName.Text = item.Artist;

                OnSyncTimerTick(this, EventArgs.Empty);
            }
            else
            {
                ShowSingleMessage($"No lyrics content in {item.Title}");
            }
        }

        private void Swatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                SetCustomColorHex(hex);
            }
        }

        private void ApplyHexButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyHexInput();
        }

        private void HexInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyHexInput();
            }
        }

        private void ApplyHexInput()
        {
            string hex = HexInputBox.Text.Trim();
            if (!hex.StartsWith("#") && (hex.Length == 6 || hex.Length == 8)) hex = "#" + hex;
            try
            {
                var converted = ColorConverter.ConvertFromString(hex);
                if (converted is Color c)
                {
                    SetCustomColor(c, isTransparent: false);
                }
            }
            catch { }
        }

        private void SetCustomColorHex(string hex)
        {
            try
            {
                var converted = ColorConverter.ConvertFromString(hex);
                if (converted is Color c)
                {
                    SetCustomColor(c, isTransparent: false);
                }
            }
            catch { }
        }

        private void AlbumSyncButton_Click(object sender, RoutedEventArgs e)
        {
            _isAlbumSync = true;
            SetCustomColor(_primaryColor, isTransparent: false);
        }

        private void NoBoxButton_Click(object sender, RoutedEventArgs e)
        {
            SetCustomColor(_currentColor, isTransparent: true);
        }

        private void ResetDefaultColorButton_Click(object sender, RoutedEventArgs e)
        {
            _customColorOverride = null;
            _isTransparentBadge = false;
            _isAlbumSync = false;
            ApplyPresetStyles();
            OnSyncTimerTick(this, EventArgs.Empty);
        }

        private void SetCustomColor(Color color, bool isTransparent = false)
        {
            _customColorOverride = color;
            _isTransparentBadge = isTransparent;
            _currentColor = color;

            if (CurrentColorDot != null)
            {
                CurrentColorDot.Fill = new SolidColorBrush(color);
            }
            if (HexInputBox != null)
            {
                HexInputBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }

            ApplyPresetStyles();
            OnSyncTimerTick(this, EventArgs.Empty);
        }

        private async void CenterSlotText_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _currentLyrics.Count == 0 && _mediaService.CurrentTrack != null)
            {
                var track = _mediaService.CurrentTrack;
                if (!string.IsNullOrWhiteSpace(track.Title))
                {
                    _lastTrackKey = string.Empty;
                    ShowSingleMessage($"Searching: {track.Title}...");
                    await LoadLyricsForTrackAsync(track.Title, track.Artist, track.Duration);
                }
            }
        }

        private void SizeDownButton_Click(object sender, RoutedEventArgs e)
        {
            _userCustomSize = true;
            _activeFontSize = Math.Max(20, _activeFontSize - 4);
            ApplyFontSize();
        }

        private void SizeUpButton_Click(object sender, RoutedEventArgs e)
        {
            _userCustomSize = true;
            _activeFontSize = Math.Min(96, _activeFontSize + 4);
            ApplyFontSize();
        }

        private void SizeLabelText_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _userCustomSize = false;
            _activeFontSize = 56;
            ApplyFontSize();
        }

        private void ApplyFontSize()
        {
            if (SizeLabelText != null)
            {
                SizeLabelText.Text = $"{_activeFontSize}px";
            }
            _slotFontSize = Math.Max(16, Math.Round(_activeFontSize * 0.54));

            CenterSlotText.FontSize = _activeFontSize;
            TopSlotText.FontSize = _slotFontSize;
            BottomSlotText.FontSize = _slotFontSize;

            CenterSlotEmoji.FontSize = Math.Max(20, Math.Round(_activeFontSize * 0.68));
            TopSlotEmoji.FontSize = Math.Max(16, Math.Round(_slotFontSize * 0.8));
            BottomSlotEmoji.FontSize = Math.Max(16, Math.Round(_slotFontSize * 0.8));
        }

        private void FadeElement(UIElement element, double toOpacity)
        {
            var anim = new DoubleAnimation
            {
                To = toOpacity,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            element.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void ModeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isCardMode = !_isCardMode;

            if (_isCardMode)
            {
                DesktopLyricsView.Visibility = Visibility.Collapsed;
                CardPlayerView.Visibility = Visibility.Visible;
                Height = 440;
                Width = 550;
                MainRootBorder.Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x14, 0x14, 0x1E));
                HoverToolbar.Opacity = 1.0;
                ModeToggleIconText.Text = "🖥️ CapCut Edit";
            }
            else
            {
                DesktopLyricsView.Visibility = Visibility.Visible;
                CardPlayerView.Visibility = Visibility.Collapsed;
                Height = 360;
                Width = 1200;
                MainRootBorder.Background = new SolidColorBrush(Color.FromArgb(0x00, 0, 0, 0));
                HoverToolbar.Opacity = 0.0;
                ModeToggleIconText.Text = "🗖 Player";
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            PinIconText.Foreground = Topmost ? new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)) : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            PinButton.ToolTip = Topmost ? "Always On Top: ON" : "Always On Top: OFF";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
