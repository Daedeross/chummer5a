/*  This file is part of Chummer5a.
 *
 *  Chummer5a is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Chummer5a is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with Chummer5a.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  You can obtain the full source code for Chummer5a at
 *  https://github.com/chummer5a/chummer5a
 */
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
#if DEBUG
using System.Net;
#endif
using Application = System.Windows.Forms.Application;
using System.Text;
using NLog;

namespace Chummer
{
    public partial class frmOptions : Form
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        // List of custom data directories possible to be added to a character
        private readonly HashSet<CustomDataDirectoryInfo> _setCustomDataDirectoryInfos;
        // List of sourcebook infos, needed to make sure we don't directly modify ones in the options unless we save our options
        private readonly Dictionary<string, SourcebookInfo> _dicSourcebookInfos;
        private bool _blnSkipRefresh;
        private bool _blnDirty;
        private bool _blnLoading = true;
        private string _strSelectedLanguage = GlobalOptions.Language;
        private CultureInfo _objSelectedCultureInfo = GlobalOptions.CultureInfo;
        private ColorMode _eSelectedColorModeSetting = GlobalOptions.ColorModeSetting;

        #region Form Events
        public frmOptions(string strActiveTab = "")
        {
            InitializeComponent();
#if !DEBUG
            // tabPage3 only contains cmdUploadPastebin, which is not used if DEBUG is not enabled
            // Remove this line if cmdUploadPastebin_Click has some functionality if DEBUG is not enabled or if tabPage3 gets some other control that can be used if DEBUG is not enabled
            tabOptions.TabPages.Remove(tabGitHubIssues);
#endif
            this.UpdateLightDarkMode();
            this.TranslateWinForm(_strSelectedLanguage);

            _setCustomDataDirectoryInfos = new HashSet<CustomDataDirectoryInfo>(GlobalOptions.CustomDataDirectoryInfos);
            _dicSourcebookInfos = new Dictionary<string, SourcebookInfo>(GlobalOptions.SourcebookInfos);
            if (!string.IsNullOrEmpty(strActiveTab))
            {
                int intActiveTabIndex = tabOptions.TabPages.IndexOfKey(strActiveTab);
                if (intActiveTabIndex > 0)
                    tabOptions.SelectedTab = tabOptions.TabPages[intActiveTabIndex];
            }
        }

        private void frmOptions_Load(object sender, EventArgs e)
        {
            PopulateDefaultCharacterOptionList();
            PopulateMugshotCompressionOptions();
            SetToolTips();
            PopulateOptions();
            PopulateLanguageList();
            SetDefaultValueForLanguageList();
            PopulateSheetLanguageList();
            SetDefaultValueForSheetLanguageList();
            PopulateXsltList();
            SetDefaultValueForXsltList();
            PopulatePDFParameters();
            _blnLoading = false;
        }
        #endregion

