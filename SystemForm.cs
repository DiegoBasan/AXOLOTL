using Acura3._1.FunctionForms;
using AcuraLibrary.Forms;
using Cerberus.CoreControls;
using Cerberus.CoreEngine.GlobalEngine.EtherCAT;
using Cerberus.CoreEngine.Master;
using Cerberus.CoreEngine.Vision.VisionCore.Light;
using Cerberus.Utility;
using Cerberus.Utility.FlowChartUtility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static Acura3._1.Forms.SignalTowerForm;
using static Cerberus.Utility.MachineMatrix;

namespace Acura3._1.ModuleForms
{
    public partial class SystemForm : ModuleBaseForm
    {
        //public MESForm MesF = new MESForm();
        //public MESManualForm MESManualFrm = new MESManualForm();
        private bool StatusChange_Start = false;
        private bool StatusChange_Stop = false;
        private bool StatusChange_AlarmReset = false;
        private bool StatusChange_ModeSwitch = false;
        private bool StatusChange_SafetyDoor = false;
        private bool StatusChange_SafetyCurtain = false;
        private bool StatusChange_Emergency = false;
        private bool StatusChange_AirPressure = false;
        private CTimer BlinkTM = new CTimer();
        private CTimer BuzzerTM = new CTimer();
        private bool BlinkIsOn = false;
        private const int BlinkTime = 500;
        public bool BuzzerNeedWork = false;

        private string CurrentRecipe = null;//Teong CFX 

        public List<MotorParameter> MotorMaximumParameter = new List<MotorParameter>();
        public struct MotorParameter
        {
            public string Port;
            public double MaximumSpeed;
        }


        public SystemForm()
        {
            InitializeComponent();

            //MesF.TopLevel = false;
            //this.panel1.Controls.Add(MesF);
            //MesF.Show();

            //MESManualFrm.Visible = true;
            //MESManualFrm.TopLevel = false;
            //MESManualFrm.Dock = DockStyle.Top;
            //MESManualFrm.FormBorderStyle = FormBorderStyle.None;
            //this.panel1.Controls.Add(MESManualFrm);

            FlowChartMessage.MessageFormRaise += FlowChartMessage_MessageFormRaise;
            MessageForm.MuteRaise += MessageForm_MuteRaise;
            FlowChartMessage.PauseRaise += FlowChartMessage_PauseRaise;
            TCPManager.AddEventLog += TCPManager_AddEventLog;
        }



        private void TCPManager_AddEventLog(object sender, EventArgs e)
        {
            MiddleLayer.LogF.AddLog(LogForm.LogType.EventFlow, sender.ToString());
        }

        private void FlowChartMessage_PauseRaise(object sender, EventArgs e)
        {
            MiddleLayer.StopRun();

            if (SysPara.SystemMode == RunMode.RUNWARM || SysPara.SystemMode == RunMode.INITIALWARM)
            {
                SysPara.SystemMode = RunMode.IDLE;
                SysPara.SystemInitialOk = false;
            }
        }

        private void MessageForm_MuteRaise(object sender, EventArgs e)
        {
            BuzzOff();
        }

        private void FlowChartMessage_MessageFormRaise(object sender, EventArgs e)
        {
            // BuzzerNeedWork = true;
            BuzzOn();
        }

        public override void AcuraStartUp()
        {
            base.AcuraStartUp();
            OB_ServoOn.On();
        }

        public override void AfterRecipeEditor()
        {

        }

        public override void AfterProductionSetting()
        {
            //MesF.WriteMesTisConfig(SysPara.MESDirectory + "\\" + SysPara.RecipeName);

            SysPara.IsMES = GetSettingValue("PSet", "MES");
            SysPara.IsTightening = GetSettingValue("PSet", "Tightening");
            SysPara.IsInspection = GetSettingValue("PSet", "Inspection");
            SysPara.IsAlignment = GetSettingValue("PSet", "Alignment");
            SysPara.ReInspection = GetSettingValue("PSet", "ReInspection");
            SysPara.PV_Check = GetSettingValue("PSet", "PV_Check");

            // MES
            SysPara.RouteLocal = GetSettingValue("PSet", "RouteLocal");
            SysPara.RouteExternal = GetSettingValue("PSet", "RouteExternal");
            SysPara.SN = GetSettingValue("PSet", "SN");
            SysPara.CustomerName = GetSettingValue("PSet", "CusName");
            SysPara.Division = GetSettingValue("PSet", "Division");
            SysPara.StationName = GetSettingValue("PSet", "StationName");
            SysPara.ProcessStep = GetSettingValue("PSet", "ProcessStep");
            SysPara.Site = GetSettingValue("PSet", "Site");
        }

