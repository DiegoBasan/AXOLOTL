using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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

        // Tiempo entre intentos de reconexion automatica (ms)
        public int ReconnectInterval { get; set; } = 2000;

        public string[][] Result = new string[5][];

        public bool[] TriggerFinish = new bool[5];

        public bool[] IsOK = { false, false, false, false, false };

        // true cuando ya llego la linea de resultado del ultimo trigger (no solo el eco "T1")
        public bool[] ResultReady = new bool[5];

        // true mientras hay un trigger enviado cuyo resultado todavia no llega
        public bool[] WaitingResult = new bool[5];

        public bool IsConnected
        {
            get
            {
                Socket c = Client;
                if (c == null) return false;
                return c.Connected;
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
        public event EventHandler<bool> OnConnectionChanged;

        BackgroundWorker _worker = new BackgroundWorker();
        BackgroundWorker _worker_2 = new BackgroundWorker();
        private ConcurrentQueue<string> _cmd = new ConcurrentQueue<string>();

        private readonly object _connLock = new object();
        private readonly List<byte> _rxBuffer = new List<byte>();
        private bool _autoReconnect;
        private DateTime _lastReconnect = DateTime.MinValue;

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

        // Hilo de envio: los workers ya no se cancelan al desconectar, solo esperan a que haya socket
        private void _worker_2_DoWork(object sender, DoWorkEventArgs e)
        {
            while (!_worker_2.CancellationPending)
            {
                Socket c = Client;
                if (c == null || !c.Connected)
                {
                    Thread.Sleep(100);
                    continue;
                }

                if (_cmd.TryDequeue(out var cmd))
                {
                    try
                    {
                        c.Send(Encoding.ASCII.GetBytes(cmd));
                    }
                    catch (Exception)
                    {
                        CloseSocket(c);
                    }
                }
                Thread.Sleep(50);
            }
        }

        // Hilo de recepcion
        private void _worker_DoWork(object sender, DoWorkEventArgs e)
        {
            byte[] buf = new byte[1024];

            while (!_worker.CancellationPending)
            {
                Socket c = Client;
                if (c == null || !c.Connected)
                {
                    _rxBuffer.Clear();
                    TryReconnect();
                    Thread.Sleep(100);
                    continue;
                }

                try
                {
                    // Espera hasta 100 ms por datos, sin usar ReceiveTimeout
                    if (!c.Poll(100000, SelectMode.SelectRead))
                        continue;

                    int n = c.Receive(buf);
                    if (n == 0)
                    {
                        // La camara cerro la conexion
                        CloseSocket(c);
                        continue;
                    }

                    for (int i = 0; i < n; i++)
                    {
                        if (buf[i] == 13)
                        {
                            string line = Encoding.ASCII.GetString(_rxBuffer.ToArray());
                            _rxBuffer.Clear();
                            HandleLine(line);
                        }
                        else if (buf[i] != 10)
                        {
                            _rxBuffer.Add(buf[i]);
                        }
                    }
                }
                catch (Exception)
                {
                    CloseSocket(c);
                    Thread.Sleep(100);
                }
            }
        }

        private void HandleLine(string s)
        {
            // Una respuesta mal formada no debe tumbar la conexion
            try
            {
                RspHandle(s);
            }
            catch (Exception)
            {
            }
        }

        public void SendData(string req)
        {
            // Todo pasa por la cola para no mezclar bytes con el hilo de envio
            _cmd.Enqueue(req);
        }

        private void RspHandle(string s)
        {
            if (string.IsNullOrEmpty(s)) return;

            SafeInvoke(() => OnResponseReceived?.Invoke(this, s));

            // Error command
            if (s == "1" || s == "R0") return;

            if (s.StartsWith("RM"))
            {
                string[] p = s.Split(',');
                if (p.Length > 1)
                    RunMode = p[1] == "1";
                return;
            }

            if (s.StartsWith("PR"))
            {
                string[] p = s.Split(',');
                if (p.Length > 1 && int.TryParse(p[p.Length - 1], out int prog))
                    CurrentProgram = prog;
                return;
            }

            if (s.StartsWith("ER"))
            {
                for (int i = 0; i < WaitingResult.Length; i++)
                    WaitingResult[i] = false;
                HandleErrorResponse(s);
                return;
            }

            HandleTriggerResult(s);
        }

        private void HandleErrorResponse(string s)
        {
            SafeInvoke(() => OnErrorCommand?.Invoke(this, s));
        }

        private void HandleTriggerResult(string s)
        {
            if (s.StartsWith("T"))
            {
                if (s.Length >= 2 && s[1] >= '1' && s[1] <= '4')
                {
                    int ack = s[1] - '0';
                    SafeInvoke(() => OnCamTriggerAck?.Invoke(this, ack));
                }
                return;
            }

            string[] s1 = s.Split(',');
            if (s1.Length < 2) return;

            if (!double.TryParse(s1[0].Trim('+'), NumberStyles.Float, CultureInfo.InvariantCulture, out double camNo)
                || !double.TryParse(s1[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double status))
                return;

            int cam = (int)camNo;
            if (cam < 1 || cam > 4) return;

            bool pass = (int)status == 0;

            Result[cam] = s1;
            IsOK[cam] = pass;
            WaitingResult[cam] = false;
            ResultReady[cam] = true;

            dResult[cam].Pass = pass;
            dResult[cam].ResultString = s;
            SafeInvoke(() => OnCamResultReceived?.Invoke(this, dResult[cam]));
        }

        private void SafeInvoke(Action a)
        {
            // Una excepcion en un handler (UI) no debe matar el hilo de recepcion
            try
            {
                a();
            }
            catch (Exception)
            {
            }
        }

        public bool Connect()
        {
            lock (_connLock)
            {
                try
                {
                    if (Client != null && Client.Connected)
                    {
                        _autoReconnect = true;
                        StartWorkers();
                        return true;
                    }

                    CloseSocket(Client);

                    Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    s.NoDelay = true;
                    SetKeepAlive(s);

                    int connectTimeout = Timeout > 0 ? Math.Min(Timeout, 3000) : 3000;
                    IAsyncResult ar = s.BeginConnect(IP, Port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(connectTimeout))
                    {
                        s.Close();
                        return false;
                    }
                    s.EndConnect(ar);

                    Client = s;
                    _autoReconnect = true;
                    StartWorkers();
                    SafeInvoke(() => OnConnectionChanged?.Invoke(this, true));
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public void Disconnect()
        {
            _autoReconnect = false;
            CloseSocket(Client);
        }

        private void StartWorkers()
        {
            if (!_worker.IsBusy) _worker.RunWorkerAsync();
            if (!_worker_2.IsBusy) _worker_2.RunWorkerAsync();
        }

        // Solo cierra el socket indicado, asi un hilo viejo no puede cerrar una conexion nueva
        private void CloseSocket(Socket s)
        {
            if (s == null) return;

            bool wasCurrent;
            lock (_connLock)
            {
                wasCurrent = Client == s;
                if (wasCurrent) Client = null;
            }

            try { s.Shutdown(SocketShutdown.Both); } catch { }
            try { s.Close(); } catch { }

            if (!wasCurrent) return;

            // Comandos viejos (ej. un T1) no se deben enviar al reconectar
            while (_cmd.TryDequeue(out _)) { }
            for (int i = 0; i < WaitingResult.Length; i++)
                WaitingResult[i] = false;

            SafeInvoke(() => OnConnectionChanged?.Invoke(this, false));
        }

        private void TryReconnect()
        {
            if (!_autoReconnect) return;
            if ((DateTime.Now - _lastReconnect).TotalMilliseconds < ReconnectInterval) return;
            _lastReconnect = DateTime.Now;

            if (Connect())
                SetRunMode();
        }

        private static void SetKeepAlive(Socket s)
        {
            try
            {
                // Detecta cable desconectado: keepalive cada 1 s despues de 2 s sin trafico
                s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                byte[] values = new byte[12];
                BitConverter.GetBytes(1u).CopyTo(values, 0);
                BitConverter.GetBytes(2000u).CopyTo(values, 4);
                BitConverter.GetBytes(1000u).CopyTo(values, 8);
                s.IOControl(IOControlCode.KeepAliveValues, values, null);
            }
            catch
            {
            }
        }

        public void StartRun()
        {
            if (IsConnected)
                StartWorkers();
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
                WaitingResult[camNum] = true;
                string cmd = $"T{camNum}\r";
                _cmd.Enqueue(cmd);
            }
        }

        // Limpia el resultado anterior antes de un trigger para no leer datos viejos
        public void ClearResult(int camNum)
        {
            if (camNum > 0 && camNum <= 4)
            {
                ResultReady[camNum] = false;
                IsOK[camNum] = false;
                TriggerFinish[camNum] = false;
            }
        }

        public void WriteVariables((string, double)[] variables)
        {
            foreach (var variable in variables)
            {
                string cmd = $"MW,{variable.Item1},{variable.Item2.ToString(CultureInfo.InvariantCulture)}\r";
                _cmd.Enqueue(cmd);
            }
        }
    }
}
