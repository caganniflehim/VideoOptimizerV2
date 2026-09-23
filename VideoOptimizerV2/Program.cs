using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Text;
using System.ComponentModel;

namespace VideoOptimizerV2
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }

    public class FlickerFreeListBox : ListBox
    {
        public FlickerFreeListBox()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0014) // WM_ERASEBKGND
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }
    }

    public class ModernTabControl : TabControl
    {
        public ModernTabControl()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.FromArgb(37, 37, 40));

            for (int i = 0; i < this.TabPages.Count; i++)
            {
                TabPage page = this.TabPages[i];
                Rectangle rect = this.GetTabRect(i);
                bool isSelected = (i == this.SelectedIndex);

                Color tabBg = isSelected ? Color.FromArgb(60, 60, 65) : Color.FromArgb(45, 45, 48);
                Color textColor = isSelected ? Color.White : Color.FromArgb(170, 170, 170);

                using (SolidBrush brush = new SolidBrush(tabBg))
                {
                    g.FillRectangle(brush, rect);
                }

                if (isSelected)
                {
                    using (SolidBrush accentBrush = new SolidBrush(Color.FromArgb(0, 120, 212)))
                    {
                        g.FillRectangle(accentBrush, new Rectangle(rect.X, rect.Bottom - 3, rect.Width, 3));
                    }
                }

                TextRenderer.DrawText(g, page.Text, new Font("Segoe UI", 9.5f, isSelected ? FontStyle.Bold : FontStyle.Regular), rect, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    public class MainForm : Form
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenThread(int dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport("kernel32.dll")]
        private static extern uint SuspendThread(IntPtr hThread);

        [DllImport("kernel32.dll")]
        private static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int THREAD_SUSPEND_RESUME = 0x0002;

        private ModernTabControl mainTabControl;
        private TabPage tabDashboard;
        private TabPage tabDetailsSettings;
        private TabPage tabPreview; // Buraya ekle
        private Panel pnlLeftQueue;

        private Button btnSelectFolder;
        private Button btnSelectFiles;
        private Button btnLoadList;
        private Button btnSelectTargetFolder;
        private Button btnWatchFolder;
        private Button btnToggleTheme;
        private Button btnStart;
        private Button btnPauseResume;
        private Button btnStop;
        private Button btnClearQueue;
        private Button btnCleanOriginals;

        private Button btnDetailPause;
        private Button btnDetailStop;

        private CheckBox chkAutoMode;
        private CheckBox chkSplitAudio;
        private ComboBox cmbPresets;

        private FlickerFreeListBox lstQueueBox;
        private ContextMenuStrip contextMenuQueue;
        private ToolStripMenuItem menuItemDeleteOriginal;

        private Panel pnlDetails;
        private Label lblDetailTitle;
        private Label lblDetailStatus;
        private Label lblDetailSource;
        private Label lblDetailTarget;
        private Label lblDetailPreset;
        private Label lblDetailSizeInfo;
        private Label lblDashboardSizeInfo;
        private Label lblQueueCounter; // Sabit video sayacı etiketi

        private Button btnCopySource;
        private Button btnCopyTarget;

        private Label lblStatus;
        private Label lblDropZone;
        private System.Windows.Forms.Timer autoTimer;
        private System.Windows.Forms.Timer folderWatchTimer;

        private CancellationTokenSource cts;
        private Process currentProcess = null;
        private bool isRunning = false;
        private bool isPaused = false;
        private string nasFolderPath = string.Empty;
        private string syncFolderPath;
        private string customTargetFolder = string.Empty;
        private string targetWatchFolderPath = string.Empty;
        private string tempDummyFile = string.Empty;

        private BindingList<QueueItemData> queueList = new BindingList<QueueItemData>();

        private bool isDarkMode = true;
        private Color darkBg = Color.FromArgb(30, 30, 30);
        private Color darkPanelBg = Color.FromArgb(45, 45, 45);
        private Color darkText = Color.White;

        private Color lightBg = Color.FromArgb(240, 240, 240);
        private Color lightPanelBg = Color.FromArgb(245, 245, 247);
        private Color lightText = Color.Black;

        private int clickCount = 0;
        private DateTime lastClickTime;

        private GroupBox grpTrimBox;
        private TextBox txtTrimStart;
        private TextBox txtTrimEnd;
        private Button btnApplyTrim;

        public MainForm(string[] args = null)
        {
            syncFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Merzigo_Sync");
            if (!Directory.Exists(syncFolderPath))
            {
                Directory.CreateDirectory(syncFolderPath);
            }

            string oldPreview = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview_temp.mp4");
            if (File.Exists(oldPreview))
            {
                try { File.Delete(oldPreview); } catch { }
            }

            InitializeComponentCustom();
            InitAutoTimer();
            InitFolderWatchTimer();
            ApplyTheme();

            if (args != null && args.Length > 0)
            {
                string incomingFile = args[0].Replace("videooptimizer://", "").Trim();
                if (File.Exists(incomingFile))
                {
                    AddVideoToQueue(incomingFile);
                    UpdateStatus($"Durum: Dış sistemden 1 adet video kuyruğa eklendi.");
                }
            }
        }

        private void InitializeComponentCustom()
        {
            this.Text = "VideoOptimizer - GPU Render Kontrol Paneli";
            this.Size = new Size(1160, 740);
            this.MinimumSize = new Size(1160, 740);
            this.StartPosition = FormStartPosition.CenterScreen;

            this.AllowDrop = true;
            this.DragEnter += MainForm_DragEnter;
            this.DragDrop += MainForm_DragDrop;

            pnlLeftQueue = new Panel()
            {
                Location = new Point(12, 12),
                Size = new Size(330, 630),
                BackColor = Color.FromArgb(45, 45, 48),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
            };
            this.Controls.Add(pnlLeftQueue);

            /*contextMenuQueue = new ContextMenuStrip();
            menuItemDeleteOriginal = new ToolStripMenuItem("🗑 Bu Videonun Orijinal Ham Dosyasını Sil");
            menuItemDeleteOriginal.Click += MenuItemDeleteOriginal_Click;
            contextMenuQueue.Items.Add(menuItemDeleteOriginal);*/

            lstQueueBox = new FlickerFreeListBox()
            {
                Location = new Point(10, 10),
                Size = new Size(310, 610),
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 56,
                IntegralHeight = false,
                DataSource = queueList,
                ContextMenuStrip = contextMenuQueue,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                SelectionMode = SelectionMode.MultiExtended,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            lstQueueBox.DrawItem += LstQueueBox_DrawItem;
            lstQueueBox.SelectedIndexChanged += LstQueueBox_SelectedIndexChanged;
            pnlLeftQueue.Controls.Add(lstQueueBox);

            mainTabControl = new ModernTabControl()
            {
                Location = new Point(354, 12),
                Size = new Size(778, 630),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(150, 35)
            };

            mainTabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
            mainTabControl.DrawItem += mainTabControl_DrawItem;

            tabDashboard = new TabPage("ANA SAYFA");
            tabDashboard.BackColor = Color.FromArgb(37, 37, 40);

            tabDetailsSettings = new TabPage("ÖZET SAYFASI");
            tabDetailsSettings.BackColor = Color.FromArgb(37, 37, 40);

            tabPreview = new TabPage("ÖN İZLEME");
            tabPreview.BackColor = Color.FromArgb(37, 37, 40);

            mainTabControl.TabPages.Add(tabDashboard);
            mainTabControl.TabPages.Add(tabDetailsSettings);
            mainTabControl.TabPages.Add(tabPreview);
            this.Controls.Add(mainTabControl);

            Label lblEncoderMode = new Label()
            {
                Text = "Kodlayıcı Tercihi:",
                Location = new Point(15, 235),
                Size = new Size(110, 20),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = isDarkMode ? Color.White : Color.Black
            };
            tabDashboard.Controls.Add(lblEncoderMode);

            ComboBox cmbEncoderMode = new ComboBox()
            {
                Name = "cmbEncoderMode",
                Location = new Point(130, 230),
                Size = new Size(115, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbEncoderMode.Items.AddRange(new string[] {
                "Otomatik",
                "Sadece GPU",
                "Sadece CPU"
             });

            cmbEncoderMode.SelectedIndex = 0;
            tabDashboard.Controls.Add(cmbEncoderMode);

            lblDropZone = new Label()
            {
                Text = "📁 DOSYALARI SÜRÜKLE & BIRAK\nveya aşağıdaki şeffaf butonlar ile seçin",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Location = new Point(15, 15),
                Size = new Size(150, 120),
                BackColor = Color.FromArgb(50, 50, 55),
                ForeColor = Color.FromArgb(200, 200, 200),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tabDashboard.Controls.Add(lblDropZone);

            btnSelectFolder = CreateTransparentButton("📁 Klasör Seç", 15, 145, 140, 34, Color.White);
            btnSelectFolder.Click += BtnSelectFolder_Click;
            tabDashboard.Controls.Add(btnSelectFolder);

            btnSelectFiles = CreateTransparentButton("🎬 Video Seç", 165, 145, 140, 34, Color.White);
            btnSelectFiles.Click += BtnSelectFiles_Click;
            tabDashboard.Controls.Add(btnSelectFiles);

            btnSelectTargetFolder = CreateTransparentButton("🎯 Hedef Klasör", 315, 145, 140, 34, Color.White);
            btnSelectTargetFolder.Click += BtnSelectTargetFolder_Click;
            tabDashboard.Controls.Add(btnSelectTargetFolder);

            btnWatchFolder = CreateTransparentButton("🔍 Klasör İzle", 465, 145, 140, 34, Color.White);
            btnWatchFolder.Click += BtnWatchFolder_Click;
            tabDashboard.Controls.Add(btnWatchFolder);

            btnLoadList = CreateTransparentButton("📄 Liste Yükle", 615, 145, 140, 34, Color.White);
            btnLoadList.Click += BtnLoadList_Click;
            tabDashboard.Controls.Add(btnLoadList);

            cmbPresets = new ComboBox() { Location = new Point(15, 190), Size = new Size(230, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbPresets.Items.AddRange(new string[] {
                "Otomatik Akıllı Mod (Auto-CRF / Bitrate Analizi)",
                "Max Kalite Orjinal (Ultra - RF/CQ 14)",
                "Yüksek Kalite Orjinal (HQ - RF/CQ 18)",
                "Dengeli Orijinal (Dengeli - RF/CQ 20)",
                "Full HD Sabitle (1920x1080)",
                "HD Sabitle (1280x720)",
                "Fast 60 FPS",
                "Fast 30 FPS"
                });
            cmbPresets.SelectedIndex = 0;
            cmbPresets.SelectedIndexChanged += (s, e) => {
                if (lstQueueBox.SelectedItem is QueueItemData data) DisplayItemDetails(data);
            };
            tabDashboard.Controls.Add(cmbPresets);

            grpTrimBox = new GroupBox()
            {
                Text = "Video Kesme Ayarları",
                Location = new Point(300, 180),
                Size = new Size(460, 57),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            tabDashboard.Controls.Add(grpTrimBox);

            GroupBox grpAudioChannelBox = new GroupBox()
            {
                Text = "Seçili Video Ses Kanalı Ayarı",
                Location = new Point(300, 240), // Kesme kutusunun hemen altı (195 + 65 piksel aşağıda)
                Size = new Size(460, 57),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            tabDashboard.Controls.Add(grpAudioChannelBox);

            TextBox txtAudioChannels = new TextBox()
            {
                Name = "txtAudioChannels",
                PlaceholderText = "Örn: 1,3 veya 2-4 (Boşsa Tümü)",
                Location = new Point(15, 22),
                Size = new Size(270, 23)
            };
            grpAudioChannelBox.Controls.Add(txtAudioChannels);

            Button btnExtractAudioOnly = CreateModernActionButton("🎵 Sesleri Çıkar", 300, 20, 150, 27, Color.FromArgb(0, 120, 212), Color.White);

            btnExtractAudioOnly.Click += (s, e) => {
                if (lstQueueBox.SelectedItems.Count == 0)
                {
                    MessageBox.Show("Lütfen önce listeden en az bir video seçin!", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string val = txtAudioChannels.Text.Trim();
                foreach (QueueItemData item in lstQueueBox.SelectedItems)
                {
                    item.TargetAudioChannels = val;
                    item.ExtractAudioDuringConvert = true; // Artık bu video için convert sırasında ses de alınacak!
                }

                MessageBox.Show("Seçilen video(lar) için ses kanal ayarı ve convert sırasında ses çıkarma aktifleşti!", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // 2. Doğrudan ses çıkarma metodunu tetikleyelim:
                // Eğer kutu boşsa ParseChannelSelection otomatik tümünü alır, doluysa özel kanalları ayıklar.
                BtnExtractAudioOnly_Click(s, e);
            };


            grpAudioChannelBox.Controls.Add(btnExtractAudioOnly);



            txtTrimStart = new TextBox() { PlaceholderText = "Başlangıç (sn)", Location = new Point(15, 22), Size = new Size(130, 23) };
            grpTrimBox.Controls.Add(txtTrimStart);

            txtTrimEnd = new TextBox() { PlaceholderText = "Bitiş (sn)", Location = new Point(155, 22), Size = new Size(130, 23) };
            grpTrimBox.Controls.Add(txtTrimEnd);

            btnApplyTrim = CreateModernActionButton("Uygula", 300, 20, 150, 27, Color.FromArgb(0, 120, 212), Color.White);
            btnApplyTrim.Click += BtnApplyTrim_Click;
            grpTrimBox.Controls.Add(btnApplyTrim);

            chkSplitAudio = new CheckBox() { Text = "Sesleri .WAV Olarak Ayır", Location = new Point(15, 270), Size = new Size(170, 22), Checked = false, Font = new Font("Segoe UI", 8.5f) };                                         //ömer 
            chkSplitAudio.CheckedChanged += (s, e) => {
                chkSplitAudio.ForeColor = isDarkMode ? darkText : lightText;

                // Kutuya tıklandığında, o an listede seçili olan TÜM videolara bu tercihi kaydedelim!
                foreach (var selectedObj in lstQueueBox.SelectedItems)
                {
                    if (selectedObj is QueueItemData itemData)
                    {
                        itemData.ExtractAudioDuringConvert = chkSplitAudio.Checked;
                    }
                }

                if (lstQueueBox.SelectedItem is QueueItemData data) DisplayItemDetails(data);
            };
            tabDashboard.Controls.Add(chkSplitAudio);

            chkAutoMode = new CheckBox() { Text = "Merzigo Oto", Location = new Point(200, 270), Size = new Size(110, 22), Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };                                                                    //iyi köpek
            chkAutoMode.CheckedChanged += ChkAutoMode_CheckedChanged;
            tabDashboard.Controls.Add(chkAutoMode);

            btnStart = CreateModernActionButton("🚀 BAŞLAT", 15, 310, 150, 38, Color.FromArgb(16, 124, 65), Color.White);
            btnStart.Click += BtnStart_Click;
            tabDashboard.Controls.Add(btnStart);

            btnPauseResume = CreateModernActionButton("Duraklat", 175, 310, 130, 38, Color.FromArgb(0, 120, 212), Color.White);
            btnPauseResume.Enabled = false;
            btnPauseResume.Click += BtnPauseResume_Click;
            tabDashboard.Controls.Add(btnPauseResume);

            btnStop = CreateModernActionButton("İptal Et", 315, 310, 120, 38, Color.FromArgb(232, 17, 35), Color.White);
            btnStop.Enabled = false;
            btnStop.Click += BtnStop_Click;
            tabDashboard.Controls.Add(btnStop);



            btnClearQueue = CreateModernActionButton("🧹 Temizle", 445, 310, 120, 38, Color.FromArgb(55, 55, 60), Color.FromArgb(240, 240, 240));
            btnClearQueue.Click += BtnClearQueue_Click;
            tabDashboard.Controls.Add(btnClearQueue);

            btnToggleTheme = CreateModernActionButton("🌓 Tema", 575, 310, 110, 38, Color.FromArgb(55, 55, 60), Color.FromArgb(240, 240, 240));
            btnToggleTheme.Click += (s, e) => {
                isDarkMode = !isDarkMode;
                ApplyTheme();
            };
            tabDashboard.Controls.Add(btnToggleTheme);

            /*btnCleanOriginals = CreateModernActionButton("🗑 Hamları Sil", 585, 310, 155, 38, Color.FromArgb(85, 50, 50), Color.White);
            btnCleanOriginals.Click += BtnCleanOriginals_Click;
            tabDashboard.Controls.Add(btnCleanOriginals);*/

            lblDashboardSizeInfo = new Label()
            {
                Text = "Dönüştürme Bilgisi: Listeden bir video seçin...",
                Location = new Point(15, 355),
                Size = new Size(740, 260),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.LightSkyBlue,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            tabDashboard.Controls.Add(lblDashboardSizeInfo);

            Panel pnlDetailsToolbar = new Panel()
            {
                Location = new Point(15, 12),
                Size = new Size(740, 40),
                BackColor = Color.FromArgb(45, 45, 48),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tabDetailsSettings.Controls.Add(pnlDetailsToolbar);

            btnDetailPause = CreateModernActionButton("⏸ Duraklat", 10, 5, 130, 30, Color.FromArgb(0, 120, 212), Color.White);
            btnDetailPause.Enabled = false;
            btnDetailPause.Click += BtnPauseResume_Click;

            btnDetailStop = CreateModernActionButton("⏹ İptal Et", 148, 5, 130, 30, Color.FromArgb(232, 17, 35), Color.White);
            btnDetailStop.Enabled = false;
            btnDetailStop.Click += BtnStop_Click;

            pnlDetailsToolbar.Controls.Add(btnDetailPause);
            pnlDetailsToolbar.Controls.Add(btnDetailStop);

            pnlDetails = new Panel()
            {
                Location = new Point(15, 60),
                Size = new Size(740, 520),
                BackColor = Color.FromArgb(245, 245, 247),
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            lblDetailTitle = new Label() { Text = "Özet & Detay Bilgileri", Location = new Point(15, 12), Size = new Size(700, 25), Font = new Font("Segoe UI", 11, FontStyle.Bold) };
            pnlDetails.Controls.Add(lblDetailTitle);

            lblDetailStatus = new Label() { Text = "Status: Bekliyor", Location = new Point(15, 42), Size = new Size(700, 20), Font = new Font("Segoe UI", 9) };
            pnlDetails.Controls.Add(lblDetailStatus);

            lblDetailSource = new Label() { Text = "Kaynak: -", Location = new Point(15, 72), Size = new Size(610, 45), Font = new Font("Segoe UI", 9) };
            pnlDetails.Controls.Add(lblDetailSource);

            Button btnCopySource = CreateModernActionButton("📁 Klasörde Aç", 635, 74, 90, 28, Color.FromArgb(60, 60, 65), Color.White);
            btnCopySource.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            btnCopySource.Click += (s, e) => {
                string fullPath = lblDetailSource.Text.Replace("Kaynak:\n", "").Trim();
                if (!string.IsNullOrEmpty(fullPath) && fullPath != "-")
                {
                    try
                    {
                        if (File.Exists(fullPath))
                        {
                            Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                            UpdateStatus("Durum: Kaynak videonun klasörü açıldı.");
                        }
                        else if (Directory.Exists(fullPath))
                        {
                            Process.Start("explorer.exe", $"\"{fullPath}\"");
                        }
                        else
                        {
                            Clipboard.SetText(fullPath);
                            UpdateStatus("Durum: Dosya bulunamadı, yol panoya kopyalandı.");
                        }
                    }
                    catch { }
                }
            };
            pnlDetails.Controls.Add(btnCopySource);

            lblDetailTarget = new Label() { Text = "Hedef: -", Location = new Point(15, 127), Size = new Size(610, 45), Font = new Font("Segoe UI", 9) };
            pnlDetails.Controls.Add(lblDetailTarget);

            Button btnCopyTarget = CreateModernActionButton("📁 Klasörde Aç", 635, 129, 90, 28, Color.FromArgb(60, 60, 65), Color.White);
            btnCopyTarget.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            btnCopyTarget.Click += (s, e) => {
                string fullPath = lblDetailTarget.Text.Replace("Hedef:\n", "").Trim();
                if (!string.IsNullOrEmpty(fullPath) && fullPath != "-")
                {
                    try
                    {
                        if (File.Exists(fullPath))
                        {
                            Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                            UpdateStatus("Durum: Hedef videonun klasörü açıldı.");
                        }
                        else
                        {
                            string dirPath = Path.GetDirectoryName(fullPath);
                            if (Directory.Exists(dirPath))
                            {
                                Process.Start("explorer.exe", $"\"{dirPath}\"");
                                UpdateStatus("Durum: Hedef klasör açıldı.");
                            }
                            else
                            {
                                Clipboard.SetText(fullPath);
                                UpdateStatus("Durum: Yol panoya kopyalandı.");
                            }
                        }
                    }
                    catch { }
                }
            };
            pnlDetails.Controls.Add(btnCopyTarget);
            lblDetailPreset = new Label() { Text = "Ön Ayar: -", Location = new Point(15, 182), Size = new Size(700, 22), Font = new Font("Segoe UI", 9) };
            pnlDetails.Controls.Add(lblDetailPreset);

            lblDetailSizeInfo = new Label() { Text = "Boyut Analizi & Zaman: İşlem bekleniyor...", Location = new Point(15, 212), Size = new Size(700, 290), Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.DarkBlue };                                                            //duman
            pnlDetails.Controls.Add(lblDetailSizeInfo);

            tabDetailsSettings.Controls.Add(pnlDetails);

            Button btnGeneratePreview = CreateModernActionButton("🎬 10. Dakikadan 1 Dklık Ön İzleme Üret", 20, 20, 350, 40, Color.FromArgb(0, 120, 212), Color.White);
            btnGeneratePreview.Click += BtnGeneratePreview_Click;
            tabPreview.Controls.Add(btnGeneratePreview);

            ProgressBar previewProgressBar = new ProgressBar()
            {
                Name = "previewProgressBar",
                Location = new Point(20, 80),
                Size = new Size(350, 25),
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };
            tabPreview.Controls.Add(previewProgressBar);

            Label lblPreviewInfo = new Label()
            {
                Text = "• Listeden bir video seçin.\n• 10. dakikadan başlayarak 1 dakikalık test kesiti alınır.\n• İşlem bitince oynatıcı otomatik açılır.",
                Location = new Point(20, 120),
                Size = new Size(500, 80),
                ForeColor = isDarkMode ? Color.FromArgb(200, 200, 200) : Color.FromArgb(50, 50, 50),
                Font = new Font("Segoe UI", 9f)
            };
            tabPreview.Controls.Add(lblPreviewInfo);

            lblStatus = new Label()
            {
                Text = $"Durum: Hazır. Akıllı Hedef Eşleme Aktif. Entegrasyon Klasörü: {syncFolderPath}",
                Location = new Point(15, 650),
                Size = new Size(1115, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            lblStatus.MouseDown += lblStatus_MouseDown;
            this.Controls.Add(lblStatus);


            lblQueueCounter = new Label()
            {
                Text = "Kuyruk Durumu: 0 video yüklenmeyi bekliyor",
                Location = new Point(15, 673), // lblStatus'ün hemen 24 piksel üstü
                Size = new Size(1115, 22),
                Font = new Font("Segoe UI", 9, FontStyle.Bold), // lblStatus ile birebir aynı font ve kalınlık
                ForeColor = Color.FromArgb(200, 200, 200),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            this.Controls.Add(lblQueueCounter);
        }



        private async void BtnExtractAudioOnly_Click(object sender, EventArgs e)
        {
            if (lstQueueBox.SelectedItems.Count == 0)
            {
                MessageBox.Show("Lütfen önce listeden sesini çıkarmak istediğiniz en az bir video seçin!", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                MessageBox.Show("ffmpeg.exe bulunamadı!", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdateStatus("Durum: Seçilen videolar için ses çıkarma kuyruğu başlatıldı...");

            // Başarıyla işlenen videoları saymak için sayaç
            int successCount = 0;
            int totalSelected = lstQueueBox.SelectedItems.Count;

            await Task.Run(() =>
            {
                foreach (QueueItemData item in lstQueueBox.SelectedItems)
                {
                    if (!File.Exists(item.FilePath) || item.FilePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                        continue;

                    SafeInvoke(() => {
                        item.Status = "Sesler Ayrıştırılıyor";
                        item.Percent = 0;
                        int idx = queueList.IndexOf(item);
                        if (idx != -1) lstQueueBox.Invalidate(lstQueueBox.GetItemRectangle(idx));
                        if (lstQueueBox.SelectedItem == item) DisplayItemDetails(item);
                    });

                    string nameOnly = Path.GetFileNameWithoutExtension(item.FilePath);
                    string audioSubFolder = GetDynamicAudioPath(item.FilePath);

                    if (!Directory.Exists(audioSubFolder))
                        Directory.CreateDirectory(audioSubFolder);

                    int totalChannels = item.AudioStreamCount;
                    if (totalChannels <= 0)
                    {
                        totalChannels = GetAudioStreamCount(ffmpegPath, item.FilePath);
                        item.AudioStreamCount = totalChannels;
                    }

                    if (totalChannels <= 0)
                    {
                        SafeInvoke(() => {
                            item.Status = "Hata (Ses Yok)";
                            item.Percent = 0;
                            int idx = queueList.IndexOf(item);
                            if (idx != -1) lstQueueBox.Invalidate(lstQueueBox.GetItemRectangle(idx));
                            if (lstQueueBox.SelectedItem == item) DisplayItemDetails(item);
                        });
                        continue; // Ses yoksa bu videoyu atla ve sayaca ekleme
                    }

                    string channelInput = item.TargetAudioChannels;
                    List<int> targetChannels = ParseChannelSelection(channelInput, totalChannels);

                    if (targetChannels.Count == 0) continue;

                    var (duration, res, br) = GetVideoInfo(ffmpegPath, item.FilePath);
                    item.TotalSeconds = duration;

                    StringBuilder sbArgs = new StringBuilder();
                    sbArgs.Append($"-y -i \"{item.FilePath}\" ");

                    foreach (int ch in targetChannels)
                    {
                        int ffmpegIndex = ch - 1;
                        string channelAudioFile = Path.Combine(audioSubFolder, $"{nameOnly}_Kanal_{ch}.wav");
                        sbArgs.Append($"-map 0:a:{ffmpegIndex} -vn -acodec pcm_s16le -ar 48000 \"{channelAudioFile}\" ");
                    }

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = sbArgs.ToString(),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true
                    };

                    try
                    {
                        using (Process audioProc = new Process())
                        {
                            audioProc.StartInfo = psi;

                            audioProc.ErrorDataReceived += (s, errArgs) =>
                            {
                                if (!string.IsNullOrEmpty(errArgs.Data))
                                {
                                    string line = errArgs.Data;
                                    Match timeMatch = Regex.Match(line, @"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                                    if (timeMatch.Success && item.TotalSeconds > 0)
                                    {
                                        try
                                        {
                                            double h = double.Parse(timeMatch.Groups[1].Value);
                                            double m = double.Parse(timeMatch.Groups[2].Value);
                                            double sVal = double.Parse(timeMatch.Groups[3].Value);
                                            double cs = double.Parse(timeMatch.Groups[4].Value);

                                            double currentSeconds = h * 3600 + m * 60 + sVal + cs / 100.0;
                                            int percent = (int)((currentSeconds / item.TotalSeconds) * 100);
                                            if (percent > 100) percent = 100;
                                            if (percent < 0) percent = 0;

                                            item.Percent = percent;

                                            SafeInvoke(() => {
                                                int idx = queueList.IndexOf(item);
                                                if (idx != -1) lstQueueBox.Invalidate(lstQueueBox.GetItemRectangle(idx));
                                                if (lstQueueBox.SelectedItem == item) DisplayItemDetails(item);
                                                UpdateStatus($"Durum: {item.FileName} sesleri çıkarılıyor... (%{percent})");
                                            });
                                        }
                                        catch { }
                                    }
                                }
                            };

                            audioProc.Start();
                            audioProc.BeginErrorReadLine();
                            audioProc.WaitForExit();
                        }

                        // Başarılı bir şekilde tamamlandıysa sayacı artır
                        item.Status = "Tamam";
                        item.Percent = 100;
                        successCount++;
                    }
                    catch { }

                    SafeInvoke(() => {
                        int idx = queueList.IndexOf(item);
                        if (idx != -1) lstQueueBox.Invalidate(lstQueueBox.GetItemRectangle(idx));
                        if (lstQueueBox.SelectedItem == item) DisplayItemDetails(item);
                    });
                }
            });

            UpdateStatus($"Durum: İşlem tamamlandı. {successCount}/{totalSelected} videonun sesleri dışarı aktarıldı.");
            MessageBox.Show($"{successCount} adet videonun ses dosyaları .WAV formatında ilgili klasörlere başarıyla çıkarıldı!", "İşlem Tamam", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private List<int> ParseChannelSelection(string input, int maxChannels)
        {
            List<int> channels = new List<int>();

            // Eğer boş bırakıldıysa tüm kanalları alalım
            if (string.IsNullOrWhiteSpace(input))
            {
                for (int i = 1; i <= maxChannels; i++) channels.Add(i);
                return channels;
            }

            string[] parts = input.Split(',');
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Contains("-"))
                {
                    string[] rangeParts = trimmed.Split('-');
                    if (rangeParts.Length == 2 && int.TryParse(rangeParts[0], out int start) && int.TryParse(rangeParts[1], out int end))
                    {
                        for (int i = start; i <= end; i++)
                        {
                            if (i >= 1 && i <= maxChannels && !channels.Contains(i))
                                channels.Add(i);
                        }
                    }
                }
                else
                {
                    if (int.TryParse(trimmed, out int ch))
                    {
                        if (ch >= 1 && ch <= maxChannels && !channels.Contains(ch))
                            channels.Add(ch);
                    }
                }
            }

            if (channels.Count == 0)
            {
                for (int i = 1; i <= maxChannels; i++) channels.Add(i);
            }

            return channels;
        }

        private async void BtnGeneratePreview_Click(object sender, EventArgs e)
        {
            if (lstQueueBox.SelectedItem is not QueueItemData selectedItem || !File.Exists(selectedItem.FilePath))
            {
                MessageBox.Show("Lütfen önce listeden ön izlemek istediğiniz bir video seçin!", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (selectedItem.FilePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("DVD VOB listeleri için ön izleme desteklenmiyor.", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Button btn = sender as Button;
            if (btn != null) btn.Enabled = false;

            Control pBarControl = btn.Parent.Controls["previewProgressBar"];
            ProgressBar pBar = pBarControl as ProgressBar;
            if (pBar != null)
            {
                pBar.Visible = true;
                pBar.Style = ProgressBarStyle.Continuous;
                pBar.Value = 0;
            }

            UpdateStatus("Durum: Ön izleme kesiti hazırlanıyor (%0)...");

            await Task.Run(() =>
            {
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                string tempPreviewFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview_temp.mp4");

                if (File.Exists(tempPreviewFile))
                {
                    try { File.Delete(tempPreviewFile); } catch { }
                }

                string previewArgs = $"-y -ss 00:10:00 -i \"{selectedItem.FilePath}\" -t 00:01:00 -c:v libx264 -pix_fmt yuv420p -crf 23 -c:a aac -ac 2 \"{tempPreviewFile}\"";

                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = previewArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                string errorOutput = "";
                try
                {
                    using (System.Diagnostics.Process p = new System.Diagnostics.Process())
                    {
                        p.StartInfo = psi;

                        p.ErrorDataReceived += (s, args) =>
                        {
                            if (!string.IsNullOrEmpty(args.Data))
                            {
                                string line = args.Data;
                                errorOutput += line + "\n";

                                if (line.Contains("time="))
                                {
                                    try
                                    {
                                        int timeIndex = line.IndexOf("time=");
                                        string timeStr = line.Substring(timeIndex + 5, 8);
                                        TimeSpan elapsed = TimeSpan.Parse(timeStr);

                                        int percent = (int)((elapsed.TotalSeconds / 60.0) * 100);
                                        if (percent > 100) percent = 100;
                                        if (percent < 0) percent = 0;

                                        SafeInvoke(() =>
                                        {
                                            if (pBar != null) pBar.Value = percent;
                                            UpdateStatus($"Durum: Ön izleme üretiliyor... (%{percent})");
                                        });
                                    }
                                    catch { }
                                }
                            }
                        };

                        p.Start();
                        p.BeginErrorReadLine();
                        p.WaitForExit();
                    }
                }
                catch (Exception ex)
                {
                    errorOutput = ex.Message;
                }

                SafeInvoke(() =>
                {
                    if (btn != null)
                    {
                        btn.Enabled = true;
                        btn.Text = "🎬 Ön İzlemeyi Yeniden Aç / Oynat";
                    }

                    if (pBar != null) pBar.Visible = false;

                    if (File.Exists(tempPreviewFile) && new FileInfo(tempPreviewFile).Length > 0)
                    {
                        UpdateStatus("Durum: Ön izleme hazır ve açılıyor!");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tempPreviewFile) { UseShellExecute = true });
                    }
                    else
                    {
                        MessageBox.Show("Ön izleme oluşturulamadı! Video süresi 10 dakikadan kısa olabilir.\n\nDetay:\n" + errorOutput, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UpdateStatus("Durum: Ön izleme başarısız.");
                    }
                });
            });
        }

        private void BtnApplyTrim_Click(object sender, EventArgs e)
        {
            if (lstQueueBox.SelectedItems.Count == 0)
            {
                MessageBox.Show("Lütfen listeden en az bir video seçin!", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string startVal = txtTrimStart.Text.Trim();
            string endVal = txtTrimEnd.Text.Trim();

            foreach (QueueItemData item in lstQueueBox.SelectedItems)
            {
                item.TrimStart = startVal;
                item.TrimEnd = endVal;
            }

            if (lstQueueBox.SelectedItem is QueueItemData currentData)
            {
                DisplayItemDetails(currentData);
            }
            MessageBox.Show($"{lstQueueBox.SelectedItems.Count} adet video için kesme aralığı başarıyla uygulandı!\n\nBaşlangıç: {(string.IsNullOrEmpty(startVal) ? "Baştan" : startVal)}\nBitiş: {(string.IsNullOrEmpty(endVal) ? "Sona Kadar" : endVal)}",             //191874569
                "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);

            lstQueueBox.Invalidate();
        }



        /*   private double ParseTimestampToSeconds(string timeText)
           {
               if (string.IsNullOrWhiteSpace(timeText)) return 0;

               if (double.TryParse(timeText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double directSec))
               {
                   return directSec;
               }

               string[] parts = timeText.Split(':');
               double totalSeconds = 0;

               if (parts.Length == 2)
               {
                   if (double.TryParse(parts[0], out double min) && double.TryParse(parts[1], out double sec))
                   {
                       totalSeconds = (min * 60) + sec;
                   }
               }
               else if (parts.Length == 3)
               {
                   if (double.TryParse(parts[0], out double hour) && double.TryParse(parts[1], out double min) && double.TryParse(parts[2], out double sec))
                   {
                       totalSeconds = (hour * 3600) + (min * 60) + sec;
                   }
               }

               return totalSeconds;
           }*/

        private void lblStatus_MouseDown(object sender, MouseEventArgs e)
        {
            int[] hiddenRules = { 0, 50, 4 };

            if (e.Button != MouseButtons.Middle) return;

            if (e.X >= hiddenRules[0] && e.X <= hiddenRules[1])
            {
                if ((DateTime.Now - lastClickTime).TotalSeconds > 1)
                {
                    clickCount = 0;
                }

                clickCount++;
                lastClickTime = DateTime.Now;

                if (clickCount == hiddenRules[2])
                {
                    ShowDeveloperSignature();
                    clickCount = 0;
                }
            }
            else
            {
                clickCount = 0;
            }
        }

        private void ShowDeveloperSignature()
        {
            // Bayt dizisi olarak doğrudan tanımlıyoruz, şifreleme/Base64 hatası verme ihtimali sıfırdır!
            byte[] data = new byte[] {
        86, 105, 100, 101, 111, 79, 112, 116, 105, 109, 105, 122, 101, 114, 32, 118, 46, 48, 10,
        45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 45, 10,
        71, 101, 108, 105, 115, 116, 105, 114, 105, 99, 105, 58, 32, 79, 109, 101, 114, 32, 67, 97, 103, 97, 110, 32, 68, 101, 109, 105, 114, 107, 105, 114, 97, 110, 10,
        84, 97, 114, 105, 104, 58, 32, 48, 54, 47, 48, 55, 47, 50, 48, 50, 54, 32, 45, 32, 50, 54, 47, 48, 56, 47, 50, 48, 50, 54, 10,
        83, 116, 97, 106, 32, 121, 97, 112, 105, 108, 97, 110, 32, 98, 105, 114, 32, 112, 114, 111, 106, 101, 100, 105, 114, 46, 10,
        84, 117, 109, 32, 104, 97, 107, 108, 97, 114, 105, 32, 79, 109, 101, 114, 32, 67, 97, 103, 97, 110, 32, 68, 101, 109, 105, 114, 99, 105, 114, 97, 110, 39, 97, 32, 115, 97, 107, 108, 105, 100, 105, 114
    };

            string signature = System.Text.Encoding.UTF8.GetString(data);
            MessageBox.Show(signature, "Sistem Bilgisi", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void mainTabControl_DrawItem(object sender, DrawItemEventArgs e)
        {
            TabPage page = mainTabControl.TabPages[e.Index];
            Rectangle rect = mainTabControl.GetTabRect(e.Index);

            Color backColor = isDarkMode ? Color.FromArgb(45, 45, 48) : Color.FromArgb(240, 240, 240);
            Color foreColor = isDarkMode ? Color.White : Color.Black;

            if (e.Index == mainTabControl.SelectedIndex)
            {
                backColor = isDarkMode ? Color.FromArgb(63, 63, 70) : Color.White;
            }

            using (SolidBrush brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, rect);
            }

            TextRenderer.DrawText(e.Graphics, page.Text, mainTabControl.Font, rect, foreColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private Button CreateTransparentButton(string text, int x, int y, int width, int height, Color textColor)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Location = new Point(x, y);
            btn.Size = new Size(width, height);

            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 85);
            btn.BackColor = Color.Transparent;
            btn.ForeColor = textColor;
            btn.Cursor = Cursors.Hand;
            btn.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            btn.Tag = "normal";

            btn.MouseEnter += (s, e) => {
                if (btn != btnWatchFolder || (btn.Tag?.ToString() != "active"))
                    btn.BackColor = Color.FromArgb(60, 60, 65);
            };

            btn.MouseLeave += (s, e) => {
                if (btn.Tag != null && (btn.Tag.ToString() == "active_pause" || btn.Tag.ToString() == "active" || btn.Tag.ToString() == "active_resume"))
                {
                }
                else
                {
                    btn.BackColor = Color.Transparent;
                }
            };

            return btn;
        }

        private Button CreateModernActionButton(string text, int x, int y, int width, int height, Color backColor, Color textColor)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Location = new Point(x, y);
            btn.Size = new Size(width, height);

            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 95);
            btn.BackColor = backColor;
            btn.ForeColor = textColor;
            btn.Cursor = Cursors.Hand;
            btn.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

            btn.MouseEnter += (s, e) => {
                btn.FlatAppearance.BorderColor = Color.White;
            };

            btn.MouseLeave += (s, e) => {
                btn.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 95);
            };

            return btn;
        }

        private string GetDynamicTargetPath(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string rootTarget = string.IsNullOrEmpty(customTargetFolder) ? Path.GetDirectoryName(filePath) : customTargetFolder;

            string showName = "Diger_Projeler";
            string seasonName = "Sezon_Genel";

            Match seasonMatch = Regex.Match(fileName, @"[sS](\d{1,2})[eE]\d{1,2}");
            if (seasonMatch.Success)
            {
                string seasonNum = seasonMatch.Groups[1].Value;
                seasonName = $"Sezon_{seasonNum}";

                int index = fileName.IndexOf(seasonMatch.Value, StringComparison.OrdinalIgnoreCase);
                if (index > 0)
                {
                    string rawShowName = fileName.Substring(0, index).Trim('_');
                    showName = rawShowName.Replace("_", " ");
                }
            }
            else
            {
                string[] parts = fileName.Split('_');
                if (parts.Length > 2)
                {
                    showName = $"{parts[0]} {parts[1]}";
                }
                else
                {
                    showName = parts[0];
                }
            }

            // Örn: HedefKlasor / Dizi Adı / Sezon_X
            string targetSubFolder = Path.Combine(rootTarget, showName, seasonName);

            try
            {
                if (!Directory.Exists(targetSubFolder))
                {
                    Directory.CreateDirectory(targetSubFolder);
                }
            }
            catch { }

            return targetSubFolder;
        }

        private string GetDynamicAudioPath(string filePath)
        {
            // Videonun gideceği sezon klasörünün yolunu alıyoruz
            string seasonFolder = GetDynamicTargetPath(filePath);

            // Sezon klasörünün içinde "Ses_Dosyalari" klasörü oluşturuyoruz
            string audioSubFolder = Path.Combine(seasonFolder, "Ses_Dosyalari");

            try
            {
                if (!Directory.Exists(audioSubFolder))
                {
                    Directory.CreateDirectory(audioSubFolder);
                }
            }
            catch { }

            return audioSubFolder;
        }


        private void MenuItemDeleteOriginal_Click(object sender, EventArgs e)
        {
            if (lstQueueBox.SelectedItem is QueueItemData selectedItem)
            {
                if (selectedItem.Status != "Tamam")
                {
                    MessageBox.Show("Bu video henüz başarıyla tamamlanmamış! Sadece 'Tamam' durumundaki videoların ham dosyaları silinebilir.",
                                    "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // --- YÖNETİCİ ŞİFRESİ DOĞRULAMASI EKLENDİ ---
                string password = Microsoft.VisualBasic.Interaction.InputBox(
                    $"'{selectedItem.FileName}' adlı ham video silinecek!\nLütfen yönetici şifresini girin:",
                    "Güvenlik Doğrulaması",
                    ""
                );

                if (password != "123")
                {
                    if (!string.IsNullOrEmpty(password))
                    {
                        MessageBox.Show("Hatalı şifre! İşlem iptal edildi.", "Güvenlik", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return;
                }
                // -------------------------------------------

                DialogResult result = MessageBox.Show($"Şifre onaylandı. '{selectedItem.FileName}' adlı orijinal ham video diskten kalıcı olarak silinecek. Devam edilsin mi?",
                    "Ham Dosya Silme Onayı", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    try
                    {
                        if (File.Exists(selectedItem.FilePath))
                        {
                            File.Delete(selectedItem.FilePath);
                            MessageBox.Show("Orijinal ham video başarıyla diskten silindi.", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            UpdateStatus($"Durum: {selectedItem.FileName} ham dosyası temizlendi.");
                        }
                        else
                        {
                            MessageBox.Show("Ham dosya zaten diskte bulunamadı.", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Dosya silinirken hata oluştu:\n" + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Lütfen listeden bir video seçin.", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void BtnCleanOriginals_Click(object sender, EventArgs e)
        {
            string password = Microsoft.VisualBasic.Interaction.InputBox(
                "Bu işlem diskteki ham dosyaları kalıcı olarak silecek!\nLütfen yönetici şifresini girin:",
                "Güvenlik Doğrulaması",
                ""
            );

            if (password != "123")
            {
                if (!string.IsNullOrEmpty(password))
                {
                    MessageBox.Show("Hatalı şifre! İşlem iptal edildi.", "Güvenlik", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            DialogResult result = MessageBox.Show("Şifre onaylandı. Listelenen tüm ham videolar klasörlerinden kalıcı olarak silinecektir. Devam edilsin mi?",
                "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                int deletedCount = 0;

                foreach (QueueItemData item in queueList)
                {
                    if (item.Status == "Tamam" && File.Exists(item.FilePath))
                    {
                        try
                        {
                            File.Delete(item.FilePath);
                            deletedCount++;
                        }
                        catch { }
                    }
                }

                MessageBox.Show($"{deletedCount} adet ham video başarıyla temizlendi.", "Tamamlandı", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ApplyTheme()
        {
            Color lightBg = Color.FromArgb(245, 245, 247);
            Color lightPanel = Color.White;
            Color lightText = Color.FromArgb(30, 30, 30);

            Color darkBg = Color.FromArgb(18, 18, 18);
            Color darkPanel = Color.FromArgb(30, 30, 30);
            Color darkText = Color.FromArgb(240, 240, 240);

            Color activeBg = isDarkMode ? darkBg : lightBg;
            Color activePanelBg = isDarkMode ? darkPanel : lightPanel;
            Color activeText = isDarkMode ? darkText : lightText;



            this.BackColor = activeBg;

            if (mainTabControl != null)
            {
                mainTabControl.BackColor = activeBg;
                foreach (TabPage tp in mainTabControl.TabPages)
                {
                    tp.BackColor = activePanelBg;
                }
                mainTabControl.Invalidate();
            }

            if (pnlLeftQueue != null) pnlLeftQueue.BackColor = activePanelBg;
            if (pnlDetails != null) pnlDetails.BackColor = activePanelBg;

            if (lstQueueBox != null)
            {
                lstQueueBox.BackColor = activePanelBg;
                lstQueueBox.ForeColor = activeText;
                lstQueueBox.Invalidate();
            }

            if (tabDashboard != null)
            {
                foreach (Control ctrl in tabDashboard.Controls)
                {
                    if (ctrl is Label && ctrl != lblDropZone && ctrl != lblDashboardSizeInfo)
                        ctrl.ForeColor = activeText;

                    if (ctrl is Button btn && btn != btnStart && btn != btnPauseResume && btn != btnStop && btn != btnClearQueue && btn != btnToggleTheme && btn != btnCleanOriginals)
                    {
                        btn.ForeColor = activeText;
                        if (btn.FlatStyle == FlatStyle.Flat)
                        {
                            btn.FlatAppearance.BorderColor = isDarkMode ? Color.FromArgb(80, 80, 85) : Color.FromArgb(180, 180, 185);
                        }
                    }
                }
            }

            if (lblDashboardSizeInfo != null)
                lblDashboardSizeInfo.ForeColor = isDarkMode ? Color.LightSkyBlue : Color.DarkBlue;

            if (pnlDetails != null)
            {
                foreach (Control ctrl in pnlDetails.Controls)
                {
                    if (ctrl is Label)
                    {
                        if (ctrl == lblDetailSizeInfo)
                            ctrl.ForeColor = isDarkMode ? Color.LightSkyBlue : Color.DarkBlue;
                        else
                            ctrl.ForeColor = activeText;
                    }
                }
            }

            if (lblStatus != null) lblStatus.ForeColor = activeText;
            if (chkSplitAudio != null) chkSplitAudio.ForeColor = activeText;
            if (chkAutoMode != null) chkAutoMode.ForeColor = activeText;

            if (lblQueueCounter != null) lblQueueCounter.ForeColor = isDarkMode ? Color.FromArgb(180, 180, 180) : Color.FromArgb(80, 80, 80);
        }

        private void UpdateQueueCounter()
        {
            SafeInvoke(() => {
                int count = queueList.Count(x => x.FilePath != tempDummyFile);
                if (lblQueueCounter != null)
                {
                    lblQueueCounter.Text = $"Kuyruktaki Toplam Video Sayısı: {count}";
                }
            });
        }

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            string[] allowedExtensions = { ".ts", ".mov", ".mxf", ".mkv", ".mp4", ".avi", ".webm" };
            List<string> collectedFiles = new List<string>();

            foreach (string file in files)
            {
                if (File.Exists(file))
                {
                    string ext = Path.GetExtension(file).ToLower();
                    if (allowedExtensions.Contains(ext) && !file.Contains("__converting__") && !file.Contains("_compressed"))
                    {
                        collectedFiles.Add(file);
                    }
                }
                else if (Directory.Exists(file))
                {
                    string[] videoFiles = Directory.GetFiles(file, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => allowedExtensions.Contains(Path.GetExtension(f).ToLower()) && !f.Contains("__converting__"))
                        .ToArray();

                    collectedFiles.AddRange(videoFiles);
                }
            }

            if (collectedFiles.Count > 0)
            {
                // Otomatik turbo geçici dosyayı en başa koyup listeyi besleyen akıllı metot
                AddFilesToQueueWithTurbo(collectedFiles.ToArray());
            }

            if (queueList.Count > 0 && lstQueueBox.SelectedIndex == -1)
                lstQueueBox.SelectedIndex = 0;

            lstQueueBox.Refresh();
            Application.DoEvents();

            UpdateStatus("Durum: Sürükle-bırak ile dosyalar turbo kuyruğa eklendi.");
        }

        private void BtnClearQueue_Click(object sender, EventArgs e)
        {
            if (isRunning)
            {
                MessageBox.Show("Şu anda aktif bir dönüştürme işlemi devam ediyor! İşlem devam ederken liste temizlenemez. Lütfen önce devam eden işlemi durdurun veya iptal edin.",
                    "İşlem Devam Ediyor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            isRunning = false;
            isPaused = false;
            currentProcess = null;
            try { cts?.Cancel(); } catch { }

            queueList.Clear();
            UpdateQueueCounter();

            SafeInvoke(() => {
                btnSelectFolder.Enabled = true;
                btnSelectFiles.Enabled = true;
                btnLoadList.Enabled = true;
                btnSelectTargetFolder.Enabled = true;
                btnWatchFolder.Enabled = true;
                btnStart.Enabled = true;

                if (btnPauseResume != null) btnPauseResume.Enabled = false;
                if (btnDetailPause != null) btnDetailPause.Enabled = false;
                if (btnStop != null) btnStop.Enabled = false;
                if (btnDetailStop != null) btnDetailStop.Enabled = false;

                cmbPresets.Enabled = true;
                chkSplitAudio.Enabled = true;
                chkAutoMode.Enabled = true;
            });

            lblDetailTitle.Text = "Özet & Detay Bilgileri";
            lblDetailStatus.Text = "Status: -";
            lblDetailSource.Text = "Kaynak: -";
            lblDetailTarget.Text = "Hedef: -";
            lblDetailPreset.Text = "Ön Ayar: -";
            lblDetailSizeInfo.Text = "İşlem bekleniyor...";
            if (lblDashboardSizeInfo != null) lblDashboardSizeInfo.Text = "Dönüştürme Bilgisi: Listeden bir video seçin...";

            lstQueueBox.Refresh();
            UpdateStatus("Durum: Kuyruk tamamen temizlendi ve sıfırlandı. Yeni video bırakabilirsiniz.");
        }

        private void BtnSelectFolder_Click(object sender, EventArgs e)
        {
            nasFolderPath = string.Empty;

            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Ana Dizi veya Video Klasörünü Seçin";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    string selectedFolderPath = fbd.SelectedPath;
                    nasFolderPath = selectedFolderPath;

                    string[] allowedExtensions = { ".ts", ".mov", ".mxf", ".mkv", ".mp4", ".avi", ".webm" };

                    try
                    {
                        // VOB dosyası kontrolü
                        string[] directVobFiles = Directory.GetFiles(selectedFolderPath, "*.vob", SearchOption.TopDirectoryOnly)
                                                 .OrderBy(f => f)
                                                 .ToArray();

                        queueList.Clear();
                        isRunning = false;
                        currentProcess = null;

                        if (directVobFiles.Length > 0)
                        {
                            string listFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"vob_list_{Guid.NewGuid()}.txt");

                            using (StreamWriter sw = new StreamWriter(listFilePath, false, new UTF8Encoding(false)))
                            {
                                foreach (var vob in directVobFiles)
                                {
                                    sw.WriteLine($"file '{Path.GetFullPath(vob).Replace("\\", "/")}'");
                                }
                            }

                            string dirName = Path.GetFileName(selectedFolderPath);
                            string displayName = string.IsNullOrEmpty(dirName) ? "DVD_Birlestirilmis" : dirName + "_DVD";

                            var itemData = new QueueItemData
                            {
                                FilePath = listFilePath,
                                FileName = displayName,
                                Status = "Bekliyor",
                                Percent = 0,
                                TimeRemaining = "00:00:00",
                                Fps = 0,
                                Resolution = "DVD (VOB Listesi)",
                                StartTimeText = "-",
                                EndTimeText = "-",
                                AudioStreamCount = 2,
                                Bitrate = "Bilinmiyor"
                            };

                            queueList.Add(itemData);
                            UpdateStatus($"Durum: {directVobFiles.Length} adet VOB parçası birleştirme kuyruğuna eklendi.");
                        }
                        else
                        {
                            string[] videoFiles = Directory.GetFiles(selectedFolderPath, "*.*", SearchOption.TopDirectoryOnly)
                                                    .Where(file => allowedExtensions.Contains(Path.GetExtension(file).ToLower()))
                                                    .OrderBy(f => f)
                                                    .ToArray();

                            if (videoFiles.Length > 0)
                            {
                                // Geçersiz/yedek dosyaları eleyip geçerli videoları seçiyoruz
                                var validVideos = videoFiles
                                    .Where(file => !file.Contains("__converting__") && !file.Contains("_compressed") && !file.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
                                    .ToArray();

                                if (validVideos.Length > 0)
                                {
                                    // Akıllı Turbo Başlangıç metodu ile listeye dahil ediyoruz
                                    AddFilesToQueueWithTurbo(validVideos);
                                    UpdateStatus($"Durum: Seçilen klasörden {validVideos.Length} adet video turbo kuyruğa eklendi.");
                                }
                                else
                                {
                                    MessageBox.Show("Seçilen klasörde işlenebilecek uygun formatta video bulunamadı!", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                }
                            }
                            else
                            {
                                MessageBox.Show("Seçilen klasörde ne desteklenen video dosyası ne de VOB arşivi bulunamadı!", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                        }

                        lstQueueBox.Refresh();
                        Application.DoEvents();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Klasör taranırken hata oluştu:\n" + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void BtnSelectFiles_Click(object sender, EventArgs e)
        {
            nasFolderPath = string.Empty;

            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "İşlenecek Videoları Seçin";
                ofd.Filter = "Video Dosyaları|*.ts;*.mov;*.mxf;*.mkv;*.mp4;*.avi;*.webm|Tüm Dosyalar|*.*";
                ofd.Multiselect = true;

                if (ofd.ShowDialog() == DialogResult.OK && ofd.FileNames.Length > 0)
                {
                    isRunning = false;
                    currentProcess = null;
                    queueList.Clear();
                    nasFolderPath = Path.GetDirectoryName(ofd.FileNames[0]);

                    // Geçersiz dosyaları eleyerek seçilenleri alıyoruz
                    List<string> selectedFiles = ofd.FileNames
                        .Where(file => !file.Contains("__converting__") && !file.Contains("_compressed"))
                        .ToList();

                    if (selectedFiles.Count > 0)
                    {
                        // Artık "Video Seç" ile dosya eklendiğinde de turbo mantığı ve video/ses klasörleme düzgünce çalışacak!
                        AddFilesToQueueWithTurbo(selectedFiles.ToArray());
                    }

                    if (queueList.Count > 0 && lstQueueBox.SelectedIndex == -1)
                        lstQueueBox.SelectedIndex = 0;

                    lstQueueBox.Refresh();
                    Application.DoEvents();

                    UpdateStatus($"Durum: {queueList.Count} adet video turbo kuyruğa eklendi.");
                }
            }
        }

        private void BtnSelectTargetFolder_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Çıktıların Dağıtılacağı Ana Hedef Klasörü (Örn: Arşiv Kök Dizini) Seçin";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    string selectedPath = fbd.SelectedPath;

                    // Yazma izni kontrolü
                    if (!IsDirectoryWritable(selectedPath))
                    {
                        MessageBox.Show("Seçilen dizine yazma izniniz bulunmuyor! Lütfen yetkinizin olduğu bir klasör seçin.", "Yetki Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    customTargetFolder = selectedPath;
                    UpdateStatus($"Durum: Ana hedef kök klasör ayarlandı -> {customTargetFolder}");
                    MessageBox.Show($"Ana hedef klasör başarıyla seçildi:\n{customTargetFolder}\n\nVideolar isimlerine göre ilgili Dizi ve Sezon alt klasörlerine doğrudan yönlendirilecektir.", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    if (lstQueueBox.SelectedItem is QueueItemData data)
                    {
                        DisplayItemDetails(data);
                    }
                }
            }
        }

        private bool IsDirectoryWritable(string folderPath)
        {
            try
            {
                string testFile = Path.Combine(folderPath, "test_permission.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void BtnWatchFolder_Click(object sender, EventArgs e)
        {
            if (folderWatchTimer.Enabled || !string.IsNullOrEmpty(targetWatchFolderPath))
            {
                folderWatchTimer.Stop();
                targetWatchFolderPath = string.Empty;

                btnWatchFolder.Text = "🔍 Klasör İzle";
                btnWatchFolder.Tag = "normal";
                btnWatchFolder.BackColor = Color.Transparent;

                UpdateStatus("Durum: Harici klasör izleme kapatıldı.");
                Application.DoEvents();

                MessageBox.Show("Klasör izleme modu kapatıldı.", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Otomatik İzlenecek ve İçine Video Geldikçe İşlenecek Klasörü Seçin";
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        targetWatchFolderPath = fbd.SelectedPath;

                        folderWatchTimer.Stop();
                        folderWatchTimer.Start();

                        btnWatchFolder.Text = "⏹ İzlemeyi Kapat";
                        btnWatchFolder.Tag = "active";
                        btnWatchFolder.BackColor = Color.FromArgb(16, 124, 65);

                        UpdateStatus($"Durum: Klasör izleniyor -> {targetWatchFolderPath}");
                        MessageBox.Show($"Klasör başarıyla izlemeye alındı:\n{targetWatchFolderPath}\n\nBu klasöre yeni video geldikçe otomatik kuyruğa eklenecektir.", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
        }

        private void SafeInvoke(Action action)
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;
            if (this.InvokeRequired)
            {
                try { this.Invoke(action); } catch { }
            }
            else { action(); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            cts?.Cancel();
            try { currentProcess?.Kill(); } catch { }

            // --- GİZLİ TURBO BAŞLANGIÇ DOSYASI TEMİZLİĞİ ---
            try
            {
                if (!string.IsNullOrEmpty(tempDummyFile) && File.Exists(tempDummyFile))
                    File.Delete(tempDummyFile);
            }
            catch { }
            // ----------------------------------------------

            base.OnFormClosing(e);
        }

        private void LstQueueBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= queueList.Count) return;

            var itemData = queueList[e.Index];
            if (itemData == null) return;

            Graphics g = e.Graphics;
            Rectangle rect = e.Bounds;

            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            Color normalBg = isDarkMode ? Color.FromArgb(45, 45, 48) : Color.White;
            Color selectBg = isDarkMode ? Color.FromArgb(62, 62, 66) : Color.FromArgb(210, 230, 255);
            Color textColor = isDarkMode ? Color.White : Color.Black;

            if (isSelected) g.FillRectangle(new SolidBrush(selectBg), rect);
            else g.FillRectangle(new SolidBrush(normalBg), rect);

            string statusIcon = "⏳";
            Brush iconBrush = Brushes.Gray;
            if (itemData.Status == "Tamam") { statusIcon = "✔"; iconBrush = Brushes.ForestGreen; }
            else if (itemData.Status.StartsWith("İşleniyor") || itemData.Status.StartsWith("Ses")) { statusIcon = "🔄"; iconBrush = Brushes.DarkOrange; }                                                                                                                                                                                          //demirkırn 654897
            else if (itemData.Status == "Duraklatıldı") { statusIcon = "⏸"; iconBrush = Brushes.Goldenrod; }
            else if (itemData.Status == "İptal Edildi" || itemData.Status == "Hata" || itemData.Status.StartsWith("Hata")) { statusIcon = "❌"; iconBrush = Brushes.Red; }

            Font fontTitle = new Font("Segoe UI", 9f, FontStyle.Bold);
            g.DrawString(statusIcon, fontTitle, iconBrush, rect.X + 8, rect.Y + 6);

            Rectangle textRect = new Rectangle(rect.X + 28, rect.Y + 6, rect.Width - 40, 20);
            TextRenderer.DrawText(g, itemData.FileName, fontTitle, textRect, textColor, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            if (itemData.Status.StartsWith("İşleniyor") || itemData.Status.StartsWith("Ses") || itemData.Status == "Duraklatıldı")
            {
                Rectangle barBg = new Rectangle(rect.X + 28, rect.Y + 28, rect.Width - 45, 5);
                g.FillRectangle(isDarkMode ? Brushes.DimGray : Brushes.LightGray, barBg);

                int filledWidth = (int)((barBg.Width * itemData.Percent) / 100.0);
                if (filledWidth > 0)
                {
                    Rectangle barFill = new Rectangle(barBg.X, barBg.Y, filledWidth, barBg.Height);
                    g.FillRectangle(itemData.Status == "Duraklatıldı" ? Brushes.Goldenrod : Brushes.ForestGreen, barFill);
                }

                Font fontSub = new Font("Segoe UI", 8f, FontStyle.Regular);
                string subText = itemData.Status == "Duraklatıldı"
                    ? $"DURAKLATILDI - Kaldığı Yer: %{itemData.Percent}"
                    : (itemData.Status.StartsWith("Ses") ? $"{itemData.Status} (%{itemData.Percent})" : $"Donanım Aktif, %{itemData.Percent}, {itemData.Fps:F1} fps, TAHMİNİ: {itemData.TimeRemaining}");

                g.DrawString(subText, fontSub, isDarkMode ? Brushes.LightGray : Brushes.DimGray, rect.X + 28, rect.Y + 36);
            }
            else if (itemData.Status == "Tamam")
            {
                Rectangle barBg = new Rectangle(rect.X + 28, rect.Y + 28, rect.Width - 45, 5);
                g.FillRectangle(Brushes.ForestGreen, barBg);

                Font fontSub = new Font("Segoe UI", 8f, FontStyle.Regular);
                string subText = $"Çıktı: {itemData.ResultSizeMb:F1} MB (Kazanç: %{itemData.SavedPercent:F1})";
                g.DrawString(subText, fontSub, Brushes.ForestGreen, rect.X + 28, rect.Y + 36);
            }
            else if (itemData.Status == "İptal Edildi" || itemData.Status == "Hata" || itemData.Status.StartsWith("Hata"))
            {
                Font fontSub = new Font("Segoe UI", 8f, FontStyle.Italic);
                g.DrawString($"İşlem {itemData.Status.ToLower()}.", fontSub, Brushes.Red, rect.X + 28, rect.Y + 36);
            }
            else
            {
                Font fontSub = new Font("Segoe UI", 8f, FontStyle.Italic);
                g.DrawString("Sırada bekliyor...", fontSub, isDarkMode ? Brushes.DarkGray : Brushes.Gray, rect.X + 28, rect.Y + 36);
            }

            g.DrawLine(isDarkMode ? new Pen(Color.FromArgb(60, 60, 60)) : Pens.WhiteSmoke, rect.Left, rect.Bottom - 1, rect.Right, rect.Bottom - 1);
        }

        private void InitAutoTimer()
        {
            autoTimer = new System.Windows.Forms.Timer();
            autoTimer.Interval = 5000;
            autoTimer.Tick += AutoTimer_Tick;
        }

        private FileSystemWatcher fileWatcher;

        private void InitFolderWatchTimer()
        {
            folderWatchTimer = new System.Windows.Forms.Timer();
            folderWatchTimer.Interval = 5000;
            folderWatchTimer.Tick += FolderWatchTimer_Tick;
        }

        private void LogToDetail(string title, string status, string source, string target, string preset, string sizeInfo)
        {
            SafeInvoke(() => {
                lblDetailTitle.Text = title;
                lblDetailStatus.Text = $"Status: {status}";
                lblDetailSource.Text = $"Kaynak:\n{source}";
                lblDetailTarget.Text = $"Hedef:\n{target}";
                lblDetailPreset.Text = $"Ön Ayar: {preset}";
                lblDetailSizeInfo.Text = sizeInfo;
                if (lblDashboardSizeInfo != null)
                {
                    lblDashboardSizeInfo.Text = $"SEÇİLİ VİDEO: {title}\n" + sizeInfo;
                }
            });
        }

        private void UpdateStatus(string statusText)
        {
            SafeInvoke(() => { lblStatus.Text = statusText; });
        }

        private bool IsFileReady(string filePath)
        {
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                FileInfo fi = new FileInfo(filePath);
                long initialSize = fi.Length;
                Thread.Sleep(3000);
                fi.Refresh();
                long finalSize = fi.Length;
                return initialSize == finalSize && finalSize > 0;
            }
            catch { return false; }
        }

        private void AddVideoToQueue(string filePath)
        {
            if (!filePath.Contains("__converting__") && !filePath.Contains("_compressed") && !filePath.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
            {
                bool alreadyInQueue = queueList.Any(x => x.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));

                if (!alreadyInQueue)
                {
                    var itemData = new QueueItemData
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        Status = "Bekliyor",
                        Percent = 0,
                        TimeRemaining = "00:00:00",
                        Fps = 0,
                        Resolution = "Seçilmedi / Bekliyor", // Tıklanınca yüklenecek
                        Bitrate = "-",                     // Tıklanınca yüklenecek
                        AudioStreamCount = 0
                    };

                    try
                    {
                        string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                        if (File.Exists(ffmpegPath))
                        {
                            itemData.AudioStreamCount = GetAudioStreamCount(ffmpegPath, filePath);
                            var (dur, res, br) = GetVideoInfo(ffmpegPath, filePath);
                            if (!string.IsNullOrEmpty(res) && res != "Bilinmiyor") itemData.Resolution = res;
                            if (!string.IsNullOrEmpty(br) && br != "Bilinmiyor") itemData.Bitrate = br;
                        }
                    }
                    catch { }

                    queueList.Add(itemData);
                    UpdateQueueCounter();
                }
            }


        }

        private void AddFilesToQueueWithTurbo(string[] filePaths)
        {
            List<string> validFiles = new List<string>();
            foreach (string file in filePaths)
            {
                if (File.Exists(file) && !queueList.Any(x => x.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                {
                    validFiles.Add(file);
                }
            }

            if (validFiles.Count == 0) return;

            int projectedTotalCount = queueList.Count(x => x.FilePath != tempDummyFile) + validFiles.Count;

            if (projectedTotalCount >= 10)
            {
                if (string.IsNullOrEmpty(tempDummyFile) || !File.Exists(tempDummyFile))
                {
                    tempDummyFile = Path.Combine(Path.GetTempPath(), "optimizer_turbo_start.mp4");
                    if (!File.Exists(tempDummyFile))
                    {
                        File.WriteAllBytes(tempDummyFile, new byte[0]);
                    }
                }

                if (!queueList.Any(x => x.FilePath == tempDummyFile))
                {
                    var dummyItem = new QueueItemData
                    {
                        FilePath = tempDummyFile,
                        FileName = "Turbo_Start_Optimizer",
                        Status = "Turbo",
                        Resolution = "Turbo",
                        Bitrate = "-"
                    };
                    queueList.Insert(0, dummyItem);
                }
            }
            else
            {
                var existingDummy = queueList.FirstOrDefault(x => x.FilePath == tempDummyFile);
                if (existingDummy != null)
                {
                    queueList.Remove(existingDummy);
                }
            }

            foreach (string file in validFiles)
            {
                AddVideoToQueue(file);
            }

            if (queueList.Count > 0 && lstQueueBox.SelectedIndex == -1)
                lstQueueBox.SelectedIndex = 0;

            lstQueueBox.Refresh();
            Application.DoEvents();
            UpdateQueueCounter();

        }

        private void BtnLoadList_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Video Yollarını İçeren Txt/Csv Dosyasını Seçin";
                ofd.Filter = "Metin/CSV Dosyaları|*.txt;*.csv|Tüm Dosyalar|*.*";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string[] lines = File.ReadAllLines(ofd.FileName, System.Text.Encoding.UTF8);
                        int addedCount = 0;

                        foreach (string line in lines)
                        {
                            string path = line.Trim().Trim('"');
                            if (!string.IsNullOrEmpty(path))
                            {
                                AddVideoToQueue(path);
                                addedCount++;
                            }
                        }

                        if (queueList.Count > 0) lstQueueBox.SelectedIndex = 0;
                        UpdateStatus($"Durum: Listeden {addedCount} adet geçerli video kuyruğa eklendi.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Liste okunurken hata oluştu:\n" + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void DisplayItemDetails(QueueItemData data)
        {
            if (data != null && File.Exists(data.FilePath))
            {
                FileInfo fi = new FileInfo(data.FilePath);
                double sizeMb = fi.Length / (1024.0 * 1024.0);

                string statusDescription = "Bekliyor";

                string calculatedCrf = "18";
                try
                {
                    if (!string.IsNullOrEmpty(data.Bitrate))
                    {
                        string cleanBitrateStr = System.Text.RegularExpressions.Regex.Replace(data.Bitrate, @"[^\d]", "");

                        if (double.TryParse(cleanBitrateStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double realBitrate))
                        {
                            // Ham 1080p ve 4K belgesel/dizi akışına göre optimize edilmiş eşikler:
                            if (realBitrate > 50000) calculatedCrf = "14"; // Ham 4K veya aşırı yüksek bit hızları -> CQ 14
                            else if (realBitrate < 4000) calculatedCrf = "20"; // Düşük kalite kaynak -> CQ 20
                            else calculatedCrf = "18"; // Ham 1080p dizi ve belgesellerin çoğu burada buluşur -> CQ 18
                        }
                    }
                }
                catch { }

                string sourceExt = Path.GetExtension(data.FilePath).ToUpper();
                string outputSubFolder = GetDynamicTargetPath(data.FilePath);
                string targetFile = Path.Combine(outputSubFolder, Path.GetFileNameWithoutExtension(data.FilePath) + ".mp4");

                string selectedPresetText = cmbPresets.SelectedItem?.ToString() ?? "";

                string presetDisplay = selectedPresetText;
                if (selectedPresetText.Contains("Otomatik Akıllı Mod"))
                {
                    presetDisplay = $"Otomatik Akıllı Mod -> {calculatedCrf}";
                }

                string conversionSummary = $"• Dönüştürme: {sourceExt}  ➔  MP4 (H.264 Video + AAC Ses) | [{presetDisplay}]";

                string audioSummary = data.AudioStreamCount > 0 ? $"{data.AudioStreamCount} Adet Ses Kanalı Bulundu" : "Ses kanalı yok veya taranmadı";
                if (chkSplitAudio.Checked && data.AudioStreamCount > 0)
                {
                    audioSummary += " (.WAV Olarak Ayrıştırılacak)";
                }

                string trimSummary = "Kesme: Yok (Tamamı Alınıyor)";
                if (!string.IsNullOrEmpty(data.TrimStart) || !string.IsNullOrEmpty(data.TrimEnd))
                {
                    string startStr = string.IsNullOrEmpty(data.TrimStart) ? "Baştan" : data.TrimStart;
                    string endStr = string.IsNullOrEmpty(data.TrimEnd) ? "Sona Kadar" : data.TrimEnd;
                    trimSummary = $"Kesme Aralığı: {startStr} -> {endStr}";
                }

                string detailsText = $"İşlem Özeti & Boyut Analizi:\n{conversionSummary}\nOrijinal Çözünürlük: {data.Resolution} | Bitrate: {data.Bitrate}\nOrijinal Boyut: {sizeMb:F2} MB\n\nKesme Bilgisi:\n• {trimSummary}\n\nSes Bilgisi:\n• {audioSummary}\n\nZaman Bilgileri:\nBaşlangıç: {data.StartTimeText}\nBitiş: {data.EndTimeText}";

                if (data.Status == "Tamam")
                {
                    statusDescription = "Başarıyla Tamamlandı";
                    double savedMb = sizeMb - data.ResultSizeMb;
                    string wavOutputInfo = (chkSplitAudio.Checked && data.AudioStreamCount > 0) ? $"\n• Ses: {data.AudioStreamCount} kanal .wav olarak kaydedildi." : "";
                    detailsText = $"İşlem Tamamlandı:\n{conversionSummary}\nOrijinal: {sizeMb:F2} MB | Yeni: {data.ResultSizeMb:F2} MB\nKazanç: %{data.SavedPercent:F1} ({savedMb:F2} MB azaldı)\n\nKesme Bilgisi:\n• {trimSummary}\n\nÇıktılar:\n• Video: {Path.GetFileName(targetFile)}{wavOutputInfo}\n\nZaman Bilgileri:\nBaşlangıç: {data.StartTimeText}\nBitiş: {data.EndTimeText}";
                }
                else if (data.Status.StartsWith("İşleniyor") || data.Status.StartsWith("Ses") || data.Status == "Duraklatıldı")
                {
                    statusDescription = $"{data.Status} - %{data.Percent}";
                    detailsText = $"İşlem Sürüyor:\n{conversionSummary}\nOrijinal Boyut: {sizeMb:F2} MB | İlerleme: %{data.Percent}\n\nKesme Bilgisi:\n• {trimSummary}\n\nSes Bilgisi:\n• {audioSummary}\n\nZaman Bilgileri:\nBaşlangıç: {data.StartTimeText}\nBitiş: {data.EndTimeText}";
                }
                else if (data.Status == "İptal Edildi" || data.Status == "Hata" || data.Status.StartsWith("Hata"))
                {
                    statusDescription = data.Status;
                }

                LogToDetail(data.FileName, statusDescription, data.FilePath, targetFile, selectedPresetText, detailsText);
            }
        }

        private async void BtnStart_Click(object sender, EventArgs e)
        {
            if (queueList.Count == 0)
            {
                MessageBox.Show("Önce video veya klasör seçin!", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (isRunning) return;

            // "Tamam" ve "İptal Edildi" olmama şartıyla, listedeki diğer tüm videoları işleme hazır hale getirelim:
            foreach (var item in queueList)
            {
                if (item.Status != "Tamam" && item.Status != "İptal Edildi")
                {
                    item.Status = "Bekliyor";
                    item.Percent = 0;
                }
            }
            lstQueueBox.Invalidate();

            isRunning = true;
            isPaused = false;

            btnSelectFolder.Enabled = false;
            btnSelectFiles.Enabled = false;
            btnLoadList.Enabled = false;
            btnSelectTargetFolder.Enabled = false;
            btnWatchFolder.Enabled = false;
            btnStart.Enabled = false;

            btnPauseResume.Enabled = true;
            btnPauseResume.Text = "Duraklat";
            btnPauseResume.BackColor = Color.FromArgb(0, 120, 212);
            btnPauseResume.Tag = "active_pause";

            btnStop.Enabled = true;

            if (btnDetailPause != null)
            {
                btnDetailPause.Enabled = true;
                btnDetailPause.Text = "Duraklat";
                btnDetailPause.BackColor = Color.FromArgb(0, 120, 212);
            }
            if (btnDetailStop != null) btnDetailStop.Enabled = true;

            cmbPresets.Enabled = false;
            chkSplitAudio.Enabled = false;
            chkAutoMode.Enabled = false;
            cts = new CancellationTokenSource();

            // Kuyruktaki toplam gerçek video sayısını alalım (Turbo dummy dosyasını saymıyoruz)
            int totalVideoCount = queueList.Count(x => x.FilePath != tempDummyFile);

            UpdateStatus($"Durum: {totalVideoCount} adet video için kuyruk işlenmeye başlandı (Akıllı Hedef Eşleme Aktif)...");

            try
            {
                string selectedPreset = "Yüksek Kalite (HQ - RF/CQ 18)";
                SafeInvoke(() => {
                    if (cmbPresets.SelectedItem != null) selectedPreset = cmbPresets.SelectedItem.ToString();
                });

                string selectedEncoderMode = "Otomatik";
                SafeInvoke(() => {
                    Control[] foundControls = tabDashboard.Controls.Find("cmbEncoderMode", true);
                    if (foundControls.Length > 0 && foundControls[0] is ComboBox cb && cb.SelectedItem != null)
                    {
                        selectedEncoderMode = cb.SelectedItem.ToString();
                    }
                });

                await Task.Run(() => ProcessQueueItems(selectedPreset, selectedEncoderMode, cts.Token));
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Durum: İşlem kullanıcı tarafından iptal edildi. Sıradakiler kontrol ediliyor...");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Sistemsel bir hata oluştu:\n" + ex.Message, "Kritik Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("Durum: İşlem durduruldu (Hata).");
            }
            finally
            {
                isRunning = false;
                isPaused = false;
                SafeInvoke(() => {
                    btnSelectFolder.Enabled = true;
                    btnSelectFiles.Enabled = true;
                    btnLoadList.Enabled = true;
                    btnSelectTargetFolder.Enabled = true;
                    btnWatchFolder.Enabled = true;
                    btnStart.Enabled = true;

                    if (btnPauseResume != null)
                    {
                        btnPauseResume.Enabled = false;
                        btnPauseResume.Text = "Duraklat";
                        btnPauseResume.BackColor = Color.FromArgb(0, 120, 212);
                    }
                    if (btnDetailPause != null)
                    {
                        btnDetailPause.Enabled = false;
                        btnDetailPause.Text = "Duraklat";
                        btnDetailPause.BackColor = Color.FromArgb(0, 120, 212);
                    }
                    if (btnDetailStop != null) btnDetailStop.Enabled = false;

                    btnStop.Enabled = false;
                    cmbPresets.Enabled = true;
                    chkSplitAudio.Enabled = true;
                    chkAutoMode.Enabled = true;
                });
            }
        }

        private void BtnPauseResume_Click(object sender, EventArgs e)
        {
            if (lstQueueBox.SelectedItem is QueueItemData selectedItem)
            {
                if (selectedItem.Status == "İptal Edildi" || selectedItem.Status == "Tamam" || selectedItem.Status == "Hata")
                {
                    MessageBox.Show("İptal edilmiş veya tamamlanmış bir video üzerinde duraklatma/devam etme işlemi yapılamaz!",
                        "Geçersiz İşlem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            if ((isRunning || folderWatchTimer.Enabled) && currentProcess != null)
            {
                if (!isPaused)
                {
                    try
                    {
                        foreach (ProcessThread thread in currentProcess.Threads)
                        {
                            var pOpenThread = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)thread.Id);
                            if (pOpenThread != IntPtr.Zero) { SuspendThread(pOpenThread); CloseHandle(pOpenThread); }
                        }
                        isPaused = true;

                        btnPauseResume.Text = "Devam Et";
                        btnPauseResume.BackColor = Color.FromArgb(202, 81, 0);
                        btnPauseResume.ForeColor = Color.White;
                        btnPauseResume.Tag = "active_resume";

                        if (btnDetailPause != null)
                        {
                            btnDetailPause.Text = "Devam Et";
                            btnDetailPause.BackColor = Color.FromArgb(202, 81, 0);
                        }

                        if (lstQueueBox.SelectedIndex != -1 && queueList.Count > lstQueueBox.SelectedIndex)
                        {
                            queueList[lstQueueBox.SelectedIndex].Status = "Duraklatıldı";
                        }
                        lstQueueBox.Invalidate();

                        UpdateStatus("Durum: İşlem duraklatıldı.");
                    }
                    catch (Exception ex) { MessageBox.Show("Hata: " + ex.Message); }
                }
                else
                {
                    try
                    {
                        foreach (ProcessThread thread in currentProcess.Threads)
                        {
                            var pOpenThread = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)thread.Id);
                            if (pOpenThread != IntPtr.Zero) { ResumeThread(pOpenThread); CloseHandle(pOpenThread); }
                        }
                        isPaused = false;

                        btnPauseResume.Text = "Duraklat";
                        btnPauseResume.BackColor = Color.FromArgb(0, 120, 212);
                        btnPauseResume.ForeColor = Color.White;
                        btnPauseResume.Tag = "active_pause";

                        if (btnDetailPause != null)
                        {
                            btnDetailPause.Text = "Duraklat";
                            btnDetailPause.BackColor = Color.FromArgb(0, 120, 212);
                        }

                        if (lstQueueBox.SelectedIndex != -1 && queueList.Count > lstQueueBox.SelectedIndex)
                        {
                            queueList[lstQueueBox.SelectedIndex].Status = "İşleniyor";
                        }
                        lstQueueBox.Invalidate();

                        UpdateStatus("Durum: İşlem devam ediyor...");
                    }
                    catch (Exception ex) { MessageBox.Show("Hata: " + ex.Message); }
                }
            }
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            if (currentProcess != null)
            {
                try
                {
                    foreach (ProcessThread thread in currentProcess.Threads)
                    {
                        var pOpenThread = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)thread.Id);
                        if (pOpenThread != IntPtr.Zero)
                        {
                            ResumeThread(pOpenThread);
                            CloseHandle(pOpenThread);
                        }
                    }
                }
                catch { }
            }

            cts?.Cancel();
            try { currentProcess?.Kill(); } catch { }
            isPaused = false;
            isRunning = false;

            var activeItem = queueList.FirstOrDefault(x => x.Status.StartsWith("İşleniyor") || x.Status.StartsWith("Ses") || x.Status == "Duraklatıldı");
            if (activeItem != null)
            {
                activeItem.Status = "İptal Edildi";
                activeItem.EndTimeText = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            }

            lstQueueBox.Invalidate();
            UpdateStatus("Durum: İşlem iptal edildi. Sıradakiler kontrol ediliyor...");

            SafeInvoke(() => {
                var nextItem = queueList.FirstOrDefault(x => x.Status == "Bekliyor");
                if (nextItem != null)
                {
                    int nextIdx = queueList.IndexOf(nextItem);
                    if (nextIdx != -1)
                    {
                        lstQueueBox.SelectedIndex = nextIdx;
                    }
                }
            });
        }

        private void WriteStatusForPython(string fileName, string status, int percent)
        {
            try
            {
                string statusFile = Path.Combine(syncFolderPath, "durum.txt");
                string content = $"{fileName}|{status}|{percent}";
                File.WriteAllText(statusFile, content);
            }
            catch { }
        }

        private void ProcessQueueItems(string preset, string encoderMode, CancellationToken token)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string ffmpegPath = Path.Combine(baseDirectory, "ffmpeg.exe");

            if (!File.Exists(ffmpegPath))
            {
                SafeInvoke(() => MessageBox.Show("ffmpeg.exe bulunamadı! Lütfen program klasörüne atın.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error));
                return;
            }

            string videoEncoder = DetectBestEncoder(ffmpegPath);

            string userChoiceEncoderMode = "Otomatik";
            SafeInvoke(() => {
                foreach (Control ctrl in tabDashboard.Controls)
                {
                    if (ctrl is ComboBox cb && cb.Name == "cmbEncoderMode" && cb.SelectedItem != null)
                    {
                        userChoiceEncoderMode = cb.SelectedItem.ToString();
                        break;
                    }
                }
            });

            while (true)
            {
                token.ThrowIfCancellationRequested();

                QueueItemData qData = null;
                SafeInvoke(() => {
                    qData = queueList.FirstOrDefault(x => x.Status == "Bekliyor" || x.Status == "Turbo");
                    if (qData != null)
                    {
                        int index = queueList.IndexOf(qData);
                        if (index != -1)
                        {
                            lstQueueBox.SelectedIndex = index;
                            lstQueueBox.Invalidate(lstQueueBox.GetItemRectangle(index));
                        }
                    }
                });

                if (qData == null) break;

                // --- GİZLİ TURBO BAŞLANGIÇ DOSYASI KONTROLÜ ---
                if (qData.FilePath == tempDummyFile || qData.Status == "Turbo")
                {
                    qData.Status = "Tamam";
                    try { if (File.Exists(tempDummyFile)) File.Delete(tempDummyFile); } catch { }
                    continue; // Motoru tetikledikten sonra bu sahte/geçici dosyayı anında atla!
                }
                // ----------------------------------------------

                string nameOnly = Path.GetFileNameWithoutExtension(qData.FilePath);
                string outputSubFolder = GetDynamicTargetPath(qData.FilePath);

                StringBuilder logBuilder = new StringBuilder();
                logBuilder.AppendLine($"=== VİDEO OPTİMİZER LOG KAYDI ===");
                logBuilder.AppendLine($"Başlangıç Zamanı: {DateTime.Now}");
                logBuilder.AppendLine($"Dosya Yolu: {qData.FilePath}");
                logBuilder.AppendLine($"Ön Ayar (Preset): {preset}");
                logBuilder.AppendLine($"Başlangıçta Denenen Encoder: {videoEncoder}");

                string trimLogInfo = "Kesme İşlemi: Uygulanmadı (Tamamı Alınıyor)";
                if (!string.IsNullOrEmpty(qData.TrimStart) || !string.IsNullOrEmpty(qData.TrimEnd))
                {
                    string startStr = string.IsNullOrEmpty(qData.TrimStart) ? "Baştan" : qData.TrimStart;
                    string endStr = string.IsNullOrEmpty(qData.TrimEnd) ? "Sona Kadar" : qData.TrimEnd;
                    trimLogInfo = $"Kesme Aralığı: [{startStr}] - [{endStr}] arasında kırpılacak.";
                }
                logBuilder.AppendLine(trimLogInfo);
                logBuilder.AppendLine("--------------------------------------------------");

                SaveLogFile(outputSubFolder, nameOnly + "_baslangic", logBuilder);

                int audioStreamCount = 0;
                if (!qData.FilePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        audioStreamCount = GetAudioStreamCount(ffmpegPath, qData.FilePath);
                        logBuilder.AppendLine($"Tespit Edilen Ses Kanal Sayısı: {audioStreamCount}");
                    }
                    catch (Exception ex)
                    {
                        logBuilder.AppendLine($"Ses Kanalı Taranırken Hata: {ex.Message}");
                    }
                }
                else
                {
                    audioStreamCount = 2;
                }
                qData.AudioStreamCount = audioStreamCount;

                SafeInvoke(() => {
                    qData.Status = "İşleniyor";
                    qData.Percent = 0;
                    qData.StartTimeText = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                    DisplayItemDetails(qData);
                });

                WriteStatusForPython(qData.FileName, "İşleniyor", 0);

                double totalSeconds = 0;
                string originalResolution = "1920x1080";
                string originalBitrate = "Bilinmiyor";

                if (!qData.FilePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    var videoInfo = GetVideoInfo(ffmpegPath, qData.FilePath);
                    totalSeconds = videoInfo.duration;
                    originalResolution = videoInfo.resolution;
                    originalBitrate = videoInfo.bitrate;
                }
                else
                {
                    totalSeconds = 2919;
                }

                qData.Resolution = originalResolution;
                qData.Bitrate = originalBitrate;
                logBuilder.AppendLine($"Video Süresi (Saniye): {totalSeconds}, Çözünürlük: {originalResolution}");

                string ssParam = !string.IsNullOrEmpty(qData.TrimStart) ? $"-ss {qData.TrimStart} " : "";
                string toParam = !string.IsNullOrEmpty(qData.TrimEnd) ? $"-to {qData.TrimEnd} " : "";

                // İçerideki karmaşık arama döngüsü yerine doğrudan parametreden gelen değeri kullanıyoruz:
                string activeEncoder = videoEncoder;
                string cleanRes = (originalResolution ?? "").Trim();

                if (encoderMode.Contains("Sadece CPU"))
                {
                    activeEncoder = "libx264";
                    logBuilder.AppendLine("BİLGİ: Kullanıcı manuel olarak Sadece İşlemci (CPU - libx264) seçti.");
                }
                else if (encoderMode.Contains("Sadece GPU"))
                {
                    if (videoEncoder != "libx264")
                    {
                        activeEncoder = videoEncoder;
                        logBuilder.AppendLine($"BİLGİ: Kullanıcı manuel olarak Donanım (GPU: {activeEncoder}) seçti.");
                    }
                    else
                    {
                        logBuilder.AppendLine("BİLGİ: Kullanıcı GPU seçti ancak sistemde uygun GPU bulunamadı, CPU'ya devam ediliyor.");
                    }
                }
                else // Otomatik
                {
                    if (qData.FilePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        activeEncoder = "libx264";
                        logBuilder.AppendLine("BİLGİ: DVD VOB birleştirmesi olduğu için otomatik olarak CPU (libx264) moduna geçildi.");
                    }
                    else
                    {
                        logBuilder.AppendLine($"BİLGİ: Otomatik modda tespit edilen en iyi kodlayıcı ({activeEncoder}) ile devam ediliyor.");
                    }
                }

                string encoderDisplayName = "İşlemci (CPU - libx264)";
                if (activeEncoder == "h264_nvenc") encoderDisplayName = "NVIDIA Ekran Kartı (NVENC)";
                else if (activeEncoder == "h264_amf") encoderDisplayName = "AMD Ekran Kartı (AMF)";

                logBuilder.AppendLine($"Aktif Kullanılan Donanım/Kodlayıcı: {encoderDisplayName}");
                SafeInvoke(() => UpdateStatus($"İşleniyor: {qData.FileName} ({encoderDisplayName})..."));

                double originalSizeMb = 0;
                if (!qData.FilePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        FileInfo originalInfo = new FileInfo(qData.FilePath);
                        originalSizeMb = originalInfo.Length / (1024.0 * 1024.0);
                        logBuilder.AppendLine($"Orijinal Boyut (MB): {originalSizeMb:F2}");
                    }
                    catch (Exception ex)
                    {
                        originalSizeMb = 1000;
                        logBuilder.AppendLine($"Dosya Boyutu Okunurken Hata: {ex.Message}");
                    }
                }
                else
                {
                    originalSizeMb = 4000;
                }

                System.Collections.Generic.List<string> videoFilters = new System.Collections.Generic.List<string>();
                if (qData.FilePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
                {
                    videoFilters.Add("bwdif=mode=send_frame:parity=auto:deint=interlaced");
                }
                if (cleanRes.Contains("608"))
                {
                    videoFilters.Add("crop=720:576:0:0");
                }
                if (preset.Contains("60")) videoFilters.Add("fps=60");
                else if (preset.Contains("30")) videoFilters.Add("fps=30");

                string finalFilter = "";
                if (videoFilters.Count > 0)
                {
                    finalFilter = "-vf \"" + string.Join(",", videoFilters) + "\" ";
                }

                string tempOutputVideo = Path.Combine(outputSubFolder, "__converting__" + nameOnly + ".mp4");
                string finalOutputVideo = Path.Combine(outputSubFolder, nameOnly + ".mp4");
                if (File.Exists(finalOutputVideo))
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    finalOutputVideo = Path.Combine(outputSubFolder, $"{nameOnly}_{timestamp}.mp4");
                }

                int processExitCode = -1;
                bool successRender = false;

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    token.ThrowIfCancellationRequested();

                    string presetSpeed = "p6";
                    string qualityParam = "-cq 18 -rc constqp";
                    string crfValue = "18";

                    // Seçilen ön ayara göre CRF değerini 14, 18 veya 19 olarak net şekilde belirliyoruz:
                    if (preset.Contains("Max Kalite") || preset.Contains("14"))
                    {
                        crfValue = "14";
                    }
                    else if (preset.Contains("Dengeli") || preset.Contains("20"))
                    {
                        crfValue = "20";
                    }
                    else
                    {
                        crfValue = "18"; // Yüksek Kalite için 18
                    }

                    // GPU ve CPU için kararlı NVENC komut yapısı
                    if (activeEncoder == "h264_nvenc")
                    {
                        presetSpeed = (crfValue == "14") ? "p7" : "p6";
                        qualityParam = $"-cq {crfValue} -rc constqp";
                    }
                    else if (activeEncoder == "h264_amf")
                    {
                        presetSpeed = (crfValue == "14") ? "quality" : "balanced";
                        qualityParam = $"-rc cqp -qp_i {crfValue} -qp_p {crfValue}";
                    }
                    else
                    {
                        presetSpeed = "faster";
                        qualityParam = $"-crf {crfValue}";
                    }

                    string activeFilter = finalFilter;
                    string colorParams = "-color_primaries bt709 -color_trc bt709 -colorspace bt709 -color_range tv";
                    if (activeEncoder == "h264_nvenc")
                    {
                        colorParams = "-color_primaries bt470bg -color_trc bt470bg -colorspace bt470bg -color_range pc";
                    }

                    string resolutionParam = !string.IsNullOrEmpty(originalResolution) ? $"-s {originalResolution} " : "";
                    if (preset.Contains("1920x1080")) resolutionParam = "-s 1920x1080 ";
                    else if (preset.Contains("1280x720")) resolutionParam = "-s 1280x720 ";

                    // -to yerine -t (süre) parametresini kullanıyoruz:
                    string durationParam = !string.IsNullOrEmpty(qData.TrimEnd) ? $"-t {qData.TrimEnd} " : "";

                    string videoCmdArgs = "";
                    if (qData.FilePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        videoCmdArgs = $"-y -f concat -safe 0 -i \"{qData.FilePath}\" -c:v libx264 -crf 18 -preset faster {colorParams} -pix_fmt yuv420p -c:a aac -b:a 192k -ac 2 -movflags +faststart \"{tempOutputVideo}\"";
                    }
                    else
                    {
                        // Eklenen optimizasyon parametreleri kaldırılmış orijinal hızlı komut satırı:
                        videoCmdArgs = $"-y {ssParam}-i \"{qData.FilePath}\" {durationParam}{activeFilter}{resolutionParam}-c:v {activeEncoder} -preset {presetSpeed} {qualityParam} {colorParams} -pix_fmt yuv420p -c:a aac -b:a 128k -ac 2 -movflags +faststart \"{tempOutputVideo}\"";
                    }

                    // --- DEĞİŞİKLİK BİTTİ (Devamında logBuilder.AppendLine ile komut loglanıyor...)

                    logBuilder.AppendLine($"Aktif Kullanılan Encoder ({activeEncoder}) ile Deneniyor. Komut:\nffmpeg.exe {videoCmdArgs}");

                    DateTime startTime = DateTime.Now;
                    DateTime lastUiUpdate = DateTime.MinValue;

                    try
                    {
                        using (Process process = new Process())
                        {
                            currentProcess = process;
                            process.StartInfo = new ProcessStartInfo
                            {
                                FileName = ffmpegPath,
                                Arguments = videoCmdArgs,
                                UseShellExecute = false,
                                RedirectStandardError = true,
                                CreateNoWindow = true,
                                StandardErrorEncoding = Encoding.UTF8
                            };

                            process.ErrorDataReceived += (sender, e) =>
                            {
                                if (!string.IsNullOrEmpty(e.Data))
                                {
                                    logBuilder.AppendLine(e.Data);

                                    if (isPaused) return;

                                    Match fpsMatch = Regex.Match(e.Data, @"fps=\s*([\d\.]+)");
                                    if (fpsMatch.Success && double.TryParse(fpsMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedFps))
                                    {
                                        qData.Fps = parsedFps;
                                    }

                                    if (totalSeconds > 0)
                                    {
                                        Match timeMatch = Regex.Match(e.Data, @"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                                        if (timeMatch.Success)
                                        {
                                            double h = double.Parse(timeMatch.Groups[1].Value);
                                            double m = double.Parse(timeMatch.Groups[2].Value);
                                            double sVal = double.Parse(timeMatch.Groups[3].Value);
                                            double cs = double.Parse(timeMatch.Groups[4].Value);

                                            double currentSeconds = h * 3600 + m * 60 + sVal + cs / 100.0;
                                            int percent = (int)((currentSeconds / totalSeconds) * 100);
                                            if (percent > 100) percent = 100;
                                            if (percent < 0) percent = 0;

                                            qData.Percent = percent;

                                            if (percent > 2)
                                            {
                                                double elapsedSeconds = (DateTime.Now - startTime).TotalSeconds;
                                                double estimatedTotalSeconds = elapsedSeconds / (percent / 100.0);
                                                double remainingSeconds = estimatedTotalSeconds - elapsedSeconds;
                                                TimeSpan remainingTime = TimeSpan.FromSeconds(Math.Max(0, remainingSeconds));
                                                qData.TimeRemaining = $"{remainingTime.Hours:D2}:{remainingTime.Minutes:D2}:{remainingTime.Seconds:D2}";
                                            }

                                            if ((DateTime.Now - lastUiUpdate).TotalMilliseconds >= 500)
                                            {
                                                lastUiUpdate = DateTime.Now;
                                                SafeInvoke(() => {
                                                    int idx = queueList.IndexOf(qData);
                                                    if (idx != -1)
                                                    {
                                                        lstQueueBox.Invalidate(lstQueueBox.GetItemRectangle(idx));
                                                    }
                                                    if (lstQueueBox.SelectedItem == qData) DisplayItemDetails(qData);
                                                });

                                                WriteStatusForPython(qData.FileName, "İşleniyor", qData.Percent);
                                            }
                                        }
                                    }
                                }
                            };

                            process.Start();
                            process.BeginErrorReadLine();
                            process.WaitForExit();
                            processExitCode = process.ExitCode;
                        }
                    }
                    catch (Exception ex)
                    {
                        logBuilder.AppendLine($"Süreç Başlatılırken Kritik İstisna (Exception): {ex.Message}\n{ex.StackTrace}");
                    }

                    currentProcess = null;

                    if (processExitCode == 0 && File.Exists(tempOutputVideo))
                    {
                        successRender = true;
                        break;
                    }
                    else
                    {
                        if (activeEncoder != "libx264")
                        {
                            activeEncoder = "libx264";
                            try { if (File.Exists(tempOutputVideo)) File.Delete(tempOutputVideo); } catch { }
                            continue;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                logBuilder.AppendLine($"Sonuç Çıkış Kodu (ExitCode): {processExitCode}");
                SaveLogFile(outputSubFolder, nameOnly, logBuilder);

                if (qData.FilePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    try { if (File.Exists(qData.FilePath)) File.Delete(qData.FilePath); } catch { }
                }

                if (token.IsCancellationRequested)
                {
                    qData.Status = "İptal Edildi";
                    qData.EndTimeText = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                    try { if (File.Exists(tempOutputVideo)) File.Delete(tempOutputVideo); } catch { }
                    WriteStatusForPython(qData.FileName, "İptal Edildi", qData.Percent);

                    SafeInvoke(() => {
                        int idx = queueList.IndexOf(qData);
                        if (idx != -1) lstQueueBox.Invalidate(lstQueueBox.GetItemRectangle(idx));
                        if (lstQueueBox.SelectedItem == qData) DisplayItemDetails(qData);
                    });
                    continue;
                }

                if (successRender && File.Exists(tempOutputVideo))
                {
                    // DEĞİŞİKLİK: Sadece bu videoya özel seçilmişse (qData.ExtractAudioDuringConvert) ses ayıkla!
                    if (qData.ExtractAudioDuringConvert && qData.AudioStreamCount > 0 && !qData.FilePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        SafeInvoke(() =>
                        {
                            qData.Status = "Sesler Ayrıştırılıyor...";
                            qData.Percent = 0;
                            int idx = queueList.IndexOf(qData);
                            if (idx != -1) lstQueueBox.Invalidate(lstQueueBox.GetItemRectangle(idx));
                            DisplayItemDetails(qData);
                        });
                        Application.DoEvents();

                        try
                        {
                            string audioSubFolder = GetDynamicAudioPath(qData.FilePath);
                            if (!Directory.Exists(audioSubFolder)) Directory.CreateDirectory(audioSubFolder);

                            int totalChannels = qData.AudioStreamCount;
                            string channelInput = qData.TargetAudioChannels;
                            List<int> targetChannels = ParseChannelSelection(channelInput, totalChannels);

                            StringBuilder sbArgs = new StringBuilder();
                            sbArgs.Append($"-y -i \"{qData.FilePath}\" ");

                            foreach (int ch in targetChannels)
                            {
                                int ffmpegIndex = ch - 1; // FFmpeg 0 tabanlı indeks
                                string channelAudioFile = Path.Combine(audioSubFolder, $"{nameOnly}_Kanal_{ch}.wav");
                                sbArgs.Append($"-map 0:a:{ffmpegIndex} -vn -acodec pcm_s16le -ar 48000 \"{channelAudioFile}\" ");
                            }

                            ProcessStartInfo audioPsi = new ProcessStartInfo
                            {
                                FileName = ffmpegPath,
                                Arguments = sbArgs.ToString(),
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardError = true,
                                StandardErrorEncoding = Encoding.UTF8
                            };

                            using (Process audioProc = new Process())
                            {
                                audioProc.StartInfo = audioPsi;
                                audioProc.ErrorDataReceived += (s, errArgs) =>
                                {
                                    if (!string.IsNullOrEmpty(errArgs.Data))
                                    {
                                        Match timeMatch = Regex.Match(errArgs.Data, @"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                                        if (timeMatch.Success && totalSeconds > 0)
                                        {
                                            try
                                            {
                                                double h = double.Parse(timeMatch.Groups[1].Value);
                                                double m = double.Parse(timeMatch.Groups[2].Value);
                                                double sVal = double.Parse(timeMatch.Groups[3].Value);
                                                double cs = double.Parse(timeMatch.Groups[4].Value);

                                                double currentSeconds = h * 3600 + m * 60 + sVal + cs / 100.0;
                                                int percent = (int)((currentSeconds / totalSeconds) * 100);
                                                if (percent > 100) percent = 100;
                                                if (percent < 0) percent = 0;

                                                qData.Percent = percent;

                                                SafeInvoke(() =>
                                                {
                                                    int idx = queueList.IndexOf(qData);
                                                    if (idx != -1) lstQueueBox.Invalidate(lstQueueBox.GetItemRectangle(idx));
                                                    if (lstQueueBox.SelectedItem == qData) DisplayItemDetails(qData);
                                                    UpdateStatus($"Durum: {qData.FileName} sesleri ayrıştırılıyor... (%{percent})");
                                                });
                                            }
                                            catch { }
                                        }
                                    }
                                };

                                audioProc.Start();
                                audioProc.BeginErrorReadLine();
                                audioProc.WaitForExit();
                            }
                        }
                        catch { }
                    }

                    Thread.Sleep(1500);
                    try
                    {
                        FileInfo processedInfo = new FileInfo(tempOutputVideo);
                        double processedSizeMb = processedInfo.Length / (1024.0 * 1024.0);

                        if (processedSizeMb < originalSizeMb)
                        {
                            if (File.Exists(finalOutputVideo))
                            {
                                try { File.Delete(finalOutputVideo); } catch { Thread.Sleep(500); }
                            }

                            bool moved = false;
                            for (int retry = 0; retry < 5; retry++)
                            {
                                try
                                {
                                    File.Move(tempOutputVideo, finalOutputVideo);
                                    moved = true;
                                    break;
                                }
                                catch
                                {
                                    Thread.Sleep(1000);
                                }
                            }

                            if (!moved)
                            {
                                File.Copy(tempOutputVideo, finalOutputVideo, true);
                                try { File.Delete(tempOutputVideo); } catch { }
                            }

                            qData.Status = "Tamam";
                            qData.ResultSizeMb = processedSizeMb;
                            qData.SavedPercent = (originalSizeMb - processedSizeMb) / originalSizeMb * 100;
                            qData.Percent = 100;
                        }
                        else
                        {
                            try { File.Delete(tempOutputVideo); } catch { }
                            qData.Status = "Tamam";
                            qData.ResultSizeMb = originalSizeMb;
                            qData.SavedPercent = 0;
                            qData.Percent = 100;
                        }
                    }
                    catch (Exception ex)
                    {
                        qData.Status = "Hata";
                        logBuilder.AppendLine($"Kayıt Hatası: {ex.Message}");

                        bool isAlreadyInErrors = qData.FilePath.Contains("__ERRORS__");
                        if (!isAlreadyInErrors)
                        {
                            try
                            {
                                string errorsFolder = Path.Combine(outputSubFolder, "__ERRORS__");
                                if (!Directory.Exists(errorsFolder)) Directory.CreateDirectory(errorsFolder);

                                string destFile = Path.Combine(errorsFolder, Path.GetFileName(qData.FilePath));
                                if (File.Exists(qData.FilePath))
                                {
                                    File.Copy(qData.FilePath, destFile, true);
                                }
                            }
                            catch { }
                        }

                        SafeInvoke(() => MessageBox.Show($"Kayıt Hatası: {ex.Message}", "Kayıt Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error));
                    }

                    qData.EndTimeText = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                    WriteStatusForPython(qData.FileName, qData.Status, 100);

                    SafeInvoke(() => {
                        int idx = queueList.IndexOf(qData);
                        if (idx != -1) lstQueueBox.Invalidate(lstQueueBox.GetItemRectangle(idx));
                        if (lstQueueBox.SelectedItem == qData) DisplayItemDetails(qData);
                        UpdateStatus(qData.Status == "Tamam" ? $"Durum: {qData.FileName} başarıyla tamamlandı." : $"Durum: {qData.FileName} kaydedilemedi.");
                    });
                }
                else
                {
                    try { File.Delete(tempOutputVideo); } catch { }
                    qData.Status = "Hata";
                    qData.EndTimeText = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");

                    bool isAlreadyInErrors = qData.FilePath.Contains("__ERRORS__");
                    if (!isAlreadyInErrors)
                    {
                        try
                        {
                            string errorsFolder = Path.Combine(outputSubFolder, "__ERRORS__");
                            if (!Directory.Exists(errorsFolder)) Directory.CreateDirectory(errorsFolder);

                            string destFile = Path.Combine(errorsFolder, Path.GetFileName(qData.FilePath));
                            if (File.Exists(qData.FilePath))
                            {
                                File.Copy(qData.FilePath, destFile, true);
                            }
                        }
                        catch { }
                    }

                    WriteStatusForPython(qData.FileName, "Hata", qData.Percent);

                    SafeInvoke(() =>
                    {
                        int idx = queueList.IndexOf(qData);
                        if (idx != -1) lstQueueBox.Invalidate(lstQueueBox.GetItemRectangle(idx));
                        if (lstQueueBox.SelectedItem == qData) DisplayItemDetails(qData);
                        UpdateStatus($"Durum: {qData.FileName} işlenirken hata oluştu ve error klasörüne kopyalandı.");
                    });
                }
            }
        }

        private void SaveLogFile(string baseFolder, string fileNameOnly, StringBuilder logContent)
        {
            try
            {
                string logsFolder = Path.Combine(baseFolder, "__LOGS__");
                if (!Directory.Exists(logsFolder)) Directory.CreateDirectory(logsFolder);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string logFileName = $"{fileNameOnly}_{timestamp}.log";
                string logFilePath = Path.Combine(logsFolder, logFileName);

                File.WriteAllText(logFilePath, logContent.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private void ChkAutoMode_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAutoMode.Checked)
            {
                autoTimer.Start();
                UpdateStatus("Durum: Merzigo Otomatik Mod Aktif (Python'dan Görev Bekleniyor...)");
            }
            else
            {
                autoTimer.Stop();
                UpdateStatus("Durum: Hazır");
            }
        }

        private void AutoTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                string taskFile = Path.Combine(syncFolderPath, "convert_listesi.txt");
                if (File.Exists(taskFile))
                {
                    string[] lines = File.ReadAllLines(taskFile, System.Text.Encoding.UTF8);
                    if (lines.Length > 0)
                    {
                        bool hasNewTasks = false;
                        SafeInvoke(() => {
                            foreach (string line in lines)
                            {
                                string filePath = line.Trim().Replace("\"", "").Replace("'", "").Trim();
                                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                                {
                                    bool alreadyExists = queueList.Any(x => x.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                                    if (!alreadyExists)
                                    {
                                        var itemData = new QueueItemData
                                        {
                                            FilePath = filePath,
                                            FileName = Path.GetFileName(filePath),
                                            Status = "Bekliyor",
                                            Resolution = "Hesaplanıyor...",
                                            StartTimeText = "-",
                                            EndTimeText = "-",
                                            AudioStreamCount = 0
                                        };
                                        queueList.Add(itemData);
                                        hasNewTasks = true;
                                    }
                                }
                            }

                            if (hasNewTasks)
                            {
                                if (queueList.Count > 0 && lstQueueBox.SelectedIndex == -1)
                                    lstQueueBox.SelectedIndex = 0;
                                lstQueueBox.Refresh();
                            }
                        });

                        try { File.WriteAllText(taskFile, string.Empty); } catch { }

                        if (hasNewTasks && !isRunning)
                        {
                            SafeInvoke(() => { BtnStart_Click(null, null); });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("AutoTimer Hata: " + ex.Message);
            }
        }

        private async void FolderWatchTimer_Tick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(targetWatchFolderPath) || !Directory.Exists(targetWatchFolderPath) || this.IsDisposed)
                return;

            folderWatchTimer.Stop();

            try
            {
                await Task.Run(() =>
                {
                    string[] allowedExtensions = { ".ts", ".mov", ".mxf", ".mkv", ".mp4", ".avi", ".webm" };
                    var videoFiles = Directory.GetFiles(targetWatchFolderPath, "*.*", SearchOption.TopDirectoryOnly)
                                           .Where(file => allowedExtensions.Contains(Path.GetExtension(file).ToLower()) && !file.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
                                           .ToArray();

                    bool hasNewTasks = false;

                    foreach (var file in videoFiles)
                    {
                        if (!IsFileReady(file)) continue;

                        bool alreadyExists = false;

                        SafeInvoke(() => {
                            alreadyExists = queueList.Any(x => x.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase));
                        });

                        if (!alreadyExists)
                        {
                            var itemData = new QueueItemData
                            {
                                FilePath = file,
                                FileName = Path.GetFileName(file),
                                Status = "Bekliyor",
                                Percent = 0,
                                TimeRemaining = "00:00:00",
                                Fps = 0,
                                Resolution = "Hesaplanıyor...",
                                StartTimeText = "-",
                                EndTimeText = "-",
                                AudioStreamCount = 0
                            };

                            try
                            {
                                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                                if (File.Exists(ffmpegPath))
                                {
                                    itemData.AudioStreamCount = GetAudioStreamCount(ffmpegPath, file);
                                }
                            }
                            catch { }

                            SafeInvoke(() => {
                                queueList.Add(itemData);
                                hasNewTasks = true;
                            });
                        }
                    }

                    SafeInvoke(() => {
                        if (hasNewTasks)
                        {
                            if (queueList.Count > 0 && lstQueueBox.SelectedIndex == -1)
                                lstQueueBox.SelectedIndex = 0;

                            lstQueueBox.Refresh();
                            UpdateStatus("Durum: İzlenen klasörden yeni video kuyruğa eklendi.");
                        }
                    });

                    if (hasNewTasks && !isRunning)
                    {
                        SafeInvoke(() => {
                            BtnStart_Click(null, null);
                        });
                    }
                });
            }
            catch { }
            finally
            {
                if (!this.IsDisposed && !string.IsNullOrEmpty(targetWatchFolderPath))
                {
                    folderWatchTimer.Start();
                }
            }
        }

        private int GetAudioStreamCount(string ffmpegPath, string inputVideo)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-i \"{inputVideo}\"",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardError.ReadToEnd();
                    return Regex.Matches(output, @"Stream #\d+:\d+.*Audio").Count;
                }
            }
            catch { return 0; }
        }

        private (double duration, string resolution, string bitrate) GetVideoInfo(string ffmpegPath, string inputVideo)
        {
            double duration = 0;
            string resolution = "Bilinmiyor";
            string bitrate = "Bilinmiyor";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-i \"{inputVideo}\"",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardError.ReadToEnd();

                    Match matchDuration = Regex.Match(output, @"Duration: (\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                    if (matchDuration.Success)
                    {
                        double h = double.Parse(matchDuration.Groups[1].Value);
                        double m = double.Parse(matchDuration.Groups[2].Value);
                        double s = double.Parse(matchDuration.Groups[3].Value);
                        double cs = double.Parse(matchDuration.Groups[4].Value);
                        duration = h * 3600 + m * 60 + s + cs / 100.0;
                    }

                    Match matchRes = Regex.Match(output, @",\s*(\d{3,4})x(\d{3,4})");
                    if (matchRes.Success)
                    {
                        resolution = $"{matchRes.Groups[1].Value}x{matchRes.Groups[2].Value}";
                    }

                    Match matchBitrate = Regex.Match(output, @"bitrate:\s*([^\s]+)");
                    if (matchBitrate.Success)
                    {
                        bitrate = matchBitrate.Groups[1].Value;
                    }
                }
            }
            catch { }
            return (duration, resolution, bitrate);
        }

        private string DetectBestEncoder(string ffmpegPath)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-encoders",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();

                    if (output.Contains("h264_nvenc"))
                    {
                        return "h264_nvenc";
                    }
                    else if (output.Contains("h264_amf"))
                    {
                        return "h264_amf";
                    }
                }
            }
            catch { }

            return "libx264";
        }

        // 1. Sol listeden video seçildiğinde tetiklenen olay (WinForms Uyarlanmış Hali)
        private async void LstQueueBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstQueueBox.SelectedItem is QueueItemData selectedItem)
            {
                if (selectedItem.FilePath == tempDummyFile) return;

                // 1. LAZY LOADING (Ertelemeli Yükleme Kontrolü):
                // Eğer dosya bilgileri daha önceden taranmadıysa, sadece bu seçilen videoya tıklandığı an detayları çek
                if (selectedItem.Resolution == "Seçilmedi / Bekliyor" || selectedItem.Resolution == "Hesaplanıyor..." || string.IsNullOrEmpty(selectedItem.Bitrate) || selectedItem.Bitrate == "-")
                {
                    UpdateStatus("Durum: Seçilen videonun bilgileri okunuyor...");

                    string currentFilePath = selectedItem.FilePath;

                    // Arka planda sadece bu tek dosyanın bilgilerini alıyoruz
                    await Task.Run(() =>
                    {
                        string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                        if (File.Exists(ffmpegPath) && !currentFilePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                selectedItem.AudioStreamCount = GetAudioStreamCount(ffmpegPath, currentFilePath);
                                var (dur, res, br) = GetVideoInfo(ffmpegPath, currentFilePath);

                                if (dur > 0) selectedItem.TotalSeconds = dur;
                                if (!string.IsNullOrEmpty(res) && res != "Bilinmiyor") selectedItem.Resolution = res;
                                if (!string.IsNullOrEmpty(br) && br != "Bilinmiyor") selectedItem.Bitrate = br;
                            }
                            catch { }
                        }
                    });
                }

                // 2. SENİN MEVCUT ARAYÜZ ATAMALARIN
                txtTrimStart.Text = selectedItem.TrimStart ?? "";
                txtTrimEnd.Text = selectedItem.TrimEnd ?? "";

                Control[] audioTxts = tabDashboard.Controls.Find("txtAudioChannels", true);
                if (audioTxts.Length > 0 && audioTxts[0] is TextBox tb)
                {
                    tb.Text = selectedItem.TargetAudioChannels ?? "";
                }

                if (chkSplitAudio != null)
                {
                    chkSplitAudio.Checked = selectedItem.ExtractAudioDuringConvert;
                }

                DisplayItemDetails(selectedItem);

                // 3. ÖNİZLEME KARELERİ
                if (File.Exists(selectedItem.FilePath) && !selectedItem.FilePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    ClearPreviousThumbnails();
                    try
                    {
                        UpdateStatus("Durum: Video için önizleme karesi üretiliyor...");
                        await GenerateTenThumbnailsAsync(selectedItem.FilePath);
                        DisplayThumbnailsOnUI();
                        UpdateStatus("Durum: Önizleme kareleri başarıyla yüklendi.");
                    }
                    catch { }
                }
            }
            else
            {
                txtTrimStart.Text = "";
                txtTrimEnd.Text = "";
                Control[] audioTxts = tabDashboard.Controls.Find("txtAudioChannels", true);
                if (audioTxts.Length > 0 && audioTxts[0] is TextBox tb) tb.Text = "";
                if (lblDashboardSizeInfo != null) lblDashboardSizeInfo.Text = "Dönüştürme Bilgisi: Listeden bir video seçin...";
            }
        }

        // 2. Arka planda FFmpeg ile 10 kareyi oluşturan metot
        private async Task GenerateTenThumbnailsAsync(string videoPath)
        {
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath)) return;

            // Süreyi al
            var videoInfo = GetVideoInfo(ffmpegPath, videoPath);
            double durationSeconds = videoInfo.duration;
            if (durationSeconds <= 0) return;

            string tempFolder = Path.Combine(Path.GetTempPath(), "VideoOptimizer_Thumbs");
            Directory.CreateDirectory(tempFolder);

            double interval = durationSeconds / 10.0;
            List<Task> thumbnailTasks = new List<Task>();

            for (int i = 0; i < 10; i++)
            {
                double timestamp = interval * i;
                string outputFilePath = Path.Combine(tempFolder, $"thumb_{i + 1}.jpg");

                // Çözünürlüğü orijinal oranda tutarak genişliği 1080p'ye çıkarıyor ve JPEG sıkıştırma kalitesini en yüksek değere (-q:v 2) ayarlıyor
                string ffmpegArgs = $"-y -ss {TimeSpan.FromSeconds(timestamp):c} -i \"{videoPath}\" -vframes 1 -vf scale=1920:-1 -q:v 2 \"{outputFilePath}\"";
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = ffmpegArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                thumbnailTasks.Add(Task.Run(() =>
                {
                    try
                    {
                        using (Process p = Process.Start(psi))
                        {
                            p.WaitForExit();
                        }
                    }
                    catch { }
                }));
            }

            await Task.WhenAll(thumbnailTasks);
        }

        // 3. Üretilen resimleri önizleme sayfasına (FlowLayoutPanel içine) basan metot
        // Sınıfın üst kısımlarına global olarak ekleyebilirsin (Mevcut listeye):
        private List<string> currentThumbnailPaths = new List<string>();
        private int currentThumbnailIndex = 0;

        // 3. Üretilen resimleri önizleme sayfasına büyük tekli gösterici ve ok tuşları ile basan metot
        private void DisplayThumbnailsOnUI()
        {
            SafeInvoke(() =>
            {
                // Önizleme alanındaki eski container veya bileşenleri temizle
                Control oldContainer = tabPreview.Controls["panelSingleThumbContainer"];
                if (oldContainer != null) tabPreview.Controls.Remove(oldContainer);

                string tempFolder = Path.Combine(Path.GetTempPath(), "VideoOptimizer_Thumbs");
                currentThumbnailPaths.Clear();

                for (int i = 1; i <= 10; i++)
                {
                    string imagePath = Path.Combine(tempFolder, $"thumb_{i}.jpg");
                    if (File.Exists(imagePath))
                    {
                        currentThumbnailPaths.Add(imagePath);
                    }
                }

                if (currentThumbnailPaths.Count == 0) return;
                currentThumbnailIndex = 0;

                // Ana Konteyner Panel
                Panel panelContainer = new Panel()
                {
                    Name = "panelSingleThumbContainer",
                    Location = new Point(20, 210),
                    Size = new Size(735, 390),
                    BackColor = Color.FromArgb(40, 40, 45)
                };
                tabPreview.Controls.Add(panelContainer);

                // Büyük PictureBox
                PictureBox picLarge = new PictureBox()
                {
                    Name = "picLargeThumbnail",
                    Location = new Point(15, 15),
                    Size = new Size(705, 305),
                    SizeMode = PictureBoxSizeMode.CenterImage, // Zoom yerine CenterImage yaparak resmi orijinal boyutunda ve kalitesinde ortalar
                    BackColor = Color.Black,
                    Cursor = Cursors.Hand
                };
                panelContainer.Controls.Add(picLarge);

                // Tıklayınca orijinal boyutta açma
                picLarge.Click += (s, e) =>
                {
                    if (currentThumbnailIndex >= 0 && currentThumbnailIndex < currentThumbnailPaths.Count)
                    {
                        try { Process.Start(new ProcessStartInfo(currentThumbnailPaths[currentThumbnailIndex]) { UseShellExecute = true }); } catch { }
                    }
                };

                // Önceki Butonu (◀)
                Button btnPrev = CreateModernActionButton("◀ Önceki Kare", 15, 335, 130, 35, Color.FromArgb(60, 60, 65), Color.White);
                btnPrev.Click += (s, e) =>
                {
                    if (currentThumbnailPaths.Count > 0)
                    {
                        currentThumbnailIndex = (currentThumbnailIndex - 1 + currentThumbnailPaths.Count) % currentThumbnailPaths.Count;
                        UpdateLargeThumbnailView(panelContainer);
                    }
                };
                panelContainer.Controls.Add(btnPrev);

                // Sayaç Etiketi (Örn: Kare 1 / 10)
                Label lblCounter = new Label()
                {
                    Name = "lblThumbCounter",
                    Location = new Point(155, 342),
                    Size = new Size(425, 25),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 10, FontStyle.Bold),
                    ForeColor = Color.White
                };
                panelContainer.Controls.Add(lblCounter);

                // Sonraki Butonu (▶)
                Button btnNext = CreateModernActionButton("Sonraki Kare ▶", 590, 335, 130, 35, Color.FromArgb(60, 60, 65), Color.White);
                btnNext.Click += (s, e) =>
                {
                    if (currentThumbnailPaths.Count > 0)
                    {
                        currentThumbnailIndex = (currentThumbnailIndex + 1) % currentThumbnailPaths.Count;
                        UpdateLargeThumbnailView(panelContainer);
                    }
                };
                panelContainer.Controls.Add(btnNext);

                // İlk görseli yükle
                UpdateLargeThumbnailView(panelContainer);
            });
        }

        // Görseli ve sayaç yazısını güncelleyen yardımcı metot
        private void UpdateLargeThumbnailView(Panel container)
        {
            if (currentThumbnailPaths.Count == 0) return;

            PictureBox picLarge = container.Controls["picLargeThumbnail"] as PictureBox;
            Label lblCounter = container.Controls["lblThumbCounter"] as Label;

            string imagePath = currentThumbnailPaths[currentThumbnailIndex];

            if (picLarge != null && File.Exists(imagePath))
            {
                if (picLarge.Image != null)
                {
                    picLarge.Image.Dispose();
                    picLarge.Image = null;
                }

                using (Image original = Image.FromFile(imagePath))
                {
                    // Orijinal kopyayı oluşturuyoruz
                    picLarge.Image = new Bitmap(original);

                    // Resmin oranını bozmadan panel içine ortalamak ve boyutlandırmak için:
                    picLarge.SizeMode = PictureBoxSizeMode.Zoom;
                }
            }

            if (lblCounter != null)
            {
                lblCounter.Text = $"Kare: {currentThumbnailIndex + 1} / {currentThumbnailPaths.Count}";
            }
        }

        // 4. Eski önizlemeleri temizleme metodu
        private void ClearPreviousThumbnails()
        {
            SafeInvoke(() =>
            {
                FlowLayoutPanel panelThumbs = tabPreview.Controls["panelThumbsContainer"] as FlowLayoutPanel;
                if (panelThumbs != null)
                {
                    foreach (Control ctrl in panelThumbs.Controls)
                    {
                        ctrl.Dispose();
                    }
                    panelThumbs.Controls.Clear();
                }
            });
        }




    }

    public class QueueItemData
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string Status { get; set; }
        public int Percent { get; set; }
        public string TimeRemaining { get; set; }
        public double Fps { get; set; }
        public double ResultSizeMb { get; set; }
        public string Resolution { get; set; }
        public double SavedPercent { get; set; }

        public string StartTimeText { get; set; }
        public string EndTimeText { get; set; }
        public int AudioStreamCount { get; set; }

        public double TotalSeconds { get; set; }
        public string Bitrate { get; set; } = "Hesaplanıyor...";

        public string TrimStart { get; set; } = "";
        public string TrimEnd { get; set; } = "";

        // Her videonun kendine özel ses kanal seçim metni (Örn: "1,3,5" veya "2-4")
        public string TargetAudioChannels { get; set; } = "";
        public string ActiveCrf { get; set; } = "18";
        public bool ExtractAudioDuringConvert { get; set; } = false;



    }
}