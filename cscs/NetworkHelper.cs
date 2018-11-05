using MNF_Common;
using MNF;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using SplitAndMerge;
using System.Text;

namespace scripting
{
    public class NetworkConnector
    {
        static bool s_init;

        public static void LookAround(string port, string strAction)
        {
            Console.WriteLine("Looking around for port : " + port);
            MNF.LookAround.Instance.Start(port, false);
            string result = "";

            int count = 0;
            while (count++ < 10 && !CancelFunction.Canceled)
            {
                System.Threading.Thread.Sleep(500);
            }
            if (CancelFunction.Canceled)
            {
                scripting.CommonFunctions.RunOnMainThread(strAction, "\"" + result + "\"", "\"1\"");
                return;
            }

            try
            {
                List<MNF.ResponseEndPointInfo> responseEndPoints = MNF.LookAround.Instance.GetResponseEndPoint();
                foreach (var endPoint in responseEndPoints)
                {
                    Console.WriteLine("Response IPEndPoint : {0}", endPoint);
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        result += ";";
                    }

                    string host = endPoint.ipEndPoint.Address.ToString();
                    result += host;
                    scripting.CommonFunctions.RunOnMainThread(strAction, "\"" + host + "\"", "\"0\"");
                }
                Console.WriteLine("My IP:{0} Port:{1}", MNF.LookAround.Instance.MyIP, MNF.LookAround.Instance.MyPort);

            }
            catch (Exception exc)
            {
                Console.WriteLine(exc);
            }

            scripting.CommonFunctions.RunOnMainThread(strAction, "\"" + result + "\"", "\"1\"");
            //MNF.LookAround.Instance.Stop();
        }

        public static string GetIPv4Address(IPAddress address)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return address.ToString();
            }
            var result = address.MapToIPv4().ToString();
            if (result == "0.0.0.1" || result == "127.0.0.1")
            {
                result = null;
            }
            return result;
        }

        static List<string> GetIPAddresses()
        {
            List<string> results = new List<string>();
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var interf in interfaces)
            {
                if (interf.OperationalStatus != OperationalStatus.Up &&
                    interf.OperationalStatus != OperationalStatus.Unknown)
                {
                    continue;
                }

                string key = interf.Name + "_" + interf.Description + "_" + interf.Id + "_" +
                                   interf.NetworkInterfaceType;

                var props = interf.GetIPProperties();
                var addresses = interf.GetPhysicalAddress();

                foreach (var addr in props.AnycastAddresses)
                {
                    string address = GetIPv4Address(addr.Address);
                    if (!string.IsNullOrWhiteSpace(address) && !results.Contains(key + address))
                    {
                        results.Add(key + address);
                    }
                }
                foreach (var addr in props.UnicastAddresses)
                {
                    string address = GetIPv4Address(addr.Address);
                    if (!string.IsNullOrWhiteSpace(address) && !results.Contains(key + address))
                    {
                        results.Add(key + address);
                    }
                }
                foreach (var addr in props.MulticastAddresses)
                {
                    string address = GetIPv4Address(addr.Address);
                    if (!string.IsNullOrWhiteSpace(address) && !results.Contains(key + address))
                    {
                        results.Add(key + address);
                    }
                }
                foreach (var addr in props.WinsServersAddresses)
                {
                    string address = GetIPv4Address(addr);
                    if (!string.IsNullOrWhiteSpace(address))
                    {
                        results.Add(key + address);
                    }
                }
                var stats4 = interf.GetIPv4Statistics();

                var addr2 = props.GatewayAddresses;
                var addr4 = props.DnsAddresses;
            }

            return results;
        }

        // This function is currently not used:
        /*static void SearchConnections(string strAction, int port, string pattern = "192.168.0.*")
        {
            Socket client = null;
            string prefix = pattern.Substring(0, pattern.Length - 1);
            string result = "";

            int count = 0;
            while (count++ < 2 && !CancelFunction.Canceled)
            {
                System.Threading.Thread.Sleep(500);
            }
            if (CancelFunction.Canceled)
            {
                scripting.CommonFunctions.RunOnMainThread(strAction, "\"" + result + "\"", "\"1\"");
                return;
            }

            try
            {
                for (int i = 1; i < 128 && !CancelFunction.Canceled; i++)
                {
                    string host = prefix + i;
                    client = CheckConnection(host, port);
                    if (client != null)
                    {
                        if (!string.IsNullOrWhiteSpace(result))
                        {
                            result += ";";
                        }
                        result += host;
                        scripting.CommonFunctions.RunOnMainThread(strAction, "\"" + host + "\"", "\"0\"");

                        client.Dispose();
                    }
                }
            }
            catch (Exception exc)
            {
                Console.WriteLine(exc);
            }

            scripting.CommonFunctions.RunOnMainThread(strAction, "\"" + result + "\"", "\"1\"");
        }*/

        public static Socket CheckConnection(string host, int port, int timeout = 500)
        {
            try
            {
                IPAddress ipAddress = IPAddress.Parse(host);

                Socket client = new Socket(SocketType.Stream, ProtocolType.Tcp);
                IAsyncResult result = client.BeginConnect(ipAddress, port, null, null);

                bool success = result.AsyncWaitHandle.WaitOne(timeout, true);
                if (!client.Connected)
                {
                    try
                    {
                        client.Dispose();
                    }
                    catch (Exception exc)
                    {
                        Console.WriteLine(exc);
                    }
                    return null;
                }
                client.EndConnect(result);

                return client;
            }
            catch (Exception e)
            {
                var msg = e.Message;
                Console.WriteLine(e.ToString());
                return null;
            }
        }
    }

    public class ClientSession : BinarySession
    {
        public override int OnConnectSuccess()
        {
            LogManager.Instance.Write("OnConnectSuccess : {0}:{1}", this.ToString(), this.GetType());

            var ecshoPacket = new BinaryMessageDefine.PACK_CS_ECHO();
            AsyncSend((int)BinaryMessageDefine.ENUM_CS_.CS_ECHO, ecshoPacket);

            return 0;
        }

        public override int OnConnectFail()
        {
            LogManager.Instance.Write("OnConnectFail : {0}:{1}", this.ToString(), this.GetType());
            return 0;
        }

        public override int OnDisconnect()
        {
            LogManager.Instance.Write("OnDisconnect : {0}:{1}", this.ToString(), this.GetType());
            return 0;
        }
    }

    public class BinaryMessageDispatcher : DefaultDispatchHelper<ClientSession, BinaryMessageDefine, BinaryMessageDefine.ENUM_SC_>
    {
        int count = 0;

        int onSC_ECHO(ClientSession session, object message)
        {
            var echo = (BinaryMessageDefine.PACK_SC_ECHO)message;
            if (++count % 100 == 0)
                LogManager.Instance.Write("{0}, {1}, {2}", session, echo.GetType(), count);

            var ecshoPacket = new BinaryMessageDefine.PACK_CS_ECHO();
            session.AsyncSend((int)BinaryMessageDefine.ENUM_CS_.CS_ECHO, ecshoPacket);

            return 0;
        }
    }
}
