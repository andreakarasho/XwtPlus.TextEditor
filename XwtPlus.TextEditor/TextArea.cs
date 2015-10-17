using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xwt;
using Xwt.Drawing;
using XwtPlus.TextEditor.Margins;

namespace XwtPlus.TextEditor
{
    class URCommand
    {
        private int _pos;
        private string _s, _s2;
        private bool _delete;

        public DocumentLocation OldLocation { get; set; }
        public DocumentLocation  NewLocation { get; set; }

        public URCommand(int pos, string s, bool delete, DocumentLocation oldLocation, DocumentLocation newLocation)
        {
            this._pos = pos;
            this._s = s;
            this._delete = delete;

            this.OldLocation = oldLocation;
            this.NewLocation = newLocation;
        }

        public URCommand(int pos, string s, string s2, DocumentLocation oldLocation, DocumentLocation newLocation)
        {
            this._pos = pos;
            this._s = s;
            this._s2 = s2;

            this.OldLocation = oldLocation;
            this.NewLocation = newLocation;
        }

        private string Do(string s, bool delete)
        {
            if (string.IsNullOrEmpty(_s2))
                return delete ? s.Remove(_pos, _s.Length) : s.Insert(_pos, _s);
            else
                return delete ? s.Remove(_pos, _s2.Length).Insert(_pos, _s) : s.Remove(_pos, _s.Length).Insert(_pos, _s2);
        }

        public string Undo(string s)
        {
            return Do(s, !_delete);
        }

        public string Redo(string s)
        {
            return Do(s, _delete);
        }
    }

    class TextArea : Canvas
    {
        const int StartOffset = 4;

        Menu contextMenu;
        MenuItem cutMenuItem, copyMenuItem, pasteMenuItem, selectallMenuItem;
        TextEditor editor;

        List<Margin> margins = new List<Margin>();
        LineNumberMargin lineNumberMargin;
        PaddingMargin paddingMargin;
        TextViewMargin textViewMargin;

        public TextArea(TextEditor editor)
        {
            this.editor = editor;

            CanGetFocus = true;

            lineNumberMargin = new LineNumberMargin(editor);
            paddingMargin = new PaddingMargin(5);
            textViewMargin = new TextViewMargin(editor);

            margins.Add(lineNumberMargin);
            margins.Add(paddingMargin);
            margins.Add(textViewMargin);

            contextMenu = new Menu();

            cutMenuItem = new MenuItem("Cut");
            cutMenuItem.Clicked += (sender, e) => Cut();
            contextMenu.Items.Add(cutMenuItem);

            copyMenuItem = new MenuItem("Copy");
            copyMenuItem.Clicked += (sender, e) => Copy();
            contextMenu.Items.Add(copyMenuItem);

            pasteMenuItem = new MenuItem("Paste");
            pasteMenuItem.Clicked += (sender, e) => Paste();
            contextMenu.Items.Add(pasteMenuItem);

            contextMenu.Items.Add(new SeparatorMenuItem());

            selectallMenuItem = new MenuItem("Select All");
            selectallMenuItem.Clicked += (sender, e) => SelectAll();
            contextMenu.Items.Add(selectallMenuItem);

            ButtonPressed += HandleButtonPressed;
        }

        public double ComputedWidth
        {
            get { return margins.Select(margin => margin.ComputedWidth).Sum(); }
        }

        public double GetWidth()
        {
            return ComputedWidth;
        }

        public double GetHeight()
        {
            return textViewMargin.LineHeight * (editor.Document.LineCount + 1);
        }

        protected override void OnDraw(Context ctx, Rectangle dirtyRect)
        {
            base.OnDraw(ctx, dirtyRect);

            ctx.Save();
            ctx.SetColor(editor.Options.Background);
            ctx.Rectangle(dirtyRect);
            ctx.Fill();
            ctx.Restore();

            UpdateMarginXOffsets();
            RenderMargins(ctx, dirtyRect);
        }

        void UpdateMarginXOffsets()
        {
            double currentX = 0;
            foreach (Margin margin in margins.Where(margin => margin.IsVisible))
            {
                margin.XOffset = currentX;
                currentX += margin.Width;
            }
        }

