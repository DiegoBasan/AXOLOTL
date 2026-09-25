using Acura3._1.Classes.IVN;
using AcuraLibrary.Forms;
using Cerberus.Enum;
using Cerberus.Utility;
using Cerberus.Utility.FlowChartUtility;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Acura3._1.ModuleForms
{
    public partial class ManualForm : ModuleBaseForm
    {
        MTFManual Screw;

        private bool msgAccepted, resultReceived, overTighten, status = false;
        private int maxTightenTimes = 1000;
        public string RecipeFolder = "./ModuleData/Position";
        public bool IsManual = false;
        public static int NumRetry;
        public static int ManualCavity;
        public int TotalCavity;
        public bool tightenFail = false;
        public static bool isInspect = false;
        public int InspectTool;

        private CTimer ManualTM = new CTimer();

        public ManualForm()
        {
            InitializeComponent();
        }

        #region override
        public override void AcuraStartUp()
        {
            base.AcuraStartUp();
            ReadTighteningCount();
        }

        #region Setting
        public override void BeforeProductionSetting()
        {
        }
        public override void AfterProductionSetting()
        {
            IsManual = GetSettingValue("PSet", "Manual");
            maxTightenTimes = GetSettingValue("PSet", "Maintenance");
            isInspect = GetSettingValue("PSet", "funcInspect");    //Patrick
        }
        public override void ExitProductionSettingPage()
        {
        }
        public override void BeforRecipeEditor()
        {
        }
        public override void AfterRecipeEditor()
        {
            
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
            IsManual = GetSettingValue("PSet", "Manual");
            maxTightenTimes = GetSettingValue("PSet", "Maintenance");
            TotalCavity = GetRecipeValue("RSet", "NoS");
            //isInspect = GetSettingValue("PSet", "funcInspect");    //Patrick
            //InspectTool = GetRecipeValue("RSet", "InspectTool");    //Patrick


            fcInitStart.TaskReset();
            fcAutoTightenStart.TaskReset();
        }

        //Initial - Run untill initial complete. bInitialOk = true
        public override void Initial()
        {
            fcInitStart.TaskRun();
        }

        //Run Once when press Start.
        public override void StartRun()
        {
            ManualTM.Restart();
        }

        //Run Once when press Start.
        public override void RunReset()
        {
            fcAutoTightenStart.TaskReset();
        }

        //Start Run Machine.
        public override void Run()
        {
            fcAutoTightenStart.TaskRun();
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

        #region Function
        private void ReadTighteningCount()
        {
            string filePath = Path.Combine(RecipeFolder, "manul_screw_maint.txt");

            if (!File.Exists(filePath)) return;

            string text = File.ReadAllText(filePath);
            if (int.TryParse(text.Split(':')[1], out int count))
            {
                SysPara.TighteningCount = count;
            }
        }

        private void WriteTighteningCount(int count)
        {
            string filePath = Path.Combine(RecipeFolder, "manual_screw_maint.txt");
            if (!File.Exists(filePath)) return;

            File.WriteAllText(filePath, $"Current Tightening times: {count}");
        }
        #endregion

        #region Screw Manual
        private void TightenResultReceived(object sender, string result)
        {
            resultReceived = true;
            lblTorque.Text = $"Torque: {Screw.ResultData.PeakTorque.ToString("F3")} mNm";
            lblAngle.Text = $"Angle: {Screw.ResultData.TotalAngle.ToString()} °";
            lblTime.Text = $"Duration: {Screw.ResultData.Duration.ToString("F3")} s";

            WriteTighteningCount(++SysPara.TighteningCount);
        }

        private void MessageReceived(object sender, bool isAccepted)
        {
            msgAccepted = isAccepted;
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            string ip = GetSettingValue("PSet", "ScrewIP");
            int port = GetSettingValue("PSet", "ScrewPort");

            if (Screw != null) return;

            Screw = new MTFManual(ip, port) ;
            if (Screw.Connect())
            {
                Screw.ResultHandler += TightenResultReceived;
                Screw.MessageHandler += MessageReceived;
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (Screw == null) return;
            if (Screw.IsConnected)
            {
                Screw.StartCommunication();
            }
        }

        private void btnUnsub_Click(object sender, EventArgs e)
        {
            if (Screw == null) return;
            if (Screw.IsConnected)
            {
                Screw.UnsubscribeData();
            }
        }

        private void btnSelectPSet_Click(object sender, EventArgs e)
        {
            if (Screw == null) return;
            if (Screw.IsConnected)
            {
                Screw.SelectPSet(int.Parse(txtPSet.Text));
            }
        }

        private void btnResult_Click(object sender, EventArgs e)
        {
            if (Screw == null) return;
            if (Screw.IsConnected)
            {
                Screw.SubscribeData();
            }
        }
        #endregion

        #region Flow Initial
        private FCResultType fcInitStart_FlowRun(object sender, EventArgs e)
        {
            return FCResultType.NEXT;
        }

        private FCResultType fcInitCtrlConnectScrew_FlowRun(object sender, EventArgs e)
        {
            if (Screw == null)
            {
                string ip = GetSettingValue("PSet", "ScrewIP");
                int port = GetSettingValue("PSet", "ScrewPort");
                Screw = new MTFManual(ip, port);
                if (Screw.Connect())
                {
                    Screw.ResultHandler += TightenResultReceived;
                    Screw.MessageHandler += MessageReceived;
                    Screw.StartCommunication();
                }
                return FCResultType.IDLE;
            }

            if (Screw.IsConnected)
            {
                msgAccepted = false;
                int pset = GetRecipeValue("RSet", "PSet");
                Screw.SelectPSet(pset);
                return FCResultType.NEXT;
            }

            return FCResultType.IDLE;
        }

        private FCResultType fcInitChangePSet_FlowRun(object sender, EventArgs e)
        {
            if (msgAccepted)
            {
                RunTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcInitCtrlSubData_FlowRun(object sender, EventArgs e)
        {
            if (RunTM.IsOn(500))
            {
                Screw.SubscribeData();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcInitCtrlEnd_FlowRun(object sender, EventArgs e)
        {
            bInitialOk = true;
            return FCResultType.IDLE;
        }
        #endregion

        #region Flow Auto
        private FCResultType fcAutoTightenStart_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.hsManualTighten.Ready)
            {
                SysPara.hsManualTighten.Ready = false;
                NumRetry = GetSettingValue("PSet", "NumRetry");
                TotalCavity = GetRecipeValue("RSet", "NoS");
                isInspect = GetSettingValue("PSet", "funcInspect");    //Patrick
                

                //ManualCavity = 1;
                tightenFail = false;
                return FCResultType.NEXT;
            }    
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoTightenOn_FlowRun(object sender, EventArgs e)
        {
            if (iBusy.IsOn() && (iOK.IsOff() && iNG.IsOff()))
            {
                resultReceived = false;
                return FCResultType.NEXT;
            }    
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoTightenStatus_FlowRun(object sender, EventArgs e)
        {
            if ((iOK.IsOn() || iNG.IsOn() || iError.IsOn()) && iBusy.IsOff())
            {
                //Screw.SubscribeData();
                //msgAccepted = false;
                
                ManualTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoTightenReadData_FlowRun(object sender, EventArgs e)
        {
            if (!resultReceived || !ManualTM.IsOn(500))
                return FCResultType.IDLE;

            //Patrick upate UI
            string result = $"Torque:{Screw.ResultData.PeakTorque}cNm,Angle:{Screw.ResultData.TotalAngle}°";
            if(ManualCavity == 1)
            {
                MiddleLayer.ControlF.UpdateResult(
                $"Manual Screw 1",
                iOK.IsOn() ? true : false,
                result
                );
            }
            
            if (ManualCavity == 2)
            {
                MiddleLayer.ControlF.UpdateResult(
                    $"Manual Screw 2",
                    iOK.IsOn() ? true : false,
                    result
                    );
            }

            lvScrewData.Invoke(new MethodInvoker(delegate
            {
                lvScrewData.Items.Add(new ListViewItem(new string[]
                {
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    iOK.IsOn() ? "Passed" : "Failed",
                    (Screw.ResultData.PeakTorque / 10).ToString("F3"),
                    Screw.ResultData.TotalAngle.ToString(),
                    Screw.ResultData.Duration.ToString("F3"),
                    string.IsNullOrEmpty(SysPara.Barcode) ? "Barcode" : SysPara.Barcode,
                }));
            }));
            //Screw.UnsubscribeData();

            //Patrick update MES and Tar file
            bool ret = iOK.IsOn();
            
            if (ret)
            {
                if (ManualCavity == 1)
                {
                    SysPara.MESData.Add(( $"Torque Manual Screw {1}", Screw.ResultData.PeakTorque ));
                    SysPara.MESData.Add(( $"Angle Manual Screw {1}", Screw.ResultData.TotalAngle ));
                }
                if (ManualCavity == 2)
                {
                    SysPara.MESData.Add(($"Torque Manual Screw {2}", Screw.ResultData.PeakTorque));
                    SysPara.MESData.Add(($"Angle Manual Screw {2}", Screw.ResultData.TotalAngle));
                }
                //ManualCavity++;
                tightenFail = false;
                NumRetry = GetSettingValue("PSet", "NumRetry");

                return FCResultType.NEXT;
            }
            if (!ret) tightenFail = true;

            if (NumRetry > 0)
                return FCResultType.CASE1;
            else
                return FCResultType.CASE2;
        }

        private FCResultType fcAutoTightenEnd_FlowRun(object sender, EventArgs e)
        {
            // Add logging for Tightening result
            MiddleLayer.LogF.AddLog(FunctionForms.LogForm.LogType.Production,
            $"Code: {SysPara.Barcode}, " +
            $"Tightening Result: {Screw.ResultData.PeakTorque:F3} mNm, " +
            $"{Screw.ResultData.TotalAngle} °, " +
            $"{Screw.ResultData.Duration:F3} s, " +
            $"Status: {(iOK.IsOn() ? "OK" : "NG")}");

            SysPara.hsManualTighten.Complete = true;
            return FCResultType.PREVIOUS;
        }

        //Patrick update check cavity
        private FCResultType fcAutoTightenCheckCavity_FlowRun(object sender, EventArgs e)
        {
            return FCResultType.NEXT;
            if (ManualCavity <= 2)
                return FCResultType.PREVIOUS;
            else
                return FCResultType.NEXT;
        }

        private FCResultType fcAutoTightenRetry_FlowRun(object sender, EventArgs e)
        {
            NumRetry--;
            return FCResultType.NEXT;
        }
        #endregion

        private void uiRefresh_Tick(object sender, EventArgs e)
        {
            if (Screw != null)
            {
                if (Screw.IsConnected)
                {
                    btnConnect.BackColor = System.Drawing.Color.Red;
                    btnConnect.Text = "Disconnect";
                    grpbxScrew.Enabled = true;
                    grpbxScrew.BackColor = Color.LimeGreen;

                    if (txtResponse?.IsHandleCreated == true)
                    {
                        txtResponse.Invoke(new MethodInvoker(delegate
                        {
                            txtResponse.Clear();
                            //divide string into lines, each line contains 10 characters
                            string[] lines = Screw.Response.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string line in lines)
                            {
                                txtResponse.AppendText(line + Environment.NewLine);
                            }
                        }));

                        if (Screw.ControllerName.Length > 0) lblName.Text = Screw.ControllerName;
                    }
                }
                else
                {
                    btnConnect.BackColor = System.Drawing.Color.LimeGreen;
                    btnConnect.Text = "Connect";
                    grpbxScrew.Enabled = false;
                    grpbxScrew.BackColor = System.Drawing.Color.White;
                }
            }

            txtTightenCount?.Invoke(new MethodInvoker(delegate
            {
                txtTightenCount.Text = SysPara.TighteningCount.ToString();
            }));

            if (status != resultReceived)
            {
                status = resultReceived;
                if (status)
                {
                    SysPara.TighteningCount++;
                    WriteTighteningCount(SysPara.TighteningCount);
                }
            }

            if (SysPara.TighteningCount < maxTightenTimes) overTighten = false;

            if (SysPara.TighteningCount >= maxTightenTimes && !overTighten)
            {
                overTighten = true;
                MessageForm message = new MessageForm()
                {
                    lbltitle = { Text = "Screw Head Maintenance" },
                    lblMessage = { Text = "Change or maintain screw head" },
                    btnRetry = { Text = "Confirm" },
                    btnSkip = { Visible = false },
                };
                message.TopMost = true;
                message.ShowDialog();
            }
        }

        private void btnResetCount_Click(object sender, EventArgs e)
        {
            SysPara.TighteningCount = 0;
            WriteTighteningCount(SysPara.TighteningCount);
        }
    }
}