        public override void InitialReset()
        {
            SysPara.mcMode = Classes.IVN.MachineOperationState.Not_Ready;
            SysPara.IsMES = GetSettingValue("PSet", "MES");
            SysPara.IsTightening = GetSettingValue("PSet", "Tightening");
            SysPara.IsInspection = GetSettingValue("PSet", "Inspection");
            SysPara.IsAlignment = GetSettingValue("PSet", "Alignment");
            SysPara.ReInspection = GetSettingValue("PSet", "ReInspection");
            SysPara.PV_Check = GetSettingValue("PSet", "PV_Check");

            // MES
            SysPara.RouteLocal = GetSettingValue("PSet", "RouteLocal");
            SysPara.RouteExternal = GetSettingValue("PSet", "RouteExternal");
            SysPara.SN = GetSettingValue("PSet", "SN");
            SysPara.CustomerName = GetSettingValue("PSet", "CusName");
            SysPara.Division = GetSettingValue("PSet", "Division");
            SysPara.StationName = GetSettingValue("PSet", "StationName");
            SysPara.ProcessStep = GetSettingValue("PSet", "ProcessStep");
            SysPara.Site = GetSettingValue("PSet", "Site");

            fmLog.TaskRun();
            foreach (var item in MasterCore.MachineMatrixList)
            {
                item.Reset(_Index.All);
            }
            foreach (var item in MasterCore.CoreTCPManagerList)
            {
                item.ResetAuto();
                item.Disconnect();
            }
        }

        public override void RunReset()
        {
            foreach (var item in MasterCore.CoreTCPManagerList)
            {
                item.ResetAuto();
                item.Disconnect();
            }

            fcAutoSafetyScan.TaskReset();
        }

