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
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.XPath;
using NLog;

namespace Chummer
{
    public partial class EditCharacterSettings : Form
    {
        private static Logger Log { get; } = LogManager.GetCurrentClassLogger();
        private readonly CharacterSettings _objCharacterSettings;
        private CharacterSettings _objReferenceCharacterSettings;
        private readonly List<ListItem> _lstSettings = Utils.ListItemListPool.Get();

        // List of custom data directory infos on the character, in load order. If the character has a directory name for which we have no info, key will be a string instead of an info
        private readonly TypedOrderedDictionary<object, bool> _dicCharacterCustomDataDirectoryInfos = new TypedOrderedDictionary<object, bool>();

        private bool _blnLoading = true;
        private bool _blnSkipLimbCountUpdate;
        private bool _blnDirty;
        private bool _blnSourcebookToggle = true;
        private bool _blnWasRenamed;
        private bool _blnIsLayoutSuspended = true;
        private bool _blnForceMasterIndexRepopulateOnClose;

        // Used to revert to old selected setting if user cancels out of selecting a different one
        private int _intOldSelectedSettingIndex = -1;

        private readonly HashSet<string> _setPermanentSourcebooks = Utils.StringHashSetPool.Get();

        #region Form Events

        public EditCharacterSettings(CharacterSettings objExistingSettings = null)
        {
            InitializeComponent();
            this.UpdateLightDarkMode();
            this.TranslateWinForm();
            _objReferenceCharacterSettings = objExistingSettings;
            if (_objReferenceCharacterSettings == null)
            {
                if (SettingsManager.LoadedCharacterSettings.TryGetValue(GlobalSettings.DefaultCharacterSetting,
                                                                        out CharacterSettings objSetting))
                    _objReferenceCharacterSettings = objSetting;
                else if (SettingsManager.LoadedCharacterSettings.TryGetValue(
                    GlobalSettings.DefaultCharacterSettingDefaultValue,
                    out objSetting))
                    _objReferenceCharacterSettings = objSetting;
                else
                    _objReferenceCharacterSettings = SettingsManager.LoadedCharacterSettings.Values.First();
            }
            _objCharacterSettings = new CharacterSettings(_objReferenceCharacterSettings);
            _objCharacterSettings.PropertyChanged += SettingsChanged;
            RebuildCustomDataDirectoryInfos();
        }

        private async void EditCharacterSettings_Load(object sender, EventArgs e)
        {
            await SetToolTips();
            await PopulateSettingsList();

            using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool, out List<ListItem> lstBuildMethods))
            {
                lstBuildMethods.Add(new ListItem(CharacterBuildMethod.Priority, await LanguageManager.GetStringAsync("String_Priority")));
                lstBuildMethods.Add(new ListItem(CharacterBuildMethod.SumtoTen, await LanguageManager.GetStringAsync("String_SumtoTen")));
                lstBuildMethods.Add(new ListItem(CharacterBuildMethod.Karma, await LanguageManager.GetStringAsync("String_Karma")));
                if (GlobalSettings.LifeModuleEnabled)
                    lstBuildMethods.Add(new ListItem(CharacterBuildMethod.LifeModule,
                                                     await LanguageManager.GetStringAsync("String_LifeModule")));

                await cboBuildMethod.PopulateWithListItemsAsync(lstBuildMethods);
            }

            await PopulateOptions();
            SetupDataBindings();