        #region Control Events
        private void cmdOK_Click(object sender, EventArgs e)
        {
            if(_blnDirty)
            {
                string text = LanguageManager.GetString("Message_Options_SaveForms", _strSelectedLanguage);
                string caption = LanguageManager.GetString("MessageTitle_Options_CloseForms", _strSelectedLanguage);

                if(Program.MainForm.ShowMessageBox(this, text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }

            DialogResult = DialogResult.OK;
            SaveRegistrySettings();

            if(_blnDirty)
                Utils.RestartApplication(_strSelectedLanguage, "Message_Options_CloseForms");
        }

        private void cboLanguage_SelectedIndexChanged(object sender, EventArgs e)
        {
            _strSelectedLanguage = cboLanguage.SelectedValue?.ToString() ?? GlobalOptions.DefaultLanguage;
            try
            {
                _objSelectedCultureInfo = CultureInfo.GetCultureInfo(_strSelectedLanguage);
            }
            catch (CultureNotFoundException)
            {
                _objSelectedCultureInfo = GlobalOptions.SystemCultureInfo;
            }

            imgLanguageFlag.Image = FlagImageGetter.GetFlagFromCountryCode(_strSelectedLanguage.Substring(3, 2));

            bool isEnabled = !string.IsNullOrEmpty(_strSelectedLanguage) && _strSelectedLanguage != GlobalOptions.DefaultLanguage;
            cmdVerify.Enabled = isEnabled;
            cmdVerifyData.Enabled = isEnabled;

            if(!_blnLoading)
            {
                using (new CursorWait(this))
                {
                    _blnLoading = true;
                    TranslateForm();
                    _blnLoading = false;
                }
            }

            OptionsChanged(sender, e);
        }

        private void cboSheetLanguage_SelectedIndexChanged(object sender, EventArgs e)
        {
            using (new CursorWait(this))
                PopulateXsltList();
        }

        private void cmdVerify_Click(object sender, EventArgs e)
        {
            using (new CursorWait(this))
                LanguageManager.VerifyStrings(_strSelectedLanguage);
        }

        private void cmdVerifyData_Click(object sender, EventArgs e)
        {
            using (new CursorWait(this))
            {
	            // Build a list of Sourcebooks that will be passed to the Verify method.
	            // This is done since not all of the books are available in every language or the user may only wish to verify the content of certain books.
	            HashSet<string> setBooks = new HashSet<string>();
	            foreach(ListItem objItem in lstGlobalSourcebookInfos.Items)
	            {
	                string strItemValue = objItem.Value?.ToString();
	                setBooks.Add(strItemValue);
	            }

                string strSelectedLanguage = _strSelectedLanguage;
	            XmlManager.Verify(strSelectedLanguage, setBooks);

                string strFilePath = Path.Combine(Utils.GetStartupPath, "lang", "results_" + strSelectedLanguage + ".xml");
                Program.MainForm.ShowMessageBox(this, string.Format(_objSelectedCultureInfo, LanguageManager.GetString("Message_Options_ValidationResults", _strSelectedLanguage), strFilePath),
                    LanguageManager.GetString("MessageTitle_Options_ValidationResults", _strSelectedLanguage), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void cmdPDFAppPath_Click(object sender, EventArgs e)
        {
            // Prompt the user to select a save file to associate with this Contact.
            using (new CursorWait(this))
            {
                using (OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = LanguageManager.GetString("DialogFilter_Exe") + '|' +
                             LanguageManager.GetString("DialogFilter_All")
                })
                {
                    if (!string.IsNullOrEmpty(txtPDFAppPath.Text) && File.Exists(txtPDFAppPath.Text))
                    {
                        openFileDialog.InitialDirectory = Path.GetDirectoryName(txtPDFAppPath.Text);
                        openFileDialog.FileName = Path.GetFileName(txtPDFAppPath.Text);
                    }

                    if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                        txtPDFAppPath.Text = openFileDialog.FileName;
                }
            }
        }

        private void cmdPDFLocation_Click(object sender, EventArgs e)
        {
            // Prompt the user to select a save file to associate with this Contact.
            using (new CursorWait(this))
            {
                using (OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = LanguageManager.GetString("DialogFilter_Pdf") + '|' +
                             LanguageManager.GetString("DialogFilter_All")
                })
                {
                    if (!string.IsNullOrEmpty(txtPDFLocation.Text) && File.Exists(txtPDFLocation.Text))
                    {
                        openFileDialog.InitialDirectory = Path.GetDirectoryName(txtPDFLocation.Text);
                        openFileDialog.FileName = Path.GetFileName(txtPDFLocation.Text);
                    }

                    if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                    {
                        UpdateSourcebookInfoPath(openFileDialog.FileName);
                        txtPDFLocation.Text = openFileDialog.FileName;
                    }
                }
            }
        }

        private void lstGlobalSourcebookInfos_SelectedIndexChanged(object sender, EventArgs e)
        {
            string strSelectedCode = lstGlobalSourcebookInfos.SelectedValue?.ToString() ?? string.Empty;

            // Find the selected item in the Sourcebook List.
            SourcebookInfo objSource = _dicSourcebookInfos.ContainsKey(strSelectedCode) ? _dicSourcebookInfos[strSelectedCode] : null;

            if (objSource != null)
            {
                grpSelectedSourcebook.Enabled = true;
                txtPDFLocation.Text = objSource.Path;
                nudPDFOffset.Value = objSource.Offset;
            }
            else
            {
                grpSelectedSourcebook.Enabled = false;
            }
        }

        private void nudPDFOffset_ValueChanged(object sender, EventArgs e)
        {
            if(_blnSkipRefresh || _blnLoading)
                return;

            int intOffset = decimal.ToInt32(nudPDFOffset.Value);
            string strTag = lstGlobalSourcebookInfos.SelectedValue?.ToString() ?? string.Empty;
            SourcebookInfo objFoundSource = _dicSourcebookInfos.ContainsKey(strTag) ? _dicSourcebookInfos[strTag] : null;

            if (objFoundSource != null)
            {
                objFoundSource.Offset = intOffset;
            }
            else
            {
                // If the Sourcebook was not found in the options, add it.
                _dicSourcebookInfos.Add(strTag, new SourcebookInfo
                {
                    Code = strTag,
                    Offset = intOffset
                });
            }
        }

        private void cmdPDFTest_Click(object sender, EventArgs e)
        {
            if(string.IsNullOrEmpty(txtPDFLocation.Text))
                return;
            using (new CursorWait(this))
                CommonFunctions.OpenPdf(lstGlobalSourcebookInfos.SelectedValue + " 5", null, cboPDFParameters.SelectedValue?.ToString() ?? string.Empty, txtPDFAppPath.Text);
        }
        #endregion

        #region Methods
        private void TranslateForm()
        {
            this.TranslateWinForm(_strSelectedLanguage);
            PopulateDefaultCharacterOptionList();
            PopulateMugshotCompressionOptions();
            SetToolTips();

            string strSheetLanguage = cboSheetLanguage.SelectedValue?.ToString();
            if(strSheetLanguage != _strSelectedLanguage
               && cboSheetLanguage.Items.Cast<ListItem>().Any(x => x.Value.ToString() == _strSelectedLanguage))
            {
                cboSheetLanguage.SelectedValue = _strSelectedLanguage;
            }

            PopulatePDFParameters();
            PopulateCustomDataDirectoryListBox();
            PopulateApplicationInsightsOptions();
            PopulateColorModes();
        }

        private void RefreshGlobalSourcebookInfosListView()
        {
            // Load the Sourcebook information.
            XmlDocument objXmlDocument = XmlManager.Load("books.xml", null, _strSelectedLanguage);

            // Put the Sourcebooks into a List so they can first be sorted.
            List<ListItem> lstSourcebookInfos = new List<ListItem>();
            using(XmlNodeList objXmlBookList = objXmlDocument.SelectNodes("/chummer/books/book"))
            {
                if(objXmlBookList != null)
                {
                    foreach(XmlNode objXmlBook in objXmlBookList)
                    {
                        string strCode = objXmlBook["code"]?.InnerText;
                        if(!string.IsNullOrEmpty(strCode))
                        {
                            ListItem objBookInfo = new ListItem(strCode, objXmlBook["translate"]?.InnerText ?? objXmlBook["name"]?.InnerText ?? strCode);
                            lstSourcebookInfos.Add(objBookInfo);
                        }
                    }
                }
            }

            lstSourcebookInfos.Sort(CompareListItems.CompareNames);
            bool blnOldSkipRefresh = _blnSkipRefresh;
            _blnSkipRefresh = true;
            lstGlobalSourcebookInfos.BeginUpdate();
            string strOldSelected = lstGlobalSourcebookInfos.SelectedValue?.ToString();
            lstGlobalSourcebookInfos.DataSource = null;
            lstGlobalSourcebookInfos.DataSource = lstSourcebookInfos;
            lstGlobalSourcebookInfos.ValueMember = nameof(ListItem.Value);
            lstGlobalSourcebookInfos.DisplayMember = nameof(ListItem.Name);
            _blnSkipRefresh = blnOldSkipRefresh;
            if(string.IsNullOrEmpty(strOldSelected))
                lstGlobalSourcebookInfos.SelectedIndex = -1;
            else
                lstGlobalSourcebookInfos.SelectedValue = strOldSelected;
            lstGlobalSourcebookInfos.EndUpdate();
        }

        private void PopulateCustomDataDirectoryListBox()
        {
            ListItem objOldSelected = lsbCustomDataDirectories.SelectedIndex != -1 ? (ListItem)lsbCustomDataDirectories.SelectedItem : ListItem.Blank;
            string strNameFormat = "{0}" + LanguageManager.GetString("String_Space", _strSelectedLanguage) + "<{1}>";
            lsbCustomDataDirectories.BeginUpdate();
            if (_setCustomDataDirectoryInfos.Count != lsbCustomDataDirectories.Items.Count)
            {
                lsbCustomDataDirectories.Items.Clear();
                
                foreach (CustomDataDirectoryInfo objCustomDataDirectory in _setCustomDataDirectoryInfos)
                {
                    ListItem objItem = new ListItem(objCustomDataDirectory,
                        string.Format(_objSelectedCultureInfo, strNameFormat, objCustomDataDirectory.Name,
                            objCustomDataDirectory.Path));
                    lsbCustomDataDirectories.Items.Add(objItem);
                }
            }
            else
            {
                HashSet<CustomDataDirectoryInfo> setListedInfos = new HashSet<CustomDataDirectoryInfo>();
                for (int iI = lsbCustomDataDirectories.Items.Count - 1; iI >= 0; --iI)
                {
                    ListItem objExistingItem = (ListItem) lsbCustomDataDirectories.Items[iI];
                    CustomDataDirectoryInfo objExistingInfo = objExistingItem.Value as CustomDataDirectoryInfo;
                    if (objExistingInfo == null || !_setCustomDataDirectoryInfos.Contains(objExistingInfo))
                        lsbCustomDataDirectories.Items.RemoveAt(iI);
                    else
                        setListedInfos.Add(objExistingInfo);
                }
                foreach (CustomDataDirectoryInfo objCustomDataDirectory in _setCustomDataDirectoryInfos.Where(x => !setListedInfos.Contains(x)))
                {
                    ListItem objItem = new ListItem(objCustomDataDirectory,
                        string.Format(_objSelectedCultureInfo, strNameFormat, objCustomDataDirectory.Name,
                            objCustomDataDirectory.Path));
                    lsbCustomDataDirectories.Items.Add(objItem);
                }
            }
            if (_blnLoading)
            {
                lsbCustomDataDirectories.DisplayMember = nameof(ListItem.Name);
                lsbCustomDataDirectories.ValueMember = nameof(ListItem.Value);
            }
            lsbCustomDataDirectories.EndUpdate();
            lsbCustomDataDirectories.SelectedItem = objOldSelected;
        }

        /// <summary>
        /// Set the values for all of the controls based on the Options for the selected Setting.
        /// </summary>
        private void PopulateOptions()
        {
            RefreshGlobalSourcebookInfosListView();
            PopulateCustomDataDirectoryListBox();

            chkAutomaticUpdate.Checked = GlobalOptions.AutomaticUpdate;
            chkLiveCustomData.Checked = GlobalOptions.LiveCustomData;
            chkLiveUpdateCleanCharacterFiles.Checked = GlobalOptions.LiveUpdateCleanCharacterFiles;
            chkUseLogging.Checked = GlobalOptions.UseLogging;
            cboUseLoggingApplicationInsights.Enabled = chkUseLogging.Checked;
            PopulateApplicationInsightsOptions();
            PopulateColorModes();

            chkLifeModule.Checked = GlobalOptions.LifeModuleEnabled;
            chkPreferNightlyBuilds.Checked = GlobalOptions.PreferNightlyBuilds;
            chkStartupFullscreen.Checked = GlobalOptions.StartupFullscreen;
            chkSingleDiceRoller.Checked = GlobalOptions.SingleDiceRoller;
            chkDatesIncludeTime.Checked = GlobalOptions.DatesIncludeTime;
            chkPrintToFileFirst.Checked = GlobalOptions.PrintToFileFirst;
            nudBrowserVersion.Value = GlobalOptions.EmulatedBrowserVersion;
            txtPDFAppPath.Text = GlobalOptions.PDFAppPath;
            txtCharacterRosterPath.Text = GlobalOptions.CharacterRosterPath;
            chkHideMasterIndex.Checked = GlobalOptions.HideMasterIndex;
            chkHideCharacterRoster.Checked = GlobalOptions.HideCharacterRoster;
            chkCreateBackupOnCareer.Checked = GlobalOptions.CreateBackupOnCareer;
            chkConfirmDelete.Checked = GlobalOptions.ConfirmDelete;
            chkConfirmKarmaExpense.Checked = GlobalOptions.ConfirmKarmaExpense;
            chkHideItemsOverAvail.Checked = GlobalOptions.HideItemsOverAvailLimit;
            chkAllowHoverIncrement.Checked = GlobalOptions.AllowHoverIncrement;
            chkSearchInCategoryOnly.Checked = GlobalOptions.SearchInCategoryOnly;
            chkAllowSkillDiceRolling.Checked = GlobalOptions.AllowSkillDiceRolling;
            chkAllowEasterEggs.Checked = GlobalOptions.AllowEasterEggs;
            chkEnablePlugins.Checked = GlobalOptions.PluginsEnabled;
            chkCustomDateTimeFormats.Checked = GlobalOptions.CustomDateTimeFormats;
            if (!chkCustomDateTimeFormats.Checked)
            {
                txtDateFormat.Text = GlobalOptions.CultureInfo.DateTimeFormat.ShortDatePattern;
                txtTimeFormat.Text = GlobalOptions.CultureInfo.DateTimeFormat.ShortTimePattern;
            }
            else
            {
                txtDateFormat.Text = GlobalOptions.CustomDateFormat;
                txtTimeFormat.Text = GlobalOptions.CustomTimeFormat;
            }
            PluginsShowOrHide(chkEnablePlugins.Checked);
        }

        private void SaveGlobalOptions()
        {
            GlobalOptions.AutomaticUpdate = chkAutomaticUpdate.Checked;
            GlobalOptions.LiveCustomData = chkLiveCustomData.Checked;
            GlobalOptions.LiveUpdateCleanCharacterFiles = chkLiveUpdateCleanCharacterFiles.Checked;
            GlobalOptions.UseLogging = chkUseLogging.Checked;
            if (Enum.TryParse(cboUseLoggingApplicationInsights.SelectedValue.ToString(), out UseAILogging useAI))
                GlobalOptions.UseLoggingApplicationInsights = useAI;

            if (string.IsNullOrEmpty(_strSelectedLanguage))
            {
                // We have this set differently because changing the selected language also changes the selected default character sheet
                _strSelectedLanguage = GlobalOptions.DefaultLanguage;
                try
                {
                    _objSelectedCultureInfo = CultureInfo.GetCultureInfo(_strSelectedLanguage);
                }
                catch (CultureNotFoundException)
                {
                    _objSelectedCultureInfo = GlobalOptions.SystemCultureInfo;
                }
            }
            GlobalOptions.Language = _strSelectedLanguage;
            GlobalOptions.ColorModeSetting = _eSelectedColorModeSetting;
            GlobalOptions.StartupFullscreen = chkStartupFullscreen.Checked;
            GlobalOptions.SingleDiceRoller = chkSingleDiceRoller.Checked;
            GlobalOptions.DefaultCharacterSheet = cboXSLT.SelectedValue?.ToString() ?? GlobalOptions.DefaultCharacterSheetDefaultValue;
            GlobalOptions.DatesIncludeTime = chkDatesIncludeTime.Checked;
            GlobalOptions.PrintToFileFirst = chkPrintToFileFirst.Checked;
            GlobalOptions.EmulatedBrowserVersion = decimal.ToInt32(nudBrowserVersion.Value);
            GlobalOptions.PDFAppPath = txtPDFAppPath.Text;
            GlobalOptions.PDFParameters = cboPDFParameters.SelectedValue?.ToString() ?? string.Empty;
            GlobalOptions.LifeModuleEnabled = chkLifeModule.Checked;
            GlobalOptions.PreferNightlyBuilds = chkPreferNightlyBuilds.Checked;
            GlobalOptions.CharacterRosterPath = txtCharacterRosterPath.Text;
            GlobalOptions.HideMasterIndex = chkHideMasterIndex.Checked;
            GlobalOptions.HideCharacterRoster = chkHideCharacterRoster.Checked;
            GlobalOptions.CreateBackupOnCareer = chkCreateBackupOnCareer.Checked;
            GlobalOptions.ConfirmDelete = chkConfirmDelete.Checked;
            GlobalOptions.ConfirmKarmaExpense = chkConfirmKarmaExpense.Checked;
            GlobalOptions.HideItemsOverAvailLimit = chkHideItemsOverAvail.Checked;
            GlobalOptions.AllowHoverIncrement = chkAllowHoverIncrement.Checked;
            GlobalOptions.SearchInCategoryOnly = chkSearchInCategoryOnly.Checked;
            GlobalOptions.AllowSkillDiceRolling = chkAllowSkillDiceRolling.Checked;
            GlobalOptions.DefaultCharacterOption = OptionsManager.LoadedCharacterOptions.Values.FirstOrDefault(x =>
                                                       x.Name == cboDefaultCharacterOption.SelectedValue.ToString())?.Name
                                                  ?? GlobalOptions.DefaultCharacterOptionDefaultValue;
            GlobalOptions.AllowEasterEggs = chkAllowEasterEggs.Checked;
            GlobalOptions.PluginsEnabled = chkEnablePlugins.Checked;
            GlobalOptions.SavedImageQuality = nudMugshotCompressionQuality.Enabled ? decimal.ToInt32(nudMugshotCompressionQuality.Value) : int.MaxValue;
            GlobalOptions.CustomDateTimeFormats = chkCustomDateTimeFormats.Checked;
            if (GlobalOptions.CustomDateTimeFormats)
            {
                GlobalOptions.CustomDateFormat = txtDateFormat.Text;
                GlobalOptions.CustomTimeFormat = txtTimeFormat.Text;
            }

            GlobalOptions.CustomDataDirectoryInfos.Clear();
            foreach (CustomDataDirectoryInfo objInfo in _setCustomDataDirectoryInfos)
                GlobalOptions.CustomDataDirectoryInfos.Add(objInfo);
            XmlManager.RebuildDataDirectoryInfo(GlobalOptions.CustomDataDirectoryInfos);
            GlobalOptions.SourcebookInfos.Clear();
            foreach (SourcebookInfo objInfo in _dicSourcebookInfos.Values)
                GlobalOptions.SourcebookInfos.Add(objInfo.Code, objInfo);
        }

        /// <summary>
        /// Save the global settings to the registry.
        /// </summary>
        private void SaveRegistrySettings()
        {
            SaveGlobalOptions();

            GlobalOptions.SaveOptionsToRegistry();
        }

        private void PopulateDefaultCharacterOptionList()
        {
            List<ListItem> lstCharacterOptions = new List<ListItem>(OptionsManager.LoadedCharacterOptions.Count);
            foreach (KeyValuePair<string, CharacterOptions> kvpLoopCharacterOptions in OptionsManager.LoadedCharacterOptions)
            {
                string strId = kvpLoopCharacterOptions.Key;
                if (!string.IsNullOrEmpty(strId))
                {
                    string strName = kvpLoopCharacterOptions.Value.Name;
                    if (strName.IsGuid() || (strName.StartsWith('{') && strName.EndsWith('}')))
                        strName = LanguageManager.GetString(strName.TrimStartOnce('{').TrimEndOnce('}'), _strSelectedLanguage);
                    lstCharacterOptions.Add(new ListItem(strId, strName));
                }
            }
            lstCharacterOptions.Sort(CompareListItems.CompareNames);

            string strOldSelected = cboDefaultCharacterOption.SelectedValue?.ToString() ?? GlobalOptions.DefaultCharacterOption;

            cboDefaultCharacterOption.BeginUpdate();
            cboDefaultCharacterOption.DataSource = null;
            cboDefaultCharacterOption.DataSource = lstCharacterOptions;
            cboDefaultCharacterOption.ValueMember = nameof(ListItem.Value);
            cboDefaultCharacterOption.DisplayMember = nameof(ListItem.Name);

            if(!string.IsNullOrEmpty(strOldSelected))
            {
                cboDefaultCharacterOption.SelectedValue = strOldSelected;
                if(cboDefaultCharacterOption.SelectedIndex == -1 && lstCharacterOptions.Count > 0)
                    cboDefaultCharacterOption.SelectedIndex = 0;
            }

            cboDefaultCharacterOption.EndUpdate();
        }

        private void PopulateMugshotCompressionOptions()
        {
            List<ListItem> lstMugshotCompressionOptions = new List<ListItem>(2)
            {
                new ListItem(ImageFormat.Png.ToString(), LanguageManager.GetString("String_Lossless_Compression_Option")),
                new ListItem(ImageFormat.Jpeg.ToString(), LanguageManager.GetString("String_Lossy_Compression_Option"))
            };

            string strOldSelected = cboMugshotCompression.SelectedValue?.ToString();

            cboMugshotCompression.BeginUpdate();
            cboMugshotCompression.DataSource = null;
            cboMugshotCompression.DataSource = lstMugshotCompressionOptions;
            cboMugshotCompression.ValueMember = nameof(ListItem.Value);
            cboMugshotCompression.DisplayMember = nameof(ListItem.Name);

            if (!string.IsNullOrEmpty(strOldSelected))
            {
                cboMugshotCompression.SelectedValue = strOldSelected;
                if (cboMugshotCompression.SelectedIndex == -1 && lstMugshotCompressionOptions.Count > 0)
                    cboMugshotCompression.SelectedIndex = 0;
            }

            cboMugshotCompression.EndUpdate();
            nudMugshotCompressionQuality.Enabled = string.Equals(cboMugshotCompression.SelectedValue.ToString(), ImageFormat.Jpeg.ToString(), StringComparison.Ordinal);
        }

        private void PopulatePDFParameters()
        {
            List<ListItem> lstPdfParameters;

            int intIndex = 0;

            using (XmlNodeList objXmlNodeList = XmlManager.Load("options.xml", null, _strSelectedLanguage).SelectNodes("/chummer/pdfarguments/pdfargument"))
            {
                lstPdfParameters = new List<ListItem>(objXmlNodeList?.Count ?? 0);
                if (objXmlNodeList?.Count > 0)
                {
                    foreach (XmlNode objXmlNode in objXmlNodeList)
                    {
                        string strValue = objXmlNode["value"]?.InnerText;
                        lstPdfParameters.Add(new ListItem(strValue, objXmlNode["translate"]?.InnerText ?? objXmlNode["name"]?.InnerText ?? string.Empty));
                        if (!string.IsNullOrWhiteSpace(GlobalOptions.PDFParameters) && GlobalOptions.PDFParameters == strValue)
                        {
                            intIndex = lstPdfParameters.Count - 1;
                        }
                    }
                }
            }

            string strOldSelected = cboPDFParameters.SelectedValue?.ToString();

            cboPDFParameters.BeginUpdate();
            cboPDFParameters.DataSource = null;
            cboPDFParameters.DataSource = lstPdfParameters;
            cboPDFParameters.ValueMember = nameof(ListItem.Value);
            cboPDFParameters.DisplayMember = nameof(ListItem.Name);
            cboPDFParameters.SelectedIndex = intIndex;

            if(!string.IsNullOrEmpty(strOldSelected))
            {
                cboPDFParameters.SelectedValue = strOldSelected;
                if(cboPDFParameters.SelectedIndex == -1 && lstPdfParameters.Count > 0)
                    cboPDFParameters.SelectedIndex = 0;
            }

            cboPDFParameters.EndUpdate();
        }

        private void PopulateApplicationInsightsOptions()
        {
            string strOldSelected = cboUseLoggingApplicationInsights.SelectedValue?.ToString() ?? GlobalOptions.UseLoggingApplicationInsights.ToString();

            List<ListItem> lstUseAIOptions = new List<ListItem>(6);
            foreach (UseAILogging eOption in Enum.GetValues(typeof(UseAILogging)))
            {
                lstUseAIOptions.Add(new ListItem(eOption, LanguageManager.GetString("String_ApplicationInsights_" + eOption, _strSelectedLanguage)));
            }

            cboUseLoggingApplicationInsights.BeginUpdate();
            cboUseLoggingApplicationInsights.DataSource = null;
            cboUseLoggingApplicationInsights.DataSource = lstUseAIOptions;
            cboUseLoggingApplicationInsights.ValueMember = nameof(ListItem.Value);
            cboUseLoggingApplicationInsights.DisplayMember = nameof(ListItem.Name);

            if (!string.IsNullOrEmpty(strOldSelected))
                cboUseLoggingApplicationInsights.SelectedValue = Enum.Parse(typeof(UseAILogging), strOldSelected);
            if (cboUseLoggingApplicationInsights.SelectedIndex == -1 && lstUseAIOptions.Count > 0)
                cboUseLoggingApplicationInsights.SelectedIndex = 0;
            cboUseLoggingApplicationInsights.EndUpdate();
        }

        private void PopulateColorModes()
        {
            string strOldSelected = cboColorMode.SelectedValue?.ToString() ?? GlobalOptions.ColorModeSetting.ToString();

            List<ListItem> lstColorModes = new List<ListItem>(3);
            foreach (ColorMode eLoopColorMode in Enum.GetValues(typeof(ColorMode)))
            {
                lstColorModes.Add(new ListItem(eLoopColorMode, LanguageManager.GetString("String_" + eLoopColorMode, _strSelectedLanguage)));
            }

            cboColorMode.BeginUpdate();
            cboColorMode.DataSource = null;
            cboColorMode.DataSource = lstColorModes;
            cboColorMode.ValueMember = nameof(ListItem.Value);
            cboColorMode.DisplayMember = nameof(ListItem.Name);

            if (!string.IsNullOrEmpty(strOldSelected))
                cboColorMode.SelectedValue = Enum.Parse(typeof(ColorMode), strOldSelected);
            if (cboColorMode.SelectedIndex == -1 && lstColorModes.Count > 0)
                cboColorMode.SelectedIndex = 0;
            cboColorMode.EndUpdate();
        }

        private void SetToolTips()
        {
            cboUseLoggingApplicationInsights.SetToolTip(string.Format(_objSelectedCultureInfo, LanguageManager.GetString("Tip_Options_TelemetryId", _strSelectedLanguage),
                Properties.Settings.Default.UploadClientId.ToString("D", GlobalOptions.InvariantCultureInfo)).WordWrap());
        }

        private void PopulateLanguageList()
        {
            string languageDirectoryPath = Path.Combine(Utils.GetStartupPath, "lang");
            string[] languageFilePaths = Directory.GetFiles(languageDirectoryPath, "*.xml");
            List<ListItem> lstLanguages = new List<ListItem>(languageFilePaths.Length);
            foreach (string filePath in languageFilePaths)
            {
                XmlDocument xmlDocument = new XmlDocument
                {
                    XmlResolver = null
                };

                try
                {
                    using (StreamReader objStreamReader = new StreamReader(filePath, Encoding.UTF8, true))
                        using (XmlReader objXmlReader = XmlReader.Create(objStreamReader, GlobalOptions.SafeXmlReaderSettings))
                            xmlDocument.Load(objXmlReader);
                }
                catch(IOException)
                {
                    continue;
                }
                catch(XmlException)
                {
                    continue;
                }

                XmlNode node = xmlDocument.SelectSingleNode("/chummer/name");
                if(node == null)
                    continue;

                lstLanguages.Add(new ListItem(Path.GetFileNameWithoutExtension(filePath), node.InnerText));
            }

            lstLanguages.Sort(CompareListItems.CompareNames);

            cboLanguage.BeginUpdate();
            cboLanguage.DataSource = null;
            cboLanguage.DataSource = lstLanguages;
            cboLanguage.ValueMember = nameof(ListItem.Value);
            cboLanguage.DisplayMember = nameof(ListItem.Name);
            cboLanguage.EndUpdate();
        }

        private void PopulateSheetLanguageList()
        {
            cboSheetLanguage.BeginUpdate();
            cboSheetLanguage.DataSource = null;
            cboSheetLanguage.DataSource = GetSheetLanguageList();
            cboSheetLanguage.ValueMember = nameof(ListItem.Value);
            cboSheetLanguage.DisplayMember = nameof(ListItem.Name);
            cboSheetLanguage.EndUpdate();
        }

        public static List<ListItem> GetSheetLanguageList()
        {
            HashSet<string> setLanguagesWithSheets = new HashSet<string>();

            // Populate the XSL list with all of the manifested XSL files found in the sheets\[language] directory.
            using (XmlNodeList xmlSheetLanguageList = XmlManager.Load("sheets.xml").SelectNodes("/chummer/sheets/@lang"))
            {
                if (xmlSheetLanguageList != null)
                {
                    foreach (XmlNode xmlSheetLanguage in xmlSheetLanguageList)
                    {
                        setLanguagesWithSheets.Add(xmlSheetLanguage.InnerText);
                    }
                }
            }

            string languageDirectoryPath = Path.Combine(Utils.GetStartupPath, "lang");
            string[] languageFilePaths = Directory.GetFiles(languageDirectoryPath, "*.xml");
            List<ListItem> lstSheetLanguages = new List<ListItem>(languageFilePaths.Length);
            foreach (string filePath in languageFilePaths)
            {
                string strLanguageName = Path.GetFileNameWithoutExtension(filePath);
                if(!setLanguagesWithSheets.Contains(strLanguageName))
                    continue;

                XmlDocument xmlDocument = new XmlDocument
                {
                    XmlResolver = null
                };

                try
                {
                    using (StreamReader objStreamReader = new StreamReader(filePath, Encoding.UTF8, true))
                        using (XmlReader objXmlReader = XmlReader.Create(objStreamReader, GlobalOptions.SafeXmlReaderSettings))
                            xmlDocument.Load(objXmlReader);
                }
                catch(IOException)
                {
                    continue;
                }
                catch(XmlException)
                {
                    continue;
                }

                XmlNode node = xmlDocument.SelectSingleNode("/chummer/name");
                if(node == null)
                    continue;

                lstSheetLanguages.Add(new ListItem(strLanguageName, node.InnerText));
            }

            lstSheetLanguages.Sort(CompareListItems.CompareNames);

            return lstSheetLanguages;
        }

        private List<ListItem> GetXslFilesFromLocalDirectory(string strLanguage)
        {
            List<ListItem> lstSheets;

            // Populate the XSL list with all of the manifested XSL files found in the sheets\[language] directory.
            using (XmlNodeList xmlSheetList = XmlManager.Load("sheets.xml", null, strLanguage).SelectNodes($"/chummer/sheets[@lang='{strLanguage}']/sheet[not(hide)]"))
            {
                lstSheets = new List<ListItem>(xmlSheetList?.Count ?? 0);
                if (xmlSheetList?.Count > 0)
                {
                    foreach (XmlNode xmlSheet in xmlSheetList)
                    {
                        string strFile = xmlSheet["filename"]?.InnerText ?? string.Empty;
                        lstSheets.Add(new ListItem(strLanguage != GlobalOptions.DefaultLanguage ? Path.Combine(strLanguage, strFile) : strFile, xmlSheet["name"]?.InnerText ?? string.Empty));
                    }
                }
            }

            return lstSheets;
        }

        private void PopulateXsltList()
        {
            string strSelectedSheetLanguage = cboSheetLanguage.SelectedValue?.ToString();
            imgSheetLanguageFlag.Image = FlagImageGetter.GetFlagFromCountryCode(strSelectedSheetLanguage?.Substring(3, 2));

            List<ListItem> lstFiles = GetXslFilesFromLocalDirectory(strSelectedSheetLanguage);

            string strOldSelected = cboXSLT.SelectedValue?.ToString() ?? string.Empty;
            // Strip away the language prefix
            int intPos = strOldSelected.LastIndexOf(Path.DirectorySeparatorChar);
            if(intPos != -1)
                strOldSelected = strOldSelected.Substring(intPos + 1);

            cboXSLT.BeginUpdate();
            cboXSLT.DataSource = null;
            cboXSLT.DataSource = lstFiles;
            cboXSLT.ValueMember = nameof(ListItem.Value);
            cboXSLT.DisplayMember = nameof(ListItem.Name);

            if(!string.IsNullOrEmpty(strOldSelected))
            {
                cboXSLT.SelectedValue = !string.IsNullOrEmpty(strSelectedSheetLanguage) && strSelectedSheetLanguage != GlobalOptions.DefaultLanguage ? Path.Combine(strSelectedSheetLanguage, strOldSelected) : strOldSelected;
                // If the desired sheet was not found, fall back to the Shadowrun 5 sheet.
                if(cboXSLT.SelectedIndex == -1 && lstFiles.Count > 0)
                {
                    cboXSLT.SelectedValue = !string.IsNullOrEmpty(strSelectedSheetLanguage) && strSelectedSheetLanguage != GlobalOptions.DefaultLanguage ? Path.Combine(strSelectedSheetLanguage, GlobalOptions.DefaultCharacterSheetDefaultValue) : GlobalOptions.DefaultCharacterSheetDefaultValue;
                    if(cboXSLT.SelectedIndex == -1)
                    {
                        cboXSLT.SelectedIndex = 0;
                    }
                }
            }

            cboXSLT.EndUpdate();
        }

        private void SetDefaultValueForLanguageList()
        {
            cboLanguage.SelectedValue = GlobalOptions.Language;

            if(cboLanguage.SelectedIndex == -1)
                cboLanguage.SelectedValue = GlobalOptions.DefaultLanguage;
        }

        private void SetDefaultValueForSheetLanguageList()
        {
            string strDefaultCharacterSheet = GlobalOptions.DefaultCharacterSheet;
            if(string.IsNullOrEmpty(strDefaultCharacterSheet) || strDefaultCharacterSheet == "Shadowrun (Rating greater 0)")
                strDefaultCharacterSheet = GlobalOptions.DefaultCharacterSheetDefaultValue;

            string strDefaultSheetLanguage = GlobalOptions.Language;
            int intLastIndexDirectorySeparator = strDefaultCharacterSheet.LastIndexOf(Path.DirectorySeparatorChar);
            if(intLastIndexDirectorySeparator != -1)
            {
                string strSheetLanguage = strDefaultCharacterSheet.Substring(0, intLastIndexDirectorySeparator);
                if(strSheetLanguage.Length == 5)
                    strDefaultSheetLanguage = strSheetLanguage;
            }

            cboSheetLanguage.SelectedValue = strDefaultSheetLanguage;

            if(cboSheetLanguage.SelectedIndex == -1)
                cboSheetLanguage.SelectedValue = GlobalOptions.DefaultLanguage;
        }

        private void SetDefaultValueForXsltList()
        {
            if(string.IsNullOrEmpty(GlobalOptions.DefaultCharacterSheet))
                GlobalOptions.DefaultCharacterSheet = GlobalOptions.DefaultCharacterSheetDefaultValue;

            cboXSLT.SelectedValue = GlobalOptions.DefaultCharacterSheet;
            if(cboXSLT.SelectedValue == null && cboXSLT.Items.Count > 0)
            {
                int intNameIndex;
                string strLanguage = _strSelectedLanguage;
                if(string.IsNullOrEmpty(strLanguage) || strLanguage == GlobalOptions.DefaultLanguage)
                    intNameIndex = cboXSLT.FindStringExact(GlobalOptions.DefaultCharacterSheet);
                else
                    intNameIndex = cboXSLT.FindStringExact(GlobalOptions.DefaultCharacterSheet.Substring(GlobalOptions.DefaultLanguage.LastIndexOf(Path.DirectorySeparatorChar) + 1));
                cboXSLT.SelectedIndex = Math.Max(0, intNameIndex);
            }
        }

        private void UpdateSourcebookInfoPath(string strPath)
        {
            string strTag = lstGlobalSourcebookInfos.SelectedValue?.ToString() ?? string.Empty;
            SourcebookInfo objFoundSource = _dicSourcebookInfos.ContainsKey(strTag) ? _dicSourcebookInfos[strTag] : null;

            if (objFoundSource != null)
            {
                objFoundSource.Path = strPath;
            }
            else
            {
                // If the Sourcebook was not found in the options, add it.
                _dicSourcebookInfos.Add(strTag, new SourcebookInfo
                {
                    Code = strTag,
                    Path = strPath
                });
            }
        }

        private void cmdUploadPastebin_Click(object sender, EventArgs e)
        {
#if DEBUG
            string strFilePath = "Insert local file here";
            System.Collections.Specialized.NameValueCollection data = new System.Collections.Specialized.NameValueCollection();
            string line;
            using(StreamReader sr = new StreamReader(strFilePath, Encoding.UTF8, true))
            {
                line = sr.ReadToEnd();
            }
            data["api_paste_name"] = "Chummer";
            data["api_paste_expire_date"] = "N";
            data["api_paste_format"] = "xml";
            data["api_paste_code"] = line;
            data["api_dev_key"] = "7845fd372a1050899f522f2d6bab9666";
            data["api_option"] = "paste";

            using (WebClient wb = new WebClient())
            {
                byte[] bytes;
                try
                {
                    bytes = wb.UploadValues("http://pastebin.com/api/api_post.php", data);
                }
                catch (WebException)
                {
                    return;
                }

                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    using (StreamReader reader = new StreamReader(ms, Encoding.UTF8, true))
                    {
                        string response = reader.ReadToEnd();
                        Clipboard.SetText(response);
                    }
                }
            }
#endif
        }
        #endregion

        private void OptionsChanged(object sender, EventArgs e)
        {
            if(!_blnLoading)
            {
                _blnDirty = true;
            }
        }

        private void chkLifeModules_CheckedChanged(object sender, EventArgs e)
        {
            if (!chkLifeModule.Checked || _blnLoading) return;
            if(Program.MainForm.ShowMessageBox(this, LanguageManager.GetString("Tip_LifeModule_Warning", _strSelectedLanguage), Application.ProductName,
                   MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                chkLifeModule.Checked = false;
            else
            {
                OptionsChanged(sender, e);
            }
        }

        private void cmdCharacterRoster_Click(object sender, EventArgs e)
        {
            // Prompt the user to select a save file to associate with this Contact.
            using (FolderBrowserDialog dlgSelectFolder = new FolderBrowserDialog())
                if (dlgSelectFolder.ShowDialog(this) == DialogResult.OK)
                    txtCharacterRosterPath.Text = dlgSelectFolder.SelectedPath;
        }

        private void cmdAddCustomDirectory_Click(object sender, EventArgs e)
        {
            // Prompt the user to select a save file to associate with this Contact.
            using (FolderBrowserDialog dlgSelectFolder = new FolderBrowserDialog {SelectedPath = Utils.GetStartupPath})
            {
                if (dlgSelectFolder.ShowDialog(this) != DialogResult.OK)
                    return;
                using (frmSelectText frmSelectCustomDirectoryName = new frmSelectText
                {
                    Description = LanguageManager.GetString("String_CustomItem_SelectText", _strSelectedLanguage)
                })
                {
                    if (frmSelectCustomDirectoryName.ShowDialog(this) != DialogResult.OK)
                        return;
                    CustomDataDirectoryInfo objNewCustomDataDirectory = new CustomDataDirectoryInfo(frmSelectCustomDirectoryName.SelectedValue, dlgSelectFolder.SelectedPath);

                    if (_setCustomDataDirectoryInfos.Any(x => x.Name == objNewCustomDataDirectory.Name))
                    {
                        Program.MainForm.ShowMessageBox(this, LanguageManager.GetString("Message_Duplicate_CustomDataDirectoryName", _strSelectedLanguage),
                            LanguageManager.GetString("Message_Duplicate_CustomDataDirectoryName_Title", _strSelectedLanguage), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        _setCustomDataDirectoryInfos.Add(objNewCustomDataDirectory);
                        PopulateCustomDataDirectoryListBox();
                    }
                }
            }
        }

        private void cmdRemoveCustomDirectory_Click(object sender, EventArgs e)
        {
            if (lsbCustomDataDirectories.SelectedIndex == -1)
                return;
            ListItem objSelected = (ListItem)lsbCustomDataDirectories.SelectedItem;
            CustomDataDirectoryInfo objInfoToRemove = objSelected.Value as CustomDataDirectoryInfo;
            if (objInfoToRemove == null || !_setCustomDataDirectoryInfos.Contains(objInfoToRemove))
                return;
            OptionsChanged(sender, e);
            _setCustomDataDirectoryInfos.Remove(objInfoToRemove);
            PopulateCustomDataDirectoryListBox();
        }

        private void cmdRenameCustomDataDirectory_Click(object sender, EventArgs e)
        {
            if (lsbCustomDataDirectories.SelectedIndex == -1)
                return;
            ListItem objSelected = (ListItem)lsbCustomDataDirectories.SelectedItem;
            CustomDataDirectoryInfo objInfoToRename = objSelected.Value as CustomDataDirectoryInfo;
            if (objInfoToRename == null)
                return;
            using (frmSelectText frmSelectCustomDirectoryName = new frmSelectText
            {
                Description = LanguageManager.GetString("String_CustomItem_SelectText", _strSelectedLanguage)
            })
            {
                if (frmSelectCustomDirectoryName.ShowDialog(this) != DialogResult.OK)
                    return;
                if (_setCustomDataDirectoryInfos.Any(x => x.Name == frmSelectCustomDirectoryName.Name))
                {
                    Program.MainForm.ShowMessageBox(this, LanguageManager.GetString("Message_Duplicate_CustomDataDirectoryName", _strSelectedLanguage),
                        LanguageManager.GetString("Message_Duplicate_CustomDataDirectoryName_Title", _strSelectedLanguage), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    CustomDataDirectoryInfo objNewInfo = new CustomDataDirectoryInfo(frmSelectCustomDirectoryName.SelectedValue, objInfoToRename.Path);
                    _setCustomDataDirectoryInfos.Remove(objInfoToRename);
                    _setCustomDataDirectoryInfos.Add(objNewInfo);
                    PopulateCustomDataDirectoryListBox();
                }
            }
        }

        private void cmdCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }


        private void chkEnablePlugins_CheckedChanged(object sender, EventArgs e)
        {
            PluginsShowOrHide(chkEnablePlugins.Checked);
            OptionsChanged(sender, e);
        }

        private void PluginsShowOrHide(bool show)
        {
            if (show)
            {
                if (!tabOptions.TabPages.Contains(tabPlugins))
                    tabOptions.TabPages.Add(tabPlugins);
            }
            else
            {
                if (tabOptions.TabPages.Contains(tabPlugins))
                    tabOptions.TabPages.Remove(tabPlugins);
            }
        }

        private void clbPlugins_VisibleChanged(object sender, EventArgs e)
        {
            clbPlugins.Items.Clear();
            if (Program.PluginLoader.MyPlugins.Count == 0) return;
            using (new CursorWait(this))
            {
                foreach (var plugin in Program.PluginLoader.MyPlugins)
                {
                    try
                    {
                        plugin.CustomInitialize(Program.MainForm);
                        if (GlobalOptions.PluginsEnabledDic.TryGetValue(plugin.ToString(), out var check))
                        {
                            clbPlugins.Items.Add(plugin, check);
                        }
                        else
                        {
                            clbPlugins.Items.Add(plugin);
                        }
                    }
                    catch (ApplicationException ae)
                    {
                        Log.Debug(ae);
                    }
                }

                if (clbPlugins.Items.Count > 0)
                {
                    clbPlugins.SelectedIndex = 0;
                }
            }
        }

        private void clbPlugins_SelectedValueChanged(object sender, EventArgs e)
        {
            UserControl pluginControl = (clbPlugins.SelectedItem as Plugins.IPlugin)?.GetOptionsControl();
            if (pluginControl != null)
            {
                pnlPluginOption.Controls.Clear();
                pnlPluginOption.Controls.Add(pluginControl);
            }
        }

        private void clbPlugins_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            using (new CursorWait(this))
            {
                var plugin = clbPlugins.Items[e.Index];
                if (GlobalOptions.PluginsEnabledDic.ContainsKey(plugin.ToString()))
                    GlobalOptions.PluginsEnabledDic.Remove(plugin.ToString());
                GlobalOptions.PluginsEnabledDic.Add(plugin.ToString(), e.NewValue == CheckState.Checked);
                OptionsChanged(sender, e);
            }
        }

        private void cboUseLoggingApplicationInsights_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            UseAILogging useAI = (UseAILogging) ((ListItem) cboUseLoggingApplicationInsights.SelectedItem).Value;
            if (useAI > UseAILogging.Info
                && GlobalOptions.UseLoggingApplicationInsights <= UseAILogging.Info
                && DialogResult.Yes != Program.MainForm.ShowMessageBox(this,
                    LanguageManager.GetString("Message_Options_ConfirmTelemetry", _strSelectedLanguage).WordWrap(),
                    LanguageManager.GetString("MessageTitle_Options_ConfirmTelemetry", _strSelectedLanguage),
                    MessageBoxButtons.YesNo))
            {
                _blnLoading = true;
                cboUseLoggingApplicationInsights.SelectedItem = UseAILogging.Info;
                _blnLoading = false;
                return;
            }
            OptionsChanged(sender, e);
        }

        private void chkUseLogging_CheckedChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            if (chkUseLogging.Checked && !GlobalOptions.UseLogging)
            {
                if (DialogResult.Yes != Program.MainForm.ShowMessageBox(this,
                    LanguageManager.GetString("Message_Options_ConfirmDetailedTelemetry", _strSelectedLanguage).WordWrap(),
                    LanguageManager.GetString("MessageTitle_Options_ConfirmDetailedTelemetry", _strSelectedLanguage),
                    MessageBoxButtons.YesNo))
                {
                    _blnLoading = true;
                    chkUseLogging.Checked = false;
                    _blnLoading = false;
                    return;
                }
            }
            cboUseLoggingApplicationInsights.Enabled = chkUseLogging.Checked;
            OptionsChanged(sender, e);
        }

        private void cboUseLoggingHelp_Click(object sender, EventArgs e)
        {
            //open the telemetry document
            System.Diagnostics.Process.Start("https://docs.google.com/document/d/1LThAg6U5qXzHAfIRrH0Kb7griHrPN0hy7ab8FSJDoFY/edit?usp=sharing");
        }

        private void cmdPluginsHelp_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://docs.google.com/document/d/1WOPB7XJGgcmxg7REWxF6HdP3kQdtHpv6LJOXZtLggxM/edit?usp=sharing");
        }

        private void chkCustomDateTimeFormats_CheckedChanged(object sender, EventArgs e)
        {
            grpDateFormat.Enabled = chkCustomDateTimeFormats.Checked;
            grpTimeFormat.Enabled = chkCustomDateTimeFormats.Checked;
            if (!chkCustomDateTimeFormats.Checked)
            {
                txtDateFormat.Text = GlobalOptions.CultureInfo.DateTimeFormat.ShortDatePattern;
                txtTimeFormat.Text = GlobalOptions.CultureInfo.DateTimeFormat.ShortTimePattern;
            }
            OptionsChanged(sender, e);
        }

        private void txtDateFormat_TextChanged(object sender, EventArgs e)
        {
            try
            {
                txtDateFormatView.Text = DateTime.Now.ToString(txtDateFormat.Text, _objSelectedCultureInfo);
            }
            catch (Exception)
            {
                txtDateFormatView.Text = LanguageManager.GetString("String_Error", _strSelectedLanguage);
            }
            OptionsChanged(sender, e);
        }

        private void txtTimeFormat_TextChanged(object sender, EventArgs e)
        {
            try
            {
                txtTimeFormatView.Text = DateTime.Now.ToString(txtTimeFormat.Text, _objSelectedCultureInfo);
            }
            catch (Exception)
            {
                txtTimeFormatView.Text = LanguageManager.GetString("String_Error", _strSelectedLanguage);
            }
            OptionsChanged(sender, e);
        }

        private void cboMugshotCompression_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            nudMugshotCompressionQuality.Enabled = string.Equals(cboMugshotCompression.SelectedValue.ToString(), ImageFormat.Jpeg.ToString(), StringComparison.Ordinal);
            OptionsChanged(sender, e);
        }

        private void cboColorMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            using (new CursorWait(this))
            {
                if (Enum.TryParse(cboColorMode.SelectedValue.ToString(), true, out ColorMode eNewColorMode) && _eSelectedColorModeSetting != eNewColorMode)
                {
                    _eSelectedColorModeSetting = eNewColorMode;
                    switch (eNewColorMode)
                    {
                        case ColorMode.Automatic:
                            this.UpdateLightDarkMode(!ColorManager.DoesRegistrySayDarkMode());
                            break;
                        case ColorMode.Light:
                            this.UpdateLightDarkMode(true);
                            break;
                        case ColorMode.Dark:
                            this.UpdateLightDarkMode(false);
                            break;
                    }
                }
            }

            OptionsChanged(sender, e);
        }
    }
}
