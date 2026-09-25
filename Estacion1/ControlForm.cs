using Acura3._1.Classes.IVN;
using AcuraLibrary.Forms;
using Cerberus.CoreEngine.Master;
using Cerberus.Enum;
using Cerberus.Forms;
using Cerberus.Utility;
using Cerberus.Utility.FlowChartUtility;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Cerberus.Utility.MotorUtility.MotorTeachPoint;

namespace Acura3._1.ModuleForms
{
    public enum WorkPos
    {
        X,
        Y,
        Z_Cam,
        Z_Touch,
        Z_Tighten,
        Rot,
        Flip,
    }

    public enum ScrewData
    {
        PeakTorque,
        Angle,
        Duration,
    }

    public partial class ControlForm : ModuleBaseForm
    {
        #region Work Data Source
        public static DataSet WorkDataSet;
        public Dictionary<int, List<(int Cavity, bool Alignment)>> WorkCavity = new Dictionary<int, List<(int Pos, bool Alignment)>>();
        #endregion

        #region Safety Condition
        private bool bHeadSafety, bFlipSafety, bRotSafety = false;
        #endregion

        #region CTimer
        private CTimer PickTM = new CTimer();
        private CTimer WorkTM = new CTimer();
        private CTimer InspectTM = new CTimer();
        private CTimer TighteningTM = new CTimer();
        private CTimer ReInspectTM = new CTimer();
        private CTimer RemoveTM = new CTimer();
        private CTimer ErrorTM = new CTimer();

        private CTimer BlinkVacTM = new CTimer();
        private CTimer BlinkStartBtn = new CTimer();
        #endregion

        #region AtlasCopco
        AtlasController Screw;
        #endregion

        #region Module Variables
        private MotorControlForm pMtrCtrlFrm = new MotorControlForm();
        private Queue<int> workCavity = new Queue<int>();
        private Queue<int> inspectCavity = new Queue<int>();
        private List<BH_Input> ClampSs, SafetySs;
        public List<(TextBox Code, RadioButton MES)> Barcodes;
        private double[] screwPos = new double[4];
        private double[] camPos = new double[3];
        private int currentWork, currentInspect, maxTightenTimes = 1000;
        public static bool errorTighten, errorInspection = false;
        private bool msgAccepted, resultReceived, overTighten, status = false;
        public bool isRetry, isTestPickup;
        private int numRetry;
        public string RecipeFolder = "./ModuleData/Position";
        private string BackupFolder = "./ModuleData/Position/Backup";
        public bool IsError;
        private bool prevVacState = false;
                

        public string sSerializer_SerialNumber;

        AutomationDLL.GenericFunctions AutoCUU = new AutomationDLL.GenericFunctions();
        #endregion

        public Action<int[]> UpdateStatus;
        public Action<string, bool, string> UpdateResult;
        public event EventHandler UpdateSetting;

        public ControlForm()
        {
            InitializeComponent();

            CreatePosTeachingDS();

            UpdateStatus = new Action<int[]>(GetCurrentState);
            UpdateResult = new Action<string, bool, string>(GetScrewResult);

            ClampSs = new List<BH_Input>
            {
                iClampSs1,
                iClampSs2,
                iClampSs3,
                iClampSs4,
            };

            SafetySs = new List<BH_Input>
            {
                iSafetySs1,
                iSafetySs2,
                iSafetySs3,
                iSafetySs4,
            };

            Barcodes = new List<(TextBox, RadioButton)>
            {
                (txtCode1, rdbtnMES1),
                (txtCode2, rdbtnMES2),
                (txtCode3, rdbtnMES3),
                (txtCode4, rdbtnMES4),
                (txtCode5, rdbtnMES5),
            };
        }

        private void RecipeEditorF_OpenRecipe(object sender, EventArgs e)
        {
            ReadTeachingPoint(Path.Combine(RecipeFolder, GetRecipeValue("RSet", "FilePath")));
        }

        private void RecipeEditorF_SaveRecipe(object sender, EventArgs e)
        {
            WriteTeachingPoint(WorkDataSet, GetRecipeValue("RSet", "FilePath"));
            ReadTeachingPoint(Path.Combine(RecipeFolder, GetRecipeValue("RSet", "FilePath")));
        }

        #region Function
        private void CreatePosTeachingDS()
        {
            #region Work DataSet
            WorkDataSet = new DataSet();
            WorkDataSet.Tables.Add("TB0");
            WorkDataSet.Tables[0].Columns.Add("Process", typeof(int));
            WorkDataSet.Tables[0].Columns.Add("Program ID", typeof(int));
            WorkDataSet.Tables[0].Columns.Add("PSet", typeof(int));
            WorkDataSet.Tables[0].Columns.Add("Cavity", typeof(int));
            WorkDataSet.Tables[0].Columns.Add("Tool", typeof(int));
            WorkDataSet.Tables[0].Columns.Add("X", typeof(double));
            WorkDataSet.Tables[0].Columns.Add("Y", typeof(double));
            WorkDataSet.Tables[0].Columns.Add("Z_Cam", typeof(double));
            WorkDataSet.Tables[0].Columns.Add("Z_Touch", typeof(double));
            WorkDataSet.Tables[0].Columns.Add("Z_Tighten", typeof(double));
            WorkDataSet.Tables[0].Columns.Add("Rot", typeof(double));
            WorkDataSet.Tables[0].Columns.Add("Flip", typeof(double));
            WorkDataSet.Tables[0].Columns.Add("Tighten", typeof(bool));
            WorkDataSet.Tables[0].Columns.Add("Description", typeof(string));

            dtWork.DataSource = WorkDataSet.Tables[0];
            dtWork.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            for (int i = 0; i < dtWork.Columns.Count; i++)
            {
                dtWork.Columns[i].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dtWork.Columns[i].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dtWork.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            dtWork.Refresh();
            dtWork.RowsAdded += (s, e) => ColorizeRowsByProcess(dtWork);
            dtWork.Sorted += (s, e) => ColorizeRowsByProcess(dtWork);
            #endregion
        }

        public void ReadTeachingPoint(string filePath)
        {
            #region Read DataSet
            if (string.IsNullOrEmpty(filePath)) return;
            if (!File.Exists(filePath)) return;

            WorkDataSet.Tables[0].Clear();
            WorkDataSet.ReadXml(filePath);
            WorkDataSet.AcceptChanges();
            WorkDataSet.Tables[0].DefaultView.Sort = "Process ASC";
            #endregion

            #region Sort Data
            if (WorkDataSet.Tables[0].Rows.Count > 0)
            {
                var workProcesses = WorkDataSet.Tables[0].AsEnumerable()
                    .Select(row => int.TryParse(row["Process"].ToString(), out int res) ? res : 0)
                    .Distinct().OrderBy(p => p).ToList();
                for (int i = 0; i < workProcesses.Count; i++)
                {
                    int process = workProcesses[i];
                    if (process != i + 1)
                    {
                        var rowsToUpdate = WorkDataSet.Tables[0].AsEnumerable()
                            .Where(row => int.TryParse(row["Process"].ToString(), out int res) && res == process).ToList();
                        foreach (var row in rowsToUpdate)
                        {
                            row["Process"] = i + 1;
                        }
                    }
                }

                var duplicateCavity = WorkDataSet.Tables[0].AsEnumerable()
                .GroupBy(row => new
                {
                    Process = int.TryParse(row["Process"]?.ToString(), out int p) ? p : 0,
                    Cavity = int.TryParse(row["Cavity"]?.ToString(), out int c) ? c : 0
                })
                .SelectMany(g => g.Skip(1));

                foreach (var row in duplicateCavity.ToList())
                {
                    WorkDataSet.Tables[0].Rows.Remove(row);
                }

                var processGroups = WorkDataSet.Tables[0].AsEnumerable()
                    .GroupBy(row => int.TryParse(row["Process"].ToString(), out int res) ? res : 0);
                foreach (var group in processGroups)
                {
                    if (group.Count() > 1)
                    {
                        int firstPSet = int.TryParse(group.First()["PSet"].ToString(), out int pset) ? pset : 0;
                        int firstProgram = int.TryParse(group.First()["Program ID"].ToString(), out int program) ? program : 0;
                        foreach (var row in group.Skip(1))
                        {
                            row["PSet"] = firstPSet;
                            row["Program ID"] = firstProgram;
                        }
                    }
                    int cavityCount = 1;
                    foreach (var row in group)
                    {
                        row["Cavity"] = cavityCount++;
                    }
                }

                var alignmentGroups = WorkDataSet.Tables[0].AsEnumerable()
                    .Where(row => bool.TryParse(row["Tighten"].ToString(), out bool isChecked) && isChecked)
                    .GroupBy(row => int.TryParse(row["Process"].ToString(), out int res) ? res : 0);
                foreach (var group in alignmentGroups)
                {
                    if (group.Count() > 1)
                    {
                        int firstTool = int.TryParse(group.First()["Tool"].ToString(), out int tool) ? tool : 0;
                        foreach (var row in group.Skip(1))
                        {
                            row["Tool"] = firstTool;
                        }
                    }
                }
            }

            // Update DataGridView
            dtWork.DataSource = WorkDataSet.Tables[0];
            dtWork.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            for (int i = 0; i < dtWork.Columns.Count; i++)
            {
                dtWork.Columns[i].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dtWork.Columns[i].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dtWork.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            // rows in dtWork cannot be adjusted width
            dtWork.AllowUserToResizeRows = false;
            dtWork.Refresh();
            ColorizeRowsByProcess(dtWork);
            #endregion

            #region Update Process & Cavity List
            if (WorkDataSet.Tables[0].Rows.Count > 0)
            {
                WorkCavity.Clear();
                int rowCount = WorkDataSet.Tables[0].Rows.Count;

                int[] process = WorkDataSet.Tables[0].AsEnumerable()
                    .Select(row => int.TryParse(row["Process"].ToString(), out int res) ? res : 0)
                    .ToArray();
                int[] cavity = WorkDataSet.Tables[0].AsEnumerable()
                    .Select(row => int.TryParse(row["Cavity"].ToString(), out int res1) ? res1 : 0)
                    .ToArray();
                bool[] alignment = WorkDataSet.Tables[0].AsEnumerable()
                    .Select(row => bool.TryParse(row["Tighten"].ToString(), out bool res2) ? res2 : false)
                    .ToArray();

                for (int i = 0; i < rowCount; i++)
                {
                    if (!WorkCavity.ContainsKey(process[i]))
                    {
                        WorkCavity.Add(process[i], new List<(int, bool)> { (cavity[i], alignment[i]) });
                    }
                    else
                    {
                        if (!WorkCavity[process[i]].Any(c => c.Cavity == cavity[i]))
                        {
                            WorkCavity[process[i]].Add((cavity[i], alignment[i]));
                        }
                    }
                }
                WorkCavity = WorkCavity.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }
            else
            {
                MessageBox.Show("No data in the DataTable.");
            }
            #endregion

            #region Update ComboBox
            cbbProcess.DataSource = cbbStepProcess.DataSource = cbbRunProcess.DataSource = WorkCavity.Keys.ToList();
            cbbProcess.SelectedIndex = cbbStepProcess.SelectedIndex = cbbRunProcess.SelectedIndex = 0;
            if (WorkCavity.ContainsKey((int)cbbProcess.SelectedValue))
            {
                List<int> cavity = WorkCavity[(int)cbbProcess.SelectedValue].Where(c => c.Alignment).Select(c => c.Cavity).ToList();
                cbbWork.DataSource = cbbStepWork.DataSource = (cavity.Count > 0) ? cavity : null;
                cbbInspect.DataSource = cbbStepInspection.DataSource = WorkCavity[(int)cbbProcess.SelectedValue].Select(c => c.Cavity).ToList();
            }

            cbbProcess.SelectedIndexChanged += (s, e) =>
            {
                if (WorkCavity.ContainsKey((int)cbbProcess.SelectedValue))
                {
                    List<int> cavity = WorkCavity[(int)cbbProcess.SelectedValue].Where(c => c.Alignment).Select(c => c.Cavity).ToList();
                    cbbWork.DataSource = cbbStepWork.DataSource = (cavity.Count > 0) ? cavity : null;
                    cbbInspect.DataSource = cbbStepInspection.DataSource = WorkCavity[(int)cbbProcess.SelectedValue].Select(c => c.Cavity).ToList();
                }
            };
            cbbStepProcess.SelectedIndexChanged += (s, e) =>
            {
                if (WorkCavity.ContainsKey((int)cbbStepProcess.SelectedValue))
                {
                    List<int> cavity = WorkCavity[(int)cbbStepProcess.SelectedValue].Where(c => c.Alignment).Select(c => c.Cavity).ToList();
                    cbbWork.DataSource = cbbStepWork.DataSource = (cavity.Count > 0) ? cavity : null;
                    cbbInspect.DataSource = cbbStepInspection.DataSource = WorkCavity[(int)cbbStepProcess.SelectedValue].Select(c => c.Cavity).ToList();
                }
            };

            cbbRunProcess.SelectedIndexChanged += (s, e) =>
            {
                if (WorkCavity.ContainsKey((int)cbbRunProcess.SelectedValue))
                {
                    List<int> cavity = WorkCavity[(int)cbbRunProcess.SelectedValue].Where(c => c.Alignment).Select(c => c.Cavity).ToList();
                    cbbWork.DataSource = cbbStepWork.DataSource = (cavity.Count > 0) ? cavity : null;
                    cbbInspect.DataSource = cbbStepInspection.DataSource = WorkCavity[(int)cbbRunProcess.SelectedValue].Select(c => c.Cavity).ToList();
                }
            };
            UpdateSetting?.Invoke(this, new EventArgs());
            #endregion
        }

        private void WriteTeachingPoint(DataSet ds, string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;

            // Check if the DataSet has any tables
            if (ds.Tables.Count < 1) return;
            if (ds.Tables[0].Rows.Count < 1) return;

            // check any empty value or string value in the DataSet
            foreach (DataColumn col in ds.Tables[0].Columns)
            {
                if (col == ds.Tables[0].Columns["Tighten"]) continue;

                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    if (row.RowState == DataRowState.Deleted) continue;
                    if (row[col] == null || string.IsNullOrEmpty(row[col].ToString()))
                    {
                        MessageBox.Show("There is an empty value or string value in the DataSet. Please check again");
                        return;
                    }
                }
            }

            // Make a copy to BackupFolder before update RecipeFolder
            if (!File.Exists(Path.Combine(RecipeFolder, fileName)))
                File.Create(Path.Combine(RecipeFolder, fileName)).Close();

            File.Copy(
                Path.Combine(RecipeFolder, fileName),
                Path.Combine(BackupFolder, fileName.Replace(".xml", "_backup.xml")),
                true
            );

            DataTable originalTable = ds.Tables[0];
            DataTable reorderedTable = originalTable.Clone(); // Copies schema only

            foreach (DataGridViewRow gridViewRow in dtWork.Rows)
            {
                if (gridViewRow.IsNewRow) continue; // Skip the new row

                DataRowView rowView = gridViewRow.DataBoundItem as DataRowView;
                if (rowView != null)
                {
                    reorderedTable.ImportRow(rowView.Row);
                }
            }

            ds.Tables.Remove(originalTable);
            ds.Tables.Add(reorderedTable);

            reorderedTable.TableName = "TB0";

            ds.AcceptChanges();
            ds.WriteXml(Path.Combine(RecipeFolder, fileName));
        }

        private void ColorizeRowsByProcess(DataGridView dgv)
        {
            Dictionary<string, Color> processColors = new Dictionary<string, Color>();
            List<Color> availableColors = new List<Color>
            {
                Color.LightBlue, Color.LightGreen, Color.LightYellow,
                Color.LightPink, Color.LightSalmon, Color.LightCyan,
                Color.LightGray, Color.Moccasin, Color.Thistle
            };

            processColors.Clear();
            int colorIndex = 0;

            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.IsNewRow) continue;

                string processVal = row.Cells["Process"].Value?.ToString();

                if (string.IsNullOrWhiteSpace(processVal)) continue;

                if (!processColors.ContainsKey(processVal))
                {
                    Color color = availableColors[colorIndex % availableColors.Count];
                    processColors[processVal] = color;
                    colorIndex++;
                }

                row.DefaultCellStyle.BackColor = processColors[processVal];
            }
        }

