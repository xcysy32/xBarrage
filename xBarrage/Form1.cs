using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

// ================== 别名定义 ==================
using D2D = GameOverlay.Drawing;
using OverlayWin = GameOverlay.Windows;
using SysDraw = System.Drawing;

namespace DanmakuClient
{
    // 纯数据类
    public class DanmakuItem : IDisposable
    {
        public string Text { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public int Track { get; set; }
        public D2D.SolidBrush? Brush { get; set; }

        public void Dispose()
        {
            Brush?.Dispose();
        }
    }

    public partial class Form1 : Form
    {
        // ===== 弹幕设置 =====
        public static float DanmakuSpeed = 6.0f;
        public static int DanmakuFontSize = 25;
        public static bool RandomColor = true;
        public static SysDraw.Color DanmakuFixedColor = SysDraw.Color.White;
        public static int DanmakuOpacity = 255;
        public static string? WebSocketUrl = null;

        // ===== 布局策略 =====
        private const int TopMargin = 80;
        private const int BottomMargin = 20;
        private const float AvoidCenterTopPercent = 0.35f;
        private const float AvoidCenterBottomPercent = 0.75f;

        // ===== GameOverlay 组件 =====
        private readonly OverlayWin.GraphicsWindow _window;
        private readonly D2D.Graphics _graphics;

        private D2D.Font? _mainFont;
        private D2D.SolidBrush? _shadowBrush;

        // 【新增】用于检测字号是否发生变化
        private int _currentFontSizeInUse = 0;

        // ===== 逻辑组件 =====
        private readonly Random _rand = new();
        private readonly List<DanmakuItem> _items = new();
        private readonly object _queueLock = new object();
        private readonly Queue<string> _danmakuQueue = new();

        private NotifyIcon _tray = new();
        private ClientWebSocket? _ws;

        private int _trackHeight = 40;
        private int _trackCount;
        private float[]? _trackRightEdge;
        private const int TrackGap = 80;

        private readonly string _settingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DanmakuClientSettings.json"
        );

        public Form1()
        {
            LoadSettings();
            InitializeComponent();
            InitTray();

            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
            this.Visible = false;

            // ===== GameOverlay 初始化 =====
            var bounds = Screen.PrimaryScreen.Bounds;

            _graphics = new D2D.Graphics()
            {
                MeasureFPS = false,
                PerPrimitiveAntiAliasing = true,
                TextAntiAliasing = true,
                UseMultiThreadedFactories = false,
                VSync = true
            };

            _window = new OverlayWin.GraphicsWindow(bounds.X, bounds.Y, bounds.Width, bounds.Height - 1, _graphics)
            {
                FPS = 130,
                IsTopmost = true,
                IsVisible = true
            };

            _window.SetupGraphics += OnSetupGraphics;
            _window.DestroyGraphics += OnDestroyGraphics;
            _window.DrawGraphics += OnDrawGraphics;

            _window.Create();

            if (!string.IsNullOrWhiteSpace(WebSocketUrl))
                _ = ConnectWebSocket();
        }

        private void OnSetupGraphics(object? sender, OverlayWin.SetupGraphicsEventArgs e)
        {
            var gfx = e.Graphics;

            // 初始化资源
            RecreateFontAndLayout(gfx);

            _shadowBrush = gfx.CreateSolidBrush(0, 0, 0, 200);
        }

        // 【核心修复】将创建字体和计算轨道的逻辑提取出来
        private void RecreateFontAndLayout(D2D.Graphics gfx)
        {
            // 销毁旧字体
            _mainFont?.Dispose();

            // 创建新字体
            _mainFont = gfx.CreateFont("Microsoft YaHei", DanmakuFontSize, true);

            // 更新当前使用的字号记录
            _currentFontSizeInUse = DanmakuFontSize;

            // 重新计算轨道高度和数量
            _trackHeight = DanmakuFontSize + 15;
            _trackCount = Math.Max(1, (_window.Height - TopMargin - BottomMargin) / _trackHeight);

            // 重置轨道占用数组
            _trackRightEdge = new float[_trackCount];
        }

        private void OnDestroyGraphics(object? sender, OverlayWin.DestroyGraphicsEventArgs e)
        {
            _mainFont?.Dispose();
            _shadowBrush?.Dispose();
            foreach (var item in _items) item.Dispose();
        }

        private void OnDrawGraphics(object? sender, OverlayWin.DrawGraphicsEventArgs e)
        {
            var gfx = e.Graphics;

            // 【核心修复】每一帧检查：如果设置的字号变了，立刻重建字体和轨道
            if (_currentFontSizeInUse != DanmakuFontSize)
            {
                RecreateFontAndLayout(gfx);
            }

            gfx.ClearScene();

            long now = e.FrameTime;
            float dt = e.DeltaTime / 1000.0f;

            float pixelPerSecond = DanmakuSpeed * 60.0f;
            float moveDistance = pixelPerSecond * dt;

            if (_trackRightEdge != null)
                Array.Clear(_trackRightEdge, 0, _trackRightEdge.Length);

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];
                item.X -= moveDistance;

                if (item.X + item.Width < 0)
                {
                    item.Dispose();
                    _items.RemoveAt(i);
                }
                else
                {
                    // 只有当弹幕所在的轨道索引依然有效时才更新（防止改字号后轨道变少导致越界）
                    if (_trackRightEdge != null && item.Track < _trackRightEdge.Length)
                    {
                        float effectiveRight = item.X + item.Width + TrackGap;
                        if (effectiveRight > _trackRightEdge[item.Track])
                        {
                            _trackRightEdge[item.Track] = effectiveRight;
                        }
                    }

                    var currentFont = _mainFont;
                    var currentShadow = _shadowBrush;
                    var currentBrush = item.Brush;

                    if (currentFont != null && currentShadow != null && currentBrush != null)
                    {
                        gfx.DrawText(currentFont, currentShadow, item.X + 2, item.Y + 2, item.Text);
                        gfx.DrawText(currentFont, currentBrush, item.X, item.Y, item.Text);
                    }
                }
            }

