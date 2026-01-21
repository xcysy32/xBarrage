using System;
using System.Drawing;
using System.Windows.Forms;

namespace DanmakuClient
{
    public partial class SettingsForm : Form
    {
        private TrackBar speedBar;
        private TrackBar fontBar;
        private CheckBox colorCheck;
        private Label speedLabelValue;
        private Label fontLabelValue;
        private Panel previewPanel;
        private Button colorPickerButton;
        private TrackBar opacityBar;
        private Label opacityLabelValue;
        private TextBox wsUrlBox;
        private Button wsConnectButton;

        private Form1? mainForm;

        public SettingsForm(Form1 main)
        {
            mainForm = main;

            InitializeComponent();

            Text = "弹幕设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(450, 450);

            speedBar = new TrackBar();
            fontBar = new TrackBar();
            colorCheck = new CheckBox();
            speedLabelValue = new Label();
            fontLabelValue = new Label();
            previewPanel = new Panel();
            colorPickerButton = new Button();
            opacityBar = new TrackBar();
            opacityLabelValue = new Label();
            wsUrlBox = new TextBox();
            wsConnectButton = new Button();

            InitUI();
            InitPreview();
        }

        private void InitUI()
        {
            // 弹幕速度
            Label speedLabel = new() { Text = "弹幕速度", Left = 20, Top = 20 };
            speedBar.Left = 20; speedBar.Top = 45; speedBar.Width = 240;
            speedBar.Minimum = 2; speedBar.Maximum = 15; speedBar.Value = (int)Form1.DanmakuSpeed;
            speedLabelValue.Left = 270; speedLabelValue.Top = 45; speedLabelValue.AutoSize = true;
            speedLabelValue.Text = Form1.DanmakuSpeed.ToString();
            speedBar.Scroll += (_, _) =>
            {
                Form1.DanmakuSpeed = speedBar.Value;
                speedLabelValue.Text = speedBar.Value.ToString();
                ShowPreview();
                mainForm?.SaveSettings(); // 实时保存
            };

            // 字体大小
            Label fontLabel = new() { Text = "字体大小", Left = 20, Top = 85 };
            fontBar.Left = 20; fontBar.Top = 110; fontBar.Width = 240;
            fontBar.Minimum = 14; fontBar.Maximum = 36; fontBar.Value = Form1.DanmakuFontSize;
            fontLabelValue.Left = 270; fontLabelValue.Top = 110; fontLabelValue.AutoSize = true;
            fontLabelValue.Text = Form1.DanmakuFontSize.ToString();
            fontBar.Scroll += (_, _) =>
            {
                Form1.DanmakuFontSize = fontBar.Value;
                fontLabelValue.Text = fontBar.Value.ToString();
                ShowPreview();
                mainForm?.SaveSettings();
            };

            // 随机颜色
            colorCheck.Left = 20; colorCheck.Top = 160;
            colorCheck.Text = "随机颜色弹幕"; colorCheck.Checked = Form1.RandomColor;
            colorCheck.CheckedChanged += (_, _) =>
            {
                Form1.RandomColor = colorCheck.Checked;
                colorPickerButton.Enabled = !colorCheck.Checked;
                ShowPreview();
                mainForm?.SaveSettings();
            };

            // 固定颜色选择
            colorPickerButton.Left = 150; colorPickerButton.Top = 155;
            colorPickerButton.Text = "选择颜色";
            colorPickerButton.Enabled = !Form1.RandomColor;
            colorPickerButton.Click += (_, _) =>
            {
                using var dlg = new ColorDialog();
                dlg.Color = Form1.DanmakuFixedColor;
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    Form1.DanmakuFixedColor = dlg.Color;
                    ShowPreview();
                    mainForm?.SaveSettings();
                }
            };