        public override void AlwaysRun()
        {
            if (CurrentRecipe != SysPara.RecipeName)
            {
                CurrentRecipe = SysPara.RecipeName;

            }

            if (SysPara.Simulation)
                return;

            if (!SysPara.isSettingRefresh)
            {
                SysPara.IsDryRun = GetSettingValue("PSet", "Dryrun");
                MasterCore.CoreIOManager.SetDryrun(SysPara.IsDryRun);
            }


            #region BlinkTimer
            if (BlinkTM.IsOn(BlinkTime))
            {
                BlinkIsOn = !BlinkIsOn;
                BlinkTM.Restart();
            }
            #endregion

            #region MaintainMode Scan
            if (IB_ModeSwitch.IsOn() && IB_ModeSwitch.Enabled)
            {
                if (!StatusChange_ModeSwitch)
                {
                    MiddleLayer.StopRun();
                    BackupMotorMaximumSpeed();
                    SwitchMotorToMaintenanceSpeed();
                    OB_FluorescentLight.On();
                    //OB_DoorInterlock.On();
                    StatusChange_ModeSwitch = true;
                    OB_LockLeftBackDoor.Off();
                    OB_LockRightBackDoor.Off();
                }
                SysPara.IsMaintenanceMode = true;
                SysPara.MState.IsMaintenanceMode = true; // CFX - GLobal automation
            }
            else
            {
                if (StatusChange_ModeSwitch)
                {
                    MiddleLayer.StopRun();
                    RestoreMotorMaximumSpeed();
                    //OB_DoorInterlock.Off();
                    StatusChange_ModeSwitch = false;
                }
                OB_LockLeftBackDoor.On();
                OB_LockRightBackDoor.On();
                SysPara.IsMaintenanceMode = false;
                SysPara.MState.IsMaintenanceMode = false; // CFX - Global automation

            }
            #endregion

            #region Emergency Stop Scan
            if (IB_SafetyReady.IsOff() && IB_SafetyReady.Enabled)
            {
                if (StatusChange_Emergency)
                {
                    SysPara.SystemRun = false;
                    SysPara.SystemMode = RunMode.IDLE;
                    SysPara.SystemInitialOk = false;
                    StatusChange_Emergency = false;
                }
                MasterCore.Alarm.Show("1020");
            }
            else
            {
                if (!StatusChange_Emergency)
                    StatusChange_Emergency = true;
            }
            #endregion

            #region Safety door Scan
            if (IB_BackSafetyDoorLeft.IsOff() || IB_BackSafetyDoorRight.IsOff()
               /*|| IB_LockLeftBackSensor.IsOff() || IB_LockRightBackSensor.IsOff()*/)
            {
                if (!StatusChange_SafetyDoor)
                {
                    OB_FluorescentLight.On();
                    StatusChange_SafetyDoor = true;

                    
                }
                if (!SysPara.IsMaintenanceMode)
                    MasterCore.Alarm.Show("1021");
            }
            else
            {
                if (StatusChange_SafetyDoor)
                {
                    OB_FluorescentLight.Off();
                    StatusChange_SafetyDoor = false;
                }
            }
            #endregion

            #region Safety Curtain Scan
            if (IB_TopAreaSensor.IsOn() || IB_MiddleAreaSensor.IsOn()
                || IB_BottomAreaSensor.IsOn())
            {
                if (!StatusChange_SafetyCurtain)
                {
                    //OB_FluorescentLight.On();
                    StatusChange_SafetyCurtain = true;
                }

                MiddleLayer.StopAllMotor();

                if (SysPara.SystemMode == RunMode.INITIAL)
                    MasterCore.Alarm.Show("1020");
            }
            else
            {
                if (StatusChange_SafetyCurtain)
                {
                    //OB_FluorescentLight.Off();
                    StatusChange_SafetyCurtain = false;
                }
            }
            #endregion

            #region Air Pressure Scan
            if (!IB_AirPressure.IsOn() && IB_AirPressure.Enabled)
            {
                if (StatusChange_AirPressure)
                    StatusChange_AirPressure = false;

                SysPara.SystemMode = RunMode.IDLE;
                SysPara.SystemInitialOk = false;
                MasterCore.Alarm.Show("1022");
            }
            else
            {
                if (!StatusChange_AirPressure)
                {
                    MasterCore.Alarm.Clear();
                    MasterCore.Alarm.DoStop = false;
                    StatusChange_AirPressure = true;
                }
            }
            #endregion

            #region Start Button Scan

            if (IB_BtnStart.IsOn() && IB_BtnStart.Enabled)
            {
                if (StatusChange_Start && SysPara.bSafetyReady)
                {
                    StatusChange_Start = false;
                    RefreshDifferentThreadUI(MiddleLayer.MainF.btnStart, () =>
                    {
                        if (MiddleLayer.MainF.btnStart.Enabled)
                            MiddleLayer.MainF.btnStart.PerformClick();
                    });
                }
            }
            else
            {
                if (!StatusChange_Start)
                    StatusChange_Start = true;
            }
            #endregion

            #region Stop Button Scan
            if (IB_BtnStop.IsOn() && IB_BtnStop.Enabled)
            {
                if (StatusChange_Stop)
                {
                    StatusChange_Stop = false;
                    RefreshDifferentThreadUI(MiddleLayer.MainF.btnPause, () =>
                    {
                        if (MiddleLayer.MainF.btnPause.Enabled)
                            MiddleLayer.MainF.btnPause.PerformClick();
                    });
                }
                BuzzOff();
            }
            else
            {
                if (!StatusChange_Stop)
                    StatusChange_Stop = true;
            }
            #endregion

            #region Alarm Reset Button Scan
            if (IB_BtnAlarmReset.IsOn() && IB_BtnAlarmReset.Enabled)
            {
                if (StatusChange_AlarmReset)
                {
                    StatusChange_AlarmReset = false;
                    RefreshDifferentThreadUI(MiddleLayer.MainF.btnAlarmReset, () =>
                    {
                        MiddleLayer.MainF.btnAlarmReset.PerformClick();
                    });

                }
                BuzzOff();
                BuzzerTM.Restart();
            }
            else
            {
                if (!StatusChange_AlarmReset)
                    StatusChange_AlarmReset = true;
            }
            #endregion

            #region Button Light
            if (SysPara.SystemInitialOk)
            {
                if (SysPara.IsMaintenanceMode)
                {
                    if (BlinkIsOn)
                        OB_ResetLight.On();
                    else
                        OB_ResetLight.Off();
                }
                else
                    OB_ResetLight.On();
            }
            else
                OB_ResetLight.Off();
            #endregion

            #region Signal Tower Control
            if (MasterCore.Alarm.IsError)
            {
                if (!SysPara.CFX.IsDowntimeRecord)
                {
                    SysPara.CFX.IsDowntimeRecord = true;
                    SysPara.CFX.DowntimeRecordStartTime = DateTime.Now;
                }
                if (SysPara.MState.IsMachineNoMaterial)
                {
                    MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(SignalTowerStatusType.MachineNoMaterial);
                    if (!BuzzerNeedWork)
                        SysPara.CFX.StationStateChanged(CFX.Structures.ResourceState.USD_Repair); //Under Repair
                    else
                        SysPara.CFX.StationStateChanged(CFX.Structures.ResourceState.USD_ChangeOfConsumables); //Feeder No Material
                }
                else
                {
                    MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(SignalTowerStatusType.MessageError);
                    if (!BuzzerNeedWork)
                        SysPara.CFX.StationStateChanged(CFX.Structures.ResourceState.USD_Repair); //Under Repair
                    else
                        SysPara.CFX.StationStateChanged(CFX.Structures.ResourceState.USD); //ALARM
                }
            }
            else if (SysPara.MState.IsMaintenanceMode)
            {
                MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(SignalTowerStatusType.MachineIdle);
                SysPara.CFX.StationStateChanged(CFX.Structures.ResourceState.SDT); //Maintenance
            }
            else if (SysPara.SystemRun)
            {
                if (SysPara.SystemMode == RunMode.RUN)
                {
                    if (SysPara.MState.IsMachineNoMaterial)
                    {
                        MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(SignalTowerStatusType.MachineNoMaterial);
                        SysPara.CFX.StationStateChanged(CFX.Structures.ResourceState.USD_ChangeOfConsumables); //Feeder No Material
                    }
                    else if (SysPara.MState.IsMachineBlocked)
                    {
                        MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(MasterCore.Alarm.IsWarning ? SignalTowerStatusType.MessageWarning : SignalTowerStatusType.MachineBlocked);
                        SysPara.CFX.StationStateChanged(MasterCore.Alarm.IsWarning ? CFX.Structures.ResourceState.PRD_Engineering : CFX.Structures.ResourceState.SBY_NoProductBlocked); //Blocked
                    }
                    else if (SysPara.MState.IsMachineStarving)
                    {
                        MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(MasterCore.Alarm.IsWarning ? SignalTowerStatusType.MessageWarning : SignalTowerStatusType.MachineStarving);
                        SysPara.CFX.StationStateChanged(MasterCore.Alarm.IsWarning ? CFX.Structures.ResourceState.PRD_Engineering : CFX.Structures.ResourceState.SBY_NoProductStarved); //Starving
                    }
                    else if (SysPara.MState.IsMachineRetryProcess)
                    {
                        MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(MasterCore.Alarm.IsWarning ? SignalTowerStatusType.MessageWarning : SignalTowerStatusType.MachineRetryProcess);
                        SysPara.CFX.StationStateChanged(MasterCore.Alarm.IsWarning ? CFX.Structures.ResourceState.PRD_Engineering : CFX.Structures.ResourceState.PRD); //Run(for Retry process)
                    }
                    else if (SysPara.MState.IsMachineLowMaterial)
                    {
                        MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(MasterCore.Alarm.IsWarning ? SignalTowerStatusType.MessageWarning : SignalTowerStatusType.MachineLowMaterial);
                        SysPara.CFX.StationStateChanged(MasterCore.Alarm.IsWarning ? CFX.Structures.ResourceState.PRD_Engineering : CFX.Structures.ResourceState.PRD_RegularWork); //Low Material
                    }
                    else
                    {
                        MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(MasterCore.Alarm.IsWarning ? SignalTowerStatusType.MessageWarning : SignalTowerStatusType.MachineRunning);
                        SysPara.CFX.StationStateChanged(MasterCore.Alarm.IsWarning ? CFX.Structures.ResourceState.PRD_Engineering : CFX.Structures.ResourceState.PRD); //Run
                    }
                }
                else if (SysPara.SystemMode == RunMode.INITIAL)
                {
                    MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(MasterCore.Alarm.IsWarning ? SignalTowerStatusType.MessageWarning : SignalTowerStatusType.MachineInitialize);
                    SysPara.CFX.StationStateChanged(MasterCore.Alarm.IsWarning ? CFX.Structures.ResourceState.PRD_Engineering : CFX.Structures.ResourceState.PRD); //Run
                }
            }
            else // Stop
            {
                if (SysPara.MState.IsMachineNoMaterial)
                {
                    MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(SignalTowerStatusType.MachineNoMaterial);
                    if (!BuzzerNeedWork)
                        SysPara.CFX.StationStateChanged(CFX.Structures.ResourceState.USD_Repair); //Under Repair
                    else
                        SysPara.CFX.StationStateChanged(CFX.Structures.ResourceState.USD_ChangeOfConsumables); //Feeder No Material
                }
                else
                {
                    MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(MasterCore.Alarm.IsWarning ? SignalTowerStatusType.MessageWarning : SignalTowerStatusType.MachineIdle);
                    SysPara.CFX.StationStateChanged(MasterCore.Alarm.IsWarning ? CFX.Structures.ResourceState.PRD_Engineering : CFX.Structures.ResourceState.NST); //IDLE

                    if (SysPara.CFX.IsDowntimeRecord)
                    {
                        SysPara.CFX.IsDowntimeRecord = false;
                        SysPara.CFX.DowntimeRecordEndTime = DateTime.Now;
                        SysPara.CFX.ScheduledDowntime(SysPara.CFX.DowntimeRecordStartTime, SysPara.CFX.DowntimeRecordEndTime, 00000, "ERROR to IDLE");
                    }
                }
            }
            //Refresh Output Status
            Control_SignalTower(MiddleLayer.SignalTowerF.GreenLightStatus, OB_GreenLight);
            Control_SignalTower(MiddleLayer.SignalTowerF.YellowLightStatus, OB_YellowLight);
            Control_SignalTower(MiddleLayer.SignalTowerF.RedLightStatus, OB_RedLight);
            if (MiddleLayer.SignalTowerF.bHaveChangeStatus)
            {
                BuzzerNeedWork = true;
                MiddleLayer.SignalTowerF.bHaveChangeStatus = false;
            }
            if (BuzzerNeedWork && !SysPara.isSettingRefresh && !GetSettingValue("PSet", "DisableSafetyBuzzer") && BuzzerTM.IsOn(1000))
                Control_SignalTower(MiddleLayer.SignalTowerF.BuzzerStatus, OB_Buzzer);
            else
                BuzzerNeedWork = false;
            #endregion

            /*
            #region Signal Tower Control
            if (SysPara.SystemRun)
            {
                if (MasterCore.Alarm.IsWarning)
                    MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(SignalTowerForm.SignalTowerStatusType.MessageWarning);
                else if (MasterCore.Alarm.IsInformation)
                    MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(SignalTowerForm.SignalTowerStatusType.MessageInformation);
                else
                {
                    if (SysPara.SystemMode == RunMode.RUN)
                        MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(SignalTowerForm.SignalTowerStatusType.MachineRunning);
                    if (SysPara.SystemMode == RunMode.INITIAL)
                        MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(SignalTowerForm.SignalTowerStatusType.MachineInitialize);
                }
            }
            else
            {
                if (MasterCore.Alarm.IsError)
                    MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(SignalTowerForm.SignalTowerStatusType.MessageError);
                else
                    MiddleLayer.SignalTowerF.SwitchSignalTowerStatus(SignalTowerForm.SignalTowerStatusType.MachineIdle);
            }
            //Refresh Output Status
            Control_SignalTower(MiddleLayer.SignalTowerF.GreenLightStatus, OB_GreenLight);
            Control_SignalTower(MiddleLayer.SignalTowerF.YellowLightStatus, OB_YellowLight);
            Control_SignalTower(MiddleLayer.SignalTowerF.RedLightStatus, OB_RedLight);
            if (MiddleLayer.SignalTowerF.bHaveChangeStatus)
            {
                BuzzerNeedWork = true;
                MiddleLayer.SignalTowerF.bHaveChangeStatus = false;
            }
            if (BuzzerNeedWork && !SysPara.isSettingRefresh && !GetSettingValue("PSet", "DisableSafetyBuzzer") && BuzzerTM.IsOn(1000))
                Control_SignalTower(MiddleLayer.SignalTowerF.BuzzerStatus, OB_Buzzer);
            else
                BuzzerNeedWork = false;
            #endregion
            */

            if (!StatusChange_SafetyDoor && !StatusChange_SafetyCurtain)
                SysPara.bSafetyReady = true;
            else
                SysPara.bSafetyReady = false;
        }