        public int YToLine(double yPos)
        {
            return textViewMargin.YToLine(yPos);
        }

        public double LineToY(int logicalLine)
        {
            return textViewMargin.LineToY(logicalLine);
        }

        public double GetLineHeight(DocumentLine line)
        {
            return textViewMargin.GetLineHeight(line);
        }

        void RenderMargins(Context ctx, Rectangle dirtyRect)
        {
            int startLine = YToLine(dirtyRect.Y);
            double startY = LineToY(startLine);
            double currentY = startY;

            for (int lineNumber = startLine; ; lineNumber++)
            {
                var line = editor.Document.GetLine(lineNumber);

                double lineHeight = GetLineHeight(line);
                foreach (var margin in this.margins.Where(margin => margin.IsVisible))
                {
                    margin.DrawBackground(ctx, dirtyRect, line, lineNumber, margin.XOffset, currentY, lineHeight);
                    margin.Draw(ctx, dirtyRect, line, lineNumber, margin.XOffset, currentY, lineHeight);
                }

                currentY += lineHeight;
                if (currentY > dirtyRect.Bottom)
                    break;
            }
        }

        Margin GetMarginAtX(double x, out double startingPos)
        {
            double currentX = 0;
            foreach (Margin margin in margins.Where(margin => margin.IsVisible))
            {
                if (currentX <= x && (x <= currentX + margin.Width || margin.Width < 0))
                {
                    startingPos = currentX;
                    return margin;
                }
                currentX += margin.Width;
            }
            startingPos = -1;
            return null;
        }

        internal void RedrawLine(int lineNumber)
        {
            var line = editor.Document.GetLine(lineNumber);
            var dirtyRect = new Rectangle(0, LineToY(lineNumber), ComputedWidth, GetLineHeight(line));
            QueueDraw(dirtyRect);
        }

        internal void RedrawLines(int start, int end)
        {
            var line = editor.Document.GetLine(start);
            int lineCount = end - start;
            var dirtyRect = new Rectangle(0, LineToY(start), ComputedWidth, GetLineHeight(line) * lineCount);
            QueueDraw(dirtyRect);
        }

        internal void RedrawPosition(int line, int column)
        {
            //STUB
            QueueDraw();
        }

        double pressPositionX, pressPositionY;
        protected override void OnButtonPressed(ButtonEventArgs args)
        {
            base.OnButtonPressed(args);

            pressPositionX = args.X;
            pressPositionY = args.Y;

            double startPos;
            Margin margin = GetMarginAtX(pressPositionX, out startPos);
            if (margin != null)
            {
                margin.MousePressed(new MarginMouseEventArgs(editor, args.Button, args.X, args.Y, args.MultiplePress));
            }

            editor.SetFocus();
        }

        internal List<int> GetBreakpoints()
        {
            return lineNumberMargin.breakpoints;
        }

        internal void HighlightDebuggingLine(int line)
        {
            textViewMargin.HighlightDebuggingLine = line;
        }

        List<URCommand> urcommands = new List<URCommand>();
        int urpos = -1;

        private void AddURCommand(URCommand urcommand)
        {
            while (urpos < urcommands.Count - 1)
                urcommands.RemoveAt(urcommands.Count - 1);

            urcommands.Add(urcommand);
            urpos++;
        }

        public bool CanUndo()
        {
            return urpos != -1;
        }

        public bool CanRedo()
        {
            return urpos < urcommands.Count - 1;
        }

        public void Undo()
        {
            if (!CanUndo())
                return;

            this.editor.Document.Text = urcommands[urpos].Undo(this.editor.Document.Text);
            editor.Caret.Location = urcommands[urpos].OldLocation;
            urpos--;

            editor.ResetCaretState();

        }

        public void Redo()
        {
            if (!CanRedo())
                return;

            this.editor.Document.Text = urcommands[urpos + 1].Redo(this.editor.Document.Text);
            editor.Caret.Location = urcommands[urpos + 1].NewLocation;
            urpos++;

            editor.ResetCaretState();
        }

        internal bool CanCopy()
        {
            return !editor.Selection.IsEmpty;
        }

        internal bool CanPaste()
        {
            return !string.IsNullOrEmpty(Clipboard.GetText());
        }

