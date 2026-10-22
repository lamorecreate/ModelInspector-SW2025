using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using SolidWorks.Interop.swconst;

namespace SwAiGuide
{
    internal static class Log_
    {
        // 写到 %LOCALAPPDATA%\SwAiGuide\addin.log，任何机器都可用
        public static string Path = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "SwAiGuide", "addin.log");
        public static void W(string m)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.AppendAllText(Path, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + m + System.Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    public class Finding
    {
        public int Level;       // 2=错误 1=警告 0=信息
        public string Item;
        public string Detail;

        public Finding(int level, string item, string detail)
        {
            Level = level; Item = item; Detail = detail;
        }
        public string Line
        {
            get { return (Level == 2 ? "✖ " : Level == 1 ? "⚠ " : "· ") + Item + "：" + Detail; }
        }
    }

    public class FileResult
    {
        public string Path;
        public string Name;
        public string TypeName;
        public int Errors;
        public int Warnings;
        public bool Failed;
        public string FailReason = "";
        public List<Finding> Findings = new List<Finding>();
        public List<string> Highlights = new List<string>();

        public string Summary
        {
            get
            {
                if (Failed) return "打开失败：" + FailReason;
                if (Highlights.Count == 0) return "无异常";
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < Highlights.Count && i < 3; i++)
                {
                    if (i > 0) sb.Append("；");
                    sb.Append(Highlights[i]);
                }
                if (Highlights.Count > 3) sb.Append(" 等 " + Highlights.Count + " 项");
                return sb.ToString();
            }
        }
    }

    // ---------------- 加载项入口 ----------------
    [ComVisible(true)]
    [Guid("8C2F1A70-4B4D-4E52-9C1E-3A6E1B2D4F01")]
    [ProgId("SwAiGuide.Addin")]
    public class SwAiGuideAddin : ISwAddin
    {
        private ISldWorks swApp;
        private int cookie;
        private InspectorForm form;
        private Timer showTimer;

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            try
            {
                Log_.W("=== ConnectToSW begin cookie=" + Cookie);
                swApp = (ISldWorks)ThisSW;
                cookie = Cookie;
                Log_.W("revision=" + swApp.RevisionNumber());

                showTimer = new Timer();
                showTimer.Interval = 2000;
                showTimer.Tick += delegate
                {
                    showTimer.Stop();
                    try
                    {
                        form = new InspectorForm(swApp);
                        form.Show();
                        Log_.W("InspectorForm shown OK");
                    }
                    catch (Exception ex) { Log_.W("InspectorForm show EX: " + ex); }
                };
                showTimer.Start();
                return true;
            }
            catch (Exception ex)
            {
                Log_.W("ConnectToSW FAILED: " + ex);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            try
            {
                Log_.W("DisconnectFromSW");
                if (showTimer != null) { showTimer.Stop(); showTimer.Dispose(); showTimer = null; }
                if (form != null) { form.Close(); form = null; }
                return true;
            }
            catch (Exception ex) { Log_.W("Disconnect EX: " + ex); return true; }
        }
    }

    // ---------------- 主窗口 ----------------
    public class InspectorForm : Form
    {
        private ISldWorks swApp;
        private ListBox list;
        private Label docInfo;
        private Label status;
        private ProgressBar bar;
        private Button btnStop, btnCsv;

        private List<Finding> findings = new List<Finding>();
        private List<int> rowLevels = new List<int>();
        private List<FileResult> batchResults = new List<FileResult>();
        private bool stopRequested = false;
        private bool scanning = false;

        public InspectorForm(ISldWorks app)
        {
            swApp = app;
            BuildUi();
            AddRow(0, "就绪 —— 点「扫描当前文档」检查手上这个，或点「批量扫描文件夹」检查一整个目录");
            Log_.W("InspectorForm ctor done");
        }

