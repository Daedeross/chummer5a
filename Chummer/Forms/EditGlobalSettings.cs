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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;
using Chummer.Plugins;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using NLog;
using Application = System.Windows.Forms.Application;

namespace Chummer
{
    public partial class EditGlobalSettings : Form
    {
        private static Logger Log { get; } = LogManager.GetCurrentClassLogger();

        // List of custom data directories possible to be added to a character
        private readonly HashSet<CustomDataDirectoryInfo> _setCustomDataDirectoryInfos;

        // List of sourcebook infos, needed to make sure we don't directly modify ones in the options unless we save our options
        private readonly Dictionary<string, SourcebookInfo> _dicSourcebookInfos;

        private bool _blnSkipRefresh;
        private bool _blnDirty;
        private bool _blnLoading = true;
        private string _strSelectedLanguage = GlobalSettings.Language;
        private CultureInfo _objSelectedCultureInfo = GlobalSettings.CultureInfo;
        private ColorMode _eSelectedColorModeSetting = GlobalSettings.ColorModeSetting;

        #region Form Events

        public EditGlobalSettings(string strActiveTab = "")
        {
            InitializeComponent();
#if !DEBUG
            // tabPage3 only contains cmdUploadPastebin, which is not used if DEBUG is not enabled
            // Remove this line if cmdUploadPastebin_Click has some functionality if DEBUG is not enabled or if tabPage3 gets some other control that can be used if DEBUG is not enabled
            tabOptions.TabPages.Remove(tabGitHubIssues);
#endif
            this.UpdateLightDarkMode();
            this.TranslateWinForm();

            _setCustomDataDirectoryInfos = new HashSet<CustomDataDirectoryInfo>(GlobalSettings.CustomDataDirectoryInfos);
            _dicSourcebookInfos = new Dictionary<string, SourcebookInfo>(GlobalSettings.SourcebookInfos);
            if (!string.IsNullOrEmpty(strActiveTab))
            {
                int intActiveTabIndex = tabOptions.TabPages.IndexOfKey(strActiveTab);
                if (intActiveTabIndex > 0)
                    tabOptions.SelectedTab = tabOptions.TabPages[intActiveTabIndex];
            }
        }

        private async void EditGlobalSettings_Load(object sender, EventArgs e)
        {
            await PopulateDefaultCharacterSettingLists();
            await PopulateMugshotCompressionOptions();
            await SetToolTips();
            await PopulateOptions();
            await PopulateLanguageList();
            await SetDefaultValueForLanguageList();
            await PopulateSheetLanguageList();
            await SetDefaultValueForSheetLanguageList();
            await PopulateXsltList();
            await SetDefaultValueForXsltList();
            await PopulatePdfParameters();

            _blnLoading = false;

            if (_blnPromptPdfReaderOnLoad)
            {
                _blnPromptPdfReaderOnLoad = false;
                await PromptPdfAppPath();
            }

            if (!string.IsNullOrEmpty(_strSelectCodeOnRefresh))
            {
                bool blnDoPdfPrompt = await lstGlobalSourcebookInfos.DoThreadSafeFuncAsync(x =>
                {
                    x.SelectedValue = _strSelectCodeOnRefresh;
                    return x.SelectedIndex >= 0;
                });
                if (blnDoPdfPrompt)
                    await PromptPdfLocation();
                _strSelectCodeOnRefresh = string.Empty;
            }
        }

        #endregion Form Events

        #region Control Events

        private async void cmdOK_Click(object sender, EventArgs e)
        {
            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                if (_blnDirty)
                {
                    string text = await LanguageManager.GetStringAsync("Message_Options_SaveForms",
                                                                       _strSelectedLanguage);
                    string caption
                        = await LanguageManager.GetStringAsync("MessageTitle_Options_CloseForms", _strSelectedLanguage);

                    if (Program.ShowMessageBox(this, text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                        != DialogResult.Yes)
                        return;
                }

                await this.DoThreadSafeAsync(x => x.DialogResult = DialogResult.OK);
                await SaveRegistrySettings();
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
            if (_blnDirty)
                await Utils.RestartApplication(_strSelectedLanguage, "Message_Options_CloseForms");
        }

        private async void cboLanguage_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                _strSelectedLanguage = await cboLanguage.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString()) ?? GlobalSettings.DefaultLanguage;
                try
                {
                    _objSelectedCultureInfo = CultureInfo.GetCultureInfo(_strSelectedLanguage);
                }
                catch (CultureNotFoundException)
                {
                    _objSelectedCultureInfo = GlobalSettings.SystemCultureInfo;
                }

                await imgLanguageFlag.DoThreadSafeAsync(x => x.Image
                                                            = Math.Min(x.Width, x.Height) >= 32
                                                                ? FlagImageGetter.GetFlagFromCountryCode192Dpi(
                                                                    _strSelectedLanguage.Substring(3, 2))
                                                                : FlagImageGetter.GetFlagFromCountryCode(
                                                                    _strSelectedLanguage.Substring(3, 2)));

                bool isEnabled = !string.IsNullOrEmpty(_strSelectedLanguage) && !_strSelectedLanguage.Equals(GlobalSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase);
                await cmdVerify.DoThreadSafeAsync(x => x.Enabled = isEnabled);
                await cmdVerifyData.DoThreadSafeAsync(x => x.Enabled = isEnabled);

                if (!_blnLoading)
                {
                    CursorWait objCursorWait = await CursorWait.NewAsync(this);
                    try
                    {
                        _blnLoading = true;
                        await TranslateForm();
                        _blnLoading = false;
                    }
                    finally
                    {
                        await objCursorWait.DisposeAsync();
                    }
                }