        public override void StartRun()
        {
            SysPara.mcMode = Classes.IVN.MachineOperationState.Auto_Run;

            OB_FluorescentLight.Off();
            if (bInitialOk)
            {
                MiddleLayer.LogF.AddMachineStatusEvent(LogForm.MachineStatusType.MachineRun);

                foreach (var item in MasterCore.MachineMatrixList)
                {
                    item.Resume(_Index.All);
                }
            }
        }

        public override void Run()
        {
            fcAutoSafetyScan.TaskRun();
        }

        public override void StopRun()
        {
            SysPara.mcMode = Classes.IVN.MachineOperationState.Idle;

            //OB_FluorescentLight.On();
            MiddleLayer.LogF.AddMachineStatusEvent(LogForm.MachineStatusType.MachineIdle);
            foreach (var item in MasterCore.MachineMatrixList)
            {
                item.Pause(_Index.All);
            }

        }

        public override void ServoOn()
        {

        }
        public override void ServoOff()
        {
        }
        public static void RefreshDifferentThreadUI(Control control, Action action)
        {
            if (control.InvokeRequired)
            {
                Action refreshUI = new Action(action);
                control.Invoke(refreshUI);
            }
            else
            {
                action.Invoke();
            }
        }

        public void BackupMotorMaximumSpeed()
        {
            MotorMaximumParameter.Clear();
            foreach (var control in MasterCore.CoreMotorManager.MotorList)
                if (control is Motor)
                {
                    MotorParameter d = new MotorParameter();
                    d.Port = ((Motor)control).Port;
                    d.MaximumSpeed = ((Motor)control).MaximumSpeed;
                    MotorMaximumParameter.Add(d);
                }

            foreach (var control in MasterCore.CoreRobotManager.RobotScaraList)
                if (control is RobotScara)
                {
                    MotorParameter d = new MotorParameter();
                    d.Port = ((RobotScara)control).SerialNo;
                    d.MaximumSpeed = ((RobotScara)control).WorkSpeed;
                    MotorMaximumParameter.Add(d);
                }

            foreach (var control in MasterCore.CoreRobotManager.Robot6AxisList)
                if (control is Robot6Axis)
                {
                    MotorParameter d = new MotorParameter();
                    d.Port = ((Robot6Axis)control).IPAddress;
                    d.MaximumSpeed = ((Robot6Axis)control).WorkSpeed;
                    MotorMaximumParameter.Add(d);
                }
        }