        private void BuildUi()
        {
            Text = "模型体检";
            Width = 780;
            Height = 620;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            BackColor = Color.FromArgb(250, 250, 252);

            docInfo = new Label();
            docInfo.SetBounds(12, 10, 740, 22);
            docInfo.Text = "就绪";
            docInfo.Font = new Font("Microsoft YaHei", 10f, FontStyle.Bold);
            docInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(docInfo);

            Button btnOne = MakeButton("扫描当前文档", 12, 118, delegate { ScanCurrent(); });
            Button btnBatch = MakeButton("批量扫描文件夹", 138, 146, delegate { ScanFolder(); });
            btnStop = MakeButton("停止", 292, 76, delegate { stopRequested = true; status.Text = "正在停止…"; });
            btnStop.Enabled = false;
            btnCsv = MakeButton("导出 CSV", 376, 104, delegate { ExportCsv(); });
            btnCsv.Enabled = false;
            Button btnCopy = MakeButton("复制报告", 488, 104, delegate { CopyReport(); });

            list = new ListBox();
            list.SetBounds(12, 80, 740, 418);
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = 26;
            list.Font = new Font("Microsoft YaHei", 9.5f);
            list.DrawItem += new DrawItemEventHandler(DrawRow);
            list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(list);

            bar = new ProgressBar();
            bar.SetBounds(12, 504, 740, 16);
            bar.Minimum = 0;
            bar.Maximum = 100;
            bar.Value = 0;
            bar.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(bar);

            status = new Label();
            status.SetBounds(12, 526, 740, 44);
            status.Text = "扫描当前文档，或选一个文件夹批量体检。";
            status.ForeColor = Color.DimGray;
            status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(status);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private Timer zTimer;
        private bool isTopMost = false;
        private int myPid = 0;

        private const int GWLP_HWNDPARENT = -8;

        private void UpdateTopMost()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                int pid = 0;
                GetWindowThreadProcessId(fg, out pid);
                bool swInFront = (pid == myPid);   // 本窗口也属于 SW 进程，切到 SW 就算在前台

                if (swInFront && !isTopMost)
                {
                    SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    isTopMost = true;
                }
                else if (!swInFront && isTopMost)
                {
                    // 取消置顶并压到最底层，确保不会浮在别的程序上面
                    SetWindowPos(this.Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    SetWindowPos(this.Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    isTopMost = false;
                }
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                myPid = System.Diagnostics.Process.GetCurrentProcess().Id;

                // 注意：不再设置属主窗口(GWLP_HWNDPARENT)。实测它既压不住 SW 的 Z 序，
                // 又疑似拖慢 OpenDoc6（9 个文件 23 秒 → 175 秒）。已移除。

                UpdateTopMost();

                zTimer = new Timer();
                zTimer.Interval = 500;
                zTimer.Tick += delegate { UpdateTopMost(); };
                zTimer.Start();
                Log_.W("zorder timer started, myPid=" + myPid);
            }
            catch (Exception ex)
            {
                Log_.W("owner attach EX " + ex.Message);
                try { this.TopMost = true; } catch { }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { if (zTimer != null) { zTimer.Stop(); zTimer.Dispose(); zTimer = null; } } catch { }
            base.OnFormClosing(e);
        }

        private Button MakeButton(string text, int x, int w, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, 38, w, 32);
            b.Font = new Font("Microsoft YaHei", 9f);
            b.Click += onClick;
            Controls.Add(b);
            return b;
        }

        private void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            int lv = (e.Index < rowLevels.Count) ? rowLevels[e.Index] : 0;
            Color bg = lv == 2 ? Color.FromArgb(255, 240, 240) : lv == 1 ? Color.FromArgb(255, 250, 235) : Color.White;
            Color fg = lv == 2 ? Color.FromArgb(190, 30, 30) : lv == 1 ? Color.FromArgb(170, 100, 0) : Color.FromArgb(60, 60, 60);

            using (Brush b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
            using (Pen p = new Pen(Color.FromArgb(235, 235, 240))) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            using (Brush tb = new SolidBrush(fg))
                e.Graphics.DrawString(list.Items[e.Index].ToString(), list.Font, tb, new RectangleF(e.Bounds.Left + 8, e.Bounds.Top + 4, e.Bounds.Width - 16, e.Bounds.Height));
        }

        private void ResetView()
        {
            list.Items.Clear();
            rowLevels.Clear();
            findings.Clear();
            batchResults.Clear();
        }

        private void AddRow(int level, string text)
        {
            list.Items.Add(text);
            rowLevels.Add(level);
        }

        private void CopyReport()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("SolidWorks 模型体检报告  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                sb.AppendLine(docInfo.Text);
                for (int i = 0; i < list.Items.Count; i++) sb.AppendLine(list.Items[i].ToString());
                Clipboard.SetText(sb.ToString());
                status.Text = "已复制 " + list.Items.Count + " 行到剪贴板。";
            }
            catch (Exception ex) { status.Text = "复制失败：" + ex.Message; }
        }

