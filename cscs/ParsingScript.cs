using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SplitAndMerge
{
  
  public class ParsingScript
  {
    private string m_data;          // contains the whole script
    private int m_from;             // a pointer to the script
    private string m_filename;      // filename containing the script
    private string m_originalScript;// original raw script
    private int m_scriptOffset = 0; // used in functiond defined in bigger scripts
    private Dictionary<int, int> m_char2Line = null; // pointers to the original lines

    public int Pointer {
      get { return m_from; }
      set { m_from = value; }
    }
    public string String {
      get { return m_data; }
    }
    public string Rest {
      get { return Substr(m_from, Constants.MAX_CHARS_TO_SHOW); }
    }
    public char Current {
      get { return m_data[m_from]; }
    }
    public Dictionary<int, int> Char2Line {
      get { return m_char2Line; }
      set { m_char2Line = value; }
    }
    public int ScriptOffset {
      set { m_scriptOffset = value; }
    }
    public string Filename {
      get { return m_filename; }
      set { m_filename = value; }
    }
    public string OriginalScript {
      get { return m_originalScript; }
      set { m_originalScript = value; }
    }

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
    }

    public int Size()        { return m_data.Length; }
    public bool StillValid() { return m_from < m_data.Length; }

    public void SetDone()    { m_from = m_data.Length; }

    public int Find(char ch, int from = -1) 
    { return m_data.IndexOf(ch, from < 0 ? m_from : from); }
  
    public int FindFirstOf(string str, int from = -1)
    { return FindFirstOf (str.ToCharArray (), from); }

    public int FindFirstOf(char[] arr, int from = -1)
    { return m_data.IndexOfAny (arr, from < 0 ? m_from : from); }

    public string Substr(int fr = -2, int len = -1) 
    {
      int from = Math.Min(Pointer, m_data.Length - 1);
      fr = fr == -2 ? from : fr == -1 ? 0 : fr;
      return len < 0 || len >= m_data.Length - fr ? m_data.Substring(fr) : m_data.Substring(fr, len);
    }

    public string GetOriginalLine(out int lineNumber)
    {
      lineNumber = GetOriginalLineNumber();
      if (lineNumber < 0) {
        return "";
      }

      string[] lines = m_originalScript.Split(Constants.END_LINE);
      if (lineNumber < lines.Length) {
        return lines[lineNumber];
      }

      return "";
    }

    public int GetOriginalLineNumber()
    {
      if (m_char2Line == null || m_char2Line.Count == 0) {
        return -1;
      }

      int pos = m_scriptOffset + m_from;
      List<int> lineStart = m_char2Line.Keys.ToList();
      int lower = 0;
      int index = lower;

      if (pos <= lineStart[lower]) { // First line.
        return m_char2Line[lineStart[lower]];
      }
      int upper = lineStart.Count - 1;
      if (pos >= lineStart[upper]) { // Last line.
        return m_char2Line[lineStart[upper]];
      }

      while (lower <= upper) {
        index = (lower + upper) / 2;
        int guessPos = lineStart[index];
        if (pos == guessPos) {
          break;
        }
        if (pos < guessPos) {
          if (index == 0 || pos > lineStart[index - 1]) {
            break;
          }
          upper = index - 1;
        } else {
          lower = index + 1;
        }
      }

      return m_char2Line[lineStart[index]];
    }
  
    public char At(int i)           { return m_data[i]; }  
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
  
    public string FromPrev(int backChars = 1, int maxChars = Constants.MAX_CHARS_TO_SHOW)
    {
      return Substr(m_from - backChars, maxChars);
    }
  
    public void Forward(int delta = 1)  { m_from += delta; }
    public void Backward(int delta = 1) { if (m_from >= delta) m_from -= delta; }

    public void MoveForwardIf(char[] arr)
    {
      foreach (char ch in arr) {
        if (MoveForwardIf(ch)) {
          return;
        }
      }
    }
    public bool MoveForwardIf(char expected, char expected2 = Constants.EMPTY)
    {
      if (StillValid() && (Current == expected || Current == expected2)) {
        Forward();
        return true;
      }
      return false;
    }
    public void MoveBackIf(char notExpected)
    {
      if (StillValid() && Pointer > 0 && Current == notExpected) {
        Backward ();
      }
    }
    public void SkipAllIfNotIn(char toSkip, char[] to)
    {
      if (to.Contains(toSkip)) {
        return;
      }
      while (StillValid () && Current == toSkip) {
        Forward ();
      }
    }

    public List<Variable> GetFunctionArgs(char start = Constants.START_ARG,
                                  char end   = Constants.END_ARG)
    {
      bool isList;
      List<Variable> args = Utils.GetArgs(this,
                                          start, end, out isList);
      return args;
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
            Forward();                    // '}'
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

    public Variable Execute()
    {
      return ExecuteFrom(Pointer);
    }
    public Variable ExecuteTo(char to = '\0')
    {
      return ExecuteFrom(Pointer, to);
    }
    public Variable ExecuteFrom(int from, char to = '\0')
    {
      Pointer = from;
      char[] toArray = to == '\0' ? Constants.END_PARSE_ARRAY :
                                    to.ToString().ToCharArray();
      return Execute(toArray);
    }
    public Variable Execute(char[] toArray)
    {
      if (!m_data.EndsWith(Constants.END_STATEMENT.ToString())) {
        m_data += Constants.END_STATEMENT;
      }
      return Parser.SplitAndMerge(this, toArray);
    }
    public void ExecuteAll()
    {
      while (StillValid()) {
        ExecuteTo(Constants.END_LINE);
        GoToNextStatement();
      }
    }
  }
}