        public void RestoreMotorMaximumSpeed()
        {
            foreach (var control in MasterCore.CoreMotorManager.MotorList)
                if (control is Motor)
                {
                    foreach (MotorParameter d in MotorMaximumParameter)
                    {
                        string port = ((Motor)control).Port;
                        if (port == d.Port)
                            ((Motor)control).MaximumSpeed = d.MaximumSpeed;
                    }
                }

            foreach (var control in MasterCore.CoreRobotManager.RobotScaraList)
                if (control is RobotScara)
                {
                    foreach (MotorParameter d in MotorMaximumParameter)
                    {
                        string port = ((RobotScara)control).SerialNo;
                        if (port == d.Port)
                            ((RobotScara)control).SetWorkSpeed(d.MaximumSpeed);
                    }
                }

            foreach (var control in MasterCore.CoreRobotManager.Robot6AxisList)
                if (control is Robot6Axis)
                {
                    foreach (MotorParameter d in MotorMaximumParameter)
                    {
                        string port = ((Robot6Axis)control).IPAddress;
                        if (port == d.Port)
                            ((Robot6Axis)control).WorkSpeed = Convert.ToSingle(d.MaximumSpeed);
                    }
                }
        }

        private void SwitchMotorToMaintenanceSpeed()
        {
            foreach (var control in MasterCore.CoreMotorManager.MotorList)
                if (control is Motor)
                    if (((Motor)control).MaximumSpeed > 33)
                    {
                        ((Motor)control).MaximumSpeed = 33;
                    }

            foreach (var control in MasterCore.CoreRobotManager.RobotScaraList)
                if (control is RobotScara)
                    if (((RobotScara)control).WorkSpeed > 33)
                    {
                        ((RobotScara)control).SetWorkSpeed(33);
                    }

            foreach (var control in MasterCore.CoreRobotManager.Robot6AxisList)
                if (control is Robot6Axis)
                    if (((Robot6Axis)control).WorkSpeed > 33)
                    {
                        ((Robot6Axis)control).WorkSpeed = 33;
                    }
        }