        // ================= 单文档 =================
        private void ScanCurrent()
        {
            if (scanning) return;
            ResetView();
            try
            {
                IModelDoc2 doc = swApp.ActiveDoc as IModelDoc2;
                if (doc == null)
                {
                    docInfo.Text = "没有打开的文档";
                    AddRow(1, "⚠ 无文档：请先打开零件或装配体");
                    return;
                }
                string title = SafeStr(delegate { return doc.GetTitle(); });
                string typeName = TypeNameOf(doc);
                docInfo.Text = title + "  ·  " + typeName;

                List<Finding> fs = ScanDoc(doc, title, false);
                findings = fs;
                int err = 0, warn = 0;
                for (int i = 0; i < fs.Count; i++)
                {
                    AddRow(fs[i].Level, fs[i].Line);
                    if (fs[i].Level == 2) err++; else if (fs[i].Level == 1) warn++;
                    Log_.W("FIND " + fs[i].Level + " | " + fs[i].Item + " | " + fs[i].Detail);
                }
                status.Text = "扫描完成：错误 " + err + " · 警告 " + warn + " · 共 " + fs.Count + " 条";
                btnCsv.Enabled = false;
            }
            catch (Exception ex)
            {
                Log_.W("ScanCurrent EX " + ex);
                AddRow(2, "✖ 扫描异常：" + ex.Message);
            }
        }

        // ================= 批量 =================
        private void ScanFolder()
        {
            if (scanning) return;
            string folder = null;
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择要体检的文件夹（会递归扫描零件与装配体）";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                folder = dlg.SelectedPath;
            }

            List<string> files = new List<string>();
            try
            {
                string[] exts = new string[] { "*.sldprt", "*.sldasm" };
                for (int e2 = 0; e2 < exts.Length; e2++)
                    files.AddRange(Directory.GetFiles(folder, exts[e2], SearchOption.AllDirectories));
                files.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                MessageBox.Show("列目录失败：" + ex.Message);
                return;
            }

            if (files.Count == 0)
            {
                MessageBox.Show("这个文件夹里没有 .sldprt / .sldasm 文件。");
                return;
            }

            ResetView();
            scanning = true;
            stopRequested = false;
            btnStop.Enabled = true;
            btnCsv.Enabled = false;
            docInfo.Text = "批量扫描：" + folder;

            HashSet<string> preOpened = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                object[] docs = (object[])swApp.GetDocuments();
                if (docs != null)
                    for (int i = 0; i < docs.Length; i++)
                    {
                        IModelDoc2 d = docs[i] as IModelDoc2;
                        if (d == null) continue;
                        string p = SafeStr(delegate { return d.GetPathName(); });
                        if (!string.IsNullOrEmpty(p)) preOpened.Add(p);
                    }
            }
            catch (Exception ex) { Log_.W("GetDocuments EX " + ex.Message); }

            Log_.W("=== batch scan start folder=" + folder + " files=" + files.Count);

            // 记住用户当前文档，扫完每个文件后把它切回前台
            string prevTitle = "";
            try
            {
                IModelDoc2 ad = swApp.ActiveDoc as IModelDoc2;
                if (ad != null) prevTitle = SafeStr(delegate { return ad.GetTitle(); });
            }
            catch { }

            bar.Minimum = 0;
            bar.Maximum = files.Count;
            bar.Value = 0;

