//#define MAIN_THREAD_CHECK

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
        public enum Action { NONE, FILE, NEXT, CONTINUE, STEP_IN, STEP_OUT, SET_BP, UNSET_BP };

        public static Debugger MainInstance { get; set; }
        public static Action<string> OnResult;

        static int m_id;
        static int m_startLine;
        static string m_startFilename;
        static bool m_firstBlock;

        public static bool CheckBreakpointsNeeded { get; private set; }
        public static bool Continue { get; private set; }
        public static bool SteppingIn { get; private set; }
        public static bool SteppingOut { get; private set; }
        public static bool Executing { get; private set; }
        public static Breakpoints TheBreakpoints { get { return MainInstance.m_breakpoints; } }

        public bool ContinueLocal { get; private set; }
        public bool InInclude { get; private set; }
        public int Id { get; private set; }
        public bool ProcessingBlock { get; set; }
        public bool End { get; set; }
        public bool ReplMode { get; set; }
        public bool SendBackResult { get; set; } = true;
        public string Output { get; set; } = "";
        public ParsingScript Script { get { return m_debugging; } }
        public Variable LastResult { get; set; }

        ParsingScript m_debugging;
        string m_script;

        Stack<Debugger> m_steppingIns = new Stack<Debugger>();
        AutoResetEvent m_completedStepIn = new AutoResetEvent(false);

        Dictionary<int, int> m_char2Line;
        Breakpoints m_breakpoints = new Breakpoints();

        public Debugger()
        {
            MainInstance = MainInstance == null ? this : MainInstance;
            Id = m_id++;
        }

        public void Trace(string msg = "")
        {
            string output = Output.Length > 22 ? Output.Substring(0, 10) + "..." +
                            Output.Substring(Output.Length - 10) : Output;
            output = output.Trim().Replace('\n', '_');
            msg = msg.Trim().Replace('\n', '_');
            if (msg.Length > 50)
            {
                //msg = msg.Substring(0, 50);
            }
            try
            {
                Console.WriteLine("==> {0}: In={1} Out={2} Cont={3} PB={4} SB={5} Stack={6} End={7} [{8}] {9}",
                                  Id, SteppingIn, SteppingOut, Continue, ProcessingBlock, SendBackResult,
                                  m_steppingIns.Count, End, output, msg);
            }
            catch (Exception exc)
            {
                Console.WriteLine(exc);
            }
        }

        public void ProcessClientCommands(string data)
        {
            string[] commands = data.Split(new char[] { '\n' });
            foreach (string dataCmd in commands)
            {
                if (!string.IsNullOrWhiteSpace(dataCmd))
                {
                    ProcessClientCommand(dataCmd);
                }
            }
        }

        void ProcessClientCommand(string data)
        {
            string[] parts = data.Split(new char[] { '|' });
            string cmd = parts[0].ToLower();
            string result = "N/A";
            SteppingIn = SteppingOut = false;
            SendBackResult = true;
            m_firstBlock = true;

            CheckBreakpointsNeeded = cmd == "continue" || cmd == "stepout";
            Trace("REQUEST: " + data);
            if (cmd == "repl" || cmd == "_repl")
            {
                result = ProcessRepl(data.Substring(cmd.Length + 1));
                SendBack(result);
                return;
            }
            if (cmd == "setbp")
            {
                TheBreakpoints.AddBreakpoints(this, data);
                return;
            }

            Action action = StringToAction(cmd);

            if (cmd == "file")
            {
                MainInstance = this;
                string filename = data.Substring(cmd.Length + 1);
                string rawScript = Utils.GetFileContents(filename);

                m_script = Utils.ConvertToScript(rawScript, out m_char2Line);
                m_debugging = new ParsingScript(m_script, 0, m_char2Line);
                m_debugging.Filename = filename;
                m_debugging.OriginalScript = m_script;
                m_debugging.Debugger = this;

            }
            else if (cmd == "vars")
            {
                result = GetVariables();
            }
            else if (cmd == "all")
            {
                result = DebugScript();
            }
            else if (cmd == "stack")
            {
                result = GetStack();
            }
            else if (cmd == "continue")
            {
                ContinueLocal = Continue = true;
                cmd = "next";
            }
            else if (cmd == "stepin")
            {
                SteppingIn = true;
                ContinueLocal = Continue = false;
                cmd = "next";
            }
            else if (cmd == "stepout")
            {
                SteppingOut = true;
                ContinueLocal = Continue = false;
                cmd = "next";
            }
            else if (cmd == "next")
            {
                ContinueLocal = Continue = false;
            }
            else
            {
                Console.WriteLine("UNKNOWN CMD: {0}", cmd);
                return;
            }

            if (cmd == "next")
            {
                if (m_debugging == null)
                {
                    result = "Error: Not initialized";
                    Console.WriteLine(result);
                }
                else
                {
                    string processedStr;
                    Variable res = ProcessNext(out processedStr);
                    Trace("MAIN Ret:" + (LastResult != null && LastResult.IsReturn) +
                          " NULL:" + (res == null) + " db.sb=" + m_debugging.Debugger.SendBackResult +
                          " PTR:" + m_debugging.Pointer + "/" + m_script.Length);
                    if (End)
                    {
                        cmd = "end";
                    }
                    else if (res == null || !SendBackResult)
                    {
                        // It will be processed by the stepped-out OR by completed stepped-in code.
                        return;
                    }
                    else
                    {
                        result = CreateResult(Output);
                    }
                }
            }

            result = cmd + "\n" + result;
            SendBack(result);
        }
        public string CreateResult(string output, ParsingScript script = null)
        {
            if (script == null)
            {
                script = m_steppingIns.Count > 0 ? m_steppingIns.Peek().m_debugging : m_debugging;
            }

            string filename = GetCurrentFilename(script);
            int lineNumber = GetCurrentLineNumber(script);

            int outputCount = output.Split('\n').Length;
            string result = filename + "\n";
            result += lineNumber + "\n";
            result += outputCount + "\n";
            result += output + "\n";

            string vars = GetVariables();
            int varsCount = vars.Split('\n').Length;
            result += varsCount + "\n";
            result += vars + "\n";

            string stack = GetStack(script);
            result += stack + "\n";

            return result;
        }

        string GetStack(ParsingScript script = null)
        {
            if (script == null)
            {
                script = m_steppingIns.Count > 0 ? m_steppingIns.Peek().m_debugging : m_debugging;
            }
            string stack = script.GetStack();
            return stack.Trim();
        }

        public void SendBack(string str)
        {
            Trace("SEND_BACK: " + str);
            OnResult?.Invoke(str);

            Output = "";
        }
        public void CreateResultAndSendBack(string cmd, string output, ParsingScript script = null)
        {
            string result = CreateResult(output, script);
            result = cmd + "\n" + result;
            SendBack(result);
        }

        string GetVariables()
        {
            string vars = ParserFunction.GetVariables();
            return vars;
        }

        int GetCurrentLineNumber(ParsingScript script)
        {
            if (script == m_debugging && !m_debugging.StillValid())
            {
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
                return -1;
#endif
                return -2;
            }
            return script.GetOriginalLineNumber();
        }
        string GetCurrentFilename(ParsingScript script)
        {
            string filename = Path.GetFullPath(script.Filename);
            return filename;
        }

        public static bool IsPureReplRequest(string data)
        {
            if (data.StartsWith("repl|"))
            {
                return true;
            }
            return false;
        }

        string ProcessRepl(string repl)
        {
            ReplMode = true;

            Dictionary<int, int> char2Line;
            string script = Utils.ConvertToScript(repl, out char2Line);
            ParsingScript tempScript = new ParsingScript(script, 0, char2Line);
            tempScript.OriginalScript = repl;
            tempScript.Debugger = this;

            Variable result = null;

            try
            {
                while (tempScript.Pointer < script.Length)
                {
                    Trace("REPL Starting Exec");
                    //result = tempScript.__Execute();
                    result = Execute(tempScript);
                    Trace("REPL Finished Exec");
                    tempScript.GoToNextStatement();
                }
            }
            catch (Exception exc)
            {
                return "Exception thrown: " + exc.Message;
            }
            finally
            {
                ReplMode = false;
            }

            string stringRes = Output + "\n";
            stringRes += result == null ? "" : result.AsString();

            return stringRes;
        }

        public bool CanProcess(string data)
        {
            if (MainInstance == null || MainInstance.m_steppingIns.Count == 0)
            {
                return false;
            }
            return true;
        }

        Variable ProcessNext(out string processed)
        {
            processed = "";
            if (MainInstance != null && MainInstance.m_steppingIns.Count > 0)
            {
                Debugger stepIn = MainInstance.m_steppingIns.Peek();
                stepIn.m_completedStepIn.Set();
                return null;
            }

            m_startFilename = m_debugging.Filename;
            m_startLine = m_debugging.OriginalLineNumber;

            ExecuteNext(out processed);
            return LastResult;
        }
        public bool ExecuteNext(out string processed)
        {
            processed = Output = "";
            int endGroupRead = 0;

            if (m_debugging.Pointer >= m_script.Length - 1)
            {
                LastResult = null;
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
                End = true;
                Trace("END!");
#endif
                return true;
            }

            int startPointer = m_debugging.Pointer;
            if (string.IsNullOrWhiteSpace(m_startFilename))
            {
                m_startFilename = m_debugging.Filename;
                m_startLine = m_debugging.OriginalLineNumber;
            }

            if (ProcessingBlock)
            {
                endGroupRead = m_debugging.GoToNextStatement();
                if (ProcessingBlock && endGroupRead > 0)
                {
                    return true;
                }
            }

            Executing = true;
            try
            {
                //LastResult = m_debugging.__Execute();
                LastResult = Execute(m_debugging);
            }
            catch (ParsingException exc)
            {
                ProcessException(m_debugging, exc);
                return true;
            }
            finally
            {
                Executing = false;
            }

            endGroupRead = m_debugging.GoToNextStatement();

            //int endPointer = m_debugging.Pointer;
            //processed = m_debugging.Substr(startPointer, endPointer - startPointer);

            return Completed(m_debugging) || (ProcessingBlock && endGroupRead > 0);
        }

        public Variable Execute(ParsingScript script)
        {
            char[] toArray = Constants.END_PARSE_ARRAY;
            Variable result = null;
            Exception exception = null;
#if UNITY_EDITOR || UNITY_STANDALONE || MAIN_THREAD_CHECK
            // Do nothing: already on the main thread
#elif __ANDROID__
            scripting.Droid.MainActivity.TheView.RunOnUiThread(() => {
#elif __IOS__
            scripting.iOS.AppDelegate.GetCurrentController().InvokeOnMainThread(() =>
            {
#else
#endif
                try
                {
                    result = script.Execute(toArray);
                }
                catch (ParsingException exc)
                {
                    exception = exc;
                }

#if UNITY_EDITOR || UNITY_STANDALONE || MAIN_THREAD_CHECK
            // Do nothing: already on the main thread
#elif __ANDROID__ || __IOS__
            });
#endif

            if (exception != null)
            {
                throw exception;
            }
            return result;
        }

        public static void ProcessException(ParsingScript script, ParsingException exc)
        {
            Debugger debugger = script.Debugger != null ? script.Debugger : MainInstance;
            if (debugger == null)
            {
                return;
            }

            string stack = exc.ExceptionStack;
            string vars = debugger.GetVariables();
            int varsCount = vars.Split('\n').Length;

            string result = "exc\n" + exc.Message + "\n";
            result += varsCount + "\n";
            result += vars + "\n";
            result += stack + "\n";

            debugger.SendBack(result);
            debugger.LastResult = null;

            ParserFunction.InvalidateStacksAfterLevel(0);
        }

        bool Completed(ParsingScript debugging)
        {
            return (LastResult != null && LastResult.IsReturn) ||
                   !debugging.StillValid();
        }

        public static Variable CheckBreakpoints(ParsingScript stepInScript)
        {
            var debugger = stepInScript.Debugger != null ? stepInScript.Debugger : MainInstance;
            if (debugger == null)
            {
                return null;
            }
            return debugger.StepInBreakpointIfNeeded(stepInScript);
        }

        public Variable DebugBlockIfNeeded(ParsingScript stepInScript, ref bool done)
        {
            if (SteppingOut || Continue || ReplMode || !m_firstBlock)
            {
                ContinueLocal = true;
                return null;
            }
            m_firstBlock = false;
            done = stepInScript.GoToNextStatement() > 0;
            if (done) {
                return Variable.EmptyInstance;
            }

            ProcessingBlock = true;

            ParsingScript tempScript = new ParsingScript(stepInScript.String, stepInScript.Pointer);
            tempScript.ParentScript = stepInScript;
            tempScript.InTryBlock = stepInScript.InTryBlock;
            string body = Utils.GetBodyBetween(tempScript, Constants.START_GROUP, Constants.END_GROUP);

            Trace("Started ProcessBlock");
            StepIn(stepInScript);

            ProcessingBlock = false;
            done = stepInScript.Pointer >= tempScript.Pointer;

            Trace("Finished ProcessBlock");
            return LastResult;
        }
        public Variable StepInFunctionIfNeeded(ParsingScript stepInScript)
        {
            stepInScript.Debugger = this;
            m_firstBlock = false;
            if (ReplMode || !SteppingIn)
            {
                ContinueLocal = true;
                return null;
            }

            Trace("Starting StepIn");
            StepIn(stepInScript);

            Trace("Finished StepIn");
            return LastResult;
        }

        public Variable StepInBreakpointIfNeeded(ParsingScript stepInScript)
        {
            stepInScript.Debugger = this;
            if (ReplMode)
            {
                return null;
            }

            int startPointer = stepInScript.Pointer;
            string filename = stepInScript.Filename;
            int line = stepInScript.OriginalLineNumber;

            if (filename == m_startFilename && line == m_startLine)
            {
                return null;
            }
            if (!TheBreakpoints.BreakpointExists(stepInScript))
            {
                return null;
            }

            m_startFilename = filename;
            m_startLine = line;

            Trace("Starting StepInBreakpoint");
            StepIn(stepInScript);

            Trace("Finished StepInBreakpoint");
            SendBackResult = true;
            return LastResult;
        }

        public Variable StepInIncludeIfNeeded(ParsingScript stepInScript)
        {
            stepInScript.Debugger = this;
            if (ReplMode || !SteppingIn)
            {
                ContinueLocal = true;
                return null;
            }
            m_firstBlock = false;

            Trace("Starting StepInInclude");
            StepIn(stepInScript);

            Trace("Finished StepInInclude");
            SendBackResult = true;
            return LastResult;
        }

        public void PostProcessCustomFunction(ParsingScript customScript)
        {
            Output = customScript.Debugger.Output;
        }
        public void AddOutput(string output, ParsingScript script)
        {
            if (!string.IsNullOrEmpty(Output) && !Output.EndsWith("\n"))
            {
                Output += "\n";
            }
            if (ReplMode)
            {
                Output += output;
                return;
            }
            int origLineNumber = script.GetOriginalLineNumber();
            string filename = Path.GetFullPath(script.Filename);
            Output += origLineNumber + "\t" + filename + "\n";
            Output += output;//.Replace('\n', ' ');
        }

        void StepIn(ParsingScript stepInScript)
        {
            Debugger stepIn = new Debugger();
            stepIn.m_debugging = stepInScript;
            stepIn.m_script = stepInScript.String;
            stepIn.ProcessingBlock = ProcessingBlock;
            stepInScript.Debugger = stepIn;

            MainInstance?.m_steppingIns.Push(stepIn);

            stepIn.Trace("Started StepIn, this: " + Id);
            CreateResultAndSendBack("next", Output, stepInScript);

            string processed;
            bool done = false;
            while (!done)
            {
                stepIn.m_completedStepIn.WaitOne();

                stepIn.Trace("StepIn WakedUp. SteppingOut:" + SteppingOut + ", this: " + Id);
                if (Debugger.SteppingOut)
                {
                    break;
                }
                stepIn.Output = "";
                m_startFilename = null;
                done = stepIn.ExecuteNext(out processed);

                if (stepIn.LastResult == null)
                {
                    continue;
                }

                LastResult = stepIn.LastResult;
                Output = stepIn.Output;

                if (!done)
                {
                    stepIn.Trace("Completed one StepIn, this: " + Id);
                    CreateResultAndSendBack("next", Output, stepInScript);
                }
            }

            //m_startFilename = stepIn.m_debugging.Filename;
            //m_startLine = stepIn.m_debugging.OriginalLineNumber;

            MainInstance?.m_steppingIns.Pop();
            stepIn.Trace("Finished StepIn, this: " + Id);
        }

        string DebugScript()
        {
            if (string.IsNullOrWhiteSpace(m_script))
            {
                return null;
            }

            m_debugging = new ParsingScript(m_script, 0, m_char2Line);
            m_debugging.OriginalScript = m_script;

            string result = null;
            string processed;
            while (m_debugging.Pointer < m_script.Length)
            {
                result = ProcessNext(out processed).AsString();
                Console.WriteLine("{0} --> {1}", processed, result);
            }

            return result;
        }

        public static Action StringToAction(string str)
        {
            switch (str)
            {
                case "next": return Action.NEXT;
                case "continue": return Action.CONTINUE;
                case "stepin": return Action.STEP_IN;
                case "stepout": return Action.STEP_OUT;
                case "file": return Action.FILE;
                case "setbp": return Action.SET_BP;
                case "unsetbp": return Action.UNSET_BP;
            }

            return Action.NONE;
        }
    }
}