            TrySpawnDanmaku(gfx);
        }

        private void TrySpawnDanmaku(D2D.Graphics gfx)
        {
            lock (_queueLock) { if (_danmakuQueue.Count == 0) return; }

            if (_trackRightEdge == null || _mainFont == null) return;
            var font = _mainFont;

            int loops = 0;
            while (loops < 8)
            {
                string text;
                lock (_queueLock)
                {
                    if (_danmakuQueue.Count == 0) break;
                    text = _danmakuQueue.Peek();
                }

                var size = gfx.MeasureString(font, text);
                float textWidth = size.X + 40;

                int track = GetRandomTrackAvoidCenter();

                if (track == -1) break;

                lock (_queueLock) { _danmakuQueue.Dequeue(); }

                int r = RandomColor ? _rand.Next(200, 256) : DanmakuFixedColor.R;
                int g = RandomColor ? _rand.Next(200, 256) : DanmakuFixedColor.G;
                int b = RandomColor ? _rand.Next(200, 256) : DanmakuFixedColor.B;

                var brush = gfx.CreateSolidBrush(r, g, b, DanmakuOpacity);

                var item = new DanmakuItem
                {
                    Text = text,
                    X = _window.Width,
                    Y = TopMargin + track * _trackHeight,
                    Width = textWidth,
                    Track = track,
                    Brush = brush
                };

                _items.Add(item);
                // 安全检查
                if (track < _trackRightEdge.Length)
                {
                    _trackRightEdge[track] = _window.Width + textWidth + TrackGap;
                }
                loops++;
            }
        }

        private int GetRandomTrackAvoidCenter()
        {
            if (_trackCount <= 0 || _trackRightEdge == null) return -1;

            int screenHeight = _window.Height;
            int avoidTopY = (int)(screenHeight * AvoidCenterTopPercent);
            int avoidBottomY = (int)(screenHeight * AvoidCenterBottomPercent);

            List<int> validTracks = new List<int>();

            for (int i = 0; i < _trackCount; i++)
            {
                int trackY = TopMargin + i * _trackHeight;
                if (trackY > avoidTopY && trackY < avoidBottomY) continue;
                validTracks.Add(i);
            }

            if (validTracks.Count == 0) return -1;

            int n = validTracks.Count;
            for (int i = n - 1; i > 0; i--)
            {
                int j = _rand.Next(i + 1);
                (validTracks[i], validTracks[j]) = (validTracks[j], validTracks[i]);
            }

            foreach (int trackIndex in validTracks)
            {
                if (_trackRightEdge[trackIndex] <= _window.Width)
                {
                    return trackIndex;
                }
            }

            return -1;
        }

        // ================= WinForms 托盘 =================
        private void InitTray()
        {
            _tray.Icon = SystemIcons.Application;
            _tray.Text = "弹幕客户端 (GPU加速)";
            _tray.Visible = true;
            _tray.DoubleClick += (_, _) => SettingsForm.ShowSettings(this);
            var m = new ContextMenuStrip();
            m.Items.Add("设置", null, (_, _) => SettingsForm.ShowSettings(this));
            m.Items.Add("退出", null, (_, _) => {
                SaveSettings();
                _window.Dispose();
                _tray.Visible = false;
                Application.Exit();
            });
            _tray.ContextMenuStrip = m;
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        // ================= WebSocket =================
        public async Task ConnectWebSocket()
        {
            if (string.IsNullOrWhiteSpace(WebSocketUrl)) return;
            try
            {
                _ws?.Abort();
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(new Uri(WebSocketUrl), System.Threading.CancellationToken.None);
                _ = Task.Run(ReceiveLoop);
                EnqueueDanmaku("✅ GPU引擎启动");
            }
            catch { EnqueueDanmaku("⚠ 连接失败"); }
        }

        private async Task ReceiveLoop()
        {
            var currentWs = _ws;
            if (currentWs == null) return;
            var buffer = new byte[4096];
            while (currentWs.State == WebSocketState.Open)
            {
                try
                {
                    var r = await currentWs.ReceiveAsync(new ArraySegment<byte>(buffer), System.Threading.CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    string json = Encoding.UTF8.GetString(buffer, 0, r.Count);
                    HandleMessage(json);
                }
                catch { break; }
            }
        }
        private void HandleMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var t) && t.GetString() == "message")
                    if (root.TryGetProperty("data", out var d) && d.TryGetProperty("text", out var txt))
                        EnqueueDanmaku(txt.GetString() ?? "");
            }
            catch { }
        }
        private void EnqueueDanmaku(string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) lock (_queueLock) { _danmakuQueue.Enqueue(text); }
        }

        // ================= 设置 =================
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    string json = File.ReadAllText(_settingsFile);
                    var doc = JsonDocument.Parse(json).RootElement;
                    if (doc.TryGetProperty("DanmakuSpeed", out var p)) DanmakuSpeed = p.GetSingle();
                    if (doc.TryGetProperty("DanmakuFontSize", out var p2)) DanmakuFontSize = p2.GetInt32();
                    if (doc.TryGetProperty("WebSocketUrl", out var p3)) WebSocketUrl = p3.GetString();
                }
            }
            catch { }
        }
        public void SaveSettings()
        {
            try
            {
                var obj = new { DanmakuSpeed, DanmakuFontSize, WebSocketUrl };
                string json = JsonSerializer.Serialize(obj);
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
                File.WriteAllText(_settingsFile, json);
            }
            catch { }
        }
    }
}