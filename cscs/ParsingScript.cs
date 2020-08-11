using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public class ParsingScript
    {
        private string m_data;          // contains the whole script
        private int m_from;             // a pointer to the script
        private string m_filename;      // filename containing the script
        private string m_originalScript;// original raw script
        private int m_scriptOffset = 0; // used in functions defined in bigger scripts
        private Dictionary<int, int> m_char2Line = null; // pointers to the original lines

        public int Pointer
        {
            get { return m_from; }
            set { m_from = value; }
        }
        public string String
        {
            get { return m_data; }
            set { m_data = value; }
        }
        public string Rest
        {
            get { return Substr(m_from, Constants.MAX_CHARS_TO_SHOW); }
        }
        public char Current
        {
            get { return m_from < m_data.Length ? m_data[m_from] : Constants.EMPTY; }
        }
        public char Prev
        {
            get { return m_from >= 1 ? m_data[m_from - 1] : Constants.EMPTY; }
        }
        public char PrevPrev
        {
            get { return m_from >= 2 ? m_data[m_from - 2] : Constants.EMPTY; }
        }
        public char Next
        {
            get { return m_from + 1 < m_data.Length ? m_data[m_from + 1] : Constants.EMPTY; }
        }
        public Dictionary<int, int> Char2Line
        {
            get { return m_char2Line; }
            set { m_char2Line = value; }
        }
        public int ScriptOffset
        {
            get { return m_scriptOffset; }
            set { m_scriptOffset = value; }
        }
        public string Filename
        {
            get { return m_filename; }
            set
            {
                m_filename = Utils.GetFullPath(value);
            }
        }
        public string PWD
        {
            get
            {
                return Utils.GetDirectoryName(m_filename);
            }
        }
        public string OriginalScript
        {
            get { return m_originalScript; }
            set { m_originalScript = value; }
        }

        public string CurrentAssign { get; set; }

        public Debugger Debugger
        {
            get;
            set;
        }

        public string CurrentModule { get; set; }

        public Dictionary<string, Dictionary<string, int>> AllLabels
        {
            get;
            set;
        }
        public Dictionary<string, string> LabelToFile
        {
            get;
            set;
        }

        public List<int> PointersBack { get; set; } = new List<int>();

        string m_functionName = "";
        public string FunctionName
        {
            get { return m_functionName;  }
            set { m_functionName = value.ToLower(); }
        }

        public ParserFunction.StackLevel StackLevel { get; set; }
        public bool ProcessingList { get; set; }

        public bool DisableBreakpoints;
        public bool InTryBlock;
        public string MainFilename;

        public ParsingScript ParentScript;

        public CSCSClass CurrentClass { get; set; }
        public CSCSClass.ClassInstance ClassInstance { get; set; }

        public ParsingScript(string data, int from = 0,
                             Dictionary<int, int> char2Line = null)
        {
            m_data = data;
            m_from = from;
            m_char2Line = char2Line;
        }

        public ParsingScript(ParsingScript other)
        {
            m_data = other.String;
            m_from = other.Pointer;
            m_char2Line = other.Char2Line;
            m_filename = other.Filename;
            m_originalScript = other.OriginalScript;
            StackLevel = other.StackLevel;
            CurrentClass = other.CurrentClass;
            ClassInstance = other.ClassInstance;
            ScriptOffset = other.ScriptOffset;
            Debugger = other.Debugger;
            InTryBlock = other.InTryBlock;
            AllLabels = other.AllLabels;
            LabelToFile = other.LabelToFile;
            FunctionName = other.FunctionName;
        }

        public int Size() { return m_data.Length; }
        public bool StillValid() { return m_from < m_data.Length; }

        public void SetDone() { m_from = m_data.Length; }

        public string GetFilePath(string path)
        {
            if (!Path.IsPathRooted(path))
            {
                string pathname = Path.Combine(PWD, path);
                if (File.Exists(pathname))
                {
                    return pathname;
                }
            }
            return path;
        }

        public bool StartsWith(string str, bool caseSensitive = true)
        {
            if (String.IsNullOrEmpty(str) || str.Length > m_data.Length - m_from)
            {
                return false;
            }
            for (int i = m_from; i < m_data.Length && i < str.Length + m_from; i++)
            {
                var ch1 = str[i - m_from];
                var ch2 = m_data[i];

                if ((caseSensitive && ch1 != ch2) ||
                   (!caseSensitive && char.ToUpperInvariant(ch1) != char.ToUpperInvariant(ch2)))
                {
                    return false;
                }
            }

            return true;
        }

        public bool ProcessReturn()
        {
            if (PointersBack.Count > 0)
            {
                Pointer = PointersBack[PointersBack.Count - 1];
                PointersBack.RemoveAt(PointersBack.Count - 1);
                return true;
            }
            return false;
        }

        public int Find(char ch, int from = -1)
        { return m_data.IndexOf(ch, from < 0 ? m_from : from); }

        public int FindFirstOf(string str, int from = -1)
        { return FindFirstOf(str.ToCharArray(), from); }

        public int FindFirstOf(char[] arr, int from = -1)
        { return m_data.IndexOfAny(arr, from < 0 ? m_from : from); }

        public string Substr(int fr = -2, int len = -1)
        {
            int from = Math.Min(Pointer, m_data.Length - 1);
            fr = fr == -2 ? from : fr == -1 ? 0 : fr;
            return len < 0 || len >= m_data.Length - fr ? m_data.Substring(fr) : m_data.Substring(fr, len);
        }

        public string GetStack(int firstOffset = 0)
        {
            StringBuilder result = new StringBuilder();
            ParsingScript script = this;

            while (script != null)
            {
                int pointer = script == this ? script.Pointer + firstOffset : script.Pointer;
                int lineNumber = script.GetOriginalLineNumber(pointer);
                string filename = string.IsNullOrWhiteSpace(script.Filename) ? "" :
                                  Utils.GetFullPath(script.Filename);
                string line = string.IsNullOrWhiteSpace(filename) || !File.Exists(filename) ? "" :
                              File.ReadLines(filename).Skip(lineNumber).Take(1).First();

                result.AppendLine("" + lineNumber);
                result.AppendLine(filename);
                result.AppendLine(line.Trim());

                script = script.ParentScript;
            }

            return result.ToString().Trim();
        }

        public string GetOriginalLine(out int lineNumber)
        {
            lineNumber = GetOriginalLineNumber();
            if (lineNumber < 0 || m_originalScript == null)
            {
                return "";
            }

            string[] lines = m_originalScript.Split(Constants.END_LINE);
            if (lineNumber < lines.Length)
            {
                return lines[lineNumber];
            }

            return "";
        }

        public int OriginalLineNumber { get { return GetOriginalLineNumber(); } }
        public string OriginalLine
        {
            get
            {
                int lineNumber;
                return GetOriginalLine(out lineNumber);
            }
        }

        public int GetOriginalLineNumber()
        {
            return GetOriginalLineNumber(m_from);
        }
        public int GetOriginalLineNumber(int charNumber)
        {
            if (m_char2Line == null || m_char2Line.Count == 0)
            {
                return -1;
            }

            int pos = m_scriptOffset + charNumber;
            List<int> lineStart = m_char2Line.Keys.ToList();
            int lower = 0;
            int index = lower;

            if (pos <= lineStart[lower])
            { // First line.
                return m_char2Line[lineStart[lower]];
            }
            int upper = lineStart.Count - 1;
            if (pos >= lineStart[upper])
            { // Last line.
                return m_char2Line[lineStart[upper]];
            }

            while (lower <= upper)
            {
                index = (lower + upper) / 2;
                int guessPos = lineStart[index];
                if (pos == guessPos)
                {
                    break;
                }
                if (pos < guessPos)
                {
                    if (index == 0 || pos > lineStart[index - 1])
                    {
                        break;
                    }
                    upper = index - 1;
                }
                else
                {
                    lower = index + 1;
                }
            }

            int charIndex = lineStart[index];
            return m_char2Line[charIndex];
        }

        public char At(int i) { return m_data[i]; }
        public char CurrentAndForward() { return m_data[m_from++]; }

        public char TryCurrent()
        {
            return m_from < m_data.Length ? m_data[m_from] : Constants.EMPTY;
        }
        public char TryNext()
        {
            return m_from + 1 < m_data.Length ? m_data[m_from + 1] : Constants.EMPTY;
        }
        public char TryPrev()
        {
            return m_from >= 1 ? m_data[m_from - 1] : Constants.EMPTY;
        }
        public char TryPrevPrev()
        {
            return m_from >= 2 ? m_data[m_from - 2] : Constants.EMPTY;
        }
        public char TryPrevPrevPrev()
        {
            return m_from >= 3 ? m_data[m_from - 3] : Constants.EMPTY;
        }

        public string FromPrev(int backChars = 1, int maxChars = Constants.MAX_CHARS_TO_SHOW)
        {
            int from = Math.Max(0, m_from - backChars);
            int max = Math.Min(m_data.Length - from, maxChars);
            string result = m_data.Substring(from, max);
            return result;
        }

        public bool IsPrevious(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return true;
            }
            if (m_from < str.Length || m_data.Length < str.Length)
            {
                return false;
            }

            var substr = m_data.Substring(m_from - str.Length, str.Length);
            return substr.Equals(str, StringComparison.OrdinalIgnoreCase);
        }

        public void Forward(int delta = 1) { m_from += delta; }
        public void Backward(int delta = 1) { if (m_from >= delta) m_from -= delta; }

        public void MoveForwardIf(char[] arr)
        {
            foreach (char ch in arr)
            {
                if (MoveForwardIf(ch))
                {
                    return;
                }
            }
        }
        public bool MoveForwardIf(char expected, char expected2 = Constants.EMPTY)
        {
            if (StillValid() && (Current == expected || Current == expected2))
            {
                Forward();
                return true;
            }
            return false;
        }
        public void MoveBackIf(char notExpected)
        {
            if (StillValid() && Pointer > 0 && Current == notExpected)
            {
                Backward();
            }
        }
        public void MoveBackIfPrevious(char ch)
        {
            if (Prev == ch)
            {
                Backward();
            }
        }
        public void MoveForwardIfNotPrevious(char ch)
        {
            if (Prev != ch)
            {
                Forward();
            }
        }
        public void SkipAllIfNotIn(char toSkip, char[] to)
        {
            if (to.Contains(toSkip))
            {
                return;
            }
            while (StillValid() && Current == toSkip)
            {
                Forward();
            }
        }

        public List<Variable> GetFunctionArgs(char start = Constants.START_ARG,
                                      char end = Constants.END_ARG)
        {
            bool isList;
            List<Variable> args = Utils.GetArgs(this,
                                                start, end, (outList) => { isList = outList; } );
            return args;
        }
        public async Task<List<Variable>> GetFunctionArgsAsync(char start = Constants.START_ARG,
                                      char end = Constants.END_ARG)
        {
            bool isList;
            List<Variable> args = await Utils.GetArgsAsync(this,
                                                start, end, (outList) => { isList = outList; });
            return args;
        }

        public bool IsProcessingFunctionCall()
        {
            if (TryPrev() == Constants.START_ARG || TryCurrent() == Constants.START_ARG)
            {
                return true;
            }
            return false;
        }

        public int GoToNextStatement()
        {
            int endGroupRead = 0;
            while (StillValid())
            {
                char currentChar = Current;
                switch (currentChar)
                {
                    case Constants.END_GROUP:
                        endGroupRead++;
                        Forward();                  // '}'
                        return endGroupRead;
                    case Constants.START_GROUP:     // '{'
                    case Constants.QUOTE:           // '"'
                    case Constants.SPACE:           // ' '
                    case Constants.END_STATEMENT:   // ';'
                    case Constants.END_ARG:         // ')'
                        Forward();
                        break;
                    default: return endGroupRead;
                }
            }
            return endGroupRead;
        }

        public static Variable RunString(string str)
        {
            ParsingScript tempScript = new ParsingScript(str);
            Variable result = tempScript.Execute();
            return result;
        }

        public Variable Execute(char[] toArray = null, int from = -1)
        {
            toArray = toArray == null ? Constants.END_PARSE_ARRAY : toArray;
            Pointer = from < 0 ? Pointer : from;

            if (!m_data.EndsWith(Constants.END_STATEMENT.ToString()))
            {
                m_data += Constants.END_STATEMENT;
            }

            Variable result = null;

            bool handleByDebugger = DebuggerServer.DebuggerAttached && !Debugger.Executing;
            if (DebuggerServer.DebuggerAttached)
            {
                result = Debugger.CheckBreakpoints(this).Result;
                if (result != null)
                {
                    return result;
                }
            }

            if (InTryBlock)
            {
                result = Parser.SplitAndMerge(this, toArray);
            }
            else
            {
                try
                {
                    result = Parser.SplitAndMerge(this, toArray);
                }
                catch (ParsingException parseExc)
                {
                    if (handleByDebugger)
                    {
                        Debugger.ProcessException(this, parseExc);
                    }
                    throw;
                }
                catch (Exception exc)
                {
                    ParsingException parseExc = new ParsingException(exc.Message, this, exc);
                    if (handleByDebugger)
                    {
                        Debugger.ProcessException(this, parseExc);
                    }
                    throw parseExc;
                }
            }
            return result;
        }

        public async Task<Variable> ExecuteAsync(char[] toArray = null, int from = -1)
        {
            toArray = toArray == null ? Constants.END_PARSE_ARRAY : toArray;
            Pointer = from < 0 ? Pointer : from;

            if (!m_data.EndsWith(Constants.END_STATEMENT.ToString()))
            {
                m_data += Constants.END_STATEMENT;
            }

            Variable result = null;

            bool handleByDebugger = DebuggerServer.DebuggerAttached && !Debugger.Executing;
            if (DebuggerServer.DebuggerAttached)
            {
                result = await Debugger.CheckBreakpoints(this);
                if (result != null)
                {
                    return result;
                }
            }

            if (InTryBlock)
            {
                result = await Parser.SplitAndMergeAsync(this, toArray);
            }
            else
            {
                try
                {
                    result = await Parser.SplitAndMergeAsync(this, toArray);
                }
                catch (ParsingException parseExc)
                {
                    if (handleByDebugger)
                    {
                        Debugger.ProcessException(this, parseExc);
                    }
                    throw;
                }
                catch (Exception exc)
                {
                    ParsingException parseExc = new ParsingException(exc.Message, this, exc);
                    if (handleByDebugger)
                    {
                        Debugger.ProcessException(this, parseExc);
                    }
                    throw parseExc;
                }
            }
            return result;
        }

        public void ExecuteAll()
        {
            while (StillValid())
            {
                Execute(Constants.END_LINE_ARRAY);
                GoToNextStatement();
            }
        }

        public ParsingScript GetTempScript(string str, int startIndex = 0)
        {
            ParsingScript tempScript  = new ParsingScript(str, startIndex);
            tempScript.Filename       = this.Filename;
            tempScript.InTryBlock     = this.InTryBlock;
            tempScript.ParentScript   = this;
            tempScript.Char2Line      = this.Char2Line;
            tempScript.OriginalScript = this.OriginalScript;
            tempScript.InTryBlock     = this.InTryBlock;
            tempScript.StackLevel     = this.StackLevel;
            tempScript.AllLabels      = this.AllLabels;
            tempScript.LabelToFile    = this.LabelToFile;
            tempScript.FunctionName   = this.FunctionName;            

            //tempScript.Debugger       = this.Debugger;

            return tempScript;
        }

        public ParsingScript GetIncludeFileScript(string filename)
        {
            string pathname = GetFilePath(filename);
            string[] lines = Utils.GetFileLines(pathname);

            string includeFile = string.Join(Environment.NewLine, lines);
            Dictionary<int, int> char2Line;
            var includeScript = Utils.ConvertToScript(includeFile, out char2Line, pathname);
            ParsingScript tempScript = new ParsingScript(includeScript, 0, char2Line);
            tempScript.Filename = pathname;
            tempScript.OriginalScript = string.Join(Constants.END_LINE.ToString(), lines);
            tempScript.ParentScript = this;
            tempScript.InTryBlock = InTryBlock;

            return tempScript;
        }
    }

    public class ParsingException : Exception
    {
        public ParsingScript ExceptionScript { get; private set; }
        public string ExceptionStack { get; private set; } = "";

        public ParsingException(string message, string excStack = "")
            : base(message)
        {
            ExceptionStack = excStack.Trim();
        }
        public ParsingException(string message, ParsingScript script)
            : base(message)
        {
            ExceptionScript = script;
            ExceptionStack = script.GetStack(-1);
        }
        public ParsingException(string message, ParsingScript script, Exception inner)
            : base(message, inner)
        {
            ExceptionScript = script;
            ExceptionStack = script.GetStack(-1);
        }
    }
}
