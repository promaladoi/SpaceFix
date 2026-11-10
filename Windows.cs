using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LayoutPilot
{
    static class DemoWindow
    {
        private static readonly Color Surface = Color.FromArgb(13, 28, 46);
        private static readonly Color Card = Color.FromArgb(21, 43, 64);
        private static readonly Color Header = Color.FromArgb(16, 73, 89);
        private static readonly Color Border = Color.FromArgb(51, 82, 104);
        private static readonly Color Ink = Color.FromArgb(241, 250, 255);
        private static readonly Color Muted = Color.FromArgb(178, 203, 219);
        private static readonly Color Accent = Color.FromArgb(104, 240, 195);

        public static Form Create(Icon icon, KeyboardMap[] layouts, Action toggle, Action showCredits, Action showDictionary)
        {
            var form = new Form();
            form.SuspendLayout();
            form.Text = "SpaceFix";
            form.Icon = icon;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.Font = new Font("Segoe UI", 10);
            form.BackColor = Surface;
            form.ForeColor = Ink;
            form.FormBorderStyle = FormBorderStyle.FixedSingle;
            form.MaximizeBox = false;
            form.ClientSize = new Size(700, 456);
            form.HandleCreated += (sender, e) => SetDarkCaption(form);

            var header = AddCard(form, new Rectangle(24, 18, 652, 84), Header);
            var logo = new Panel { Name = "headerLogo", Bounds = new Rectangle(15, 10, 64, 64),
                BackColor = Header, TabStop = false };
            var logoImage = LoadHeaderLogo();
            logo.Paint += (sender, e) =>
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(logoImage, logo.ClientRectangle, 0, 0, logoImage.Width, logoImage.Height,
                    GraphicsUnit.Pixel);
            };
            logo.Disposed += (sender, e) => logoImage.Dispose();
            header.Controls.Add(logo);
            AddLabel(header, "SpaceFix", 92, 9, 250, 40, 25, Ink, true);
            AddLabel(header, "Панель управления", 94, 51, 238, 21, 10, Muted, false);

            var controls = AddCard(form, new Rectangle(24, 118, 218, 176), Card);
            AddLabel(controls, "Автозамена", 16, 12, 186, 26, 13, Ink, true);
            AddLabel(controls, "Исправляет после пробела", 16, 42, 186, 21, 10, Muted, false);
            var badge = AddCard(controls, new Rectangle(16, 73, 126, 30), Surface);
            var status = AddLabel(badge, "", 0, 0, 126, 30, 10, Accent, true);
            status.Name = "status";
            status.TextAlign = ContentAlignment.MiddleCenter;

            var test = AddCard(form, new Rectangle(256, 118, 420, 176), Card);
            AddLabel(test, "Проверка ввода", 16, 12, 207, 26, 13, Ink, true);
            var undoHint = AddLabel(test, "F10  ·  Отменить замену", 223, 13, 180, 24, 9, Muted, false);
            undoHint.TextAlign = ContentAlignment.MiddleRight;
            var field = AddCard(test, new Rectangle(16, 48, 387, 80), Surface);
            var editor = new TextBox
            {
                Name = "testField", AccessibleName = "Поле для проверки SpaceFix",
                Bounds = new Rectangle(10, 8, 367, 64), Font = new Font("Segoe UI", 13),
                BackColor = Surface, ForeColor = Ink, BorderStyle = BorderStyle.None, Multiline = true
            };
            field.Controls.Add(editor);
            field.Click += (sender, e) => editor.Focus();
            AddLabel(test, "В английской раскладке  ·  ghbdtn + пробел → привет",
                16, 139, 387, 21, 9, Muted, false);

            var pause = AddButton(controls, "", new Rectangle(16, 119, 186, 40), () => { toggle(); editor.Focus(); }, true);
            pause.Name = "pause";

            AddLabel(form, "Раскладки Windows", 24, 308, 250, 24, 11, Ink, true);
            if (layouts.Length < 3)
            {
                var hint = AddLabel(form, "Добавь нужную раскладку в настройках Windows",
                    366, 308, 310, 24, 9, Muted, false);
                hint.TextAlign = ContentAlignment.MiddleRight;
            }
            AddLayout(form, 24, "EN", Language.English, layouts);
            AddLayout(form, 246, "RU", Language.Russian, layouts);
            AddLayout(form, 468, "KK", Language.Kazakh, layouts);

            AddButton(form, "Личный словарь", new Rectangle(24, 406, 160, 34), showDictionary, false);
            AddButton(form, "Словари и лицензии", new Rectangle(334, 406, 170, 34), showCredits, false);
            AddButton(form, "Свернуть к часам", new Rectangle(516, 406, 160, 34), form.Close, false);
            form.AutoScaleDimensions = new SizeF(96, 96);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.ResumeLayout(true);
            form.ActiveControl = editor;
            return form;
        }

        public static Form CreatePersonalDictionary(Icon icon, PersonalDictionary dictionary)
        {
            var form = new Form();
            form.SuspendLayout();
            form.Text = "SpaceFix · личный словарь";
            form.Icon = icon;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.Font = new Font("Segoe UI", 10);
            form.BackColor = Surface;
            form.ForeColor = Ink;
            form.FormBorderStyle = FormBorderStyle.FixedSingle;
            form.MaximizeBox = false;
            form.ClientSize = new Size(700, 462);
            form.HandleCreated += (sender, e) => SetDarkCaption(form);

            AddLabel(form, "Личный словарь", 24, 15, 652, 40, 22, Ink, true);
            AddLabel(form, "Например, спс → спасибо. Для исключения заполни только поле «Слово».",
                24, 59, 652, 23, 10, Muted, false);
            AddLabel(form, "Слово", 24, 95, 206, 22, 10, Ink, true);
            AddLabel(form, "Замена", 244, 95, 432, 22, 10, Ink, true);
            var word = AddDictionaryInput(form, "Слово", new Rectangle(24, 120, 206, 28), PersonalDictionary.MaxWordLength);
            var replacement = AddDictionaryInput(form, "Замена", new Rectangle(244, 120, 432, 28),
                PersonalDictionary.MaxReplacementLength);
            var feedback = AddLabel(form, "Правила применяются после пробела", 24, 190, 652, 24, 9, Muted, false);
            AddLabel(form, "Сохранённые правила", 24, 226, 380, 28, 13, Ink, true);
            var count = AddLabel(form, "", 480, 229, 196, 22, 9, Muted, false);
            count.TextAlign = ContentAlignment.MiddleRight;

            var grid = CreateDictionaryGrid();
            Action refresh = () =>
            {
                grid.Rows.Clear();
                foreach (var entry in dictionary.Entries)
                    grid.Rows.Add(entry.Key, entry.Value == null ? "Не исправлять" : "Заменить", entry.Value);
                grid.CurrentCell = null;
                grid.ClearSelection();
                count.Text = grid.Rows.Count == 0 ? "Правил пока нет" : "Правил · " + grid.Rows.Count;
            };
            Action<Action, string> change = (save, message) =>
            {
                try { save(); }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException)) throw;
                    feedback.Text = "Не удалось сохранить словарь. Попробуй ещё раз.";
                    feedback.ForeColor = Ink;
                    return;
                }
                refresh();
                word.Clear();
                replacement.Clear();
                word.Focus();
                feedback.Text = message;
                feedback.ForeColor = Accent;
            };
            Action<bool> saveRule = keep =>
            {
                string key = word.Text.Trim();
                if (!PersonalDictionary.IsValidWord(key))
                {
                    feedback.Text = "Введи одно слово на английском, русском или казахском.";
                    feedback.ForeColor = Ink;
                    word.Focus();
                    return;
                }
                string value = keep ? null : replacement.Text;
                if (!keep && !PersonalDictionary.IsValidReplacement(value))
                {
                    feedback.Text = "Введи текст замены в одну строку.";
                    feedback.ForeColor = Ink;
                    replacement.Focus();
                    return;
                }
                change(() => dictionary.Set(key, value), keep ? "Исключение сохранено" : "Замена сохранена");
            };
            var saveButton = AddButton(form, "Сохранить замену", new Rectangle(24, 153, 164, 34), () => saveRule(false), true);
            var keepButton = AddButton(form, "Не исправлять", new Rectangle(200, 153, 146, 34), () => saveRule(true), false);
            form.Controls.Add(grid);
            var delete = AddButton(form, "Удалить правило", new Rectangle(24, 412, 155, 34), () =>
            {
                if (grid.SelectedRows.Count == 1)
                {
                    string key = (string)grid.SelectedRows[0].Cells[0].Value;
                    change(() => dictionary.Remove(key), "Правило удалено");
                }
            }, false);
            delete.Enabled = false;
            grid.SelectionChanged += (sender, e) =>
            {
                delete.Enabled = grid.SelectedRows.Count == 1;
                if (!delete.Enabled) return;
                DataGridViewRow row = grid.SelectedRows[0];
                word.Text = (string)row.Cells[0].Value;
                replacement.Text = (string)row.Cells[2].Value ?? "";
            };
            form.CancelButton = AddButton(form, "Закрыть", new Rectangle(536, 412, 140, 34), form.Close, false);
            form.AcceptButton = saveButton;
            if (dictionary.LoadFailed)
            {
                feedback.Text = "Не удалось прочитать личный словарь.";
                feedback.ForeColor = Ink;
                saveButton.Enabled = keepButton.Enabled = false;
            }
            refresh();
            form.AutoScaleDimensions = new SizeF(96, 96);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.ResumeLayout(true);
            form.ActiveControl = word;
            return form;
        }

        private static TextBox AddDictionaryInput(Form form, string name, Rectangle bounds, int maxLength)
        {
            var box = new TextBox { AccessibleName = name, Bounds = bounds, MaxLength = maxLength,
                BackColor = Card, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle };
            form.Controls.Add(box);
            return box;
        }

        private static DataGridView CreateDictionaryGrid()
        {
            var grid = new DataGridView
            {
                AccessibleName = "Сохранённые правила", Bounds = new Rectangle(24, 259, 652, 139),
                ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                MultiSelect = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                StandardTab = true, BackgroundColor = Card, GridColor = Border, EnableHeadersVisualStyles = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Card, ForeColor = Ink,
                SelectionBackColor = Border, SelectionForeColor = Ink, Padding = new Padding(5, 2, 5, 2) };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = Muted,
                SelectionBackColor = Surface, SelectionForeColor = Muted, Padding = new Padding(5, 2, 5, 2) };
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Слово", FillWeight = 30,
                SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Действие", FillWeight = 25,
                SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Замена", FillWeight = 45,
                SortMode = DataGridViewColumnSortMode.NotSortable });
            return grid;
        }

        private static Bitmap LoadHeaderLogo()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.logo"))
            using (var source = Image.FromStream(stream))
            {
                var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
                using (var canvas = Graphics.FromImage(bitmap))
                {
                    canvas.Clear(Header);
                    canvas.DrawImage(source, new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
                }
                return bitmap;
            }
        }

        private static void AddLayout(Control parent, int x, string code, Language language, KeyboardMap[] layouts)
        {
            bool installed = layouts.Any(layout => layout.Language == language);
            var tile = AddCard(parent, new Rectangle(x, 340, 208, 48), Card);
            AddLabel(tile, code, 13, 6, 31, 36, 10, installed ? Accent : Muted, true);
            AddLabel(tile, KeyboardMap.Label(language), 47, 4, 121, 22, 10, Ink, true);
            AddLabel(tile, installed ? "Подключена" : "Не подключена", 48, 24, 120, 18, 8, Muted, false);
        }

        private static Panel AddCard(Control parent, Rectangle bounds, Color fill)
        {
            var panel = new Panel { Bounds = bounds, BackColor = Color.Transparent, TabStop = false };
            panel.Paint += (sender, e) =>
            {
                float width = panel.ClientSize.Width - 1;
                float height = panel.ClientSize.Height - 1;
                float diameter = Math.Min(24 * e.Graphics.DpiX / 96, Math.Min(width, height));
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var shape = RoundedRectangle(width, height, diameter))
                using (var brush = new SolidBrush(fill))
                using (var stroke = new Pen(Border))
                {
                    e.Graphics.FillPath(brush, shape);
                    e.Graphics.DrawPath(stroke, shape);
                }
            };
            parent.Controls.Add(panel);
            return panel;
        }

        private static GraphicsPath RoundedRectangle(float width, float height, float diameter)
        {
            var shape = new GraphicsPath();
            shape.AddArc(0, 0, diameter, diameter, 180, 90);
            shape.AddArc(width - diameter, 0, diameter, diameter, 270, 90);
            shape.AddArc(width - diameter, height - diameter, diameter, diameter, 0, 90);
            shape.AddArc(0, height - diameter, diameter, diameter, 90, 90);
            shape.CloseFigure();
            return shape;
        }

        private static Label AddLabel(Control parent, string text, int x, int y, int width, int height,
            int points, Color color, bool bold)
        {
            var label = new Label { Text = text, Bounds = new Rectangle(x, y, width, height), ForeColor = color,
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", points, bold ? FontStyle.Bold : FontStyle.Regular) };
            parent.Controls.Add(label);
            return label;
        }

        private static Button AddButton(Control parent, string text, Rectangle bounds, Action click, bool primary)
        {
            var button = new Button { Text = text, Bounds = bounds, FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Surface, ForeColor = primary ? Surface : Muted,
                Font = new Font("Segoe UI", primary ? 10 : 9, primary ? FontStyle.Bold : FontStyle.Regular),
                UseVisualStyleBackColor = false, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(152, 249, 218) : Card;
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(82, 213, 173) : Border;
            button.Click += (sender, e) => click();
            button.SizeChanged += (sender, e) => RoundButton(button);
            RoundButton(button);
            button.Paint += (sender, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var shape = RoundedRectangle(button.Width - 1, button.Height - 1, button.Height * 0.65f))
                using (var corners = new GraphicsPath(FillMode.Alternate))
                using (var background = new SolidBrush(parent is Form ? Surface : Card))
                {
                    corners.AddRectangle(Rectangle.Inflate(button.ClientRectangle, 2, 2));
                    corners.AddPath(shape, false);
                    e.Graphics.FillPath(background, corners);
                    if (!primary)
                        using (var pen = new Pen(Border)) e.Graphics.DrawPath(pen, shape);
                }
            };
            parent.Controls.Add(button);
            return button;
        }

        private static void RoundButton(Button button)
        {
            using (var shape = RoundedRectangle(button.Width, button.Height, button.Height * 0.65f))
                button.Region = new Region(shape);
        }

        public static void SetPaused(Form form, bool paused)
        {
            var status = form.Controls.Find("status", true)[0];
            status.Text = paused ? "●  На паузе" : "●  Работает";
            status.ForeColor = paused ? Muted : Accent;
            form.Controls.Find("pause", true)[0].Text = paused ? "Возобновить" : "Приостановить";
        }

        public static ContextMenuStrip CreateTrayMenu()
        {
            return new ContextMenuStrip { BackColor = Card, ForeColor = Ink, ShowImageMargin = false,
                Font = new Font("Segoe UI", 10), Renderer = new TrayRenderer() };
        }

        private sealed class TrayRenderer : ToolStripSystemRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var brush = new SolidBrush(Card)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var pen = new Pen(Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected) return;
                using (var brush = new SolidBrush(Border))
                    e.Graphics.FillRectangle(brush, 1, 0, e.Item.Width - 2, e.Item.Height);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Selected ? Accent : Ink;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                using (var pen = new Pen(Border))
                    e.Graphics.DrawLine(pen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
            }
        }

        public static void ShowCredits(Icon icon)
        {
            var window = new Form { Text = "SpaceFix · словари и лицензии", Icon = icon,
                ClientSize = new Size(638, 447), StartPosition = FormStartPosition.CenterScreen,
                BackColor = Surface, Padding = new Padding(16) };
            window.HandleCreated += (sender, e) => SetDarkCaption(window);
            var credits = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 10), Dock = DockStyle.Fill, BackColor = Surface,
                ForeColor = Ink, BorderStyle = BorderStyle.None };
            credits.Text = string.Join("\r\n\r\n", new[] { "credits", "license.cc", "license.mpl",
                "license.typos.cc", "license.typos.mit", "license.typos.kk" }.Select(ReadResource))
                .Replace("\r\n", "\n").Replace("\n", "\r\n");
            window.Controls.Add(credits);
            window.Show();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        private static void SetDarkCaption(Form window)
        {
            const int useImmersiveDarkMode = 20, captionColor = 35;
            int enabled = 1;
            int color = ColorTranslator.ToWin32(Surface);
            DwmSetWindowAttribute(window.Handle, useImmersiveDarkMode, ref enabled, sizeof(int));
            DwmSetWindowAttribute(window.Handle, captionColor, ref color, sizeof(int));
        }

        private static string ReadResource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }
    }
}
