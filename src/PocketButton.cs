using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RussiaFlag
{
    internal sealed class PocketButton : Button
    {
        private bool hover, pressed;
        internal bool Primary;
        internal PocketButton(string text, bool primary = false)
        {
            Text = text; AccessibleName = text; Primary = primary;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            Height = 40; Margin = new Padding(4); Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? SystemColors.Control : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF box = new RectangleF(1, 1, Width - 3, Height - 3);
            using (GraphicsPath shape = new GraphicsPath())
            {
                const float diameter = 16;
                shape.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
                shape.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
                shape.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
                shape.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90); shape.CloseFigure();
                Color fill = !Enabled ? Color.FromArgb(232, 233, 236) : Primary ?
                    (pressed ? Color.FromArgb(0, 86, 190) : hover ? Color.FromArgb(0, 111, 235) : Color.FromArgb(0, 122, 255)) :
                    (pressed ? Color.FromArgb(222, 225, 231) : hover ? Color.FromArgb(247, 249, 252) : Color.White);
                using (Brush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, shape);
                using (Pen border = new Pen(Focused && Enabled ? Color.FromArgb(0, 122, 255) : Primary && Enabled ? fill : Color.FromArgb(207, 210, 218), Focused ? 2 : 1)) e.Graphics.DrawPath(border, shape);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(ClientRectangle, -8, -4),
                !Enabled ? Color.FromArgb(142, 146, 154) : Primary ? Color.White : Color.FromArgb(35, 39, 47),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}
