using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.IO;

namespace SplitAndMerge
{
  public class Debugger
  {
    Debugger m_mainInstance;
    public static Action<string> OnResult;

    static int m_id;

    ParsingScript m_debugging;
    string m_filename;
    string m_script;

    public bool Continue        { get; private set; }
    public bool InInclude       { get; private set; }
    public bool SteppingIn      { get; private set; }
    public bool SteppingOut     { get; private set; }
    public int  Id              { get; private set; }
    public bool ProcessingBlock { get; set; }
    public bool End             { get; set; }
    public bool ReplMode        { get; set; }
    public bool SendBackResult  { get; set; } = true;
    public string Output        { get; set; } = "";

    Stack<Debugger> m_steppingIns = new Stack<Debugger>();
    AutoResetEvent m_completedStepIn = new AutoResetEvent(false);
    Variable m_lastResult;

    Dictionary<int, int> m_char2Line;

    public Debugger (Debugger mainInstance = null)
    {
      m_mainInstance = mainInstance == null ? this : mainInstance;
      Id = m_id++;
    }

    public void Trace(string msg = "")
    {
      string output = Output.Length > 22 ? Output.Substring(0, 10) + "..." +
                      Output.Substring(Output.Length - 10) : Output;
      output = output.Trim().Replace('\n', '_');
      msg = msg.Trim().Replace ('\n', '_');
      if (msg.Length > 50) {
        //msg = msg.Substring(0, 50);
      }
      Console.WriteLine("==> {0}: In={1} Out={2} Cont={3} PB={4} SB={5} Stack={6} End={7} [{8}] {9}",
                        Id, SteppingIn, SteppingOut, Continue, ProcessingBlock, SendBackResult,
                        m_steppingIns.Count, End, output, msg);
    }

    public void ProcessClientCommands(string data)
    {
      string [] commands = data.Split (new char [] { '\n' });
      foreach (string dataCmd in commands) {
        if (!string.IsNullOrWhiteSpace(dataCmd)) {
          ProcessClientCommand(dataCmd);
        }
      }
    }

    void ProcessClientCommand(string data)
    {
      string [] parts = data.Split(new char [] { '|' });
      string cmd = parts[0].ToLower();
      string result = "N/A";
      SteppingIn = SteppingOut = false;
      SendBackResult = true;

      Trace ("REQUEST: " + data);

      if (cmd == "file") {
        m_filename = data.Substring(cmd.Length + 1);
        string rawScript = Utils.GetFileContents(m_filename);

        m_script = Utils.ConvertToScript(rawScript, out m_char2Line);
        m_debugging = new ParsingScript (m_script, 0, m_char2Line);
        m_debugging.Filename = m_filename;
        m_debugging.OriginalScript = m_script;
        m_debugging.Debugger = this;

      } else if (cmd == "vars") {
        result = GetVariables ();

      } else if (cmd == "repl") {
        result = ProcessRepl(data.Substring (cmd.Length + 1));
        SendBack(result);
        return;

      } else if (cmd == "all") {
        result = DebugScript();

      } else if (cmd == "stack") {
        result = GetStack();

      } else if (cmd == "continue") {
        Continue = true;
        cmd = "next";

      } else if (cmd == "stepin") {
        SteppingIn = true;
        cmd = "next";

      } else if (cmd == "stepout") {
        SteppingOut = true;
        cmd = "next";

      } else if (cmd != "next") {
        Console.WriteLine("UNKNOWN CMD: {0}", cmd);
        return;
      }

      if (cmd == "next") {
        if (m_debugging == null) {
          result = "Error: Not initialized";
          Console.WriteLine (result);

        } else {
          string processedStr;
          Variable res = ProcessNext(out processedStr);
          Trace("MAIN Ret:" + (m_lastResult != null && m_lastResult.IsReturn)+
                " NULL:" + (res == null)+" db.sb="+m_debugging.Debugger.SendBackResult+
                " PTR:" + m_debugging.Pointer + "/" + m_script.Length);
          if (End) {
            cmd = "end";
          } else if (res == null || !SendBackResult) {
            // It will be processed by the stepped-out OR by completed stepped-in code.
            return;
          } else {
            int origLineNumber = GetCurrentLineNumber();
            string filename = GetCurrentFilename ();
            result = CreateResult (filename, origLineNumber, Output, processedStr);
          }
        }
      }

      result = cmd + "\n" + result;
      SendBack(result);
    }
    string CreateResult(string filename, int lineNumber, string output, string processed = "")
    {
      int outputCount = output.Split('\n').Length;
      string result = filename + "\n";
      result += lineNumber + "\n";
      result += outputCount + "\n";
      result += output + "\n";

      string vars = GetVariables();
      int varsCount = vars.Split('\n').Length;
      result += varsCount + "\n";
      result += vars + "\n";

      string stack = GetStack();
      result += stack + "\n";

      return result;
    }

