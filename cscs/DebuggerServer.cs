using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.IO;

namespace SplitAndMerge
{
  public class DebuggerServer
  {
    static Debugger m_debugger;
    static TcpClient m_client;
    static NetworkStream m_stream;

    public static void StartServer(int port = 13337)
    {
      IPAddress localAddr = IPAddress.Parse ("127.0.0.1");

      TcpListener server = new TcpListener(localAddr, port);
      server.Start ();

      while (true) {
        Console.Write ("Waiting for a connection on {0}... ", port);

        // Perform a blocking call to accept requests.
        m_client = server.AcceptTcpClient();
        m_stream = m_client.GetStream ();

        RunClient();
      }
    }

    static void RunClient ()
    {
      Byte [] bytes = new Byte [256];
      string data = null;
      Console.WriteLine ("Starting client {0}", m_client.Client.RemoteEndPoint);

      Interpreter.Instance.Init();

      m_debugger = new Debugger ();
      Debugger.OnResult += SendBack;

      int i;
      try {
        while ((i = m_stream.Read (bytes, 0, bytes.Length)) != 0) {
          data = System.Text.Encoding.UTF8.GetString (bytes, 0, i);
          ThreadPool.QueueUserWorkItem (ThreadPoolCallback, data);
        }
      } catch (Exception exc) {
        Console.Write ("Client disconnected: {0}", exc.Message);
      }

      Debugger.OnResult -= SendBack;

      // Shutdown and end connection
      Console.Write ("Closed connection.");
      m_client.Close();
    }

    static void ThreadPoolCallback(Object threadContext)  
    {
      m_debugger.ProcessClientCommands((string)threadContext);
    }
    static void SendBack (string str)
    {
      byte [] msg = System.Text.Encoding.UTF8.GetBytes (str);
      try {
        m_stream.Write (msg, 0, msg.Length);
        m_stream.Flush ();
      } catch (Exception exc) {
        Console.Write ("Client disconnected: {0}", exc.Message);
        return;
      }
    }
  }
}
