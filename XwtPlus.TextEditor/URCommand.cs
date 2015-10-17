
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
}

