using Acura3._0.Classes;
using Acura3._1.Classes;
using Acura3._1.Classes.IVN;
using AcuraLibrary.Forms;
using Cerberus.CoreEngine.Master;
using Cerberus.Enum;
using Cerberus.Utility;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Acura3._1.ModuleForms
{
    public partial class VisionForm : ModuleBaseForm
    {
        public static Dictionary<(int Process, int Program), List<(int Cavity, bool Alignment)>> VisionCavity
            = new Dictionary<(int, int), List<(int, bool)>>();
        public static Queue<(int Cavity, bool Alignment, bool ReInspect)> visionCavity = new Queue<(int, bool, bool)>();
        public static Dictionary<int, (double X, double Y, bool Safety)> Alignment = new Dictionary<int, (double X, double Y, bool)>();

        public KeyenceController XGX;
        private ContrastechLight Light;
        private int retryCount, delayTrigger;
        private bool initProgSent;
        public static int CurrentProcess, CurrentProgram;
        public (int Cavity, bool Alignment, bool ReInspect) currentCavity;
        public static bool Retry = false;
        public static int RetryNum;
        public (int Process, int Program, int Cavity) RetryProcess;
        public bool Reset, ManualResult;
        public int ManualTool;

        private CTimer VisionTM = new CTimer();
        private CTimer RetryTM = new CTimer();
        private CTimer ManualTM = new CTimer();

        public VisionForm()
        {
            InitializeComponent();
            txtLight.Text = trackbarLight.Value.ToString();

            MiddleLayer.ControlF.UpdateSetting += ControlForm_UpdateSetting;
        }

        private void ControlForm_UpdateSetting(object sender, EventArgs e)
        {
            ReadDataSet();
        }

        private void GetTool(int process, int cavity, out int tool)
        {
            tool = 0;
            if (ControlForm.WorkDataSet.Tables[0].Rows.Count < 1) return;
            var rows = ControlForm.WorkDataSet.Tables[0].AsEnumerable()
                .Where(row => int.TryParse(row["Process"]?.ToString(), out int p) && p == process &&
                              int.TryParse(row["Cavity"]?.ToString(), out int c) && c == cavity);
            if (rows.Any())
            {
                tool = int.Parse(rows.First()["Tool"].ToString());
            }
        }

        private void ReadDataSet()
        {
            if (ControlForm.WorkDataSet.Tables[0].Rows.Count < 1) return;

            VisionCavity.Clear();
            int rowCount = ControlForm.WorkDataSet.Tables[0].Rows.Count;
            int[] process = ControlForm.WorkDataSet.Tables[0].AsEnumerable()
                    .Select(row => int.TryParse(row["Process"].ToString(), out int res) ? res : 0)
                    .ToArray();
            int[] program = ControlForm.WorkDataSet.Tables[0].AsEnumerable()
                    .Select(row => int.TryParse(row["Program ID"].ToString(), out int res) ? res : 0)
                    .ToArray();
            int[] cavity = ControlForm.WorkDataSet.Tables[0].AsEnumerable()
                .Select(row => int.TryParse(row["Cavity"].ToString(), out int res1) ? res1 : 0)
                .ToArray();
            bool[] alignment = ControlForm.WorkDataSet.Tables[0].AsEnumerable()
                .Select(row => bool.TryParse(row["Tighten"].ToString(), out bool res2) ? res2 : false)
                .ToArray();

            for (int i = 0; i < rowCount; i++)
            {
                if (!VisionCavity.ContainsKey((process[i], program[i])))
                {
                    VisionCavity.Add((process[i], program[i]), new List<(int, bool)> { (cavity[i], alignment[i]) });
                }
                else
                {
                    if (!VisionCavity[(process[i], program[i])].Any(c => c.Cavity == cavity[i]))
                    {
                        VisionCavity[(process[i], program[i])].Add((cavity[i], alignment[i]));
                    }
                }
            }
              
            VisionCavity = VisionCavity.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            if (VisionCavity.Count > 0)
            {
                cbbProcess1.DataSource = VisionCavity.Keys.Select(p => p.Process).Distinct().ToList();
                cbbProcess1.SelectedIndex = 0;
                cbbProg.DataSource = VisionCavity.Keys.Where(p => p.Process == (int)cbbProcess1.SelectedValue).
                        Select(p => p.Program).Distinct().ToList();

                var row = ControlForm.WorkDataSet.Tables[0].AsEnumerable()
                    .Where(r => int.TryParse(r["Process"]?.ToString(), out int p) && p == (int)cbbProcess1.SelectedValue)
                    .Select(r => int.Parse(r["Tool"].ToString())).ToList();
                var reInspectRow = ControlForm.WorkDataSet.Tables[0].AsEnumerable()
                    .Where(r => int.TryParse(r["Process"]?.ToString(), out int p) && p == (int)cbbProcess1.SelectedValue &&
                            bool.TryParse(r["Tighten"]?.ToString(), out bool alignmentFlag) && alignmentFlag)
                    .Select(r => int.Parse(r["Tool"].ToString()) + 1).ToList();
                row.AddRange(reInspectRow);

                cbbTool.DataSource = row.Distinct().OrderBy(tool => tool).ToList();
                cbbTool.SelectedIndex = 0;

                cbbProcess1.SelectedIndexChanged += (s, e) =>
                {
                    cbbProg.DataSource = VisionCavity.Keys.Where(p => p.Process == (int)cbbProcess1.SelectedValue).
                        Select(p => p.Program).Distinct().ToList();

                    var rows = ControlForm.WorkDataSet.Tables[0].AsEnumerable()
                    .Where(r => int.TryParse(r["Process"]?.ToString(), out int p) && p == (int)cbbProcess1.SelectedValue)
                    .Select(r => int.Parse(r["Tool"].ToString())).ToList();
                    var reInspectRows = ControlForm.WorkDataSet.Tables[0].AsEnumerable()
                        .Where(r => int.TryParse(r["Process"]?.ToString(), out int p) && p == (int)cbbProcess1.SelectedValue &&
                                bool.TryParse(r["Tighten"]?.ToString(), out bool alignmentFlag) && alignmentFlag)
                        .Select(r => int.Parse(r["Tool"].ToString()) + 1).ToList();
                    rows.AddRange(reInspectRows);

                    cbbTool.DataSource = rows.Distinct().OrderBy(tool => tool).ToList();
                    cbbTool.SelectedIndex = 0;
                };
            }
        }

        public int ResetRetry()
        {
            return GetSettingValue("PSet", "RetryNum");
        }

        private void SetImage(string folder, PictureBox picbox)
        {
            try
            {
                var file = new DirectoryInfo(folder).GetFiles("*.jpg")
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();
                if (file != null)
                {
                    // Copia en memoria para no dejar el archivo bloqueado y liberar la imagen anterior
                    Image newImage;
                    using (var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var img = Image.FromStream(fs))
                    {
                        newImage = new Bitmap(img);
                    }

                    // stretch the image to fit the picture box
                    picbox.SizeMode = PictureBoxSizeMode.StretchImage;
                    Image oldImage = picbox.Image;
                    picbox.Image = newImage;
                    oldImage?.Dispose();
                    picbox.Refresh();
                }    
                    
            }
            catch (Exception)
            {
            }
        }

        private void ShowVisionImage()
        {
            string folder = GetSettingValue("PSet", "FtpPath");
            if (!Directory.Exists(folder))
                return;

            // Check if the folder exists and contains 100 images, clear all
            var files = new DirectoryInfo(folder).GetFiles("*.jpg");
            if (files.Length > 100)
            {
                foreach (var file in files)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (Exception)
                    {
                        // Handle exception if needed
                    }
                }
            }

            if (picVision.IsHandleCreated)
            {
                picVision.BeginInvoke(new Action(() =>
                {
                    SetImage(folder, picVision);
                }));
            }
        }

        #region override
        public override void AcuraStartUp()
        {
            base.AcuraStartUp();
            //EmbeddedExeTool exeToolXGX = new EmbeddedExeTool();
            //exeToolXGX.LoadEXE(pXGX, SysPara.ExePathXGX, XGX_Ctrl.IP);
        }

        #region Setting
        public override void BeforeProductionSetting()
        {
        }
        public override void AfterProductionSetting()
        {
            delayTrigger = GetSettingValue("PSet", "DelayTrigger");
            Retry = GetSettingValue("PSet", "Retry");
            RetryNum = GetSettingValue("PSet", "RetryNum");
        }
        public override void ExitProductionSettingPage()
        {
        }
        public override void BeforRecipeEditor()
        {
        }
        public override void AfterRecipeEditor()
        {
            if (Light != null && Light.IsConnected)
            {
                Light.SetBrightness(trackbarLight.Value);
            }
        }

        public override void ExitRecipeEditorPage()
        {
        }
        #endregion

        public override void AlwaysRun()
        {
        }

        //InitialReset - Run Once when press Initial.
        public override void InitialReset()
        {
            retryCount = 3;
            initProgSent = false;
            delayTrigger = GetSettingValue("PSet", "DelayTrigger");
            Retry = GetSettingValue("PSet", "Retry");
            RetryNum = GetSettingValue("PSet", "RetryNum");

            fcInitCamStart.TaskReset();
            fcAutoCamStart.TaskReset();
            fcAutoRetryStart.TaskReset();
            fcAutoManualStart.TaskReset();
        }

        //Initial - Run untill initial complete. bInitialOk = true
        public override void Initial()
        {
            fcInitCamStart.TaskRun();
        }

        //Run Once when press Start.
        public override void StartRun()
        {
            VisionTM.Restart();
            RetryTM.Restart();
            ManualTM.Restart();
        }

        //Run Once when press Start.
        public override void RunReset()
        {
            fcAutoCamStart.TaskReset();
            fcAutoRetryStart.TaskReset();
            fcAutoManualStart.TaskReset();
        }

        //Start Run Machine.
        public override void Run()
        {
            if (MiddleLayer.ControlF.rdbtnAll.Checked || MiddleLayer.ControlF.rdbtnOne.Checked
                || SysPara.Step[(int)Step.Inspection])
            {
                fcAutoCamStart.TaskRun();
                fcAutoRetryStart.TaskRun();
                fcAutoManualStart.TaskRun();
            }
        }

        public override void StopRun()
        {
        }

        public override void ServoOff()
        {
        }

        public override void ServoOn()
        {
        }

        public override void SetSpeed(int SpeedRate)
        {

        }

        public override void ModuleDispose()
        {

        }

        #endregion

        #region Light Manual
        private void btnLightConnect_Click(object sender, EventArgs e)
        {
            string sIP = GetSettingValue("PSet", "LightIP");
            int iPort = GetSettingValue("PSet", "LightPort");

            if (Light == null)
                Light = new ContrastechLight(sIP, iPort);

            if (!Light.Connect()) return;

            if (Light.IsConnected)
            {
                int val = GetRecipeValue("RSet", "LightIntensity");
                Light.SetBrightness(val);
            }
        }

        private void trackbarLight_Scroll(object sender, EventArgs e)
        {
            string value = trackbarLight.Value.ToString("D3");
            txtLight.Text = value;
        }

        private void trackbarLight_ValueChanged(object sender, EventArgs e)
        {
            string value = trackbarLight.Value.ToString("D3");
            txtLight.Text = value;
        }

        private void trackbarLight_MouseUp(object sender, MouseEventArgs e)
        {
            if (Light != null && Light.IsConnected)
            {
                Light.SetBrightness(trackbarLight.Value);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (Light != null && Light.IsConnected)
            {
                Light.SetBrightness(GetRecipeValue("RSet", "LightIntensity"));
                trackbarLight.Value = GetRecipeValue("RSet", "LightIntensity");
            }

        }
        #endregion

        #region Camera Manual
        private void XGX_OnCamDataReceived(object sender, KeyenceController.CamResult e)
        {
            // Result / IsOK / ResultReady ya los llena KeyenceController; aqui solo se actualiza la UI
            if (e.CamId == 1)
            {
                string[] lines = e.ResultString.Split(',');
                bool pass = e.Pass;

                if (txtCam?.IsHandleCreated == true)
                {
                    // BeginInvoke: no bloquear el hilo de recepcion de la camara
                    txtCam.BeginInvoke(new Action(() =>
                    {
                        txtCam.Lines = lines;
                        lblStatus.Text = pass ? "Total status: OK" : "Total status: NG";
                    }));
                }
            }
        }

        private void XGX_OnErrorCommand(object sender, string e)
        {
            MiddleLayer.LogF.AddLog(FunctionForms.LogForm.LogType.Production, $"Vision: camera error response {e}");
        }

        private void XGX_OnConnectionChanged(object sender, bool connected)
        {
            MiddleLayer.LogF.AddLog(FunctionForms.LogForm.LogType.Production,
                connected ? "Vision: camera connected" : "Vision: camera disconnected");
        }

        private void CreateXGX()
        {
            if (XGX != null) return;

            string sIP = GetSettingValue("PSet", "XGX_Ip");
            int iPort = GetSettingValue("PSet", "XGX_Port");
            int iTimeout = GetSettingValue("PSet", "XGX_Timeout");

            XGX = new KeyenceController(sIP, iPort, iTimeout);
            XGX.OnCamResultReceived += XGX_OnCamDataReceived;
            XGX.OnCamTriggerAck += XGX_OnCamTriggerAck;
            XGX.OnErrorCommand += XGX_OnErrorCommand;
            XGX.OnConnectionChanged += XGX_OnConnectionChanged;
        }

        // Espera el resultado pendiente de un trigger previo (ej. el de Change Tool),
        // limpia el resultado anterior y dispara. Regresa false mientras sigue esperando.
        private bool TriggerCam(CTimer tm)
        {
            if (!tm.IsOn(delayTrigger))
                return false;

            if (XGX.WaitingResult[1] && !tm.IsOn(delayTrigger + 2000))
                return false;

            XGX.ClearResult(1);
            XGX.Trigger(1);
            return true;
        }

        // Lee X,Y de alineacion; false si el resultado no trae esos datos
        private bool GetAlignment(out double X, out double Y)
        {
            X = Y = 0.0;
            string[] r = XGX.Result[1];
            return r != null && r.Length > 3
                && double.TryParse(r[2], NumberStyles.Float, CultureInfo.InvariantCulture, out X)
                && double.TryParse(r[3], NumberStyles.Float, CultureInfo.InvariantCulture, out Y);
        }

        private void XGX_OnCamTriggerAck(object sender, int e)
        {
            if (e == 1)
            {
                XGX.TriggerFinish[1] = true;
            }
        }

        private void btnCamConnect_Click(object sender, EventArgs e)
        {
            // El boton dice "Disconnect" cuando esta conectado
            if (XGX != null && XGX.IsConnected)
            {
                XGX.Disconnect();
                return;
            }

            CreateXGX();

            if (XGX.Connect())
                XGX.SetRunMode();
            else
                MessageBox.Show("Failed to Connect");
        }

        private void btnCamTrigger_Click(object sender, EventArgs e)
        {
            XGX.ClearResult(1);
            XGX.Trigger(1);
            txtCam.Clear();
        }

        private void btnCamRead_Click(object sender, EventArgs e)
        {
            txtCam.Invoke(new Action(() =>
            {
                txtCam.Lines = XGX.Result[1];
                lblStatus.Text = XGX.IsOK[1] ? "Total status: OK" : "Total status: NG";
            }));
        }

        private void btnRun_Click(object sender, EventArgs e)
        {
            XGX.SetRunMode();
        }

        private void btnChangeProg_Click(object sender, EventArgs e)
        {
            XGX.ChangeProgram(1, int.Parse(cbbProg.Text));
        }

        private void btnChangeTool_Click(object sender, EventArgs e)
        {
            int tool = int.Parse(cbbTool.Text);
            XGX.WriteVariables(new (string, double)[] { ("#Branch", tool )});
            XGX.Trigger(1);
            XGX.SendData("TE,1\r");
        }
        #endregion

        #region Flow Initial
        private FCResultType fcInitCamStart_FlowRun(object sender, EventArgs e)
        {
            Reset = false;
            return FCResultType.NEXT;
        }

        private FCResultType fcInitCamConnect_FlowRun(object sender, EventArgs e)
        {
            CreateXGX();

            if (XGX.Connect())
            {
                XGX.SendData("SS\r");
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcInitCamOutputRdy_FlowRun(object sender, EventArgs e)
        {
            if (!XGX.IsRunMode())
            {
                XGX.SetRunMode();
            }
            return FCResultType.NEXT;
        }

        private FCResultType fcInitCamLoadProg_FlowRun(object sender, EventArgs e)
        {
            if (!initProgSent)
            {
                CurrentProcess = VisionCavity.Keys.FirstOrDefault().Process;
                CurrentProgram = VisionCavity.Keys.FirstOrDefault().Program;
                XGX.ChangeProgram(1, CurrentProgram);
                initProgSent = true;
                VisionTM.Restart();
                return FCResultType.IDLE;
            }

            // Dar tiempo a que la camara termine de cargar el programa antes del primer trigger
            if (!VisionTM.IsOn(3000))
                return FCResultType.IDLE;

            initProgSent = false;
            return FCResultType.NEXT;
        }
        private FCResultType fcInitCamConnectLight_FlowRun(object sender, EventArgs e)
        {
            string sIP = GetSettingValue("PSet", "LightIP");
            int iPort = GetSettingValue("PSet", "LightPort");

            if (Light == null)
                Light = new ContrastechLight(sIP, iPort);

            if (!Light.Connect()) return FCResultType.IDLE;

            if (Light.IsConnected)
            {
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcInitCamSetBright_FlowRun(object sender, EventArgs e)
        {
            int val = GetRecipeValue("RSet", "LightIntensity");
            Light.SetBrightness(val);
            return FCResultType.NEXT;
        }
        private FCResultType fcInitCamEnd_FlowRun(object sender, EventArgs e)
        {
            bInitialOk = true;
            return FCResultType.IDLE;
        }
        #endregion

        #region FLow Auto
        #region Inspection Flow
        private FCResultType fcAutoCamStart_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.Step[(int)Step.Inspection])
                CurrentProcess = SysPara.CurrentProcess;

            CurrentProgram = VisionCavity.Keys.FirstOrDefault(k => k.Process == CurrentProcess).Program;
            
            visionCavity.Clear();
            if (VisionCavity.ContainsKey((CurrentProcess, CurrentProgram)))
            {
                foreach (var cavity in VisionCavity[(CurrentProcess, CurrentProgram)])
                {
                    if (cavity.Alignment)
                    {
                        if (SysPara.IsAlignment)
                            visionCavity.Enqueue((cavity.Cavity, cavity.Alignment, false));
                    }
                    else
                    {
                        if (SysPara.IsInspection)
                            visionCavity.Enqueue((cavity.Cavity, cavity.Alignment, false));
                    }
                }

                if (SysPara.ReInspection)
                {
                    foreach (var cavity in VisionCavity[(CurrentProcess, CurrentProgram)])
                    {
                        if (!cavity.Alignment) continue;
                        visionCavity.Enqueue((cavity.Cavity, false, true));
                    }
                }
            }

            if (visionCavity.Count > 0)
            {
                if (SysPara.Step[(int)Step.Inspection] || SysPara.IsOneProcess)
                {
                    XGX.ReadProgram();
                    return FCResultType.CASE1;
                }
                    
                VisionTM.Restart();
                return FCResultType.NEXT;
            }

            if (visionCavity.Count == 0)
            {
                if (!SysPara.IsOneProcess)
                {
                    if (CurrentProcess < VisionCavity.Keys.Max(k => k.Process))
                        CurrentProcess++;
                    else CurrentProcess = 1;
                }    
            }    
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoCamCheckCavity_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.Step[(int)Step.Inspection] || SysPara.Step[(int)Step.ReInspect])
            {
                VisionTM.Restart();
                return FCResultType.NEXT;
            }

            if (visionCavity.Count > 0 && !Reset)
            {
                currentCavity = visionCavity.Dequeue();
                VisionTM.Restart();
                return FCResultType.NEXT;
            }
            else
            {
                if (SysPara.IsOneProcess) 
                    return FCResultType.PREVIOUS;

                if (!Reset)
                {
                    if (CurrentProcess < VisionCavity.Keys.Max(k => k.Process))
                        CurrentProcess++;
                    else CurrentProcess = 1;
                }

                Reset = false;
                CurrentProgram = VisionCavity.Keys.FirstOrDefault(k => k.Process == CurrentProcess).Program;
                XGX.ReadProgram();
                VisionTM.Restart();
                return FCResultType.CASE1;
            }
        }

        private FCResultType fcAutoCamCheckProgram_FlowRun(object sender, EventArgs e)
        {
            if (VisionTM.IsOn(500))
            {
                if (XGX.CurrentProgram != CurrentProgram)
                {
                    XGX.ChangeProgram(1, CurrentProgram);
                    VisionTM.Restart();
                    return FCResultType.NEXT;
                }
                else
                {
                    if (!SysPara.IsOneProcess)
                        return FCResultType.PREVIOUS;

                    VisionTM.Restart();
                    return FCResultType.CASE1;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoCamChangeProg_FlowRun(object sender, EventArgs e)
        {
            if (VisionTM.IsOn(3000))
            {
                if (SysPara.Step[(int)Step.Inspection] || SysPara.IsOneProcess)
                    return FCResultType.NEXT;

                VisionTM.Restart();
                return FCResultType.PREVIOUS;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoCamChangeTool_FlowRun(object sender, EventArgs e)
        {
            GetTool(CurrentProcess, currentCavity.Cavity, out int tool);
            if (tool > 0)
            {
                tool = currentCavity.ReInspect ? tool + 1 : tool;
                XGX.WriteVariables(new (string, double)[] { ("#Branch", tool) });
                XGX.Trigger(1);
                VisionTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoCamWaitReady_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.hsCam.Ready && CurrentProcess == SysPara.CurrentProcess)
            {
                retryCount = 3;
                SysPara.hsCam.Ready = false;
                VisionTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoCamTriggerReady_FlowRun(object sender, EventArgs e)
        {
            if (VisionTM.IsOn(100))
            {
                XGX.SendData("TE,1\r");
                VisionTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoCamTrigger_FlowRun(object sender, EventArgs e)
        {
            if (TriggerCam(VisionTM))
            {
                VisionTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoCamRetry_FlowRun(object sender, EventArgs e)
        {
            retryCount--;
            VisionTM.Restart();
            return FCResultType.NEXT;
        }

        private FCResultType fcCamDelay_FlowRun(object sender, EventArgs e)
        {
            // Esperar el resultado real, no solo el eco "T1"
            if (XGX.ResultReady[1])
            {
                ShowVisionImage();
                return FCResultType.NEXT;
            }

            if (VisionTM.IsOn(3000))
            {
                if (retryCount == 0)
                {
                    MiddleLayer.LogF.AddLog(FunctionForms.LogForm.LogType.Production,
                        $"Vision:Timeout,Process:{CurrentProcess};Cavity:{currentCavity.Cavity}");
                    SysPara.Status[(int)Step.Inspection] = false;
                    SysPara.hsCam.Complete = true;
                    return FCResultType.PREVIOUS;
                }

                VisionTM.Restart();
                return FCResultType.CASE1;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoCamGetData_FlowRun(object sender, EventArgs e)
        {
            if (!VisionTM.IsOn(500))
                return FCResultType.IDLE;

            if (!XGX.IsOK[1] && !SysPara.IsDryRun)
            {
                if (retryCount == 0)
                {
                    SysPara.Status[(int)Step.Inspection] = false;
                    SysPara.hsCam.Complete = true;
                    MasterCore.Alarm.Show("9020", "Vision Inspection Failed");

                    MiddleLayer.LogF.AddLog(FunctionForms.LogForm.LogType.Production,
                        $"Vision:NG,Process:{CurrentProcess};Cavity:{currentCavity.Cavity}");

                    return FCResultType.PREVIOUS;
                }

                VisionTM.Restart();
                return FCResultType.CASE1;
            }

            if (currentCavity.Alignment)
            {
                // Sin X,Y validos no se aprieta con offset 0: se trata como NG
                if (!GetAlignment(out double X, out double Y) && !SysPara.IsDryRun)
                {
                    if (retryCount == 0)
                    {
                        SysPara.Status[(int)Step.Inspection] = false;
                        SysPara.hsCam.Complete = true;
                        MasterCore.Alarm.Show("9020", "Vision alignment data invalid");
                        return FCResultType.PREVIOUS;
                    }

                    VisionTM.Restart();
                    return FCResultType.CASE1;
                }

                if (!Alignment.ContainsKey(currentCavity.Cavity))
                    Alignment.Add(currentCavity.Cavity, (X, Y, true));
                else
                    Alignment[currentCavity.Cavity] = (X, Y, true);

                MiddleLayer.LogF.AddLog(FunctionForms.LogForm.LogType.Production,
                        $"Alignment:OK,Process:{CurrentProcess};Cavity:{currentCavity.Cavity}" +
                        $",X,{Alignment[currentCavity.Cavity].X},Y,{Alignment[currentCavity.Cavity].Y}"
                );
            }

            MiddleLayer.LogF.AddLog(FunctionForms.LogForm.LogType.Production,
                        $"Vision:OK,Process:{CurrentProcess};Cavity:{currentCavity.Cavity}");

            return FCResultType.NEXT;
        }

        private FCResultType fcAutoCamEnd_FlowRun(object sender, EventArgs e)
        {
            retryCount = 3;
            SysPara.Status[(int)Step.Inspection] = true;
            SysPara.hsCam.Complete = true;

            if (SysPara.Step[(int)Step.Inspection])
                return FCResultType.CASE1;

            return FCResultType.PREVIOUS;
        }

        private FCResultType fcAutoCamStep_FlowRun(object sender, EventArgs e)
        {
            return FCResultType.IDLE;
        }
        #endregion

        #region Retry Tightening Flow
        private FCResultType fcAutoRetryStart_FlowRun(object sender, EventArgs e)
        {
            if (ControlForm.errorTighten)
            {
                RetryProcess.Program = VisionCavity.Keys
                    .FirstOrDefault(k => k.Process == RetryProcess.Process).Program;
                XGX.ReadProgram();
                RetryTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoRetryCheckProg_FlowRun(object sender, EventArgs e)
        {
            if (RetryTM.IsOn(500))
            {
                if (XGX.CurrentProgram != RetryProcess.Program)
                {
                    XGX.ChangeProgram(1, RetryProcess.Program);
                    RetryTM.Restart();
                    return FCResultType.NEXT;
                }
                else
                {
                    RetryTM.Restart();
                    return FCResultType.CASE1;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoRetryChangeProg_FlowRun(object sender, EventArgs e)
        {
            if (RetryTM.IsOn(3000))
            {
                RetryTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoRetryChangeTool_FlowRun(object sender, EventArgs e)
        {
            GetTool(RetryProcess.Process, RetryProcess.Cavity, out int tool);
            if (tool > 0)
            {
                XGX.WriteVariables(new (string, double)[] { ("#Branch", tool) });
                XGX.Trigger(1);
                RetryTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoRetryWaitReady_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.hsRetry.Ready && RetryProcess.Process == SysPara.CurrentProcess)
            {
                retryCount = 3;
                SysPara.hsRetry.Ready = false;
                RetryTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoRetryTriggerReady_FlowRun(object sender, EventArgs e)
        {
            if (RetryTM.IsOn(100))
            {
                XGX.SendData("TE,1\r");
                RetryTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoRetryTrigger_FlowRun(object sender, EventArgs e)
        {
            if (TriggerCam(RetryTM))
            {
                RetryTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoRetryWaitData_FlowRun(object sender, EventArgs e)
        {
            if (XGX.ResultReady[1])
            {
                return FCResultType.NEXT;
            }

            if (RetryTM.IsOn(3000))
            {
                if (retryCount == 0)
                {
                    SysPara.Status[(int)Step.Inspection] = false;
                    SysPara.hsRetry.Complete = true;
                    return FCResultType.PREVIOUS;
                }

                RetryTM.Restart();
                return FCResultType.CASE1;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoRetryGetData_FlowRun(object sender, EventArgs e)
        {
            if (!RetryTM.IsOn(500))
                return FCResultType.IDLE;

            if (!XGX.IsOK[1])
            {
                if (retryCount == 0)
                {
                    SysPara.Status[(int)Step.Inspection] = false;
                    SysPara.hsRetry.Complete = true;
                    return FCResultType.PREVIOUS;
                }

                RetryTM.Restart();
                return FCResultType.CASE1;
            }

            // Check if the RetryProcess.Cavity is alignment
            bool alignment = VisionCavity.TryGetValue((RetryProcess.Process, RetryProcess.Program), out var cavities) &&
                             cavities.Any(c => c.Cavity == RetryProcess.Cavity && c.Alignment);
            if (alignment)
            {
                if (!GetAlignment(out double X, out double Y))
                {
                    if (retryCount == 0)
                    {
                        SysPara.Status[(int)Step.Inspection] = false;
                        SysPara.hsRetry.Complete = true;
                        return FCResultType.PREVIOUS;
                    }

                    RetryTM.Restart();
                    return FCResultType.CASE1;
                }

                if (!Alignment.ContainsKey(RetryProcess.Cavity))
                    Alignment.Add(RetryProcess.Cavity, (X, Y, true));
                else
                    Alignment[RetryProcess.Cavity] = (X, Y, true);
            }

            SysPara.hsRetry.Complete = true;
            XGX.ReadProgram();
            return FCResultType.NEXT;
        }

        private FCResultType fcAutoRetryWaitTighten_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.hsTightening.Complete)
            {
                if (SysPara.Status[(int)Step.Inspection])
                {
                    RetryTM.Restart();
                    return FCResultType.NEXT;
                }
                else
                {
                    RetryTM.Restart();
                    return FCResultType.PREVIOUS;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoRetryCheckProg2_FlowRun(object sender, EventArgs e)
        {
            if (RetryTM.IsOn(500))
            {
                if (XGX.CurrentProgram != CurrentProgram)
                {
                    XGX.ChangeProgram(1, CurrentProgram);
                    RetryTM.Restart();
                    return FCResultType.NEXT;
                }
                else
                {
                    RetryTM.Restart();
                    return FCResultType.CASE1;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoRetryChangeProg2_FlowRun(object sender, EventArgs e)
        {
            if (RetryTM.IsOn(3000))
            {
                RetryTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoRetryChangeTool2_FlowRun(object sender, EventArgs e)
        {
            GetTool(CurrentProcess, currentCavity.Cavity, out int tool);
            if (tool > 0)
            {
                tool = currentCavity.ReInspect ? tool + 1 : tool;
                XGX.WriteVariables(new (string, double)[] { ("#Branch", tool) });
                XGX.Trigger(1);
                RetryTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoRetryEnd_FlowRun(object sender, EventArgs e)
        {
            if (!RetryTM.IsOn(500)) return FCResultType.IDLE;

            XGX.SendData("TE,1\r");
            return FCResultType.PREVIOUS;
        }

        #region Manual Inspection Flow
        private FCResultType fcAutoManualStart_FlowRun(object sender, EventArgs e)
        {
            if(SysPara.hsManualInspection.Ready)
            {
                retryCount = 3;
                ManualTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoManualChangeTool_FlowRun(object sender, EventArgs e)
        {
            if (ManualForm.ManualCavity == 1)
                ManualTool = MiddleLayer.ManualF.GetRecipeValue("RSet", "InspectTool1");
            if (ManualForm.ManualCavity == 2)
                ManualTool = MiddleLayer.ManualF.GetRecipeValue("RSet", "InspectTool2");

            if(ManualTool > 0)
            {
                XGX.WriteVariables(new (string, double)[] { ("#Branch", ManualTool) });
                XGX.Trigger(1);
                ManualTM.Restart();
                return FCResultType.NEXT;
            }
            else
            {
                MasterCore.Alarm.Show("9002", "Manual Inspection Tool must larger than 0");
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoManualTriggerReady_FlowRun(object sender, EventArgs e)
        {
            if (ManualTM.IsOn(500))
            {
                XGX.SendData("TE,1\r");
                ManualTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoManualTrigger_FlowRun(object sender, EventArgs e)
        {
            if (TriggerCam(ManualTM))
            {
                ManualTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoManualWaitData_FlowRun(object sender, EventArgs e)
        {
            if(XGX.ResultReady[1])
            {
                ShowVisionImage();
                return FCResultType.NEXT;
            }

            if (ManualTM.IsOn(3000))
            {
                MasterCore.Alarm.Show("9003", "Vision Trigger Timeout");
                ManualTM.Restart();
                return FCResultType.PREVIOUS;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoManualGetData_FlowRun(object sender, EventArgs e)
        {
            if (!ManualTM.IsOn(500))
                return FCResultType.IDLE;
            if (!XGX.IsOK[1] && !SysPara.IsDryRun)
            {
                if(retryCount == 0)
                {
                    ManualResult = false;
                    SysPara.Status[(int)Step.Inspection] = false;
                    MasterCore.Alarm.Show("9020", "Vision Inspection Failed");

                    MiddleLayer.LogF.AddLog(FunctionForms.LogForm.LogType.Production,
                        $"Vision:NG,Process: Manual");
                    return FCResultType.PREVIOUS ;
                }
                ManualTM.Restart();
                return FCResultType.CASE1;
            }
            else
            {
                ManualResult = true;
                return FCResultType.NEXT;
            }
        }

        private FCResultType fcAutoManualRestoreTool_FlowRun(object sender, EventArgs e)
        {
            GetTool(CurrentProcess, currentCavity.Cavity, out int tool);
            if (tool > 0)
            {
                tool = currentCavity.ReInspect ? tool + 1 : tool;
                XGX.WriteVariables(new (string, double)[] { ("#Branch", tool) });
                XGX.Trigger(1);
                ManualTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoManualEnd_FlowRun(object sender, EventArgs e)
        {
            if(ManualTM.IsOn(500))
            {
                SysPara.hsManualInspection.Complete = true;
                return FCResultType.PREVIOUS;
            }
            return FCResultType.IDLE ;
        }

        private FCResultType fcAutoManualRetry_FlowRun(object sender, EventArgs e)
        {
            retryCount--;
            ManualTM.Restart();
            return FCResultType.NEXT;
        }

        #endregion

        private FCResultType fcAutoRetryRetry_FlowRun(object sender, EventArgs e)
        {
            retryCount--;
            RetryTM.Restart();
            return FCResultType.NEXT;
        }
        #endregion
        #endregion

        #region Other
        private void uiRefresh_Tick(object sender, EventArgs e)
        {
            if (XGX != null)
            {
                if (XGX.IsConnected)
                {
                    btnCamConnect.BackColor = Color.Red;
                    btnCamConnect.Text = "Disconnect";
                    grpbxCam.Enabled = true;
                    grpbxCam.BackColor = Color.LightGreen;
                }
                else
                {
                    btnCamConnect.BackColor = Color.LimeGreen;
                    btnCamConnect.Text = "Connect";
                    grpbxCam.Enabled = false;
                    grpbxCam.BackColor = Color.White;
                }
            }

            if (Light != null)
            {
                if (Light.IsConnected)
                {
                    btnLightConnect.BackColor = Color.Red;
                    btnLightConnect.Text = "Disconnect";
                    grpbxLight.BackColor = Color.LightGreen;
                }
                else
                {
                    btnLightConnect.BackColor = Color.Green;
                    btnLightConnect.Text = "Connect";
                    grpbxLight.BackColor = Color.White;
                }
            }
        }

        private void btnTestFtp_Click(object sender, EventArgs e)
        {
            ShowVisionImage();
        }
        #endregion
    }
}