            IsDirty = false;
            _blnLoading = false;
            _blnIsLayoutSuspended = false;
        }

        #endregion Form Events

        #region Control Events

        private async void cmdGlobalOptionsCustomData_Click(object sender, EventArgs e)
        {
            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                using (ThreadSafeForm<EditGlobalSettings> frmOptions =
                       await ThreadSafeForm<EditGlobalSettings>.GetAsync(() =>
                                                                             new EditGlobalSettings(
                                                                                 "tabCustomDataDirectories")))
                    await frmOptions.ShowDialogSafeAsync(this);
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async void cmdRename_Click(object sender, EventArgs e)
        {
            string strRename = await LanguageManager.GetStringAsync("Message_CharacterOptions_SettingRename");
            using (ThreadSafeForm<SelectText> frmSelectName = await ThreadSafeForm<SelectText>.GetAsync(() => new SelectText
                   {
                       DefaultString = _objCharacterSettings.Name,
                       Description = strRename
                   }))
            {
                if (await frmSelectName.ShowDialogSafeAsync(this) != DialogResult.OK)
                    return;
                _objCharacterSettings.Name = frmSelectName.MyForm.SelectedValue;
            }

            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                bool blnDoResumeLayout = !_blnIsLayoutSuspended;
                if (blnDoResumeLayout)
                {
                    _blnIsLayoutSuspended = true;
                    await this.DoThreadSafeAsync(x => x.SuspendLayout());
                }

                try
                {
                    int intCurrentSelectedSettingIndex = await cboSetting.DoThreadSafeFuncAsync(x => x.SelectedIndex);
                    if (intCurrentSelectedSettingIndex >= 0)
                    {
                        ListItem objNewListItem = new ListItem(_lstSettings[intCurrentSelectedSettingIndex].Value,
                                                               _objCharacterSettings.DisplayName);
                        _blnLoading = true;
                        _lstSettings[intCurrentSelectedSettingIndex] = objNewListItem;
                        await cboSetting.PopulateWithListItemsAsync(_lstSettings);
                        await cboSetting.DoThreadSafeAsync(x => x.SelectedIndex = intCurrentSelectedSettingIndex);
                        _blnLoading = false;
                    }

                    _blnWasRenamed = true;
                    IsDirty = true;
                }
                finally
                {
                    if (blnDoResumeLayout)
                    {
                        _blnIsLayoutSuspended = false;
                        await this.DoThreadSafeAsync(x => x.ResumeLayout());
                    }
                }

                _intOldSelectedSettingIndex = await cboSetting.DoThreadSafeFuncAsync(x => x.SelectedIndex);
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async void cmdDelete_Click(object sender, EventArgs e)
        {
            // Verify that the user wants to delete this setting
            if (Program.ShowMessageBox(
                string.Format(GlobalSettings.CultureInfo, await LanguageManager.GetStringAsync("Message_CharacterOptions_ConfirmDelete"),
                    _objReferenceCharacterSettings.Name),
                await LanguageManager.GetStringAsync("MessageTitle_CharacterOptions_ConfirmDelete"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                (bool blnSuccess, CharacterSettings objDeletedSettings)
                    = await SettingsManager.LoadedCharacterSettingsAsModifiable.TryRemoveAsync(
                        _objReferenceCharacterSettings.DictionaryKey);
                if (!blnSuccess)
                    return;
                if (!await Utils.SafeDeleteFileAsync(
                        Path.Combine(Utils.GetStartupPath, "settings", _objReferenceCharacterSettings.FileName), true))
                {
                    // Revert removal of setting if we cannot delete the file
                    await SettingsManager.LoadedCharacterSettingsAsModifiable.AddAsync(
                        objDeletedSettings.DictionaryKey, objDeletedSettings);
                    return;
                }

                // Force repopulate character settings list in Master Index from here in lieu of event handling for concurrent dictionaries
                _blnForceMasterIndexRepopulateOnClose = true;
                KeyValuePair<string, CharacterSettings> kvpReplacementOption
                    = await SettingsManager.LoadedCharacterSettings.FirstOrDefaultAsync(
                        x => x.Value.BuiltInOption
                             && x.Value.BuildMethod == _objReferenceCharacterSettings.BuildMethod);
                foreach (Character objCharacter in Program.OpenCharacters.Where(x =>
                             x.SettingsKey == _objReferenceCharacterSettings.FileName))
                    objCharacter.SettingsKey = kvpReplacementOption.Key;
                bool blnDoResumeLayout = !_blnIsLayoutSuspended;
                if (blnDoResumeLayout)
                {
                    _blnIsLayoutSuspended = true;
                    await this.DoThreadSafeAsync(x => x.SuspendLayout());
                }

                try
                {
                    _objReferenceCharacterSettings = kvpReplacementOption.Value;
                    await _objCharacterSettings.CopyValuesAsync(_objReferenceCharacterSettings);
                    RebuildCustomDataDirectoryInfos();
                    IsDirty = false;
                    await PopulateSettingsList();
                }
                finally
                {
                    if (blnDoResumeLayout)
                    {
                        _blnIsLayoutSuspended = false;
                        await this.DoThreadSafeAsync(x => x.ResumeLayout());
                    }
                }
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async void cmdSaveAs_Click(object sender, EventArgs e)
        {
            string strSelectedName;
            string strSelectedFullFileName;
            string strSelectSettingName
                = await LanguageManager.GetStringAsync("Message_CharacterOptions_SelectSettingName");
            do
            {
                do
                {
                    using (ThreadSafeForm<SelectText> frmSelectName = await ThreadSafeForm<SelectText>.GetAsync(() => new SelectText
                           {
                               DefaultString = _objCharacterSettings.BuiltInOption
                                   ? string.Empty
                                   : _objCharacterSettings.FileName.TrimEndOnce(".xml"),
                               Description = strSelectSettingName
                           }))
                    {
                        if (await frmSelectName.ShowDialogSafeAsync(this) != DialogResult.OK)
                            return;
                        strSelectedName = frmSelectName.MyForm.SelectedValue;
                    }

                    if (SettingsManager.LoadedCharacterSettings.Any(x => x.Value.Name == strSelectedName))
                    {
                        DialogResult eCreateDuplicateSetting = Program.ShowMessageBox(
                            string.Format(await LanguageManager.GetStringAsync("Message_CharacterOptions_DuplicateSettingName"),
                                strSelectedName),
                            await LanguageManager.GetStringAsync("MessageTitle_CharacterOptions_DuplicateFileName"),
                            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                        switch (eCreateDuplicateSetting)
                        {
                            case DialogResult.Cancel:
                                return;

                            case DialogResult.No:
                                strSelectedName = string.Empty;
                                break;
                        }
                    }
                } while (string.IsNullOrWhiteSpace(strSelectedName));

                string strBaseFileName = strSelectedName.FastEscape(Path.GetInvalidFileNameChars()).TrimEndOnce(".xml");
                // Make sure our file name isn't too long, otherwise we run into problems on Windows
                // We can assume that Chummer's startup path plus 16 is within the limit, otherwise the user would have had problems installing Chummer with its data files in the first place
                int intStartupPathLimit = Utils.GetStartupPath.Length + 16;
                if (strBaseFileName.Length > intStartupPathLimit)
                    strBaseFileName = strBaseFileName.Substring(0, intStartupPathLimit);
                strSelectedFullFileName = strBaseFileName + ".xml";
                int intMaxNameLength = char.MaxValue - Utils.GetStartupPath.Length - "settings".Length - 6;
                uint uintAccumulator = 1;
                string strSeparator = "_";
                while (SettingsManager.LoadedCharacterSettings.Any(x => x.Value.FileName == strSelectedFullFileName))
                {
                    strSelectedFullFileName = strBaseFileName + strSeparator + uintAccumulator.ToString(GlobalSettings.InvariantCultureInfo) + ".xml";
                    if (strSelectedFullFileName.Length > intMaxNameLength)
                    {
                        Program.ShowMessageBox(
                            await LanguageManager.GetStringAsync("Message_CharacterOptions_SettingFileNameTooLongError"),
                            await LanguageManager.GetStringAsync("MessageTitle_CharacterOptions_SettingFileNameTooLongError"),
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        strSelectedName = string.Empty;
                        break;
                    }

                    if (uintAccumulator == uint.MaxValue)
                        uintAccumulator = uint.MinValue;
                    else if (++uintAccumulator == 1)
                        strSeparator += '_';
                }
            } while (string.IsNullOrWhiteSpace(strSelectedName));

            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                _objCharacterSettings.Name = strSelectedName;
                bool blnDoResumeLayout = !_blnIsLayoutSuspended;
                if (blnDoResumeLayout)
                {
                    _blnIsLayoutSuspended = true;
                    await this.DoThreadSafeAsync(x => x.SuspendLayout());
                }

                try
                {
                    CharacterSettings objNewCharacterSettings
                        = new CharacterSettings(_objCharacterSettings, false, strSelectedFullFileName);
                    if (!await SettingsManager.LoadedCharacterSettingsAsModifiable.TryAddAsync(
                            objNewCharacterSettings.DictionaryKey, objNewCharacterSettings))
                    {
                        await objNewCharacterSettings.DisposeAsync();
                        return;
                    }

                    if (!_objCharacterSettings.Save(strSelectedFullFileName, true))
                    {
                        // Revert addition of settings if we cannot create a file
                        await SettingsManager.LoadedCharacterSettingsAsModifiable.RemoveAsync(
                            objNewCharacterSettings.DictionaryKey);
                        await objNewCharacterSettings.DisposeAsync();
                        return;
                    }

                    // Force repopulate character settings list in Master Index from here in lieu of event handling for concurrent dictionaries
                    _blnForceMasterIndexRepopulateOnClose = true;
                    _objReferenceCharacterSettings = objNewCharacterSettings;
                    IsDirty = false;
                    await PopulateSettingsList();
                }
                finally
                {
                    if (blnDoResumeLayout)
                    {
                        _blnIsLayoutSuspended = false;
                        await this.DoThreadSafeAsync(x => x.ResumeLayout());
                    }
                }
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async void cmdSave_Click(object sender, EventArgs e)
        {
            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                if (_objReferenceCharacterSettings.BuildMethod != _objCharacterSettings.BuildMethod)
                {
                    using (new FetchSafelyFromPool<StringBuilder>(Utils.StringBuilderPool,
                                                                  out StringBuilder sbdConflictingCharacters))
                    {
                        foreach (Character objCharacter in Program.OpenCharacters)
                        {
                            if (!objCharacter.Created
                                && ReferenceEquals(objCharacter.Settings, _objReferenceCharacterSettings))
                                sbdConflictingCharacters.AppendLine(objCharacter.CharacterName);
                        }

                        if (sbdConflictingCharacters.Length > 0)
                        {
                            Program.ShowMessageBox(this,
                                                   await LanguageManager.GetStringAsync(
                                                       "Message_CharacterOptions_OpenCharacterOnBuildMethodChange")
                                                   +
                                                   sbdConflictingCharacters,
                                                   await LanguageManager.GetStringAsync(
                                                       "MessageTitle_CharacterOptions_OpenCharacterOnBuildMethodChange"),
                                                   MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                }

                if (!_objCharacterSettings.Save())
                    return;
                bool blnDoResumeLayout = !_blnIsLayoutSuspended;
                if (blnDoResumeLayout)
                {
                    _blnIsLayoutSuspended = true;
                    await this.DoThreadSafeAsync(x => x.SuspendLayout());
                }

                try
                {
                    await _objReferenceCharacterSettings.CopyValuesAsync(_objCharacterSettings);
                    IsDirty = false;
                }
                finally
                {
                    if (blnDoResumeLayout)
                    {
                        _blnIsLayoutSuspended = false;
                        await this.DoThreadSafeAsync(x => x.ResumeLayout());
                    }
                }
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async void cboSetting_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            string strSelectedFile = await cboSetting.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString());
            if (string.IsNullOrEmpty(strSelectedFile))
                return;
            (bool blnSuccess, CharacterSettings objNewOption)
                = await SettingsManager.LoadedCharacterSettings.TryGetValueAsync(strSelectedFile);
            if (!blnSuccess)
                return;

            if (IsDirty)
            {
                string text = await LanguageManager.GetStringAsync("Message_CharacterOptions_UnsavedDirty");
                string caption = await LanguageManager.GetStringAsync("MessageTitle_CharacterOptions_UnsavedDirty");

                if (Program.ShowMessageBox(text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) !=
                    DialogResult.Yes)
                {
                    _blnLoading = true;
                    await cboSetting.DoThreadSafeFuncAsync(x => x.SelectedIndex = _intOldSelectedSettingIndex);
                    _blnLoading = false;
                    return;
                }
                IsDirty = false;
            }

            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                _blnLoading = true;
                bool blnDoResumeLayout = !_blnIsLayoutSuspended;
                if (blnDoResumeLayout)
                {
                    _blnIsLayoutSuspended = true;
                    await this.DoThreadSafeAsync(x => x.SuspendLayout());
                }

                try
                {
                    if (_blnWasRenamed && _intOldSelectedSettingIndex >= 0)
                    {
                        int intCurrentSelectedSettingIndex
                            = await cboSetting.DoThreadSafeFuncAsync(x => x.SelectedIndex);
                        ListItem objNewListItem =
                            new ListItem(_lstSettings[_intOldSelectedSettingIndex].Value,
                                         _objReferenceCharacterSettings.DisplayName);
                        _lstSettings[_intOldSelectedSettingIndex] = objNewListItem;
                        await cboSetting.PopulateWithListItemsAsync(_lstSettings);
                        await cboSetting.DoThreadSafeAsync(x => x.SelectedIndex = intCurrentSelectedSettingIndex);
                    }

                    _objReferenceCharacterSettings = objNewOption;
                    await _objCharacterSettings.CopyValuesAsync(objNewOption);
                    RebuildCustomDataDirectoryInfos();
                    await PopulateOptions();
                    _blnLoading = false;
                    IsDirty = false;
                }
                finally
                {
                    if (blnDoResumeLayout)
                    {
                        _blnIsLayoutSuspended = false;
                        await this.DoThreadSafeAsync(x => x.ResumeLayout());
                    }
                }

                _intOldSelectedSettingIndex = cboSetting.SelectedIndex;
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async void cmdRestoreDefaults_Click(object sender, EventArgs e)
        {
            // Verify that the user wants to reset these values.
            if (Program.ShowMessageBox(
                await LanguageManager.GetStringAsync("Message_Options_RestoreDefaults"),
                await LanguageManager.GetStringAsync("MessageTitle_Options_RestoreDefaults"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                _blnLoading = true;
                bool blnDoResumeLayout = !_blnIsLayoutSuspended;
                if (blnDoResumeLayout)
                {
                    _blnIsLayoutSuspended = true;
                    await this.DoThreadSafeAsync(x => x.SuspendLayout());
                }

                try
                {
                    int intCurrentSelectedSettingIndex = await cboSetting.DoThreadSafeFuncAsync(x => x.SelectedIndex);
                    if (_blnWasRenamed && intCurrentSelectedSettingIndex >= 0)
                    {
                        ListItem objNewListItem =
                            new ListItem(_lstSettings[intCurrentSelectedSettingIndex].Value,
                                         _objReferenceCharacterSettings.DisplayName);
                        _lstSettings[intCurrentSelectedSettingIndex] = objNewListItem;
                        await cboSetting.PopulateWithListItemsAsync(_lstSettings);
                        await cboSetting.DoThreadSafeAsync(x => x.SelectedIndex = intCurrentSelectedSettingIndex);
                    }

                    await _objCharacterSettings.CopyValuesAsync(_objReferenceCharacterSettings);
                    RebuildCustomDataDirectoryInfos();
                    await PopulateOptions();
                    _blnLoading = false;
                    IsDirty = false;
                }
                finally
                {
                    if (blnDoResumeLayout)
                    {
                        _blnIsLayoutSuspended = false;
                        await this.DoThreadSafeAsync(x => x.ResumeLayout());
                    }
                }

                _intOldSelectedSettingIndex = cboSetting.SelectedIndex;
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private void cboLimbCount_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnLoading || _blnSkipLimbCountUpdate)
                return;

            string strLimbCount = cboLimbCount.SelectedValue?.ToString();
            if (string.IsNullOrEmpty(strLimbCount))
            {
                _objCharacterSettings.LimbCount = 6;
                _objCharacterSettings.ExcludeLimbSlot = string.Empty;
            }
            else
            {
                int intSeparatorIndex = strLimbCount.IndexOf('<');
                if (intSeparatorIndex == -1)
                {
                    if (int.TryParse(strLimbCount, NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out int intLimbCount))
                        _objCharacterSettings.LimbCount = intLimbCount;
                    else
                    {
                        Utils.BreakIfDebug();
                        _objCharacterSettings.LimbCount = 6;
                    }
                    _objCharacterSettings.ExcludeLimbSlot = string.Empty;
                }
                else
                {
                    if (int.TryParse(strLimbCount.Substring(0, intSeparatorIndex), NumberStyles.Any,
                        GlobalSettings.InvariantCultureInfo, out int intLimbCount))
                    {
                        _objCharacterSettings.LimbCount = intLimbCount;
                        _objCharacterSettings.ExcludeLimbSlot = intSeparatorIndex + 1 < strLimbCount.Length ? strLimbCount.Substring(intSeparatorIndex + 1) : string.Empty;
                    }
                    else
                    {
                        Utils.BreakIfDebug();
                        _objCharacterSettings.LimbCount = 6;
                        _objCharacterSettings.ExcludeLimbSlot = string.Empty;
                    }
                }
            }
        }

        private void cmdOK_Click(object sender, EventArgs e)
        {
            Close();
        }

        private async void EditCharacterSettings_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (IsDirty && Program.ShowMessageBox(await LanguageManager.GetStringAsync("Message_CharacterOptions_UnsavedDirty"),
                await LanguageManager.GetStringAsync("MessageTitle_CharacterOptions_UnsavedDirty"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
            }

            if (_blnForceMasterIndexRepopulateOnClose && Program.MainForm.MasterIndex != null)
            {
                await Program.MainForm.MasterIndex.ForceRepopulateCharacterSettings();
            }
        }

        private void cmdEnableSourcebooks_Click(object sender, EventArgs e)
        {
            _blnLoading = true;
            foreach (TreeNode objNode in treSourcebook.Nodes)
            {
                string strBookCode = objNode.Tag.ToString();
                if (!_setPermanentSourcebooks.Contains(strBookCode))
                {
                    objNode.Checked = _blnSourcebookToggle;
                    if (_blnSourcebookToggle)
                        _objCharacterSettings.BooksWritable.Add(strBookCode);
                    else
                        _objCharacterSettings.BooksWritable.Remove(strBookCode);
                }
            }
            _blnLoading = false;
            _objCharacterSettings.OnPropertyChanged(nameof(CharacterSettings.Books));
            _blnSourcebookToggle = !_blnSourcebookToggle;
        }

        private void treSourcebook_AfterCheck(object sender, TreeViewEventArgs e)
        {
            if (_blnLoading)
                return;
            TreeNode objNode = e.Node;
            if (objNode == null)
                return;
            string strBookCode = objNode.Tag.ToString();
            if (string.IsNullOrEmpty(strBookCode) || (_setPermanentSourcebooks.Contains(strBookCode) && !objNode.Checked))
            {
                _blnLoading = true;
                objNode.Checked = !objNode.Checked;
                _blnLoading = false;
                return;
            }
            if (objNode.Checked)
                _objCharacterSettings.BooksWritable.Add(strBookCode);
            else
                _objCharacterSettings.BooksWritable.Remove(strBookCode);
            _objCharacterSettings.OnPropertyChanged(nameof(CharacterSettings.Books));
        }

        private async void cmdIncreaseCustomDirectoryLoadOrder_Click(object sender, EventArgs e)
        {
            TreeNode nodSelected = await treCustomDataDirectories.DoThreadSafeFuncAsync(x => x.SelectedNode);
            if (nodSelected == null)
                return;
            int intIndex = nodSelected.Index;
            if (intIndex <= 0)
                return;
            _dicCharacterCustomDataDirectoryInfos.Reverse(intIndex - 1, 2);
            _objCharacterSettings.CustomDataDirectoryKeys.Reverse(intIndex - 1, 2);
            _objCharacterSettings.OnPropertyChanged(nameof(CharacterSettings.CustomDataDirectoryKeys));
            await PopulateCustomDataDirectoryTreeView();
        }

        private async void cmdToTopCustomDirectoryLoadOrder_Click(object sender, EventArgs e)
        {
            TreeNode nodSelected = await treCustomDataDirectories.DoThreadSafeFuncAsync(x => x.SelectedNode);
            if (nodSelected == null)
                return;
            int intIndex = nodSelected.Index;
            if (intIndex <= 0)
                return;
            for (int i = intIndex; i > 0; --i)
            {
                _dicCharacterCustomDataDirectoryInfos.Reverse(i - 1, 2);
                _objCharacterSettings.CustomDataDirectoryKeys.Reverse(i - 1, 2);
            }
            _objCharacterSettings.OnPropertyChanged(nameof(CharacterSettings.CustomDataDirectoryKeys));
            await PopulateCustomDataDirectoryTreeView();
        }

        private async void cmdDecreaseCustomDirectoryLoadOrder_Click(object sender, EventArgs e)
        {
            TreeNode nodSelected = await treCustomDataDirectories.DoThreadSafeFuncAsync(x => x.SelectedNode);
            if (nodSelected == null)
                return;
            int intIndex = nodSelected.Index;
            if (intIndex >= _dicCharacterCustomDataDirectoryInfos.Count - 1)
                return;
            _dicCharacterCustomDataDirectoryInfos.Reverse(intIndex, 2);
            _objCharacterSettings.CustomDataDirectoryKeys.Reverse(intIndex, 2);
            _objCharacterSettings.OnPropertyChanged(nameof(CharacterSettings.CustomDataDirectoryKeys));
            await PopulateCustomDataDirectoryTreeView();
        }

        private async void cmdToBottomCustomDirectoryLoadOrder_Click(object sender, EventArgs e)
        {
            TreeNode nodSelected = await treCustomDataDirectories.DoThreadSafeFuncAsync(x => x.SelectedNode);
            if (nodSelected == null)
                return;
            int intIndex = nodSelected.Index;
            if (intIndex >= _dicCharacterCustomDataDirectoryInfos.Count - 1)
                return;
            for (int i = intIndex; i < _dicCharacterCustomDataDirectoryInfos.Count - 1; ++i)
            {
                _dicCharacterCustomDataDirectoryInfos.Reverse(i, 2);
                _objCharacterSettings.CustomDataDirectoryKeys.Reverse(i, 2);
            }
            _objCharacterSettings.OnPropertyChanged(nameof(CharacterSettings.CustomDataDirectoryKeys));
            await PopulateCustomDataDirectoryTreeView();
        }

        private void treCustomDataDirectories_AfterCheck(object sender, TreeViewEventArgs e)
        {
            TreeNode objNode = e.Node;
            if (objNode == null)
                return;
            int intIndex = objNode.Index;
            _dicCharacterCustomDataDirectoryInfos[_dicCharacterCustomDataDirectoryInfos[intIndex].Key] = objNode.Checked;
            switch (objNode.Tag)
            {
                case CustomDataDirectoryInfo objCustomDataDirectoryInfo when _objCharacterSettings.CustomDataDirectoryKeys.ContainsKey(objCustomDataDirectoryInfo.CharacterSettingsSaveKey):
                    _objCharacterSettings.CustomDataDirectoryKeys[objCustomDataDirectoryInfo.CharacterSettingsSaveKey] = objNode.Checked;
                    _objCharacterSettings.OnPropertyChanged(nameof(CharacterSettings.CustomDataDirectoryKeys));
                    break;
                case string strCustomDataDirectoryKey when _objCharacterSettings.CustomDataDirectoryKeys.ContainsKey(strCustomDataDirectoryKey):
                    _objCharacterSettings.CustomDataDirectoryKeys[strCustomDataDirectoryKey] = objNode.Checked;
                    _objCharacterSettings.OnPropertyChanged(nameof(CharacterSettings.CustomDataDirectoryKeys));
                    break;
            }
        }

        private void txtPriorities_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.Handled = !char.IsControl(e.KeyChar)
                        && e.KeyChar != 'A' && e.KeyChar != 'B' && e.KeyChar != 'C' && e.KeyChar != 'D' && e.KeyChar != 'E'
                        && e.KeyChar != 'a' && e.KeyChar != 'b' && e.KeyChar != 'c' && e.KeyChar != 'd' && e.KeyChar != 'e';
            switch (e.KeyChar)
            {
                case 'a':
                    e.KeyChar = 'A';
                    break;

                case 'b':
                    e.KeyChar = 'B';
                    break;

                case 'c':
                    e.KeyChar = 'C';
                    break;

                case 'd':
                    e.KeyChar = 'D';
                    break;

                case 'e':
                    e.KeyChar = 'E';
                    break;
            }
        }

        private async void txtPriorities_TextChanged(object sender, EventArgs e)
        {
            Color objWindowTextColor = await ColorManager.WindowTextAsync;
            await txtPriorities.DoThreadSafeAsync(x => x.ForeColor
                                                      = x.TextLength == 5
                                                          ? objWindowTextColor
                                                          : ColorManager.ErrorColor);
        }

        private async void txtContactPoints_TextChanged(object sender, EventArgs e)
        {
            Color objColor
                = await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                    await txtContactPoints.DoThreadSafeFuncAsync(x => x.Text))
                    ? await ColorManager.WindowTextAsync
                    : ColorManager.ErrorColor;
            await txtContactPoints.DoThreadSafeAsync(x => x.ForeColor = objColor);
        }

        private async void txtKnowledgePoints_TextChanged(object sender, EventArgs e)
        {
            Color objColor
                = await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                    await txtKnowledgePoints.DoThreadSafeFuncAsync(x => x.Text))
                    ? await ColorManager.WindowTextAsync
                    : ColorManager.ErrorColor;
            await txtKnowledgePoints.DoThreadSafeAsync(x => x.ForeColor = objColor);
        }

        private async void txtNuyenExpression_TextChanged(object sender, EventArgs e)
        {
            Color objColor
                = await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                    await txtNuyenExpression.DoThreadSafeFuncAsync(x => x.Text))
                    ? await ColorManager.WindowTextAsync
                    : ColorManager.ErrorColor;
            await txtNuyenExpression.DoThreadSafeAsync(x => x.ForeColor = objColor);
        }

        private async void txtBoundSpiritLimit_TextChanged(object sender, EventArgs e)
        {
            Color objColor
                = await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                    await txtBoundSpiritLimit.DoThreadSafeFuncAsync(x => x.Text))
                    ? await ColorManager.WindowTextAsync
                    : ColorManager.ErrorColor;
            await txtBoundSpiritLimit.DoThreadSafeAsync(x => x.ForeColor = objColor);
        }

        private async void txtRegisteredSpriteLimit_TextChanged(object sender, EventArgs e)
        {
            Color objColor
                = await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                    await txtRegisteredSpriteLimit.DoThreadSafeFuncAsync(x => x.Text))
                    ? await ColorManager.WindowTextAsync
                    : ColorManager.ErrorColor;
            await txtRegisteredSpriteLimit.DoThreadSafeAsync(x => x.ForeColor = objColor);
        }

        private async void txtLiftLimit_TextChanged(object sender, EventArgs e)
        {
            Color objColor
                = await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                    await txtLiftLimit.DoThreadSafeFuncAsync(x => x.Text))
                    ? await ColorManager.WindowTextAsync
                    : ColorManager.ErrorColor;
            await txtLiftLimit.DoThreadSafeAsync(x => x.ForeColor = objColor);
        }

        private async void txtCarryLimit_TextChanged(object sender, EventArgs e)
        {
            Color objColor
                = await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                    await txtCarryLimit.DoThreadSafeFuncAsync(x => x.Text))
                    ? await ColorManager.WindowTextAsync
                    : ColorManager.ErrorColor;
            await txtCarryLimit.DoThreadSafeAsync(x => x.ForeColor = objColor);
        }

        private async void txtEncumbranceInterval_TextChanged(object sender, EventArgs e)
        {
            Color objColor
                = await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                    await txtEncumbranceInterval.DoThreadSafeFuncAsync(x => x.Text))
                    ? await ColorManager.WindowTextAsync
                    : ColorManager.ErrorColor;
            await txtEncumbranceInterval.DoThreadSafeAsync(x => x.ForeColor = objColor);
        }

        private void chkGrade_CheckedChanged(object sender, EventArgs e)
        {
            if (!(sender is CheckBox chkGrade))
                return;

            string strGrade = chkGrade.Tag.ToString();
            if (chkGrade.Checked)
            {
                if (_objCharacterSettings.BannedWareGrades.Contains(strGrade))
                {
                    _objCharacterSettings.BannedWareGrades.Remove(strGrade);
                    _objCharacterSettings.OnPropertyChanged(nameof(CharacterSettings.BannedWareGrades));
                }
            }
            else if (!_objCharacterSettings.BannedWareGrades.Contains(strGrade))
            {
                _objCharacterSettings.BannedWareGrades.Add(strGrade);
                _objCharacterSettings.OnPropertyChanged(nameof(CharacterSettings.BannedWareGrades));
            }
        }

        private void cboPriorityTable_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            string strNewPriorityTable = cboPriorityTable.SelectedValue?.ToString();
            if (string.IsNullOrWhiteSpace(strNewPriorityTable))
                return;
            _objCharacterSettings.PriorityTable = strNewPriorityTable;
        }

        private void treCustomDataDirectories_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (!(e.Node?.Tag is CustomDataDirectoryInfo objSelected))
            {
                gpbDirectoryInfo.Visible = false;
                return;
            }

            gpbDirectoryInfo.SuspendLayout();
            try
            {
                rtbDirectoryDescription.Text = objSelected.DisplayDescription;
                lblDirectoryVersion.Text = objSelected.MyVersion.ToString();
                lblDirectoryAuthors.Text = objSelected.DisplayAuthors;
                lblDirectoryName.Text = objSelected.Name;

                if (objSelected.DependenciesList.Count > 0)
                {
                    using (new FetchSafelyFromPool<StringBuilder>(Utils.StringBuilderPool,
                                                                  out StringBuilder sbdDependencies))
                    {
                        foreach (DirectoryDependency dependency in objSelected.DependenciesList)
                            sbdDependencies.AppendLine(dependency.DisplayName);
                        lblDependencies.Text = sbdDependencies.ToString();
                    }
                }
                else
                {
                    //Make sure all old information is discarded
                    lblDependencies.Text = string.Empty;
                }

                if (objSelected.IncompatibilitiesList.Count > 0)
                {
                    using (new FetchSafelyFromPool<StringBuilder>(Utils.StringBuilderPool,
                                                                  out StringBuilder sbdIncompatibilities))
                    {
                        foreach (DirectoryDependency exclusivity in objSelected.IncompatibilitiesList)
                            sbdIncompatibilities.AppendLine(exclusivity.DisplayName);
                        lblIncompatibilities.Text = sbdIncompatibilities.ToString();
                    }
                }
                else
                {
                    //Make sure all old information is discarded
                    lblIncompatibilities.Text = string.Empty;
                }

                gpbDirectoryInfo.Visible = true;
            }
            finally
            {
                gpbDirectoryInfo.ResumeLayout();
            }
        }

        #endregion Control Events

        #region Methods

        private async ValueTask PopulateSourcebookTreeView(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            // Load the Sourcebook information.
            // Put the Sourcebooks into a List so they can first be sorted.
            object objOldSelected = await treSourcebook.DoThreadSafeFuncAsync(x => x.SelectedNode?.Tag, token);
            await treSourcebook.DoThreadSafeAsync(x => x.BeginUpdate(), token);
            try
            {
                await treSourcebook.DoThreadSafeAsync(x => x.Nodes.Clear(), token);
                _setPermanentSourcebooks.Clear();
                foreach (XPathNavigator objXmlBook in await (await XmlManager.LoadXPathAsync(
                                 "books.xml", _objCharacterSettings.EnabledCustomDataDirectoryPaths, token: token))
                             .SelectAndCacheExpressionAsync("/chummer/books/book"))
                {
                    if (await objXmlBook.SelectSingleNodeAndCacheExpressionAsync("hide") != null)
                        continue;
                    string strCode = (await objXmlBook.SelectSingleNodeAndCacheExpressionAsync("code"))?.Value;
                    if (string.IsNullOrEmpty(strCode))
                        continue;
                    bool blnChecked = _objCharacterSettings.Books.Contains(strCode);
                    if (await objXmlBook.SelectSingleNodeAndCacheExpressionAsync("permanent") != null)
                    {
                        _setPermanentSourcebooks.Add(strCode);
                        if (_objCharacterSettings.BooksWritable.Add(strCode))
                            _objCharacterSettings.OnPropertyChanged(nameof(CharacterSettings.Books));
                        blnChecked = true;
                    }

                    string strTranslate = (await objXmlBook.SelectSingleNodeAndCacheExpressionAsync("translate"))?.Value;
                    string strName = (await objXmlBook.SelectSingleNodeAndCacheExpressionAsync("name"))?.Value;
                    await treSourcebook.DoThreadSafeAsync(x =>
                    {
                        TreeNode objNode = new TreeNode
                        {
                            Text = strTranslate ?? strName ?? string.Empty,
                            Tag = strCode,
                            Checked = blnChecked
                        };
                        x.Nodes.Add(objNode);
                    }, token);
                }

                await treSourcebook.DoThreadSafeAsync(x =>
                {
                    x.Sort();
                    if (objOldSelected != null)
                        x.SelectedNode = x.FindNodeByTag(objOldSelected);
                }, token);
            }
            finally
            {
                await treSourcebook.DoThreadSafeAsync(x => x.EndUpdate(), token);
            }
        }

        private async ValueTask PopulateCustomDataDirectoryTreeView(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            object objOldSelected = await treCustomDataDirectories.DoThreadSafeFuncAsync(x => x.SelectedNode?.Tag, token);
            await treCustomDataDirectories.DoThreadSafeAsync(x => x.BeginUpdate(), token);
            try
            {
                string strFileNotFound = await LanguageManager.GetStringAsync("MessageTitle_FileNotFound");
                Color objGrayTextColor = await ColorManager.GrayTextAsync;
                if (_dicCharacterCustomDataDirectoryInfos.Count != treCustomDataDirectories.Nodes.Count)
                {
                    await treCustomDataDirectories.DoThreadSafeAsync(x =>
                    {
                        x.Nodes.Clear();
                        foreach (KeyValuePair<object, bool> kvpInfo in _dicCharacterCustomDataDirectoryInfos)
                        {
                            TreeNode objNode = new TreeNode
                            {
                                Tag = kvpInfo.Key,
                                Checked = kvpInfo.Value
                            };
                            if (kvpInfo.Key is CustomDataDirectoryInfo objInfo)
                            {
                                objNode.Text = objInfo.DisplayName;
                                if (objNode.Checked)
                                {
                                    // check dependencies and exclusivities only if they could exist at all instead of calling and running into empty an foreach.
                                    string missingDirectories = string.Empty;
                                    if (objInfo.DependenciesList.Count > 0)
                                        missingDirectories = objInfo.CheckDependency(_objCharacterSettings);

                                    string prohibitedDirectories = string.Empty;
                                    if (objInfo.IncompatibilitiesList.Count > 0)
                                        prohibitedDirectories = objInfo.CheckIncompatibility(_objCharacterSettings);

                                    if (!string.IsNullOrEmpty(missingDirectories)
                                        || !string.IsNullOrEmpty(prohibitedDirectories))
                                    {
                                        objNode.ToolTipText
                                            = CustomDataDirectoryInfo.BuildIncompatibilityDependencyString(
                                                missingDirectories, prohibitedDirectories);
                                        objNode.ForeColor = ColorManager.ErrorColor;
                                    }
                                }
                            }
                            else
                            {
                                objNode.Text = kvpInfo.Key.ToString();
                                objNode.ForeColor = objGrayTextColor;
                                objNode.ToolTipText = strFileNotFound;
                            }

                            x.Nodes.Add(objNode);
                        }
                    }, token);
                }
                else
                {
                    Color objWindowTextColor = await ColorManager.WindowTextAsync;
                    await treCustomDataDirectories.DoThreadSafeAsync(x =>
                    {
                        for (int i = 0; i < x.Nodes.Count; ++i)
                        {
                            TreeNode objNode = x.Nodes[i];
                            KeyValuePair<object, bool> kvpInfo = _dicCharacterCustomDataDirectoryInfos[i];
                            if (!kvpInfo.Key.Equals(objNode.Tag))
                                objNode.Tag = kvpInfo.Key;
                            if (kvpInfo.Value != objNode.Checked)
                                objNode.Checked = kvpInfo.Value;
                            if (kvpInfo.Key is CustomDataDirectoryInfo objInfo)
                            {
                                objNode.Text = objInfo.DisplayName;
                                if (objNode.Checked)
                                {
                                    // check dependencies and exclusivities only if they could exist at all instead of calling and running into empty an foreach.
                                    string missingDirectories = string.Empty;
                                    if (objInfo.DependenciesList.Count > 0)
                                        missingDirectories = objInfo.CheckDependency(_objCharacterSettings);

                                    string prohibitedDirectories = string.Empty;
                                    if (objInfo.IncompatibilitiesList.Count > 0)
                                        prohibitedDirectories = objInfo.CheckIncompatibility(_objCharacterSettings);

                                    if (!string.IsNullOrEmpty(missingDirectories)
                                        || !string.IsNullOrEmpty(prohibitedDirectories))
                                    {
                                        objNode.ToolTipText
                                            = CustomDataDirectoryInfo.BuildIncompatibilityDependencyString(
                                                missingDirectories, prohibitedDirectories);
                                        objNode.ForeColor = ColorManager.ErrorColor;
                                    }
                                    else
                                    {
                                        objNode.ToolTipText = string.Empty;
                                        objNode.ForeColor = objWindowTextColor;
                                    }
                                }
                                else
                                {
                                    objNode.ToolTipText = string.Empty;
                                    objNode.ForeColor = objWindowTextColor;
                                }
                            }
                            else
                            {
                                objNode.Text = kvpInfo.Key.ToString();
                                objNode.ForeColor = objGrayTextColor;
                                objNode.ToolTipText = strFileNotFound;
                            }
                        }

                        if (objOldSelected != null)
                            x.SelectedNode = x.FindNodeByTag(objOldSelected);
                        x.ShowNodeToolTips = true;
                    }, token);
                }
            }
            finally
            {
                await treCustomDataDirectories.DoThreadSafeAsync(x => x.EndUpdate(), token);
            }
        }

        /// <summary>
        /// Set the values for all of the controls based on the Options for the selected Setting.
        /// </summary>
        private async ValueTask PopulateOptions(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            CursorWait objCursorWait = await CursorWait.NewAsync(this, token: token);
            try
            {
                bool blnDoResumeLayout = !_blnIsLayoutSuspended;
                if (blnDoResumeLayout)
                {
                    _blnIsLayoutSuspended = true;
                    await this.DoThreadSafeAsync(x => x.SuspendLayout(), token);
                }

                try
                {
                    await PopulateSourcebookTreeView(token);
                    await PopulatePriorityTableList(token);
                    await PopulateLimbCountList(token);
                    await PopulateAllowedGrades(token);
                    await PopulateCustomDataDirectoryTreeView(token);
                }
                finally
                {
                    if (blnDoResumeLayout)
                    {
                        _blnIsLayoutSuspended = false;
                        await this.DoThreadSafeAsync(x => x.ResumeLayout(), token);
                    }
                }
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async ValueTask PopulatePriorityTableList(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            CursorWait objCursorWait = await CursorWait.NewAsync(this, token: token);
            try
            {
                using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool,
                                                               out List<ListItem> lstPriorityTables))
                {
                    foreach (XPathNavigator objXmlNode in await (await XmlManager
                                     .LoadXPathAsync("priorities.xml",
                                                     _objCharacterSettings.EnabledCustomDataDirectoryPaths,
                                                     token: token))
                                 .SelectAndCacheExpressionAsync(
                                     "/chummer/prioritytables/prioritytable"))
                    {
                        string strName = objXmlNode.Value;
                        if (!string.IsNullOrEmpty(strName))
                            lstPriorityTables.Add(new ListItem(objXmlNode.Value,
                                                               (await objXmlNode
                                                                   .SelectSingleNodeAndCacheExpressionAsync(
                                                                       "@translate"))
                                                               ?.Value ?? strName));
                    }

                    string strOldSelected = _objCharacterSettings.PriorityTable;

                    bool blnOldLoading = _blnLoading;
                    _blnLoading = true;
                    await cboPriorityTable.PopulateWithListItemsAsync(lstPriorityTables, token);
                    await cboPriorityTable.DoThreadSafeAsync(x =>
                    {
                        if (!string.IsNullOrEmpty(strOldSelected))
                            x.SelectedValue = strOldSelected;
                        if (x.SelectedIndex == -1 && lstPriorityTables.Count > 0)
                            x.SelectedValue = _objReferenceCharacterSettings.PriorityTable;
                        if (x.SelectedIndex == -1 && lstPriorityTables.Count > 0)
                            x.SelectedIndex = 0;
                    }, token);
                    _blnLoading = blnOldLoading;
                }

                string strSelectedTable
                    = await cboPriorityTable.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token);
                if (!string.IsNullOrWhiteSpace(strSelectedTable) &&
                    _objCharacterSettings.PriorityTable != strSelectedTable)
                    _objCharacterSettings.PriorityTable = strSelectedTable;
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async ValueTask PopulateLimbCountList(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            CursorWait objCursorWait = await CursorWait.NewAsync(this, token: token);
            try
            {
                using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool,
                                                               out List<ListItem> lstLimbCount))
                {
                    foreach (XPathNavigator objXmlNode in await (await XmlManager
                                     .LoadXPathAsync("options.xml",
                                                     _objCharacterSettings.EnabledCustomDataDirectoryPaths,
                                                     token: token))
                                 .SelectAndCacheExpressionAsync("/chummer/limbcounts/limb"))
                    {
                        string strExclude = (await objXmlNode.SelectSingleNodeAndCacheExpressionAsync("exclude"))?.Value
                                            ??
                                            string.Empty;
                        if (!string.IsNullOrEmpty(strExclude))
                            strExclude = '<' + strExclude;
                        lstLimbCount.Add(new ListItem(
                                             (await objXmlNode.SelectSingleNodeAndCacheExpressionAsync("limbcount"))
                                             ?.Value + strExclude,
                                             (await objXmlNode.SelectSingleNodeAndCacheExpressionAsync("translate"))
                                             ?.Value
                                             ?? (await objXmlNode.SelectSingleNodeAndCacheExpressionAsync("name"))
                                             ?.Value
                                             ?? string.Empty));
                    }

                    string strLimbSlot = _objCharacterSettings.LimbCount.ToString(GlobalSettings.InvariantCultureInfo);
                    if (!string.IsNullOrEmpty(_objCharacterSettings.ExcludeLimbSlot))
                        strLimbSlot += '<' + _objCharacterSettings.ExcludeLimbSlot;

                    _blnSkipLimbCountUpdate = true;
                    await cboLimbCount.PopulateWithListItemsAsync(lstLimbCount, token);
                    await cboLimbCount.DoThreadSafeAsync(x =>
                    {
                        if (!string.IsNullOrEmpty(strLimbSlot))
                            x.SelectedValue = strLimbSlot;
                        if (x.SelectedIndex == -1 && lstLimbCount.Count > 0)
                            x.SelectedIndex = 0;
                    }, token);
                }

                _blnSkipLimbCountUpdate = false;
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async ValueTask PopulateAllowedGrades(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            CursorWait objCursorWait = await CursorWait.NewAsync(this, token: token);
            try
            {
                using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool,
                                                               out List<ListItem> lstGrades))
                {
                    foreach (XPathNavigator objXmlNode in await (await XmlManager
                                     .LoadXPathAsync("bioware.xml",
                                                     _objCharacterSettings.EnabledCustomDataDirectoryPaths,
                                                     token: token))
                                 .SelectAndCacheExpressionAsync("/chummer/grades/grade[not(hide)]"))
                    {
                        string strName = (await objXmlNode.SelectSingleNodeAndCacheExpressionAsync("name"))?.Value;
                        if (!string.IsNullOrEmpty(strName) && strName != "None")
                        {
                            string strBook = (await objXmlNode.SelectSingleNodeAndCacheExpressionAsync("source"))
                                ?.Value;
                            if (!string.IsNullOrEmpty(strBook)
                                && treSourcebook.Nodes.Cast<TreeNode>().All(x => x.Tag.ToString() != strBook))
                                continue;
                            if (lstGrades.Any(x => strName.Contains(x.Value.ToString())))
                                continue;
                            ListItem objExistingCoveredGrade =
                                lstGrades.Find(x => x.Value.ToString().Contains(strName));
                            if (objExistingCoveredGrade.Value != null)
                                lstGrades.Remove(objExistingCoveredGrade);
                            lstGrades.Add(new ListItem(
                                              strName,
                                              (await objXmlNode.SelectSingleNodeAndCacheExpressionAsync("translate"))
                                              ?.Value
                                              ?? strName));
                        }
                    }

                    foreach (XPathNavigator objXmlNode in await (await XmlManager
                                     .LoadXPathAsync("cyberware.xml",
                                                     _objCharacterSettings.EnabledCustomDataDirectoryPaths,
                                                     token: token))
                                 .SelectAndCacheExpressionAsync("/chummer/grades/grade[not(hide)]"))
                    {
                        string strName = (await objXmlNode.SelectSingleNodeAndCacheExpressionAsync("name"))?.Value;
                        if (!string.IsNullOrEmpty(strName) && strName != "None")
                        {
                            string strBook = (await objXmlNode.SelectSingleNodeAndCacheExpressionAsync("source"))
                                ?.Value;
                            if (!string.IsNullOrEmpty(strBook)
                                && treSourcebook.Nodes.Cast<TreeNode>().All(x => x.Tag.ToString() != strBook))
                                continue;
                            if (lstGrades.Any(x => strName.Contains(x.Value.ToString())))
                                continue;
                            ListItem objExistingCoveredGrade =
                                lstGrades.Find(x => x.Value.ToString().Contains(strName));
                            if (objExistingCoveredGrade.Value != null)
                                lstGrades.Remove(objExistingCoveredGrade);
                            lstGrades.Add(new ListItem(
                                              strName,
                                              (await objXmlNode.SelectSingleNodeAndCacheExpressionAsync("translate"))
                                              ?.Value
                                              ?? strName));
                        }
                    }

                    await flpAllowedCyberwareGrades.DoThreadSafeAsync(x =>
                    {
                        x.SuspendLayout();
                        try
                        {
                            x.Controls.Clear();
                            foreach (ListItem objGrade in lstGrades)
                            {
                                CheckBox chkGrade = new CheckBox
                                {
                                    UseVisualStyleBackColor = true,
                                    Text = objGrade.Name,
                                    Tag = objGrade.Value,
                                    AutoSize = true,
                                    Anchor = AnchorStyles.Left,
                                    Checked = !_objCharacterSettings.BannedWareGrades.Contains(
                                        objGrade.Value.ToString())
                                };
                                chkGrade.CheckedChanged += chkGrade_CheckedChanged;
                                x.Controls.Add(chkGrade);
                            }
                        }
                        finally
                        {
                            x.ResumeLayout();
                        }
                    }, token);
                }
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private void RebuildCustomDataDirectoryInfos()
        {
            _dicCharacterCustomDataDirectoryInfos.Clear();
            foreach (KeyValuePair<string, bool> kvpCustomDataDirectory in _objCharacterSettings.CustomDataDirectoryKeys)
            {
                CustomDataDirectoryInfo objLoopInfo = GlobalSettings.CustomDataDirectoryInfos.FirstOrDefault(x => x.CharacterSettingsSaveKey == kvpCustomDataDirectory.Key);
                if (objLoopInfo != default)
                {
                    _dicCharacterCustomDataDirectoryInfos.Add(objLoopInfo, kvpCustomDataDirectory.Value);
                }
                else
                {
                    _dicCharacterCustomDataDirectoryInfos.Add(kvpCustomDataDirectory.Key, kvpCustomDataDirectory.Value);
                }
            }
        }

        private async ValueTask SetToolTips()
        {
            await chkUnarmedSkillImprovements.SetToolTipAsync((await LanguageManager.GetStringAsync("Tip_OptionsUnarmedSkillImprovements")).WordWrap());
            await chkIgnoreArt.SetToolTipAsync((await LanguageManager.GetStringAsync("Tip_OptionsIgnoreArt")).WordWrap());
            await chkIgnoreComplexFormLimit.SetToolTipAsync((await LanguageManager.GetStringAsync("Tip_OptionsIgnoreComplexFormLimit")).WordWrap());
            await chkCyberlegMovement.SetToolTipAsync((await LanguageManager.GetStringAsync("Tip_OptionsCyberlegMovement")).WordWrap());
            await chkDontDoubleQualityPurchases.SetToolTipAsync((await LanguageManager.GetStringAsync("Tip_OptionsDontDoubleQualityPurchases")).WordWrap());
            await chkDontDoubleQualityRefunds.SetToolTipAsync((await LanguageManager.GetStringAsync("Tip_OptionsDontDoubleQualityRefunds")).WordWrap());
            await chkStrictSkillGroups.SetToolTipAsync((await LanguageManager.GetStringAsync("Tip_OptionStrictSkillGroups")).WordWrap());
            await chkAllowInitiation.SetToolTipAsync((await LanguageManager.GetStringAsync("Tip_OptionsAllowInitiation")).WordWrap());
            await chkUseCalculatedPublicAwareness.SetToolTipAsync((await LanguageManager.GetStringAsync("Tip_PublicAwareness")).WordWrap());
        }

        private void SetupDataBindings()
        {
            cmdRename.DoOneWayNegatableDataBinding("Enabled", _objCharacterSettings, nameof(CharacterSettings.BuiltInOption));
            cmdDelete.DoOneWayNegatableDataBinding("Enabled", _objCharacterSettings, nameof(CharacterSettings.BuiltInOption));

            cboBuildMethod.DoDataBinding("SelectedValue", _objCharacterSettings, nameof(CharacterSettings.BuildMethod));
            lblPriorityTable.DoOneWayDataBinding("Visible", _objCharacterSettings, nameof(CharacterSettings.BuildMethodUsesPriorityTables));
            cboPriorityTable.DoOneWayDataBinding("Visible", _objCharacterSettings, nameof(CharacterSettings.BuildMethodUsesPriorityTables));
            lblPriorities.DoOneWayDataBinding("Visible", _objCharacterSettings, nameof(CharacterSettings.BuildMethodIsPriority));
            txtPriorities.DoOneWayDataBinding("Visible", _objCharacterSettings, nameof(CharacterSettings.BuildMethodIsPriority));
            txtPriorities.DoDataBinding("Text", _objCharacterSettings, nameof(CharacterSettings.PriorityArray));
            lblSumToTen.DoOneWayDataBinding("Visible", _objCharacterSettings, nameof(CharacterSettings.BuildMethodIsSumtoTen));
            nudSumToTen.DoOneWayDataBinding("Visible", _objCharacterSettings, nameof(CharacterSettings.BuildMethodIsSumtoTen));
            nudSumToTen.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.SumtoTen));
            nudStartingKarma.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.BuildKarma));
            nudMaxNuyenKarma.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.NuyenMaximumBP));
            nudMaxAvail.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MaximumAvailability));
            nudQualityKarmaLimit.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.QualityKarmaLimit));
            nudMaxNumberMaxAttributes.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MaxNumberMaxAttributesCreate));
            nudMaxSkillRatingCreate.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MaxSkillRatingCreate));
            nudMaxKnowledgeSkillRatingCreate.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MaxKnowledgeSkillRatingCreate));
            nudMaxSkillRatingCreate.DoDataBinding("Maximum", _objCharacterSettings, nameof(CharacterSettings.MaxSkillRating));
            nudMaxKnowledgeSkillRatingCreate.DoDataBinding("Maximum", _objCharacterSettings, nameof(CharacterSettings.MaxKnowledgeSkillRating));
            txtContactPoints.DoDataBinding("Text", _objCharacterSettings, nameof(CharacterSettings.ContactPointsExpression));
            txtKnowledgePoints.DoDataBinding("Text", _objCharacterSettings, nameof(CharacterSettings.KnowledgePointsExpression));
            txtNuyenExpression.DoDataBinding("Text", _objCharacterSettings, nameof(CharacterSettings.ChargenKarmaToNuyenExpression));
            txtRegisteredSpriteLimit.DoDataBinding("Text", _objCharacterSettings, nameof(CharacterSettings.RegisteredSpriteExpression));
            txtBoundSpiritLimit.DoDataBinding("Text", _objCharacterSettings, nameof(CharacterSettings.BoundSpiritExpression));
            txtLiftLimit.DoDataBinding("Text", _objCharacterSettings, nameof(CharacterSettings.LiftLimitExpression));
            txtCarryLimit.DoDataBinding("Text", _objCharacterSettings, nameof(CharacterSettings.CarryLimitExpression));
            txtEncumbranceInterval.DoDataBinding("Text", _objCharacterSettings, nameof(CharacterSettings.EncumbranceIntervalExpression));
            nudWeightDecimals.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.WeightDecimals));

            chkEncumbrancePenaltyPhysicalLimit.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.DoEncumbrancePenaltyPhysicalLimit));
            chkEncumbrancePenaltyMovementSpeed.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.DoEncumbrancePenaltyMovementSpeed));
            chkEncumbrancePenaltyAgility.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.DoEncumbrancePenaltyAgility));
            chkEncumbrancePenaltyReaction.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.DoEncumbrancePenaltyReaction));
            chkEncumbrancePenaltyWoundModifier.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.DoEncumbrancePenaltyWoundModifier));

            nudEncumbrancePenaltyPhysicalLimit.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.EncumbrancePenaltyPhysicalLimit));
            nudEncumbrancePenaltyMovementSpeed.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.EncumbrancePenaltyMovementSpeed));
            nudEncumbrancePenaltyAgility.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.EncumbrancePenaltyAgility));
            nudEncumbrancePenaltyReaction.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.EncumbrancePenaltyReaction));
            nudEncumbrancePenaltyWoundModifier.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.EncumbrancePenaltyWoundModifier));

            chkEnforceCapacity.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.EnforceCapacity));
            chkLicenseEachRestrictedItem.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.LicenseRestricted));
            chkReverseAttributePriorityOrder.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.ReverseAttributePriorityOrder));
            chkDronemods.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.DroneMods));
            chkDronemodsMaximumPilot.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.DroneModsMaximumPilot));
            chkRestrictRecoil.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.RestrictRecoil));
            chkStrictSkillGroups.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.StrictSkillGroupsInCreateMode));
            chkAllowPointBuySpecializationsOnKarmaSkills.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.AllowPointBuySpecializationsOnKarmaSkills));
            chkAllowFreeGrids.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.AllowFreeGrids));

            chkDontUseCyberlimbCalculation.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.DontUseCyberlimbCalculation));
            chkCyberlegMovement.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.CyberlegMovement));
            chkCyberlimbAttributeBonusCap.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.CyberlimbAttributeBonusCapOverride));
            nudCyberlimbAttributeBonusCap.DoOneWayDataBinding("Enabled", _objCharacterSettings, nameof(CharacterSettings.CyberlimbAttributeBonusCapOverride));
            nudCyberlimbAttributeBonusCap.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.CyberlimbAttributeBonusCap));
            chkRedlinerLimbsSkull.DoNegatableDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.RedlinerExcludesSkull));
            chkRedlinerLimbsTorso.DoNegatableDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.RedlinerExcludesTorso));
            chkRedlinerLimbsArms.DoNegatableDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.RedlinerExcludesArms));
            chkRedlinerLimbsLegs.DoNegatableDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.RedlinerExcludesLegs));

            nudNuyenDecimalsMaximum.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MaxNuyenDecimals));
            nudNuyenDecimalsMinimum.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MinNuyenDecimals));
            nudEssenceDecimals.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.EssenceDecimals));
            chkDontRoundEssenceInternally.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.DontRoundEssenceInternally));

            nudMinInitiativeDice.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MinInitiativeDice));
            nudMaxInitiativeDice.DoDataBinding("Minimum", _objCharacterSettings, nameof(CharacterSettings.MinInitiativeDice));
            nudMaxInitiativeDice.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MaxInitiativeDice));
            nudMinAstralInitiativeDice.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MinAstralInitiativeDice));
            nudMaxAstralInitiativeDice.DoDataBinding("Minimum", _objCharacterSettings, nameof(CharacterSettings.MinAstralInitiativeDice));
            nudMaxAstralInitiativeDice.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MaxAstralInitiativeDice));
            nudMinColdSimInitiativeDice.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MinColdSimInitiativeDice));
            nudMaxColdSimInitiativeDice.DoDataBinding("Minimum", _objCharacterSettings, nameof(CharacterSettings.MinColdSimInitiativeDice));
            nudMaxColdSimInitiativeDice.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MaxColdSimInitiativeDice));
            nudMinHotSimInitiativeDice.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MinHotSimInitiativeDice));
            nudMaxHotSimInitiativeDice.DoDataBinding("Minimum", _objCharacterSettings, nameof(CharacterSettings.MinHotSimInitiativeDice));
            nudMaxHotSimInitiativeDice.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MaxHotSimInitiativeDice));

            chkEnable4eStyleEnemyTracking.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.EnableEnemyTracking));
            flpKarmaGainedFromEnemies.DoOneWayDataBinding("Enabled", _objCharacterSettings, nameof(CharacterSettings.EnableEnemyTracking));
            nudKarmaGainedFromEnemies.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaEnemy));
            chkEnemyKarmaQualityLimit.DoOneWayDataBinding("Enabled", _objCharacterSettings, nameof(CharacterSettings.EnableEnemyTracking));
            chkEnemyKarmaQualityLimit.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.EnemyKarmaQualityLimit));
            chkMoreLethalGameplay.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.MoreLethalGameplay));

            chkNoArmorEncumbrance.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.NoArmorEncumbrance));
            chkIgnoreArt.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.IgnoreArt));
            chkIgnoreComplexFormLimit.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.IgnoreComplexFormLimit));
            chkUnarmedSkillImprovements.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.UnarmedImprovementsApplyToWeapons));
            chkMysAdPp.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.MysAdeptAllowPpCareer));
            chkMysAdPp.DoOneWayNegatableDataBinding("Enabled", _objCharacterSettings, nameof(CharacterSettings.MysAdeptSecondMAGAttribute));
            chkPrioritySpellsAsAdeptPowers.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.PrioritySpellsAsAdeptPowers));
            chkPrioritySpellsAsAdeptPowers.DoOneWayNegatableDataBinding("Enabled", _objCharacterSettings, nameof(CharacterSettings.MysAdeptSecondMAGAttribute));
            chkMysAdeptSecondMAGAttribute.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.MysAdeptSecondMAGAttribute));
            chkMysAdeptSecondMAGAttribute.DoOneWayDataBinding("Enabled", _objCharacterSettings, nameof(CharacterSettings.MysAdeptSecondMAGAttributeEnabled));
            chkUsePointsOnBrokenGroups.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.UsePointsOnBrokenGroups));
            chkSpecialKarmaCost.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.SpecialKarmaCostBasedOnShownValue));
            chkUseCalculatedPublicAwareness.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.UseCalculatedPublicAwareness));
            chkAlternateMetatypeAttributeKarma.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.AlternateMetatypeAttributeKarma));
            chkCompensateSkillGroupKarmaDifference.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.CompensateSkillGroupKarmaDifference));
            chkFreeMartialArtSpecialization.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.FreeMartialArtSpecialization));
            chkIncreasedImprovedAbilityModifier.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.IncreasedImprovedAbilityMultiplier));
            chkAllowTechnomancerSchooling.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.AllowTechnomancerSchooling));
            chkAllowSkillRegrouping.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.AllowSkillRegrouping));
            chkSpecializationsBreakSkillGroups.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.SpecializationsBreakSkillGroups));
            chkDontDoubleQualityPurchases.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.DontDoubleQualityPurchases));
            chkDontDoubleQualityRefunds.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.DontDoubleQualityRefunds));
            chkDroneArmorMultiplier.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.DroneArmorMultiplierEnabled));
            nudDroneArmorMultiplier.DoOneWayDataBinding("Enabled", _objCharacterSettings, nameof(CharacterSettings.DroneArmorMultiplierEnabled));
            nudDroneArmorMultiplier.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.DroneArmorMultiplier));
            chkESSLossReducesMaximumOnly.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.ESSLossReducesMaximumOnly));
            chkExceedNegativeQualities.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.ExceedNegativeQualities));
            chkExceedNegativeQualitiesNoBonus.DoOneWayDataBinding("Enabled", _objCharacterSettings, nameof(CharacterSettings.ExceedNegativeQualities));
            chkExceedNegativeQualitiesNoBonus.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.ExceedNegativeQualitiesNoBonus));
            chkExceedPositiveQualities.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.ExceedPositiveQualities));
            chkExceedPositiveQualitiesCostDoubled.DoOneWayDataBinding("Enabled", _objCharacterSettings, nameof(CharacterSettings.ExceedPositiveQualities));
            chkExceedPositiveQualitiesCostDoubled.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.ExceedPositiveQualitiesCostDoubled));
            chkExtendAnyDetectionSpell.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.ExtendAnyDetectionSpell));
            chkAllowCyberwareESSDiscounts.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.AllowCyberwareESSDiscounts));
            chkAllowInitiation.DoDataBinding("Checked", _objCharacterSettings, nameof(CharacterSettings.AllowInitiationInCreateMode));
            nudMaxSkillRating.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MaxSkillRating));
            nudMaxKnowledgeSkillRating.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MaxKnowledgeSkillRating));

            // Karma options.
            nudMetatypeCostsKarmaMultiplier.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.MetatypeCostsKarmaMultiplier));
            nudKarmaNuyenPerWftM.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.NuyenPerBPWftM));
            nudKarmaNuyenPerWftP.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.NuyenPerBPWftP));
            nudKarmaAttribute.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaAttribute));
            nudKarmaQuality.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaQuality));
            nudKarmaSpecialization.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaSpecialization));
            nudKarmaKnowledgeSpecialization.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaKnowledgeSpecialization));
            nudKarmaNewKnowledgeSkill.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaNewKnowledgeSkill));
            nudKarmaNewActiveSkill.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaNewActiveSkill));
            nudKarmaNewSkillGroup.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaNewSkillGroup));
            nudKarmaImproveKnowledgeSkill.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaImproveKnowledgeSkill));
            nudKarmaImproveActiveSkill.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaImproveActiveSkill));
            nudKarmaImproveSkillGroup.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaImproveSkillGroup));
            nudKarmaSpell.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaSpell));
            nudKarmaNewComplexForm.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaNewComplexForm));
            nudKarmaNewAIProgram.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaNewAIProgram));
            nudKarmaNewAIAdvancedProgram.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaNewAIAdvancedProgram));
            nudKarmaMetamagic.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaMetamagic));
            nudKarmaContact.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaContact));
            nudKarmaCarryover.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaCarryover));
            nudKarmaSpirit.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaSpirit));
            nudKarmaSpiritFettering.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaSpiritFettering));
            nudKarmaTechnique.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaTechnique));
            nudKarmaInitiation.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaInitiation));
            nudKarmaInitiationFlat.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaInitiationFlat));
            nudKarmaJoinGroup.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaJoinGroup));
            nudKarmaLeaveGroup.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaLeaveGroup));
            nudKarmaMysticAdeptPowerPoint.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaMysticAdeptPowerPoint));

            // Focus costs
            nudKarmaAlchemicalFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaAlchemicalFocus));
            nudKarmaBanishingFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaBanishingFocus));
            nudKarmaBindingFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaBindingFocus));
            nudKarmaCenteringFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaCenteringFocus));
            nudKarmaCounterspellingFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaCounterspellingFocus));
            nudKarmaDisenchantingFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaDisenchantingFocus));
            nudKarmaFlexibleSignatureFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaFlexibleSignatureFocus));
            nudKarmaMaskingFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaMaskingFocus));
            nudKarmaPowerFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaPowerFocus));
            nudKarmaQiFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaQiFocus));
            nudKarmaRitualSpellcastingFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaRitualSpellcastingFocus));
            nudKarmaSpellcastingFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaSpellcastingFocus));
            nudKarmaSpellShapingFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaSpellShapingFocus));
            nudKarmaSummoningFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaSummoningFocus));
            nudKarmaSustainingFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaSustainingFocus));
            nudKarmaWeaponFocus.DoDataBinding("Value", _objCharacterSettings, nameof(CharacterSettings.KarmaWeaponFocus));
        }

        private async ValueTask PopulateSettingsList(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            CursorWait objCursorWait = await CursorWait.NewAsync(this, token: token);
            try
            {
                string strSelect = string.Empty;
                if (!_blnLoading)
                    strSelect = await cboSetting.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString(), token);
                _lstSettings.Clear();
                foreach (KeyValuePair<string, CharacterSettings> kvpCharacterSettingsEntry in SettingsManager
                             .LoadedCharacterSettings)
                {
                    _lstSettings.Add(new ListItem(kvpCharacterSettingsEntry.Key,
                                                  kvpCharacterSettingsEntry.Value.DisplayName));
                    if (ReferenceEquals(_objReferenceCharacterSettings, kvpCharacterSettingsEntry.Value))
                        strSelect = kvpCharacterSettingsEntry.Key;
                }

                _lstSettings.Sort(CompareListItems.CompareNames);
                await cboSetting.PopulateWithListItemsAsync(_lstSettings, token);
                await cboSetting.DoThreadSafeAsync(x =>
                {
                    if (!string.IsNullOrEmpty(strSelect))
                        x.SelectedValue = strSelect;
                    if (x.SelectedIndex == -1 && _lstSettings.Count > 0)
                        x.SelectedValue = x.FindStringExact(GlobalSettings.DefaultCharacterSetting);
                    if (x.SelectedIndex == -1 && _lstSettings.Count > 0)
                        x.SelectedIndex = 0;
                }, token);
                _intOldSelectedSettingIndex = await cboSetting.DoThreadSafeFuncAsync(x => x.SelectedIndex, token);
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private async void SettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            CursorWait objCursorWait = await CursorWait.NewAsync(this);
            try
            {
                if (!_blnLoading)
                {
                    IsDirty = !_objCharacterSettings.HasIdenticalSettings(_objReferenceCharacterSettings);
                    switch (e.PropertyName)
                    {
                        case nameof(CharacterSettings.EnabledCustomDataDirectoryPaths):
                            await PopulateOptions();
                            break;

                        case nameof(CharacterSettings.PriorityTable):
                            await PopulatePriorityTableList();
                            break;
                    }
                }
                else
                {
                    switch (e.PropertyName)
                    {
                        case nameof(CharacterSettings.BuiltInOption):
                        {
                            bool blnAllTextBoxesLegal = await IsAllTextBoxesLegalAsync();
                            await cmdSave.DoThreadSafeAsync(
                                x => x.Enabled = IsDirty && blnAllTextBoxesLegal
                                                         && !_objCharacterSettings.BuiltInOption);
                            break;
                        }
                        case nameof(CharacterSettings.PriorityArray):
                        case nameof(CharacterSettings.BuildMethod):
                        {
                            bool blnAllTextBoxesLegal = await IsAllTextBoxesLegalAsync();
                            await cmdSaveAs.DoThreadSafeAsync(x => x.Enabled = IsDirty && blnAllTextBoxesLegal);
                            await cmdSave.DoThreadSafeAsync(
                                x => x.Enabled = IsDirty && blnAllTextBoxesLegal
                                                         && !_objCharacterSettings.BuiltInOption);
                            break;
                        }
                    }
                }
            }
            finally
            {
                await objCursorWait.DisposeAsync();
            }
        }

        private bool IsAllTextBoxesLegal()
        {
            if (_objCharacterSettings.BuildMethod == CharacterBuildMethod.Priority && _objCharacterSettings.PriorityArray.Length != 5)
                return false;

            return CommonFunctions.IsCharacterAttributeXPathValidOrNull(
                       _objCharacterSettings.ContactPointsExpression) &&
                   CommonFunctions.IsCharacterAttributeXPathValidOrNull(
                       _objCharacterSettings.KnowledgePointsExpression) &&
                   CommonFunctions.IsCharacterAttributeXPathValidOrNull(
                       _objCharacterSettings.ChargenKarmaToNuyenExpression.Replace("{Karma}", "1")
                                            .Replace("{PriorityNuyen}", "1")) &&
                   CommonFunctions.IsCharacterAttributeXPathValidOrNull(
                       _objCharacterSettings.RegisteredSpriteExpression) &&
                   CommonFunctions.IsCharacterAttributeXPathValidOrNull(
                       _objCharacterSettings.BoundSpiritExpression) &&
                   CommonFunctions.IsCharacterAttributeXPathValidOrNull(
                       _objCharacterSettings.LiftLimitExpression) &&
                   CommonFunctions.IsCharacterAttributeXPathValidOrNull(
                       _objCharacterSettings.CarryLimitExpression) &&
                   CommonFunctions.IsCharacterAttributeXPathValidOrNull(
                       _objCharacterSettings.EncumbranceIntervalExpression);
        }

        private async ValueTask<bool> IsAllTextBoxesLegalAsync()
        {
            if (_objCharacterSettings.BuildMethod == CharacterBuildMethod.Priority && _objCharacterSettings.PriorityArray.Length != 5)
                return false;

            return await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                       _objCharacterSettings.ContactPointsExpression) &&
                   await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                       _objCharacterSettings.KnowledgePointsExpression) &&
                   await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                       _objCharacterSettings.ChargenKarmaToNuyenExpression.Replace("{Karma}", "1")
                                            .Replace("{PriorityNuyen}", "1")) &&
                   await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                       _objCharacterSettings.RegisteredSpriteExpression) &&
                   await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                       _objCharacterSettings.BoundSpiritExpression) &&
                   await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                       _objCharacterSettings.LiftLimitExpression) &&
                   await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                       _objCharacterSettings.CarryLimitExpression) &&
                   await CommonFunctions.IsCharacterAttributeXPathValidOrNullAsync(
                       _objCharacterSettings.EncumbranceIntervalExpression);
        }

        private bool IsDirty
        {
            get => _blnDirty;
            set
            {
                if (_blnDirty == value)
                    return;
                _blnDirty = value;
                cmdOK.DoThreadSafe(x => x.Text = LanguageManager.GetString(value ? "String_Cancel" : "String_OK"));
                if (value)
                {
                    bool blnIsAllTextBoxesLegal = IsAllTextBoxesLegal();
                    cmdSaveAs.DoThreadSafe(x => x.Enabled = blnIsAllTextBoxesLegal);
                    cmdSave.DoThreadSafe(x => x.Enabled = blnIsAllTextBoxesLegal && !_objCharacterSettings.BuiltInOption);
                }
                else
                {
                    _blnWasRenamed = false;
                    cmdSaveAs.DoThreadSafe(x => x.Enabled = false);
                    cmdSave.DoThreadSafe(x => x.Enabled = false);
                }
            }
        }

        #endregion Methods
    }
}