            DateTime t0 = DateTime.Now;
            for (int i = 0; i < files.Count; i++)
            {
                if (stopRequested) break;
                string f = files[i];
                status.Text = "正在扫描 " + (i + 1) + "/" + files.Count + "：" + System.IO.Path.GetFileName(f);
                Application.DoEvents();

                DateTime tf = DateTime.Now;
                FileResult r = ScanFile(f, preOpened);
                int ms = (int)(DateTime.Now - tf).TotalMilliseconds;
                Log_.W("file " + r.Name + " ms=" + ms + " ok=" + (!r.Failed));
                batchResults.Add(r);

                int lv = r.Failed ? 2 : (r.Errors > 0 ? 2 : (r.Warnings > 0 ? 1 : 0));
                string mark = (r.Failed || r.Errors > 0) ? "✖" : (r.Warnings > 0 ? "⚠" : "✓");
                AddRow(lv, mark + " " + r.Name + "  ｜ " + r.TypeName + " ｜ " + r.Summary);
                if (list.Items.Count > 0) list.TopIndex = list.Items.Count - 1;

                bar.Value = i + 1;

                // 把用户原来的文档切回前台，减少窗口跳动
                if (prevTitle.Length > 0)
                {
                    try { int ae = 0; swApp.ActivateDoc3(prevTitle, false, 0, ref ae); }
                    catch { }
                }
                Application.DoEvents();
            }
            bar.Value = files.Count;

            scanning = false;
            btnStop.Enabled = false;
            btnCsv.Enabled = batchResults.Count > 0;

