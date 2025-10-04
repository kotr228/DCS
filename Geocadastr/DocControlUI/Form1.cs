using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DocControlUI.Models;
using DocControlUI.Services;
using System.Runtime.InteropServices;

namespace DocControlUI
{
    public partial class Form1 : Form
    {
        private FakeApi _fapi = new FakeApi();

        private readonly ApiClient _api;

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8) return GetWindowLongPtr64(hWnd, nIndex);
            return new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        private void SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, nIndex, value);
            else SetWindowLong32(hWnd, nIndex, value.ToInt32());
        }

        // Флаги
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_BORDER = 0x00800000;
        private const int WS_EX_CLIENTEDGE = 0x00000200;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;

        // Метод, що видаляє системний бордер/client-edge і примушує оновити вікно
        private void RemoveNativeComboBorder(ComboBox cb)
        {
            if (cb == null) return;
            // гарантуємо, що handle створено
            IntPtr h = cb.Handle;

            // стиль
            var stylePtr = GetWindowLongPtr(h, GWL_STYLE);
            long style = stylePtr.ToInt64();
            style &= ~WS_BORDER; // прибираємо WS_BORDER
            SetWindowLongPtr(h, GWL_STYLE, new IntPtr(style));

            // розширений стиль
            var exPtr = GetWindowLongPtr(h, GWL_EXSTYLE);
            long ex = exPtr.ToInt64();
            ex &= ~WS_EX_CLIENTEDGE; // прибираємо client edge
            SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));

            // оновлюємо фрейм
            SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER);
        }

        public Form1()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            InitializeComponent();
            ApplyDarkTheme();
            menuStrip1.Renderer = new NoBorderRenderer();
            menuStrip1.BackColor = Color.FromArgb(36, 36, 36);

            comboRepo.Container = this.panelControl; // <- задаємо контейнер
            this.panelControl.Controls.Add(comboRepo);
            comboRepo.SetHeaderText("Виберіть директорію");

            // Додаємо кнопку всередину comboRepo
            Button btnAddRepo = new Button();
            btnAddRepo.Text = "Додати репозиторій";
            btnAddRepo.Height = 30;
            btnAddRepo.Click += async (s, ev) =>
            {
                string newRepo = ShowInputDialog("Введіть назву нового репозиторію:");
                if (!string.IsNullOrWhiteSpace(newRepo))
                {
                    await _fapi.AddRepo(newRepo); // заглушка
                    MessageBox.Show($"Репозиторій '{newRepo}' додано! (заглушка)");
                }
            };
            comboRepo.AddControl(btnAddRepo);


            comboBranch.Container = this.panelControl; // <- задаємо контейнер
            this.panelControl.Controls.Add(comboBranch);
            comboBranch.SetHeaderText("Виберіть гілку");

            // Додаємо кнопку всередину comboBranch
            Button btnAddBranch = new Button();
            btnAddBranch.Text = "Додати гілку";
            btnAddBranch.Height = 30;
            btnAddBranch.Click += (s, ev) =>
            {
                MessageBox.Show("Гілка додана! (заглушка)");
            };
            comboBranch.AddControl(btnAddBranch);

            // Додаємо елементи, наприклад:
            for (int i = 0; i < 5; i++)
            {
                Button b = new Button();
                b.Text = "Repo " + i;
                b.Height = 30;
                comboRepo.AddControl(b);

                Button bb = new Button();
                bb.Text = "Branch " + i;
                bb.Height = 30;
                comboBranch.AddControl(bb);
            }

            // Робимо текст білим
            foreach (ToolStripMenuItem item in menuStrip1.Items)
            {
                item.ForeColor = Color.White;
            }
            _api = new ApiClient();
        }

        private void ApplyDarkTheme()
        {
            // Основний фон форми
            this.BackColor = Color.FromArgb(25, 25, 25);
            this.ForeColor = Color.Black;

            foreach (Control ctrl in this.Controls)
            {
                ApplyThemeToControl(ctrl);
            }
        }

        private void ApplyThemeToControl(Control ctrl)
        {
            if (ctrl is Button btn)
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.BorderColor = Color.FromArgb(0, 200, 0);

                btn.BackColor = Color.FromArgb(40, 40, 40);
                btn.ForeColor = Color.FromArgb(0, 200, 0);
            }
            else if (ctrl is TextBox txt)
            {
                txt.BackColor = Color.FromArgb(35, 35, 35);
                txt.ForeColor = Color.White;
                txt.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (ctrl is ListBox list)
            {
                list.BackColor = Color.FromArgb(35, 35, 35);
                list.ForeColor = Color.White;
            }
            else if (ctrl is TabControl tabs)
            {
                tabs.Appearance = TabAppearance.Normal;
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabs.DrawItem += (s, e) =>
                {
                    TabPage page = tabs.TabPages[e.Index];
                    Rectangle rec = e.Bounds;

                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(40, 40, 40)))
                        e.Graphics.FillRectangle(brush, rec);

                    TextRenderer.DrawText(e.Graphics, page.Text,
                        e.Font, rec, Color.FromArgb(0, 200, 0), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };

                foreach (TabPage page in tabs.TabPages)
                {
                    page.BackColor = Color.FromArgb(25, 25, 25);
                    page.ForeColor = Color.White;
                }
            }
            else if (ctrl is DataGridView grid)
            {
                grid.BackgroundColor = Color.FromArgb(25, 25, 25);
                grid.GridColor = Color.FromArgb(0, 200, 0);

                grid.DefaultCellStyle.BackColor = Color.FromArgb(35, 35, 35);
                grid.DefaultCellStyle.ForeColor = Color.White;
                grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 200, 0);
                grid.DefaultCellStyle.SelectionForeColor = Color.Black;

                grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(40, 40, 40);
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(0, 200, 0);
                grid.EnableHeadersVisualStyles = false;
            }

            // рекурсивно для внутрішніх контролів (наприклад, у TabPage)
            foreach (Control child in ctrl.Controls)
            {
                ApplyThemeToControl(child);
            }
        }




        private async void btnRefresh_Click(object sender, EventArgs e)
        {
            var folders = await _api.GetFolders();
            dataGridView1.DataSource = folders;
        }

        private async void btnAdd_Click(object sender, EventArgs e)
        {
            /*var folder = new FolderObject
            {
                Path = "/newFolder",
                Name = txtFolderName.Text,
                IsFile = false
            };

            await _api.AddFolder(folder);*/
            MessageBox.Show("Папку додано!");

            // Автооновлення
            var folders = await _api.GetFolders();
            dataGridView1.DataSource = folders;
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void btnMaximize_Click(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Normal)
                this.WindowState = FormWindowState.Maximized;
            else
                this.WindowState = FormWindowState.Normal;
        }

        private void btnMinimize_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
        }

        private void panelTop_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HTCAPTION = 0x2;

        private void Form1_Load(object sender, EventArgs e)
        {
            // DropDown для Repo
            comboRepo = new DropDownPanel();
            comboRepo.Left = 20;
            comboRepo.Top = 20;
            comboRepo.Width = 250;
            
            // DropDown для Branch
            comboBranch = new DropDownPanel();
            comboBranch.Left = 300;
            comboBranch.Top = 20;
            comboBranch.Width = 250;
        }


        private string ShowInputDialog(string prompt)
        {
            Form inputForm = new Form();
            inputForm.Width = 400;
            inputForm.Height = 150;
            inputForm.Text = prompt;

            TextBox textBox = new TextBox() { Left = 20, Top = 20, Width = 340 };
            Button okButton = new Button() { Text = "OK", Left = 200, Width = 70, Top = 60, DialogResult = DialogResult.OK };
            Button cancelButton = new Button() { Text = "Скасувати", Left = 280, Width = 70, Top = 60, DialogResult = DialogResult.Cancel };

            inputForm.Controls.Add(textBox);
            inputForm.Controls.Add(okButton);
            inputForm.Controls.Add(cancelButton);
            inputForm.AcceptButton = okButton;
            inputForm.CancelButton = cancelButton;

            return inputForm.ShowDialog() == DialogResult.OK ? textBox.Text : null;
        }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            comboRepo.HideDropDown();
            comboBranch.HideDropDown();
        }

        private void tabControl1_DrawItem(object sender, DrawItemEventArgs e)
        {
            TabControl tabControl = sender as TabControl;
            TabPage page = tabControl.TabPages[e.Index];

            // Чи вибрана вкладка
            bool isSelected = (e.Index == tabControl.SelectedIndex);

            // Фон вкладки
            Color backColor = isSelected ? Color.FromArgb(50, 50, 50) : Color.FromArgb(36, 36, 36);
            using (SolidBrush brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            // Текст вкладки
            TextRenderer.DrawText(
                e.Graphics,
                page.Text,
                tabControl.Font,
                e.Bounds,
                isSelected ? Color.LimeGreen : Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
        }
    }
}