        internal void Cut()
        {
            if (!CanCopy())
                return;
            
            Copy();

            var oldtext = editor.Document.Text.Substring(editor.Selection.Offset, editor.Selection.Length);
            var pos = editor.Selection.Offset;
            var oldloc = editor.Caret.Location;

            editor.Document.Remove(editor.Selection);
            Deselect();

            AddURCommand(new URCommand(pos, oldtext, true, oldloc, editor.Caret.Location));
        }

        internal void Copy()
        {
            if (CanCopy())
                Clipboard.SetText(editor.Document.GetTextAt(editor.Selection.GetRegion(editor.Document)));
        }

        internal void Paste()
        {
            if (CanPaste())
            {
                string text = Clipboard.GetText();

                string[] split = text.Split(new [] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                InsertText(text);

                if (split.Length > 1)
                {
                    editor.Caret.Line += split.Length - 1;
                    editor.Caret.Column = split[split.Length - 1].Length + 1;
                }
                editor.ResetCaretState();
            }
        }

        private void MoveCursorUp()
        {
            if (editor.Caret.Line > 1)
                editor.Caret.Line--;
            Deselect();
        }

        private void MoveCursorDown()
        {
            if(editor.Caret.Line < editor.Document.LineCount)
                editor.Caret.Line++;
            Deselect();
        }

        private void MoveCursorLeft()
        {
            if (editor.Selection.IsEmpty)
            {
                if (editor.Caret.Column == 1)
                {
                    if (editor.Caret.Line == 1)
                        return;

                    var line = editor.Document.GetLine(editor.Caret.Line - 1);
                    editor.Caret.Location = new DocumentLocation(editor.Caret.Line - 1, line.Length + 1);
                }
                else
                    editor.Caret.Column--;
            }
            else
                editor.Caret.Offset = editor.Selection.Offset;
            Deselect();
        }

        private void MoveCursorRight()
        {
            if (editor.Selection.IsEmpty)
            {
                var line = editor.Document.GetLine(editor.Caret.Line);
                if (editor.Caret.Column > line.Length)
                {
                    editor.Caret.Column = 1;
                    editor.Caret.Line++;
                }
                else
                    editor.Caret.Column++;
            }
            else
                editor.Caret.Offset = editor.Selection.EndOffset;
            Deselect();
        }

        private void DeleteText(bool back)
        {
            if (editor.Selection.IsEmpty)
            {
                var line = editor.Document.GetLine(editor.Caret.Line);

                if (!back && editor.Caret.Line == editor.Document.LineCount && editor.Caret.Column > line.Length)
                    return;

                if (back && editor.Caret.Line == 1 && editor.Caret.Column == 1)
                    return;

                var offset = editor.Document.GetOffset(editor.Caret.Location) - Convert.ToInt32(back);
                var oldloc = editor.Caret.Location;
                var oldtext = editor.Document.Text.Substring(offset, 1);

                editor.Document.Remove(offset, 1);
                if (back)
                    MoveCursorLeft();
                
                AddURCommand(new URCommand(offset, oldtext, true, oldloc, editor.Caret.Location));
            }
            else
            {
                var offset = editor.Document.GetOffset(editor.Caret.Location);
                var oldloc = editor.Caret.Location;
                var oldtext = editor.Document.Text.Substring(editor.Selection.Offset, editor.Selection.Length);

                editor.Document.Remove(editor.Selection);
                Deselect();

                AddURCommand(new URCommand(offset, oldtext, true, oldloc, editor.Caret.Location));
            }
            QueueDraw();
        }

        internal void HandleButtonPressed(object sender, ButtonEventArgs e)
        {
            if (e.Button == PointerButton.Right)
            {
                cutMenuItem.Sensitive = !editor.Selection.IsEmpty;
                copyMenuItem.Sensitive = !editor.Selection.IsEmpty;
                pasteMenuItem.Sensitive = !string.IsNullOrEmpty(Clipboard.GetText());

                contextMenu.Popup();
            }
        }

        internal void HandleKeyPressed(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            if (e.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (e.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    switch (e.Key)
                    {
                        case Key.Z:
                        case Key.z:
                            Redo();
                            break;
                    }
                }
                else
                {
                    switch (e.Key)
                    {
                        case Key.a:
                        case Key.A:
                            SelectAll();
                            break;
                        case Key.x:
                        case Key.X:
                            Cut();
                            break;
                        case Key.c:
                        case Key.C:
                            Copy();
                            break;
                        case Key.v:
                        case Key.V:
                            Paste();
                            break;
                        case Key.Z:
                        case Key.z:
                            Undo();
                            break;
                        case Key.Y:
                        case Key.y:
                            Redo();
                            break;
                    }
                }
            }

            switch (e.Key)
            {
                case Key.Home:
                    editor.Caret.Column = 1;
                    Deselect();
                    break;
                case Key.Up:
                    MoveCursorUp();
                    break;
                case Key.Down:
                    MoveCursorDown();
                    break;
                case Key.Left:
                    MoveCursorLeft();
                    break;
                case Key.Right:
                    MoveCursorRight();
                    break;
                case Key.Delete:
                    DeleteText(false);
                    break;
                case Key.BackSpace:
                    DeleteText(true);
                    break;
                case Key.Tab:
                    InsertText("\t");
                    break;
                default:
                    e.Handled = false;
                    break;
            }

            if (e.Handled)
            {
                editor.ResetCaretState();
            }
        }

        internal void SelectAll()
        {
            editor.Selection = new TextSegment(0, editor.Document.TextLength);
        }

        void Deselect()
        {
            editor.Selection = new TextSegment();
        }

        internal void HandleTextInput(object sender, TextInputEventArgs args)
        {
            base.OnTextInput(args);

            InsertText(args.Text);

            editor.ResetCaretState();

            args.Handled = true;
        }

        void InsertText(string text)
        {
            var oldloc = editor.Caret.Location;
            string oldtext = null;

            if (!editor.Selection.IsEmpty)
            {
                oldtext = editor.Document.Text.Substring(editor.Selection.Offset, editor.Selection.Length);

                editor.Document.Remove(editor.Selection);
                editor.Caret.Offset = editor.Selection.Offset;
                Deselect();
            }

            int offset = editor.Document.GetOffset(editor.Caret.Location);

            if (text == "\r" || text == "\n")
            {
                string tabText = "";
                if (editor.Options.IndentStyle == IndentStyle.Auto)
                {
                    tabText = editor.Document.GetLine(editor.Caret.Line).GetIndentation(editor.Document);
                }
                editor.Document.Insert(offset, text + tabText);
                editor.Caret.Location = new DocumentLocation(editor.Caret.Line + 1, tabText.Length + 1);
            }
            else
            {
                editor.Document.Insert(offset, text);
                editor.Caret.Column += text.Length;
            }

            if (string.IsNullOrEmpty(oldtext))
                AddURCommand(new URCommand(offset, text, false, oldloc, editor.Caret.Location));
            else
                AddURCommand(new URCommand(offset, oldtext, text, oldloc, editor.Caret.Location));

            QueueDraw();
        }

        List<Tuple<PointerButton, Action<double, double>>> mouseMotionTrackers = new List<Tuple<PointerButton, Action<double, double>>>();

        internal void RegisterMouseMotionTracker(PointerButton releaseButton, Action<double, double> callback)
        {
            mouseMotionTrackers.Add(Tuple.Create(releaseButton, callback));
        }

        protected override void OnMouseMoved(MouseMovedEventArgs args)
        {
            base.OnMouseMoved(args);

            if (args.X >= lineNumberMargin.Width)
                this.Cursor = CursorType.IBeam;
            else
                this.Cursor = CursorType.Arrow;

            NotifyTrackers(args.X, args.Y);
        }

        protected override void OnButtonReleased(ButtonEventArgs args)
        {
            base.OnButtonReleased(args);

            NotifyTrackers(args.X, args.Y);

            mouseMotionTrackers.RemoveAll(tracker => tracker.Item1 == args.Button);
        }

        void NotifyTrackers(double x, double y)
        {
            foreach (var mouseMotionTracker in mouseMotionTrackers)
                mouseMotionTracker.Item2(Math.Max((int)x, textViewMargin.XOffset), Math.Max((int)y, 0));
        }
    }
}
