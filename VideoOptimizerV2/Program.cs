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

    // Modern, Pürüzsüz ve Altı Vurgulu Sekme Sınıfı (Özel Çizim)
    public class ModernTabControl : TabControl
    {
        public ModernTabControl()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.FromArgb(37, 37, 40)); // Koyu tema arka planı

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
                    using (SolidBrush accentBrush = new SolidBrush(Color.FromArgb(0, 120, 212))) // Modern Mavi Vurgu
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

        // Modern UI Bileşenleri
        private ModernTabControl mainTabControl; // Özel pürüzsüz sekme kontrolü
        private TabPage tabDashboard;
        private TabPage tabDetailsSettings;
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

        private BindingList<QueueItemData> queueList = new BindingList<QueueItemData>();

        private bool isDarkMode = true;
        private Color darkBg = Color.FromArgb(30, 30, 30);
        private Color darkPanelBg = Color.FromArgb(45, 45, 45);
        private Color darkText = Color.White;

        private Color lightBg = Color.FromArgb(240, 240, 240);
        private Color lightPanelBg = Color.FromArgb(245, 245, 247);
        private Color lightText = Color.Black;

        public MainForm(string[] args = null)
        {
            syncFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Merzigo_Sync");
            if (!Directory.Exists(syncFolderPath))
            {
                Directory.CreateDirectory(syncFolderPath);
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

            // Sol Sabit Kuyruk Paneli
            pnlLeftQueue = new Panel()
            {
                Location = new Point(12, 12),
                Size = new Size(330, 630),
                BackColor = Color.FromArgb(45, 45, 48),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
            };
            this.Controls.Add(pnlLeftQueue);

            contextMenuQueue = new ContextMenuStrip();
            menuItemDeleteOriginal = new ToolStripMenuItem("🗑 Bu Videonun Orijinal Ham Dosyasını Sil");
            menuItemDeleteOriginal.Click += MenuItemDeleteOriginal_Click;
            contextMenuQueue.Items.Add(menuItemDeleteOriginal);

            // Kuyruk Listesi
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
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            lstQueueBox.DrawItem += LstQueueBox_DrawItem;
            lstQueueBox.SelectedIndexChanged += LstQueueBox_SelectedIndexChanged;
            pnlLeftQueue.Controls.Add(lstQueueBox);

            // Sağ Taraf İçin Modern Pürüzsüz Sekmeli Yapı
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

            mainTabControl.TabPages.Add(tabDashboard);
            mainTabControl.TabPages.Add(tabDetailsSettings);
            this.Controls.Add(mainTabControl);

            // --- TAB 1: ANA SAYFA ---
            lblDropZone = new Label()
            {
                Text = "📁 DOSYALARI SÜRÜKLE & BIRAK\nveya aşağıdaki şeffaf butonlar ile seçin",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Location = new Point(15, 15),
                Size = new Size(150, 260),
                BackColor = Color.FromArgb(50, 50, 55),
                ForeColor = Color.FromArgb(200, 200, 200),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tabDashboard.Controls.Add(lblDropZone);

            btnSelectFolder = CreateTransparentButton("📁 Klasör Seç", 15, 290, 120, 38, Color.White);
            btnSelectFolder.Click += BtnSelectFolder_Click;
            tabDashboard.Controls.Add(btnSelectFolder);

            btnSelectFiles = CreateTransparentButton("🎬 Video Seç", 145, 290, 120, 38, Color.White);
            btnSelectFiles.Click += BtnSelectFiles_Click;
            tabDashboard.Controls.Add(btnSelectFiles);

            btnLoadList = CreateTransparentButton("📄 Liste Yükle", 545, 290, 125, 38, Color.White);
            btnLoadList.Click += BtnLoadList_Click;
            tabDashboard.Controls.Add(btnLoadList);

            btnSelectTargetFolder = CreateTransparentButton("🎯 Hedef Klasör", 275, 290, 120, 38, Color.White);
            btnSelectTargetFolder.Click += BtnSelectTargetFolder_Click;
            tabDashboard.Controls.Add(btnSelectTargetFolder);

            btnWatchFolder = CreateTransparentButton("🔍 Klasör İzle", 405, 290, 130, 38, Color.White);
            btnWatchFolder.Click += BtnWatchFolder_Click;
            tabDashboard.Controls.Add(btnWatchFolder);

            cmbPresets = new ComboBox() { Location = new Point(15, 350), Size = new Size(240, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbPresets.Items.AddRange(new string[] {
                "Yüksek Kalite (HQ - RF/CQ 18)",
                "Maksimum Kalite (Ultra - RF/CQ 14)",
                "Fast 60 FPS",
                "Fast 30 FPS"
            });
            cmbPresets.SelectedIndex = 0;
            tabDashboard.Controls.Add(cmbPresets);

            chkSplitAudio = new CheckBox() { Text = "Sesleri .WAV Olarak Ayır", Location = new Point(270, 348), Size = new Size(170, 25), Checked = false, Font = new Font("Segoe UI", 9) };
            chkSplitAudio.CheckedChanged += (s, e) => { chkSplitAudio.ForeColor = isDarkMode ? darkText : lightText; };
            tabDashboard.Controls.Add(chkSplitAudio);

            chkAutoMode = new CheckBox() { Text = "Merzigo Oto", Location = new Point(460, 348), Size = new Size(110, 25), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            chkAutoMode.CheckedChanged += ChkAutoMode_CheckedChanged;
            tabDashboard.Controls.Add(chkAutoMode);

            // Aksiyon Butonları
            btnStart = CreateModernActionButton("🚀 BAŞLAT", 15, 410, 150, 45, Color.FromArgb(16, 124, 65), Color.White);
            btnStart.Click += BtnStart_Click;
            tabDashboard.Controls.Add(btnStart);

            btnPauseResume = CreateModernActionButton("Duraklat", 175, 410, 120, 45, Color.FromArgb(0, 120, 212), Color.White);
            btnPauseResume.Enabled = false;
            btnPauseResume.Click += BtnPauseResume_Click;
            tabDashboard.Controls.Add(btnPauseResume);

            btnStop = CreateModernActionButton("İptal Et", 305, 410, 100, 45, Color.FromArgb(232, 17, 35), Color.White);
            btnStop.Enabled = false;
            btnStop.Click += BtnStop_Click;
            tabDashboard.Controls.Add(btnStop);

            btnClearQueue = CreateModernActionButton("🧹 Temizle", 415, 410, 100, 45, Color.FromArgb(55, 55, 60), Color.FromArgb(240, 240, 240));
            btnClearQueue.Click += BtnClearQueue_Click;
            tabDashboard.Controls.Add(btnClearQueue);

            btnToggleTheme = CreateModernActionButton("🌓 Tema", 525, 410, 110, 45, Color.FromArgb(55, 55, 60), Color.FromArgb(240, 240, 240));
            btnToggleTheme.Click += (s, e) => {
                isDarkMode = !isDarkMode;
                ApplyTheme();
            };
            tabDashboard.Controls.Add(btnToggleTheme);

            btnCleanOriginals = CreateModernActionButton("🗑 Hamları Sil", 645, 410, 110, 45, Color.FromArgb(85, 50, 50), Color.White);
            btnCleanOriginals.Click += BtnCleanOriginals_Click;
            tabDashboard.Controls.Add(btnCleanOriginals);


            // --- TAB 2: ÖZET SAYFASI ---
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

            // Kaynak Alanı
            lblDetailSource = new Label() { Text = "Kaynak: -", Location = new Point(15, 72), Size = new Size(610, 45), Font = new Font("Segoe UI", 9) };
            pnlDetails.Controls.Add(lblDetailSource);

            // Minimalist Şık Kaynak Kopyala Butonu
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
                            // Dosyanın bulunduğu klasörü aç ve dosyayı seçili yap
                            Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                            UpdateStatus("Durum: Kaynak videonun klasörü açıldı.");
                        }
                        else if (Directory.Exists(fullPath))
                        {
                            Process.Start("explorer.exe", $"\"{fullPath}\"");
                        }
                        else
                        {
                            // Dosya yerinde yoksa sadece yolu panoya kopyala yedek plan olarak
                            Clipboard.SetText(fullPath);
                            UpdateStatus("Durum: Dosya bulunamadı, yol panoya kopyalandı.");
                        }
                    }
                    catch { }
                }
            };
            pnlDetails.Controls.Add(btnCopySource);

            // Hedef Alanı
            lblDetailTarget = new Label() { Text = "Hedef: -", Location = new Point(15, 127), Size = new Size(610, 45), Font = new Font("Segoe UI", 9) };
            pnlDetails.Controls.Add(lblDetailTarget);

            // Minimalist Şık Hedef Kopyala Butonu
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
                            // Hedef dosyanın bulunduğu klasörü aç ve dosyayı seçili yap
                            Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                            UpdateStatus("Durum: Hedef videonun klasörü açıldı.");
                        }
                        else
                        {
                            // Eğer dosya henüz oluşmadıysa veya klasörse direkt klasörü aç
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

            // Orijinal boyut ve zaman detay alanı (Eski şık Label yapısı)
            lblDetailSizeInfo = new Label() { Text = "Boyut Analizi & Zaman: İşlem bekleniyor...", Location = new Point(15, 212), Size = new Size(700, 290), Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.DarkBlue };
            pnlDetails.Controls.Add(lblDetailSizeInfo);

            tabDetailsSettings.Controls.Add(pnlDetails);

            // Alt Durum Çubuğu
            lblStatus = new Label()
            {
                Text = $"Durum: Hazır. Akıllı Hedef Eşleme Aktif. Entegrasyon Klasörü: {syncFolderPath}",
                Location = new Point(15, 655),
                Size = new Size(1115, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            this.Controls.Add(lblStatus);
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
                    // Rengi sabit tut
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

                DialogResult result = MessageBox.Show($"'{selectedItem.FileName}' adlı orijinal ham video diskten kalıcı olarak silinecek. Devam edilsin mi?",
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
                        try { File.Delete(item.FilePath); deletedCount++; } catch { }
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
                    if (ctrl is Label && ctrl != lblDropZone)
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

            foreach (string file in files)
            {
                if (File.Exists(file))
                {
                    string ext = Path.GetExtension(file).ToLower();
                    if (allowedExtensions.Contains(ext))
                    {
                        AddVideoToQueue(file);
                    }
                }
                else if (Directory.Exists(file))
                {
                    string[] videoFiles = Directory.GetFiles(file, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => allowedExtensions.Contains(Path.GetExtension(f).ToLower()))
                        .ToArray();

                    foreach (var vf in videoFiles)
                    {
                        AddVideoToQueue(vf);
                    }
                }
            }

            if (queueList.Count > 0 && lstQueueBox.SelectedIndex == -1)
                lstQueueBox.SelectedIndex = 0;

            lstQueueBox.Refresh();
            Application.DoEvents();

            UpdateStatus("Durum: Sürükle-bırak ile dosyalar kuyruğa eklendi.");
        }

        private void BtnClearQueue_Click(object sender, EventArgs e)
        {
            isRunning = false;
            isPaused = false;
            currentProcess = null;
            try { cts?.Cancel(); } catch { }

            queueList.Clear();

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

            lstQueueBox.Refresh();
            UpdateStatus("Durum: Kuyruk tamamen temizlendi ve sıfırlandı. Yeni video bırakabilirsiniz.");
        }

        private void BtnSelectFolder_Click(object sender, EventArgs e)
        {
            nasFolderPath = string.Empty;

            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "İçindeki Videoların Taranacağı Ana Klasörü Seçin";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    string selectedFolderPath = fbd.SelectedPath;
                    nasFolderPath = selectedFolderPath;

                    string[] allowedExtensions = { ".ts", ".mov", ".mxf", ".mkv", ".mp4", ".avi", ".webm" };

                    try
                    {
                        var videoFiles = Directory.GetFiles(selectedFolderPath, "*.*", SearchOption.TopDirectoryOnly)
                                                .Where(file => allowedExtensions.Contains(Path.GetExtension(file).ToLower()))
                                                .ToArray();

                        if (videoFiles.Length > 0)
                        {
                            isRunning = false;
                            currentProcess = null;
                            queueList.Clear();

                            foreach (var file in videoFiles)
                            {
                                AddVideoToQueue(file);
                            }

                            if (queueList.Count > 0) lstQueueBox.SelectedIndex = 0;

                            lstQueueBox.Refresh();
                            Application.DoEvents();

                            UpdateStatus($"Durum: Seçilen klasörden {videoFiles.Length} adet video kuyruğa eklendi.");
                        }
                        else
                        {
                            MessageBox.Show("Seçilen klasörde desteklenen video dosyası bulunamadı!", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
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

                    foreach (var file in ofd.FileNames) AddVideoToQueue(file);

                    if (queueList.Count > 0) lstQueueBox.SelectedIndex = 0;

                    lstQueueBox.Refresh();
                    Application.DoEvents();

                    UpdateStatus($"Durum: {queueList.Count} adet video kuyruğa eklendi.");
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
                    customTargetFolder = fbd.SelectedPath;
                    UpdateStatus($"Durum: Ana hedef kök klasör ayarlandı -> {customTargetFolder}");
                    MessageBox.Show($"Ana hedef klasör başarıyla seçildi:\n{customTargetFolder}\n\nVideolar isimlerine göre ilgili Dizi ve Sezon alt klasörlerine doğrudan yönlendirilecektir.", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    if (lstQueueBox.SelectedItem is QueueItemData data)
                    {
                        DisplayItemDetails(data);
                    }
                }
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
            else if (itemData.Status.StartsWith("İşleniyor") || itemData.Status.StartsWith("Ses")) { statusIcon = "🔄"; iconBrush = Brushes.DarkOrange; }
            else if (itemData.Status == "Duraklatıldı") { statusIcon = "⏸"; iconBrush = Brushes.Goldenrod; }
            else if (itemData.Status == "İptal Edildi" || itemData.Status == "Hata") { statusIcon = "❌"; iconBrush = Brushes.Red; }

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
            else if (itemData.Status == "İptal Edildi" || itemData.Status == "Hata")
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
                            itemData.AudioStreamCount = GetAudioStreamCount(ffmpegPath, filePath);
                        }
                    }
                    catch { }

                    queueList.Add(itemData);
                }
            }
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

        private void LstQueueBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstQueueBox.SelectedItem is QueueItemData data)
            {
                DisplayItemDetails(data);
            }
        }

        private void DisplayItemDetails(QueueItemData data)
        {
            if (data != null && File.Exists(data.FilePath))
            {
                FileInfo fi = new FileInfo(data.FilePath);
                double sizeMb = fi.Length / (1024.0 * 1024.0);

                string outputSubFolder = GetDynamicTargetPath(data.FilePath);
                string targetFile = Path.Combine(outputSubFolder, Path.GetFileNameWithoutExtension(data.FilePath) + ".mp4");

                string selectedPresetText = cmbPresets.SelectedItem?.ToString() ?? "";
                string statusDescription = "Bekliyor";

                string audioSummary = data.AudioStreamCount > 0 ? $"{data.AudioStreamCount} Adet Ses Kanalı Bulundu" : "Ses kanalı yok veya taranmadı";
                if (chkSplitAudio.Checked && data.AudioStreamCount > 0)
                {
                    audioSummary += " (.WAV Olarak Ayrıştırılacak)";
                }

                string detailsText = $"Boyut Analizi:\nOrijinal Çözünürlük: {data.Resolution} (Orijinal Korunuyor)\nOrijinal Boyut: {sizeMb:F2} MB\n\nSes Bilgisi:\n• {audioSummary}\n\nZaman Bilgileri:\nBaşlangıç: {data.StartTimeText}\nBitiş: {data.EndTimeText}";

                if (data.Status == "Tamam")
                {
                    statusDescription = "Başarıyla Tamamlandı";
                    double savedMb = sizeMb - data.ResultSizeMb;
                    string wavOutputInfo = (chkSplitAudio.Checked && data.AudioStreamCount > 0) ? $"\n• Ses: {data.AudioStreamCount} kanal .wav olarak kaydedildi." : "";
                    detailsText = $"Boyut Analizi:\nOrijinal Çözünürlük: {data.Resolution} (Orijinal Korundu)\nOrijinal: {sizeMb:F2} MB | Yeni: {data.ResultSizeMb:F2} MB\nKazanç: %{data.SavedPercent:F1} ({savedMb:F2} MB azaldı)\n\nÇıktılar:\n• Video: {Path.GetFileName(targetFile)}{wavOutputInfo}\n\nZaman Bilgileri:\nBaşlangıç: {data.StartTimeText}\nBitiş: {data.EndTimeText}";
                }
                else if (data.Status.StartsWith("İşleniyor") || data.Status.StartsWith("Ses") || data.Status == "Duraklatıldı")
                {
                    statusDescription = $"{data.Status} - %{data.Percent}";
                    detailsText = $"Boyut Analizi:\nOrijinal Çözünürlük: {data.Resolution}\nOrijinal Boyut: {sizeMb:F2} MB\nDurum: {data.Status} (%{data.Percent})\n\nSes Bilgisi:\n• {audioSummary}\n\nZaman Bilgileri:\nBaşlangıç: {data.StartTimeText}\nBitiş: {data.EndTimeText}";
                }
                else if (data.Status == "İptal Edildi" || data.Status == "Hata")
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

            UpdateStatus("Durum: Kuyruk akıllı hedef eşleme ile işleniyor (Yüksek Kalite Optimizasyonu + Web FastStart)...");

            try
            {
                string selectedPreset = "Yüksek Kalite (HQ - RF/CQ 18)";
                SafeInvoke(() => {
                    if (cmbPresets.SelectedItem != null) selectedPreset = cmbPresets.SelectedItem.ToString();
                });

                await Task.Run(() => ProcessQueueItems(selectedPreset, cts.Token));
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Durum: İşlem kullanıcı tarafından iptal edildi.");
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
            if (lstQueueBox.SelectedItem is QueueItemData selectedItem)
            {
                if (selectedItem.Status == "Bekliyor" || selectedItem.Status == "Duraklatıldı")
                {
                    selectedItem.Status = "İptal Edildi";
                    selectedItem.EndTimeText = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                    lstQueueBox.Invalidate();
                    UpdateStatus($"Durum: {selectedItem.FileName} kuyruktan iptal edildi.");
                    DisplayItemDetails(selectedItem);
                    return;
                }
            }

            if (isRunning || folderWatchTimer.Enabled)
            {
                if (isPaused && currentProcess != null)
                {
                    try
                    {
                        foreach (ProcessThread thread in currentProcess.Threads)
                        {
                            var pOpenThread = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)thread.Id);
                            if (pOpenThread != IntPtr.Zero) { ResumeThread(pOpenThread); CloseHandle(pOpenThread); }
                        }
                    }
                    catch { }
                }

                cts?.Cancel();
                try { currentProcess?.Kill(); } catch { }
                isPaused = false;
                isRunning = false;

                var activeItem = queueList.FirstOrDefault(x => x.Status.StartsWith("İşleniyor") || x.Status.StartsWith("Ses"));
                if (activeItem != null)
                {
                    activeItem.Status = "İptal Edildi";
                    activeItem.EndTimeText = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                }
                else if (lstQueueBox.SelectedItem is QueueItemData currentItem)
                {
                    currentItem.Status = "İptal Edildi";
                    currentItem.EndTimeText = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                }

                lstQueueBox.Invalidate();
                UpdateStatus("Durum: İşlem iptal edildi.");
            }
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

        private void ProcessQueueItems(string preset, CancellationToken token)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string ffmpegPath = Path.Combine(baseDirectory, "ffmpeg.exe");

            if (!File.Exists(ffmpegPath))
            {
                SafeInvoke(() => MessageBox.Show("ffmpeg.exe bulunamadı! Lütfen program klasörüne atın.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error));
                return;
            }

            string videoEncoder = DetectBestEncoder(ffmpegPath);

            while (true)
            {
                token.ThrowIfCancellationRequested();

                QueueItemData qData = null;
                SafeInvoke(() => {
                    qData = queueList.FirstOrDefault(x => x.Status == "Bekliyor");
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

                string nameOnly = Path.GetFileNameWithoutExtension(qData.FilePath);
                string outputSubFolder = GetDynamicTargetPath(qData.FilePath);

                StringBuilder logBuilder = new StringBuilder();
                logBuilder.AppendLine($"=== VİDEO OPTİMİZER LOG KAYDI ===");
                logBuilder.AppendLine($"Başlangıç Zamanı: {DateTime.Now}");
                logBuilder.AppendLine($"Dosya Yolu: {qData.FilePath}");
                logBuilder.AppendLine($"Ön Ayar (Preset): {preset}");
                logBuilder.AppendLine($"Kullanılan Encoder: {videoEncoder}");
                logBuilder.AppendLine("--------------------------------------------------");

                SaveLogFile(outputSubFolder, nameOnly + "_baslangic", logBuilder);

                int audioStreamCount = 0;
                try
                {
                    audioStreamCount = GetAudioStreamCount(ffmpegPath, qData.FilePath);
                    logBuilder.AppendLine($"Tespit Edilen Ses Kanal Sayısı: {audioStreamCount}");
                }
                catch (Exception ex)
                {
                    logBuilder.AppendLine($"Ses Kanalı Taranırken Hata: {ex.Message}");
                }
                qData.AudioStreamCount = audioStreamCount;

                SafeInvoke(() => {
                    qData.Status = "İşleniyor";
                    qData.Percent = 0;
                    qData.StartTimeText = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                    DisplayItemDetails(qData);
                });

                WriteStatusForPython(qData.FileName, "İşleniyor", 0);

                var (totalSeconds, originalResolution) = GetVideoInfo(ffmpegPath, qData.FilePath);
                qData.Resolution = originalResolution;
                logBuilder.AppendLine($"Video Süresi (Saniye): {totalSeconds}, Çözünürlük: {originalResolution}");

                double originalSizeMb = 0;
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

                System.Collections.Generic.List<string> videoFilters = new System.Collections.Generic.List<string>();

                if (qData.FilePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
                {
                    videoFilters.Add("bwdif=mode=send_frame:parity=auto:deint=interlaced");
                }

                if (preset.Contains("60")) videoFilters.Add("fps=60");
                else if (preset.Contains("30")) videoFilters.Add("fps=30");

                string finalFilter = "";
                if (videoFilters.Count > 0)
                {
                    finalFilter = "-vf \"" + string.Join(",", videoFilters) + "\" ";
                }

                string presetSpeed = "p6";
                string qualityParam = "-cq 18 -rc constqp";

                if (videoEncoder == "h264_nvenc")
                {
                    presetSpeed = "p6";
                    qualityParam = "-cq 18 -rc constqp";
                }
                else if (videoEncoder == "h264_amf")
                {
                    presetSpeed = "balanced";
                    qualityParam = "-rc cqp -qp_i 18 -qp_p 18";
                }
                else
                {
                    presetSpeed = "medium";
                    qualityParam = "-crf 18";
                }

                if (preset.Contains("Maksimum Kalite"))
                {
                    if (videoEncoder == "h264_nvenc") { presetSpeed = "p7"; qualityParam = "-cq 14 -rc constqp"; }
                    else if (videoEncoder == "h264_amf") { presetSpeed = "quality"; qualityParam = "-rc cqp -qp_i 14 -qp_p 14"; }
                    else { presetSpeed = "slow"; qualityParam = "-crf 14"; }
                }
                else if (preset.Contains("60 FPS") || preset.Contains("30 FPS"))
                {
                    if (videoEncoder == "h264_nvenc") { presetSpeed = "p6"; qualityParam = "-cq 18 -rc constqp"; }
                    else if (videoEncoder == "h264_amf") { presetSpeed = "balanced"; qualityParam = "-rc cqp -qp_i 18 -qp_p 18"; }
                    else { presetSpeed = "medium"; qualityParam = "-crf 18"; }
                }

                string tempOutputVideo = Path.Combine(outputSubFolder, "__converting__" + nameOnly + ".mp4");

                string finalOutputVideo = Path.Combine(outputSubFolder, nameOnly + ".mp4");
                if (File.Exists(finalOutputVideo))
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    finalOutputVideo = Path.Combine(outputSubFolder, $"{nameOnly}_{timestamp}.mp4");
                }

                string videoCmdArgs = $"-y -i \"{qData.FilePath}\" {finalFilter}-vf \"scale=1920:1080:flags=lanczos,colorspace=bt709:iall=bt709:fast=1\" -c:v {videoEncoder} -preset {presetSpeed} {qualityParam} -color_primaries bt709 -color_trc bt709 -colorspace bt709 -color_range tv -pix_fmt yuv420p -c:a aac -b:a 128k -ac 2 -movflags +faststart \"{tempOutputVideo}\"";
                logBuilder.AppendLine($"Çalıştırılan FFmpeg Komutu:\nffmpeg.exe {videoCmdArgs}");

                int processExitCode = -1;
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

                logBuilder.AppendLine($"Süreç Çıkış Kodu (ExitCode): {processExitCode}");
                SaveLogFile(outputSubFolder, nameOnly, logBuilder);

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
                    break;
                }

                if (processExitCode == 0 && File.Exists(tempOutputVideo))
                {
                    if (chkSplitAudio.Checked && qData.AudioStreamCount > 0)
                    {
                        SafeInvoke(() => {
                            qData.Status = "Sesler Ayrıştırılıyor...";
                            qData.Percent = 0;
                            int idx = queueList.IndexOf(qData);
                            if (idx != -1) lstQueueBox.Invalidate(lstQueueBox.GetItemRectangle(idx));
                            DisplayItemDetails(qData);
                        });
                        Application.DoEvents();

                        try
                        {
                            string audioSubFolder = Path.Combine(outputSubFolder, "Ses_Dosyalari");
                            if (!Directory.Exists(audioSubFolder)) Directory.CreateDirectory(audioSubFolder);

                            StringBuilder sbArgs = new StringBuilder();
                            sbArgs.Append($"-y -i \"{qData.FilePath}\" ");

                            for (int i = 0; i < qData.AudioStreamCount; i++)
                            {
                                string channelAudioFile = Path.Combine(audioSubFolder, $"{nameOnly}_Kanal_{i + 1}.wav");
                                sbArgs.Append($"-map 0:a:{i} -vn -acodec pcm_s16le -ar 48000 \"{channelAudioFile}\" ");
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
                                audioProc.Start();
                                audioProc.BeginErrorReadLine();
                                audioProc.WaitForExit();
                            }
                        }
                        catch { }
                    }

                    Thread.Sleep(1000);
                    try
                    {
                        FileInfo processedInfo = new FileInfo(tempOutputVideo);
                        double processedSizeMb = processedInfo.Length / (1024.0 * 1024.0);

                        if (processedSizeMb < originalSizeMb)
                        {
                            if (File.Exists(finalOutputVideo)) File.Delete(finalOutputVideo);
                            File.Move(tempOutputVideo, finalOutputVideo);

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
                    WriteStatusForPython(qData.FileName, "Hata", qData.Percent);

                    try
                    {
                        string errorHamFolder = Path.Combine(outputSubFolder, "__ERROR_HAMLAR__");
                        if (!Directory.Exists(errorHamFolder)) Directory.CreateDirectory(errorHamFolder);

                        string destErrorPath = Path.Combine(errorHamFolder, Path.GetFileName(qData.FilePath));
                        if (File.Exists(qData.FilePath))
                        {
                            if (File.Exists(destErrorPath)) File.Delete(destErrorPath);
                            File.Move(qData.FilePath, destErrorPath);
                        }
                    }
                    catch { }

                    SafeInvoke(() => {
                        int idx = queueList.IndexOf(qData);
                        if (idx != -1) lstQueueBox.Invalidate(lstQueueBox.GetItemRectangle(idx));
                        if (lstQueueBox.SelectedItem == qData) DisplayItemDetails(qData);
                        UpdateStatus($"Durum: {qData.FileName} işlenirken hata oluştu.");
                    });
                }
            }

            if (!token.IsCancellationRequested)
            {
                UpdateStatus("Durum: Tüm kuyruk işlemi tamamlandı.");
                try { File.Delete(Path.Combine(syncFolderPath, "durum.txt")); } catch { }
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

        private async void AutoTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                string[] txtFiles = Directory.GetFiles(syncFolderPath, "*.txt");

                if (txtFiles.Length > 0)
                {
                    string taskFile = txtFiles[0];
                    bool hasNewTasks = false;

                    string[] lines = File.ReadAllLines(taskFile, System.Text.Encoding.UTF8);

                    SafeInvoke(() => {
                        foreach (string line in lines)
                        {
                            string filePath = line.Trim().Trim('"', '\'').Trim();

                            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                            {
                                if (!IsFileReady(filePath)) continue;

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

                                    try
                                    {
                                        string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                                        if (File.Exists(ffmpegPath))
                                        {
                                            itemData.AudioStreamCount = GetAudioStreamCount(ffmpegPath, filePath);
                                        }
                                    }
                                    catch { }

                                    queueList.Add(itemData);
                                    hasNewTasks = true;
                                }
                            }
                        }
                        if (hasNewTasks && queueList.Count > 0 && lstQueueBox.SelectedIndex == -1)
                            lstQueueBox.SelectedIndex = 0;
                    });

                    try
                    {
                        File.WriteAllText(taskFile, string.Empty);
                    }
                    catch
                    {
                        try { File.Delete(taskFile); } catch { }
                    }

                    if (hasNewTasks && !isRunning)
                    {
                        BtnStart_Click(null, null);
                    }
                }
            }
            catch { }
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

        private (double duration, string resolution) GetVideoInfo(string ffmpegPath, string inputVideo)
        {
            double duration = 0;
            string resolution = "Bilinmiyor";
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
                }
            }
            catch { }
            return (duration, resolution);
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
        public double SavedPercent { get; set; }
        public string Resolution { get; set; }
        public string StartTimeText { get; set; }
        public string EndTimeText { get; set; }
        public int AudioStreamCount { get; set; }
    }
}