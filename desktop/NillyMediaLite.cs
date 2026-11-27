// ============================================================================
// NillyMedia Lite — Ultra-Light Native Windows Media Player & Voice Recorder
// created by Nilly graphics (Sotiris Christos) — https://nillygraphics.com/
// Strictly C# 5 compatible (.NET Framework 4.x csc.exe — 0 external DLLs)
// Replaces Windows Media Player (36.3 MB) + Sound Recorder (9.5 MB) in ~55 KB!
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("NillyMedia Lite")]
[assembly: AssemblyDescription("Ultra-Light Native Media Player & 1-Click Voice Recorder — created by Nilly graphics (Sotiris Christos)")]
[assembly: AssemblyCompany("Nilly graphics (Sotiris Christos) — https://nillygraphics.com/")]
[assembly: AssemblyProduct("NillyMedia Lite")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Nilly graphics (Sotiris Christos)")]
[assembly: AssemblyVersion("1.0.5.0")]
[assembly: AssemblyFileVersion("1.0.5.0")]

namespace NillyMediaLite
{
    // ========================================================================
    // 1. WIN32 INTEROP (Dark Titlebar + WinMM Voice Recorder — 0 DLLs)
    // ========================================================================
    internal static class Native
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr minSize, IntPtr maxSize);

        public static void TrimMemory()
        {
            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        public static extern int mciSendString(string command, StringBuilder buffer, int bufferSize, IntPtr hwndCallback);

        [DllImport("winmm.dll")]
        public static extern uint waveInGetNumDevs();

        public static void SetDarkTitleBar(Window w)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return;
                int val = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref val, 4) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref val, 4);
            }
            catch { }
        }

        public static string Mci(string cmd)
        {
            StringBuilder sb = new StringBuilder(256);
            mciSendString(cmd, sb, sb.Capacity, IntPtr.Zero);
            return sb.ToString();
        }
    }

    // ========================================================================
    // 2. ULTRA-LIGHT WINMM VOICE RECORDER (44.1 kHz 16-bit WAV)
    // ========================================================================
    internal class VoiceRecorderEngine
    {
        public bool IsRecording { get; private set; }
        public bool IsPaused { get; private set; }
        private DateTime _startUtc;
        private TimeSpan _accumulated = TimeSpan.Zero;

        public TimeSpan Elapsed
        {
            get
            {
                if (!IsRecording) return TimeSpan.Zero;
                if (IsPaused) return _accumulated;
                return _accumulated + (DateTime.UtcNow - _startUtc);
            }
        }

        public bool HasMicrophone()
        {
            try { return Native.waveInGetNumDevs() > 0; }
            catch { return false; }
        }

        public bool Start(out string error)
        {
            error = "";
            if (!HasMicrophone()) { error = "Δεν βρέθηκε συνδεδεμένο μικρόφωνο."; return false; }
            Native.Mci("close nillyrec");
            StringBuilder buf = new StringBuilder(128);
            int rc = Native.mciSendString("open new type waveaudio alias nillyrec", buf, buf.Capacity, IntPtr.Zero);
            if (rc != 0) { error = "Αδυναμία εκκίνησης μικροφώνου (MCI error " + rc + ")."; return false; }
            Native.Mci("set nillyrec time format ms bitspersample 16 channels 1 samplespersec 44100");
            rc = Native.mciSendString("record nillyrec", buf, buf.Capacity, IntPtr.Zero);
            if (rc != 0) { Native.Mci("close nillyrec"); error = "Αποτυχία εγγραφής (error " + rc + ")."; return false; }
            _accumulated = TimeSpan.Zero;
            _startUtc = DateTime.UtcNow;
            IsRecording = true;
            IsPaused = false;
            return true;
        }

        public void TogglePause()
        {
            if (!IsRecording) return;
            if (!IsPaused)
            {
                _accumulated += (DateTime.UtcNow - _startUtc);
                Native.Mci("pause nillyrec");
                IsPaused = true;
            }
            else
            {
                _startUtc = DateTime.UtcNow;
                Native.Mci("resume nillyrec");
                IsPaused = false;
            }
        }

        public double GetLiveLevel01()
        {
            if (!IsRecording || IsPaused) return 0;
            try
            {
                string s = Native.Mci("status nillyrec level");
                int lv;
                if (int.TryParse(s.Trim(), out lv))
                    return Math.Max(0.0, Math.Min(1.0, lv / 128.0));
            }
            catch { }
            return 0;
        }

        public bool StopAndSave(string targetWavPath, out string error)
        {
            error = "";
            if (!IsRecording) return false;
            try
            {
                Native.Mci("stop nillyrec");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetWavPath));
                StringBuilder buf = new StringBuilder(128);
                int rc = Native.mciSendString("save nillyrec \"" + targetWavPath + "\"", buf, buf.Capacity, IntPtr.Zero);
                Native.Mci("close nillyrec");
                IsRecording = false;
                IsPaused = false;
                if (rc != 0 || !File.Exists(targetWavPath))
                {
                    error = "Αποτυχία αποθήκευσης WAV (code " + rc + ").";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Native.Mci("close nillyrec");
                IsRecording = false; IsPaused = false;
                error = ex.Message;
                return false;
            }
        }

        public void Cancel()
        {
            if (!IsRecording) return;
            Native.Mci("stop nillyrec");
            Native.Mci("close nillyrec");
            IsRecording = false;
            IsPaused = false;
        }
    }

    // ========================================================================
    // 3. MAIN ULTRA-LIGHT MEDIA PLAYER & RECORDER WINDOW (100% Native WPF)
    // ========================================================================
    internal class MainWindow : Window
    {
        private static readonly HashSet<string> MediaExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm",
            ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac", ".ogg"
        };
        private static readonly HashSet<string> AudioOnlyExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac", ".ogg"
        };

        // Brushes (Ariakit Charcoal + Nilly Orange #FC961F palette)
        private static readonly SolidColorBrush BgWindow = new SolidColorBrush(Color.FromRgb(29, 31, 32));   // #1D1F20
        private static readonly SolidColorBrush BgBar = new SolidColorBrush(Color.FromRgb(34, 36, 38));      // #222426
        private static readonly SolidColorBrush BgCard = new SolidColorBrush(Color.FromRgb(39, 41, 43));     // #27292B
        private static readonly SolidColorBrush BgBtn = new SolidColorBrush(Color.FromRgb(39, 41, 43));      // #27292B
        private static readonly SolidColorBrush BgDarkInk = new SolidColorBrush(Color.FromRgb(20, 21, 23));
        private static readonly SolidColorBrush FgText = new SolidColorBrush(Color.FromRgb(242, 244, 247));
        private static readonly SolidColorBrush FgMuted = new SolidColorBrush(Color.FromRgb(154, 160, 168));
        private static readonly SolidColorBrush AccentGreen = new SolidColorBrush(Color.FromRgb(166, 226, 46));
        private static readonly SolidColorBrush AccentDanger = new SolidColorBrush(Color.FromRgb(240, 104, 118));
        private static readonly SolidColorBrush AccentDangerBg = new SolidColorBrush(Color.FromRgb(46, 28, 32));
        private static readonly SolidColorBrush AccentDangerBd = new SolidColorBrush(Color.FromRgb(116, 48, 58));
        private static readonly SolidColorBrush AccentAmber = new SolidColorBrush(Color.FromRgb(252, 150, 31)); // #FC961F
        private static readonly SolidColorBrush BorderSub = new SolidColorBrush(Color.FromRgb(54, 57, 61));     // #36393D

        // Crisp 1.5px Vector Path Icons (Ariakit Minimal)
        private const string ICO_PLAY = "M 3,2 L 12,7.5 L 3,13 Z";
        private const string ICO_PAUSE = "M 4,2.5 L 4,12.5 M 10,2.5 L 10,12.5";
        private const string ICO_STOP = "M 3,3 L 11,3 L 11,11 L 3,11 Z";
        private const string ICO_PREV = "M 2.5,2.5 L 2.5,12.5 M 11.5,2.5 L 4.5,7.5 L 11.5,12.5 Z";
        private const string ICO_NEXT = "M 11.5,2.5 L 11.5,12.5 M 2.5,2.5 L 9.5,7.5 L 2.5,12.5 Z";
        private const string ICO_VOL = "M 2,5.5 L 5,5.5 L 9,2.5 L 9,12.5 L 5,9.5 L 2,9.5 Z M 11.5,5 C 13,6.5 13,8.5 11.5,10";
        private const string ICO_MUTE = "M 2,5.5 L 5,5.5 L 9,2.5 L 9,12.5 L 5,9.5 L 2,9.5 Z M 11,5 L 14.5,10 M 14.5,5 L 11,10";
        private const string ICO_FULL = "M 2,5.5 L 2,2 L 5.5,2 M 9.5,2 L 13,2 L 13,5.5 M 13,9.5 L 13,13 L 9.5,13 M 5.5,13 L 2,13 L 2,9.5";
        private const string ICO_LIST = "M 2,3.5 L 13,3.5 M 2,7.5 L 13,7.5 M 2,11.5 L 10,11.5";
        private const string ICO_PIN = "M 4,2.5 L 11,2.5 M 5,2.5 L 3.5,8 L 11.5,8 L 10,2.5 M 7.5,8 L 7.5,13.5";
        private const string ICO_PLUS = "M 7.5,2.5 L 7.5,12.5 M 2.5,7.5 L 12.5,7.5";
        private const string ICO_MIC = "M 5.5,4 A 2,2 0 0 1 9.5,4 L 9.5,7.5 A 2,2 0 0 1 5.5,7.5 Z M 3.5,7.5 A 4,4 0 0 0 11.5,7.5 M 7.5,11.5 L 7.5,13.8 M 5,13.8 L 10,13.8";
        private const string ICO_FOLDER = "M 1.5,3.5 L 5.5,3.5 L 7,5 L 13.5,5 L 13.5,12.5 L 1.5,12.5 Z";
        private const string ICO_LOOP = "M 11.5,4 L 4,4 A 2.5,2.5 0 0 0 1.5,6.5 L 1.5,7.5 M 9.5,2 L 11.5,4 L 9.5,6 M 3.5,11 L 11,11 A 2.5,2.5 0 0 0 13.5,8.5 L 13.5,7.5 M 5.5,9 L 3.5,11 L 5.5,13";

        // Top Bar
        private Grid _topBar;
        private Button _btnModePlayer, _btnModeRec, _btnPlaylistToggle, _btnPin, _btnFullscreen;
        private TextBlock _lblNowPlaying;

        // Player View
        private Grid _playerRoot;
        private MediaElement _media;
        private StackPanel _audioOverlay;
        private TextBlock _lblAudioTitle, _lblAudioMeta;
        private Border _playlistPane;
        private ListBox _lstPlaylist;
        private Border _controlsBar;
        private Slider _seekSlider, _volSlider;
        private TextBlock _lblTimeCur, _lblTimeTot;
        private Button _btnPlay, _btnMute, _btnLoop, _btnSpeed;

        // Recorder View
        private Grid _recorderRoot;
        private TextBlock _lblRecTimer, _lblRecStatus;
        private Rectangle _vuFill;
        private Button _btnRecStartStop, _btnRecPause;
        private ListBox _lstRecordings;
        private VoiceRecorderEngine _rec = new VoiceRecorderEngine();

        // State
        private List<string> _playlist = new List<string>();
        private int _playIdx = -1;
        private bool _isPlaying = false;
        private bool _isLoop = false;
        private bool _isSeeking = false;
        private bool _isFullscreen = false;
        private WindowState _savedState;
        private WindowStyle _savedStyle;
        private double[] _speeds = new double[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
        private int _speedIdx = 2;
        private DispatcherTimer _timer;

        private static string RecordingsDir
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sound Recordings"); }
        }

        public MainWindow(string[] args)
        {
            this.Title = "NillyMedia Lite";
            this.Width = 880;
            this.Height = 560;
            this.MinWidth = 620;
            this.MinHeight = 420;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Background = BgWindow;
            this.AllowDrop = true;

            try
            {
                string icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NillyMediaLite.ico");
                if (File.Exists(icoPath)) this.Icon = new BitmapImage(new Uri(icoPath));
            }
            catch { }

            BuildUI();
            this.SourceInitialized += delegate { Native.SetDarkTitleBar(this); };
            this.Drop += OnFilesDropped;
            this.DragOver += delegate(object s, DragEventArgs e)
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            this.KeyDown += OnWindowKeyDown;
            this.StateChanged += delegate { SyncTimerState(); };
            this.Closing += delegate { if (_timer != null) _timer.Stop(); _rec.Cancel(); try { _media.Stop(); _media.Close(); } catch { } };

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(120);
            _timer.Tick += OnTick;

            if (args != null && args.Length > 0)
            {
                List<string> paths = new List<string>();
                for (int i = 0; i < args.Length; i++)
                    if (!string.IsNullOrEmpty(args[i]) && !args[i].StartsWith("--")) paths.Add(args[i]);
                if (paths.Count > 0) LoadPaths(paths.ToArray(), true);
            }
        }

        private void BuildUI()
        {
            Grid shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 1. Top Slim Ariakit Header Bar (bottom-indicator tabs + crisp vector icons)
            Border topWrap = new Border { Background = BgBar, BorderBrush = BorderSub, BorderThickness = new Thickness(0, 0, 0, 1) };
            _topBar = new Grid { Background = BgBar };
            _topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel leftBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            _btnModePlayer = MakeTabBtn(ICO_PLAY, "Player", true, true);
            _btnModePlayer.Click += delegate { SwitchMode(0); };
            _btnModeRec = MakeTabBtn(ICO_MIC, "Recorder", false, false);
            _btnModeRec.Click += delegate { SwitchMode(1); };
            Button btnOpen = MakePillBtn(MakeIconLabel(ICO_PLUS, "Open", FgText, false), 0, false);
            btnOpen.Padding = new Thickness(10, 4, 10, 4);
            btnOpen.Margin = new Thickness(8, 0, 2, 0);
            btnOpen.Click += delegate { PromptOpenFiles(); };
            leftBtns.Children.Add(_btnModePlayer);
            leftBtns.Children.Add(_btnModeRec);
            leftBtns.Children.Add(btnOpen);
            Grid.SetColumn(leftBtns, 0);
            _topBar.Children.Add(leftBtns);

            _lblNowPlaying = new TextBlock
            {
                Text = "Drop Video/Audio or click Open",
                Foreground = FgMuted,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(_lblNowPlaying, 1);
            _topBar.Children.Add(_lblNowPlaying);

            StackPanel rightBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            _btnPlaylistToggle = MakePillBtn(MakeIconLabel(ICO_LIST, "List", FgText, false), 0, false);
            _btnPlaylistToggle.Padding = new Thickness(9, 4, 9, 4);
            _btnPlaylistToggle.ToolTip = "Playlist (P)";
            _btnPlaylistToggle.Click += delegate { TogglePlaylist(); };
            _btnPin = MakePillBtn(MakeIcon(ICO_PIN, FgMuted, false), 30, false);
            _btnPin.ToolTip = "Always on Top (T)";
            _btnPin.Click += delegate { TogglePin(); };
            _btnFullscreen = MakePillBtn(MakeIcon(ICO_FULL, FgText, false), 30, false);
            _btnFullscreen.ToolTip = "Fullscreen (F11)";
            _btnFullscreen.Click += delegate { ToggleFullscreen(); };
            rightBtns.Children.Add(_btnPlaylistToggle);
            rightBtns.Children.Add(_btnPin);
            rightBtns.Children.Add(_btnFullscreen);
            Grid.SetColumn(rightBtns, 2);
            _topBar.Children.Add(rightBtns);

            topWrap.Child = _topBar;
            Grid.SetRow(topWrap, 0);
            shell.Children.Add(topWrap);

            // 2. Content Host (Player + Recorder)
            Grid content = new Grid();
            Grid.SetRow(content, 1);
            shell.Children.Add(content);

            _playerRoot = BuildPlayerView();
            _recorderRoot = BuildRecorderView();
            _recorderRoot.Visibility = Visibility.Collapsed;

            content.Children.Add(_playerRoot);
            content.Children.Add(_recorderRoot);

            this.Content = shell;
        }

        private Grid BuildPlayerView()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Middle: Video/Audio Canvas + Collapsible Playlist
            Grid stage = new Grid();
            stage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            stage.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border videoHost = new Border { Background = Brushes.Black, ClipToBounds = true };
            Grid videoGrid = new Grid();

            _media = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                Stretch = Stretch.Uniform,
                Volume = 0.85
            };
            _media.MediaOpened += OnMediaOpened;
            _media.MediaEnded += OnMediaEnded;
            _media.MediaFailed += delegate(object s, ExceptionRoutedEventArgs e)
            {
                _isPlaying = false;
                _btnPlay.Content = MakeIcon(ICO_PLAY, BgDarkInk, true);
                SyncTimerState();
                _lblAudioTitle.Text = "Αδυναμία αναπαραγωγής αρχείου";
                _lblAudioMeta.Text = e.ErrorException != null ? e.ErrorException.Message : "Μη υποστηριζόμενος codec.";
                _audioOverlay.Visibility = Visibility.Visible;
            };
            videoGrid.Children.Add(_media);

            // Minimal Audio/Empty Overlay
            _audioOverlay = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24) };
            Border heroBadge = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Background = BgBar, BorderBrush = BorderSub, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center };
            System.Windows.Shapes.Path bigIcon = new System.Windows.Shapes.Path { Data = Geometry.Parse(ICO_PLAY), Fill = AccentAmber, Width = 24, Height = 24, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            heroBadge.Child = bigIcon;
            _lblAudioTitle = new TextBlock { Text = "NillyMedia Lite", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = FgText, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 4), TextAlignment = TextAlignment.Center };
            _lblAudioMeta = new TextBlock { Text = "Ρίξε βίντεο ή ήχο εδώ (MP4 · MOV · MKV · MP3 · WAV · FLAC) · 0 MB background RAM", FontSize = 12, Foreground = FgMuted, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            _audioOverlay.Children.Add(heroBadge);
            _audioOverlay.Children.Add(_lblAudioTitle);
            _audioOverlay.Children.Add(_lblAudioMeta);
            videoGrid.Children.Add(_audioOverlay);

            videoHost.Child = videoGrid;
            videoHost.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2) ToggleFullscreen();
                else if (_playlist.Count > 0) TogglePlayPause();
            };

            Grid.SetColumn(videoHost, 0);
            stage.Children.Add(videoHost);

            // Collapsible Playlist Pane
            _playlistPane = new Border { Width = 240, Background = BgBar, BorderBrush = BorderSub, BorderThickness = new Thickness(1, 0, 0, 0), Visibility = Visibility.Collapsed };
            DockPanel plDock = new DockPanel();
            TextBlock plHdr = new TextBlock { Text = "PLAYLIST", Foreground = AccentAmber, FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(10, 8, 10, 6) };
            DockPanel.SetDock(plHdr, Dock.Top);
            plDock.Children.Add(plHdr);

            _lstPlaylist = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = FgText, FontSize = 12, Padding = new Thickness(4) };
            _lstPlaylist.MouseDoubleClick += delegate
            {
                if (_lstPlaylist.SelectedIndex >= 0) PlayIndex(_lstPlaylist.SelectedIndex);
            };
            plDock.Children.Add(_lstPlaylist);
            _playlistPane.Child = plDock;
            Grid.SetColumn(_playlistPane, 1);
            stage.Children.Add(_playlistPane);

            Grid.SetRow(stage, 0);
            root.Children.Add(stage);

            // Bottom Sleek Transport Controls Bar
            _controlsBar = new Border { Background = BgBar, BorderBrush = BorderSub, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 6, 12, 8) };
            StackPanel ctrlStack = new StackPanel();

            // Seek bar row
            Grid seekGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            seekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            seekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            seekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _lblTimeCur = new TextBlock { Text = "00:00", Foreground = FgMuted, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            _seekSlider = new Slider { Minimum = 0, Maximum = 100, Value = 0, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
            _seekSlider.PreviewMouseDown += delegate { _isSeeking = true; };
            _seekSlider.PreviewMouseUp += delegate
            {
                _isSeeking = false;
                if (_media.NaturalDuration.HasTimeSpan)
                    _media.Position = TimeSpan.FromSeconds(_seekSlider.Value);
            };
            _lblTimeTot = new TextBlock { Text = "00:00", Foreground = FgMuted, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

            Grid.SetColumn(_lblTimeCur, 0); Grid.SetColumn(_seekSlider, 1); Grid.SetColumn(_lblTimeTot, 2);
            seekGrid.Children.Add(_lblTimeCur); seekGrid.Children.Add(_seekSlider); seekGrid.Children.Add(_lblTimeTot);
            ctrlStack.Children.Add(seekGrid);

            // Buttons + Volume row
            Grid btnRow = new Grid();
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel transport = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button bPrev = MakePillBtn(MakeIcon(ICO_PREV, FgText, true), 34, false); bPrev.ToolTip = "Προηγούμενο"; bPrev.Click += delegate { PlayPrev(); };
            Button bBack = MakePillBtn("-10s", 42, false); bBack.Click += delegate { SeekRelative(-10); };
            _btnPlay = MakePillBtn(MakeIcon(ICO_PLAY, BgDarkInk, true), 46, true);
            _btnPlay.ToolTip = "Play / Pause (Space)";
            _btnPlay.Click += delegate { TogglePlayPause(); };
            Button bFwd = MakePillBtn("+10s", 42, false); bFwd.Click += delegate { SeekRelative(10); };
            Button bNext = MakePillBtn(MakeIcon(ICO_NEXT, FgText, true), 34, false); bNext.ToolTip = "Επόμενο"; bNext.Click += delegate { PlayNext(); };
            Button bStop = MakePillBtn(MakeIcon(ICO_STOP, FgText, true), 34, false); bStop.ToolTip = "Stop"; bStop.Click += delegate { StopPlayback(); };
            transport.Children.Add(bPrev); transport.Children.Add(bBack); transport.Children.Add(_btnPlay);
            transport.Children.Add(bFwd); transport.Children.Add(bNext); transport.Children.Add(bStop);
            Grid.SetColumn(transport, 0);
            btnRow.Children.Add(transport);

            StackPanel rightCtrl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _btnSpeed = MakePillBtn("1.0×", 46, false);
            _btnSpeed.Click += delegate { CycleSpeed(); };
            _btnLoop = MakePillBtn(MakeIconLabel(ICO_LOOP, "Loop", FgText, false), 62, false);
            _btnLoop.Click += delegate { ToggleLoop(); };
            _btnMute = MakePillBtn(MakeIcon(ICO_VOL, FgText, false), 34, false);
            _btnMute.ToolTip = "Mute (M)";
            _btnMute.Click += delegate { ToggleMute(); };
            _volSlider = new Slider { Minimum = 0, Maximum = 1, Value = 0.85, Width = 88, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
            _volSlider.ValueChanged += delegate
            {
                _media.Volume = _volSlider.Value;
                if (_media.IsMuted && _volSlider.Value > 0) { _media.IsMuted = false; _btnMute.Content = MakeIcon(ICO_VOL, FgText, false); }
            };
            rightCtrl.Children.Add(_btnSpeed); rightCtrl.Children.Add(_btnLoop); rightCtrl.Children.Add(_btnMute); rightCtrl.Children.Add(_volSlider);
            Grid.SetColumn(rightCtrl, 2);
            btnRow.Children.Add(rightCtrl);

            ctrlStack.Children.Add(btnRow);
            _controlsBar.Child = ctrlStack;
            Grid.SetRow(_controlsBar, 1);
            root.Children.Add(_controlsBar);
            return root;
        }

        // ====================================================================
        // VOICE RECORDER VIEW (1-Click WAV Recording + Live VU Meter)
        // ====================================================================
        private Grid BuildRecorderView()
        {
            Grid root = new Grid { Margin = new Thickness(24, 18, 24, 18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Border recCard = new Border { Background = BgCard, BorderBrush = BorderSub, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(20) };
            StackPanel st = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            _lblRecTimer = new TextBlock { Text = "00:00.0", FontFamily = new FontFamily("Consolas"), FontSize = 44, FontWeight = FontWeights.Bold, Foreground = FgText, HorizontalAlignment = HorizontalAlignment.Center };
            _lblRecStatus = new TextBlock { Text = "Έτοιμο για ηχογράφηση (44.1 kHz WAV → Documents\\Sound Recordings)", FontSize = 12, Foreground = FgMuted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 14) };

            // Live VU Level Meter Bar
            Border vuTrack = new Border { Width = 360, Height = 10, Background = BgWindow, CornerRadius = new CornerRadius(5), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 16) };
            _vuFill = new Rectangle { Fill = AccentGreen, Width = 0, Height = 10, HorizontalAlignment = HorizontalAlignment.Left };
            vuTrack.Child = _vuFill;

            StackPanel recBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            _btnRecStartStop = MakePillBtn(MakeIconLabel(ICO_MIC, "Record", AccentDanger, false), 140, false);
            _btnRecStartStop.Height = 36; _btnRecStartStop.Background = AccentDangerBg; _btnRecStartStop.BorderBrush = AccentDangerBd; _btnRecStartStop.Margin = new Thickness(4);
            _btnRecStartStop.Click += delegate { ToggleRecord(); };

            _btnRecPause = MakePillBtn(MakeIconLabel(ICO_PAUSE, "Pause", FgText, false), 110, false);
            _btnRecPause.Height = 36; _btnRecPause.Margin = new Thickness(4); _btnRecPause.IsEnabled = false;
            _btnRecPause.Click += delegate
            {
                _rec.TogglePause();
                _btnRecPause.Content = _rec.IsPaused ? MakeIconLabel(ICO_PLAY, "Resume", FgText, true) : MakeIconLabel(ICO_PAUSE, "Pause", FgText, false);
                _lblRecStatus.Text = _rec.IsPaused ? "Σε παύση..." : "Ηχογράφηση σε εξέλιξη...";
            };

            Button btnOpenRecDir = MakePillBtn(MakeIconLabel(ICO_FOLDER, "Open Folder", FgText, false), 124, false);
            btnOpenRecDir.Height = 36; btnOpenRecDir.Margin = new Thickness(4);
            btnOpenRecDir.Click += delegate
            {
                Directory.CreateDirectory(RecordingsDir);
                try { Process.Start("explorer.exe", "\"" + RecordingsDir + "\""); } catch { }
            };

            recBtns.Children.Add(_btnRecStartStop);
            recBtns.Children.Add(_btnRecPause);
            recBtns.Children.Add(btnOpenRecDir);

            st.Children.Add(_lblRecTimer);
            st.Children.Add(_lblRecStatus);
            st.Children.Add(vuTrack);
            st.Children.Add(recBtns);
            recCard.Child = st;
            Grid.SetRow(recCard, 0);
            root.Children.Add(recCard);

            // Saved Recordings List
            Border listCard = new Border { Background = BgCard, BorderBrush = BorderSub, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(12) };
            DockPanel ld = new DockPanel();
            TextBlock lh = new TextBlock { Text = "ΑΠΟΘΗΚΕΥΜΕΝΕΣ ΗΧΟΓΡΑΦΗΣΕΙΣ (διπλό κλικ για αναπαραγωγή)", Foreground = AccentAmber, FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(2, 0, 0, 8) };
            DockPanel.SetDock(lh, Dock.Top);
            ld.Children.Add(lh);

            _lstRecordings = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = FgText, FontFamily = new FontFamily("Consolas"), FontSize = 12.5 };
            _lstRecordings.MouseDoubleClick += delegate
            {
                string path = _lstRecordings.SelectedItem != null ? ((ListBoxItem)_lstRecordings.SelectedItem).Tag as string : null;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    SwitchMode(0);
                    LoadPaths(new string[] { path }, true);
                }
            };
            ld.Children.Add(_lstRecordings);
            listCard.Child = ld;
            Grid.SetRow(listCard, 1);
            root.Children.Add(listCard);

            RefreshRecordingsList();
            return root;
        }

        // ====================================================================
        // PLAYBACK & RECORDER LOGIC
        // ====================================================================
        public void SwitchMode(int mode)
        {
            bool isRec = (mode == 1);
            _playerRoot.Visibility = isRec ? Visibility.Collapsed : Visibility.Visible;
            _recorderRoot.Visibility = isRec ? Visibility.Visible : Visibility.Collapsed;
            _btnModePlayer.BorderBrush = isRec ? Brushes.Transparent : AccentAmber;
            _btnModePlayer.Foreground = isRec ? FgMuted : FgText;
            _btnModeRec.BorderBrush = isRec ? AccentAmber : Brushes.Transparent;
            _btnModeRec.Foreground = isRec ? FgText : FgMuted;
            if (isRec) RefreshRecordingsList();
        }

        private void ToggleRecord()
        {
            if (!_rec.IsRecording)
            {
                StopPlayback();
                string err;
                if (_rec.Start(out err))
                {
                    _btnRecStartStop.Content = MakeIconLabel(ICO_STOP, "Stop & Save", AccentDanger, true);
                    _btnRecPause.IsEnabled = true;
                    _btnRecPause.Content = MakeIconLabel(ICO_PAUSE, "Pause", FgText, false);
                    _lblRecStatus.Text = "Ηχογράφηση σε εξέλιξη...";
                    _lblRecStatus.Foreground = AccentDanger;
                    SyncTimerState();
                }
                else
                {
                    _lblRecStatus.Text = err;
                    _lblRecStatus.Foreground = AccentAmber;
                }
            }
            else
            {
                string file = System.IO.Path.Combine(RecordingsDir, "Recording_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".wav");
                string err;
                bool ok = _rec.StopAndSave(file, out err);
                SyncTimerState();
                _btnRecStartStop.Content = MakeIconLabel(ICO_MIC, "Record", AccentDanger, false);
                _btnRecPause.IsEnabled = false;
                _vuFill.Width = 0;
                if (ok)
                {
                    _lblRecStatus.Text = "Αποθηκεύτηκε: " + System.IO.Path.GetFileName(file);
                    _lblRecStatus.Foreground = AccentGreen;
                    RefreshRecordingsList();
                }
                else
                {
                    _lblRecStatus.Text = err;
                    _lblRecStatus.Foreground = AccentAmber;
                }
            }
        }

        private void RefreshRecordingsList()
        {
            _lstRecordings.Items.Clear();
            if (!Directory.Exists(RecordingsDir)) return;
            try
            {
                FileInfo[] files = new DirectoryInfo(RecordingsDir).GetFiles("*.wav");
                Array.Sort(files, delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
                for (int i = 0; i < files.Length; i++)
                {
                    ListBoxItem item = new ListBoxItem();
                    item.Content = string.Format("▶  {0,-36}  ({1:0.0} KB)", files[i].Name, files[i].Length / 1024.0);
                    item.Tag = files[i].FullName;
                    item.Padding = new Thickness(6, 5, 6, 5);
                    _lstRecordings.Items.Add(item);
                }
            }
            catch { }
        }

        private void OnFilesDropped(object sender, DragEventArgs e)
        {
            string[] items = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (items != null && items.Length > 0)
            {
                SwitchMode(0);
                LoadPaths(items, true);
            }
        }

        private void PromptOpenFiles()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Multiselect = true;
            dlg.Filter = "Media Files|*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv;*.webm;*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.ogg|All Files|*.*";
            if (dlg.ShowDialog(this) == true)
            {
                SwitchMode(0);
                LoadPaths(dlg.FileNames, true);
            }
        }

        public void LoadPaths(string[] paths, bool autoPlay)
        {
            _playlist.Clear();
            int startIdx = 0;

            if (paths.Length == 1 && File.Exists(paths[0]))
            {
                // Auto-populate sibling media files in same folder (smooth Next/Prev!)
                string target = System.IO.Path.GetFullPath(paths[0]);
                string dir = System.IO.Path.GetDirectoryName(target);
                string[] all = Directory.GetFiles(dir);
                Array.Sort(all, StringComparer.CurrentCultureIgnoreCase);
                for (int i = 0; i < all.Length; i++)
                {
                    if (MediaExts.Contains(System.IO.Path.GetExtension(all[i])) ||
                        string.Equals(all[i], target, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(all[i], target, StringComparison.OrdinalIgnoreCase))
                            startIdx = _playlist.Count;
                        _playlist.Add(all[i]);
                    }
                }
            }
            else
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    if (Directory.Exists(paths[i]))
                    {
                        string[] sub = Directory.GetFiles(paths[i]);
                        Array.Sort(sub, StringComparer.CurrentCultureIgnoreCase);
                        for (int j = 0; j < sub.Length; j++)
                            if (MediaExts.Contains(System.IO.Path.GetExtension(sub[j]))) _playlist.Add(sub[j]);
                    }
                    else if (File.Exists(paths[i]))
                    {
                        _playlist.Add(System.IO.Path.GetFullPath(paths[i]));
                    }
                }
            }

            _lstPlaylist.Items.Clear();
            for (int i = 0; i < _playlist.Count; i++)
                _lstPlaylist.Items.Add(System.IO.Path.GetFileName(_playlist[i]));

            if (_playlist.Count > 0 && autoPlay) PlayIndex(startIdx);
        }

        private void PlayIndex(int idx)
        {
            if (idx < 0 || idx >= _playlist.Count) return;
            _playIdx = idx;
            string file = _playlist[idx];
            string name = System.IO.Path.GetFileName(file);
            bool audioOnly = AudioOnlyExts.Contains(System.IO.Path.GetExtension(file));

            _lstPlaylist.SelectedIndex = idx;
            _lblNowPlaying.Text = name;
            this.Title = name + " — NillyMedia Lite";

            _audioOverlay.Visibility = audioOnly ? Visibility.Visible : Visibility.Collapsed;
            _lblAudioTitle.Text = name;
            _lblAudioMeta.Text = string.Format("{0:0.00} MB · {1} ({2}/{3})", new FileInfo(file).Length / (1024.0 * 1024.0), System.IO.Path.GetExtension(file).TrimStart('.').ToUpperInvariant(), idx + 1, _playlist.Count);

            _media.Stop();
            _media.Source = new Uri(file);
            _media.SpeedRatio = _speeds[_speedIdx];
            _media.Play();
            _isPlaying = true;
            _btnPlay.Content = MakeIcon(ICO_PAUSE, BgDarkInk, false);
            SyncTimerState();
        }

        private void SyncTimerState()
        {
            if (_timer == null) return;
            bool need = _rec.IsRecording || (_isPlaying && this.WindowState != WindowState.Minimized);
            if (need && !_timer.IsEnabled) _timer.Start();
            else if (!need && _timer.IsEnabled) _timer.Stop();
        }

        private void OnMediaOpened(object sender, RoutedEventArgs e)
        {
            if (_media.NaturalDuration.HasTimeSpan)
            {
                TimeSpan tot = _media.NaturalDuration.TimeSpan;
                _seekSlider.Maximum = Math.Max(1, tot.TotalSeconds);
                _lblTimeTot.Text = FormatTime(tot);
            }
            if (_media.NaturalVideoWidth > 0)
                _audioOverlay.Visibility = Visibility.Collapsed;
        }

        private void OnMediaEnded(object sender, RoutedEventArgs e)
        {
            if (_isLoop)
            {
                _media.Position = TimeSpan.Zero;
                _media.Play();
            }
            else if (_playIdx + 1 < _playlist.Count)
            {
                PlayIndex(_playIdx + 1);
            }
            else
            {
                _isPlaying = false;
                _btnPlay.Content = MakeIcon(ICO_PLAY, BgDarkInk, true);
                SyncTimerState();
            }
        }

        private void TogglePlayPause()
        {
            if (_playlist.Count == 0) { PromptOpenFiles(); return; }
            if (_isPlaying) { _media.Pause(); _isPlaying = false; _btnPlay.Content = MakeIcon(ICO_PLAY, BgDarkInk, true); }
            else { _media.Play(); _isPlaying = true; _btnPlay.Content = MakeIcon(ICO_PAUSE, BgDarkInk, false); }
            SyncTimerState();
        }

        private void StopPlayback()
        {
            try { _media.Stop(); } catch { }
            _isPlaying = false;
            _btnPlay.Content = MakeIcon(ICO_PLAY, BgDarkInk, true);
            _seekSlider.Value = 0;
            _lblTimeCur.Text = "00:00";
            SyncTimerState();
        }

        private void PlayNext() { if (_playlist.Count > 0) PlayIndex((_playIdx + 1) % _playlist.Count); }
        private void PlayPrev() { if (_playlist.Count > 0) PlayIndex((_playIdx - 1 + _playlist.Count) % _playlist.Count); }

        private void SeekRelative(double sec)
        {
            if (!_media.NaturalDuration.HasTimeSpan) return;
            double next = Math.Max(0, Math.Min(_media.NaturalDuration.TimeSpan.TotalSeconds, _media.Position.TotalSeconds + sec));
            _media.Position = TimeSpan.FromSeconds(next);
        }

        private void CycleSpeed()
        {
            _speedIdx = (_speedIdx + 1) % _speeds.Length;
            _media.SpeedRatio = _speeds[_speedIdx];
            _btnSpeed.Content = _speeds[_speedIdx].ToString("0.##") + "×";
        }

        private void ToggleLoop()
        {
            _isLoop = !_isLoop;
            _btnLoop.Content = MakeIconLabel(ICO_LOOP, "Loop", _isLoop ? AccentAmber : FgText, false);
            _btnLoop.BorderBrush = _isLoop ? AccentAmber : BorderSub;
        }

        private void ToggleMute()
        {
            _media.IsMuted = !_media.IsMuted;
            _btnMute.Content = MakeIcon(_media.IsMuted ? ICO_MUTE : ICO_VOL, _media.IsMuted ? AccentAmber : FgText, false);
        }

        private void TogglePlaylist()
        {
            _playlistPane.Visibility = (_playlistPane.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void TogglePin()
        {
            this.Topmost = !this.Topmost;
            _btnPin.Content = MakeIcon(ICO_PIN, this.Topmost ? AccentAmber : FgMuted, false);
            _btnPin.BorderBrush = this.Topmost ? AccentAmber : BorderSub;
        }

        private void ToggleFullscreen()
        {
            if (!_isFullscreen)
            {
                _savedState = this.WindowState;
                _savedStyle = this.WindowStyle;
                _topBar.Visibility = Visibility.Collapsed;
                this.WindowStyle = WindowStyle.None;
                this.WindowState = WindowState.Maximized;
                _isFullscreen = true;
            }
            else
            {
                this.WindowStyle = _savedStyle;
                this.WindowState = _savedState;
                _topBar.Visibility = Visibility.Visible;
                _isFullscreen = false;
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_isPlaying && !_isSeeking && _media.NaturalDuration.HasTimeSpan)
            {
                _seekSlider.Value = _media.Position.TotalSeconds;
                _lblTimeCur.Text = FormatTime(_media.Position);
            }
            if (_rec.IsRecording)
            {
                TimeSpan el = _rec.Elapsed;
                _lblRecTimer.Text = string.Format("{0:00}:{1:00}.{2}", (int)el.TotalMinutes, el.Seconds, el.Milliseconds / 100);
                _vuFill.Width = Math.Round(_rec.GetLiveLevel01() * 360.0);
            }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isFullscreen) { ToggleFullscreen(); e.Handled = true; }
            else if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
            else if (e.Key == Key.Space && _playerRoot.Visibility == Visibility.Visible) { TogglePlayPause(); e.Handled = true; }
            else if (e.Key == Key.Right && _playerRoot.Visibility == Visibility.Visible) { SeekRelative(5); e.Handled = true; }
            else if (e.Key == Key.Left && _playerRoot.Visibility == Visibility.Visible) { SeekRelative(-5); e.Handled = true; }
            else if (e.Key == Key.Up && _playerRoot.Visibility == Visibility.Visible) { _volSlider.Value = Math.Min(1.0, _volSlider.Value + 0.08); e.Handled = true; }
            else if (e.Key == Key.Down && _playerRoot.Visibility == Visibility.Visible) { _volSlider.Value = Math.Max(0.0, _volSlider.Value - 0.08); e.Handled = true; }
            else if (e.Key == Key.M) { ToggleMute(); e.Handled = true; }
            else if (e.Key == Key.P) { TogglePlaylist(); e.Handled = true; }
            else if (e.Key == Key.T) { TogglePin(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O) { PromptOpenFiles(); e.Handled = true; }
        }

        private static string FormatTime(TimeSpan t)
        {
            return t.TotalHours >= 1
                ? string.Format("{0:0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                : string.Format("{0:00}:{1:00}", (int)t.TotalMinutes, t.Seconds);
        }

        private static UIElement MakeIcon(string data, Brush brush, bool filled)
        {
            System.Windows.Shapes.Path p = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data),
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (filled)
            {
                p.Fill = brush;
            }
            else
            {
                p.Stroke = brush;
                p.StrokeThickness = 1.5;
                p.StrokeStartLineCap = PenLineCap.Round;
                p.StrokeEndLineCap = PenLineCap.Round;
                p.StrokeLineJoin = PenLineJoin.Round;
            }
            return p;
        }

        private static UIElement MakeIconLabel(string data, string text, Brush fg, bool filled)
        {
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            UIElement ic = MakeIcon(data, fg, filled);
            if (ic is FrameworkElement) ((FrameworkElement)ic).Margin = new Thickness(0, 0, 6, 0);
            sp.Children.Add(ic);
            sp.Children.Add(new TextBlock { Text = text, Foreground = fg, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }

        private static ControlTemplate MakeRoundedTemplate(CornerRadius cr, Thickness borderTh)
        {
            ControlTemplate ct = new ControlTemplate(typeof(Button));
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.CornerRadiusProperty, cr);
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, borderTh);
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            ct.VisualTree = bd;
            return ct;
        }

        private static Button MakeTabBtn(string iconData, string label, bool active, bool filled)
        {
            return new Button
            {
                Content = MakeIconLabel(iconData, label, active ? FgText : FgMuted, filled),
                Foreground = active ? FgText : FgMuted,
                Background = Brushes.Transparent,
                BorderBrush = active ? AccentAmber : Brushes.Transparent,
                Template = MakeRoundedTemplate(new CornerRadius(0), new Thickness(0, 0, 0, 2.5)),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(2, 0, 2, 0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
        }

        private static Button MakePillBtn(object content, double width, bool primary)
        {
            Button b = new Button
            {
                Content = content,
                Height = 30,
                Background = primary ? AccentAmber : BgBtn,
                Foreground = primary ? BgDarkInk : FgText,
                BorderBrush = primary ? AccentAmber : BorderSub,
                Template = MakeRoundedTemplate(new CornerRadius(6), new Thickness(1)),
                Margin = new Thickness(2, 0, 2, 0),
                FontSize = 12,
                FontWeight = primary ? FontWeights.Bold : FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            if (width > 0) b.Width = width;
            return b;
        }
    }

    // ========================================================================
    // 4. ENTRY POINT & AUTOMATED QA SMOKE TEST
    // ========================================================================
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application app = new Application();
            if (args != null && args.Length > 0 && args[0] == "--test-smoke")
                return RunSmokeTest(app, args.Length > 1 ? args[1] : null);

            MainWindow win = new MainWindow(args);
            win.Loaded += delegate { Native.TrimMemory(); SuiteUpdater.AttachWpf(win, "nillymedia-lite", "NillyMedia-Lite", "Install_NillyMediaLite.ps1"); };
            app.Run(win);
            return 0;
        }

        private static int RunSmokeTest(Application app, string logPath)
        {
            StringBuilder sb = new StringBuilder();
            string tmpWav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nillymedia_qa.wav");
            try
            {
                // Generate tiny valid 0.3s 440Hz PCM WAV
                WriteTestWav(tmpWav);
                sb.AppendLine("TEST_WAV_CREATED=" + File.Exists(tmpWav));
                sb.AppendLine("MIC_DEVICES=" + Native.waveInGetNumDevs());

                MainWindow win = new MainWindow(new string[] { tmpWav });
                win.Show();
                win.SwitchMode(1);
                win.SwitchMode(0);
                long ramMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                sb.AppendLine("WINDOW_OPENED_OK=" + win.IsLoaded);
                sb.AppendLine("WORKING_SET_MB=" + ramMb);
                win.Close();
                sb.AppendLine("SMOKE_RESULT=PASS");
            }
            catch (Exception ex)
            {
                sb.AppendLine("SMOKE_RESULT=FAIL");
                sb.AppendLine(ex.ToString());
                if (!string.IsNullOrEmpty(logPath)) File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);
                return 1;
            }
            finally
            {
                try { if (File.Exists(tmpWav)) File.Delete(tmpWav); } catch { }
            }
            if (!string.IsNullOrEmpty(logPath)) File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);
            return 0;
        }

        private static void WriteTestWav(string path)
        {
            int sampleRate = 22050, samples = 6615;
            using (FileStream fs = File.Create(path))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                int dataLen = samples * 2;
                bw.Write(Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + dataLen);
                bw.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); bw.Write(16);
                bw.Write((short)1); bw.Write((short)1); bw.Write(sampleRate);
                bw.Write(sampleRate * 2); bw.Write((short)2); bw.Write((short)16);
                bw.Write(Encoding.ASCII.GetBytes("data")); bw.Write(dataLen);
                for (int i = 0; i < samples; i++)
                    bw.Write((short)(Math.Sin(2.0 * Math.PI * 440.0 * i / sampleRate) * 8000));
            }
        }
    }

    internal static class SuiteUpdater
    {
        public static void AttachWpf(System.Windows.Window win, string repoName, string pkgPrefix, string installerScript)
        {
            if (win == null) return;
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                try
                {
                    System.Threading.Thread.Sleep(2200);
                    string curTag, newTag;
                    if (!CheckForUpdate(repoName, out curTag, out newTag)) return;
                    win.Dispatcher.BeginInvoke((System.Action)delegate
                    {
                        System.Windows.Controls.Grid g = win.Content as System.Windows.Controls.Grid;
                        if (g == null) return;
                        g.RowDefinitions.Insert(0, new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                        for (int i = 0; i < g.Children.Count; i++)
                            System.Windows.Controls.Grid.SetRow(g.Children[i], System.Windows.Controls.Grid.GetRow(g.Children[i]) + 1);

                        System.Windows.Controls.Border bar = new System.Windows.Controls.Border
                        {
                            Height = 28,
                            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 29, 18)),
                            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(252, 150, 31)),
                            BorderThickness = new System.Windows.Thickness(0, 0, 0, 1),
                            Padding = new System.Windows.Thickness(10, 0, 8, 0)
                        };
                        System.Windows.Controls.DockPanel dp = new System.Windows.Controls.DockPanel { LastChildFill = true };
                        System.Windows.Controls.Button btnX = new System.Windows.Controls.Button
                        {
                            Content = "×", Width = 24, Background = System.Windows.Media.Brushes.Transparent,
                            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 170, 130)),
                            BorderThickness = new System.Windows.Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
                            FontWeight = System.Windows.FontWeights.Bold
                        };
                        System.Windows.Controls.DockPanel.SetDock(btnX, System.Windows.Controls.Dock.Right);
                        btnX.Click += delegate { bar.Visibility = System.Windows.Visibility.Collapsed; };

                        System.Windows.Controls.Button btnU = new System.Windows.Controls.Button
                        {
                            Content = "Ενημέρωση", Width = 88, Height = 22, Margin = new System.Windows.Thickness(6, 0, 4, 0),
                            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(252, 150, 31)),
                            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 22, 26)),
                            BorderThickness = new System.Windows.Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
                            FontWeight = System.Windows.FontWeights.Bold, FontSize = 11.5
                        };
                        System.Windows.Controls.DockPanel.SetDock(btnU, System.Windows.Controls.Dock.Right);

                        System.Windows.Controls.TextBlock lbl = new System.Windows.Controls.TextBlock
                        {
                            Text = "Νέα έκδοση " + newTag + " διαθέσιμη (έχεις " + curTag + ")",
                            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 185, 95)),
                            VerticalAlignment = System.Windows.VerticalAlignment.Center, FontSize = 12
                        };
                        btnU.Click += delegate
                        {
                            btnU.IsEnabled = false; btnU.Content = "Λήψη...";
                            System.Threading.Thread uth = new System.Threading.Thread(delegate()
                            {
                                try
                                {
                                    ApplySelfUpdate(repoName, pkgPrefix, installerScript,
                                        delegate(string st) { try { win.Dispatcher.BeginInvoke((System.Action)delegate { lbl.Text = st; }); } catch { } },
                                        delegate { try { win.Dispatcher.BeginInvoke((System.Action)delegate { win.Close(); }); } catch { } });
                                }
                                catch (System.Exception ex)
                                {
                                    try { win.Dispatcher.BeginInvoke((System.Action)delegate { lbl.Text = "Σφάλμα: " + ex.Message; btnU.Content = "Ενημέρωση"; btnU.IsEnabled = true; }); } catch { }
                                }
                            });
                            uth.IsBackground = true; uth.Start();
                        };
                        dp.Children.Add(btnX); dp.Children.Add(btnU); dp.Children.Add(lbl);
                        bar.Child = dp;
                        System.Windows.Controls.Grid.SetRow(bar, 0);
                        g.Children.Add(bar);
                    });
                }
                catch { }
            });
            th.IsBackground = true; th.Start();
        }
        private const string CATALOG_URL = "https://raw.githubusercontent.com/nillynp/nillyhub/main/catalog.json";

        private static bool TryParseVer(string raw, out System.Version ver)
        {
            ver = null;
            if (string.IsNullOrEmpty(raw)) return false;
            string s = raw.Trim();
            if (s.StartsWith("v", System.StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            string[] p = s.Split('.');
            if (p.Length < 2 || p.Length > 4) return false;
            int maj, min, pat = 0;
            if (!int.TryParse(p[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out maj)) return false;
            if (!int.TryParse(p[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out min)) return false;
            if (p.Length >= 3 && !int.TryParse(p[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out pat)) return false;
            ver = new System.Version(maj, min, pat, 0);
            return true;
        }

        private static string FmtVer(System.Version v)
        {
            return v.Build > 0
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "v{0}.{1}.{2}", v.Major, v.Minor, v.Build)
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, "v{0}.{1}", v.Major, v.Minor);
        }

        public static bool CheckForUpdate(string repoName, out string curTag, out string newTag)
        {
            curTag = null; newTag = null;
            System.Version rawCur = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            System.Version curVer = new System.Version(rawCur.Major, rawCur.Minor, rawCur.Build >= 0 ? rawCur.Build : 0, 0);
            curTag = FmtVer(curVer);

            string suiteDir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "NillySuite");
            string cachePath = System.IO.Path.Combine(suiteDir, ".nillyhub_catalog.json");
            string json = null;
            try
            {
                if (System.IO.File.Exists(cachePath))
                {
                    System.DateTime wt = System.IO.File.GetLastWriteTimeUtc(cachePath);
                    if ((System.DateTime.UtcNow - wt).TotalHours < 24.0)
                    {
                        string cached = System.IO.File.ReadAllText(cachePath, System.Text.Encoding.UTF8);
                        if (cached.IndexOf("\"latestVersion\"", System.StringComparison.Ordinal) >= 0) json = cached;
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(json))
            {
                json = HttpGetText(CATALOG_URL, 5000);
                if (string.IsNullOrEmpty(json) || json.Length > 256 * 1024) return false;
                try
                {
                    System.IO.Directory.CreateDirectory(suiteDir);
                    System.IO.File.WriteAllText(cachePath, json, new System.Text.UTF8Encoding(false));
                }
                catch { }
            }

            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                json,
                "\"repo\"\\s*:\\s*\"" + System.Text.RegularExpressions.Regex.Escape(repoName) + "\"[^\\}]{0,500}?\"latestVersion\"\\s*:\\s*\"(v[0-9]+\\.[0-9]+(?:\\.[0-9]+)?)\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            System.Version remVer;
            if (!TryParseVer(m.Groups[1].Value, out remVer) || remVer <= curVer) return false;
            newTag = FmtVer(remVer);
            return true;
        }

        private static string HttpGetText(string url, int timeoutMs)
        {
            try { System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072 | System.Net.SecurityProtocolType.Tls12; } catch { }
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.UserAgent = "NillySuite-Updater/1.0 (https://nillygraphics.com/)";
            req.Accept = "application/vnd.github+json, application/json";
            req.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
            using (System.IO.Stream st = resp.GetResponseStream())
            using (System.IO.StreamReader sr = new System.IO.StreamReader(st, System.Text.Encoding.UTF8))
                return sr.ReadToEnd();
        }

        private static string ExtractAssetBlock(string json, string assetName)
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                json, "\"name\"\\s*:\\s*\"" + System.Text.RegularExpressions.Regex.Escape(assetName) + "\"");
            if (!m.Success) return null;
            int start = m.Index;
            int depth = 0;
            while (start > 0)
            {
                char c = json[start];
                if (c == '}') depth++;
                else if (c == '{') { if (depth == 0) break; depth--; }
                start--;
            }
            int end = start;
            depth = 0;
            bool inStr = false;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) inStr = !inStr;
                if (inStr) continue;
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) { end = i; break; } }
            }
            return end > start ? json.Substring(start, end - start + 1) : null;
        }

        public static void ApplySelfUpdate(string repoName, string pkgPrefix, string installerScript, System.Action<string> onStatus, System.Action onExit)
        {
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string appDir = System.IO.Path.GetDirectoryName(exePath);
            if (System.IO.Directory.Exists(System.IO.Path.Combine(appDir, ".git")))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/nillynp/" + repoName + "/releases/latest") { UseShellExecute = true });
                if (onStatus != null) onStatus("Άνοιξε το GitHub Release (φάκελος Git).");
                return;
            }

            if (onStatus != null) onStatus("Έλεγχος SHA-256 στο GitHub...");
            string relJson = HttpGetText("https://api.github.com/repos/nillynp/" + repoName + "/releases/latest", 12000);
            if (string.IsNullOrEmpty(relJson)) throw new System.IO.InvalidDataException("Κενή απόκριση GitHub.");
            if (System.Text.RegularExpressions.Regex.IsMatch(relJson, "\"draft\"\\s*:\\s*true") ||
                System.Text.RegularExpressions.Regex.IsMatch(relJson, "\"prerelease\"\\s*:\\s*true"))
                throw new System.IO.InvalidDataException("Μη σταθερή έκδοση.");

            System.Text.RegularExpressions.Match tm = System.Text.RegularExpressions.Regex.Match(relJson, "\"tag_name\"\\s*:\\s*\"(v[0-9]+\\.[0-9]+(?:\\.[0-9]+)?)\"");
            if (!tm.Success) throw new System.IO.InvalidDataException("Μη έγκυρο tag έκδοσης.");
            string tag = tm.Groups[1].Value;

            System.Version curRaw = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            System.Version curVer = new System.Version(curRaw.Major, curRaw.Minor, curRaw.Build >= 0 ? curRaw.Build : 0, 0);
            System.Version remVer;
            if (!TryParseVer(tag, out remVer) || remVer <= curVer)
                throw new System.InvalidOperationException("Έχεις ήδη την τελευταία έκδοση.");

            string zipName = pkgPrefix + "-" + tag + "-Portable.zip";
            string expectedUrl = "https://github.com/nillynp/" + repoName + "/releases/download/" + tag + "/" + zipName;
            string blk = ExtractAssetBlock(relJson, zipName);
            if (string.IsNullOrEmpty(blk)) throw new System.IO.FileNotFoundException("Δεν βρέθηκε το Portable ZIP.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(blk, "\"state\"\\s*:\\s*\"uploaded\""))
                throw new System.IO.InvalidDataException("Το αρχείο δεν είναι έτοιμο.");
            System.Text.RegularExpressions.Match um = System.Text.RegularExpressions.Regex.Match(blk, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"");
            if (!um.Success || !string.Equals(um.Groups[1].Value, expectedUrl, System.StringComparison.Ordinal))
                throw new System.IO.InvalidDataException("Μη έγκυρο URL λήψης.");
            System.Text.RegularExpressions.Match dm = System.Text.RegularExpressions.Regex.Match(blk, "\"digest\"\\s*:\\s*\"sha256:([0-9a-f]{64})\"");
            if (!dm.Success) throw new System.IO.InvalidDataException("Απουσιάζει SHA-256.");
            string expectedSha = dm.Groups[1].Value;

            string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NillyUpd-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmpDir);
            string zipFile = System.IO.Path.Combine(tmpDir, "pkg.zip");

            if (onStatus != null) onStatus("Λήψη " + tag + "...");
            try { System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072 | System.Net.SecurityProtocolType.Tls12; } catch { }
            System.Net.HttpWebRequest dreq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(expectedUrl);
            dreq.UserAgent = "NillySuite-Updater/1.0 (https://nillygraphics.com/)";
            dreq.Timeout = 20000;
            dreq.ReadWriteTimeout = 30000;
            using (System.Net.HttpWebResponse dresp = (System.Net.HttpWebResponse)dreq.GetResponse())
            using (System.IO.Stream src = dresp.GetResponseStream())
            using (System.IO.FileStream dst = new System.IO.FileStream(zipFile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
            {
                byte[] buf = new byte[16384];
                int n; long total = 0;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    total += n;
                    if (total > 32L * 1024L * 1024L) throw new System.IO.InvalidDataException("Υπέρβαση μεγέθους ZIP.");
                    dst.Write(buf, 0, n);
                }
            }

            if (onStatus != null) onStatus("Επαλήθευση SHA-256...");
            string actualSha;
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            using (System.IO.FileStream fs = System.IO.File.OpenRead(zipFile))
            {
                byte[] hash = sha.ComputeHash(fs);
                System.Text.StringBuilder sb = new System.Text.StringBuilder(64);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                actualSha = sb.ToString();
            }
            if (!string.Equals(actualSha, expectedSha, System.StringComparison.OrdinalIgnoreCase))
            {
                try { System.IO.Directory.Delete(tmpDir, true); } catch { }
                throw new System.IO.InvalidDataException("Αποτυχία ελέγχου SHA-256.");
            }

            if (onStatus != null) onStatus("Εγκατάσταση " + tag + " & επανεκκίνηση...");
            string ps1 = System.IO.Path.Combine(tmpDir, "apply.ps1");
            string script =
                "param($myPid,$zip,$appDir,$instName,$exePath,$tag)\r\n" +
                "for($i=0;$i -lt 60;$i++){ if(-not (Get-Process -Id $myPid -ErrorAction SilentlyContinue)){break}; Start-Sleep -Milliseconds 150 }\r\n" +
                "Add-Type -AssemblyName System.IO.Compression.FileSystem\r\n" +
                "$za = [System.IO.Compression.ZipFile]::OpenRead($zip)\r\n" +
                "try {\r\n" +
                "  $pref = $null; $shared = $true\r\n" +
                "  foreach($e in $za.Entries){ $fn=$e.FullName -replace '\\\\','/'; $sl=$fn.IndexOf('/'); if($sl -le 0){$shared=$false;break}; $p=$fn.Substring(0,$sl+1); if(-not $pref){$pref=$p}elseif(-not $pref.Equals($p,[StringComparison]::OrdinalIgnoreCase)){$shared=$false;break} }\r\n" +
                "  $rootFull = [System.IO.Path]::GetFullPath($appDir).TrimEnd('\\') + '\\'\r\n" +
                "  foreach($e in $za.Entries){\r\n" +
                "    $rel = $e.FullName -replace '\\\\','/'\r\n" +
                "    if($shared -and $pref -and $rel.StartsWith($pref,[StringComparison]::OrdinalIgnoreCase)){ $rel=$rel.Substring($pref.Length) }\r\n" +
                "    if([string]::IsNullOrEmpty($rel) -or $rel.EndsWith('/') -or $rel.Contains(':')){ continue }\r\n" +
                "    $dst = [System.IO.Path]::GetFullPath((Join-Path $appDir ($rel -replace '/','\\')))\r\n" +
                "    if(-not $dst.StartsWith($rootFull,[StringComparison]::OrdinalIgnoreCase)){ continue }\r\n" +
                "    $par = Split-Path -Parent $dst; if($par){ New-Item -ItemType Directory -Force -Path $par | Out-Null }\r\n" +
                "    for($k=0;$k -lt 5;$k++){ try { [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e,$dst,$true); break } catch { Start-Sleep -Milliseconds 250 } }\r\n" +
                "  }\r\n" +
                "} finally { $za.Dispose() }\r\n" +
                "$bak = $exePath + '.bak'\r\n" +
                "if(Test-Path $exePath){ Copy-Item -LiteralPath $exePath -Destination $bak -Force -ErrorAction SilentlyContinue; for($k=0;$k -lt 5;$k++){ try { Remove-Item -LiteralPath $exePath -Force -ErrorAction Stop; break } catch { Start-Sleep -Milliseconds 250 } } }\r\n" +
                "Get-ChildItem (Join-Path $appDir 'desktop') -Filter '*.cs' -ErrorAction SilentlyContinue | ForEach-Object { $_.LastWriteTimeUtc = [DateTime]::UtcNow }\r\n" +
                "if($instName -and (Test-Path (Join-Path $appDir $instName))){ & (Join-Path $appDir $instName) -Silent 2>$null | Out-Null }\r\n" +
                "if(-not (Test-Path $exePath) -and (Test-Path $bak)){ Move-Item -LiteralPath $bak -Destination $exePath -Force -ErrorAction SilentlyContinue } else { Remove-Item -LiteralPath $bak -Force -ErrorAction SilentlyContinue }\r\n" +
                "if($tag){ Set-Content -LiteralPath (Join-Path $appDir '.nilly_version') -Value $tag -Encoding UTF8 -ErrorAction SilentlyContinue }\r\n" +
                "if(Test-Path $exePath){ Start-Process -FilePath $exePath -WorkingDirectory $appDir }\r\n" +
                "Start-Sleep -Milliseconds 400\r\n" +
                "Remove-Item -LiteralPath (Split-Path $zip) -Recurse -Force -ErrorAction SilentlyContinue\r\n";
            System.IO.File.WriteAllText(ps1, script, new System.Text.UTF8Encoding(false));

            int myPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(
                "powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1 + "\" " +
                myPid + " \"" + zipFile + "\" \"" + appDir + "\" \"" + installerScript + "\" \"" + exePath + "\" \"" + tag + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            System.Diagnostics.Process.Start(psi);
            if (onExit != null) onExit();
        }
    }
}
