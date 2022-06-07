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
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Xsl;
using Codaxy.WkHtmlToPdf;
using Microsoft.Win32;
using NLog;

namespace Chummer
{
    public partial class CharacterSheetViewer : Form, IHasCharacterObjects
    {
        private static Logger Log { get; } = LogManager.GetCurrentClassLogger();
        private readonly ThreadSafeList<Character> _lstCharacters = new ThreadSafeList<Character>();
        private XmlDocument _objCharacterXml = new XmlDocument { XmlResolver = null };
        private string _strSelectedSheet = GlobalSettings.DefaultCharacterSheet;
        private bool _blnLoading;
        private CultureInfo _objPrintCulture = GlobalSettings.CultureInfo;
        private string _strPrintLanguage = GlobalSettings.Language;
        private CancellationTokenSource _objRefresherCancellationTokenSource;
        private CancellationTokenSource _objOutputGeneratorCancellationTokenSource;
        private readonly CancellationTokenSource _objGenericFormClosingCancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _objGenericToken;
        private Task _tskRefresher;
        private Task _tskOutputGenerator;
        private readonly string _strTempSheetFilePath = Path.Combine(Utils.GetTempPath(), Path.GetRandomFileName() + ".htm");

        public IEnumerable<Character> CharacterObjects => _lstCharacters;

        #region Control Events