        private void Control_SignalTower(Status status, Output OB)
        {
            switch (status)
            {
                case Status.Off:
                    OB.Off();
                    break;
                case Status.On:
                    OB.On();
                    break;
                case Status.Blink:
                    if (BlinkIsOn)
                        OB.On();
                    else
                        OB.Off();
                    break;
            }


        }

        public void BuzzOff()
        {

            BuzzerNeedWork = false;

            OB_Buzzer.Off();
        }

        public void BuzzOn()
        {
            //  BuzzerNeedWork = true;
            OB_Buzzer.On();
        }

        public void BuzzBlinkOn()
        {
            // BuzzerNeedWork = true;
            if (BlinkIsOn)
                OB_Buzzer.On();
            else
            {
                OB_Buzzer.Off();
            }
        }

        public void BuzzBlinkOff()
        {
            // BuzzerNeedWork = false;
            OB_Buzzer.Off();
        }

        private void trackBar1_ValueChanged(object sender, EventArgs e)
        {
            lbSpeedRatio.Text = trackBar1.Value.ToString();
            foreach (var control in MasterCore.CoreRobotManager.Robot6AxisList)
                if (control is Robot6Axis)
                    ((Robot6Axis)control).SpeedRatio = trackBar1.Value;

        }

        private void panel_Paint(object sender, PaintEventArgs e)
        { 

        }

