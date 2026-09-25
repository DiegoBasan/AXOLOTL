using AcuraLibrary.Forms;
using Cerberus.CoreEngine.Master;
using System;

namespace Acura3._1.ModuleForms
{
    public partial class FeederForm : ModuleBaseForm
    {
        public bool EnableFeeder = false;
        public FeederForm()
        {
            InitializeComponent();
        }

        #region override
        public override void AcuraStartUp()
        {
            base.AcuraStartUp();
        }

        #region Setting
        public override void BeforeProductionSetting()
        {
        }
        public override void AfterProductionSetting()
        {
            EnableFeeder = GetSettingValue("PSet", "Enable");
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
            EnableFeeder = GetSettingValue("PSet", "Enable");
            oVibrate.Off();
            oFeed.Off();
            bInitialOk = true;

            fcAutoCheckHopper.TaskReset();
        }

        //Initial - Run untill initial complete. bInitialOk = true
        public override void Initial()
        {

        }

        //Run Once when press Start.
        public override void StartRun()
        {

        }

        //Run Once when press Start.
        public override void RunReset()
        {
            fcAutoCheckHopper.TaskReset();
        }

        //Start Run Machine.
        public override void Run()
        {
            if (!EnableFeeder || SysPara.IsDryRun)
                return;

            if (MiddleLayer.ControlF.rdbtnOne.Checked || MiddleLayer.ControlF.rdbtnAll.Checked
                || SysPara.Step[(int)Step.Pick] || MiddleLayer.ControlF.isTestPickup)
            {
                fcAutoCheckHopper.TaskRun();

                if (iLevelFeeder.IsOn())
                    oVibrate.Off();
                else if (iLevelFeeder.IsOff())
                    oVibrate.On();
            }
        }

        public override void StopRun()
        {
            oVibrate.Off();
            //oFeed.Off();
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

        private Cerberus.Enum.FCResultType fcAutoCheckHopper_FlowRun(object sender, EventArgs e)
        {
            if (iLevelHopper.IsOff()) MasterCore.Alarm.Show("9005", "Machine Low Material");
               // return Cerberus.Enum.FCResultType.CASE1;

            return Cerberus.Enum.FCResultType.IDLE;
        }
    }
}