        public CharacterSheetViewer()
        {
            Program.MainForm.OpenCharacterSheetViewers.Add(this);
            _objGenericToken = _objGenericFormClosingCancellationTokenSource.Token;
            if (_strSelectedSheet.StartsWith("Shadowrun 4", StringComparison.Ordinal))
            {
                _strSelectedSheet = GlobalSettings.DefaultCharacterSheetDefaultValue;
            }
            if (!GlobalSettings.Language.Equals(GlobalSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
            {
                if (!_strSelectedSheet.Contains(Path.DirectorySeparatorChar))
                    _strSelectedSheet = Path.Combine(GlobalSettings.Language, _strSelectedSheet);
                else if (!_strSelectedSheet.Contains(GlobalSettings.Language) && _strSelectedSheet.Contains(GlobalSettings.Language.Substring(0, 2)))
                {
                    _strSelectedSheet = _strSelectedSheet.Replace(GlobalSettings.Language.Substring(0, 2), GlobalSettings.Language);
                }
            }
            else
            {
                int intLastIndexDirectorySeparator = _strSelectedSheet.LastIndexOf(Path.DirectorySeparatorChar);
                if (intLastIndexDirectorySeparator != -1 && _strSelectedSheet.Contains(GlobalSettings.Language.Substring(0, 2)))
                    _strSelectedSheet = _strSelectedSheet.Substring(intLastIndexDirectorySeparator + 1);
            }

            InitializeComponent();
            this.UpdateLightDarkMode();
            this.TranslateWinForm();
            ContextMenuStrip[] lstCmsToTranslate = {
                cmsPrintButton,
                cmsSaveButton
            };
            foreach (ContextMenuStrip objCms in lstCmsToTranslate)
            {
                if (objCms == null)
                    continue;
                foreach (ToolStripMenuItem tssItem in objCms.Items.OfType<ToolStripMenuItem>())
                {
                    tssItem.UpdateLightDarkMode();
                    tssItem.TranslateToolStripItemsRecursively();
                }
            }
        }

        private async void CharacterSheetViewer_Load(object sender, EventArgs e)
        {
            _blnLoading = true;
            // Populate the XSLT list with all of the XSL files found in the sheets directory.
            await LanguageManager.PopulateSheetLanguageListAsync(cboLanguage, _strSelectedSheet, _lstCharacters);
            await PopulateXsltList(_objGenericToken);

            await cboXSLT.DoThreadSafeAsync(x => x.SelectedValue = _strSelectedSheet, _objGenericToken);
            // If the desired sheet was not found, fall back to the Shadowrun 5 sheet.
            if (await cboXSLT.DoThreadSafeFuncAsync(x => x.SelectedIndex, _objGenericToken) == -1)
            {
                string strLanguage = await cboLanguage.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), _objGenericToken);
                int intNameIndex;
                if (string.IsNullOrEmpty(strLanguage)
                    || strLanguage.Equals(GlobalSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                    intNameIndex
                        = await cboXSLT.DoThreadSafeFuncAsync(
                            x => x.FindStringExact(GlobalSettings.DefaultCharacterSheet), _objGenericToken);
                else
                    intNameIndex = await cboXSLT.DoThreadSafeFuncAsync(
                        x => x.FindStringExact(GlobalSettings.DefaultCharacterSheet.Substring(
                                                   GlobalSettings.DefaultLanguage.LastIndexOf(
                                                       Path.DirectorySeparatorChar) + 1)), _objGenericToken);
                if (intNameIndex != -1)
                    await cboXSLT.DoThreadSafeAsync(x => x.SelectedIndex = intNameIndex, _objGenericToken);
                else if (await cboXSLT.DoThreadSafeFuncAsync(x => x.Items.Count > 0, _objGenericToken))
                {
                    if (string.IsNullOrEmpty(strLanguage) || strLanguage.Equals(GlobalSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                        _strSelectedSheet = GlobalSettings.DefaultCharacterSheetDefaultValue;
                    else
                        _strSelectedSheet = Path.Combine(strLanguage, GlobalSettings.DefaultCharacterSheetDefaultValue);
                    await cboXSLT.DoThreadSafeAsync(x =>
                    {
                        x.SelectedValue = _strSelectedSheet;
                        if (x.SelectedIndex == -1)
                        {
                            x.SelectedIndex = 0;
                            _strSelectedSheet = x.SelectedValue?.ToString();
                        }
                    }, _objGenericToken);
                }
            }
            _blnLoading = false;
            try
            {
                await SetDocumentText(await LanguageManager.GetStringAsync("String_Loading_Characters"),
                                      _objGenericToken);
            }
            catch (OperationCanceledException)
            {
                //swallow this
            }
        }

        /// <summary>
        /// Update the Window title to show the Character's name and unsaved changes status.
        /// </summary>
        private async Task UpdateWindowTitleAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (_lstCharacters.Count == 0)
            {
                await LanguageManager.GetStringAsync("Title_CharacterViewer")
                                     .ContinueWith(y => this.DoThreadSafeAsync(x => x.Text = y.Result, token), token)
                                     .Unwrap();
                return;
            }
            string strSpace = await LanguageManager.GetStringAsync("String_Space");
            string strCreate = await LanguageManager.GetStringAsync("Title_CreateNewCharacter");
            string strCareer = await LanguageManager.GetStringAsync("Title_CareerMode");
            StringBuilder sbdTitle = new StringBuilder(await LanguageManager.GetStringAsync("Title_CharacterViewer") + ':' + strSpace);
            sbdTitle.AppendJoin(',' + strSpace,
                                _lstCharacters.Select(x => x.CharacterName + strSpace + '-' + strSpace
                                                           + (x.Created ? strCareer : strCreate) + strSpace + '('
                                                           + x.Settings.Name + ')'));
            string strTitle = sbdTitle.ToString();
            await this.DoThreadSafeAsync(x => x.Text = strTitle, token);
        }

        private async void cboXSLT_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Re-generate the output when a new sheet is selected.
            if (!_blnLoading)
            {
                _strSelectedSheet = await cboXSLT.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString() ?? string.Empty, _objGenericToken);
                try
                {
                    await RefreshSheet(_objGenericToken);
                }
                catch (OperationCanceledException)
                {
                    //swallow this
                }
            }
        }

        private void cmdPrint_Click(object sender, EventArgs e)
        {
            webViewer.ShowPrintDialog();
        }

        private void tsPrintPreview_Click(object sender, EventArgs e)
        {
            webViewer.ShowPrintPreviewDialog();
        }

        private async void tsSaveAsHTML_Click(object sender, EventArgs e)
        {
            // Save the generated output as HTML.
            dlgSaveFile.Filter = await LanguageManager.GetStringAsync("DialogFilter_Html") + '|' + await LanguageManager.GetStringAsync("DialogFilter_All");
            dlgSaveFile.Title = await LanguageManager.GetStringAsync("Button_Viewer_SaveAsHtml");
            if (await this.DoThreadSafeFuncAsync(x => dlgSaveFile.ShowDialog(x), token: _objGenericToken) != DialogResult.OK)
                return;
            string strSaveFile = dlgSaveFile.FileName;

            if (string.IsNullOrEmpty(strSaveFile))
                return;

            if (!strSaveFile.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                && !strSaveFile.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                strSaveFile += ".htm";

            using (StreamWriter objWriter = new StreamWriter(strSaveFile, false, Encoding.UTF8))
                await objWriter.WriteAsync(await webViewer.DoThreadSafeFuncAsync(x => x.DocumentText, _objGenericToken));
        }

        private async void tsSaveAsXml_Click(object sender, EventArgs e)
        {
            // Save the printout XML generated by the character.
            dlgSaveFile.Filter = await LanguageManager.GetStringAsync("DialogFilter_Xml") + '|' + await LanguageManager.GetStringAsync("DialogFilter_All");
            dlgSaveFile.Title = await LanguageManager.GetStringAsync("Button_Viewer_SaveAsXml");
            if (await this.DoThreadSafeFuncAsync(x => dlgSaveFile.ShowDialog(x), _objGenericToken) != DialogResult.OK)
                return;
            string strSaveFile = dlgSaveFile.FileName;

            if (string.IsNullOrEmpty(strSaveFile))
                return;

            if (!strSaveFile.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                strSaveFile += ".xml";

            try
            {
                _objCharacterXml.Save(strSaveFile);
            }
            catch (XmlException)
            {
                Program.ShowMessageBox(this, await LanguageManager.GetStringAsync("Message_Save_Error_Warning"));
            }
            catch (UnauthorizedAccessException)
            {
                Program.ShowMessageBox(this, await LanguageManager.GetStringAsync("Message_Save_Error_Warning"));
            }
        }

        private async void CharacterSheetViewer_FormClosing(object sender, FormClosingEventArgs e)
        {
            CancellationTokenSource objTempTokenSource = Interlocked.Exchange(ref _objRefresherCancellationTokenSource, null);
            if (objTempTokenSource?.IsCancellationRequested == false)
            {
                objTempTokenSource.Cancel(false);
                objTempTokenSource.Dispose();
            }

            objTempTokenSource = Interlocked.Exchange(ref _objOutputGeneratorCancellationTokenSource, null);
            if (objTempTokenSource?.IsCancellationRequested == false)
            {
                objTempTokenSource.Cancel(false);
                objTempTokenSource.Dispose();
            }

            foreach (Character objCharacter in _lstCharacters)
            {
                objCharacter.PropertyChanged -= ObjCharacterOnPropertyChanged;
                objCharacter.SettingsPropertyChanged -= ObjCharacterOnSettingsPropertyChanged;
            }

            // Remove the mugshots directory when the form closes.
            // ReSharper disable once MethodSupportsCancellation
            await Utils.SafeDeleteDirectoryAsync(Path.Combine(Utils.GetStartupPath, "mugshots"), token: CancellationToken.None);

            try
            {
                await _tskRefresher;
            }
            catch (OperationCanceledException)
            {
                //swallow this
            }
            try
            {
                await _tskOutputGenerator;
            }
            catch (OperationCanceledException)
            {
                //swallow this
            }
            _objGenericFormClosingCancellationTokenSource?.Cancel(false);
        }

        private async void cmdSaveAsPdf_Click(object sender, EventArgs e)
        {
            try
            {
                CursorWait objCursorWait = await CursorWait.NewAsync(this, token: _objGenericToken);
                try
                {
                    string strPdfPrinter = string.Empty;
                    try
                    {
                        // Check to see if we have any "Print to PDF" printers, as they will be a lot more reliable than wkhtmltopdf
                        foreach (string strPrinter in PrinterSettings.InstalledPrinters)
                        {
                            if (strPrinter == "Microsoft Print to PDF"
                                || strPrinter == "Foxit Reader PDF Printer"
                                || strPrinter == "Adobe PDF")
                            {
                                strPdfPrinter = strPrinter;
                                break;
                            }
                        }
                    }
                    // This exception type is returned if PrinterSettings.InstalledPrinters fails
                    catch (Win32Exception)
                    {
                        //swallow this
                    }

                    if (!string.IsNullOrEmpty(strPdfPrinter))
                    {
                        DialogResult ePdfPrinterDialogResult = Program.ShowMessageBox(this,
                            string.Format(GlobalSettings.CultureInfo,
                                          await LanguageManager.GetStringAsync("Message_Viewer_FoundPDFPrinter"),
                                          strPdfPrinter),
                            await LanguageManager.GetStringAsync("MessageTitle_Viewer_FoundPDFPrinter"),
                            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
                        switch (ePdfPrinterDialogResult)
                        {
                            case DialogResult.Cancel:
                            case DialogResult.Yes when await DoPdfPrinterShortcut(strPdfPrinter):
                                return;

                            case DialogResult.Yes:
                                Program.ShowMessageBox(this,
                                                       await LanguageManager.GetStringAsync(
                                                           "Message_Viewer_PDFPrinterError"));
                                break;
                        }
                    }

                    // Save the generated output as PDF.
                    dlgSaveFile.Filter = await LanguageManager.GetStringAsync("DialogFilter_Pdf") + '|' +
                                             await LanguageManager.GetStringAsync("DialogFilter_All");
                    dlgSaveFile.Title = await LanguageManager.GetStringAsync("Button_Viewer_SaveAsPdf");
                    if (await this.DoThreadSafeFuncAsync(x => dlgSaveFile.ShowDialog(x), _objGenericToken) != DialogResult.OK)
                        return;
                    string strSaveFile = dlgSaveFile.FileName;

                    if (string.IsNullOrEmpty(strSaveFile))
                        return;

                    if (!strSaveFile.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        strSaveFile += ".pdf";

                    if (!Directory.Exists(Path.GetDirectoryName(strSaveFile)) || !Utils.CanWriteToPath(strSaveFile))
                    {
                        Program.ShowMessageBox(this,
                                               string.Format(GlobalSettings.CultureInfo,
                                                             await LanguageManager.GetStringAsync(
                                                                 "Message_File_Cannot_Be_Accessed"), strSaveFile));
                        return;
                    }

                    if (!await Utils.SafeDeleteFileAsync(strSaveFile, true, token: _objGenericToken))
                    {
                        Program.ShowMessageBox(this,
                                               string.Format(GlobalSettings.CultureInfo,
                                                             await LanguageManager.GetStringAsync(
                                                                 "Message_File_Cannot_Be_Accessed"), strSaveFile));
                        return;
                    }

                    // No PDF printer found, let's use wkhtmltopdf

                    try
                    {
                        PdfDocument objPdfDocument = new PdfDocument
                        {
                            Html = webViewer.DocumentText,
                            ExtraParams = new Dictionary<string, string>(8)
                            {
                                {"encoding", "UTF-8"},
                                {"dpi", "300"},
                                {"margin-top", "13"},
                                {"margin-bottom", "19"},
                                {"margin-left", "13"},
                                {"margin-right", "13"},
                                {"image-quality", "100"},
                                {"print-media-type", string.Empty}
                            }
                        };
                        PdfConvertEnvironment objPdfConvertEnvironment = new PdfConvertEnvironment
                            {WkHtmlToPdfPath = Path.Combine(Utils.GetStartupPath, "wkhtmltopdf.exe")};
                        PdfOutput objPdfOutput = new PdfOutput {OutputFilePath = strSaveFile};
                        await PdfConvert.ConvertHtmlToPdfAsync(objPdfDocument, objPdfConvertEnvironment, objPdfOutput);

                        if (!string.IsNullOrWhiteSpace(GlobalSettings.PdfAppPath))
                        {
                            Uri uriPath = new Uri(strSaveFile);
                            string strParams = GlobalSettings.PdfParameters
                                                             .Replace("{page}", "1")
                                                             .Replace("{localpath}", uriPath.LocalPath)
                                                             .Replace("{absolutepath}", uriPath.AbsolutePath);
                            ProcessStartInfo objPdfProgramProcess = new ProcessStartInfo
                            {
                                FileName = GlobalSettings.PdfAppPath,
                                Arguments = strParams,
                                WindowStyle = ProcessWindowStyle.Hidden
                            };
                            objPdfProgramProcess.Start();
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.ShowMessageBox(this, ex.ToString());
                    }
                }
                finally
                {
                    await objCursorWait.DisposeAsync();
                }
            }
            catch (OperationCanceledException)
            {
                //swallow this
            }
        }

        private async void cboLanguage_SelectedIndexChanged(object sender, EventArgs e)
        {
            _strPrintLanguage = cboLanguage.SelectedValue?.ToString() ?? GlobalSettings.Language;
            imgSheetLanguageFlag.Image = Math.Min(imgSheetLanguageFlag.Width, imgSheetLanguageFlag.Height) >= 32
                ? FlagImageGetter.GetFlagFromCountryCode192Dpi(_strPrintLanguage.Substring(3, 2))
                : FlagImageGetter.GetFlagFromCountryCode(_strPrintLanguage.Substring(3, 2));
            try
            {
                _objPrintCulture = CultureInfo.GetCultureInfo(_strPrintLanguage);
            }
            catch (CultureNotFoundException)
            {
                // Swallow this
            }

            if (!_blnLoading)
            {
                _blnLoading = true;
                string strOldSelected = _strSelectedSheet;
                // Strip away the language prefix
                if (strOldSelected.Contains(Path.DirectorySeparatorChar))
                    strOldSelected
                        = strOldSelected.Substring(strOldSelected.LastIndexOf(Path.DirectorySeparatorChar) + 1);
                await PopulateXsltList(_objGenericToken);
                string strNewLanguage = cboLanguage.SelectedValue?.ToString() ?? strOldSelected;
                if (strNewLanguage == strOldSelected)
                {
                    _strSelectedSheet
                        = strNewLanguage.Equals(GlobalSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase)
                            ? strOldSelected
                            : Path.Combine(strNewLanguage, strOldSelected);
                }

                cboXSLT.SelectedValue = _strSelectedSheet;
                // If the desired sheet was not found, fall back to the Shadowrun 5 sheet.
                if (cboXSLT.SelectedIndex == -1)
                {
                    int intNameIndex = cboXSLT.FindStringExact(
                        strNewLanguage.Equals(GlobalSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase)
                            ? GlobalSettings.DefaultCharacterSheet
                            : GlobalSettings.DefaultCharacterSheet.Substring(
                                strNewLanguage.LastIndexOf(Path.DirectorySeparatorChar) + 1));
                    if (intNameIndex != -1)
                        cboXSLT.SelectedIndex = intNameIndex;
                    else if (cboXSLT.Items.Count > 0)
                    {
                        _strSelectedSheet
                            = strNewLanguage.Equals(GlobalSettings.DefaultLanguage, StringComparison.OrdinalIgnoreCase)
                                ? GlobalSettings.DefaultCharacterSheetDefaultValue
                                : Path.Combine(strNewLanguage, GlobalSettings.DefaultCharacterSheetDefaultValue);
                        cboXSLT.SelectedValue = _strSelectedSheet;
                        if (cboXSLT.SelectedIndex == -1)
                        {
                            cboXSLT.SelectedIndex = 0;
                            _strSelectedSheet = cboXSLT.SelectedValue?.ToString();
                        }
                    }
                }

                _blnLoading = false;
                try
                {
                    await RefreshCharacters(_objGenericToken);
                }
                catch (OperationCanceledException)
                {
                    //swallow this
                }
            }
        }

        private async void CharacterSheetViewer_CursorChanged(object sender, EventArgs e)
        {
            try
            {
                if (UseWaitCursor)
                {
                    await Task.WhenAll(this.DoThreadSafeAsync(x =>
                                       {
                                           x.tsPrintPreview.Enabled = false;
                                           x.tsSaveAsHtml.Enabled = false;
                                       }, _objGenericToken),
                                       cmdPrint.DoThreadSafeAsync(x => x.Enabled = false, _objGenericToken),
                                       cmdSaveAsPdf.DoThreadSafeAsync(x => x.Enabled = false, _objGenericToken));
                }
                else
                {
                    await Task.WhenAll(this.DoThreadSafeAsync(x =>
                                       {
                                           x.tsPrintPreview.Enabled = true;
                                           x.tsSaveAsHtml.Enabled = true;
                                       }, _objGenericToken),
                                       cmdPrint.DoThreadSafeAsync(x => x.Enabled = true, _objGenericToken),
                                       cmdSaveAsPdf.DoThreadSafeAsync(x => x.Enabled = true, _objGenericToken));
                }
            }
            catch (OperationCanceledException)
            {
                //swallow this
            }
        }

        #endregion Control Events

        #region Methods

        /// <summary>
        /// Set the text of the viewer to something descriptive. Also disables the Print, Print Preview, Save as HTML, and Save as PDF buttons.
        /// </summary>
        private async ValueTask SetDocumentText(string strText, CancellationToken token = default)
        {
            int intHeight = await webViewer.DoThreadSafeFuncAsync(x => x.Height, token);
            string strDocumentText
                = "<html xmlns=\"http://www.w3.org/1999/xhtml\" xml:lang=\"en\" lang=\"en\"><head><meta http-equiv=\"x - ua - compatible\" content=\"IE = Edge\"/><meta charset = \"UTF-8\" /></head><body style=\"width:100%;height:" +
                  intHeight.ToString(GlobalSettings.InvariantCultureInfo) +
                  ";text-align:center;vertical-align:middle;font-family:segoe, tahoma,'trebuchet ms',arial;font-size:9pt;\">" +
                  strText.CleanForHtml() + "</body></html>";
            await webViewer.DoThreadSafeAsync(x => x.DocumentText = strDocumentText, token);
        }

        /// <summary>
        /// Asynchronously update the characters (and therefore content) of the Viewer window.
        /// </summary>
        private async ValueTask RefreshCharacters(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            CancellationTokenSource objTempTokenSource = Interlocked.Exchange(ref _objOutputGeneratorCancellationTokenSource, null);
            if (objTempTokenSource?.IsCancellationRequested == false)
            {
                objTempTokenSource.Cancel(false);
                objTempTokenSource.Dispose();
            }
            token.ThrowIfCancellationRequested();

            CancellationTokenSource objNewSource = new CancellationTokenSource();
            objTempTokenSource = Interlocked.Exchange(ref _objRefresherCancellationTokenSource, objNewSource);
            if (objTempTokenSource?.IsCancellationRequested == false)
            {
                objTempTokenSource.Cancel(false);
                objTempTokenSource.Dispose();
            }

            try
            {
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                Interlocked.CompareExchange(ref _objRefresherCancellationTokenSource, null, objNewSource);
                objNewSource.Dispose();
                throw;
            }

            try
            {
                if (_tskRefresher?.IsCompleted == false)
                    await _tskRefresher;
            }
            catch (OperationCanceledException)
            {
                // Swallow this
            }
            catch
            {
                Interlocked.CompareExchange(ref _objRefresherCancellationTokenSource, null, objNewSource);
                objNewSource.Dispose();
                throw;
            }

            CancellationToken objToken = objNewSource.Token;
            _tskRefresher = Task.Run(() => RefreshCharacterXml(objToken), objToken);
        }

        /// <summary>
        /// Asynchronously update the sheet of the Viewer window.
        /// </summary>
        private async ValueTask RefreshSheet(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            CancellationTokenSource objNewSource = new CancellationTokenSource();
            CancellationTokenSource objTempTokenSource = Interlocked.Exchange(ref _objOutputGeneratorCancellationTokenSource, objNewSource);
            if (objTempTokenSource?.IsCancellationRequested == false)
            {
                objTempTokenSource.Cancel(false);
                objTempTokenSource.Dispose();
            }

            try
            {
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                Interlocked.CompareExchange(ref _objOutputGeneratorCancellationTokenSource, null, objNewSource);
                objNewSource.Dispose();
                throw;
            }

            try
            {
                if (_tskOutputGenerator?.IsCompleted == false)
                    await _tskOutputGenerator;
            }
            catch (OperationCanceledException)
            {
                // Swallow this
            }
            catch
            {
                Interlocked.CompareExchange(ref _objOutputGeneratorCancellationTokenSource, null, objNewSource);
                objNewSource.Dispose();
                throw;
            }

            CancellationToken objToken = objNewSource.Token;
            _tskOutputGenerator = Task.Run(() => AsyncGenerateOutput(objToken), objToken);
        }

        /// <summary>
        /// Update the internal XML of the Viewer window.
        /// </summary>
        private async Task RefreshCharacterXml(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            CursorWait objCursorWait = await CursorWait.NewAsync(this, true, token);
            try
            {
                await Task.WhenAll(this.DoThreadSafeAsync(x =>
                                   {
                                       x.tsPrintPreview.Enabled = false;
                                       x.tsSaveAsHtml.Enabled = false;
                                   }, token),
                                   cmdPrint.DoThreadSafeAsync(x => x.Enabled = false, token),
                                   cmdSaveAsPdf.DoThreadSafeAsync(x => x.Enabled = false, token));
                token.ThrowIfCancellationRequested();
                Character[] aobjCharacters = await _lstCharacters.ToArrayAsync();
                token.ThrowIfCancellationRequested();
                _objCharacterXml = aobjCharacters.Length > 0
                    ? await CommonFunctions.GenerateCharactersExportXml(_objPrintCulture, _strPrintLanguage,
                                                                        _objRefresherCancellationTokenSource.Token,
                                                                        aobjCharacters)
                    : null;
                token.ThrowIfCancellationRequested();
                await this.DoThreadSafeAsync(x => x.tsSaveAsXml.Enabled = _objCharacterXml != null, token);
                token.ThrowIfCancellationRequested();
                await RefreshSheet(token);
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        /// <summary>
        /// Run the generated XML file through the XSL transformation engine to create the file output.
        /// </summary>
        private async Task AsyncGenerateOutput(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            CursorWait objCursorWait = await CursorWait.NewAsync(this, token: token);
            try
            {
                await Task.WhenAll(this.DoThreadSafeAsync(x =>
                                   {
                                       x.tsPrintPreview.Enabled = false;
                                       x.tsSaveAsHtml.Enabled = false;
                                   }, token),
                                   cmdPrint.DoThreadSafeAsync(x => x.Enabled = false, token),
                                   cmdSaveAsPdf.DoThreadSafeAsync(x => x.Enabled = false, token));
                token.ThrowIfCancellationRequested();
                await SetDocumentText(await LanguageManager.GetStringAsync("String_Generating_Sheet"), token);
                token.ThrowIfCancellationRequested();
                string strXslPath = Path.Combine(Utils.GetStartupPath, "sheets", _strSelectedSheet + ".xsl");
                if (!File.Exists(strXslPath))
                {
                    string strReturn = "File not found when attempting to load " + _strSelectedSheet +
                                       Environment.NewLine;
                    Log.Debug(strReturn);
                    Program.ShowMessageBox(this, strReturn);
                    return;
                }

                token.ThrowIfCancellationRequested();
                XslCompiledTransform objXslTransform;
                try
                {
                    objXslTransform = await XslManager.GetTransformForFileAsync(strXslPath);
                }
                catch (ArgumentException)
                {
                    token.ThrowIfCancellationRequested();
                    string strReturn = "Last write time could not be fetched when attempting to load "
                                       + _strSelectedSheet +
                                       Environment.NewLine;
                    Log.Debug(strReturn);
                    Program.ShowMessageBox(this, strReturn);
                    return;
                }
                catch (PathTooLongException)
                {
                    token.ThrowIfCancellationRequested();
                    string strReturn = "Last write time could not be fetched when attempting to load "
                                       + _strSelectedSheet +
                                       Environment.NewLine;
                    Log.Debug(strReturn);
                    Program.ShowMessageBox(this, strReturn);
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    token.ThrowIfCancellationRequested();
                    string strReturn = "Last write time could not be fetched when attempting to load "
                                       + _strSelectedSheet +
                                       Environment.NewLine;
                    Log.Debug(strReturn);
                    Program.ShowMessageBox(this, strReturn);
                    return;
                }
                catch (XsltException ex)
                {
                    token.ThrowIfCancellationRequested();
                    string strReturn = "Error attempting to load " + _strSelectedSheet + Environment.NewLine;
                    Log.Debug(strReturn);
                    Log.Error("ERROR Message = " + ex.Message);
                    strReturn += ex.Message;
                    Program.ShowMessageBox(this, strReturn);
                    return;
                }

                token.ThrowIfCancellationRequested();

                XmlWriterSettings objSettings = objXslTransform.OutputSettings?.Clone();
                if (objSettings != null)
                {
                    objSettings.CheckCharacters = false;
                    objSettings.ConformanceLevel = ConformanceLevel.Fragment;
                }

                using (MemoryStream objStream = new MemoryStream())
                {
                    using (XmlWriter objWriter = objSettings != null
                               ? XmlWriter.Create(objStream, objSettings)
                               : Utils.GetXslTransformXmlWriter(objStream))
                    {
                        token.ThrowIfCancellationRequested();
                        await Task.Run(() => objXslTransform.Transform(_objCharacterXml, objWriter), token);
                    }

                    token.ThrowIfCancellationRequested();

                    objStream.Position = 0;

                    // This reads from a static file, outputs to an HTML file, then has the browser read from that file. For debugging purposes.
                    //objXSLTransform.Transform("D:\\temp\\print.xml", "D:\\temp\\output.htm");
                    //webBrowser1.Navigate("D:\\temp\\output.htm");

                    if (GlobalSettings.PrintToFileFirst)
                    {
                        // The DocumentStream method fails when using Wine, so we'll instead dump everything out a temporary HTML file, have the WebBrowser load that, then delete the temporary file.

                        // Delete any old versions of the file
                        if (!await Utils.SafeDeleteFileAsync(_strTempSheetFilePath, true, token: token))
                            return;

                        // Read in the resulting code and pass it to the browser.
                        using (StreamReader objReader = new StreamReader(objStream, Encoding.UTF8, true))
                        {
                            string strOutput = await objReader.ReadToEndAsync();
                            File.WriteAllText(_strTempSheetFilePath, strOutput);
                        }

                        token.ThrowIfCancellationRequested();
                        await this.DoThreadSafeAsync(x => x.UseWaitCursor = true, token);
                        await webViewer.DoThreadSafeAsync(
                            x => x.Url = new Uri("file:///" + _strTempSheetFilePath), token);
                        token.ThrowIfCancellationRequested();
                    }
                    else
                    {
                        token.ThrowIfCancellationRequested();
                        // Populate the browser using DocumentText (DocumentStream would cause issues due to stream disposal).
                        using (StreamReader objReader = new StreamReader(objStream, Encoding.UTF8, true))
                        {
                            string strOutput = await objReader.ReadToEndAsync();
                            token.ThrowIfCancellationRequested();
                            await this.DoThreadSafeAsync(x => x.UseWaitCursor = true, token);
                            await webViewer.DoThreadSafeAsync(x => x.DocumentText = strOutput, token);
                            token.ThrowIfCancellationRequested();
                        }
                    }
                }
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async void webViewer_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            try
            {
                await this.DoThreadSafeAsync(x => x.UseWaitCursor = false, _objGenericToken);
                if (_tskOutputGenerator?.IsCompleted == true && _tskRefresher?.IsCompleted == true)
                {
                    await Task.WhenAll(this.DoThreadSafeAsync(x =>
                                       {
                                           x.tsPrintPreview.Enabled = true;
                                           x.tsSaveAsHtml.Enabled = true;
                                       }, _objGenericToken),
                                       cmdPrint.DoThreadSafeAsync(x => x.Enabled = true, _objGenericToken),
                                       cmdSaveAsPdf.DoThreadSafeAsync(x => x.Enabled = true, _objGenericToken));
                }
            }
            catch (OperationCanceledException)
            {
                //swallow this
            }
        }

        private async ValueTask<bool> DoPdfPrinterShortcut(string strPdfPrinterName)
        {
            // We've got a proper, built-in PDF printer, so let's use that instead of wkhtmltopdf
            string strOldHeader = null;
            string strOldFooter = null;
            string strOldPrintBackground = null;
            string strOldShrinkToFit = null;
            string strOldDefaultPrinter = null;
            try
            {
                strOldDefaultPrinter = NativeMethods.GetDefaultPrinter();
                // Try to remove headers and footers from the printer and set default printer settings to be conducive to sheet printing
                try
                {
                    using (RegistryKey objKey =
                        Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Internet Explorer\\PageSetup", true))
                    {
                        if (objKey != null)
                        {
                            strOldHeader = objKey.GetValue("header")?.ToString();
                            objKey.SetValue("header", string.Empty);
                            strOldFooter = objKey.GetValue("footer")?.ToString();
                            objKey.SetValue("footer", string.Empty);
                            strOldPrintBackground = objKey.GetValue("Print_Background")?.ToString();
                            objKey.SetValue("Print_Background", "yes");
                            strOldShrinkToFit = objKey.GetValue("Shrink_To_Fit")?.ToString();
                            objKey.SetValue("Shrink_To_Fit", "yes");
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Swallow this
                }
                catch (IOException)
                {
                    // Swallow this
                }
                catch (SecurityException)
                {
                    // Swallow this
                }

                // webBrowser can only print to the default printer, so we (temporarily) change it to the PDF printer
                if (NativeMethods.SetDefaultPrinter(strPdfPrinterName))
                {
                    // There is also no way to silently have it print to a PDF, so we have to show the print dialog
                    // and have the user click through, though the PDF printer will be temporarily set as their default
                    await webViewer.DoThreadSafeAsync(x => x.ShowPrintDialog(), _objGenericToken);
                }
            }
            catch
            {
                // Error of some kind occurred, proceed to use wkhtmltopdf instead
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(strOldDefaultPrinter))
                    NativeMethods.SetDefaultPrinter(strOldDefaultPrinter);
                // Try to remove headers and footers from the printer and
                try
                {
                    using (RegistryKey objKey =
                        Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Internet Explorer\\PageSetup", true))
                    {
                        if (objKey != null)
                        {
                            if (strOldHeader != null)
                                objKey.SetValue("header", strOldHeader);
                            if (strOldFooter != null)
                                objKey.SetValue("footer", strOldFooter);
                            if (strOldPrintBackground != null)
                                objKey.SetValue("Print_Background", strOldPrintBackground);
                            if (strOldShrinkToFit != null)
                                objKey.SetValue("Shrink_To_Fit", strOldShrinkToFit);
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Swallow this
                }
                catch (IOException)
                {
                    // Swallow this
                }
                catch (SecurityException)
                {
                    // Swallow this
                }
            }

            return true;
        }

        private async ValueTask PopulateXsltList(CancellationToken token = default)
        {
            List<ListItem> lstFiles = await XmlManager.GetXslFilesFromLocalDirectoryAsync(
                await cboLanguage.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token)
                ?? GlobalSettings.DefaultLanguage, _lstCharacters, true);
            try
            {
                await cboXSLT.PopulateWithListItemsAsync(lstFiles, token);
            }
            finally
            {
                Utils.ListItemListPool.Return(lstFiles);
            }
        }

        /// <summary>
        /// Set the XSL sheet that will be selected by default.
        /// </summary>
        public ValueTask SetSelectedSheet(string strSheet, CancellationToken token = default)
        {
            _strSelectedSheet = strSheet;
            return RefreshSheet(token);
        }

        /// <summary>
        /// Set List of Characters to print.
        /// </summary>
        public async Task SetCharacters(CancellationToken token = default, params Character[] lstCharacters)
        {
            token.ThrowIfCancellationRequested();
            IAsyncDisposable objLocker = await _lstCharacters.LockObject.EnterWriteLockAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                foreach (Character objCharacter in _lstCharacters)
                {
                    objCharacter.PropertyChanged -= ObjCharacterOnPropertyChanged;
                    objCharacter.SettingsPropertyChanged -= ObjCharacterOnSettingsPropertyChanged;
                }
                await _lstCharacters.ClearAsync();
                if (lstCharacters != null)
                    await _lstCharacters.AddRangeAsync(lstCharacters);
                foreach (Character objCharacter in _lstCharacters)
                {
                    objCharacter.PropertyChanged += ObjCharacterOnPropertyChanged;
                    objCharacter.SettingsPropertyChanged += ObjCharacterOnSettingsPropertyChanged;
                }
            }
            finally
            {
                await objLocker.DisposeAsync();
            }

            await UpdateWindowTitleAsync(token);

            token.ThrowIfCancellationRequested();
            bool blnOldLoading = _blnLoading;
            try
            {
                _blnLoading = true;
                // Populate the XSLT list with all of the XSL files found in the sheets directory.
                await LanguageManager.PopulateSheetLanguageListAsync(cboLanguage, _strSelectedSheet, _lstCharacters);
                await PopulateXsltList(token);
                await RefreshCharacters(token);
            }
            finally
            {
                _blnLoading = blnOldLoading;
            }
        }

        private async void ObjCharacterOnSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == nameof(CharacterSettings.Name))
                    await UpdateWindowTitleAsync(_objGenericToken);
            }
            catch (OperationCanceledException)
            {
                //swallow this
            }
        }

        private async void ObjCharacterOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == nameof(Character.CharacterName) || e.PropertyName == nameof(Character.Created))
                    await UpdateWindowTitleAsync(_objGenericToken);
                await RefreshCharacters(_objGenericToken);
            }
            catch (OperationCanceledException)
            {
                //swallow this
            }
        }

        #endregion Methods
    }
}
