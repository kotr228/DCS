using System;
using System.Drawing;
using System.Windows.Forms;

namespace DocControlUI
{
    public class DropDownPanel : UserControl
    {
        private Panel headerPanel;       // Головна видима частина
        private Label lblText;           // Текст хедера
        private Panel dropPanel;         // Панель, що видавлюється
        private bool isExpanded = false; // Стан панелі
        private Panel container;         // Зовнішній контейнер (panelControl)

        // Властивість для зовнішнього контейнера
        public Panel Container
        {
            get => container;
            set => container = value;
        }

        // Конструктор без параметрів — Designer підтримує
        public DropDownPanel()
        {
            this.Width = 250;
            this.Height = 40;
            this.BackColor = Color.FromArgb(36, 36, 36);

            // ===== Header =====
            headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = 40;
            headerPanel.BackColor = Color.FromArgb(50, 50, 50);
            headerPanel.Cursor = Cursors.Hand;
            headerPanel.Click += HeaderPanel_Click;
            this.Controls.Add(headerPanel);

            // Текст
            lblText = new Label();
            lblText.Text = "Виберіть...";
            lblText.ForeColor = Color.White;
            lblText.Location = new Point(10, 10);
            lblText.AutoSize = true;
            headerPanel.Controls.Add(lblText);

            // Стрілка
            headerPanel.Paint += HeaderPanel_Paint;

            // ===== Drop panel =====
            dropPanel = new Panel();
            dropPanel.Height = 0; // спочатку закрита
            dropPanel.Width = this.Width;
            dropPanel.BackColor = Color.FromArgb(60, 60, 60);
            dropPanel.Visible = false;   // ховаємо поки закритий
        }

        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            int arrowX = headerPanel.Width - 20;
            int arrowY = headerPanel.Height / 2 - 2;
            Point[] triangle = {
                new Point(arrowX, arrowY),
                new Point(arrowX + 8, arrowY),
                new Point(arrowX + 4, arrowY + 5)
            };
            e.Graphics.FillPolygon(Brushes.Gray, triangle);
        }

        private void HeaderPanel_Click(object sender, EventArgs e)
        {
            ToggleDropDown();
        }

        private void ToggleDropDown()
        {
            if (container == null)
            {
                // Якщо контейнер не заданий, dropPanel не можна показати
                return;
            }

            if (isExpanded)
            {
                dropPanel.Visible = false;
            }
            else
            {
                // Позиціонуємо dropPanel під headerPanel
                Point locationOnContainer = this.PointToScreen(new Point(0, headerPanel.Bottom));
                dropPanel.Location = container.PointToClient(locationOnContainer);

                // Додаємо dropPanel у контейнер, якщо ще не доданий
                if (!container.Controls.Contains(dropPanel))
                    container.Controls.Add(dropPanel);

                // Встановлюємо висоту
                dropPanel.Height = Math.Min(dropPanel.Controls.Count * 32 + 4, 200);
                dropPanel.BringToFront();
                dropPanel.Visible = true;
            }

            isExpanded = !isExpanded;
        }

        /// <summary>
        /// Додає контроль у dropPanel
        /// </summary>
        public void AddControl(Control ctrl)
        {
            ctrl.Width = dropPanel.Width - 4;
            ctrl.Left = 2;
            ctrl.Top = dropPanel.Controls.Count * (ctrl.Height + 2) + 2;
            dropPanel.Controls.Add(ctrl);
        }

        /// <summary>
        /// Змінює текст у хедері
        /// </summary>
        public void SetHeaderText(string text)
        {
            lblText.Text = text;
            lblText.Invalidate();
            lblText.Update();
        }

        public void HideDropDown()
        {
            if (isExpanded)
                ToggleDropDown();
        }


    }
}