    void SendBack(string str)
    {
      Trace("SEND_BACK: " + str);
      OnResult?.Invoke(str);

      Output = "";
    }

    string GetVariables()
    {
      string vars = ParserFunction.GetVariables();
      return vars;
    }

    int GetCurrentLineNumber()
    {
      ParsingScript debugging =  m_steppingIns.Count > 0 ? m_steppingIns.Peek().m_debugging : m_debugging;
      if (debugging == m_debugging && !m_debugging.StillValid()) {
        return -1;
      }
      return debugging.GetOriginalLineNumber();
    }
    string GetCurrentFilename()
    {
      ParsingScript debugging = m_steppingIns.Count > 0 ? m_steppingIns.Peek ().m_debugging : m_debugging;
      string filename = Path.GetFullPath(debugging.Filename);
      return filename;
    }
    string GetStack()
    {
      ParsingScript debugging = m_steppingIns.Count > 0 ? m_steppingIns.Peek ().m_debugging : m_debugging;
      string stack = debugging.GetStack();
      return stack.Trim();
    }

    string ProcessRepl(string repl)
    {
      ReplMode = true;

      Dictionary<int, int> char2Line;
      string script = Utils.ConvertToScript(repl, out char2Line);
      ParsingScript tempScript = new ParsingScript (script, 0, char2Line);
      tempScript.OriginalScript = repl;
      tempScript.Debugger = this;

      Variable result = null;

      try {
        while (tempScript.Pointer < script.Length) {
          result = tempScript.ExecuteTo();
          tempScript.GoToNextStatement();
        }
      } catch (Exception exc) {
        return "Exception thrown: " + exc.Message;
      }

      string stringRes = Output + "\n";
      stringRes += result == null ? "" : result.AsString();

      return stringRes;    
    }

    Variable ProcessNext(out string processed)
    {
      processed = "";
      try {
        if (m_mainInstance.m_steppingIns.Count > 0) {
          // Somewhere down the stack
          Debugger stepIn = m_mainInstance.m_steppingIns.Peek ();
          if (SteppingOut) {
            Trace ("Completing SteppingOut");
            stepIn.m_completedStepIn.Set ();
            m_mainInstance.m_steppingIns.Pop ();
            return null;
          }
          stepIn.SteppingIn = SteppingIn;
          stepIn.SteppingOut = SteppingOut;
          bool done = stepIn.ExecuteNext(out processed);
          m_lastResult = stepIn.m_lastResult;
          Output = stepIn.Output;
          stepIn.Output = "";
          if (done) {
            //SendBackResult = m_lastResult != null && m_lastResult.IsReturn;
            //stepIn.SendBackResult = !SendBackResult;
            Trace ("Done Processing. Result Done: " + (m_lastResult != null && m_lastResult.IsReturn));
            stepIn.m_completedStepIn.Set ();
            m_mainInstance.m_steppingIns.Pop ();
            return null;
          }
          return m_lastResult;
        }

        ExecuteNext (out processed);
        return m_lastResult;

      } catch (ParsingException exc) {
        string stack = exc.ExceptionStack;
        string vars = GetVariables ();
        int varsCount = vars.Split ('\n').Length;

        string result = "exc\n" + exc.Message + "\n";
        result += varsCount + "\n";
        result += vars + "\n";
        result += stack + "\n";

        SendBack(result);

        ParserFunction.InvalidateStacksAfterLevel(0);
        return null;
      }
    }
    bool ExecuteNext(out string processed)
    {
      string rest = m_debugging.Rest;
      processed = Output = "";

      if (m_debugging.Pointer >= m_script.Length - 1) {
        m_lastResult = null;
        End = true;
        Trace("END!");
        return true;
      }

      int startPointer = m_debugging.Pointer;

      bool done = false;
      if (ProcessingBlock) {
        int endGroupRead = m_debugging.GoToNextStatement ();
        done = endGroupRead > 0;
      }
      if (!done) {
        m_lastResult = m_debugging.ExecuteTo();
        m_debugging.GoToNextStatement();
      }

      int endPointer = m_debugging.Pointer;
      processed = m_debugging.Substr (startPointer, endPointer - startPointer);

      if (m_lastResult == null) {
        Console.WriteLine ("NULL: {0}", processed);
      }

      return done || Completed(m_debugging);
    }