        private void btnResetMatrix_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("Confirm To Reset Machine Matrix?", "Confirmation", MessageBoxButtons.OKCancel);
            if (result == DialogResult.OK)
            {
                foreach (var item in MasterCore.MachineMatrixList)
                {
                    item.Reset(_Index.All);
                }
                foreach (var item in MasterCore.StatisticsList)
                {
                    item.Reset();
                }
            }

        }

        public void TarGeneration(string folderPath, DateTime start, DateTime end, string SN,
            (string title, double result)[] data, char status = 'P')
        {
            // Check folder exists or not
            if (!Directory.Exists(folderPath)) return;

            if (status != 'P' && status != 'F' && status != 'A') return;

            if (SysPara.SN == null || SysPara.SN.Length == 0) return;

            // create file name
            string fileName = SN;
            string filePath = Path.Combine(folderPath, $"{fileName}_{DateTime.Now:yyyyMMdd_HHmmssM}.tar");

            // write to tar file
            using (var writer = new StreamWriter(filePath, true))
            {
                writer.WriteLine($"S{SN}");
                writer.WriteLine($"C{SysPara.CustomerName}");
                writer.WriteLine($"I{SysPara.Division}");
                writer.WriteLine($"N{SysPara.StationName}");
                writer.WriteLine($"P{SysPara.ProcessStep}");
                writer.WriteLine($"n{txtAssyNum.Text}");
                writer.WriteLine($"r{txtAssyRev.Text}");
                writer.WriteLine($"p{SysPara.Site}");
                writer.WriteLine($"[{start:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"]{end:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"T{status}");

                if (data != null && data.Length > 0)
                {
                    foreach (var item in data)
                    {
                        writer.WriteLine($"M{item.title}");
                        writer.WriteLine($"d{item.result}");
                        writer.WriteLine(">");
                    }
                }
            }
        }

        private void btnTarGeneration_Click(object sender, EventArgs e)
        {
            string assyNum = txtAssyNum.Text;
            string revision = txtAssyRev.Text;
            DateTime start = DateTime.Now;
            DateTime end = start + TimeSpan.FromMinutes(2);
            (string title, double result)[] data = new (string title, double result)[]
            {
                ("Vision O-ring", 1.5),
                ("Torque Moisture Absorber ", 22.5),
                ("Bottom Housing ", 2.3),
            };
            char status = 'P';

            if (chbxLocal.Checked)
                TarGeneration(SysPara.RouteLocal, start, end, SysPara.SN, data, status);

            if (chbxExternal.Checked)
                TarGeneration(SysPara.RouteExternal, start, end, SysPara.SN, data, status);
        }

        private Cerberus.Enum.FCResultType fcAutoSafetyScan_FlowRun(object sender, EventArgs e)
        {
            if (!SysPara.bSafetyReady && !SysPara.WaitLoad && !SysPara.WaitUnload
                && !SysPara.WaitManual && !MasterCore.Alarm.IsError && !MasterCore.Alarm.IsWarning)
            {
                return Cerberus.Enum.FCResultType.NEXT;
            }
            return Cerberus.Enum.FCResultType.IDLE;
        }
    }
}
