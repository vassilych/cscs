using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public class Debugger
    {
        public static Debugger MainInstance { get; set; }
        public static Action<string, bool> OnResult;
        public static Action<string, string> OnSendFile;

        static int m_id;
        static string m_startFilename;
        static int m_startLine;
        static bool m_firstBlock;
        static int m_blockLevel;
        static int m_maxBlockLevel;

        public static bool CheckBreakpointsNeeded { get; private set; }
        public static bool Continue { get; private set; }
        public static bool SteppingIn { get; private set; }
        public static bool SteppingOut { get; private set; }
        public static bool Executing { get; private set; }
        public static bool ProcessingClientRequest { get; private set; }
        public static Breakpoints TheBreakpoints
        { get { return (MainInstance == null ? Breakpoints.Instance : MainInstance.m_breakpoints); } }

        public bool InInclude { get; private set; }
        public int Id { get; private set; }
        public bool ProcessingBlock { get; set; }
        public bool End { get; set; }
        public bool ReplMode { get; set; }
        public bool SendBackResult { get; set; } = true;
        public static string Output { get; set; } = "";
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
                msg = msg.Substring(0, 50);
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

        public static Action<Interpreter> InterpreterNotification { get; set; }
        static Interpreter m_interpreter;
        public static Interpreter TheInterpreter {
            get
            {
                if (m_interpreter == null)
                {
                    m_interpreter = Interpreter.LastInstance;
                }
                return m_interpreter;
            }
            private set
            {
                m_interpreter = value;
            }
        }
        public static void SetInterpreter(Interpreter interpreter)
        {
            TheInterpreter = interpreter;
        }

        public async Task ProcessClientCommands(string data)
        {
            string[] commands = data.Split(new char[] { '\n' });
            foreach (string dataCmd in commands)
            {
                var cmd = dataCmd.Replace("\r", "\n").Trim();
                if (!string.IsNullOrWhiteSpace(cmd))
                {
                    await ProcessClientCommand(cmd);
                }
            }
        }

        async Task ProcessClientCommand(string data)
        {
            string load = "";
            DebuggerUtils.DebugAction action = DebuggerUtils.StringToAction(data, ref load);
            string result = "N/A";
            SteppingIn = SteppingOut = false;
            SendBackResult = true;
            m_firstBlock = true;
            End = false;
            string responseToken = DebuggerUtils.ResponseMainToken(action);

            //Trace("REQUEST: " + data);
            if (action == DebuggerUtils.DebugAction.REPL ||
                action == DebuggerUtils.DebugAction._REPL)
            {
                string filename = "";
                if (action == DebuggerUtils.DebugAction.REPL)
                {
                    int ind = load.IndexOf('|');
                    if (ind >= 0)
                    {
                        if (ind > 0)
                        {
                            filename = load.Substring(0, ind);
                        }
                        if (ind + 1 < load.Length)
                        {
                            load = load.Substring(ind + 1);
                        }
                    }
                }
                result = await ProcessRepl(load, filename);
                result = responseToken + (result == null ? "" : result);
                SendBack(result, false);
                return;
            }
            if (action == DebuggerUtils.DebugAction.SET_BP)
            {
                TheBreakpoints.AddBreakpoints(this, load);
                return;
            }

            if (action == DebuggerUtils.DebugAction.FILE)
            {
                MainInstance = this;
                string filename = load;
                string rawScript = Utils.GetFileContents(filename);

                if (string.IsNullOrWhiteSpace(rawScript))
                {
                    ProcessException(null, new ParsingException("Could not load script " + filename));
                    return;
                }

                try
                {
                    m_script = Utils.ConvertToScript(TheInterpreter, rawScript, out m_char2Line, filename);
                }
                catch(ParsingException exc)
                {
                    ProcessException(m_debugging, exc);
                    return;
                }
                m_debugging = new ParsingScript(TheInterpreter, m_script, 0, m_char2Line);
                m_debugging.Filename = filename;
                m_debugging.MainFilename = m_debugging.Filename;
                m_debugging.OriginalScript = rawScript;
                m_debugging.Debugger = this;

                m_steppingIns.Clear();
                m_completedStepIn.Reset();
                ProcessingBlock = SendBackResult = InInclude = End =  false;      
                m_blockLevel = m_maxBlockLevel = 0;
            }
            else if (action == DebuggerUtils.DebugAction.VARS)
            {
                result = GetAllVariables(m_debugging);
            }
            else if (action == DebuggerUtils.DebugAction.ALL)
            {
                result = await DebugScript();
            }
            else if (action == DebuggerUtils.DebugAction.STACK)
            {
                result = GetStack();
            }
            else if (action == DebuggerUtils.DebugAction.CONTINUE)
            {
                Continue = true;
                action = DebuggerUtils.DebugAction.NEXT;
            }
            else if (action == DebuggerUtils.DebugAction.STEP_IN)
            {
                SteppingIn = true;
                Continue = false;
                action = DebuggerUtils.DebugAction.NEXT;
            }
            else if (action == DebuggerUtils.DebugAction.STEP_OUT)
            {
                SteppingOut = true;
                Continue = false;
                action = DebuggerUtils.DebugAction.NEXT;
            }
            else if (action == DebuggerUtils.DebugAction.NEXT)
            {
                Continue = false;
            }
            else
            {
                Console.WriteLine("UNKNOWN CMD: {0}", data);
                return;
            }

            if (action == DebuggerUtils.DebugAction.NEXT)
            {
                if (m_debugging == null)
                {
                    result = "Error: Not initialized";
                    Console.WriteLine(result);
                }
                else
                {
                    Variable res = await ProcessNext();
                    //Trace("MAIN Ret:" + (LastResult != null && LastResult.IsReturn) +
                    //      " NULL:" + (res == null) + " db.sb=" + m_debugging.Debugger.SendBackResult +
                    //      " PTR:" + m_debugging.Pointer + "/" + m_script.Length);
                    if (End)
                    {
                        responseToken = DebuggerUtils.ResponseMainToken(DebuggerUtils.DebugAction.END);
                    }
                    else if (res == null || !SendBackResult)
                    {
                        // It will be processed by the stepped-out OR by completed stepped-in code.
                        return;
                    }
                    else
                    {
                        result = CreateResult(Output);
                        if (string.IsNullOrEmpty(result))
                        {
                            return;
                        }
                    }
                }
            }

            result = responseToken + result;
            SendBack(result, true);
        }

        bool TrySendFile(Variable result, ParsingScript script, ref bool excThrown)
        {
            if (result != null &&
                result.Type == Variable.VarType.ARRAY &&
                result.Tuple.Count >= 3 &&
                result.Tuple[0].AsString() == Constants.GET_FILE_FROM_DEBUGGER)
            {
                try
                {
                    OnSendFile?.Invoke(result.Tuple[1].AsString(), result.Tuple[2].AsString());
                }
                catch (Exception exc)
                {
                    ProcessException(m_debugging, new ParsingException(exc.Message, script, exc));
                    excThrown = true;
                }
                return true;
            }
            return false;
        }

        public string CreateResult(string output, ParsingScript script = null)
        {
            if (script == null)
            {
                script = m_steppingIns.Count > 0 ? m_steppingIns.Peek().m_debugging : m_debugging;
            }

            bool excThrown = false;
            if (TrySendFile(LastResult, script, ref excThrown) && excThrown)
            {
                return "";
            }

            string filename = GetCurrentFilename(script);
            int lineNumber = GetCurrentLineNumber(script);

            int outputCount = output.Split('\n').Length;
            string result = filename + "\n";
            result += lineNumber + "\n";
            result += outputCount + "\n";
            result += output + "\n";

            string vars = GetAllVariables(script);
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

        public void SendBack(string str, bool sendLength = true)
        {
            OnResult?.Invoke(str, sendLength);

            Output = "";
            m_startFilename = null;
            m_startLine = 0;
            ProcessingClientRequest = false;
        }
        public void CreateResultAndSendBack(string cmd, string output, ParsingScript script = null)
        {
            string result = CreateResult(output, script);
            if (string.IsNullOrEmpty(result))
            {
                return;
            }
            result = cmd + "\n" + result;
            SendBack(result, true);
        }

        string GetAllVariables(ParsingScript script)
        {
            string vars = TheInterpreter.GetVariables(script);
            return vars;
        }

        int GetCurrentLineNumber(ParsingScript script)
        {
            if (script == m_debugging && !m_debugging.StillValid())
            {
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
                return -1;
#else
                return -2;
#endif
            }
            return script.GetOriginalLineNumber();
        }
        string GetCurrentFilename(ParsingScript script)
        {
            string filename = Path.GetFullPath(script.Filename);
            return filename;
        }

        async Task<string> ProcessRepl(string repl, string filename = "")
        {
            ReplMode = true;

            Dictionary<int, int> char2Line;
            string script = Utils.ConvertToScript(TheInterpreter, repl, out char2Line);
            ParsingScript tempScript = new ParsingScript(TheInterpreter, script, 0, char2Line);
            tempScript.OriginalScript = repl;
            tempScript.Debugger = this;
            if (!string.IsNullOrWhiteSpace(filename))
            {
                tempScript.Filename = filename;
            }

            Variable result = null;
            bool excThrown = false;
            string stringRes = "";

            try
            {
                while (tempScript.Pointer < script.Length)
                {
                    result = await DebuggerUtils.Execute(tempScript);
                    tempScript.GoToNextStatement();
                    while (tempScript.TryCurrent() == Constants.END_STATEMENT)
                    {
                        tempScript.Forward();
                    }
                }

                if (TrySendFile(result, tempScript, ref excThrown) || excThrown)
                {
                    return "";
                }

                stringRes = string.IsNullOrEmpty(Output) ? "" : Output + (Output.EndsWith("\n") ? "" : "\n");

                stringRes += result == null ? "" : result.AsString();
                stringRes += (stringRes.EndsWith("\n") ? "" : "\n");
            }
            catch (Exception exc)
            {
                Console.WriteLine("ProcessRepl Exception: " + exc);
                return ""; // The exception was already thrown and sent back.
            }
            finally
            {
                ReplMode = false;
            }


            return stringRes;
        }

        public bool CanProcess(DebuggerUtils.DebugAction action)
        {
            if (action == DebuggerUtils.DebugAction.FILE ||
                action == DebuggerUtils.DebugAction.SET_BP ||
                (MainInstance != null && MainInstance.m_steppingIns.Count > 0))
            {
                return true;
            }
            return false;
        }

        async Task<Variable> ProcessNext()
        {
            ProcessingClientRequest = true;
            if (MainInstance != null && MainInstance.m_steppingIns.Count > 0)
            {
                Debugger stepIn = MainInstance.m_steppingIns.Peek();
                m_startFilename = stepIn.m_debugging.Filename;
                m_startLine     = stepIn.m_debugging.OriginalLineNumber;
                stepIn.m_completedStepIn.Set();
                return null;
            }

            m_startFilename = m_debugging.Filename;
            m_startLine = m_debugging.OriginalLineNumber;

            await ExecuteNextStatement();
            return LastResult;
        }

        public async Task<bool> ExecuteNextStatement()
        {
            int endGroupRead = 0;

            if (m_debugging.Pointer >= m_script.Length - 1)
            {
                LastResult = null;
                End = true;
                return true;
            }

            //int startPointer = m_debugging.Pointer;
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
                LastResult = await DebuggerUtils.Execute(m_debugging);
            }
            catch (ParsingException exc)
            {
                if (m_debugging.InTryBlock)
                {
                    throw exc;
                }
                ProcessException(m_debugging, exc);
                return true;
            }
            finally
            {
                Executing = false;
            }

            endGroupRead = m_debugging.GoToNextStatement();

            // Check if after this statement the Step In is completed and we can unwind the stack:
            bool completedSteppingIn = Completed(m_debugging) || (ProcessingBlock && endGroupRead > 0) ||
                                       LastResult.Type == Variable.VarType.CONTINUE ||
                                       LastResult.Type == Variable.VarType.BREAK ||
                                       LastResult.IsReturn;

            return completedSteppingIn;
        }

        public static void ProcessException(ParsingScript script, ParsingException exc)
        {
            Debugger debugger = script != null && script.Debugger != null ?
                                script.Debugger : MainInstance;
            if (debugger == null)
            {
                return;
            }

            if (debugger.ReplMode)
            {
                string replResult = DebuggerUtils.ResponseMainToken(DebuggerUtils.DebugAction.REPL) +
                                    "Exception thrown: " + exc.Message + "\n";
                debugger.SendBack(replResult, false);
                debugger.LastResult = null;
                TheInterpreter.InvalidateStacksAfterLevel(0);
                return;
            }

            string stack = exc.ExceptionStack;
            string vars = debugger.GetAllVariables(script);
            int varsCount = vars.Split('\n').Length;

            string result = "exc\n" + exc.Message + "\n";
            //result += exc. + "\n";
            result += varsCount + "\n";
            result += vars + "\n";
            result += stack + "\n";

            debugger.SendBack(result, !debugger.ReplMode);
            debugger.LastResult = null;

            TheInterpreter.InvalidateStacksAfterLevel(0);
        }

        bool Completed(ParsingScript debugging)
        {
            return (LastResult != null && LastResult.IsReturn) ||
                   !debugging.StillValid();
        }

        public static async Task<Variable> CheckBreakpoints(ParsingScript stepInScript)
        {
            var debugger = stepInScript.Debugger != null ? stepInScript.Debugger : MainInstance;
            if (debugger == null || stepInScript.DisableBreakpoints)
            {
                return null;
            }
            if (!ProcessingClientRequest)
            {
                m_startFilename = null;
                m_startLine = 0;
            }
            return await debugger.StepInBreakpointIfNeeded(stepInScript);
        }

        public async Task<Variable> DebugBlockIfNeeded(ParsingScript stepInScript, bool done, Action<bool> doneEvent)
        {
            if (SteppingOut || Continue || ReplMode || !m_firstBlock)
            {
                return null;
            }
            m_firstBlock = false;
            done = stepInScript.GoToNextStatement() > 0;
            if (done)
            {
                return Variable.EmptyInstance;
            }

            ProcessingBlock = true;

            ParsingScript tempScript = new ParsingScript(TheInterpreter, stepInScript.String, stepInScript.Pointer);
            tempScript.ParentScript = stepInScript;
            tempScript.InTryBlock = stepInScript.InTryBlock;
            /* string body = */ Utils.GetBodyBetween(tempScript, Constants.START_GROUP, Constants.END_GROUP);

            m_blockLevel++;
            m_maxBlockLevel = Math.Max(m_maxBlockLevel, m_blockLevel);

            await StepIn(stepInScript);

            done = stepInScript.Pointer >= tempScript.Pointer ||
                   LastResult == null ||
                   LastResult.IsReturn ||
                   LastResult.Type == Variable.VarType.BREAK ||
                   LastResult.Type == Variable.VarType.CONTINUE;

            m_blockLevel--;
            m_maxBlockLevel = m_blockLevel == 0 ? m_blockLevel : m_maxBlockLevel;

            ProcessingBlock = m_blockLevel > 0;

            doneEvent(done);
            return LastResult == null ? Variable.EmptyInstance : LastResult;
        }

        public async Task<Variable> StepInFunctionIfNeeded(ParsingScript stepInScript)
        {
            stepInScript.Debugger = this;
            m_firstBlock = false;
            if (ReplMode || !SteppingIn)
            {
                return null;
            }

            //Trace("Starting StepIn");
            await StepIn(stepInScript);

            //Trace("Finished StepIn");
            return LastResult;
        }

        public async Task<Variable> StepInBreakpointIfNeeded(ParsingScript stepInScript)
        {
            if (ReplMode || stepInScript.Debugger == null)
            {
                return null;
            }
            stepInScript.Debugger = this;

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

            await StepIn(stepInScript, true);

            SendBackResult = true;
            return LastResult;
        }

        public async Task<Variable> StepInIncludeIfNeeded(ParsingScript stepInScript)
        {
            stepInScript.Debugger = this;
            if (ReplMode || !SteppingIn)
            {
                return null;
            }
            m_firstBlock = false;

            await StepIn(stepInScript);

            SendBackResult = true;
            return LastResult;
        }

        public void AddOutput(string output, ParsingScript script = null)
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
            int origLineNumber = script == null ? 0 : script.GetOriginalLineNumber();
            string filename = "";
            if (script != null && script.Filename != null)
            {
                filename = Path.GetFullPath(script.Filename);
            }


            Output += origLineNumber + "\t" + filename + "\n";
            Output += output;
        }

        static bool UnwindTheStack(Debugger debugger)
        {
            return m_maxBlockLevel > 1 && debugger.m_debugging.StillValid() &&
                   Constants.CORE_OPERATORS.Contains(debugger.LastResult.ParsingToken) &&
                   (debugger.m_debugging.PrevPrev == Constants.END_GROUP ||
                    debugger.m_debugging.Prev == Constants.END_GROUP);
        }

        async Task StepIn(ParsingScript stepInScript, bool aBreakpoint = false)
        {
            Debugger stepIn = new Debugger();
            stepIn.m_debugging = stepInScript;
            stepIn.m_script = stepInScript.String;
            stepIn.ProcessingBlock = ProcessingBlock;
            stepInScript.Debugger = stepIn;

            MainInstance?.m_steppingIns.Push(stepIn);

            try
            {
                CreateResultAndSendBack("next", Output, stepInScript);

                bool done = false;
                while (!done)
                {
                    stepIn.m_completedStepIn.WaitOne();

                    //stepIn.Trace("StepIn WakedUp. SteppingOut:" + SteppingOut + ", this: " + Id);
                    if (Debugger.SteppingOut || aBreakpoint)
                    {
                        break;
                    }
                    done = await stepIn.ExecuteNextStatement();

                    if (stepIn.LastResult == null)
                    {
                        continue;
                    }

                    LastResult = stepIn.LastResult;
                    if (UnwindTheStack(this))
                    {
                        break;
                    }

                    if (!done)
                    {
                        //stepIn.Trace("Completed one StepIn, this: " + Id);
                        CreateResultAndSendBack("next", Output, stepInScript);
                    }
                }
            }
            finally
            {
                MainInstance?.m_steppingIns.Pop();
                ProcessingClientRequest = aBreakpoint;
            }
        }

        async Task<string> DebugScript()
        {
            if (string.IsNullOrWhiteSpace(m_script))
            {
                return null;
            }

            m_debugging = new ParsingScript(TheInterpreter, m_script, 0, m_char2Line);
            m_debugging.OriginalScript = m_script;

            Variable result = Variable.EmptyInstance;
            while (m_debugging.Pointer < m_script.Length)
            {
                result = await ProcessNext();
            }

            return result.AsString();
        }

    }
}
