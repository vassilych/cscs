//#define MAIN_THREAD_CHECK

using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SplitAndMerge
{
    public class DebuggerServer
    {
        public static Action<Debugger, string> OnRequest;
        public static bool DebuggerAttached { set; get; }

        static BlockingCollection<string> m_queue = new BlockingCollection<string>();
        public static BlockingCollection<string> Queue { get { return m_queue; } }

        static List<DebuggerClient> m_clients = new List<DebuggerClient>();

        public static void StartServer(int port = 13337)
        {
            ThreadPool.QueueUserWorkItem(StartServerBlocked, port);
        }

        public static void StartServerBlocked(Object threadContext)
        {
            int port = (int)threadContext;

            IPAddress localAddr = IPAddress.Parse("127.0.0.1");

            TcpListener server = new TcpListener(localAddr, port);
            server.Start();
            DebuggerAttached = true;

            Debugger.OnResult += SendBack;
            ThreadPool.QueueUserWorkItem(StartProcessing, null);

            while (true)
            {
                Console.Write("Waiting for a connection on {0}... ", port);

                // Perform a blocking call to accept requests.
                TcpClient socket = server.AcceptTcpClient();

                DebuggerClient client = new DebuggerClient();
                m_clients.Add(client);

                ThreadPool.QueueUserWorkItem(o => client.RunClient(socket));
                Thread.Sleep(1000);
            }
        }

        static void SendBack(string str)
        {
            int i = 0;
            while (i < m_clients.Count)
            {
                DebuggerClient client = m_clients[i];
                if (!client.Connected)
                {
                    m_clients.RemoveAt(i);
                    continue;
                }
                client.SendBack(str);
                i++;
            }
        }

        static void StartProcessing(Object threadContext)
        {
#if MAIN_THREAD_CHECK
            System.Timers.Timer runTimer = new System.Timers.Timer(0.1 * 1000);
            bool processing = false;
            runTimer.Elapsed += (sender, e) =>
            {
                if (processing)
                {
                    return;
                }
                processing = true;
                scripting.iOS.AppDelegate.GetCurrentController().InvokeOnMainThread(() =>
                {
                    ProcessQueue();
                });
                processing = false;
            };
            runTimer.Start();
#elif UNITY_EDITOR || UNITY_STANDALONE
      // Do nothing: ProcessQueue() will be called from the Unity Main Thread
#else
            try
            {
                ProcessQueue();
            }
            catch (Exception exc)
            {
                Console.Write("Exception while waiting for requests: {0}", exc);
            }
#endif
        }

        public static void ProcessQueue()
        {
            string data;
#if UNITY_EDITOR || UNITY_STANDALONE || MAIN_THREAD_CHECK
            while (m_queue.TryTake(out data))
            { // Exit as soon as done processing.
                Debugger.MainInstance?.ProcessClientCommands(data);
#else
            while (true)
            { // A blocking call.
                data = m_queue.Take();

                ThreadPool.QueueUserWorkItem(RunRequestBlocked, data);
#endif
            }
        }

        public static void RunRequestBlocked(Object threadContext)
        {
            string data = (string)threadContext;
            Debugger.MainInstance?.ProcessClientCommands(data);
        }
    }

    public class DebuggerClient
    {
        public bool Connected { private set; get; }
        bool m_isRepl;

        Debugger m_debugger;
        TcpClient m_client;
        NetworkStream m_stream;

        public void RunClient(Object threadContext)
        {
            m_client = (TcpClient)threadContext;
            m_stream = m_client.GetStream();
            Connected = true;

            Byte[] bytes = new Byte[4096];
            string data = null;
            Console.WriteLine("Starting client {0}", m_client.Client.RemoteEndPoint);

#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
      Interpreter.Instance.Init ();
#endif

            m_debugger = new Debugger();

            int i;
            try
            {
                while ((i = m_stream.Read(bytes, 0, bytes.Length)) != 0 && Connected)
                {
                    data = System.Text.Encoding.UTF8.GetString(bytes, 0, i);
                    string rest = "";
                    DebuggerUtils.DebugAction action = DebuggerUtils.StringToAction(data, ref rest);

                    if (action == DebuggerUtils.DebugAction.BYE)
                    {
                        break;
                    }
                    else if (action == DebuggerUtils.DebugAction.REPL)
                    {
                        m_isRepl = true;
                        DebuggerServer.Queue.Add(data);
                    }
                    else if (m_debugger.CanProcess(action))
                    {
                        ThreadPool.QueueUserWorkItem(DebuggerServer.RunRequestBlocked, data);
                    }
                    else
                    {
                        DebuggerServer.Queue.Add(data);
                    }
                }
            }
            catch (Exception exc)
            {
                Console.Write("Client disconnected: {0}", exc.Message);
            }

            if (m_debugger == Debugger.MainInstance)
            {
                Debugger.MainInstance = null;
            }

            // Shutdown and end connection
            Console.Write("Closed connection.");
            Disconnect();
        }

        void Disconnect()
        {
            Connected = false;
            m_stream.Close();
            m_client.Close();
            m_stream.Dispose();
        }

        public void SendBack(string str)
        {
            byte[] msg = System.Text.Encoding.UTF8.GetBytes(str);
            try
            {
                m_stream.Write(msg, 0, msg.Length);
                m_stream.Flush();
            }
            catch (Exception exc)
            {
                Console.Write("Client disconnected: {0}", exc.Message);
                Disconnect();
            }

            if (m_isRepl)
            {
                Disconnect();
            }
        }
    }

    public class DebuggerUtils
    {
        public enum DebugAction { NONE, FILE, NEXT, CONTINUE, STEP_IN, STEP_OUT,
            SET_BP, VARS, STACK, ALL, REPL, _REPL, END, BYE };

        public static DebugAction StringToAction(string str, ref string rest)
        {
            int index = str.IndexOf('|');
            if (index >= 0)
            {
                rest = index < str.Length - 1 ? str.Substring(index + 1) : "";
                str = str.Substring(0, index);
            }

            switch (str)
            {
                case "next": return DebugAction.NEXT;
                case "continue": return DebugAction.CONTINUE;
                case "stepin": return DebugAction.STEP_IN;
                case "stepout": return DebugAction.STEP_OUT;
                case "file": return DebugAction.FILE;
                case "setbp": return DebugAction.SET_BP;
                case "vars": return DebugAction.VARS;
                case "stack": return DebugAction.STACK;
                case "all": return DebugAction.ALL;
                case "repl": return DebugAction.REPL;
                case "_repl": return DebugAction._REPL;
                case "bye": return DebugAction.BYE;
            }

            return DebugAction.NONE;
        }
        public static string ResponseMainToken(DebugAction action)
        {
            switch (action)
            {
                case DebugAction.NEXT:
                case DebugAction.CONTINUE:
                case DebugAction.STEP_IN:
                case DebugAction.STEP_OUT: return "next\n";
                case DebugAction.REPL: return "";
                case DebugAction._REPL: return "repl\n";
                case DebugAction.FILE: return "file\n";
                case DebugAction.SET_BP: return "set_bp\n";
                case DebugAction.END: return "end\n";
            }

            return "none\n";
        }

        public static Variable Execute(ParsingScript script)
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

    }
}