    bool Completed(ParsingScript debugging)
    {
      return (m_lastResult != null && m_lastResult.IsReturn) ||
             !debugging.StillValid();
    }

    public Variable DebugBlockIfNeeded(ParsingScript stepInScript)
    {
      if (SteppingOut || Continue || ReplMode) {
        Continue = true;
        return null;
      }
      ProcessingBlock = true;
      stepInScript.GoToNextStatement ();

      Trace ("Started ProcessBlock");
      StepIn (stepInScript);

      ProcessingBlock = false;
      Trace ("Finished ProcessBlock");
      return m_lastResult;
    }
    public Variable StepInIfNeeded(ParsingScript stepInScript)
    {
      stepInScript.Debugger = this;
      if (!SteppingIn || ReplMode) {
        Continue = true;
        return null;
      }

      Trace("Starting StepIn");
      StepIn (stepInScript);

      Trace ("Finished StepIn");
      return m_lastResult;
    }
    public Variable StepInIncludeIfNeeded(ParsingScript stepInScript)
    {
      stepInScript.Debugger = this;
      if (!SteppingIn || ReplMode) {
        Continue = true;
        return null;
      }

      Trace ("Starting StepInInclude");
      StepIn(stepInScript);

      Trace ("Finished StepInInclude");
      SendBackResult = true;
      return m_lastResult;
    }
    public void PostProcessCustomFunction(ParsingScript customScript)
    {
      Output = customScript.Debugger.Output;
    }
    public void AddOutput(string output, ParsingScript script)
    {
      if (!string.IsNullOrEmpty(Output) && !Output.EndsWith("\n")) {
        Output += "\n";
      }
      if (ReplMode) {
        Output += output;
        return;
      }
      int origLineNumber = script.GetOriginalLineNumber ();
      string filename = Path.GetFullPath(script.Filename);
      Output += origLineNumber + "\t" + filename + "\n";
      Output += output;//.Replace('\n', ' ');
    }

    void StepIn(ParsingScript stepInScript)
    {
      Debugger stepIn = new Debugger(m_mainInstance);
      stepIn.m_debugging = stepInScript;
      stepIn.m_script = stepInScript.String;
      stepIn.ProcessingBlock = ProcessingBlock;
      stepInScript.Debugger = stepIn;

      m_mainInstance.m_steppingIns.Push(stepIn);

      int origLineNumber = stepInScript.GetOriginalLineNumber();
      string filename = Path.GetFullPath (stepInScript.Filename);

      string result = CreateResult(filename, origLineNumber, Output);
      result = "next" + "\n" + result;
      stepIn.Trace("Started StepIn, this: " + Id);
      SendBack(result);

      stepIn.m_completedStepIn.WaitOne();
      stepIn.Continue = m_mainInstance.Continue;
      stepIn.SteppingOut = m_mainInstance.SteppingOut;
      stepIn.Trace ("Finished StepIn, this: " + Id);
      m_lastResult = stepIn.m_lastResult;
    }

    string DebugScript ()
    {
      if (string.IsNullOrWhiteSpace (m_script)) {
        return null;
      }

      m_debugging = new ParsingScript (m_script, 0, m_char2Line);
      m_debugging.OriginalScript = m_script;

      string result = null;
      string processed;
      while (m_debugging.Pointer < m_script.Length) {
        result = ProcessNext(out processed).AsString ();
        Console.WriteLine ("{0} --> {1}", processed, result);
      }

      return result;
    }
  }
}
