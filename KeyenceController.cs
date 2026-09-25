using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Acura3._1.Classes
{
    public class KeyenceController
    {
        public delegate void RunCamera_ResultEvent(string sGetResult);

        public Socket Client;

        public string IP { get; set; }

        public int Port { get; set; }

        public int Timeout { get; set; } = 5000;

        public string[][] Result = new string[5][];

        public bool[] TriggerFinish = new bool[5];

        public bool[] IsOK = { false, false, false, false, false };

        public bool IsConnected
        {
            get
            {
                if (Client == null) return false;
                return Client.Connected;
            }
        }

        public bool RunMode { get; set; }

        public int CurrentProgram { get; set; }

        public Dictionary<int, CamResult> dResult = new Dictionary<int, CamResult>()
        {
            {1, new CamResult(){CamId = 1 } }, {2, new CamResult(){ CamId = 2} },
            {3, new CamResult() { CamId = 3} }, {4, new CamResult(){ CamId = 4} }
        };

        public class CamResult
        {
            public int CamId { get; set; }
            public bool Pass { get; set; }
            public string ResultString { get; set; }
        }

        public event EventHandler<string> OnResponseReceived;
        public event EventHandler<int> OnCamTriggerAck;
        public event EventHandler<CamResult> OnCamResultReceived;
        public event EventHandler<string> OnErrorCommand;

        BackgroundWorker _worker = new BackgroundWorker();
        BackgroundWorker _worker_2 = new BackgroundWorker();
        private ConcurrentQueue<string> _cmd = new ConcurrentQueue<string>();


        public KeyenceController(string sIP, int iPort, int iTimeout = 5000)
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
                    Thread.Sleep(50);
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
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    break;
                }

                if (_worker.CancellationPending) 
                    break;
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
            if (_cmd.Count == 0) return;

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

                OnResponseReceived?.Invoke(this, s);

                // Error command
                if (s == "1" || s == "R0") return;

                if (s.StartsWith("RM"))
                {
                    RunMode = s.Split(',')[1] == "1";
                    return;
                }

                if (s.StartsWith("PR"))
                {
                    CurrentProgram = int.Parse(s.Split(',')[2]);
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

        private void HandleErrorResponse(string s)
        {
            OnErrorCommand?.Invoke(this, s);
        }

        private void HandleTriggerResult(string s)
        {
            if (s.StartsWith("T1"))
                OnCamTriggerAck?.Invoke(this, 1);

            if (s.StartsWith("T2"))
                OnCamTriggerAck?.Invoke(this, 2);

            if (s.StartsWith("T3"))
                OnCamTriggerAck?.Invoke(this, 3);

            if (s.StartsWith("T4"))
                OnCamTriggerAck?.Invoke(this, 4);

            if (s.StartsWith("T")) return;

            string[] s1 = s.Split(',');
            Console.WriteLine(s1[0].Trim('+'));

            double camNo = 0;
            double status = 0;

            if (!double.TryParse(s1[0].Trim('+'), out camNo)
                || !double.TryParse(s1[1], out status))
                return;

            switch ((int)camNo)
            {
                case 1:
                    dResult[1].Pass = (int)status == 0;
                    dResult[1].ResultString = s;
                    OnCamResultReceived?.Invoke(this, dResult[1]);
                    break;
                case 2:
                    dResult[2].Pass = (int)status == 0;
                    dResult[2].ResultString = s;
                    OnCamResultReceived?.Invoke(this, dResult[2]);
                    break;
                case 3:
                    dResult[3].Pass = (int)status == 0;
                    dResult[3].ResultString = s;
                    OnCamResultReceived?.Invoke(this, dResult[3]);
                    break;
                case 4:
                    dResult[4].Pass = (int)status == 0;
                    dResult[4].ResultString = s;
                    OnCamResultReceived?.Invoke(this, dResult[4]);
                    break;
                default:
                    break;
            }
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
                if(!_worker_2.IsBusy) _worker_2.RunWorkerAsync();

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

        public void StartRun()
        {
            if (Client == null) return;
            if (Client.Connected)
            {
                if (!_worker.IsBusy)
                    _worker.RunWorkerAsync();
            }

        }

        public void SetRunMode()
        {
            _cmd.Enqueue("R0\r");
        }

        public bool IsRunMode()
        {
            return RunMode;
        }

        public void ChangeProgram(int sdcardNumber, int programNumber)
        {
            string msg = "PW," + sdcardNumber + "," + programNumber.ToString("000") + "\r";
            _cmd.Enqueue(msg);
        }

        public void ReadProgram()
        {
            string msg = "PR\r";
            _cmd.Enqueue(msg);
        }

        public void Reset()
        {
            _cmd.Enqueue("RS\r");
        }

        public void Trigger(int camNum)
        {
            if (camNum > 0 && camNum <= 4)
            {
                string cmd = $"T{camNum}\r";
                _cmd.Enqueue(cmd);
            }
        }

        public void WriteVariables((string, double)[] variables)
        {
            foreach (var variable in variables)
            {
                string cmd = $"MW,{variable.Item1},{variable.Item2}\r";
                _cmd.Enqueue(cmd);
            }
        }
    }
}
