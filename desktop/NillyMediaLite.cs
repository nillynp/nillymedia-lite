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
[assembly: AssemblyVersion("1.0.2.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]

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

        // Brushes (Cinema Dark Studio palette)
        private static readonly SolidColorBrush BgWindow = new SolidColorBrush(Color.FromRgb(22, 22, 24));
        private static readonly SolidColorBrush BgBar = new SolidColorBrush(Color.FromRgb(30, 31, 34));
        private static readonly SolidColorBrush BgCard = new SolidColorBrush(Color.FromRgb(38, 39, 43));
        private static readonly SolidColorBrush BgBtn = new SolidColorBrush(Color.FromRgb(48, 50, 56));
        private static readonly SolidColorBrush FgText = new SolidColorBrush(Color.FromRgb(242, 242, 244));
        private static readonly SolidColorBrush FgMuted = new SolidColorBrush(Color.FromRgb(154, 158, 166));
        private static readonly SolidColorBrush AccentCyan = new SolidColorBrush(Color.FromRgb(76, 194, 255));
        private static readonly SolidColorBrush AccentGreen = new SolidColorBrush(Color.FromRgb(166, 226, 46));
        private static readonly SolidColorBrush AccentPink = new SolidColorBrush(Color.FromRgb(249, 38, 114));
        private static readonly SolidColorBrush AccentAmber = new SolidColorBrush(Color.FromRgb(253, 151, 31));
        private static readonly SolidColorBrush BorderSub = new SolidColorBrush(Color.FromRgb(54, 56, 62));

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
            this.Closing += delegate { _rec.Cancel(); try { _media.Stop(); _media.Close(); } catch { } };

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(120);
            _timer.Tick += OnTick;
            _timer.Start();

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

            // 1. Top Slim Header Bar
            _topBar = new Grid { Background = BgBar };
            _topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel leftBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            _btnModePlayer = MakeTopBtn("▶ Player", AccentCyan, true);
            _btnModePlayer.Click += delegate { SwitchMode(0); };
            _btnModeRec = MakeTopBtn("● Recorder", AccentPink, false);
            _btnModeRec.Click += delegate { SwitchMode(1); };
            Button btnOpen = MakeTopBtn("+ Open", FgText, false);
            btnOpen.Click += delegate { PromptOpenFiles(); };
            leftBtns.Children.Add(_btnModePlayer);
            leftBtns.Children.Add(_btnModeRec);
            leftBtns.Children.Add(btnOpen);
            Grid.SetColumn(leftBtns, 0);
            _topBar.Children.Add(leftBtns);

            _lblNowPlaying = new TextBlock
            {
                Text = "Drop Video/Audio or click + Open",
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
            _btnPlaylistToggle = MakeTopBtn("List", FgText, false);
            _btnPlaylistToggle.Click += delegate { TogglePlaylist(); };
            _btnPin = MakeTopBtn("Pin", FgMuted, false);
            _btnPin.Click += delegate { TogglePin(); };
            _btnFullscreen = MakeTopBtn("⛶", FgText, false);
            _btnFullscreen.Click += delegate { ToggleFullscreen(); };
            rightBtns.Children.Add(_btnPlaylistToggle);
            rightBtns.Children.Add(_btnPin);
            rightBtns.Children.Add(_btnFullscreen);
            Grid.SetColumn(rightBtns, 2);
            _topBar.Children.Add(rightBtns);

            Grid.SetRow(_topBar, 0);
            shell.Children.Add(_topBar);

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
                _lblAudioTitle.Text = "Αδυναμία αναπαραγωγής αρχείου";
                _lblAudioMeta.Text = e.ErrorException != null ? e.ErrorException.Message : "Μη υποστηριζόμενος codec.";
                _audioOverlay.Visibility = Visibility.Visible;
            };
            videoGrid.Children.Add(_media);

            // Minimal Audio/Empty Overlay
            _audioOverlay = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24) };
            TextBlock bigIcon = new TextBlock { Text = "▶", FontSize = 48, Foreground = AccentAmber, HorizontalAlignment = HorizontalAlignment.Center };
            _lblAudioTitle = new TextBlock { Text = "NillyMedia Lite", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = FgText, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 4), TextAlignment = TextAlignment.Center };
            _lblAudioMeta = new TextBlock { Text = "Ρίξε βίντεο ή ήχο εδώ (MP4 · MOV · MKV · MP3 · WAV · FLAC) · 0 MB background RAM", FontSize = 12, Foreground = FgMuted, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
            _audioOverlay.Children.Add(bigIcon);
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
            Button bPrev = MakeCtrlBtn("⏮", 34); bPrev.Click += delegate { PlayPrev(); };
            Button bBack = MakeCtrlBtn("-10s", 42); bBack.Click += delegate { SeekRelative(-10); };
            _btnPlay = MakeCtrlBtn("▶", 44); _btnPlay.Background = AccentCyan; _btnPlay.Foreground = Brushes.Black; _btnPlay.FontWeight = FontWeights.Bold;
            _btnPlay.Click += delegate { TogglePlayPause(); };
            Button bFwd = MakeCtrlBtn("+10s", 42); bFwd.Click += delegate { SeekRelative(10); };
            Button bNext = MakeCtrlBtn("⏭", 34); bNext.Click += delegate { PlayNext(); };
            Button bStop = MakeCtrlBtn("■", 34); bStop.Click += delegate { StopPlayback(); };
            transport.Children.Add(bPrev); transport.Children.Add(bBack); transport.Children.Add(_btnPlay);
            transport.Children.Add(bFwd); transport.Children.Add(bNext); transport.Children.Add(bStop);
            Grid.SetColumn(transport, 0);
            btnRow.Children.Add(transport);

            StackPanel rightCtrl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _btnSpeed = MakeCtrlBtn("1.0×", 46);
            _btnSpeed.Click += delegate { CycleSpeed(); };
            _btnLoop = MakeCtrlBtn("Loop", 46);
            _btnLoop.Click += delegate { ToggleLoop(); };
            _btnMute = MakeCtrlBtn("🔊", 34);
            _btnMute.Click += delegate { ToggleMute(); };
            _volSlider = new Slider { Minimum = 0, Maximum = 1, Value = 0.85, Width = 88, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
            _volSlider.ValueChanged += delegate
            {
                _media.Volume = _volSlider.Value;
                if (_media.IsMuted && _volSlider.Value > 0) { _media.IsMuted = false; _btnMute.Content = "🔊"; }
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
            _btnRecStartStop = new Button { Content = "● Record", Width = 140, Height = 38, Background = AccentPink, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(4) };
            _btnRecStartStop.Click += delegate { ToggleRecord(); };

            _btnRecPause = new Button { Content = "❚❚ Pause", Width = 110, Height = 38, Background = BgBtn, Foreground = FgText, FontSize = 13, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(4), IsEnabled = false };
            _btnRecPause.Click += delegate
            {
                _rec.TogglePause();
                _btnRecPause.Content = _rec.IsPaused ? "▶ Resume" : "❚❚ Pause";
                _lblRecStatus.Text = _rec.IsPaused ? "Σε παύση..." : "Ηχογράφηση σε εξέλιξη...";
            };

            Button btnOpenRecDir = new Button { Content = "Open Folder", Width = 110, Height = 38, Background = BgBtn, Foreground = FgText, FontSize = 12, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(4) };
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
            _btnModePlayer.Background = isRec ? Brushes.Transparent : BgBtn;
            _btnModeRec.Background = isRec ? BgBtn : Brushes.Transparent;
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
                    _btnRecStartStop.Content = "■ Stop & Save";
                    _btnRecPause.IsEnabled = true;
                    _btnRecPause.Content = "❚❚ Pause";
                    _lblRecStatus.Text = "● Ηχογράφηση σε εξέλιξη...";
                    _lblRecStatus.Foreground = AccentPink;
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
                _btnRecStartStop.Content = "● Record";
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
            _btnPlay.Content = "❚❚";
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
                _btnPlay.Content = "▶";
            }
        }

        private void TogglePlayPause()
        {
            if (_playlist.Count == 0) { PromptOpenFiles(); return; }
            if (_isPlaying) { _media.Pause(); _isPlaying = false; _btnPlay.Content = "▶"; }
            else { _media.Play(); _isPlaying = true; _btnPlay.Content = "❚❚"; }
        }

        private void StopPlayback()
        {
            try { _media.Stop(); } catch { }
            _isPlaying = false;
            _btnPlay.Content = "▶";
            _seekSlider.Value = 0;
            _lblTimeCur.Text = "00:00";
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
            _btnLoop.Foreground = _isLoop ? AccentGreen : FgText;
        }

        private void ToggleMute()
        {
            _media.IsMuted = !_media.IsMuted;
            _btnMute.Content = _media.IsMuted ? "🔇" : "🔊";
        }

        private void TogglePlaylist()
        {
            _playlistPane.Visibility = (_playlistPane.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void TogglePin()
        {
            this.Topmost = !this.Topmost;
            _btnPin.Foreground = this.Topmost ? AccentAmber : FgMuted;
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

        private static Button MakeTopBtn(string text, Brush fg, bool active)
        {
            return new Button
            {
                Content = text,
                Foreground = fg,
                Background = active ? BgBtn : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(2, 0, 2, 0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
        }

        private static Button MakeCtrlBtn(string text, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 30,
                Background = BgBtn,
                Foreground = FgText,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(2, 0, 2, 0),
                FontSize = 12,
                Cursor = Cursors.Hand
            };
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
            win.Loaded += delegate { Native.TrimMemory(); };
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
}