            int totalErr = 0, totalWarn = 0, failed = 0;
            for (int i = 0; i < batchResults.Count; i++)
            {
                totalErr += batchResults[i].Errors;
                totalWarn += batchResults[i].Warnings;
                if (batchResults[i].Failed) failed++;
            }
            TimeSpan ts = DateTime.Now - t0;
            status.Text = "完成：" + batchResults.Count + " 个文件 · 错误 " + totalErr + " · 警告 " + totalWarn
                        + " · 打开失败 " + failed + " · 用时 " + (int)ts.TotalSeconds + " 秒";
            Log_.W("=== batch scan done files=" + batchResults.Count + " err=" + totalErr + " warn=" + totalWarn + " failed=" + failed);
        }

        private FileResult ScanFile(string path, HashSet<string> preOpened)
        {
            FileResult r = new FileResult();
            r.Path = path;
            r.Name = System.IO.Path.GetFileName(path);
            r.TypeName = path.ToLower().EndsWith(".sldasm") ? "装配体" : "零件";

            bool wasOpen = preOpened.Contains(path);
            IModelDoc2 doc = null;

            try
            {
                if (wasOpen)
                {
                    try { doc = swApp.GetOpenDocumentByName(path) as IModelDoc2; } catch { }
                }

                if (doc == null)
                {
                    int docType = path.ToLower().EndsWith(".sldasm")
                        ? (int)swDocumentTypes_e.swDocASSEMBLY
                        : (int)swDocumentTypes_e.swDocPART;

                    // Silent(1) | ReadOnly(2) | LoadLightweight(128)
                    int errs = 0, warns = 0;
                    doc = swApp.OpenDoc6(path, docType, 1 | 2 | 128, "", ref errs, ref warns) as IModelDoc2;

                    // 只读方式失败时，去掉 ReadOnly 再试一次
                    if (doc == null)
                    {
                        Log_.W("open retry(no readonly) " + path + " err=" + errs);
                        int e2 = 0, w2 = 0;
                        doc = swApp.OpenDoc6(path, docType, 1 | 128, "", ref e2, ref w2) as IModelDoc2;
                        if (doc != null) { errs = 0; }
                        else { errs = e2; warns = w2; }
                    }

                    if (doc == null)
                    {
                        r.Failed = true;
                        r.FailReason = LoadErrText(errs);
                        bool exists = false;
                        try { exists = File.Exists(path); } catch { }
                        Log_.W("open fail " + r.Name + " err=" + errs + " exists=" + exists + " pathLen=" + path.Length + " path=" + path);
                        return r;
                    }
                }

                List<Finding> fs = ScanDoc(doc, r.Name, true);
                r.Findings = fs;
                for (int i = 0; i < fs.Count; i++)
                {
                    if (fs[i].Level == 2) { r.Errors++; r.Highlights.Add(fs[i].Item); }
                    else if (fs[i].Level == 1) { r.Warnings++; r.Highlights.Add(fs[i].Item); }
                }
            }
            catch (Exception ex)
            {
                r.Failed = true;
                r.FailReason = ex.Message;
                Log_.W("scan file EX " + r.Name + " " + ex);
            }
            finally
            {
                if (doc != null && !wasOpen)
                {
                    try
                    {
                        string t = SafeStr(delegate { return doc.GetTitle(); });
                        if (!string.IsNullOrEmpty(t))
                        {
                            // QuitDoc 丢弃改动直接关闭，不会弹"是否保存"
                            try { swApp.QuitDoc(t); }
                            catch { swApp.CloseDoc(t); }
                        }
                    }
                    catch (Exception ex) { Log_.W("QuitDoc EX " + r.Name + " " + ex.Message); }
                }
            }
            return r;
        }

        // ================= 通用检查 =================
        private List<Finding> ScanDoc(IModelDoc2 doc, string label, bool concise)
        {
            List<Finding> fs = new List<Finding>();
            int dt = 0;
            try { dt = doc.GetType(); } catch { }

            try
            {
                string p = SafeStr(delegate { return doc.GetPathName(); });
                if (string.IsNullOrEmpty(p)) fs.Add(new Finding(1, "未保存", "文档尚未保存到磁盘"));
            }
            catch { }

            try { if (doc.GetSaveFlag()) fs.Add(new Finding(1, "未存盘修改", "有改动未保存")); } catch { }

            int total = 0, bad = 0;
            StringBuilder badList = new StringBuilder();
            try
            {
                object o = doc.FirstFeature();
                int guard = 0;
                while (o != null && guard < 8000)
                {
                    guard++;
                    Feature feat = o as Feature;
                    if (feat == null) break;
                    total++;
                    int code = 0;
                    try { bool isWarn = false; code = feat.GetErrorCode2(out isWarn); } catch { }
                    if (code != 0)
                    {
                        bad++;
                        if (bad <= 8)
                        {
                            string nm = SafeStr(delegate { return feat.Name; });
                            badList.Append(nm + "(" + code + ") ");
                        }
                    }
                    o = feat.GetNextFeature();
                }
            }
            catch (Exception ex) { fs.Add(new Finding(1, "特征遍历中断", ex.Message)); }

            if (bad > 0) fs.Add(new Finding(2, "特征重建异常", bad + "/" + total + " 个：" + badList.ToString()));

            if (dt == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                try
                {
                    IAssemblyDoc ad = doc as IAssemblyDoc;
                    object[] comps = (object[])ad.GetComponents(concise); // 批量模式只扫顶层，快一个量级
                    int cnt = comps == null ? 0 : comps.Length;
                    int odd = 0, missing = 0;
                    Dictionary<string, int> byPath = new Dictionary<string, int>();
                    for (int i = 0; i < cnt; i++)
                    {
                        IComponent2 c = comps[i] as IComponent2;
                        if (c == null) continue;
                        int st = 0; try { st = c.GetSuppression2(); } catch { }
                        if (st != 0) odd++;
                        string cp = SafeStr(delegate { return c.GetPathName(); });
                        if (!string.IsNullOrEmpty(cp))
                        {
                            if (!byPath.ContainsKey(cp)) byPath[cp] = 0;
                            byPath[cp]++;
                            try { if (!File.Exists(cp)) missing++; } catch { }
                        }
                    }
                    if (missing > 0) fs.Add(new Finding(2, "断链文件", missing + " 个零部件的源文件找不到"));
                    if (odd > 0) fs.Add(new Finding(1, "非正常状态", odd + " 个零部件被压缩/轻化"));
                    if (!concise) fs.Add(new Finding(0, "零部件", cnt + " 个（唯一文件 " + byPath.Count + " 种）"));
                }
                catch (Exception ex) { fs.Add(new Finding(1, "装配体检查失败", ex.Message)); }
            }

            if (dt == (int)swDocumentTypes_e.swDocPART)
            {
                try
                {
                    IPartDoc pd = doc as IPartDoc;
                    string db = "";
                    string cfg = "";
                    try
                    {
                        object[] names = (object[])doc.GetConfigurationNames();
                        if (names != null && names.Length > 0) cfg = Convert.ToString(names[0]);
                    }
                    catch { }
                    string mat = "";
                    try { mat = pd.GetMaterialPropertyName2(cfg, out db); } catch { }
                    // 未指定材料不作为问题上报（最多在单文档模式下作为信息显示）
                    if (!string.IsNullOrEmpty(mat) && !concise) fs.Add(new Finding(0, "材料", mat));
                }
                catch (Exception ex) { fs.Add(new Finding(1, "材料检查失败", ex.Message)); }

                try
                {
                    IPartDoc pd2 = doc as IPartDoc;
                    object[] bodies = (object[])pd2.GetBodies2((int)swBodyType_e.swSolidBody, false);
                    int bn = bodies == null ? 0 : bodies.Length;
                    if (bn > 1) fs.Add(new Finding(1, "多实体零件", bn + " 个实体，注意后续特征作用范围"));
                    else if (!concise) fs.Add(new Finding(0, "实体数量", bn + " 个"));
                }
                catch { }
            }

            Log_.W("scan " + label + " features=" + total + " bad=" + bad + " findings=" + fs.Count);
            return fs;
        }

        // ================= 导出 =================
        private void ExportCsv()
        {
            if (batchResults.Count == 0) return;
            string dir = System.IO.Path.GetDirectoryName(batchResults[0].Path);
            string defName = "体检报告_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv";
            string target;
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "CSV 文件|*.csv";
                dlg.FileName = defName;
                dlg.InitialDirectory = dir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                target = dlg.FileName;
            }

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("文件名,类型,错误数,警告数,状态,主要问题,路径");
                for (int i = 0; i < batchResults.Count; i++)
                {
                    FileResult r = batchResults[i];
                    string st = r.Failed ? "打开失败" : (r.Errors > 0 ? "有错误" : (r.Warnings > 0 ? "有警告" : "正常"));
                    sb.Append(Q(r.Name)).Append(',')
                      .Append(Q(r.TypeName)).Append(',')
                      .Append(r.Errors).Append(',')
                      .Append(r.Warnings).Append(',')
                      .Append(Q(st)).Append(',')
                      .Append(Q(r.Summary)).Append(',')
                      .Append(Q(r.Path)).AppendLine();
                }
                File.WriteAllText(target, sb.ToString(), new UTF8Encoding(true)); // 带 BOM，Excel 才认中文
                status.Text = "已导出：" + target;
                Log_.W("csv exported " + target);
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出失败：" + ex.Message);
                Log_.W("csv EX " + ex);
            }
        }

        private static string Q(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        // ================= 工具 =================
        private static string SafeStr(Func<string> f)
        {
            try { return f(); } catch { return ""; }
        }

        private static string LoadErrText(int e)
        {
            switch (e)
            {
                case 1: return "打开失败（通用错误）";
                case 2: return "文件不存在或路径无法访问（可能路径过长/中文过深）";
                case 4: return "文件 ID 不匹配（文件可能被外部改动）";
                case 1024: return "文件类型无效";
                case 8192: return "文件版本过新，当前 SolidWorks 打不开";
                case 65536: return "同名文档已打开，请先关闭它";
                case 2097152: return "文件需要修复";
                case 4194304: return "文件关键数据损坏";
                case 8388608: return "SolidWorks 忙，稍后重试";
                default: return "打开失败（错误码 " + e + "）";
            }
        }

        private static string TypeNameOf(IModelDoc2 doc)
        {
            try
            {
                int dt = doc.GetType();
                if (dt == (int)swDocumentTypes_e.swDocPART) return "零件";
                if (dt == (int)swDocumentTypes_e.swDocASSEMBLY) return "装配体";
                if (dt == (int)swDocumentTypes_e.swDocDRAWING) return "工程图";
                return "类型 " + dt;
            }
            catch { return "未知"; }
        }
    }
}