        private void GetScrewPos(int process, int cavity, out double[] pos)
        {
            pos = new double[7] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 };
            double X_Align = tpCamAlign.GetValue(AxisName.X) - tpHeadAlign.GetValue(AxisName.X);
            double Y_Align = tpCamAlign.GetValue(AxisName.Y) - tpHeadAlign.GetValue(AxisName.Y);

            if (WorkDataSet.Tables.Count < 1) return;

            for (int i = 0; i < WorkDataSet.Tables[0].Rows.Count; i++)
            {
                if (process == int.Parse(WorkDataSet.Tables[0].Rows[i]["Process"].ToString()) &&
                    cavity == int.Parse(WorkDataSet.Tables[0].Rows[i]["Cavity"].ToString()) &&
                    bool.Parse(WorkDataSet.Tables[0].Rows[i]["Tighten"].ToString()))
                {
                    pos[(int)WorkPos.X] = double.Parse(WorkDataSet.Tables[0].Rows[i]["X"].ToString()) - X_Align;
                    pos[(int)WorkPos.Y] = double.Parse(WorkDataSet.Tables[0].Rows[i]["Y"].ToString()) - Y_Align;
                    pos[(int)WorkPos.Z_Touch] = double.Parse(WorkDataSet.Tables[0].Rows[i]["Z_Touch"].ToString());
                    pos[(int)WorkPos.Z_Tighten] = double.Parse(WorkDataSet.Tables[0].Rows[i]["Z_Tighten"].ToString());
                    pos[(int)WorkPos.Rot] = double.Parse(WorkDataSet.Tables[0].Rows[i]["Rot"].ToString());
                    pos[(int)WorkPos.Flip] = double.Parse(WorkDataSet.Tables[0].Rows[i]["Flip"].ToString());

                    break;
                }
            }
        }

        private void GetCamPos(int process, int cavity, out double[] pos)
        {
            pos = new double[7] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 };
            if (WorkDataSet.Tables.Count < 1) return;

            for (int i = 0; i < WorkDataSet.Tables[0].Rows.Count; i++)
            {
                if (process == int.Parse(WorkDataSet.Tables[0].Rows[i]["Process"].ToString()) &&
                    cavity == int.Parse(WorkDataSet.Tables[0].Rows[i]["Cavity"].ToString()))
                {
                    pos[(int)WorkPos.X] = double.Parse(WorkDataSet.Tables[0].Rows[i]["X"].ToString());
                    pos[(int)WorkPos.Y] = double.Parse(WorkDataSet.Tables[0].Rows[i]["Y"].ToString());
                    pos[(int)WorkPos.Z_Cam] = double.Parse(WorkDataSet.Tables[0].Rows[i]["Z_Cam"].ToString());
                    pos[(int)WorkPos.Rot] = double.Parse(WorkDataSet.Tables[0].Rows[i]["Rot"].ToString());
                    pos[(int)WorkPos.Flip] = double.Parse(WorkDataSet.Tables[0].Rows[i]["Flip"].ToString());

                    break;
                }
            }
        }

        private bool CheckSafetyPositionXY(double X = 0.0, double Y = 0.0)
        {
            (double Upper, double Lower)[] limit = new (double, double)[2];

            limit[0] = (GetRecipeValue("RSet", "UpperLimX"), GetRecipeValue("RSet", "LowerLimX"));
            limit[1] = (GetRecipeValue("RSet", "UpperLimY"), GetRecipeValue("RSet", "LowerLimY"));

            bool ret = X >= limit[0].Lower && X <= limit[0].Upper &&
                   Y >= limit[1].Lower && Y <= limit[1].Upper;

            if (!ret)
                MasterCore.Alarm.Show("9001", "Target positions are not in safe zone");

            return ret;
        }

        private bool CheckSafetyPositionZ(double Z = 0.0)
        {
            (double Upper, double Lower) limit = (0.0, 0.0);
            limit = (GetRecipeValue("RSet", "UpperLimHead"), GetRecipeValue("RSet", "LowerLimHead"));

            bool ret = Z >= limit.Lower && Z <= limit.Upper;

            if (!ret)
                MasterCore.Alarm.Show("9001", "Target positions are not in safe zone");

            return ret;
        }

        private bool CheckSafetyPositionFlip(double Flip = 0.0)
        {
            (double Upper, double Lower) limit = (0.0, 0.0);
            limit = (GetRecipeValue("RSet", "UpperLimFlip"), GetRecipeValue("RSet", "LowerLimFlip"));

            bool ret = Flip >= limit.Lower && Flip <= limit.Upper;

            if (!ret)
                MasterCore.Alarm.Show("9001", "Target positions are not in safe zone");

            return ret;
        }

        private void GetCurrentState(int[] status)
        {
            if (status == null || status.Length < 3) return;

            txtProcess2.Text = status[0].ToString();
            txtWork2.Text = status[1].ToString();
            txtInspect2.Text = status[2].ToString();
        }

        private void GetScrewResult(string name, bool status, string result)
        {
            // Update result to list view name lvScrewData
            lvScrewData.Items.Add(new ListViewItem(new string[]
            {
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                name,
                status ? "OK" : "NG",
                result
            }));
        }

        private void ReadTighteningCount()
        {
            string filePath = Path.Combine(RecipeFolder, "screw_maint.txt");

            if (!File.Exists(filePath)) return;

            string text = File.ReadAllText(filePath);
            if (int.TryParse(text.Split(':')[1], out int count))
            {
                SysPara.TighteningCount = count;
            }
        }

        private void WriteTighteningCount(int count)
        {
            string filePath = Path.Combine(RecipeFolder, "screw_maint.txt");
            if (!File.Exists(filePath)) return;

            File.WriteAllText(filePath, $"Current Tightening times: {count}");
        }

        private void SwapDataRow(DataRow row1, DataRow row2)
        {
            object[] temp = row1.ItemArray;
            row1.ItemArray = row2.ItemArray;
            row2.ItemArray = temp;

            dtWork.Refresh();
        }

        public void SetStartProcess()
        {
            grpbxOption?.Invoke(new Action(() =>
            {
                SysPara.IsOneProcess = rdbtnOne.Checked || rdbtnStep.Checked;
                if (SysPara.IsOneProcess)
                {
                    int.TryParse(cbbRunProcess.Text, out int process);
                    SysPara.CurrentProcess = VisionForm.CurrentProcess = process;
                }
                else
                    SysPara.CurrentProcess = VisionForm.CurrentProcess = 1;
            }));
        }

        public void BlinkStartButton()
        {
            if (BlinkStartBtn.IsOn(500))
            {
                if (MiddleLayer.SystemF.OB_StartALight.IsOn())
                    MiddleLayer.SystemF.OB_StartALight.Off();
                else
                    MiddleLayer.SystemF.OB_StartALight.On();

                if (MiddleLayer.SystemF.OB_StartBLight.IsOn())
                    MiddleLayer.SystemF.OB_StartBLight.Off();
                else
                    MiddleLayer.SystemF.OB_StartBLight.On();

                BlinkStartBtn.Restart();
            }
        }

        public bool CheckPSPSensor()
        {
            for (int i = 0; i < ClampSs.Count; i++)
            {
                bool enable = GetRecipeValue("RSet", $"ClampSs{i + 1}");
                if (enable)
                {
                    if (ClampSs[i].IsOff())
                    {
                        return false;
                    }
                }
            }

            bool check = false;
            for (int i = 0; i < SafetySs.Count; i++)
            {
                bool enable = GetRecipeValue("RSet", $"SafetySs{i + 1}");
                if (enable)
                {
                    check = true;
                    if (SafetySs[i].IsOn())
                    {
                        return true;
                    }
                }
            }

            return !check;
        }

        public void DisplayMessage(TextBox txt, string message)
        {
            txt.Invoke(new Action(() =>
            {
                txt.Text = message;
            }));
        }

        public string GetDescription(int process, int cavity)
        {
            string description = string.Empty;
            if (WorkDataSet.Tables[0].Rows.Count < 1) return description;
            var rows = WorkDataSet.Tables[0].AsEnumerable()
                .Where(row => int.TryParse(row["Process"]?.ToString(), out int p) && p == process &&
                              int.TryParse(row["Cavity"]?.ToString(), out int c) && c == cavity);
            if (rows.Any())
            {
                description = rows.First()["Description"].ToString();
            }

            return description;
        }

        public void ScanBarcode()
        {
            foreach (var barcode in Barcodes)
            {
                if (string.IsNullOrEmpty(barcode.Code.Text))
                {
                    barcode.Code?.Invoke(new Action(() => barcode.Code.Focus()));
                    break;
                }
            }
        }
        #endregion

        #region override
        public override void AcuraStartUp()
        {
            base.AcuraStartUp();
            ReadTighteningCount();

            MiddleLayer.RecipeEditorF.SaveRecipe += RecipeEditorF_SaveRecipe;
            MiddleLayer.RecipeEditorF.OpenRecipe += RecipeEditorF_OpenRecipe;

            BlinkVacTM.Restart();
        }

        #region Setting
        public override void BeforeProductionSetting()
        {
        }
        public override void AfterProductionSetting()
        {
            isRetry = GetSettingValue("PSet", "Retry");
            numRetry = GetSettingValue("PSet", "NumRetry");
            maxTightenTimes = GetSettingValue("PSet", "Maintenance");

            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));
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
            #region Machine Safety
            if (!SysPara.bSafetyReady)
            {
                grpbxZ.Enabled = grpbxZPos.Enabled = grpbxXJog.Enabled = grpbxYJog.Enabled = grpbxHeadJog.Enabled = grpbxFlipJog.Enabled = grpbxRotJog.Enabled = false;
            }
            else
            {
                grpbxZ.Enabled = grpbxZPos.Enabled = grpbxXJog.Enabled = grpbxYJog.Enabled = grpbxHeadJog.Enabled = grpbxFlipJog.Enabled = grpbxRotJog.Enabled = true;
            }
            #endregion

            #region Safety Head
            bHeadSafety = mtrHead.GetEncoderPosition() <= (tpWait.GetValue(AxisName.Z) + 1) && iHeadSsUp.IsOn();
            bFlipSafety = mtrFlip.GetEncoderPosition() >= (tpWait.GetValue(AxisName.W1) - 10) &&
                mtrFlip.GetEncoderPosition() <= (tpWait.GetValue(AxisName.W1) + 10);
            bRotSafety = mtrRot.GetEncoderPosition() <= 0.1 && mtrRot.GetEncoderPosition() >= -0.1;
            if (bHeadSafety && SysPara.bSafetyReady && iHeadSsUp.IsOn() && CheckPSPSensor())
            {
                grpbxXYPos.Enabled = true; grpbxXY.Enabled = true;
                
                if (bFlipSafety)
                    grpbxRotPos.Enabled = grpbxRot.Enabled = true;
                else grpbxRotPos.Enabled = grpbxRot.Enabled = false;
                if (bRotSafety)
                    grpbxFlipPos.Enabled = grpbxFlip.Enabled = true;
                else
                    grpbxFlipPos.Enabled = grpbxFlip.Enabled = false;
            }
            else
            {
                grpbxXY.Enabled = false; grpbxXYPos.Enabled = false;
                grpbxFlipPos.Enabled = grpbxFlip.Enabled = false;
                grpbxRotPos.Enabled = grpbxRot.Enabled = false;
            }
            #endregion

            #region Motor Safety Zone
            bool retX = mtrX.GetEncoderPosition() >= GetRecipeValue("RSet", "LowerLimX") &&
                        mtrX.GetEncoderPosition() <= GetRecipeValue("RSet", "UpperLimX");
            bool retY = mtrY.GetEncoderPosition() >= GetRecipeValue("RSet", "LowerLimY") &&
                        mtrY.GetEncoderPosition() <= GetRecipeValue("RSet", "UpperLimY");
            bool retZ = mtrHead.GetEncoderPosition() >= GetRecipeValue("RSet", "LowerLimHead") &&
                        mtrHead.GetEncoderPosition() <= GetRecipeValue("RSet", "UpperLimHead");
            bool retFlip = mtrFlip.GetEncoderPosition() >= GetRecipeValue("RSet", "LowerLimFlip") &&
                           mtrFlip.GetEncoderPosition() <= GetRecipeValue("RSet", "UpperLimFlip");
            if (!retX && mtrX.IsHomeOk())
                MasterCore.Alarm.Show("3001", $"{mtrX.Name} is out of safe zone.");
            if (!retY && mtrY.IsHomeOk())
                MasterCore.Alarm.Show("3001", $"{mtrY.Name} is out of safe zone.");
            if (!retZ)
                MasterCore.Alarm.Show("3001", $"{mtrHead.Name} is out of safe zone.");
            if (!retFlip)
                MasterCore.Alarm.Show("3001", $"{mtrFlip.Name} is out of safe zone.");
            #endregion
        }

        //InitialReset - Run Once when press Initial.
        public override void InitialReset()
        {
            grpbxOption?.Invoke(new Action(() => {
                grpbxOption.Enabled = grpbxTestPick.Enabled = true;
            }));

            ReadTeachingPoint(Path.Combine(RecipeFolder, GetRecipeValue("RSet", "FilePath")));

            lvScrewData.Items.Clear();

            SysPara.Step = new bool[7];

            isRetry = GetSettingValue("PSet", "Retry");
            numRetry = GetSettingValue("PSet", "NumRetry");
            maxTightenTimes = GetSettingValue("PSet", "Maintenance");
            

            msgAccepted = errorTighten = errorInspection = resultReceived = isTestPickup = false;

            SysPara.WorkNG_Count = 0;
            SysPara.WorkOK_Count = 0;

            SysPara.hsInspection.Reset();
            SysPara.hsCam.Reset();
            SysPara.hsPick.Reset();
            SysPara.hsWork.Reset();
            SysPara.hsTightening.Reset();
            SysPara.hsFeeder.Reset();
            SysPara.hsManual.Reset();

            fcInitCtrlStart.TaskReset();
            fcAutoMainStart.TaskReset();
            fcAutoPickStart.TaskReset();
            fcAutoWorkStart.TaskReset();
            fcAutoInspectStart.TaskReset();
            fcSubRmStart.TaskReset();
            fcSubTorqueStart.TaskReset();
            fcErrorStart.TaskReset();
            fcAutoManualStart.TaskReset();

        }

        //Initial - Run untill initial complete. bInitialOk = true
        public override void Initial()
        {
            fcInitCtrlStart.TaskRun();
        }

        //Run Once when press Start.
        public override void StartRun()
        {
            RunTM.Restart();
            PickTM.Restart();
            WorkTM.Restart();
            InspectTM.Restart();
            TighteningTM.Restart();
            RemoveTM.Restart();
        }

        //Run Once when press Start.
        public override void RunReset()
        {
            fcAutoMainStart.TaskReset();
            fcAutoPickStart.TaskReset();
            fcAutoWorkStart.TaskReset();
            fcAutoInspectStart.TaskReset();
            fcAutoTightenStart.TaskReset();
            fcAutoReInspectStart.TaskReset();
            fcSubRmStart.TaskReset();
            fcSubTorqueStart.TaskReset();
            fcErrorStart.TaskReset();
            fcTestPickStart.TaskReset();
            fcAutoManualStart.TaskReset();
        }

        //Start Run Machine.
        public override void Run()
        {
            if (rdbtnStep.Checked)
            {
                SysPara.WaitLoad = SysPara.WaitUnload = false;
                if (!CheckPSPSensor())
                {
                    SysPara.Step = new bool[7];
                    MasterCore.Alarm.Show("9003");
                    return;
                }

                if (SysPara.Step[(int)Step.Inspection])
                {
                    fcAutoInspectStart.TaskRun();
                }
                else if (SysPara.Step[(int)Step.Work])
                {
                    fcAutoWorkStart.TaskRun();
                }
                else if (SysPara.Step[(int)Step.Pick])
                {
                    fcAutoPickStart.TaskRun();
                }
                else if (SysPara.Step[(int)Step.Remove])
                {
                    fcSubRmStart.TaskRun();
                }
                else if (SysPara.Step[(int)Step.Torque])
                {
                    fcSubTorqueStart.TaskRun();
                    fcAutoPickStart.TaskRun();
                    fcAutoWorkStart.TaskRun();
                }
                else if (SysPara.Step[(int)Step.ReInspect])
                {
                    fcAutoReInspectStart.TaskRun();
                }
                else if (isTestPickup)
                {
                    fcTestPickStart.TaskRun();
                }
            }

            if (rdbtnAll.Checked || rdbtnOne.Checked)
            {
                fcAutoMainStart.TaskRun();
                fcAutoPickStart.TaskRun();
                fcAutoWorkStart.TaskRun();
                fcAutoInspectStart.TaskRun();
                fcAutoManualStart.TaskRun();
            }
        }

        public override void StopRun()
        {
            mtrX.Stop();
            mtrY.Stop();
            mtrHead.Stop();
            mtrFlip.Stop();
            mtrRot.Stop();

            isTestPickup = false;

            grpbxOption?.Invoke(new Action(() =>
            {
                grpbxOption.Enabled = true;
            }));
        }

        public override void ServoOff()
        {
            mtrX.ServoOff();
            mtrY.ServoOff();
            mtrHead.ServoOff();
            mtrFlip.ServoOff();
            mtrRot.ServoOff();
        }

        public override void ServoOn()
        {
            mtrX.ServoOn();
            mtrY.ServoOn();
            mtrHead.ServoOn();
            mtrFlip.ServoOn();
            mtrRot.ServoOn();
        }

        public override void SetSpeed(int SpeedRate)
        {
            mtrX.SpeedRatio = mtrY.SpeedRatio = mtrHead.SpeedRatio = (double)SpeedRate;

            mtrX.WorkSpeed = GetSettingValue("MSet", "SpeedX");
            mtrX.Acceleration = GetSettingValue("MSet", "AccX");
            mtrX.Deceleration = GetSettingValue("MSet", "DccX");

            mtrY.WorkSpeed = GetSettingValue("MSet", "SpeedY");
            mtrY.Acceleration = GetSettingValue("MSet", "AccY");
            mtrY.Deceleration = GetSettingValue("MSet", "DccY");

            mtrHead.WorkSpeed = GetSettingValue("MSet", "SpeedHead1");
            mtrHead.Acceleration = GetSettingValue("MSet", "AccHead1");
            mtrHead.Deceleration = GetSettingValue("MSet", "DccHead1");

            mtrFlip.WorkSpeed = GetSettingValue("MSet", "SpeedFlip");
            mtrFlip.Acceleration = GetSettingValue("MSet", "AccFlip");
            mtrFlip.Deceleration = GetSettingValue("MSet", "DccFlip");

            mtrRot.WorkSpeed = GetSettingValue("MSet", "SpeedRot");
            mtrRot.Acceleration = GetSettingValue("MSet", "AccRot");
            mtrRot.Deceleration = GetSettingValue("MSet", "DccRot");
        }

        public override void ModuleDispose()
        {

        }

        #endregion

        #region Maintenance
        private void btnStageManual_Click(object sender, EventArgs e)
        {
            pMtrCtrlFrm.Initial(sender);
            pMtrCtrlFrm.TopMost = true;
            pMtrCtrlFrm.ShowDialog();
        }

        private void btnHead1Home_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("Set Home for Head Axis?",
                "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                mtrHead.SetEncoderPosition(0);
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopRun();
        }

        #region Stage Manual
        private void btnStageWait_Click(object sender, EventArgs e)
        {
            if (CheckSafetyPositionXY(
                tpWait.GetValue(AxisName.X), tpWait.GetValue(AxisName.Y)) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrX.Goto(tpWait.GetValue(AxisName.X));
            mtrY.Goto(tpWait.GetValue(AxisName.Y));
        }

        private void btnStagePick_Click(object sender, EventArgs e)
        {
            if (CheckSafetyPositionXY(
                tpPickup.GetValue(AxisName.X), tpPickup.GetValue(AxisName.Y)) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrX.Goto(tpPickup.GetValue(AxisName.X));
            mtrY.Goto(tpPickup.GetValue(AxisName.Y));
        }

        private void btnStageWork_Click(object sender, EventArgs e)
        {
            if (cbbWork.Text == "") return;

            GetScrewPos(int.Parse(cbbProcess.Text), int.Parse(cbbWork.Text), out screwPos);

            if (CheckSafetyPositionXY(
                screwPos[(int)WorkPos.X], screwPos[(int)WorkPos.Y]) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrX.Goto(screwPos[(int)WorkPos.X]);
            mtrY.Goto(screwPos[(int)WorkPos.Y]);
        }

        private void btnStageMaint_Click(object sender, EventArgs e)
        {
            if (CheckSafetyPositionXY(
                tpMaint.GetValue(AxisName.X), tpMaint.GetValue(AxisName.Y)) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrX.Goto(tpMaint.GetValue(AxisName.X));
            mtrY.Goto(tpMaint.GetValue(AxisName.Y));
        }
        private void btnStageTorque_Click(object sender, EventArgs e)
        {
            if (CheckSafetyPositionXY(
                tpTestTorque.GetValue(AxisName.X), tpTestTorque.GetValue(AxisName.Y)) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrX.Goto(tpTestTorque.GetValue(AxisName.X));
            mtrY.Goto(tpTestTorque.GetValue(AxisName.Y));

        }
        private void btnStageRemove_Click(object sender, EventArgs e)
        {
            if (CheckSafetyPositionXY(
                tpRemove.GetValue(AxisName.X), tpRemove.GetValue(AxisName.Y)) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrX.Goto(tpRemove.GetValue(AxisName.X));
            mtrY.Goto(tpRemove.GetValue(AxisName.Y));
        }

        private void btnStageInspect_Click(object sender, EventArgs e)
        {
            GetCamPos(int.Parse(cbbProcess.Text), int.Parse(cbbInspect.Text), out double[] camPos);

            if (CheckSafetyPositionXY(
                camPos[(int)WorkPos.X], camPos[(int)WorkPos.Y]) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrX.Goto(camPos[(int)WorkPos.X]);
            mtrY.Goto(camPos[(int)WorkPos.Y]);
        }
        #endregion

        #region Head Manual
        private void btnHeadWait_Click(object sender, EventArgs e)
        {
            if (CheckSafetyPositionZ(tpWait.GetValue(AxisName.Z)) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrHead.Goto(tpWait.GetValue(AxisName.Z));
        }

        private void btnHeadPickup_Click(object sender, EventArgs e)
        {
            if (CheckSafetyPositionZ(tpPickup.GetValue(AxisName.Z)) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrHead.Goto(tpPickup.GetValue(AxisName.Z));
        }

        private void btnHeadTouch_Click(object sender, EventArgs e)
        {
            if (cbbWork.Text == "") return;

            GetScrewPos(int.Parse(cbbProcess.Text), int.Parse(cbbWork.Text), out screwPos);

            if (CheckSafetyPositionZ(screwPos[(int)WorkPos.Z_Touch]) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrHead.Goto(screwPos[(int)WorkPos.Z_Touch]);
        }

        private void btnHeadTight_Click(object sender, EventArgs e)
        {
            if (cbbWork.Text == "") return;

            GetScrewPos(int.Parse(cbbProcess.Text), int.Parse(cbbWork.Text), out screwPos);

            if (CheckSafetyPositionZ(screwPos[(int)WorkPos.Z_Tighten]) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrHead.Goto(screwPos[(int)WorkPos.Z_Tighten]);
        }

        private void btnHeadMaint_Click(object sender, EventArgs e)
        {
            if (CheckSafetyPositionZ(tpMaint.GetValue(AxisName.Z)) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrHead.Goto(tpMaint.GetValue(AxisName.Z));
        }

        private void btnHeadRemove_Click(object sender, EventArgs e)
        {
            if (CheckSafetyPositionZ(tpRemove.GetValue(AxisName.Z)) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrHead.Goto(tpRemove.GetValue(AxisName.Z));
        }

        private void btnHeadInspect_Click(object sender, EventArgs e)
        {
            GetCamPos(int.Parse(cbbProcess.Text), int.Parse(cbbInspect.Text), out double[] camPos);

            if (CheckSafetyPositionZ(camPos[(int)WorkPos.Z_Cam]) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrHead.Goto(camPos[(int)WorkPos.Z_Cam]);
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

            Screw = new AtlasController(ip, port) { Type = ControllerType.Auto };
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

        private void btnUnsub_Click(object sender, EventArgs e)
        {
            if (Screw == null) return;
            if (Screw.IsConnected)
            {
                Screw.UnsubscribeData();
            }
        }
        #endregion

        #region Flip Manual
        private void btnRotXWait_Click(object sender, EventArgs e)
        {
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            if (bHeadSafety) mtrFlip.Goto(tpWait.GetValue(AxisName.W1));
        }


        private void btnRotXManual_Click(object sender, EventArgs e)
        {
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            if (bHeadSafety) mtrFlip.Goto(tpManual.GetValue(AxisName.W1));

        }
        private void btnFlipWork_Click(object sender, EventArgs e)
        {
            GetScrewPos(int.Parse(cbbProcess.Text), int.Parse(cbbWork.Text), out screwPos);
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            if (bHeadSafety) mtrFlip.Goto(screwPos[(int)WorkPos.Flip]);

        }
        private void btnFlipInspect_Click(object sender, EventArgs e)
        {
            GetCamPos(int.Parse(cbbProcess.Text), int.Parse(cbbInspect.Text), out camPos);
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            if (bHeadSafety && bFlipSafety) mtrFlip.Goto(camPos[(int)WorkPos.Flip]);
        }
        private void btnRotXHome_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("Set Home for RotX Axis?",
                "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                mtrFlip.SetEncoderPosition(0);
            }

        }
        #endregion

        #region Rot Manual
        private void btnRotYWait_Click(object sender, EventArgs e)
        {
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            if (bHeadSafety && bFlipSafety) mtrRot.Goto(tpWait.GetValue(AxisName.W2));
        }
        private void btnRotManual_Click(object sender, EventArgs e)
        {
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            if (bHeadSafety && bFlipSafety) mtrRot.Goto(tpManual.GetValue(AxisName.W2));

        }
        private void btnRotYWork_Click(object sender, EventArgs e)
        {
            GetScrewPos(int.Parse(cbbProcess.Text), int.Parse(cbbWork.Text), out screwPos);
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            if (bHeadSafety && bFlipSafety)
                mtrRot.Goto(screwPos[(int)WorkPos.Rot]);

        }
        private void button8_Click(object sender, EventArgs e)
        {
            GetCamPos(int.Parse(cbbProcess.Text), int.Parse(cbbInspect.Text), out camPos);
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            if (bHeadSafety && bFlipSafety) mtrRot.Goto(camPos[(int)WorkPos.Rot]);
        }
        private void btnRotYHome_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("Set Home for RotY Axis?",
                "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                mtrRot.SetEncoderPosition(0);
            }

        }
        #endregion

        #endregion

        #region Flow Initial
        private FCResultType fcInitCtrlStart_FlowRun(object sender, EventArgs e)
        {
            if (!CheckPSPSensor())
            {
                MasterCore.Alarm.Show("9003");
                RunTM.Restart();
                return FCResultType.IDLE;
            }

            MiddleLayer.SystemF.OB_StartALight.Off();
            MiddleLayer.SystemF.OB_StartBLight.Off();

            SysPara.WaitLoad = SysPara.WaitUnload = SysPara.WaitManual = false;

            mcCtrl.Reset(MachineMatrix._Index.Main);
            IsError = false;
            oLoose.Off();
            oTight.Off();
            oReset.On();
            //Patrick
            mtrX.HomeReset();
            mtrY.HomeReset();

            //Javier Villagran
            string Path = Environment.CurrentDirectory + "\\Imagenes\\" + "Automation CUU Logo.Png";
            CambiarImagenPictureBox(Path);
            oStageVac.Off();
            SysPara.MESData.Clear();

            return FCResultType.NEXT;
        }

        private FCResultType fcInitCtrlHeadMoveSafety_FlowRun(object sender, EventArgs e)
        {
            cylHead.Off();
            bool ret = mtrHead.Goto(tpWait.GetValue(AxisName.Z));
            if (ret && iHeadSsUp.IsOn())
            {
                if (!mtrX.IsHomeOk()) mtrX.HomeReset();
                if (!mtrY.IsHomeOk()) mtrY.HomeReset();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcInitRotXMoveWait_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety)
            {
                bool ret = mtrFlip.Goto(tpWait.GetValue(AxisName.W1));
                if (ret)
                {
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;

        }
        private FCResultType fcInitRotYMoveWait_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety && bFlipSafety)
            {
                bool ret = mtrRot.Goto(tpWait.GetValue(AxisName.W2));
                if (ret)
                {
                    return FCResultType.NEXT;
                }
            }

            return FCResultType.IDLE;
        }
        private FCResultType fcInitCtrlStageOrg_FlowRun(object sender, EventArgs e)
        {
            if (!bHeadSafety) return FCResultType.IDLE;

            if (!mtrX.IsHomeOk()) mtrX.Home();
            if (!mtrY.IsHomeOk()) mtrY.Home();

            if (mtrX.IsHomeOk() && mtrY.IsHomeOk())
            {
                return FCResultType.NEXT;
            }

            return FCResultType.IDLE;
        }

        private FCResultType fcInitCtrlCheckScrew_FlowRun(object sender, EventArgs e)
        {
            oHeadVac.On();
            if (!oHeadVac.IsOn(200)) return FCResultType.IDLE;

            if (iHeadSsVac.IsOn())
            {
                fcSubRmStart.TaskReset();
                SysPara.hsRemove.Ready = true;
                return FCResultType.CASE1;
            }
            else
            {
                oHeadVac.Off();
                return FCResultType.NEXT;
            }
        }

        private FCResultType fcInitCtrlRemoveScrew_FlowRun(object sender, EventArgs e)
        {
            fcSubRmStart.TaskRun();
            if (SysPara.hsRemove.Complete)
            {
                SysPara.hsRemove.Reset();
                fcSubRmStart.TaskReset();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcInitCtrlStageMoveWait_FlowRun(object sender, EventArgs e)
        {
            bool ret = mtrHead.Goto(tpWait.GetValue(AxisName.Z));
            if (bHeadSafety)
            {
                bool r1 = mtrX.Goto(tpWait.GetValue(AxisName.X));
                bool r2 = mtrY.Goto(tpWait.GetValue(AxisName.Y));
                if (r1 && r2) return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcInitCtrlClampOpen_FlowRun(object sender, EventArgs e)
        {
            return FCResultType.NEXT;
        }

        private FCResultType fcInitCtrlConnectScrew_FlowRun(object sender, EventArgs e)
        {
            if (Screw == null)
            {
                string ip = GetSettingValue("PSet", "ScrewIP");
                int port = GetSettingValue("PSet", "ScrewPort");
                Screw = new AtlasController(ip, port) { Type = ControllerType.Auto };
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

        private FCResultType fcInitCtrlActive_FlowRun(object sender, EventArgs e)
        {
            if (RunTM.IsOn(500))
            {
                Screw.SelectPSet(1);
                RunTM.Restart();
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

        #region Main Flow

        public void SelectTxtSerializer()
        {
            if (txtCode1.InvokeRequired)
                txtCode1.BeginInvoke((MethodInvoker)(() => SelectTxtSerializer()));
            else
            {
                txtCode1.Select(); //Selecciona txt1 de pallet1
                txtCode1.Focus(); //Selecciona txt1 de pallet1
                txtCode1.ReadOnly = false; //Permite escanear 
               

            }
        }

        private FCResultType fcAutoMainStart_FlowRun(object sender, EventArgs e)
        {
            #region Check Process and Cavity and Program
            workCavity.Clear();
            inspectCavity.Clear();
            if (WorkCavity.ContainsKey(SysPara.CurrentProcess))
            {
                foreach (var cavity in WorkCavity[SysPara.CurrentProcess])
                {
                    if (cavity.Alignment)
                    {
                        workCavity.Enqueue(cavity.Cavity);
                        if (SysPara.IsAlignment)
                            inspectCavity.Enqueue(cavity.Cavity);
                    }
                    else
                    {
                        if (SysPara.IsInspection)
                            inspectCavity.Enqueue(cavity.Cavity);
                    }
                }
            }
            #endregion

            #region Javier Villagran
            sSerializer_SerialNumber = "";

            SelectTxtSerializer();
            string Path = Environment.CurrentDirectory + "\\Imagenes\\" + "SERIALIZER.jpg";
            CambiarImagenPictureBox(Path);
            #endregion

            if (workCavity.Count > 0 || inspectCavity.Count > 0)
            {
                SysPara.WaitLoad = true;
                SysPara.WaitUnload = SysPara.WaitManual = false;

                mtrRot.ServoOff();

                numRetry = GetSettingValue("PSet", "NumRetry");

                errorTighten = false;
                errorInspection = false;

                

                grpbxBarcode?.Invoke(new Action(() =>
                {
                    foreach (var item in Barcodes)
                    {
                        item.Code?.Invoke(new Action(() => item.Code.Clear()));
                    }
                    Barcodes.First().MES?.Invoke(new Action(() => Barcodes.First().MES.Checked = true));
                }));

                RunTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainWaitScanner_FlowRun(object sender, EventArgs e)
        {
            DisplayMessage(txtInstruction, "Escanee el Serializer para continuar con la operación.");

            if (SysPara.IsDryRun) return FCResultType.NEXT;

            //Javier Villagran:Esperar el escaneo de los dos seriales
            if (!string.IsNullOrWhiteSpace(sSerializer_SerialNumber) && sSerializer_SerialNumber.Length >= 3)
            {

                DisplayMessage(txtInstruction, "Coloque el Housing y cierre los pisadores de la fixtura.");

                string Path = Environment.CurrentDirectory + "\\Imagenes\\" + "GIF TAPA.Gif";
                CambiarImagenPictureBox(Path);

                oStageVac.Off();
                RunTM.Restart();
                return FCResultType.NEXT;
            }

            #region Old ITO
            //if (MiddleLayer.SystemF.IB_BtnStartA.IsOn() && MiddleLayer.SystemF.IB_BtnStartB.IsOn())
            //{
            //    oStageVac.Off();
            //    RunTM.Restart();
            //    return FCResultType.NEXT;
            //}
            #endregion

            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainPVCheck_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.IsDryRun || !SysPara.PV_Check) return FCResultType.NEXT;

            foreach (var code in Barcodes)
            {
                if (code.MES.Checked && string.IsNullOrEmpty(code.Code.Text))
                {
                    MasterCore.Alarm.Show("3014", "Faltan Escaneos");
                    return FCResultType.PREVIOUS;
                }
                if (code.MES.Checked)
                    SysPara.Barcode = code.Code.Text;
            }



            if (!CheckPSPSensor())//Evalua sensores para esperar a que carguen
            {
                RunTM.Restart();
                return FCResultType.NEXT;
            }

            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainCheckSensor_FlowRun(object sender, EventArgs e)
        {
            //DisplayMessage(txtInstruction, "Check PSP Sensor");

            if (CheckPSPSensor())
            {
                MiddleLayer.SystemF.OB_StartALight.Off();
                MiddleLayer.SystemF.OB_StartBLight.Off();

                //Encender Vacio para detectarla unidad
                oStageVac.On();

                string Path = Environment.CurrentDirectory + "\\Imagenes\\" + "Esperar.Gif";
                CambiarImagenPictureBox(Path);

                RunTM.Restart();
                return FCResultType.NEXT;
            }

            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainStageVacOn_FlowRun(object sender, EventArgs e)
        {
            DisplayMessage(txtInstruction, "Esperando que el vacio detecte la unidad");

            if (iStageSsVac.IsOn() || SysPara.IsDryRun)
            {
                BlinkStartBtn.Restart();
                RunTM.Restart();

               
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainConfirm_FlowRun(object sender, EventArgs e)
        {
            DisplayMessage(txtInstruction, "Presione los botones de Inicio para comenzar el proceso");

            //Javier Villagran
            string Path = Environment.CurrentDirectory + "\\Imagenes\\" + "START GIF.Gif";
            CambiarImagenPictureBox(Path);


            if (iStageSsVac.IsOff() && !SysPara.IsDryRun)
                return FCResultType.PREVIOUS;

            if (errorInspection || errorTighten)
                fcErrorStart.TaskRun();

            BlinkStartButton();

            if ((MiddleLayer.SystemF.IB_BtnStartA.IsOn() && MiddleLayer.SystemF.IB_BtnStartB.IsOn()
                && !IsError) || SysPara.IsDryRun)
            {
                fcErrorStart.TaskReset();

                MiddleLayer.SystemF.OB_StartALight.On();
                MiddleLayer.SystemF.OB_StartBLight.On();

                MiddleLayer.ControlF.mcCtrl.Start(MachineMatrix._Index.Main);
                SysPara.WorkStartTimeStamp = DateTime.Now;

                mtrRot.ServoOn();

                SysPara.WaitLoad = false;

                if (!errorInspection && !errorTighten)
                    lvScrewData.Invoke(new MethodInvoker(delegate { lvScrewData.Items.Clear(); }));

                //Javier Villagran
                string Path2 = Environment.CurrentDirectory + "\\Imagenes\\" + "GIF PROGRESS.Gif";
                CambiarImagenPictureBox(Path2);

                RunTM.Restart();
                var x = sSerializer_SerialNumber;
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainRecheckSensor_FlowRun(object sender, EventArgs e)
        {
            if (!CheckPSPSensor())
            {
                MasterCore.Alarm.Show("9003");
                RunTM.Restart();
                return FCResultType.PREVIOUS;
            }

            RunTM.Restart();
            return FCResultType.NEXT;
        }

        private FCResultType fcAutoMainUpdateStatus_FlowRun(object sender, EventArgs e)
        {
            #region Handle Error
            if (errorTighten)
            {
                currentInspect = currentWork;
                return FCResultType.NEXT;
            }

            if (errorInspection)
            {
                errorInspection = false;
                return FCResultType.NEXT;
            }
            #endregion

            #region Update Cavity
            if (inspectCavity.Count > 0)
            {
                VisionForm.RetryNum = MiddleLayer.VisionF.ResetRetry();
                currentInspect = inspectCavity.Dequeue();
                UpdateStatus(new int[]
                {
                    SysPara.CurrentProcess,
                    currentWork,
                    currentInspect,
                });
                return FCResultType.NEXT;
            }
            else if (workCavity.Count > 0)
            {
                numRetry = GetSettingValue("PSet", "NumRetry");
                currentWork = workCavity.Dequeue();
                UpdateStatus(new int[]
                {
                    SysPara.CurrentProcess,
                    currentWork,
                    currentInspect,
                });
                return FCResultType.CASE1;
            }
            #endregion

            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainTriggerInspect_FlowRun(object sender, EventArgs e)
        {
            DisplayMessage(txtInstruction, "Executing Inspection...");

            GetCamPos(SysPara.CurrentProcess, currentInspect, out camPos);

            if (CheckSafetyPositionXY(camPos[(int)WorkPos.X], camPos[(int)WorkPos.Y])
                && CheckSafetyPositionZ(camPos[(int)WorkPos.Z_Cam])
                && CheckSafetyPositionFlip(camPos[(int)WorkPos.Flip]))
            {
                SysPara.hsInspection.Ready = true;
                RunTM.Restart();
                return FCResultType.NEXT;
            }
            else
            {
                MasterCore.Alarm.Show("9001", "Target positions are not in safe zone");
                return FCResultType.IDLE;
            }
        }

        private FCResultType fcAutoMainWaitInspect_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.hsInspection.Complete)
            {
                SysPara.hsInspection.Reset();

                if (!SysPara.Status[(int)Step.Inspection])
                {
                    errorInspection = true;
                    fcErrorStart.TaskReset();
                    fcErrorStart.TaskRun();
                    return FCResultType.PREVIOUS;
                }

                if (errorTighten)
                {
                    errorTighten = false;
                    return FCResultType.CASE1;
                }

                SysPara.ProductStatus = SysPara.Status[(int)Step.Inspection] ? 'P' : 'F';
                SysPara.MESData.Add((
                    $"Vision {GetDescription(SysPara.CurrentProcess, currentInspect)}",
                    SysPara.Status[(int)Step.Inspection] ? 1 : 0
                ));
                RunTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainTriggerPick_FlowRun(object sender, EventArgs e)
        {
            DisplayMessage(txtInstruction, "Executing Pickup...");

            if (CheckSafetyPositionXY(tpPickup.GetValue(AxisName.X), tpPickup.GetValue(AxisName.Y))
                && CheckSafetyPositionZ(tpPickup.GetValue(AxisName.Z)))
            {
                SysPara.hsPick.Ready = true;
                RunTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainWaitPick_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.hsPick.Complete)
            {
                SysPara.hsPick.Reset();
                RunTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainTriggerWork_FlowRun(object sender, EventArgs e)
        {
            DisplayMessage(txtInstruction, "Executing Tightening...");

            GetScrewPos(SysPara.CurrentProcess, currentWork, out screwPos);

            if (VisionForm.Alignment.ContainsKey(currentWork) && SysPara.IsAlignment)
            {
                screwPos[(int)WorkPos.X] += VisionForm.Alignment[currentWork].X;
                screwPos[(int)WorkPos.Y] += VisionForm.Alignment[currentWork].Y;
            }

            if (CheckSafetyPositionXY(screwPos[(int)WorkPos.X], screwPos[(int)WorkPos.Y])
                && CheckSafetyPositionZ(screwPos[(int)WorkPos.Z_Tighten])
                && CheckSafetyPositionZ(screwPos[(int)WorkPos.Z_Touch])
                && CheckSafetyPositionFlip(screwPos[(int)WorkPos.Flip]))
            {
                SysPara.hsWork.Ready = true;
                RunTM.Restart();
                return FCResultType.NEXT;
            }
            else
            {
                MasterCore.Alarm.Show("9001", "Target positions are not in safe zone");
                return FCResultType.IDLE;
            }
        }

        private FCResultType fcAutoMainWaitWork_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.hsWork.Complete)
            {
                SysPara.hsWork.Reset();

                if (!SysPara.Status[(int)Step.Tightening] && !SysPara.IsDryRun && SysPara.IsTightening)
                {
                    SysPara.WorkNG_Count++;
                    MiddleLayer.VisionF.RetryProcess = (SysPara.CurrentProcess, 0, currentWork);
                    errorTighten = true;
                    fcErrorStart.TaskReset();
                    fcErrorStart.TaskRun();
                    return FCResultType.PREVIOUS;
                }

                SysPara.MESData.Add((
                    $"Torque {GetDescription(SysPara.CurrentProcess, currentWork)}",
                    Screw.ResultData.PeakTorque
                ));
                SysPara.MESData.Add((
                    $"Angle {GetDescription(SysPara.CurrentProcess, currentWork)}",
                    Screw.ResultData.TotalAngle
                ));

                SysPara.WorkOK_Count++;
                RunTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainCheckCavity_FlowRun(object sender, EventArgs e)
        {
            if (workCavity.Count > 0 || inspectCavity.Count > 0)
                return FCResultType.PREVIOUS;

            if (SysPara.ReInspection) return FCResultType.CASE1;

            RunTM.Restart();
            return FCResultType.NEXT;
        }

        private FCResultType fcAutoMainReInspection_FlowRun(object sender, EventArgs e)
        {
            foreach (var cavity in WorkCavity[SysPara.CurrentProcess])
            {
                if (cavity.Alignment)
                    inspectCavity.Enqueue(cavity.Cavity);
            }

            if (inspectCavity.Count > 0)
            {
                SysPara.hsReInspect.Ready = true;
                fcAutoReInspectStart.TaskReset();
                RunTM.Restart();
                return FCResultType.NEXT;
            }
            else
                return FCResultType.CASE1;
        }

        private FCResultType fcAutoMainWaitReInspect_FlowRun(object sender, EventArgs e)
        {
            DisplayMessage(txtInstruction, "Executing Re-Inspection...");

            if (SysPara.hsReInspect.Complete)
            {
                SysPara.hsReInspect.Reset();
                fcAutoReInspectStart.TaskReset();
                RunTM.Restart();
                return FCResultType.NEXT;
            }
            fcAutoReInspectStart.TaskRun();
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainMoveManual_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety)
            {
                bool ret1 = mtrX.Goto(tpWait.GetValue(AxisName.X));
                bool ret2 = mtrY.Goto(tpWait.GetValue(AxisName.Y));
                if (ret1 && ret2)
                {
                    RunTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainRotManual_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety && bFlipSafety)
            {
                bool ret = mtrRot.Goto(tpManual.GetValue(AxisName.W2));
                if (ret)
                {
                    mtrRot.ServoOff();
                    RunTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainFlipManual_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety && bRotSafety)
            {
                bool ret = mtrFlip.Goto(tpManual.GetValue(AxisName.W1));
                if (ret)
                {
                    mtrFlip.ServoOff();
                    if (!MiddleLayer.ManualF.IsManual)
                        return FCResultType.CASE1;

                    SysPara.hsManual.Ready = true;
                    //MiddleLayer.ManualF.fcAutoTightenStart.TaskReset();

                    SysPara.WaitManual = true;

                    RunTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainWaitManual_FlowRun(object sender, EventArgs e)
        {

            DisplayMessage(txtInstruction, "Atornille el conector Ampseal");
            string Path = Environment.CurrentDirectory + "\\Imagenes\\" + "GIF MTORQUE.Gif";
            CambiarImagenPictureBox(Path);

            //MiddleLayer.ManualF.fcAutoTightenStart.TaskRun();

            



            if (SysPara.hsManual.Complete)
            {
                BlinkStartButton();
                DisplayMessage(txtInstruction, "Presione los botones de Inicio para comenzar el proceso");
                string Path2 = Environment.CurrentDirectory + "\\Imagenes\\" + "START GIF.Gif";
                CambiarImagenPictureBox(Path2);

                if (MiddleLayer.SystemF.IB_BtnStartA.IsOn() && MiddleLayer.SystemF.IB_BtnStartB.IsOn())
                {
                    SysPara.hsManual.Reset();
                    MiddleLayer.ManualF.fcAutoTightenStart.TaskReset();

                    MiddleLayer.SystemF.OB_StartALight.On();
                    MiddleLayer.SystemF.OB_StartBLight.On();

                    mtrFlip.ServoOn();
                    mtrRot.ServoOn();

                    SysPara.WaitManual = false;   

                    //Javier Villagran
                    string Path3 = Environment.CurrentDirectory + "\\Imagenes\\" + "GIF PROGRESS.Gif";
                    CambiarImagenPictureBox(Path3);

                    RunTM.Restart();
                                                        
                    return FCResultType.NEXT;
                    
                }
            }

            #region ITO Old
            //DisplayMessage(txtInstruction, "Atornille el conector Ampseal");

            //MiddleLayer.ManualF.fcAutoTightenStart.TaskRun();

            //BlinkStartButton();

            //if (MiddleLayer.SystemF.IB_BtnStartA.IsOn() && MiddleLayer.SystemF.IB_BtnStartB.IsOn()
            //    && SysPara.hsManual.Complete)
            //{
            //    SysPara.hsManual.Reset();
            //    MiddleLayer.ManualF.fcAutoTightenStart.TaskReset();

            //    MiddleLayer.SystemF.OB_StartALight.On();
            //    MiddleLayer.SystemF.OB_StartBLight.On();

            //    mtrFlip.ServoOn();
            //    mtrRot.ServoOn();

            //    SysPara.WaitManual = false;

            //    RunTM.Restart();
            //    return FCResultType.NEXT;
            //}
            #endregion


            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainMoveWait_FlowRun(object sender, EventArgs e)
        {
            if (!mtrRot.GetAxisIOStatus().SVON)
                mtrRot.ServoOn();

            if (!mtrFlip.GetAxisIOStatus().SVON)
                mtrFlip.ServoOn();

            if (!bHeadSafety) return FCResultType.IDLE;

            bool ret1 = mtrX.Goto(tpWait.GetValue(AxisName.X));
            bool ret2 = mtrY.Goto(tpWait.GetValue(AxisName.Y));
            bool ret3 = mtrFlip.Goto(tpWait.GetValue(AxisName.W1));
            bool ret4 = mtrRot.Goto(tpWait.GetValue(AxisName.W2));
            if (ret1 && ret2 && ret3 && ret4)
            {
                SysPara.WaitUnload = true;

                oStageVac.Off();

                RunTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainMES_FlowRun(object sender, EventArgs e)
        {
            if (!SysPara.IsMES) return FCResultType.NEXT;

            var x = SysPara.MESData.ToArray();
            if (MiddleLayer.ManualF.tightenFail) SysPara.ProductStatus = 'F';

            Task.Run(() =>
            {
                if (MiddleLayer.SystemF.chbxLocal.Checked)
                {
                    MiddleLayer.SystemF.TarGeneration(
                        SysPara.RouteLocal,
                        SysPara.WorkStartTimeStamp,
                        DateTime.Now,
                       sSerializer_SerialNumber,
                        SysPara.MESData.ToArray(),
                        SysPara.ProductStatus
                    );
                }

                if (MiddleLayer.SystemF.chbxExternal.Checked)
                {
                    MiddleLayer.SystemF.TarGeneration(
                        SysPara.RouteExternal,
                        SysPara.WorkStartTimeStamp,
                        DateTime.Now,
                       sSerializer_SerialNumber,
                        SysPara.MESData.ToArray(),
                        SysPara.ProductStatus
                    );
                }
            });

            //Crear Zip de graficos de torques
            string folderPath = @"C:\Users\SVGCHI_JDESAUTO\Documents\Torque";
            DiaFileZipper.ZipDiaFiles(folderPath, sSerializer_SerialNumber);

            return FCResultType.NEXT;
        }

        public class DiaFileZipper
        {
            public static void ZipDiaFiles(string folderPath, string serial)
            {
                // Validar que el folder existe
                if (!Directory.Exists(folderPath))
                {
                    Console.WriteLine("La ruta proporcionada no existe.");
                    return;
                }

                // Obtener todos los archivos .dia
                string[] diaFiles = Directory.GetFiles(folderPath, "*.dia");

                if (diaFiles.Length == 0)
                {
                    Console.WriteLine("No se encontraron archivos .dia en la ruta.");
                    return;
                }

                // Crear subcarpeta con el nombre del serial
                string serialFolderPath = Path.Combine(folderPath, serial);
                Directory.CreateDirectory(serialFolderPath);

                // Mover los archivos .dia a la nueva carpeta
                foreach (string file in diaFiles)
                {
                    string fileName = Path.GetFileName(file);
                    string destFile = Path.Combine(serialFolderPath, fileName);

                    if (File.Exists(destFile))
                    {
                        File.Delete(destFile);
                    }

                    File.Move(file, destFile);
                }

                // Crear el archivo .zip
                string zipPath = Path.Combine(folderPath, serial + ".zip");

                // Eliminar el zip si ya existe
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                ZipFile.CreateFromDirectory(serialFolderPath, zipPath);

                // (Opcional) Eliminar la carpeta temporal
                Directory.Delete(serialFolderPath, true);

                Console.WriteLine($"ZIP creado exitosamente: {zipPath}");
            }
        }

        private FCResultType fcAutoMainWaitUnload_FlowRun(object sender, EventArgs e)
        {
            DisplayMessage(txtInstruction, "Retire el producto");

            BlinkStartButton();

            if (MiddleLayer.SystemF.IB_BottomAreaSensor.IsOn() ||
                MiddleLayer.SystemF.IB_MiddleAreaSensor.IsOn() ||
                MiddleLayer.SystemF.IB_TopAreaSensor.IsOn() || SysPara.IsDryRun)
            {
                RunTM.Restart();
                SysPara.MESData.Clear();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoMainEnd_FlowRun(object sender, EventArgs e)
        {
            if (!SysPara.IsOneProcess)
            {
                if (SysPara.CurrentProcess < WorkCavity.Keys.Max())
                    SysPara.CurrentProcess++;
                else SysPara.CurrentProcess = 1;
            }

            MiddleLayer.ControlF.mcCtrl.AddCount(MachineMatrix._CountType.Pass, 1);
            MiddleLayer.ControlF.mcCtrl.End(MachineMatrix._Index.Main);

            //txtProcess2?.Invoke(new Action(() =>
            //{
            //    txtWork2.Text = txtInspect2.Text = "0";
            //}));
            //currentInspect = currentWork = 0;

            MiddleLayer.SystemF.OB_StartALight.Off();
            MiddleLayer.SystemF.OB_StartBLight.Off();

            //Reinicia la Variable
            SysPara.ProductStatus = 'P';
            return FCResultType.PREVIOUS;
        }
        #endregion

        #region Inspection Flow
        private FCResultType fcAutoInspectStart_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.hsInspection.Ready)
            {
                cylHead.Off();
                if (iHeadSsUp.IsOn())
                {
                    mcCtrl.Start(MachineMatrix._Index.Two);
                    SysPara.hsInspection.Ready = false;
                    InspectTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoInspectStageInspect_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety == false || bFlipSafety == false)
            {
                mtrHead.Goto(tpWait.GetValue(AxisName.Z));
                mtrFlip.Goto(tpWait.GetValue(AxisName.W1));
                return FCResultType.IDLE;
            }

            bool ret1 = mtrX.Goto(camPos[(int)WorkPos.X]);
            bool ret2 = mtrY.Goto(camPos[(int)WorkPos.Y]);
            bool ret3 = mtrRot.Goto(camPos[(int)WorkPos.Rot]);
            if (ret1 && ret2 && ret3)
            {
                InspectTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }
        private FCResultType fcAutoInspectAngleInspect_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety)
            {
                bool ret = mtrFlip.Goto(camPos[(int)WorkPos.Flip]);
                if (ret)
                {
                    InspectTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoInspectHeadInspect_FlowRun(object sender, EventArgs e)
        {
            bool ret = mtrHead.Goto(camPos[(int)WorkPos.Z_Cam]);
            if (ret)
            {
                InspectTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoInspectTriggerCam_FlowRun(object sender, EventArgs e)
        {
            if (errorTighten)
                SysPara.hsRetry.Ready = true;
            else
                SysPara.hsCam.Ready = true;
            InspectTM.Restart();
            return FCResultType.NEXT;
        }

        private FCResultType fcAutoInspectWaitCam_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.hsCam.Complete || SysPara.hsRetry.Complete)
            {
                SysPara.hsRetry.Reset();
                SysPara.hsCam.Reset();
                InspectTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoInspectHeadWait_FlowRun(object sender, EventArgs e)
        {
            bool ret = mtrHead.Goto(tpWait.GetValue(AxisName.Z));
            if (ret)
            {
                InspectTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }
        private FCResultType fcAutoInspectAngleWait_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety)
            {
                bool ret = mtrFlip.Goto(tpWait.GetValue(AxisName.W1));
                if (ret)
                {
                    InspectTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;


        }
        private FCResultType fcAutoInspectEnd_FlowRun(object sender, EventArgs e)
        {
            mcCtrl.End(MachineMatrix._Index.Two);

            string result = (SysPara.Status[(int)Step.Inspection] && VisionForm.Alignment.ContainsKey(currentInspect)) ?
                    $"X:{VisionForm.Alignment[currentInspect].X}mm,Y:{VisionForm.Alignment[currentInspect].Y}mm" : "";
            UpdateResult(
                GetDescription(SysPara.CurrentProcess, currentInspect),
                SysPara.Status[(int)Step.Inspection],
                result
            );

            SysPara.Step[(int)Step.Inspection] = false;
            SysPara.hsInspection.Complete = true;
            return FCResultType.PREVIOUS;
        }
        #endregion

        #region Pick Flow
        private FCResultType fcAutoPickStart_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.hsPick.Ready)
            {
                SysPara.hsPick.Ready = false;

                if (iHeadSsVac.IsOn())
                {
                    SysPara.hsRemove.Ready = true;
                    fcSubRmStart.TaskReset();
                    return FCResultType.CASE1;
                }

                PickTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoPickRemoveScrew_FlowRun(object sender, EventArgs e)
        {
            fcSubRmStart.TaskRun();
            if (SysPara.hsRemove.Complete)
            {
                SysPara.hsRemove.Reset();
                fcSubRmStart.TaskReset();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoPickStagePick_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety == false || bFlipSafety == false)
            {
                cylHead.Off();
                mtrHead.Goto(tpWait.GetValue(AxisName.Z));
                mtrFlip.Goto(tpWait.GetValue(AxisName.W1));
                return FCResultType.IDLE;
            }

            bool ret1 = mtrX.Goto(tpPickup.GetValue(AxisName.X));
            //bool ret2 = mtrY.Goto(tpPickup.GetValue(AxisName.Y));
            bool ret2 = true;

            if (ret1 && ret2)
            {
                PickTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoPickCheckFeed_FlowRun(object sender, EventArgs e)
        {
            if (MiddleLayer.FeederF.iScrewSensor.IsOn() && iHeadSsVac.IsOff())
            {
                RunTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoPickHeadPick_FlowRun(object sender, EventArgs e)
        {

            //bool ret = mtrHead.Goto(tpPickup.GetValue(AxisName.Z));
            //if (ret)
            //{
            //    cylHead.On();
            //    if (iHeadSsDown.IsOn())
            //    {
            //        PickTM.Restart();
            //        return FCResultType.NEXT;
            //    }

            //}
            //return FCResultType.IDLE;

            #region Old
            cylHead.On();
            bool ret = mtrHead.Goto(tpPickup.GetValue(AxisName.Z));
            if (ret && iHeadSsDown.IsOn())
            {
                PickTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
            #endregion
        }

        private FCResultType fcAutoPickVacOn_FlowRun(object sender, EventArgs e)
        {
            oHeadBlow.Off();
            oHeadVac.On();
            if (iHeadSsVac.IsOn() || SysPara.IsDryRun)
            {
                mtrHead.SpeedRatio = 100.0;
                mtrHead.WorkSpeed = GetSettingValue("MSet", "PickHead1");
                PickTM.Restart();
                return FCResultType.NEXT;
            }

            int timeout = GetSettingValue("PSet", "VacTimeout");
            if (PickTM.IsOn(timeout))
            {
                oHeadVac.Off();
                mtrHead.Goto(tpWait.GetValue(AxisName.Z));
                cylHead.Off();
                if (bHeadSafety) return FCResultType.CASE1;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoPickHeadGap_FlowRun(object sender, EventArgs e)
        {
            double target = tpPickup.GetValue(AxisName.Z) - GetSettingValue("PSet", "Gap");
            bool ret = mtrHead.Goto(target);
            if (ret)
            {
                mtrHead.SpeedRatio = MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio");
                mtrHead.WorkSpeed = GetSettingValue("MSet", "SpeedHead1");
                PickTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoPickHeadWait_FlowRun(object sender, EventArgs e)
        {
            if (!PickTM.IsOn(200)) return FCResultType.IDLE;

            cylHead.Off();
            bool ret = mtrHead.Goto(tpWait.GetValue(AxisName.Z));
            if (ret && iHeadSsUp.IsOn())
            {
                PickTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoPickEnd_FlowRun(object sender, EventArgs e)
        {
            SysPara.Step[(int)Step.Pick] = false;
            SysPara.hsPick.Complete = true;
            return FCResultType.PREVIOUS;
        }
        #endregion

        #region Work Flow
        private FCResultType fcAutoWorkStart_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.hsWork.Ready)
            {
                mcCtrl.Start(MachineMatrix._Index.Three);
                SysPara.hsWork.Ready = false;
                WorkTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoWorkStageWork_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety == false || bFlipSafety == false)
            {
                cylHead.Off();
                mtrHead.Goto(tpWait.GetValue(AxisName.Z));
                mtrFlip.Goto(tpWait.GetValue(AxisName.W1));
                return FCResultType.IDLE;
            }

            bool ret1 = mtrX.Goto(screwPos[(int)WorkPos.X]);
            bool ret2 = mtrY.Goto(screwPos[(int)WorkPos.Y]);
            bool ret3 = mtrRot.Goto(screwPos[(int)WorkPos.Rot]);
            if (ret1 && ret2 && ret3)
            {
                mtrRot.ServoOff();
                WorkTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }
        private FCResultType fcAutoWorkAngleWork_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety)
            {
                bool ret = mtrFlip.Goto(screwPos[(int)WorkPos.Flip]);
                if (ret)
                {
                    WorkTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;

        }
        private FCResultType fcAutoWorkHeadTouch_FlowRun(object sender, EventArgs e)
        {
            cylHead.On();
            bool ret = mtrHead.Goto(screwPos[(int)WorkPos.Z_Touch]);
            if (ret && iHeadSsDown.IsOn())
            {
                mtrHead.SpeedRatio = 100.0;
                mtrHead.WorkSpeed = GetSettingValue("MSet", "TightHead1");

                if (SysPara.IsTightening && !SysPara.IsDryRun)
                {
                    SysPara.hsTightening.Ready = true;
                    fcAutoTightenStart.TaskReset();
                }

                WorkTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoWorkHeadTighten_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.IsTightening)
                fcAutoTightenStart.TaskRun();

            if (!SysPara.hsTightening.Busy && SysPara.IsTightening && !SysPara.IsDryRun)
                return FCResultType.IDLE;

            bool ret = mtrHead.Goto(screwPos[(int)WorkPos.Z_Tighten]);
            if (ret && ((SysPara.hsTightening.Complete && SysPara.IsTightening) || !SysPara.IsTightening || SysPara.IsDryRun))
            {

                fcAutoTightenStart.TaskReset();
                mtrHead.SpeedRatio = MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio");
                mtrHead.WorkSpeed = GetSettingValue("MSet", "SpeedHead1");
                WorkTM.Restart();
                return FCResultType.NEXT;
            }

            return FCResultType.IDLE;
        }

        private FCResultType fcAutoWorkHeadWait_FlowRun(object sender, EventArgs e)
        {
            if (!WorkTM.IsOn(500)) return FCResultType.IDLE;

            oHeadBlow.Off();
            oHeadVac.Off();
            cylHead.Off();
            bool ret = mtrHead.Goto(tpWait.GetValue(AxisName.Z));
            if (ret && iHeadSsUp.IsOn())
            {
                mtrRot.ServoOn();
                SysPara.hsTightening.Reset();
                WorkTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoWorkAngleWait_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety)
            {
                bool ret = mtrFlip.Goto(tpWait.GetValue(AxisName.W1));
                if (ret)
                {
                    WorkTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;

        }
        private FCResultType fcAutoWorkEnd_FlowRun(object sender, EventArgs e)
        {
            mcCtrl.End(MachineMatrix._Index.Three);

            string result = (SysPara.Status[(int)Step.Tightening] && SysPara.IsTightening) ?
                    $"Torque:{Screw.ResultData.PeakTorque}cNm,Angle:{Screw.ResultData.TotalAngle}°" : "";
            UpdateResult(
                GetDescription(SysPara.CurrentProcess, currentWork),
                SysPara.Status[(int)Step.Tightening],
                result
            );

            SysPara.Step[(int)Step.Work] = false;
            SysPara.hsWork.Complete = true;
            return FCResultType.PREVIOUS;
        }
        #endregion

        #region Manual Flow Auto
        private FCResultType fcAutoManualStart_FlowRun(object sender, EventArgs e)
        {
            if(SysPara.hsManual.Ready)
            {
                SysPara.hsManual.Ready = false;
                ManualForm.ManualCavity = 1;
                RunTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoManualTriggerTighten_FlowRun(object sender, EventArgs e)
        {
            SysPara.hsManualTighten.Ready = true;
            RunTM.Restart();
            return FCResultType.NEXT;
        }

        private FCResultType fcAutoManualWaitTighten_FlowRun(object sender, EventArgs e)
        {
            if(SysPara.hsManualTighten.Complete)
            {
                if(!ManualForm.isInspect)
                {
                    SysPara.hsManualTighten.Reset();
                    return FCResultType.CASE1;
                }
                    
                else
                {
                    BlinkStartButton();
                    if (MiddleLayer.SystemF.IB_BtnStartA.IsOn() && MiddleLayer.SystemF.IB_BtnStartB.IsOn())
                    {
                        SysPara.hsManualTighten.Reset();
                        MiddleLayer.SystemF.OB_StartALight.Off();
                        MiddleLayer.SystemF.OB_StartBLight.Off();
                        RunTM.Restart();
                        return FCResultType.NEXT;
                    }
                }       
            }
            return FCResultType.IDLE ;
        }

        private FCResultType fcAutoManualMoveInspect_FlowRun(object sender, EventArgs e)
        {
            if(ManualForm.ManualCavity == 1)
            {
                bool ret1 = mtrX.Goto(tpInspectScrew1.GetValue(AxisName.X));
                bool ret2 = mtrY.Goto(tpInspectScrew1.GetValue(AxisName.Y));
                if (ret1 && ret2)
                { 
                    RunTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            if (ManualForm.ManualCavity == 2)
            {
                bool ret1 = mtrX.Goto(tpInspectScrew2.GetValue(AxisName.X));
                bool ret2 = mtrY.Goto(tpInspectScrew2.GetValue(AxisName.Y));
                if (ret1 && ret2)
                {
                    RunTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE ;
        }

        private FCResultType fcAutoManualHeadInspect_FlowRun(object sender, EventArgs e)
        {
            if (ManualForm.ManualCavity == 1)
            {
                bool ret = mtrHead.Goto(tpInspectScrew1.GetValue(AxisName.Z));
                if (ret)
                {
                    RunTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            if (ManualForm.ManualCavity == 2)
            {
                bool ret = mtrHead.Goto(tpInspectScrew2.GetValue(AxisName.Z));
                if (ret)
                {
                    RunTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoManualTriggerCam_FlowRun(object sender, EventArgs e)
        {
            SysPara.hsManualInspection.Ready = true;
            RunTM.Restart();
            return FCResultType.NEXT;
        }

        private FCResultType fcAutoManualWaitCam_FlowRun(object sender, EventArgs e)
        {
            if(SysPara.hsManualInspection.Complete)
            {
                SysPara.hsManualInspection.Reset();

                if(MiddleLayer.VisionF.ManualResult)
                {
                    RunTM.Restart();
                    return FCResultType.NEXT;
                }
                else
                {
                    RunTM.Restart();
                    return FCResultType.CASE1;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoManualCheckCavity_FlowRun(object sender, EventArgs e)
        {
            if(ManualForm.ManualCavity == 1)
            {
                ManualForm.ManualCavity = 2;
                RunTM.Restart();
                return FCResultType.PREVIOUS;
            }
            else
            {
                RunTM.Restart();
                return FCResultType.NEXT;
            }
        }

        private FCResultType fcAutoManualStageManual_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety)
            {
                bool ret1 = mtrX.Goto(tpWait.GetValue(AxisName.X));
                bool ret2 = mtrY.Goto(tpWait.GetValue(AxisName.Y));
                if (ret1 && ret2)
                {
                    RunTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoManualEnd_FlowRun(object sender, EventArgs e)
        {
            SysPara.hsManual.Complete = true;
            return FCResultType.PREVIOUS;
        }

        private FCResultType fcAutoManualHeadWait_FlowRun(object sender, EventArgs e)
        {
            bool ret = mtrHead.Goto(tpWait.GetValue(AxisName.Z));
            if(ret)
            {
                RunTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoManualStageWait_FlowRun(object sender, EventArgs e)
        {
            bool ret1 = mtrX.Goto(tpWait.GetValue(AxisName.X));
            bool ret2 = mtrY.Goto(tpWait.GetValue(AxisName.Y));
            if (ret1 && ret2)
            {
                RunTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoManualRetry_FlowRun(object sender, EventArgs e)
        {
            return FCResultType.PREVIOUS;
        }
        #endregion

        #region Test Torque Flow
        private FCResultType fcSubTorqueStart_FlowRun(object sender, EventArgs e)
        {
            WorkTM.Restart();
            return FCResultType.NEXT;
        }

        private FCResultType fcSubTorqueHeadWait_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety)
            {
                bool ret = mtrHead.Goto(tpWait.GetValue(AxisName.Z));
                if (ret)
                {
                    WorkTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcSubTorqueStageWait_FlowRun(object sender, EventArgs e)
        {
            bool ret1 = mtrX.Goto(tpWait.GetValue(AxisName.X));
            bool ret2 = mtrY.Goto(tpWait.GetValue(AxisName.Y));
            if (ret1 && ret2)
            {
                WorkTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcSubTorqueWaitPick_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.hsPick.Complete)
            {
                SysPara.hsPick.Reset();
                WorkTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcSubTorqueStageTorque_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety)
            {
                bool ret1 = mtrX.Goto(tpTestTorque.GetValue(AxisName.X));
                bool ret2 = mtrY.Goto(tpTestTorque.GetValue(AxisName.Y));
                if (ret1 && ret2)
                {
                    WorkTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcSubTorqueHeadTorque_FlowRun(object sender, EventArgs e)
        {
            bool ret = mtrHead.Goto(tpTestTorque.GetValue(AxisName.Z));
            if (ret)
            {
                if (SysPara.IsTightening && !SysPara.IsDryRun)
                {
                    SysPara.hsTightening.Ready = true;
                    fcAutoTightenStart.TaskReset();
                }
                WorkTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcSubTorqueWaitTighten_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.IsTightening)
                fcAutoTightenStart.TaskRun();

            if (!SysPara.hsTightening.Busy && SysPara.IsTightening && !SysPara.IsDryRun)
                return FCResultType.IDLE;

            if ((SysPara.hsTightening.Complete && SysPara.IsTightening) || !SysPara.IsTightening || SysPara.IsDryRun)
            {
                fcAutoTightenStart.TaskReset();
                WorkTM.Restart();
                return FCResultType.NEXT;
            }

            return FCResultType.IDLE;
        }

        private FCResultType fcSubTorqueHeadWait2_FlowRun(object sender, EventArgs e)
        {
            bool ret = mtrHead.Goto(tpWait.GetValue(AxisName.Z));
            if (ret)
            {
                WorkTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcSubTorqueStageWait2_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety)
            {
                bool ret1 = mtrX.Goto(tpWait.GetValue(AxisName.X));
                bool ret2 = mtrY.Goto(tpWait.GetValue(AxisName.Y));
                if (ret1 && ret2)
                {
                    WorkTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcSubTorqueEnd_FlowRun(object sender, EventArgs e)
        {
            SysPara.Step[(int)Step.Torque] = false;
            SysPara.WaitUnload = true;
            return FCResultType.IDLE;
        }
        #endregion

        #region Tightening Flow
        private FCResultType fcAutoTightenStart_FlowRun(object sender, EventArgs e)
        {
            if (iReady.IsOff())
            {
                oTight.Off();
                oLoose.Off();
                oReset.Off();
                return FCResultType.IDLE;
            }

            if (iReady.IsOn() && SysPara.hsTightening.Ready)
            {
                SysPara.hsTightening.Ready = false;
                oTight.Off(); //oReset.Off(); oLoose.Off();

                resultReceived = false;
                msgAccepted = false;

                TighteningTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoTightenChangeProg_FlowRun(object sender, EventArgs e)
        {
            // find PSet based on current process
            var PSet = WorkDataSet.Tables[0].AsEnumerable()
                .Where(row => int.TryParse(row["Process"].ToString(), out int res)
                    && res == SysPara.CurrentProcess)
                .FirstOrDefault()["PSet"].ToString().Trim();

            //Console.WriteLine("PSet: " + PSet);
            Screw.SelectPSet(int.Parse(PSet));
            TighteningTM.Restart();
            return FCResultType.NEXT;
        }

        private FCResultType fcAutoTightenCheckPSet_FlowRun(object sender, EventArgs e)
        {
            if (msgAccepted)
            {
                oReset.Off();
                oTight.On();
                SysPara.hsTightening.Busy = true;
                msgAccepted = false;
                TighteningTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoTightenOn_FlowRun(object sender, EventArgs e)
        {
            if (iBusy.IsOn()) return FCResultType.IDLE;

            if ((iOK.IsOn() || iNG.IsOn() || iError.IsOn()) && iBusy.IsOff())
            {
                TighteningTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoTightenStatus_FlowRun(object sender, EventArgs e)
        {
            if (!TighteningTM.IsOn(500)) return FCResultType.IDLE;

            if ((iOK.IsOn() || iNG.IsOn() || iError.IsOn()))
            {
                msgAccepted = false;
                //Screw.SubscribeData();
                TighteningTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoTightenReadData_FlowRun(object sender, EventArgs e)
        {
            if (!resultReceived) return FCResultType.IDLE;

            if (TighteningTM.IsOn(100))
            {
                double upperTorque = GetRecipeValue("RSet", "UpperTorque");
                double lowerTorque = GetRecipeValue("RSet", "LowerTorque");
                bool validTorque = Screw.ResultData.PeakTorque >= lowerTorque &&
                                   Screw.ResultData.PeakTorque <= upperTorque;

                SysPara.Status[(int)Step.Tightening] = (SysPara.IsDryRun || !SysPara.IsTightening) ? true : iOK.IsOn() && validTorque;
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoTightenEnd_FlowRun(object sender, EventArgs e)
        {
            // Add logging for Tightening result
            MiddleLayer.LogF.AddLog(FunctionForms.LogForm.LogType.Production,
            $"Code: {SysPara.Barcode}," +
            $"Tightening Result: {Screw.ResultData.PeakTorque:F3},mNm," +
            $"{Screw.ResultData.TotalAngle},°," +
            $"{Screw.ResultData.Duration:F3},s," +
            $"Status: {(iOK.IsOn() ? "OK" : "NG")}");

            if (iOK.IsOff())
                MasterCore.Alarm.Show("9021", "Tightening failed");

            SysPara.Step[(int)Step.Tightening] = false;
            SysPara.hsTightening.Complete = true;
            return FCResultType.IDLE;
        }
        #endregion

        #region Re Inspection Flow
        private FCResultType fcAutoReInspectStart_FlowRun(object sender, EventArgs e)
        {
            if (!rdbtnStep.Checked)
                currentInspect = inspectCavity.Count > 0 ? inspectCavity.Dequeue() : 0;

            if (currentInspect != 0)
            {
                GetCamPos(SysPara.CurrentProcess, currentInspect, out camPos);
                ReInspectTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoReInspectStageInspect_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety == false || bFlipSafety == false)
            {
                cylHead.Off();
                mtrHead.Goto(tpWait.GetValue(AxisName.Z));
                mtrFlip.Goto(tpWait.GetValue(AxisName.W1));
                return FCResultType.IDLE;
            }

            bool ret1 = mtrX.Goto(camPos[(int)WorkPos.X]);
            bool ret2 = mtrY.Goto(camPos[(int)WorkPos.Y]);
            bool ret3 = mtrRot.Goto(camPos[(int)WorkPos.Rot]);
            if (ret1 && ret2 && ret3)
            {
                ReInspectTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }
        private FCResultType fcAutoReInspectAngleInspect_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety)
            {
                bool ret = mtrFlip.Goto(camPos[(int)WorkPos.Flip]);
                if (ret)
                {
                    ReInspectTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;

        }
        private FCResultType fcAutoReInspectHeadInspect_FlowRun(object sender, EventArgs e)
        {
            bool ret = mtrHead.Goto(camPos[(int)WorkPos.Z_Cam]);
            if (ret)
            {
                ReInspectTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoReInspectTriggerCam_FlowRun(object sender, EventArgs e)
        {
            SysPara.hsCam.Ready = true;
            ReInspectTM.Restart();
            return FCResultType.NEXT;
        }

        private FCResultType fcAutoReInspectWaitCam_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.hsCam.Complete)
            {
                SysPara.hsCam.Reset();

                if (!SysPara.Status[(int)Step.Inspection] && !SysPara.Step[(int)Step.ReInspect])
                {
                    errorInspection = true;
                    fcErrorStart.TaskReset();
                    fcErrorStart.TaskRun();
                    return FCResultType.CASE1;
                }

                ReInspectTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoReInspectHandleError_FlowRun(object sender, EventArgs e)
        {
            if (errorInspection)
                fcErrorStart.TaskRun();

            BlinkStartButton();

            if (MiddleLayer.SystemF.IB_BtnStartA.IsOn() && MiddleLayer.SystemF.IB_BtnStartB.IsOn()
                && !IsError)
            {
                MiddleLayer.SystemF.OB_StartALight.On();
                MiddleLayer.SystemF.OB_StartBLight.On();

                SysPara.WaitLoad = false;

                fcErrorEnd.TaskReset();
                RunTM.Restart();
                return FCResultType.NEXT;
            }

            if (SysPara.hsReInspect.Busy)
            {
                fcErrorEnd.TaskReset();
                RunTM.Restart();
                return FCResultType.CASE1;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcAutoReInspectHeadWait_FlowRun(object sender, EventArgs e)
        {
            bool ret = mtrHead.Goto(tpWait.GetValue(AxisName.Z));
            if (ret)
            {
                ReInspectTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }
        private FCResultType fcAutoReInspectAngleWait_FlowRun(object sender, EventArgs e)
        {
            if (bHeadSafety)
            {
                bool ret = mtrFlip.Goto(tpWait.GetValue(AxisName.W1));
                if (ret)
                {
                    ReInspectTM.Restart();
                    return FCResultType.NEXT;
                }
            }
            return FCResultType.IDLE;

        }
        private FCResultType fcAutoReInspectCheckCavity_FlowRun(object sender, EventArgs e)
        {
            if (inspectCavity.Count > 0 && !rdbtnStep.Checked)
                return FCResultType.PREVIOUS;

            return FCResultType.NEXT;
        }

        private FCResultType fcAutoReInspectEnd_FlowRun(object sender, EventArgs e)
        {
            SysPara.Step[(int)Step.ReInspect] = false;
            SysPara.hsReInspect.Complete = true;
            return FCResultType.IDLE;
        }
        #endregion

        #region Remove Flow
        private FCResultType fcSubRmStart_FlowRun(object sender, EventArgs e)
        {
            if (SysPara.hsRemove.Ready)
            {
                SysPara.hsRemove.Ready = false;
                RemoveTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcSubRmHeadWait_FlowRun(object sender, EventArgs e)
        {
            cylHead.Off();
            bool ret = mtrHead.Goto(tpWait.GetValue(AxisName.Z));
            if (ret && iHeadSsUp.IsOn())
            {
                RemoveTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcSubRmCheckVac_FlowRun(object sender, EventArgs e)
        {
            return FCResultType.NEXT;
            if (iHeadSsVac.IsOn() || SysPara.IsDryRun)
            {
                RemoveTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcSubRmStageRemove_FlowRun(object sender, EventArgs e)
        {
            bool ret = mtrX.Goto(tpRemove.GetValue(AxisName.X));
            bool ret2 = mtrY.Goto(tpRemove.GetValue(AxisName.Y));
            if (ret && ret2)
            {
                RemoveTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcSubRmHeadRemove_FlowRun(object sender, EventArgs e)
        {
            bool ret = mtrHead.Goto(tpRemove.GetValue(AxisName.Z));
            if (ret)
            {
                oHeadVac.Off();
                oHeadBlow.On();
                RemoveTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcSubRmHeadWait2_FlowRun(object sender, EventArgs e)
        {
            if (!RemoveTM.IsOn(1000)) return FCResultType.IDLE;

            oHeadVac.On();
            oHeadBlow.Off();
            bool ret = mtrHead.Goto(tpWait.GetValue(AxisName.Z));
            if (ret)
            {
                RemoveTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcSubRmEnd_FlowRun(object sender, EventArgs e)
        {
            if (!RemoveTM.IsOn(500) || iHeadSsVac.IsOn())
                return FCResultType.IDLE;

            oHeadVac.Off();
            SysPara.Step[(int)Step.Remove] = false;
            SysPara.hsRemove.Complete = true;
            return FCResultType.PREVIOUS;
        }
        #endregion

        #region Error Handling Flow
        private FCResultType fcErrorStart_FlowRun(object sender, EventArgs e)
        {
            IsError = true;
            ErrorTM.Restart();
            return FCResultType.NEXT;
        }

        private FCResultType fcErrorHeadWait_FlowRun(object sender, EventArgs e)
        {
            bool ret = mtrHead.Goto(tpWait.GetValue(AxisName.Z));
            if (ret)
            {
                ErrorTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcErrorStageWait_FlowRun(object sender, EventArgs e)
        {
            bool ret1 = mtrX.Goto(tpWait.GetValue(AxisName.X));
            bool ret2 = mtrY.Goto(tpWait.GetValue(AxisName.Y));
            if (ret1 && ret2)
            {
                ErrorTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcErrorRotWait_FlowRun(object sender, EventArgs e)
        {
            bool ret = mtrRot.Goto(tpWait.GetValue(AxisName.W2));
            if (ret)
            {
                ErrorTM.Restart();
                //Falla Unidad por error Javier Villagran
                SysPara.ProductStatus = 'F';
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcErrorFlipWait_FlowRun(object sender, EventArgs e)
        {
            bool ret = mtrFlip.Goto(tpWait.GetValue(AxisName.W1));
            if (ret)
            {
                SysPara.WaitLoad = true;
                ErrorTM.Restart();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcErrorVacOff_FlowRun(object sender, EventArgs e)
        {
            if (!isRetry && !VisionForm.Retry)
                return FCResultType.CASE1;

            if ((errorInspection && VisionForm.RetryNum == 0) ||
                (errorTighten && numRetry == 0))
                return FCResultType.CASE1;

            ErrorTM.Restart();
            return FCResultType.NEXT;
        }

        private FCResultType fcErrorRetry_FlowRun(object sender, EventArgs e)
        {
            if (errorInspection) VisionForm.RetryNum--;
            if (errorTighten) numRetry--;

            return FCResultType.NEXT;
        }

        private FCResultType fcErrorRestart_FlowRun(object sender, EventArgs e)
        {
            oStageVac.Off();
            IsError = false;
            fcAutoMainConfirm.TaskReset();
            fcAutoMainStart.TaskReset();
            MiddleLayer.VisionF.fcAutoCamStart.TaskReset();
            MiddleLayer.VisionF.fcAutoCamWaitReady.TaskReset();

            numRetry = GetSettingValue("PSet", "NumRetry");
            VisionForm.RetryNum = MiddleLayer.VisionF.ResetRetry();
            MiddleLayer.VisionF.Reset = true;

            SysPara.hsReInspect.Busy = true;

            if (!SysPara.IsOneProcess)
            {
                SysPara.CurrentProcess = 1;
                VisionForm.CurrentProcess = 1;
            }

            MiddleLayer.ControlF.mcCtrl.AddCount(MachineMatrix._CountType.Fail, 1);

            ErrorTM.Restart();
            return FCResultType.NEXT;
        }

        private FCResultType fcErrorEnd_FlowRun(object sender, EventArgs e)
        {
            IsError = false;
            return FCResultType.IDLE;
        }
        #endregion

        #region Test Pickup
        private FCResultType fcTestPickStart_FlowRun(object sender, EventArgs e)
        {
            return FCResultType.NEXT;
        }

        private FCResultType fcTestPickTriggerPick_FlowRun(object sender, EventArgs e)
        {
            fcAutoPickStart.TaskReset();
            SysPara.hsPick.Ready = true;
            return FCResultType.NEXT;
        }

        private FCResultType fcTestPickWaitPick_FlowRun(object sender, EventArgs e)
        {
            fcAutoPickStart.TaskRun();
            if (SysPara.hsPick.Complete)
            {
                fcAutoPickStart.TaskReset();
                SysPara.hsPick.Reset();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcTestPickRemove_FlowRun(object sender, EventArgs e)
        {
            fcSubRmStart.TaskReset();
            SysPara.hsRemove.Ready = true;
            return FCResultType.NEXT;
        }

        private FCResultType fcTestPickWaitRemove_FlowRun(object sender, EventArgs e)
        {
            fcSubRmStart.TaskRun();
            if (SysPara.hsRemove.Complete)
            {
                fcSubRmStart.TaskReset();
                SysPara.hsRemove.Reset();
                return FCResultType.NEXT;
            }
            return FCResultType.IDLE;
        }

        private FCResultType fcTestPickEnd_FlowRun(object sender, EventArgs e)
        {
            lblCount?.Invoke(new Action(() =>
            {
                int count = Convert.ToInt32(lblNum.Text);
                lblNum.Text = (count + 1).ToString();
            }));
            return FCResultType.PREVIOUS;
        }
        #endregion

        #endregion

        #region Other

        #region Recipe Editor
        private void btnFileRead_Click(object sender, EventArgs e)
        {
            ReadTeachingPoint(Path.Combine(RecipeFolder, txtFilePath.Text));
        }

        private void btnFileWrite_Click(object sender, EventArgs e)
        {
            WriteTeachingPoint(WorkDataSet, txtFilePath.Text);
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            ReadTeachingPoint(Path.Combine(RecipeFolder, GetRecipeValue("RSet", "FilePath")));
        }

        private void btnCopy_Click(object sender, EventArgs e)
        {
            // Copy the current encoder position to the selected row of the data grid view
            int rowIndex = dtWork.CurrentCell.RowIndex;
            if (rowIndex < 0) return;

            dtWork.Rows[rowIndex].Cells["X"].Value = mtrX.GetEncoderPosition().ToString("F3");
            dtWork.Rows[rowIndex].Cells["Y"].Value = mtrY.GetEncoderPosition().ToString("F3");
            dtWork.Rows[rowIndex].Cells["Z_Cam"].Value = mtrHead.GetEncoderPosition().ToString("F3");
            dtWork.Rows[rowIndex].Cells["Z_Touch"].Value = mtrHead.GetEncoderPosition().ToString("F3");
            dtWork.Rows[rowIndex].Cells["Z_Tighten"].Value = mtrHead.GetEncoderPosition().ToString("F3");
            dtWork.Rows[rowIndex].Cells["Rot"].Value = mtrRot.GetEncoderPosition().ToString("F3");
            dtWork.Rows[rowIndex].Cells["Flip"].Value = mtrFlip.GetEncoderPosition().ToString("F3");
        }

        private void dtWork_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            // check if that cell is in column name "X", "Y", "Z_Cam", "Z_Touch", "Z_Tighten"
            if (dtWork.Columns[e.ColumnIndex].Name == "X")
                dtWork.Rows[e.RowIndex].Cells["X"].Value = mtrX.GetEncoderPosition().ToString("F3");
            if (dtWork.Columns[e.ColumnIndex].Name == "Y")
                dtWork.Rows[e.RowIndex].Cells["Y"].Value = mtrY.GetEncoderPosition().ToString("F3");
            if (dtWork.Columns[e.ColumnIndex].Name == "Z_Cam")
                dtWork.Rows[e.RowIndex].Cells["Z_Cam"].Value = mtrHead.GetEncoderPosition().ToString("F3");
            if (dtWork.Columns[e.ColumnIndex].Name == "Z_Touch")
                dtWork.Rows[e.RowIndex].Cells["Z_Touch"].Value = mtrHead.GetEncoderPosition().ToString("F3");
            if (dtWork.Columns[e.ColumnIndex].Name == "Z_Tighten")
                dtWork.Rows[e.RowIndex].Cells["Z_Tighten"].Value = mtrHead.GetEncoderPosition().ToString("F3");
            if (dtWork.Columns[e.ColumnIndex].Name == "Rot")
                dtWork.Rows[e.RowIndex].Cells["Rot"].Value = mtrRot.GetEncoderPosition().ToString("F3");
            if (dtWork.Columns[e.ColumnIndex].Name == "Flip")
                dtWork.Rows[e.RowIndex].Cells["Flip"].Value = mtrFlip.GetEncoderPosition().ToString("F3");
        }

        private void btnXY_Click(object sender, EventArgs e)
        {
            int rowIndex = dtWork.CurrentCell.RowIndex;
            if (rowIndex < 0) return;

            double X = dtWork.Rows[rowIndex].Cells["X"].Value == null ? 0 : Convert.ToDouble(dtWork.Rows[rowIndex].Cells["X"].Value);
            double Y = dtWork.Rows[rowIndex].Cells["Y"].Value == null ? 0 : Convert.ToDouble(dtWork.Rows[rowIndex].Cells["Y"].Value);

            if (CheckSafetyPositionXY(X, Y) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrX.Goto(X);
            mtrY.Goto(Y);
        }

        private void btnZTouch_Click(object sender, EventArgs e)
        {
            int rowIndex = dtWork.CurrentCell.RowIndex;
            if (rowIndex < 0) return;

            double Z = dtWork.Rows[rowIndex].Cells["Z_Touch"].Value == null ? 0 : Convert.ToDouble(dtWork.Rows[rowIndex].Cells["Z_Touch"].Value);

            if (CheckSafetyPositionZ(Z) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrHead.Goto(Z);
        }

        private void btnZTighten_Click(object sender, EventArgs e)
        {
            int rowIndex = dtWork.CurrentCell.RowIndex;
            if (rowIndex < 0) return;

            double Z = dtWork.Rows[rowIndex].Cells["Z_Tighten"].Value == null ? 0 : Convert.ToDouble(dtWork.Rows[rowIndex].Cells["Z_Tighten"].Value);

            if (CheckSafetyPositionZ(Z) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrHead.Goto(Z);
        }

        private void btnZInspect_Click(object sender, EventArgs e)
        {
            int rowIndex = dtWork.CurrentCell.RowIndex;
            if (rowIndex < 0) return;

            double Z = dtWork.Rows[rowIndex].Cells["Z_Cam"].Value == null ? 0 : Convert.ToDouble(dtWork.Rows[rowIndex].Cells["Z_Cam"].Value);

            if (CheckSafetyPositionZ(Z) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrHead.Goto(Z);
        }

        private void btnZWait_Click(object sender, EventArgs e)
        {
            if (CheckSafetyPositionZ(tpWait.GetValue(AxisName.Z)) == false) return;
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrHead.Goto(tpWait.GetValue(AxisName.Z));
        }

        private void btnRotY_Click(object sender, EventArgs e)
        {
            int rowIndex = dtWork.CurrentCell.RowIndex;
            if (rowIndex < 0) return;

            double Rot = dtWork.Rows[rowIndex].Cells["Rot"].Value == null ? 0 : Convert.ToDouble(dtWork.Rows[rowIndex].Cells["Rot"].Value);
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrRot.Goto(Rot);
        }

        private void btnFlip_Click(object sender, EventArgs e)
        {
            int rowIndex = dtWork.CurrentCell.RowIndex;
            if (rowIndex < 0) return;

            double Flip = dtWork.Rows[rowIndex].Cells["Flip"].Value == null ? 0 : Convert.ToDouble(dtWork.Rows[rowIndex].Cells["Flip"].Value);
            SetSpeed(MiddleLayer.SystemF.GetSettingValue("PSet", "MachineSpeedRatio"));

            mtrFlip.Goto(Flip);
        }

        private void btnRowUp_Click(object sender, EventArgs e)
        {
            if (dtWork.CurrentRow == null) return;

            var selectedView = dtWork.CurrentRow.DataBoundItem as DataRowView;
            if (selectedView == null) return;

            btnRowDown.Enabled = false;

            var dataTable = WorkDataSet.Tables[0];
            int realIndex = dataTable.Rows.IndexOf(selectedView.Row);
            if (realIndex <= 0) return;

            SwapDataRow(dataTable.Rows[realIndex], dataTable.Rows[realIndex - 1]);

            // Reselect
            dtWork.ClearSelection();
            dtWork.Rows[realIndex - 1].Selected = true;
            dtWork.CurrentCell = dtWork.Rows[realIndex - 1].Cells[0];
        }

        private void btnRowDown_Click(object sender, EventArgs e)
        {
            if (dtWork.CurrentRow == null) return;

            var selectedView = dtWork.CurrentRow.DataBoundItem as DataRowView;
            if (selectedView == null) return;

            btnRowUp.Enabled = false;

            var dataTable = WorkDataSet.Tables[0];
            int realIndex = dataTable.Rows.IndexOf(selectedView.Row);
            if (realIndex >= dataTable.Rows.Count - 1) return;

            SwapDataRow(dataTable.Rows[realIndex], dataTable.Rows[realIndex + 1]);

            // Reselect
            dtWork.ClearSelection();
            dtWork.Rows[realIndex + 1].Selected = true;
            dtWork.CurrentCell = dtWork.Rows[realIndex + 1].Cells[0];
        }
        #endregion

        #region Step Operation
        private void btnStepInspection_Click(object sender, EventArgs e)
        {
            GetCamPos(
                int.Parse(cbbStepProcess.Text),
                int.Parse(cbbStepInspection.Text),
                out camPos
            );

            if (CheckSafetyPositionXY(camPos[(int)WorkPos.X], camPos[(int)WorkPos.Y]) == false
                || CheckSafetyPositionZ(camPos[(int)WorkPos.Z_Cam]) == false)
                return;

            SysPara.CurrentProcess = int.Parse(cbbStepProcess.Text);
            (int Cavity, bool Alignment, bool ReInspect) cavityRun = (int.Parse(cbbStepInspection.Text), false, false);
            foreach (var cavity in WorkCavity[SysPara.CurrentProcess])
            {
                if (cavity.Alignment && cavity.Cavity == cavityRun.Cavity)
                {
                    cavityRun.Alignment = true;
                    break;
                }
            }
            MiddleLayer.VisionF.currentCavity = cavityRun;

            SysPara.IsOneProcess = true;
            SysPara.hsInspection.Ready = true;
            SysPara.Step[(int)Step.Inspection] = true;

            MiddleLayer.RunReset();
            MiddleLayer.MainF.btnStart.PerformClick();

            Task.Run(() => 
            {
                while (SysPara.Step[(int)Step.Inspection]) ;

                MiddleLayer.StopRun();
                MiddleLayer.RunReset();
            });
        }

        private void btnStepReInspect_Click(object sender, EventArgs e)
        {
            GetCamPos(
                int.Parse(cbbStepProcess.Text),
                int.Parse(cbbStepInspection.Text),
                out camPos
            );

            if (CheckSafetyPositionXY(camPos[(int)WorkPos.X], camPos[(int)WorkPos.Y]) == false
                || CheckSafetyPositionZ(camPos[(int)WorkPos.Z_Cam]) == false)
                return;

            SysPara.CurrentProcess = int.Parse(cbbStepProcess.Text);
            (int Cavity, bool Alignment, bool ReInspect) cavityRun = (int.Parse(cbbStepInspection.Text), false, true);
            MiddleLayer.VisionF.currentCavity = cavityRun;
            currentInspect = cavityRun.Cavity;

            SysPara.IsOneProcess = true;
            SysPara.hsInspection.Ready = true;
            SysPara.Step[(int)Step.ReInspect] = true;

            MiddleLayer.RunReset();
            MiddleLayer.MainF.btnStart.PerformClick();

            Task.Run(() =>
            {
                while (SysPara.Step[(int)Step.ReInspect]) ;

                MiddleLayer.StopRun();
                MiddleLayer.RunReset();
            });
        }

        private void btnStepRemove_Click(object sender, EventArgs e)
        {
            SysPara.hsRemove.Ready = true;
            SysPara.Step[(int)Step.Remove] = true;

            if (CheckSafetyPositionXY(tpRemove.GetValue(AxisName.X), tpRemove.GetValue(AxisName.Y)) == false
                || CheckSafetyPositionZ(tpRemove.GetValue(AxisName.Z)) == false)
                return;

            MiddleLayer.RunReset();
            MiddleLayer.MainF.btnStart.PerformClick();

            Task.Run(() =>
            {
                while (SysPara.Step[(int)Step.Remove]) ;

                MiddleLayer.StopRun();
                MiddleLayer.RunReset();
            });
        }

        private void btnStepPick_Click(object sender, EventArgs e)
        {
            SysPara.hsPick.Ready = true;
            SysPara.Step[(int)Step.Pick] = true;

            if (CheckSafetyPositionXY(tpPickup.GetValue(AxisName.X), tpPickup.GetValue(AxisName.Y)) == false
                || CheckSafetyPositionZ(tpPickup.GetValue(AxisName.Z)) == false)
                return;

            MiddleLayer.RunReset();
            MiddleLayer.MainF.btnStart.PerformClick();

            Task.Run(() =>
            {
                while (SysPara.Step[(int)Step.Pick]) ;

                MiddleLayer.StopRun();
                MiddleLayer.RunReset();
            });
        }

        private void btnStepTorque_Click(object sender, EventArgs e)
        {
            SysPara.Step[(int)Step.Torque] = true;

            if (CheckSafetyPositionXY(tpTestTorque.GetValue(AxisName.X), tpTestTorque.GetValue(AxisName.Y)) == false
                || CheckSafetyPositionZ(tpTestTorque.GetValue(AxisName.Z)) == false)
                return;

            MiddleLayer.RunReset();
            MiddleLayer.MainF.btnStart.PerformClick();

            Task.Run(() =>
            {
                while (SysPara.Step[(int)Step.Torque]) ;

                MiddleLayer.StopRun();
                MiddleLayer.RunReset();
            });
        }

        private void groupBox23_Enter(object sender, EventArgs e)
        {

        }


        #region Rutinas para Cambio de Imagen en Picture Box
        private void Btn_TestChangeImage_Click(object sender, EventArgs e)
        {
            string Path = Environment.CurrentDirectory + "\\Imagenes\\" + "Automation CUU Logo.png";
            //string Path = Environment.CurrentDirectory + "\\Imagenes\\" + "Goku.gif";
            CambiarImagenPictureBox(Path);
        }

        public void CambiarImagenPictureBox(string Path)
        {
            if (pictureBoxProceso.InvokeRequired)
                pictureBoxProceso.BeginInvoke((MethodInvoker)(() => CambiarImagenPictureBox(Path)));
            else
            {
                try
                {
                    if (Path.Contains("Gif") || Path.Contains("gif"))//Gif
                    {
                        pictureBoxProceso.Image = Image.FromFile(Path);
                        //pictureBoxProceso.SizeMode = PictureBoxSizeMode.StretchImage;
                        pictureBoxProceso.SizeMode = PictureBoxSizeMode.CenterImage;
                        pictureBoxProceso.Enabled = true;
                    }
                    else//Png
                    {
                        pictureBoxProceso.Image = Image.FromFile(Path);
                        //pictureBoxProceso.SizeMode = PictureBoxSizeMode.StretchImage;
                        pictureBoxProceso.SizeMode = PictureBoxSizeMode.CenterImage;
                        pictureBoxProceso.Enabled = false;
                    }

                }
                catch (Exception ex)
                {
                    pictureBoxProceso.Image = Image.FromFile(Environment.CurrentDirectory + "\\Imagenes\\Error Cargado de Imagen.png");
                    pictureBoxProceso.SizeMode = PictureBoxSizeMode.CenterImage;
                    pictureBoxProceso.Enabled = false;
                }

            }
        }
        #endregion
        private void txtCode1_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return) //Entra hasta que recibe el retorno
            {
                if (txtCode1.ReadOnly == false)
                {
                    //Si esta MES Habilitado consukta CheckPoint
                    if (SysPara.IsMES && !SysPara.IsDryRun)
                    {
                        ////Confirmar MES si OK salta al otro TXT
                        if (SerialCheckProcess(txtCode1.Text) == true && checkLinkBotHousing(txtCode1.Text) == true)
                        {
                            //txtCode2.Select(); //Selecciona txt1 de pallet1
                            //txtCode2.Focus(); //Selecciona txt1 de pallet1
                            //txtCode2.ReadOnly = false; //Permite escanear 
                            txtCode1.ReadOnly = true; //Ya no permite escanear
                            sSerializer_SerialNumber = txtCode1.Text.ToUpper();
                        }
                        else
                        {
                            txtCode1.ReadOnly = false;
                            txtCode1.Text = "";
                            txtCode1.Focus();
                            return;
                        }
                    }
                    else
                    {
                        //txtCode2.Select(); //Selecciona txt1 de pallet1
                        //txtCode2.Focus(); //Selecciona txt1 de pallet1
                        //txtCode2.ReadOnly = false; //Permite escanear 
                        txtCode1.ReadOnly = true; //Ya no permite escanear
                        sSerializer_SerialNumber = txtCode1.Text.ToUpper();
                    }

                }
            }
        }

        public bool checkLinkBotHousing(string SerialNumbre)
        {
            
            DataTable miDataTable = AutoCUU.fnGetUniqueNonUniqueLink(SerialNumbre, 356, "9521-516", true);

            if (miDataTable != null)
            {
                return true;
            }
            else
            {
                MessageBox.Show("No cuenta con ligado de bot housing");
                return false;

            }
        }

        public bool SerialCheckProcess(string SerialNumber) //Valida que traiga todos los procesos para poder entrar al Horno
        {
            string checkPointName = "CHKLED";
            int CustomerID = 356;
            string sCheckPointResult = "";

            try
            {

               sCheckPointResult = AutoCUU.fnCheckPoint(SerialNumber, checkPointName, CustomerID);

                if (sCheckPointResult != "OK")
                {

                    MessageBox.Show("Error de Check Point: s\n \n" + sCheckPointResult.Split('>')[1],
                        "ACURA CAMERA JD", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    return false;

                }

                return true;
            }
            catch
            {
                
                MessageBox.Show("Error de Check Point: \n \n" + sCheckPointResult,
                   "ACURA CAMERA JD", MessageBoxButtons.OK, MessageBoxIcon.Error);

                return false;
            }




        }



        private void btnStepWork_Click(object sender, EventArgs e)
        {
            if (cbbStepWork.DataSource == null) return;

            GetScrewPos(
                int.Parse(cbbStepProcess.Text),
                int.Parse(cbbStepWork.Text),
                out screwPos
            );

            int cavity = int.Parse(cbbStepWork.Text);
            if (!VisionForm.Alignment[cavity].Safety)
            {
                MasterCore.Alarm.Show("6001", "Please inspect this position again");
                return;
            }

            if (VisionForm.Alignment.ContainsKey(cavity) && SysPara.IsAlignment)
            {
                screwPos[(int)WorkPos.X] += VisionForm.Alignment[cavity].X;
                screwPos[(int)WorkPos.Y] += VisionForm.Alignment[cavity].Y;
            }

            if (CheckSafetyPositionXY(screwPos[(int)WorkPos.X], screwPos[(int)WorkPos.Y]) == false
                || CheckSafetyPositionZ(screwPos[(int)WorkPos.Z_Touch]) == false
                || CheckSafetyPositionZ(screwPos[(int)WorkPos.Z_Tighten]) == false)
                return;

            SysPara.hsWork.Ready = true;
            SysPara.Step[(int)Step.Work] = true;
            SysPara.CurrentProcess = int.Parse(cbbStepProcess.Text);
            currentWork = int.Parse(cbbStepWork.Text);

            MiddleLayer.RunReset();
            MiddleLayer.MainF.btnStart.PerformClick();

            Task.Run(() =>
            {
                while (SysPara.Step[(int)Step.Work]) ;

                MiddleLayer.StopRun();
                MiddleLayer.RunReset();

                if (VisionForm.Alignment.ContainsKey(cavity))
                {
                    var alignment = VisionForm.Alignment[cavity];
                    alignment.Safety = false;
                    VisionForm.Alignment[cavity] = alignment;
                }
            });
        }

        private void btnTestPickup_Click(object sender, EventArgs e)
        {
            isTestPickup = true;
            lblNum.Text = "0";

            MiddleLayer.RunReset();
            MiddleLayer.MainF.btnStart.PerformClick();

            Task.Run(() =>
            {
                while (isTestPickup) ;

                MiddleLayer.StopRun();
                MiddleLayer.RunReset();
            });
        }
        #endregion

        private void btnReset_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("Reset counter and uph data?",
                "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                mcCtrl.Reset(MachineMatrix._Index.Main);
                // clear list view but remain header
                lvScrewData.Items.Clear();
                txtPassScrew.Text = "0";
                txtFailScrew.Text = "0";
                SysPara.WorkOK_Count = 0;
                SysPara.WorkNG_Count = 0;
            }
        }

        private void btnResetCount_Click(object sender, EventArgs e)
        {
            SysPara.TighteningCount = 0;
            WriteTighteningCount(SysPara.TighteningCount);
        }

        private void txtFilePath_TextChanged(object sender, EventArgs e)
        {
            btnFileRead.PerformClick();
        }

        private void rdbtnAll_CheckedChanged(object sender, EventArgs e)
        {
            MiddleLayer.StopRun();
            MiddleLayer.RunReset();
            SysPara.SystemMode = RunMode.IDLE;
        }

        private void rdbtnOne_CheckedChanged(object sender, EventArgs e)
        {
            MiddleLayer.StopRun();
            MiddleLayer.RunReset();
            SysPara.SystemMode = RunMode.IDLE;
        }

        private void rdbtnStep_CheckedChanged(object sender, EventArgs e)
        {
            MiddleLayer.StopRun();
            MiddleLayer.RunReset();
            SysPara.SystemMode = RunMode.IDLE;
        }

        private void uiRefresh_Tick(object sender, EventArgs e)
        {
            #region Current Position
            lblAxisX.Text = txtX.Text = txtX1.Text = txtX2.Text = mtrX.GetEncoderPosition().ToString("F3");
            lblAxisY.Text = txtY.Text = txtY1.Text = txtY2.Text = mtrY.GetEncoderPosition().ToString("F3");
            lblHead.Text = txtHead.Text = txtHead1.Text = txtHead2.Text = mtrHead.GetEncoderPosition().ToString("F3");
            txtFlip1.Text = txtFlip.Text = txtFlip2.Text = lblFlip1.Text = mtrFlip.GetEncoderPosition().ToString("F3");
            txtRotY1.Text = txtRot.Text = txtRotY2.Text = lblRot1.Text = mtrRot.GetEncoderPosition().ToString("F3");
            #endregion

            #region X Status
            lblX.Text = mtrX.GetEncoderPosition().ToString("F2");
            if (mtrX.GetAxisIOStatus().ALM)
            {
                pbXStatus.Image = Properties.Resources.Error64x64;
            }
            else if (!mtrX.GetAxisIOStatus().SVON)
            {
                pbXStatus.Image = Properties.Resources.Error64x64;
            }
            else if (mtrX.GetAxisIOStatus().SVON)
            {
                pbXStatus.Image = Properties.Resources.Tick64x64;
            }
            #endregion

            #region Y Status
            lblY.Text = mtrY.GetEncoderPosition().ToString("F2");
            if (mtrY.GetAxisIOStatus().ALM)
            {
                pbYStatus.Image = Properties.Resources.Error64x64;
            }
            else if (!mtrY.GetAxisIOStatus().SVON)
            {
                pbYStatus.Image = Properties.Resources.Error64x64;
            }
            else if (mtrY.GetAxisIOStatus().SVON)
            {
                pbYStatus.Image = Properties.Resources.Tick64x64;
            }
            #endregion

            #region Z Status
            lblZ.Text = mtrHead.GetEncoderPosition().ToString("F2");
            if (mtrHead.GetAxisIOStatus().ALM)
            {
                pbZStatus.Image = Properties.Resources.Error64x64;
            }
            else if (!mtrHead.GetAxisIOStatus().SVON)
            {
                pbZStatus.Image = Properties.Resources.Error64x64;
            }
            else if (mtrHead.GetAxisIOStatus().SVON)
            {
                pbZStatus.Image = Properties.Resources.Tick64x64;
            }
            #endregion

            #region RotX Status
            lblFlip.Text = mtrFlip.GetEncoderPosition().ToString("F2");
            if (mtrFlip.GetAxisIOStatus().ALM)
            {
                pbRotXStatus.Image = Properties.Resources.Error64x64;
            }
            else if (!mtrFlip.GetAxisIOStatus().SVON)
            {
                pbRotXStatus.Image = Properties.Resources.Error64x64;
            }
            else if (mtrFlip.GetAxisIOStatus().SVON)
            {
                pbRotXStatus.Image = Properties.Resources.Tick64x64;
            }
            #endregion

            #region RotY Status
            lblRotY.Text = mtrRot.GetEncoderPosition().ToString("F2");
            if (mtrRot.GetAxisIOStatus().ALM)
            {
                pbRotYStatus.Image = Properties.Resources.Error64x64;
            }
            else if (!mtrRot.GetAxisIOStatus().SVON)
            {
                pbRotYStatus.Image = Properties.Resources.Error64x64;
            }
            else if (mtrRot.GetAxisIOStatus().SVON)
            {
                pbRotYStatus.Image = Properties.Resources.Tick64x64;
            }
            #endregion

            #region Screw Status
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
            #endregion

            #region Tighten Count
            txtTightenCount?.Invoke(new MethodInvoker(delegate
            {
                txtTightenCount.Text = SysPara.TighteningCount.ToString();
            }));

            if (status != oTight.IsOn())
            {
                status = oTight.IsOn();
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
                MiddleLayer.MainF.btnPause.PerformClick();
                message.TopMost = true;
                message.ShowDialog();
            }
            #endregion

            #region Sample Count
            txtPassScrew?.Invoke(new MethodInvoker(delegate
            {
                txtPassScrew.Text = SysPara.WorkOK_Count.ToString();
            }));
            txtFailScrew?.Invoke(new MethodInvoker(delegate
            {
                txtFailScrew.Text = SysPara.WorkNG_Count.ToString();
            }));
            #endregion

            #region Interlock Button
            grpbxStep.Enabled = grpbxTestPick.Enabled = SysPara.bSafetyReady && !SysPara.IsMaintenanceMode && SysPara.SystemInitialOk
                && !SysPara.Step.Any(step => step == true) && rdbtnStep.Checked;
            #endregion
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            bool currVacState = MiddleLayer.SystemF.IB_BtnVac.IsOn();
            if (currVacState && !prevVacState)
            {
                if (oStageVac.IsOn())
                    oStageVac.Off();
                else
                    oStageVac.On();
            }
            prevVacState = currVacState;

            if (iStageSsVac.IsOff())
            {
                MiddleLayer.SystemF.OB_VacuumLight.Off();
            }
            else
                MiddleLayer.SystemF.OB_VacuumLight.On();
        }
        #endregion
    }
}