                OptionsChanged(sender, e);
            }
            catch(ArgumentOutOfRangeException ex)
            {
                Log.Error(ex, "How the hell? Give me the callstack! " + ex);
            }
        }

        private async void cboSheetLanguage_SelectedIndexChanged(object sender, EventArgs e)
        {
            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                await PopulateXsltList();
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async void cmdVerify_Click(object sender, EventArgs e)
        {
            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                await LanguageManager.VerifyStrings(_strSelectedLanguage);
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async void cmdVerifyData_Click(object sender, EventArgs e)
        {
            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                string strSelectedLanguage = _strSelectedLanguage;
                // Build a list of Sourcebooks that will be passed to the Verify method.
                // This is done since not all of the books are available in every language or the user may only wish to verify the content of certain books.
                using (new FetchSafelyFromPool<HashSet<string>>(Utils.StringHashSetPool,
                                                                out HashSet<string> setBooks))
                {
                    foreach (ListItem objItem in await lstGlobalSourcebookInfos.DoThreadSafeFuncAsync(x => x.Items))
                    {
                        string strItemValue = objItem.Value?.ToString();
                        setBooks.Add(strItemValue);
                    }

                    await XmlManager.Verify(strSelectedLanguage, setBooks);
                }

                string strFilePath
                    = Path.Combine(Utils.GetStartupPath, "lang", "results_" + strSelectedLanguage + ".xml");
                Program.ShowMessageBox(
                    this,
                    string.Format(_objSelectedCultureInfo,
                                  await LanguageManager.GetStringAsync("Message_Options_ValidationResults",
                                                                       _strSelectedLanguage), strFilePath),
                    await LanguageManager.GetStringAsync("MessageTitle_Options_ValidationResults",
                                                         _strSelectedLanguage), MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async void cmdPDFAppPath_Click(object sender, EventArgs e)
        {
            await PromptPdfAppPath();
        }

        private async void cmdPDFLocation_Click(object sender, EventArgs e)
        {
            await PromptPdfLocation();
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
            if (_blnSkipRefresh || _blnLoading)
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

        private async void cmdPDFTest_Click(object sender, EventArgs e)
        {
            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                await CommonFunctions.OpenPdf(
                    await lstGlobalSourcebookInfos.DoThreadSafeFuncAsync(x => x.SelectedValue) + " 3", null,
                    await cboPDFParameters.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString()) ?? string.Empty,
                    await txtPDFAppPath.DoThreadSafeFuncAsync(x => x.Text));
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async void cboUseLoggingApplicationInsights_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            UseAILogging useAI = await cboUseLoggingApplicationInsights.DoThreadSafeFuncAsync(x => (UseAILogging)((ListItem)x.SelectedItem).Value);
            GlobalSettings.UseLoggingResetCounter = 10;
            if (useAI > UseAILogging.Info
                && GlobalSettings.UseLoggingApplicationInsightsPreference <= UseAILogging.Info
                && DialogResult.Yes != Program.ShowMessageBox(this,
                    (await LanguageManager.GetStringAsync("Message_Options_ConfirmTelemetry", _strSelectedLanguage)).WordWrap(),
                    await LanguageManager.GetStringAsync("MessageTitle_Options_ConfirmTelemetry", _strSelectedLanguage),
                    MessageBoxButtons.YesNo))
            {
                _blnLoading = true;
                await cboUseLoggingApplicationInsights.DoThreadSafeAsync(x => x.SelectedItem = UseAILogging.Info);
                _blnLoading = false;
                return;
            }
            OptionsChanged(sender, e);
        }

        private async void chkUseLogging_CheckedChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            if (chkUseLogging.Checked && !GlobalSettings.UseLogging && DialogResult.Yes != Program.ShowMessageBox(this,
                (await LanguageManager.GetStringAsync("Message_Options_ConfirmDetailedTelemetry", _strSelectedLanguage)).WordWrap(),
                await LanguageManager.GetStringAsync("MessageTitle_Options_ConfirmDetailedTelemetry", _strSelectedLanguage),
                MessageBoxButtons.YesNo))
            {
                _blnLoading = true;
                await chkUseLogging.DoThreadSafeAsync(x => x.Checked = false);
                _blnLoading = false;
                return;
            }
            bool blnEnabled = await chkUseLogging.DoThreadSafeFuncAsync(x => x.Checked);
            await cboUseLoggingApplicationInsights.DoThreadSafeAsync(x => x.Enabled = blnEnabled);
            OptionsChanged(sender, e);
        }

        private void cboUseLoggingHelp_Click(object sender, EventArgs e)
        {
            //open the telemetry document
            Process.Start("https://docs.google.com/document/d/1LThAg6U5qXzHAfIRrH0Kb7griHrPN0hy7ab8FSJDoFY/edit?usp=sharing");
        }

        private void cmdPluginsHelp_Click(object sender, EventArgs e)
        {
            Process.Start("https://docs.google.com/document/d/1WOPB7XJGgcmxg7REWxF6HdP3kQdtHpv6LJOXZtLggxM/edit?usp=sharing");
        }

        private void chkCustomDateTimeFormats_CheckedChanged(object sender, EventArgs e)
        {
            grpDateFormat.Enabled = chkCustomDateTimeFormats.Checked;
            grpTimeFormat.Enabled = chkCustomDateTimeFormats.Checked;
            if (!chkCustomDateTimeFormats.Checked)
            {
                txtDateFormat.Text = GlobalSettings.CultureInfo.DateTimeFormat.ShortDatePattern;
                txtTimeFormat.Text = GlobalSettings.CultureInfo.DateTimeFormat.ShortTimePattern;
            }
            OptionsChanged(sender, e);
        }

        private async void txtDateFormat_TextChanged(object sender, EventArgs e)
        {
            string strText;
            try
            {
                strText = DateTime.Now.ToString(await txtDateFormat.DoThreadSafeFuncAsync(x => x.Text), _objSelectedCultureInfo);
            }
            catch
            {
                strText = await LanguageManager.GetStringAsync("String_Error", _strSelectedLanguage);
            }
            await txtDateFormatView.DoThreadSafeAsync(x => x.Text = strText);
            OptionsChanged(sender, e);
        }

        private async void txtTimeFormat_TextChanged(object sender, EventArgs e)
        {
            string strText;
            try
            {
                strText = DateTime.Now.ToString(await txtTimeFormat.DoThreadSafeFuncAsync(x => x.Text), _objSelectedCultureInfo);
            }
            catch
            {
                strText = await LanguageManager.GetStringAsync("String_Error", _strSelectedLanguage);
            }
            await txtTimeFormatView.DoThreadSafeAsync(x => x.Text = strText);
            OptionsChanged(sender, e);
        }

        private void cboMugshotCompression_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            bool blnShowQualitySelector = Equals(cboMugshotCompression.SelectedValue, "jpeg_manual");
            lblMugshotCompressionQuality.Visible = blnShowQualitySelector;
            nudMugshotCompressionQuality.Visible = blnShowQualitySelector;
            OptionsChanged(sender, e);
        }

        private async void cboColorMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                if (Enum.TryParse(await cboColorMode.DoThreadSafeFuncAsync(x => x.SelectedValue.ToString()), true,
                                  out ColorMode eNewColorMode) && _eSelectedColorModeSetting != eNewColorMode)
                {
                    _eSelectedColorModeSetting = eNewColorMode;
                    switch (eNewColorMode)
                    {
                        case ColorMode.Automatic:
                            await this.UpdateLightDarkModeAsync(!ColorManager.DoesRegistrySayDarkMode());
                            break;

                        case ColorMode.Light:
                            await this.UpdateLightDarkModeAsync(true);
                            break;

                        case ColorMode.Dark:
                            await this.UpdateLightDarkModeAsync(false);
                            break;
                    }
                }
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }

            OptionsChanged(sender, e);
        }

        private void chkPrintExpenses_CheckedChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            if (chkPrintExpenses.Checked)
            {
                chkPrintFreeExpenses.Enabled = true;
            }
            else
            {
                chkPrintFreeExpenses.Enabled = false;
                chkPrintFreeExpenses.Checked = false;
            }
            OptionsChanged(sender, e);
        }

        private async void cmdRemovePDFLocation_Click(object sender, EventArgs e)
        {
            await UpdateSourcebookInfoPath(string.Empty);
            await txtPDFLocation.DoThreadSafeAsync(x => x.Text = string.Empty);
        }

        private void txtPDFLocation_TextChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            cmdRemovePDFLocation.Enabled = txtPDFLocation.TextLength > 0;
            cmdPDFTest.Enabled = txtPDFAppPath.TextLength > 0 && txtPDFLocation.TextLength > 0;
            OptionsChanged(sender, e);
        }

        private void cmdRemoveCharacterRoster_Click(object sender, EventArgs e)
        {
            txtCharacterRosterPath.Text = string.Empty;
            cmdRemoveCharacterRoster.Enabled = false;
            OptionsChanged(sender, e);
        }

        private void cmdRemovePDFAppPath_Click(object sender, EventArgs e)
        {
            txtPDFAppPath.Text = string.Empty;
            cmdRemovePDFAppPath.Enabled = false;
            cmdPDFTest.Enabled = false;
            OptionsChanged(sender, e);
        }

        private async void chkLifeModules_CheckedChanged(object sender, EventArgs e)
        {
            if (_blnLoading || !await chkLifeModule.DoThreadSafeFuncAsync(x => x.Checked))
                return;
            if (Program.ShowMessageBox(this, await LanguageManager.GetStringAsync("Tip_LifeModule_Warning", _strSelectedLanguage), Application.ProductName,
                   MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                await chkLifeModule.DoThreadSafeAsync(x => x.Checked = false);
            else
            {
                OptionsChanged(sender, e);
            }
        }

        private void cmdCharacterRoster_Click(object sender, EventArgs e)
        {
            // Prompt the user to select a save file to associate with this Contact.
            using (FolderBrowserDialog dlgSelectFolder = new FolderBrowserDialog())
            {
                if (dlgSelectFolder.ShowDialog(this) != DialogResult.OK)
                    return;
                txtCharacterRosterPath.Text = dlgSelectFolder.SelectedPath;
            }
            cmdRemoveCharacterRoster.Enabled = txtCharacterRosterPath.TextLength > 0;
            OptionsChanged(sender, e);
        }

        private async void cmdAddCustomDirectory_Click(object sender, EventArgs e)
        {
            // Prompt the user to select a save file to associate with this Contact.
            using (FolderBrowserDialog dlgSelectFolder = await this.DoThreadSafeFuncAsync(() => new FolderBrowserDialog()))
            {
                dlgSelectFolder.SelectedPath = Utils.GetStartupPath;
                if (await this.DoThreadSafeFuncAsync(x => dlgSelectFolder.ShowDialog(x)) != DialogResult.OK)
                    return;
                string strDescription
                    = await LanguageManager.GetStringAsync("String_CustomItem_SelectText", _strSelectedLanguage);
                using (ThreadSafeForm<SelectText> frmSelectCustomDirectoryName =
                       await ThreadSafeForm<SelectText>.GetAsync(() => new SelectText
                       {
                           Description = strDescription
                       }))
                {
                    if (await frmSelectCustomDirectoryName.ShowDialogSafeAsync(this) != DialogResult.OK)
                        return;
                    CustomDataDirectoryInfo objNewCustomDataDirectory = new CustomDataDirectoryInfo(frmSelectCustomDirectoryName.MyForm.SelectedValue, dlgSelectFolder.SelectedPath);
                    if (objNewCustomDataDirectory.XmlException != default)
                    {
                        Program.ShowMessageBox(this,
                            string.Format(_objSelectedCultureInfo, await LanguageManager.GetStringAsync("Message_FailedLoad", _strSelectedLanguage),
                                objNewCustomDataDirectory.XmlException.Message),
                            string.Format(_objSelectedCultureInfo,
                                await LanguageManager.GetStringAsync("MessageTitle_FailedLoad", _strSelectedLanguage) +
                                await LanguageManager.GetStringAsync("String_Space", _strSelectedLanguage) + objNewCustomDataDirectory.Name + Path.DirectorySeparatorChar + "manifest.xml"),
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    string strDirectoryPath = objNewCustomDataDirectory.DirectoryPath;
                    if (_setCustomDataDirectoryInfos.Any(x => x.DirectoryPath == strDirectoryPath))
                    {
                        Program.ShowMessageBox(this,
                            string.Format(
                                await LanguageManager.GetStringAsync("Message_Duplicate_CustomDataDirectoryPath",
                                                                     _strSelectedLanguage), objNewCustomDataDirectory.Name),
                            await LanguageManager.GetStringAsync("MessageTitle_Duplicate_CustomDataDirectoryPath",
                                                                 _strSelectedLanguage), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    if (_setCustomDataDirectoryInfos.Contains(objNewCustomDataDirectory))
                    {
                        CustomDataDirectoryInfo objExistingInfo = _setCustomDataDirectoryInfos.FirstOrDefault(x => x.Equals(objNewCustomDataDirectory));
                        if (objExistingInfo != null)
                        {
                            if (objNewCustomDataDirectory.HasManifest)
                            {
                                if (objExistingInfo.HasManifest)
                                {
                                    Program.ShowMessageBox(
                                        string.Format(
                                            await LanguageManager.GetStringAsync(
                                                "Message_Duplicate_CustomDataDirectory"),
                                            objExistingInfo.Name, objNewCustomDataDirectory.Name),
                                        await LanguageManager.GetStringAsync(
                                            "MessageTitle_Duplicate_CustomDataDirectory"),
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    return;
                                }

                                _setCustomDataDirectoryInfos.Remove(objExistingInfo);
                                do
                                {
                                    objExistingInfo.RandomizeGuid();
                                } while (objExistingInfo.Equals(objNewCustomDataDirectory) || _setCustomDataDirectoryInfos.Contains(objExistingInfo));
                                _setCustomDataDirectoryInfos.Add(objExistingInfo);
                            }
                            else
                            {
                                do
                                {
                                    objNewCustomDataDirectory.RandomizeGuid();
                                } while (_setCustomDataDirectoryInfos.Contains(objNewCustomDataDirectory));
                            }
                        }
                    }
                    if (_setCustomDataDirectoryInfos.Any(x =>
                        objNewCustomDataDirectory.CharacterSettingsSaveKey.Equals(x.CharacterSettingsSaveKey,
                            StringComparison.OrdinalIgnoreCase)) && Program.ShowMessageBox(this,
                        string.Format(
                            await LanguageManager.GetStringAsync("Message_Duplicate_CustomDataDirectoryName",
                                                                 _strSelectedLanguage), objNewCustomDataDirectory.Name),
                        await LanguageManager.GetStringAsync("MessageTitle_Duplicate_CustomDataDirectoryName",
                                                             _strSelectedLanguage), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                    _setCustomDataDirectoryInfos.Add(objNewCustomDataDirectory);
                    await PopulateCustomDataDirectoryListBox();
                }
            }
        }

        private async void cmdRemoveCustomDirectory_Click(object sender, EventArgs e)
        {
            if (await lsbCustomDataDirectories.DoThreadSafeFuncAsync(x => x.SelectedIndex) == -1)
                return;
            ListItem objSelected = await lsbCustomDataDirectories.DoThreadSafeFuncAsync(x => (ListItem)x.SelectedItem);
            CustomDataDirectoryInfo objInfoToRemove = (CustomDataDirectoryInfo)objSelected.Value;
            if (!_setCustomDataDirectoryInfos.Remove(objInfoToRemove))
                return;
            OptionsChanged(sender, e);
            await PopulateCustomDataDirectoryListBox();
        }

        private async void cmdRenameCustomDataDirectory_Click(object sender, EventArgs e)
        {
            if (await lsbCustomDataDirectories.DoThreadSafeFuncAsync(x => x.SelectedIndex) == -1)
                return;
            ListItem objSelected = await lsbCustomDataDirectories.DoThreadSafeFuncAsync(x => (ListItem)x.SelectedItem);
            CustomDataDirectoryInfo objInfoToRename = (CustomDataDirectoryInfo)objSelected.Value;
            string strDescription
                = await LanguageManager.GetStringAsync("String_CustomItem_SelectText", _strSelectedLanguage);
            using (ThreadSafeForm<SelectText> frmSelectCustomDirectoryName = await ThreadSafeForm<SelectText>.GetAsync(
                       () => new SelectText
                       {
                           Description = strDescription
                       }))
            {
                if (await frmSelectCustomDirectoryName.ShowDialogSafeAsync(this) != DialogResult.OK)
                    return;
                CustomDataDirectoryInfo objNewInfo = new CustomDataDirectoryInfo(frmSelectCustomDirectoryName.MyForm.SelectedValue, objInfoToRename.DirectoryPath);
                if (!objNewInfo.HasManifest)
                    objNewInfo.CopyGuid(objInfoToRename);
                if (objNewInfo.XmlException != default)
                {
                    Program.ShowMessageBox(this,
                        string.Format(_objSelectedCultureInfo, await LanguageManager.GetStringAsync("Message_FailedLoad", _strSelectedLanguage),
                            objNewInfo.XmlException.Message),
                        string.Format(_objSelectedCultureInfo,
                            await LanguageManager.GetStringAsync("MessageTitle_FailedLoad", _strSelectedLanguage) +
                            await LanguageManager.GetStringAsync("String_Space", _strSelectedLanguage) + objNewInfo.Name + Path.DirectorySeparatorChar + "manifest.xml"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (_setCustomDataDirectoryInfos.Any(x => x != objInfoToRename &&
                                                          objNewInfo.CharacterSettingsSaveKey.Equals(
                                                              x.CharacterSettingsSaveKey,
                                                              StringComparison.OrdinalIgnoreCase)) &&
                    Program.ShowMessageBox(this,
                        string.Format(
                            await LanguageManager.GetStringAsync("Message_Duplicate_CustomDataDirectoryName",
                                                                 _strSelectedLanguage), objNewInfo.Name),
                        await LanguageManager.GetStringAsync("MessageTitle_Duplicate_CustomDataDirectoryName",
                                                             _strSelectedLanguage), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                _setCustomDataDirectoryInfos.Remove(objInfoToRename);
                _setCustomDataDirectoryInfos.Add(objNewInfo);
                await PopulateCustomDataDirectoryListBox();
            }
        }

        private void cmdCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private async void chkEnablePlugins_CheckedChanged(object sender, EventArgs e)
        {
            await PluginsShowOrHide(await chkEnablePlugins.DoThreadSafeFuncAsync(x => x.Checked));
            OptionsChanged(sender, e);
        }

#if DEBUG
        private async void cmdUploadPastebin_Click(object sender, EventArgs e)
        {
            const string strFilePath = "Insert local file here";
            System.Collections.Specialized.NameValueCollection data
                = new System.Collections.Specialized.NameValueCollection();
            string line;
            using (StreamReader sr = new StreamReader(strFilePath, Encoding.UTF8, true))
            {
                line = await sr.ReadToEndAsync();
            }

            data["api_paste_name"] = "Chummer";
            data["api_paste_expire_date"] = "N";
            data["api_paste_format"] = "xml";
            data["api_paste_code"] = line;
            data["api_dev_key"] = "7845fd372a1050899f522f2d6bab9666";
            data["api_option"] = "paste";

            using (System.Net.WebClient wb = new System.Net.WebClient())
            {
                byte[] bytes;
                try
                {
                    bytes = await wb.UploadValuesTaskAsync("https://pastebin.com/api/api_post.php", data);
                }
                catch (System.Net.WebException)
                {
                    return;
                }

                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    using (StreamReader reader = new StreamReader(ms, Encoding.UTF8, true))
                    {
                        string response = await reader.ReadToEndAsync();
                        Clipboard.SetText(response);
                    }
                }
            }
        }
#else
        private void cmdUploadPastebin_Click(object sender, EventArgs e)
        {
            // Method intentionally left empty.
        }
#endif

        private async void clbPlugins_VisibleChanged(object sender, EventArgs e)
        {
            await clbPlugins.DoThreadSafeAsync(x => x.Items.Clear());
            if (Program.PluginLoader.MyPlugins.Count == 0)
                return;
            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                foreach (IPlugin objPlugin in Program.PluginLoader.MyPlugins)
                {
                    try
                    {
                        await Program.MainForm.DoThreadSafeAsync(x => objPlugin.CustomInitialize(x));
                        (bool blnSuccess, bool blnChecked)
                            = await GlobalSettings.PluginsEnabledDic.TryGetValueAsync(objPlugin.ToString());
                        if (blnSuccess)
                        {
                            await clbPlugins.DoThreadSafeAsync(x => x.Items.Add(objPlugin, blnChecked));
                        }
                        else
                        {
                            await clbPlugins.DoThreadSafeAsync(x => x.Items.Add(objPlugin));
                        }
                    }
                    catch (ApplicationException ae)
                    {
                        Log.Debug(ae);
                    }
                }

                await clbPlugins.DoThreadSafeAsync(x =>
                {
                    if (x.Items.Count > 0)
                    {
                        x.SelectedIndex = 0;
                    }
                });
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private void clbPlugins_SelectedValueChanged(object sender, EventArgs e)
        {
            UserControl pluginControl = (clbPlugins.SelectedItem as IPlugin)?.GetOptionsControl();
            if (pluginControl != null)
            {
                pnlPluginOption.Controls.Clear();
                pnlPluginOption.Controls.Add(pluginControl);
            }
        }

        private async void clbPlugins_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                string strPlugin = (await clbPlugins.DoThreadSafeFuncAsync(x => x.Items[e.Index]))?.ToString();
                bool blnNewValue = e.NewValue == CheckState.Checked;
                await GlobalSettings.PluginsEnabledDic.AddOrUpdateAsync(strPlugin, blnNewValue, (x, y) => blnNewValue);
                OptionsChanged(sender, e);
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private void txtPDFAppPath_TextChanged(object sender, EventArgs e)
        {
            cmdRemovePDFAppPath.Enabled = txtPDFAppPath.TextLength > 0;
            cmdPDFTest.Enabled = txtPDFAppPath.TextLength > 0 && txtPDFLocation.TextLength > 0;
            OptionsChanged(sender, e);
        }

        private async void lsbCustomDataDirectories_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnSkipRefresh)
                return;
            ListItem objSelectedItem = await lsbCustomDataDirectories.DoThreadSafeFuncAsync(x => (ListItem)x.SelectedItem);
            CustomDataDirectoryInfo objSelected = (CustomDataDirectoryInfo)objSelectedItem.Value;
            if (objSelected == null)
            {
                await gpbDirectoryInfo.DoThreadSafeAsync(x => x.Visible = false);
                return;
            }

            await gpbDirectoryInfo.DoThreadSafeAsync(x => x.SuspendLayout());
            try
            {
                await objSelected.GetDisplayDescriptionAsync(_strSelectedLanguage)
                                 .ContinueWith(
                                     y => txtDirectoryDescription.DoThreadSafeAsync(x => x.Text = y.Result))
                                 .Unwrap();
                await lblDirectoryVersion.DoThreadSafeAsync(x => x.Text = objSelected.MyVersion.ToString());
                await objSelected.GetDisplayAuthorsAsync(_strSelectedLanguage, _objSelectedCultureInfo)
                                 .ContinueWith(y => lblDirectoryAuthors.DoThreadSafeAsync(x => x.Text = y.Result))
                                 .Unwrap();
                await lblDirectoryName.DoThreadSafeAsync(x => x.Text = objSelected.Name);
                string strText = objSelected.DirectoryPath.Replace(Utils.GetStartupPath, await LanguageManager.GetStringAsync("String_Chummer5a", _strSelectedLanguage));
                await lblDirectoryPath.DoThreadSafeAsync(x => x.Text = strText);

                if (objSelected.DependenciesList.Count > 0)
                {
                    using (new FetchSafelyFromPool<StringBuilder>(Utils.StringBuilderPool,
                                                                  out StringBuilder sbdDependencies))
                    {
                        foreach (DirectoryDependency dependency in objSelected.DependenciesList)
                            sbdDependencies.AppendLine(dependency.DisplayName);
                        await lblDependencies.DoThreadSafeAsync(x => x.Text = sbdDependencies.ToString());
                    }
                }
                else
                {
                    //Make sure all old information is discarded
                    await lblDependencies.DoThreadSafeAsync(x => x.Text = string.Empty);
                }

                if (objSelected.IncompatibilitiesList.Count > 0)
                {
                    using (new FetchSafelyFromPool<StringBuilder>(Utils.StringBuilderPool,
                                                                  out StringBuilder sbdIncompatibilities))
                    {
                        foreach (DirectoryDependency exclusivity in objSelected.IncompatibilitiesList)
                            sbdIncompatibilities.AppendLine(exclusivity.DisplayName);
                        await lblIncompatibilities.DoThreadSafeAsync(x => x.Text = sbdIncompatibilities.ToString());
                    }
                }
                else
                {
                    //Make sure all old information is discarded
                    await lblIncompatibilities.DoThreadSafeAsync(x => x.Text = string.Empty);
                }

                await gpbDirectoryInfo.DoThreadSafeAsync(x => x.Visible = true);
            }
            finally
            {
                await gpbDirectoryInfo.DoThreadSafeAsync(x => x.ResumeLayout());
            }
        }

        #endregion Control Events

        #region Methods

        private bool _blnPromptPdfReaderOnLoad;

        public async ValueTask DoLinkPdfReader(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (_blnLoading)
                _blnPromptPdfReaderOnLoad = true;
            else
                await PromptPdfAppPath(token);
        }

        private string _strSelectCodeOnRefresh = string.Empty;

        public async ValueTask DoLinkPdf(string strCode, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (_blnLoading)
                _strSelectCodeOnRefresh = strCode;
            else
            {
                bool blnDoPromptPdf = await lstGlobalSourcebookInfos.DoThreadSafeFuncAsync(x =>
                {
                    x.SelectedValue = strCode;
                    return x.SelectedIndex >= 0;
                }, token);
                if (blnDoPromptPdf)
                    await PromptPdfLocation(token);
            }
        }

        private async ValueTask PromptPdfLocation(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!await txtPDFLocation.DoThreadSafeFuncAsync(x => x.Enabled, token))
                return;
            // Prompt the user to select a save file to associate with this Contact.
            CursorWait objCursorWait = await CursorWait.NewAsync(this, token: token);
            try
            {
                string strNewFileName;
                using (OpenFileDialog dlgOpenFile = await this.DoThreadSafeFuncAsync(() => new OpenFileDialog(), token: token))
                {
                    dlgOpenFile.Filter = await LanguageManager.GetStringAsync("DialogFilter_Pdf") + '|' +
                                         await LanguageManager.GetStringAsync("DialogFilter_All");
                    if (!string.IsNullOrEmpty(txtPDFLocation.Text) && File.Exists(txtPDFLocation.Text))
                    {
                        dlgOpenFile.InitialDirectory = Path.GetDirectoryName(txtPDFLocation.Text);
                        dlgOpenFile.FileName = Path.GetFileName(txtPDFLocation.Text);
                    }

                    if (await this.DoThreadSafeFuncAsync(x => dlgOpenFile.ShowDialog(x), token: token) != DialogResult.OK)
                        return;

                    strNewFileName = dlgOpenFile.FileName;
                }

                try
                {
                    PdfReader objPdfReader = new PdfReader(strNewFileName);
                    objPdfReader.Close();
                }
                catch
                {
                    Program.ShowMessageBox(this, string.Format(
                                               await LanguageManager.GetStringAsync(
                                                   "Message_Options_FileIsNotPDF",
                                                   _strSelectedLanguage), Path.GetFileName(strNewFileName)),
                                           await LanguageManager.GetStringAsync(
                                               "MessageTitle_Options_FileIsNotPDF",
                                               _strSelectedLanguage), MessageBoxButtons.OK,
                                           MessageBoxIcon.Error);
                    return;
                }

                await UpdateSourcebookInfoPath(strNewFileName, token);
                await txtPDFLocation.DoThreadSafeAsync(x => x.Text = strNewFileName, token);
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async ValueTask PromptPdfAppPath(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            // Prompt the user to select a save file to associate with this Contact.
            CursorWait objCursorWait = await CursorWait.NewAsync(this, token: token);
            try
            {
                using (OpenFileDialog dlgOpenFile = await this.DoThreadSafeFuncAsync(() => new OpenFileDialog(), token))
                {
                    dlgOpenFile.Filter = await LanguageManager.GetStringAsync("DialogFilter_Exe") + '|' +
                                         await LanguageManager.GetStringAsync("DialogFilter_All");
                    string strPdfAppPath = await txtPDFAppPath.DoThreadSafeFuncAsync(x => x.Text, token);
                    if (!string.IsNullOrEmpty(strPdfAppPath) && File.Exists(strPdfAppPath))
                    {
                        dlgOpenFile.InitialDirectory = Path.GetDirectoryName(strPdfAppPath);
                        dlgOpenFile.FileName = Path.GetFileName(strPdfAppPath);
                    }

                    if (await this.DoThreadSafeFuncAsync(x => dlgOpenFile.ShowDialog(x), token) != DialogResult.OK)
                        return;
                    await txtPDFAppPath.DoThreadSafeAsync(x => x.Text = dlgOpenFile.FileName, token);
                }
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async ValueTask TranslateForm(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            await this.TranslateWinFormAsync(_strSelectedLanguage, token: token);
            await PopulateDefaultCharacterSettingLists(token);
            await PopulateMugshotCompressionOptions(token);
            await SetToolTips(token);

            await cboSheetLanguage.DoThreadSafeAsync(x =>
            {
                string strSheetLanguage = x.SelectedValue?.ToString();
                if (strSheetLanguage != _strSelectedLanguage
                    && x.Items.Cast<ListItem>().Any(y => y.Value.ToString() == _strSelectedLanguage))
                {
                    x.SelectedValue = _strSelectedLanguage;
                }
            }, token);

            await PopulatePdfParameters(token);
            await PopulateCustomDataDirectoryListBox(token);
            await PopulateApplicationInsightsOptions(token);
            await PopulateColorModes(token);
            await PopulateDpiScalingMethods(token);
        }

        private async ValueTask RefreshGlobalSourcebookInfosListView(CancellationToken token = default)
        {
            // Load the Sourcebook information.
            // Put the Sourcebooks into a List so they can first be sorted.
            using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool,
                                                           out List<ListItem> lstSourcebookInfos))
            {
                foreach (XPathNavigator objXmlBook in await (await XmlManager.LoadXPathAsync("books.xml", strLanguage: _strSelectedLanguage, token: token))
                             .SelectAndCacheExpressionAsync("/chummer/books/book"))
                {
                    string strCode = (await objXmlBook.SelectSingleNodeAndCacheExpressionAsync("code"))?.Value;
                    if (!string.IsNullOrEmpty(strCode))
                    {
                        ListItem objBookInfo
                            = new ListItem(
                                strCode,
                                (await objXmlBook.SelectSingleNodeAndCacheExpressionAsync("translate"))?.Value
                                ?? (await objXmlBook.SelectSingleNodeAndCacheExpressionAsync("name"))?.Value ?? strCode);
                        lstSourcebookInfos.Add(objBookInfo);
                    }
                }

                lstSourcebookInfos.Sort(CompareListItems.CompareNames);
                bool blnOldSkipRefresh = _blnSkipRefresh;
                _blnSkipRefresh = true;
                string strOldSelected = await lstGlobalSourcebookInfos.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token);
                await lstGlobalSourcebookInfos.PopulateWithListItemsAsync(lstSourcebookInfos, token);
                _blnSkipRefresh = blnOldSkipRefresh;
                await lstGlobalSourcebookInfos.DoThreadSafeAsync(x =>
                {
                    if (string.IsNullOrEmpty(strOldSelected))
                        x.SelectedIndex = -1;
                    else
                        x.SelectedValue = strOldSelected;
                }, token);
            }
        }

        private async ValueTask PopulateCustomDataDirectoryListBox(CancellationToken token = default)
        {
            bool blnOldSkipRefresh = _blnSkipRefresh;
            _blnSkipRefresh = true;
            ListItem objOldSelected = await lsbCustomDataDirectories.DoThreadSafeFuncAsync(x => x.SelectedIndex != -1 ? (ListItem)x.SelectedItem : ListItem.Blank, token);
            await lsbCustomDataDirectories.DoThreadSafeAsync(x => x.BeginUpdate(), token);
            try
            {
                await lsbCustomDataDirectories.DoThreadSafeAsync(x =>
                {
                    if (_setCustomDataDirectoryInfos.Count != x.Items.Count)
                    {
                        x.Items.Clear();
                        foreach (CustomDataDirectoryInfo objCustomDataDirectory in _setCustomDataDirectoryInfos)
                        {
                            ListItem objItem = new ListItem(objCustomDataDirectory, objCustomDataDirectory.Name);
                            x.Items.Add(objItem);
                        }
                    }
                    else
                    {
                        HashSet<CustomDataDirectoryInfo> setListedInfos = new HashSet<CustomDataDirectoryInfo>();
                        for (int iI = x.Items.Count - 1; iI >= 0; --iI)
                        {
                            ListItem objExistingItem = (ListItem) lsbCustomDataDirectories.Items[iI];
                            CustomDataDirectoryInfo objExistingInfo = (CustomDataDirectoryInfo) objExistingItem.Value;
                            if (!_setCustomDataDirectoryInfos.Contains(objExistingInfo))
                                x.Items.RemoveAt(iI);
                            else
                                setListedInfos.Add(objExistingInfo);
                        }

                        foreach (CustomDataDirectoryInfo objCustomDataDirectory in _setCustomDataDirectoryInfos.Where(
                                     y => !setListedInfos.Contains(y)))
                        {
                            ListItem objItem = new ListItem(objCustomDataDirectory, objCustomDataDirectory.Name);
                            x.Items.Add(objItem);
                        }
                    }

                    if (_blnLoading)
                    {
                        x.DisplayMember = nameof(ListItem.Name);
                        x.ValueMember = nameof(ListItem.Value);
                    }
                }, token);
            }
            finally
            {
                await lsbCustomDataDirectories.DoThreadSafeAsync(x => x.EndUpdate(), token);
            }
            _blnSkipRefresh = blnOldSkipRefresh;
            await lsbCustomDataDirectories.DoThreadSafeAsync(x => x.SelectedItem = objOldSelected, token);
        }

        /// <summary>
        /// Set the values for all of the controls based on the Options for the selected Setting.
        /// </summary>
        private async ValueTask PopulateOptions(CancellationToken token = default)
        {
            await RefreshGlobalSourcebookInfosListView(token);
            await PopulateCustomDataDirectoryListBox(token);

            await chkAutomaticUpdate.DoThreadSafeAsync(x => x.Checked = GlobalSettings.AutomaticUpdate, token);
            await chkPreferNightlyBuilds.DoThreadSafeAsync(x => x.Checked = GlobalSettings.PreferNightlyBuilds, token);
            await chkLiveCustomData.DoThreadSafeAsync(x => x.Checked = GlobalSettings.LiveCustomData, token);
            await chkLiveUpdateCleanCharacterFiles.DoThreadSafeAsync(x => x.Checked = GlobalSettings.LiveUpdateCleanCharacterFiles, token);
            await chkUseLogging.DoThreadSafeAsync(x => x.Checked = GlobalSettings.UseLogging, token);
            await cboUseLoggingApplicationInsights.DoThreadSafeAsync(x => x.Enabled = GlobalSettings.UseLogging, token);
            await PopulateApplicationInsightsOptions(token);
            await PopulateColorModes(token);
            await PopulateDpiScalingMethods(token);

            await chkLifeModule.DoThreadSafeAsync(x => x.Checked = GlobalSettings.LifeModuleEnabled, token);
            await chkStartupFullscreen.DoThreadSafeAsync(x => x.Checked = GlobalSettings.StartupFullscreen, token);
            await chkSingleDiceRoller.DoThreadSafeAsync(x => x.Checked = GlobalSettings.SingleDiceRoller, token);
            await chkDatesIncludeTime.DoThreadSafeAsync(x => x.Checked = GlobalSettings.DatesIncludeTime, token);
            await chkPrintToFileFirst.DoThreadSafeAsync(x => x.Checked = GlobalSettings.PrintToFileFirst, token);
            await chkPrintExpenses.DoThreadSafeAsync(x => x.Checked = GlobalSettings.PrintExpenses, token);
            await chkPrintFreeExpenses.DoThreadSafeAsync(x =>
            {
                x.Enabled = GlobalSettings.PrintExpenses;
                x.Checked = x.Enabled && GlobalSettings.PrintFreeExpenses;
            }, token);
            await chkPrintNotes.DoThreadSafeAsync(x => x.Checked = GlobalSettings.PrintNotes, token);
            await chkPrintSkillsWithZeroRating.DoThreadSafeAsync(x => x.Checked = GlobalSettings.PrintSkillsWithZeroRating, token);
            await nudBrowserVersion.DoThreadSafeAsync(x => x.Value = GlobalSettings.EmulatedBrowserVersion, token);
            bool blnEnabled = await txtPDFAppPath.DoThreadSafeFuncAsync(x =>
            {
                x.Text = GlobalSettings.PdfAppPath;
                return x.TextLength > 0;
            }, token);
            await cmdRemovePDFAppPath.DoThreadSafeAsync(x => x.Enabled = blnEnabled, token);
            blnEnabled = await txtCharacterRosterPath.DoThreadSafeFuncAsync(x =>
            {
                x.Text = GlobalSettings.CharacterRosterPath;
                return x.TextLength > 0;
            }, token);
            await cmdRemoveCharacterRoster.DoThreadSafeAsync(x => x.Enabled = blnEnabled, token);
            await chkHideMasterIndex.DoThreadSafeAsync(x => x.Checked = GlobalSettings.HideMasterIndex, token);
            await chkHideCharacterRoster.DoThreadSafeAsync(x => x.Checked = GlobalSettings.HideCharacterRoster, token);
            await chkCreateBackupOnCareer.DoThreadSafeAsync(x => x.Checked = GlobalSettings.CreateBackupOnCareer, token);
            await chkConfirmDelete.DoThreadSafeAsync(x => x.Checked = GlobalSettings.ConfirmDelete, token);
            await chkConfirmKarmaExpense.DoThreadSafeAsync(x => x.Checked = GlobalSettings.ConfirmKarmaExpense, token);
            await chkHideItemsOverAvail.DoThreadSafeAsync(x => x.Checked = GlobalSettings.HideItemsOverAvailLimit, token);
            await chkAllowHoverIncrement.DoThreadSafeAsync(x => x.Checked = GlobalSettings.AllowHoverIncrement, token);
            await chkSearchInCategoryOnly.DoThreadSafeAsync(x => x.Checked = GlobalSettings.SearchInCategoryOnly, token);
            await chkAllowSkillDiceRolling.DoThreadSafeAsync(x => x.Checked = GlobalSettings.AllowSkillDiceRolling, token);
            await chkAllowEasterEggs.DoThreadSafeAsync(x => x.Checked = GlobalSettings.AllowEasterEggs, token);
            await chkEnablePlugins.DoThreadSafeAsync(x => x.Checked = GlobalSettings.PluginsEnabled, token);
            await chkCustomDateTimeFormats.DoThreadSafeAsync(x => x.Checked = GlobalSettings.CustomDateTimeFormats, token);
            if (!await chkCustomDateTimeFormats.DoThreadSafeFuncAsync(x => x.Checked, token))
            {
                await txtDateFormat.DoThreadSafeAsync(x => x.Text = GlobalSettings.CultureInfo.DateTimeFormat.ShortDatePattern, token);
                await txtTimeFormat.DoThreadSafeAsync(x => x.Text = GlobalSettings.CultureInfo.DateTimeFormat.ShortTimePattern, token);
            }
            else
            {
                await txtDateFormat.DoThreadSafeAsync(x => x.Text = GlobalSettings.CustomDateFormat, token);
                await txtTimeFormat.DoThreadSafeAsync(x => x.Text = GlobalSettings.CustomTimeFormat, token);
            }
            await PluginsShowOrHide(await chkEnablePlugins.DoThreadSafeFuncAsync(x => x.Checked, token));
        }

        private async ValueTask SaveGlobalOptions(CancellationToken token = default)
        {
            GlobalSettings.AutomaticUpdate = await chkAutomaticUpdate.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.LiveCustomData = await chkLiveCustomData.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.LiveUpdateCleanCharacterFiles = await chkLiveUpdateCleanCharacterFiles.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.UseLogging = await chkUseLogging.DoThreadSafeFuncAsync(x => x.Checked, token);
            if (Enum.TryParse(await cboUseLoggingApplicationInsights.DoThreadSafeFuncAsync(x => x.SelectedValue.ToString(), token), out UseAILogging useAI))
                GlobalSettings.UseLoggingApplicationInsightsPreference = useAI;

            if (string.IsNullOrEmpty(_strSelectedLanguage))
            {
                // We have this set differently because changing the selected language also changes the selected default character sheet
                _strSelectedLanguage = GlobalSettings.DefaultLanguage;
                try
                {
                    _objSelectedCultureInfo = CultureInfo.GetCultureInfo(_strSelectedLanguage);
                }
                catch (CultureNotFoundException)
                {
                    _objSelectedCultureInfo = GlobalSettings.SystemCultureInfo;
                }
            }
            await GlobalSettings.SetLanguageAsync(_strSelectedLanguage, token);
            await GlobalSettings.SetColorModeSettingAsync(_eSelectedColorModeSetting, token);
            GlobalSettings.DpiScalingMethodSetting = await cboDpiScalingMethod.DoThreadSafeFuncAsync(x => x.SelectedIndex >= 0
                ? (DpiScalingMethod) Enum.Parse(typeof(DpiScalingMethod), x.SelectedValue.ToString())
                : GlobalSettings.DefaultDpiScalingMethod, token);
            GlobalSettings.StartupFullscreen = await chkStartupFullscreen.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.SingleDiceRoller = await chkSingleDiceRoller.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.DefaultCharacterSheet = await cboXSLT.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token) ?? GlobalSettings.DefaultCharacterSheetDefaultValue;
            GlobalSettings.DatesIncludeTime = await chkDatesIncludeTime.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.PrintToFileFirst = await chkPrintToFileFirst.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.PrintExpenses = await chkPrintExpenses.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.PrintFreeExpenses = await chkPrintFreeExpenses.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.PrintNotes = await chkPrintNotes.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.PrintSkillsWithZeroRating = await chkPrintSkillsWithZeroRating.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.EmulatedBrowserVersion = decimal.ToInt32(await nudBrowserVersion.DoThreadSafeFuncAsync(x => x.Value, token));
            GlobalSettings.PdfAppPath = await txtPDFAppPath.DoThreadSafeFuncAsync(x => x.Text, token);
            GlobalSettings.PdfParameters = await cboPDFParameters.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token) ?? string.Empty;
            GlobalSettings.LifeModuleEnabled = await chkLifeModule.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.PreferNightlyBuilds = await chkPreferNightlyBuilds.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.CharacterRosterPath = await txtCharacterRosterPath.DoThreadSafeFuncAsync(x => x.Text, token);
            GlobalSettings.HideMasterIndex = await chkHideMasterIndex.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.HideCharacterRoster = await chkHideCharacterRoster.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.CreateBackupOnCareer = await chkCreateBackupOnCareer.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.ConfirmDelete = await chkConfirmDelete.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.ConfirmKarmaExpense = await chkConfirmKarmaExpense.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.HideItemsOverAvailLimit = await chkHideItemsOverAvail.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.AllowHoverIncrement = await chkAllowHoverIncrement.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.SearchInCategoryOnly = await chkSearchInCategoryOnly.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.AllowSkillDiceRolling = await chkAllowSkillDiceRolling.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.DefaultCharacterSetting = await cboDefaultCharacterSetting.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token)
                                                   ?? GlobalSettings.DefaultCharacterSettingDefaultValue;
            GlobalSettings.DefaultMasterIndexSetting = await cboDefaultMasterIndexSetting.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token)
                                                      ?? GlobalSettings.DefaultMasterIndexSettingDefaultValue;
            GlobalSettings.AllowEasterEggs = await chkAllowEasterEggs.DoThreadSafeFuncAsync(x => x.Checked, token);
            GlobalSettings.PluginsEnabled = await chkEnablePlugins.DoThreadSafeFuncAsync(x => x.Checked, token);
            switch (await cboMugshotCompression.DoThreadSafeFuncAsync(x => x.SelectedValue, token))
            {
                case "jpeg_automatic":
                    GlobalSettings.SavedImageQuality = -1;
                    break;
                case "jpeg_manual":
                    GlobalSettings.SavedImageQuality = await nudMugshotCompressionQuality.DoThreadSafeFuncAsync(x => x.ValueAsInt, token);
                    break;
                default:
                    GlobalSettings.SavedImageQuality = int.MaxValue;
                    break;
            }
            GlobalSettings.CustomDateTimeFormats = await chkCustomDateTimeFormats.DoThreadSafeFuncAsync(x => x.Checked, token);
            if (GlobalSettings.CustomDateTimeFormats)
            {
                GlobalSettings.CustomDateFormat = await txtDateFormat.DoThreadSafeFuncAsync(x => x.Text, token);
                GlobalSettings.CustomTimeFormat = await txtTimeFormat.DoThreadSafeFuncAsync(x => x.Text, token);
            }

            GlobalSettings.CustomDataDirectoryInfos.Clear();
            foreach (CustomDataDirectoryInfo objInfo in _setCustomDataDirectoryInfos)
                GlobalSettings.CustomDataDirectoryInfos.Add(objInfo);
            await XmlManager.RebuildDataDirectoryInfoAsync(GlobalSettings.CustomDataDirectoryInfos);
            IAsyncDisposable objLocker = await GlobalSettings.SourcebookInfos.LockObject.EnterWriteLockAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                await GlobalSettings.SourcebookInfos.ClearAsync(token);
                foreach (SourcebookInfo objInfo in _dicSourcebookInfos.Values)
                    await GlobalSettings.SourcebookInfos.AddAsync(objInfo.Code, objInfo, token);
            }
            finally
            {
                await objLocker.DisposeAsync();
            }
        }

        /// <summary>
        /// Save the global settings to the registry.
        /// </summary>
        private async ValueTask SaveRegistrySettings()
        {
            await SaveGlobalOptions();
            await GlobalSettings.SaveOptionsToRegistry();
        }

        private async ValueTask PopulateDefaultCharacterSettingLists(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool,
                                                           out List<ListItem> lstCharacterSettings))
            {
                foreach (KeyValuePair<string, CharacterSettings> kvpLoopCharacterOptions in SettingsManager
                             .LoadedCharacterSettings)
                {
                    string strId = kvpLoopCharacterOptions.Key;
                    if (!string.IsNullOrEmpty(strId))
                    {
                        string strName = kvpLoopCharacterOptions.Value.Name;
                        if (strName.IsGuid() || (strName.StartsWith('{') && strName.EndsWith('}')))
                            strName = await LanguageManager.GetStringAsync(strName.TrimStartOnce('{').TrimEndOnce('}'),
                                                                           _strSelectedLanguage);
                        lstCharacterSettings.Add(new ListItem(strId, strName));
                    }
                }

                lstCharacterSettings.Sort(CompareListItems.CompareNames);

                string strOldSelectedDefaultCharacterSetting = await cboDefaultCharacterSetting.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token)
                                                               ?? GlobalSettings.DefaultCharacterSetting;
                
                await cboDefaultCharacterSetting.PopulateWithListItemsAsync(lstCharacterSettings, token);
                if (!string.IsNullOrEmpty(strOldSelectedDefaultCharacterSetting))
                {
                    await cboDefaultCharacterSetting.DoThreadSafeAsync(x =>
                    {
                        x.SelectedValue = strOldSelectedDefaultCharacterSetting;
                        if (x.SelectedIndex == -1 && lstCharacterSettings.Count > 0)
                            x.SelectedIndex = 0;
                    }, token);
                }

                string strOldSelectedDefaultMasterIndexSetting = await cboDefaultMasterIndexSetting.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token)
                                                                 ?? GlobalSettings.DefaultMasterIndexSetting;
                
                await cboDefaultMasterIndexSetting.PopulateWithListItemsAsync(lstCharacterSettings, token);
                if (!string.IsNullOrEmpty(strOldSelectedDefaultMasterIndexSetting))
                {
                    await cboDefaultMasterIndexSetting.DoThreadSafeAsync(x =>
                    {
                        x.SelectedValue = strOldSelectedDefaultMasterIndexSetting;
                        if (x.SelectedIndex == -1 && lstCharacterSettings.Count > 0)
                            x.SelectedIndex = 0;
                    }, token);
                }
            }
        }

        private async ValueTask PopulateMugshotCompressionOptions(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool,
                                                           out List<ListItem> lstMugshotCompressionOptions))
            {
                lstMugshotCompressionOptions.Add(
                    new ListItem("png", await LanguageManager.GetStringAsync("String_Lossless_Compression_Option")));
                lstMugshotCompressionOptions.Add(new ListItem("jpeg_automatic",
                                                              await LanguageManager.GetStringAsync(
                                                                  "String_Lossy_Automatic_Compression_Option")));
                lstMugshotCompressionOptions.Add(new ListItem("jpeg_manual",
                                                              await LanguageManager.GetStringAsync(
                                                                  "String_Lossy_Manual_Compression_Option")));

                string strOldSelected = await cboMugshotCompression.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token);

                if (_blnLoading)
                {
                    int intQuality = GlobalSettings.SavedImageQuality;
                    if (intQuality == int.MaxValue)
                    {
                        strOldSelected = "png";
                        intQuality = 90;
                    }
                    else if (intQuality < 0)
                    {
                        strOldSelected = "jpeg_automatic";
                        intQuality = 90;
                    }
                    else
                    {
                        strOldSelected = "jpeg_manual";
                    }

                    await nudMugshotCompressionQuality.DoThreadSafeAsync(x => x.ValueAsInt = intQuality, token);
                }
                
                await cboMugshotCompression.PopulateWithListItemsAsync(lstMugshotCompressionOptions, token);
                if (!string.IsNullOrEmpty(strOldSelected))
                {
                    await cboMugshotCompression.DoThreadSafeAsync(x =>
                    {
                        x.SelectedValue = strOldSelected;
                        if (x.SelectedIndex == -1 && lstMugshotCompressionOptions.Count > 0)
                            x.SelectedIndex = 0;
                    }, token);
                }
            }

            bool blnShowQualitySelector = Equals(await cboMugshotCompression.DoThreadSafeFuncAsync(x => x.SelectedValue, token), "jpeg_manual");
            await lblMugshotCompressionQuality.DoThreadSafeAsync(x => x.Visible = blnShowQualitySelector, token);
            await nudMugshotCompressionQuality.DoThreadSafeAsync(x => x.Visible = blnShowQualitySelector, token);
        }

        private async ValueTask PopulatePdfParameters(CancellationToken token = default)
        {
            int intIndex = 0;

            using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool,
                                                           out List<ListItem> lstPdfParameters))
            {
                foreach (XPathNavigator objXmlNode in await (await XmlManager.LoadXPathAsync("options.xml", strLanguage: _strSelectedLanguage, token: token))
                             .SelectAndCacheExpressionAsync(
                                 "/chummer/pdfarguments/pdfargument"))
                {
                    string strValue = (await objXmlNode.SelectSingleNodeAndCacheExpressionAsync("value"))?.Value;
                    lstPdfParameters.Add(new ListItem(
                                             strValue,
                                             (await objXmlNode.SelectSingleNodeAndCacheExpressionAsync("translate"))?.Value
                                             ?? (await objXmlNode.SelectSingleNodeAndCacheExpressionAsync("name"))?.Value
                                             ?? string.Empty));
                    if (!string.IsNullOrWhiteSpace(GlobalSettings.PdfParameters)
                        && GlobalSettings.PdfParameters == strValue)
                    {
                        intIndex = lstPdfParameters.Count - 1;
                    }
                }

                string strOldSelected = await cboPDFParameters.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token);
                await cboPDFParameters.PopulateWithListItemsAsync(lstPdfParameters, token);
                await cboPDFParameters.DoThreadSafeAsync(x =>
                {
                    x.SelectedIndex = intIndex;
                    if (!string.IsNullOrEmpty(strOldSelected))
                    {
                        x.SelectedValue = strOldSelected;
                        if (x.SelectedIndex == -1 && lstPdfParameters.Count > 0)
                            x.SelectedIndex = 0;
                    }
                }, token);
            }
        }

        private async ValueTask PopulateApplicationInsightsOptions(CancellationToken token = default)
        {
            string strOldSelected
                = await cboUseLoggingApplicationInsights.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token)
                  ?? GlobalSettings.UseLoggingApplicationInsights.ToString();
            
            using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool,
                                                           out List<ListItem> lstUseAIOptions))
            {
                foreach (UseAILogging eOption in Enum.GetValues(typeof(UseAILogging)))
                {
                    //we don't want to allow the user to set the logging options in stable builds to higher than "not set".
                    if (Utils.IsMilestoneVersion && !Debugger.IsAttached
                        && eOption > UseAILogging.NotSet)
                        continue;
                    lstUseAIOptions.Add(new ListItem(
                                            eOption,
                                            await LanguageManager.GetStringAsync("String_ApplicationInsights_" + eOption,
                                                _strSelectedLanguage)));
                }
                
                await cboUseLoggingApplicationInsights.PopulateWithListItemsAsync(lstUseAIOptions, token);
                await cboUseLoggingApplicationInsights.DoThreadSafeAsync(x =>
                {
                    if (!string.IsNullOrEmpty(strOldSelected))
                        x.SelectedValue = Enum.Parse(typeof(UseAILogging), strOldSelected);
                    if (x.SelectedIndex == -1 && lstUseAIOptions.Count > 0)
                        x.SelectedIndex = 0;
                }, token);
            }
        }

        private async ValueTask PopulateColorModes(CancellationToken token = default)
        {
            string strOldSelected = await cboColorMode.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token)
                                    ?? GlobalSettings.ColorModeSetting.ToString();
            
            using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool,
                                                           out List<ListItem> lstColorModes))
            {
                foreach (ColorMode eLoopColorMode in Enum.GetValues(typeof(ColorMode)))
                {
                    lstColorModes.Add(new ListItem(eLoopColorMode,
                                                   await LanguageManager.GetStringAsync(
                                                       "String_" + eLoopColorMode, _strSelectedLanguage)));
                }
                
                await cboColorMode.PopulateWithListItemsAsync(lstColorModes, token);
                await cboColorMode.DoThreadSafeAsync(x =>
                {
                    if (!string.IsNullOrEmpty(strOldSelected))
                        x.SelectedValue = Enum.Parse(typeof(ColorMode), strOldSelected);
                    if (x.SelectedIndex == -1 && lstColorModes.Count > 0)
                        x.SelectedIndex = 0;
                }, token);
            }
        }

        private async ValueTask PopulateDpiScalingMethods(CancellationToken token = default)
        {
            string strOldSelected = await cboDpiScalingMethod.DoThreadSafeFuncAsync(
                x => x.SelectedValue?.ToString() ?? GlobalSettings.DpiScalingMethodSetting.ToString(), token);
            
            using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool,
                                                           out List<ListItem> lstDpiScalingMethods))
            {
                foreach (DpiScalingMethod eLoopDpiScalingMethod in Enum.GetValues(typeof(DpiScalingMethod)))
                {
                    switch (eLoopDpiScalingMethod)
                    {
                        case DpiScalingMethod.Rescale:
                            if (Environment.OSVersion.Version
                                < new Version(
                                    6, 3, 0)) // Need at least Windows 8.1 to get PerMonitor/PerMonitorV2 Scaling
                                continue;
                            break;

                        case DpiScalingMethod.SmartZoom:
                            if (Environment.OSVersion.Version
                                < new Version(
                                    10, 0, 17763)) // Need at least Windows 10 Version 1809 to get GDI+ Scaling
                                continue;
                            break;
                    }

                    lstDpiScalingMethods.Add(new ListItem(eLoopDpiScalingMethod,
                                                          await LanguageManager.GetStringAsync(
                                                              "String_" + eLoopDpiScalingMethod,
                                                              _strSelectedLanguage)));
                }
                
                await cboDpiScalingMethod.PopulateWithListItemsAsync(lstDpiScalingMethods, token);
                await cboDpiScalingMethod.DoThreadSafeAsync(x =>
                {
                    if (!string.IsNullOrEmpty(strOldSelected))
                        x.SelectedValue = Enum.Parse(typeof(DpiScalingMethod), strOldSelected);
                    if (x.SelectedIndex == -1 && lstDpiScalingMethods.Count > 0)
                        x.SelectedIndex = 0;
                }, token);
            }
        }

        private async ValueTask SetToolTips(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            await cboUseLoggingApplicationInsights.SetToolTipAsync(string.Format(_objSelectedCultureInfo,
                                                                             await LanguageManager.GetStringAsync(
                                                                                 "Tip_Options_TelemetryId",
                                                                                 _strSelectedLanguage),
                                                                             Properties.Settings.Default.UploadClientId
                                                                                 .ToString(
                                                                                     "D",
                                                                                     GlobalSettings
                                                                                         .InvariantCultureInfo))
                                                                         .WordWrap(), token);
        }

        private async ValueTask PopulateLanguageList(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool, out List<ListItem> lstLanguages))
            {
                foreach (string filePath in Directory.EnumerateFiles(Utils.GetLanguageFolderPath, "*.xml"))
                {
                    token.ThrowIfCancellationRequested();
                    XPathDocument xmlDocument;
                    try
                    {
                        using (StreamReader objStreamReader = new StreamReader(filePath, Encoding.UTF8, true))
                        using (XmlReader objXmlReader
                               = XmlReader.Create(objStreamReader, GlobalSettings.SafeXmlReaderSettings))
                            xmlDocument = new XPathDocument(objXmlReader);
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch (XmlException)
                    {
                        continue;
                    }

                    token.ThrowIfCancellationRequested();

                    XPathNavigator node = await xmlDocument.CreateNavigator()
                                                           .SelectSingleNodeAndCacheExpressionAsync("/chummer/name");
                    if (node == null)
                        continue;

                    token.ThrowIfCancellationRequested();

                    lstLanguages.Add(new ListItem(Path.GetFileNameWithoutExtension(filePath), node.Value));
                }
                token.ThrowIfCancellationRequested();
                lstLanguages.Sort(CompareListItems.CompareNames);
                await cboLanguage.PopulateWithListItemsAsync(lstLanguages, token);
            }
        }

        private async ValueTask PopulateSheetLanguageList(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            using (new FetchSafelyFromPool<HashSet<string>>(Utils.StringHashSetPool,
                                                            out HashSet<string> setLanguagesWithSheets))
            {
                // Populate the XSL list with all of the manifested XSL files found in the sheets\[language] directory.
                foreach (XPathNavigator xmlSheetLanguage in await (await XmlManager.LoadXPathAsync("sheets.xml", token: token))
                             .SelectAndCacheExpressionAsync(
                                 "/chummer/sheets/@lang"))
                {
                    setLanguagesWithSheets.Add(xmlSheetLanguage.Value);
                }

                token.ThrowIfCancellationRequested();

                using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool,
                                                               out List<ListItem> lstSheetLanguages))
                {
                    foreach (string filePath in Directory.EnumerateFiles(Utils.GetLanguageFolderPath, "*.xml"))
                    {
                        token.ThrowIfCancellationRequested();
                        string strLanguageName = Path.GetFileNameWithoutExtension(filePath);
                        if (!setLanguagesWithSheets.Contains(strLanguageName))
                            continue;

                        XPathDocument xmlDocument;
                        try
                        {
                            using (StreamReader objStreamReader = new StreamReader(filePath, Encoding.UTF8, true))
                            using (XmlReader objXmlReader
                                   = XmlReader.Create(objStreamReader, GlobalSettings.SafeXmlReaderSettings))
                                xmlDocument = new XPathDocument(objXmlReader);
                        }
                        catch (IOException)
                        {
                            continue;
                        }
                        catch (XmlException)
                        {
                            continue;
                        }

                        token.ThrowIfCancellationRequested();

                        XPathNavigator node = await xmlDocument.CreateNavigator()
                                                               .SelectSingleNodeAndCacheExpressionAsync("/chummer/name");
                        if (node == null)
                            continue;

                        token.ThrowIfCancellationRequested();

                        lstSheetLanguages.Add(new ListItem(strLanguageName, node.Value));
                    }
                    token.ThrowIfCancellationRequested();
                    lstSheetLanguages.Sort(CompareListItems.CompareNames);
                    await cboSheetLanguage.PopulateWithListItemsAsync(lstSheetLanguages, token);
                }
            }
        }

        private async ValueTask PopulateXsltList(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            string strSelectedSheetLanguage = await cboSheetLanguage.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token);
            await imgSheetLanguageFlag.DoThreadSafeAsync(x => x.Image
                                                             = Math.Min(x.Width, x.Height)
                                                               >= 32
                                                                 ? FlagImageGetter.GetFlagFromCountryCode192Dpi(
                                                                     strSelectedSheetLanguage?.Substring(3, 2))
                                                                 : FlagImageGetter.GetFlagFromCountryCode(
                                                                     strSelectedSheetLanguage?.Substring(3, 2)), token);

            using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool, out List<ListItem> lstFiles))
            {
                // Populate the XSL list with all of the manifested XSL files found in the sheets\[language] directory.
                foreach (XPathNavigator xmlSheet in await (await XmlManager.LoadXPathAsync("sheets.xml", token: token))
                             .SelectAndCacheExpressionAsync(
                                 "/chummer/sheets[@lang="
                                 + GlobalSettings.Language.CleanXPath()
                                 + "]/sheet[not(hide)]"))
                {
                    string strFile = (await xmlSheet.SelectSingleNodeAndCacheExpressionAsync("filename"))?.Value ?? string.Empty;
                    lstFiles.Add(new ListItem(
                                     !GlobalSettings.Language.Equals(GlobalSettings.DefaultLanguage,
                                                                     StringComparison.OrdinalIgnoreCase)
                                         ? Path.Combine(GlobalSettings.Language, strFile)
                                         : strFile,
                                     (await xmlSheet.SelectSingleNodeAndCacheExpressionAsync("name"))?.Value ?? string.Empty));
                }
                string strOldSelected;
                try
                {
                    strOldSelected = await cboXSLT.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token) ?? string.Empty;
                }
                catch(IndexOutOfRangeException)
                {
                    strOldSelected = string.Empty;
                }
                 
                // Strip away the language prefix
                int intPos = strOldSelected.LastIndexOf(Path.DirectorySeparatorChar);
                if (intPos != -1)
                    strOldSelected = strOldSelected.Substring(intPos + 1);
                
                await cboXSLT.PopulateWithListItemsAsync(lstFiles, token);
                if (!string.IsNullOrEmpty(strOldSelected))
                {
                    await cboXSLT.DoThreadSafeAsync(x =>
                    {
                        x.SelectedValue =
                            !string.IsNullOrEmpty(strSelectedSheetLanguage) &&
                            !strSelectedSheetLanguage.Equals(GlobalSettings.DefaultLanguage,
                                                             StringComparison.OrdinalIgnoreCase)
                                ? Path.Combine(strSelectedSheetLanguage, strOldSelected)
                                : strOldSelected;
                        // If the desired sheet was not found, fall back to the Shadowrun 5 sheet.
                        if (x.SelectedIndex == -1 && lstFiles.Count > 0)
                        {
                            x.SelectedValue =
                                !string.IsNullOrEmpty(strSelectedSheetLanguage) &&
                                !strSelectedSheetLanguage.Equals(GlobalSettings.DefaultLanguage,
                                                                 StringComparison.OrdinalIgnoreCase)
                                    ? Path.Combine(strSelectedSheetLanguage,
                                                   GlobalSettings.DefaultCharacterSheetDefaultValue)
                                    : GlobalSettings.DefaultCharacterSheetDefaultValue;
                            if (x.SelectedIndex == -1)
                            {
                                x.SelectedIndex = 0;
                            }
                        }
                    }, token);
                }
            }
        }

        private Task SetDefaultValueForLanguageList(CancellationToken token = default)
        {
            return cboLanguage.DoThreadSafeAsync(x =>
            {
                x.SelectedValue = GlobalSettings.Language;
                if (x.SelectedIndex == -1)
                    x.SelectedValue = GlobalSettings.DefaultLanguage;
            }, token);
        }

        private Task SetDefaultValueForSheetLanguageList(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            string strDefaultCharacterSheet = GlobalSettings.DefaultCharacterSheet;
            if (string.IsNullOrEmpty(strDefaultCharacterSheet) || strDefaultCharacterSheet == "Shadowrun (Rating greater 0)")
                strDefaultCharacterSheet = GlobalSettings.DefaultCharacterSheetDefaultValue;

            string strDefaultSheetLanguage = GlobalSettings.Language;
            int intLastIndexDirectorySeparator = strDefaultCharacterSheet.LastIndexOf(Path.DirectorySeparatorChar);
            if (intLastIndexDirectorySeparator != -1)
            {
                string strSheetLanguage = strDefaultCharacterSheet.Substring(0, intLastIndexDirectorySeparator);
                if (strSheetLanguage.Length == 5)
                    strDefaultSheetLanguage = strSheetLanguage;
            }

            return cboSheetLanguage.DoThreadSafeAsync(x =>
            {
                x.SelectedValue = strDefaultSheetLanguage;
                if (x.SelectedIndex == -1)
                    x.SelectedValue = GlobalSettings.DefaultLanguage;
            }, token);
        }

        private Task SetDefaultValueForXsltList(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(GlobalSettings.DefaultCharacterSheet))
                GlobalSettings.DefaultCharacterSheet = GlobalSettings.DefaultCharacterSheetDefaultValue;
            return cboXSLT.DoThreadSafeAsync(x =>
            {
                x.SelectedValue = GlobalSettings.DefaultCharacterSheet;
                if (cboXSLT.SelectedValue == null && cboXSLT.Items.Count > 0)
                {
                    int intNameIndex;
                    string strLanguage = _strSelectedLanguage;
                    if (string.IsNullOrEmpty(strLanguage)
                        || strLanguage.Equals(GlobalSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                        intNameIndex = x.FindStringExact(GlobalSettings.DefaultCharacterSheet);
                    else
                        intNameIndex = x.FindStringExact(
                            GlobalSettings.DefaultCharacterSheet.Substring(
                                GlobalSettings.DefaultLanguage.LastIndexOf(Path.DirectorySeparatorChar) + 1));
                    x.SelectedIndex = Math.Max(0, intNameIndex);
                }
            }, token);
        }

        private async ValueTask UpdateSourcebookInfoPath(string strPath, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            string strTag = await lstGlobalSourcebookInfos.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token) ?? string.Empty;
            SourcebookInfo objFoundSource = _dicSourcebookInfos.ContainsKey(strTag) ? _dicSourcebookInfos[strTag] : null;
            token.ThrowIfCancellationRequested();
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

        private void OptionsChanged(object sender, EventArgs e)
        {
            if (!_blnLoading)
            {
                _blnDirty = true;
            }
        }

        private Task PluginsShowOrHide(bool show)
        {
            if (show)
            {
                return tabOptions.DoThreadSafeAsync(x =>
                {
                    if (!x.TabPages.Contains(tabPlugins))
                        x.TabPages.Add(tabPlugins);
                });
            }
            return tabOptions.DoThreadSafeAsync(x =>
            {
                if (x.TabPages.Contains(tabPlugins))
                    x.TabPages.Remove(tabPlugins);
            });
        }

        #endregion Methods

        private async void bScanForPDFs_Click(object sender, EventArgs e)
        {
            // Prompt the user to select a save file to associate with this Contact.
            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                Task<XPathNavigator> tskLoadBooks
                    = XmlManager.LoadXPathAsync("books.xml", strLanguage: _strSelectedLanguage);
                using (FolderBrowserDialog dlgSelectFolder = await this.DoThreadSafeFuncAsync(() => new FolderBrowserDialog()))
                {
                    dlgSelectFolder.ShowNewFolderButton = false;
                    if (await this.DoThreadSafeFuncAsync(x => dlgSelectFolder.ShowDialog(x)) != DialogResult.OK)
                        return;
                    if (string.IsNullOrWhiteSpace(dlgSelectFolder.SelectedPath))
                        return;
                    Stopwatch sw = new Stopwatch();
                    sw.Start();
                    XPathNavigator books = await tskLoadBooks;
                    string[] astrFiles = Directory.GetFiles(dlgSelectFolder.SelectedPath, "*.pdf");
                    XPathNodeIterator matches = books.Select("/chummer/books/book/matches/match[language = "
                                                             + _strSelectedLanguage.CleanXPath() + ']');
                    using (ThreadSafeForm<LoadingBar> frmLoadingBar
                           = await Program.CreateAndShowProgressBarAsync(dlgSelectFolder.SelectedPath, astrFiles.Length))
                    {
                        List<SourcebookInfo> list = await ScanFilesForPDFTexts(astrFiles, matches, frmLoadingBar.MyForm);
                        sw.Stop();
                        using (new FetchSafelyFromPool<StringBuilder>(Utils.StringBuilderPool,
                                                                      out StringBuilder sbdFeedback))
                        {
                            sbdFeedback.AppendLine().AppendLine()
                                       .AppendLine("-------------------------------------------------------------")
                                       .AppendFormat(GlobalSettings.InvariantCultureInfo,
                                                     "Scan for PDFs in Folder {0} completed in {1}ms.{2}{3} sourcebook(s) was/were found:",
                                                     dlgSelectFolder.SelectedPath, sw.ElapsedMilliseconds, Environment.NewLine,
                                                     list.Count).AppendLine().AppendLine();
                            foreach (SourcebookInfo sourcebook in list)
                            {
                                sbdFeedback.AppendFormat(GlobalSettings.InvariantCultureInfo,
                                                         "{0} with Offset {1} path: {2}", sourcebook.Code,
                                                         sourcebook.Offset, sourcebook.Path).AppendLine();
                            }

                            sbdFeedback.AppendLine()
                                       .AppendLine("-------------------------------------------------------------");
                            Log.Info(sbdFeedback.ToString());
                        }

                        string message = string.Format(_objSelectedCultureInfo,
                                                       await LanguageManager.GetStringAsync(
                                                           "Message_FoundPDFsInFolder", _strSelectedLanguage),
                                                       list.Count, dlgSelectFolder.SelectedPath);
                        string title
                            = await LanguageManager.GetStringAsync("MessageTitle_FoundPDFsInFolder",
                                                                   _strSelectedLanguage);
                        Program.ShowMessageBox(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async Task<List<SourcebookInfo>> ScanFilesForPDFTexts(IEnumerable<string> lstFiles, XPathNodeIterator matches, LoadingBar frmProgressBar)
        {
            // LockingDictionary makes sure we don't pick out multiple files for the same sourcebook
            LockingDictionary<string, SourcebookInfo>
                dicResults = new LockingDictionary<string, SourcebookInfo>();
            try
            {
                List<Task<SourcebookInfo>> lstLoadingTasks = new List<Task<SourcebookInfo>>(Utils.MaxParallelBatchSize);
                int intCounter = 0;
                foreach (string strFile in lstFiles)
                {
                    lstLoadingTasks.Add(GetSourcebookInfo(strFile));
                    if (++intCounter != Utils.MaxParallelBatchSize)
                        continue;
                    await Task.WhenAll(lstLoadingTasks);
                    foreach (Task<SourcebookInfo> tskLoop in lstLoadingTasks)
                    {
                        SourcebookInfo info = await tskLoop;
                        // ReSharper disable once AccessToDisposedClosure
                        if (info == null || await dicResults.ContainsKeyAsync(info.Code))
                            continue;
                        // ReSharper disable once AccessToDisposedClosure
                        await dicResults.TryAddAsync(info.Code, info);
                    }

                    intCounter = 0;
                    lstLoadingTasks.Clear();
                }

                await Task.WhenAll(lstLoadingTasks);
                foreach (Task<SourcebookInfo> tskLoop in lstLoadingTasks)
                {
                    SourcebookInfo info = await tskLoop;
                    // ReSharper disable once AccessToDisposedClosure
                    if (info == null || await dicResults.ContainsKeyAsync(info.Code))
                        continue;
                    // ReSharper disable once AccessToDisposedClosure
                    await dicResults.TryAddAsync(info.Code, info);
                }

                async Task<SourcebookInfo> GetSourcebookInfo(string strBookFile)
                {
                    FileInfo fileInfo = new FileInfo(strBookFile);
                    await frmProgressBar.PerformStepAsync(fileInfo.Name, LoadingBar.ProgressBarTextPatterns.Scanning);
                    return await ScanPDFForMatchingText(fileInfo, matches);
                }

                foreach (KeyValuePair<string, SourcebookInfo> kvpInfo in dicResults)
                    _dicSourcebookInfos[kvpInfo.Key] = kvpInfo.Value;

                return dicResults.Values.ToList();
            }
            finally
            {
                await dicResults.DisposeAsync();
            }
        }

        private static async ValueTask<SourcebookInfo> ScanPDFForMatchingText(FileSystemInfo fileInfo, XPathNodeIterator xmlMatches)
        {
            //Search the first 10 pages for all the text
            for (int intPage = 1; intPage <= 10; intPage++)
            {
                string text = GetPageTextFromPDF(fileInfo, intPage);
                if (string.IsNullOrEmpty(text))
                    continue;

                foreach (XPathNavigator xmlMatch in xmlMatches)
                {
                    string strLanguageText = (await xmlMatch.SelectSingleNodeAndCacheExpressionAsync("text"))?.Value ?? string.Empty;
                    if (!text.Contains(strLanguageText))
                        continue;
                    int trueOffset = intPage - (await xmlMatch.SelectSingleNodeAndCacheExpressionAsync("page"))?.ValueAsInt ?? 0;

                    xmlMatch.MoveToParent();
                    xmlMatch.MoveToParent();

                    return new SourcebookInfo
                    {
                        Code = (await xmlMatch.SelectSingleNodeAndCacheExpressionAsync("code"))?.Value,
                        Offset = trueOffset,
                        Path = fileInfo.FullName
                    };
                }
            }
            return null;

            string GetPageTextFromPDF(FileSystemInfo objInnerFileInfo, int intPage)
            {
                PdfReader objPdfReader = null;
                PdfDocument objPdfDocument = null;
                try
                {
                    try
                    {
                        objPdfReader = new PdfReader(objInnerFileInfo.FullName);
                        objPdfDocument = new PdfDocument(objPdfReader);
                    }
                    catch (iText.IO.Exceptions.IOException e)
                    {
                        if (e.Message == "PDF header not found.")
                            return string.Empty;
                        throw;
                    }
                    catch (Exception e)
                    {
                        //Loading failed, probably not a PDF file
                        Log.Warn(
                            e,
                            "Could not load file " + objInnerFileInfo.FullName
                                                   + " and open it as PDF to search for text.");
                        return null;
                    }

                    List<string> lstStringFromPdf = new List<string>(30);
                    // Loop through each page, starting at the listed page + offset.
                    if (intPage >= objPdfDocument.GetNumberOfPages())
                        return null;

                    int intProcessedStrings = lstStringFromPdf.Count;
                    try
                    {
                        // each page should have its own text extraction strategy for it to work properly
                        // this way we don't need to check for previous page appearing in the current page
                        // https://stackoverflow.com/questions/35911062/why-are-gettextfrompage-from-itextsharp-returning-longer-and-longer-strings
                        string strPageText = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(
                                                      objPdfDocument.GetPage(intPage),
                                                      new SimpleTextExtractionStrategy())
                                                  .CleanStylisticLigatures().NormalizeWhiteSpace()
                                                  .NormalizeLineEndings().CleanOfInvalidUnicodeChars();

                        // don't trust it to be correct, trim all whitespace and remove empty strings before we even start
                        lstStringFromPdf.AddRange(
                            strPageText.SplitNoAlloc(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                                       .Where(s => !string.IsNullOrWhiteSpace(s)).Select(x => x.Trim()));
                    }
                    // Need to catch all sorts of exceptions here just in case weird stuff happens in the scanner
                    catch (Exception e)
                    {
                        Utils.BreakIfDebug();
                        Log.Error(e);
                        return null;
                    }

                    using (new FetchSafelyFromPool<StringBuilder>(Utils.StringBuilderPool,
                                                                  out StringBuilder sbdAllLines))
                    {
                        for (int i = intProcessedStrings; i < lstStringFromPdf.Count; i++)
                        {
                            string strCurrentLine = lstStringFromPdf[i];
                            sbdAllLines.AppendLine(strCurrentLine);
                        }

                        return sbdAllLines.ToString();
                    }
                }
                finally
                {
                    objPdfDocument?.Close();
                    objPdfReader?.Close();
                }
            }
        }
    }
}