            // 透明度
            Label opacityLabel = new() { Text = "弹幕透明度", Left = 20, Top = 200 };
            opacityBar.Left = 20; opacityBar.Top = 225; opacityBar.Width = 240;
            opacityBar.Minimum = 50; opacityBar.Maximum = 255; opacityBar.Value = Form1.DanmakuOpacity;
            opacityLabelValue.Left = 270; opacityLabelValue.Top = 225; opacityLabelValue.AutoSize = true;
            opacityLabelValue.Text = Form1.DanmakuOpacity.ToString();
            opacityBar.Scroll += (_, _) =>
            {
                Form1.DanmakuOpacity = opacityBar.Value;
                opacityLabelValue.Text = opacityBar.Value.ToString();
                ShowPreview();
                mainForm?.SaveSettings();
            };

            // WebSocket 弹幕源
            Label wsLabel = new() { Text = "WebSocket 弹幕源", Left = 20, Top = 270 };
            wsUrlBox.Left = 20; wsUrlBox.Top = 295; wsUrlBox.Width = 300;
            wsUrlBox.Text = Form1.WebSocketUrl ?? "";
            wsConnectButton.Left = 330; wsConnectButton.Top = 293; wsConnectButton.Text = "连接";
            wsConnectButton.Click += async (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(wsUrlBox.Text))
                {
                    MessageBox.Show("请输入有效的 WebSocket URL！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Form1.WebSocketUrl = wsUrlBox.Text;
                mainForm?.SaveSettings(); // 保存 WS URL

                if (mainForm != null)
                    await mainForm.ConnectWebSocket();

                MessageBox.Show($"已连接 WS 弹幕源：{wsUrlBox.Text}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            Controls.AddRange(new Control[]
            {
                speedLabel, speedBar, speedLabelValue,
                fontLabel, fontBar, fontLabelValue,
                colorCheck, colorPickerButton,
                opacityLabel, opacityBar, opacityLabelValue,
                wsLabel, wsUrlBox, wsConnectButton
            });
        }

        private void InitPreview()
        {
            previewPanel.Left = 20; previewPanel.Top = 330;
            previewPanel.Width = 400; previewPanel.Height = 80;
            previewPanel.BorderStyle = BorderStyle.FixedSingle;
            previewPanel.BackColor = Color.Black;
            Controls.Add(previewPanel);
            ShowPreview();
        }

        public void ShowPreview()
        {
            previewPanel.Controls.Clear();
            string[] demoTexts = { "Hello World!", "弹幕测试", "😊🔥", "Danmaku" };
            Random rand = new();

            int y = 10;
            foreach (var text in demoTexts)
            {
                var label = new Label
                {
                    Text = text,
                    AutoSize = true,
                    Font = new Font("Microsoft YaHei", Form1.DanmakuFontSize, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    Location = new Point(previewPanel.Width, y),
                    ForeColor = Form1.RandomColor
                        ? Color.FromArgb(rand.Next(150, 255), rand.Next(150, 255), rand.Next(150, 255))
                        : Color.FromArgb(Form1.DanmakuOpacity, Form1.DanmakuFixedColor)
                };

                // 阴影效果
                label.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.DrawString(label.Text, label.Font, Brushes.Black, 2, 2);
                    using var brush = new SolidBrush(label.ForeColor);
                    e.Graphics.DrawString(label.Text, label.Font, brush, 0, 0);
                };

                previewPanel.Controls.Add(label);

                var timer = new System.Windows.Forms.Timer { Interval = 16 };
                timer.Tick += (_, _) =>
                {
                    label.Left -= (int)Form1.DanmakuSpeed;
                    if (label.Right < 0) label.Left = previewPanel.Width;
                };
                timer.Start();

                y += 20;
            }
        }

        // 单例打开设置窗口
        private static SettingsForm? instance;
        public static void ShowSettings(Form1 mainForm)
        {
            if (instance == null || instance.IsDisposed)
            {
                instance = new SettingsForm(mainForm);
                instance.Show();
            }
            else
            {
                instance.BringToFront();
            }
        }
    }
}
