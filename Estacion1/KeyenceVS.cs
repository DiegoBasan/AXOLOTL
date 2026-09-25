using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Acura3._1.Classes.KeyenceController;

namespace Acura3._1.Classes.IVN
{
    internal class KeyenceVS
    {
        public Socket Client;

        public string IP { get; set; }

        public int Port { get; set; }

        public int Timeout { get; set; } = 5000;

        public bool IsConnected
        {
            get
            {
                if (Client == null) return false;
                return Client.Connected;
            }
        }

        public bool TriggerReady { get; set; }
        public bool OutputReady { get; set; }
        public bool RunMode { get; set; }
        public bool Status { get; set; }

        public event EventHandler<string[]> OnCamResultReceived;
        public event EventHandler<string> OnErrorCommand;

        BackgroundWorker _worker = new BackgroundWorker();
        BackgroundWorker _worker_2 = new BackgroundWorker();
        private ConcurrentQueue<string> _cmd = new ConcurrentQueue<string>();

        public KeyenceVS(string sIP, int iPort, int iTimeout = 5000)
        {
            IP = sIP;
            Port = iPort;
            Timeout = iTimeout;

            _worker.WorkerSupportsCancellation = true;
            _worker.DoWork += _worker_DoWork;

            _worker_2.WorkerSupportsCancellation = true;
            _worker_2.DoWork += _worker_2_DoWork;
        }

        private void _worker_2_DoWork(object sender, DoWorkEventArgs e)
        {
            while (!_worker_2.CancellationPending)
            {
                if (Client == null) continue;
                if (Client.Connected == false) continue;

                try
                {
                    CmdHandle();
                    Thread.Sleep(10);
                }
                catch (Exception)
                {
                    break;
                }
            }
        }

        private void _worker_DoWork(object sender, DoWorkEventArgs e)
        {
            while (!_worker.CancellationPending)
            {
                if (Client == null) continue;
                if (Client.Connected == false) continue;

                try
                {
                    RspHandle();
                    Thread.Sleep(10);
                }
                catch (Exception)
                {
                    break;
                }

                if (_worker.CancellationPending) break;
            }
            Disconnect();
        }

        public void SendData(string req)
        {
            try
            {
                byte[] byteData = Encoding.UTF8.GetBytes(req);
                int bytesRead = Client.Send(byteData);
            }
            catch (Exception)
            {
                throw;
            }
        }

        private bool ReceiveData(out byte[] _bytes)
        {
            bool r = false;
            _bytes = new byte[0];
            List<byte> lb = new List<byte>();

            using (NetworkStream reader = new NetworkStream(Client))
            {
                int b = -1;
                try
                {
                    b = reader.ReadByte();
                }
                catch (Exception)
                {
                    return false;
                }

                while (b != -1)
                {
                    if (b == 13)
                    {
                        r = true;
                        break;
                    }
                    lb.Add((byte)b);

                    try
                    {
                        b = reader.ReadByte();
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
                reader.Flush();
            }
            _bytes = lb.ToArray();
            return r;
        }

        private void CmdHandle()
        {
            if (_cmd.TryDequeue(out var cmd))
            {
                SendData(cmd);
            }
        }

        private void RspHandle()
        {
            if (ReceiveData(out var _bytes))
            {
                string s = Encoding.ASCII.GetString(_bytes);
                if (string.IsNullOrEmpty(s)) return;

                if (s.StartsWith("RUN"))
                {
                    RunMode = true;
                    return;
                }

                if (s.StartsWith("TD"))
                {
                    TriggerReady = true;
                    return;
                }

                if (s.StartsWith("OD"))
                {
                    OutputReady = true;
                    return;
                }

                if (s.StartsWith("ER"))
                {
                    HandleErrorResponse(s);
                    return;
                }
                HandleTriggerResult(s);
            }
        }

        private void HandleTriggerResult(string s)
        {
            if (s.StartsWith("TRG"))
            {
                string[] result = s.Substring(3).Split(',');
                Status = result[0] == "1";
                OnCamResultReceived?.Invoke(this, result);
            }
        }

        private void HandleErrorResponse(string s)
        {
            OnErrorCommand?.Invoke(this, s);
        }

        public bool Connect()
        {
            try
            {
                if (Client == null)
                {
                    Client = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    Client.ReceiveTimeout = Timeout;
                }

                if (!Client.Connected)
                    Client.Connect(IP, Port);

                if (!_worker.IsBusy) _worker.RunWorkerAsync();
                if (!_worker_2.IsBusy) _worker_2.RunWorkerAsync();

                return Client.Connected;
            }
            catch
            {
                return false;
            }
        }

        public void Disconnect()
        {
            _worker?.CancelAsync();
            _worker_2?.CancelAsync();

            Client?.Close();
            Client = null;
        }

        public void SetRunMode()
        {
            _cmd.Enqueue("RUN\r");
        }

        public void ChangeProgram(int programNumber)
        {
            string msg = "PL,1," + programNumber.ToString("000") + "\r";
            _cmd.Enqueue(msg);
        }

        public void SaveProgram()
        {
            _cmd.Enqueue("PS\r");
        }

        public void Reset()
        {
            _cmd.Enqueue("RS\r");
        }

        public void EnableTrigger()
        {
            _cmd.Enqueue("TD,0\r");
        }

        public void Trigger()
        {
            _cmd.Enqueue("TRG\r");
        }

        public void EnableOutput()
        {
            _cmd.Enqueue("OD,0\r");
        }
    }
}
