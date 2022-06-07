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
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.XPath;

namespace Chummer
{
    public partial class SelectMentorSpirit : Form
    {
        private bool _blnSkipRefresh = true;
        private string _strForceMentor = string.Empty;

        private readonly XPathNavigator _xmlBaseMentorSpiritDataNode;
        private readonly Character _objCharacter;

        #region Control Events

        public SelectMentorSpirit(Character objCharacter, string strXmlFile = "mentors.xml")
        {
            _objCharacter = objCharacter ?? throw new ArgumentNullException(nameof(objCharacter));
            InitializeComponent();
            if (strXmlFile == "paragons.xml")
                Tag = "Title_SelectMentorSpirit_Paragon";
            this.UpdateLightDarkMode();
            this.TranslateWinForm();
            // Load the Mentor information.
            _xmlBaseMentorSpiritDataNode = objCharacter.LoadDataXPath(strXmlFile).SelectSingleNodeAndCacheExpression("/chummer");
        }

        private async void lstMentor_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnSkipRefresh)
                return;
            await this.DoThreadSafeAsync(x => x.SuspendLayout());
            try
            {
                XPathNavigator objXmlMentor = null;
                if (lstMentor.SelectedIndex >= 0)
                {
                    string strSelectedId = await lstMentor.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString());
                    if (!string.IsNullOrEmpty(strSelectedId))
                        objXmlMentor =
                            _xmlBaseMentorSpiritDataNode.SelectSingleNode("mentors/mentor[id = " +
                                                                          strSelectedId.CleanXPath() + ']');
                }

                if (objXmlMentor != null)
                {
                    // If the Mentor offers a choice of bonuses, build the list and let the user select one.
                    XPathNavigator xmlChoices = await objXmlMentor.SelectSingleNodeAndCacheExpressionAsync("choices");
                    if (xmlChoices != null)
                    {
                        using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool, out List<ListItem> lstChoice1))
                        using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool,
                                                                       out List<ListItem> lstChoice2))
                        {
                            foreach (XPathNavigator objChoice in await xmlChoices.SelectAndCacheExpressionAsync("choice"))
                            {
                                string strName = (await objChoice.SelectSingleNodeAndCacheExpressionAsync("name"))?.Value
                                                 ?? string.Empty;
                                if ((_objCharacter.AdeptEnabled ||
                                     !strName.StartsWith("Adept:", StringComparison.Ordinal)) &&
                                    (_objCharacter.MagicianEnabled ||
                                     !strName.StartsWith("Magician:", StringComparison.Ordinal)))
                                {
                                    if (objChoice.SelectSingleNode("@set")?.Value == "2")
                                        lstChoice2.Add(new ListItem(strName,
                                                                    (await objChoice.SelectSingleNodeAndCacheExpressionAsync(
                                                                        "translate"))?.Value ?? strName));
                                    else
                                        lstChoice1.Add(new ListItem(strName,
                                                                    (await objChoice.SelectSingleNodeAndCacheExpressionAsync(
                                                                        "translate"))?.Value ?? strName));
                                }
                            }

                            //If there is only a single option, show it as a label.
                            //If there are more, show the drop down menu
                            if (lstChoice1.Count > 0)
                                await cboChoice1.PopulateWithListItemsAsync(lstChoice1);
                            await cboChoice1.DoThreadSafeAsync(x => x.Visible = lstChoice1.Count > 1);
                            await lblBonusText1.DoThreadSafeAsync(x =>
                            {
                                x.Visible = lstChoice1.Count == 1;
                                if (lstChoice1.Count == 1)
                                    x.Text = lstChoice1[0].Name;
                            });
                            if (lstChoice2.Count > 0)
                                await cboChoice2.PopulateWithListItemsAsync(lstChoice2);
                            await cboChoice2.DoThreadSafeAsync(x => x.Visible = lstChoice2.Count > 1);
                            await lblBonusText2.DoThreadSafeAsync(x =>
                            {
                                x.Visible = lstChoice2.Count == 1;
                                if (lstChoice2.Count == 1)
                                    x.Text = lstChoice2[0].Name;
                            });
                        }
                    }
                    else
                    {
                        await cboChoice1.DoThreadSafeAsync(x => x.Visible = false);
                        await cboChoice2.DoThreadSafeAsync(x => x.Visible = false);
                        await lblBonusText1.DoThreadSafeAsync(x => x.Visible = false);
                        await lblBonusText2.DoThreadSafeAsync(x => x.Visible = false);
                    }

                    await cboChoice1.DoThreadSafeFuncAsync(x => x.Visible)
                                    .ContinueWith(y => lblChoice1.DoThreadSafeAsync(x => x.Visible = y.Result))
                                    .Unwrap();
                    await cboChoice2.DoThreadSafeFuncAsync(x => x.Visible)
                                    .ContinueWith(
                                        y => lblChoice2.DoThreadSafeAsync(x => x.Visible = y.Result))
                                    .Unwrap();
                    await lblBonusText1.DoThreadSafeFuncAsync(x => x.Visible)
                                       .ContinueWith(
                                           y => lblBonus1.DoThreadSafeAsync(x => x.Visible = y.Result))
                                       .Unwrap();
                    await lblBonusText2.DoThreadSafeFuncAsync(x => x.Visible)
                                       .ContinueWith(y => lblBonus2.DoThreadSafeAsync(x => x.Visible = y.Result))
                                       .Unwrap();

                    // Get the information for the selected Mentor.
                    string strUnknown = await LanguageManager.GetStringAsync("String_Unknown");
                    string strAdvantage = objXmlMentor.SelectSingleNode("altadvantage")?.Value ??
                                          objXmlMentor.SelectSingleNode("advantage")?.Value ??
                                          strUnknown;
                    await lblAdvantage.DoThreadSafeAsync(x => x.Text = strAdvantage);
                    await lblAdvantageLabel.DoThreadSafeAsync(x => x.Visible = !string.IsNullOrEmpty(strAdvantage));
                    string strDisadvantage = objXmlMentor.SelectSingleNode("altdisadvantage")?.Value ??
                                             objXmlMentor.SelectSingleNode("disadvantage")?.Value ??
                                             strUnknown;
                    await lblDisadvantage.DoThreadSafeAsync(x => x.Text = strDisadvantage);
                    await lblDisadvantageLabel.DoThreadSafeAsync(x => x.Visible = !string.IsNullOrEmpty(strDisadvantage));

                    string strSource = objXmlMentor.SelectSingleNode("source")?.Value ??
                                       await LanguageManager.GetStringAsync("String_Unknown");
                    string strPage = (await objXmlMentor.SelectSingleNodeAndCacheExpressionAsync("altpage"))?.Value ??
                                     objXmlMentor.SelectSingleNode("page")?.Value ??
                                     await LanguageManager.GetStringAsync("String_Unknown");
                    SourceString objSourceString = await SourceString.GetSourceStringAsync(strSource, strPage, GlobalSettings.Language,
                        GlobalSettings.CultureInfo, _objCharacter);
                    await objSourceString.SetControlAsync(lblSource);
                    await lblSource.DoThreadSafeFuncAsync(x => x.Text)
                                   .ContinueWith(
                                       y => lblSourceLabel.DoThreadSafeAsync(
                                           x => x.Visible = !string.IsNullOrEmpty(y.Result))).Unwrap();
                    await cmdOK.DoThreadSafeAsync(x => x.Enabled = true);
                    await tlpRight.DoThreadSafeAsync(x => x.Visible = true);
                    await tlpBottomRight.DoThreadSafeAsync(x => x.Visible = true);
                }
                else
                {
                    await tlpRight.DoThreadSafeAsync(x => x.Visible = false);
                    await tlpBottomRight.DoThreadSafeAsync(x => x.Visible = false);
                    await cmdOK.DoThreadSafeAsync(x => x.Enabled = false);
                }
            }
            finally
            {
                await this.DoThreadSafeAsync(x => x.ResumeLayout());
            }
        }

        /// <summary>
        /// Accept the selected item and close the form.
        /// </summary>
        private void AcceptForm(object sender, EventArgs e)
        {
            string strSelectedId = lstMentor.SelectedValue?.ToString();
            if (!string.IsNullOrEmpty(strSelectedId))
            {
                XPathNavigator objXmlMentor = _xmlBaseMentorSpiritDataNode.SelectSingleNode("mentors/mentor[id = " + strSelectedId.CleanXPath() + ']');
                if (objXmlMentor == null)
                    return;

                SelectedMentor = strSelectedId;

                DialogResult = DialogResult.OK;
            }
        }

        /// <summary>
        /// Populate the Mentor list.
        /// </summary>
        private async void RefreshMentorsList(object sender, EventArgs e)
        {
            string strForceId = string.Empty;

            string strFilter = '(' + _objCharacter.Settings.BookXPath() + ')';
            string strSearch = await txtSearch.DoThreadSafeFuncAsync(x => x.Text);
            if (!string.IsNullOrEmpty(strSearch))
                strFilter += " and " + CommonFunctions.GenerateSearchXPath(strSearch);
            using (new FetchSafelyFromPool<List<ListItem>>(Utils.ListItemListPool, out List<ListItem> lstMentors))
            {
                foreach (XPathNavigator objXmlMentor in _xmlBaseMentorSpiritDataNode.Select(
                             "mentors/mentor[" + strFilter + ']'))
                {
                    if (!objXmlMentor.RequirementsMet(_objCharacter)) continue;

                    string strName = (await objXmlMentor.SelectSingleNodeAndCacheExpressionAsync("name"))?.Value
                                     ?? await LanguageManager.GetStringAsync("String_Unknown");
                    string strId = (await objXmlMentor.SelectSingleNodeAndCacheExpressionAsync("id"))?.Value ?? string.Empty;
                    if (strName == _strForceMentor)
                        strForceId = strId;
                    lstMentors.Add(new ListItem(
                                       strId,
                                       (await objXmlMentor.SelectSingleNodeAndCacheExpressionAsync("translate"))?.Value ?? strName));
                }

                lstMentors.Sort(CompareListItems.CompareNames);
                string strOldSelected = await lstMentor.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString());
                _blnSkipRefresh = true;
                await lstMentor.PopulateWithListItemsAsync(lstMentors);
                _blnSkipRefresh = false;
                await lstMentor.DoThreadSafeAsync(x =>
                {
                    if (!string.IsNullOrEmpty(strOldSelected))
                        x.SelectedValue = strOldSelected;
                    else
                        x.SelectedIndex = -1;
                    if (!string.IsNullOrEmpty(strForceId))
                    {
                        x.SelectedValue = strForceId;
                        x.Enabled = false;
                    }
                });
            }
        }

        private async void OpenSourceFromLabel(object sender, EventArgs e)
        {
            await CommonFunctions.OpenPdfFromControl(sender);
        }

        private void cmdCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        #endregion Control Events

        #region Properties

        /// <summary>
        /// Forced selection for mentor spirit
        /// </summary>
        public string ForcedMentor
        {
            set => _strForceMentor = value;
        }

        /// <summary>
        /// Mentor that was selected in the dialogue.
        /// </summary>
        public string SelectedMentor { get; private set; } = string.Empty;

        /// <summary>
        /// First choice that was selected in the dialogue.
        /// </summary>
        public string Choice1 => cboChoice1.SelectedValue?.ToString() ?? string.Empty;

        /// <summary>
        /// Second choice that was selected in the dialogue.
        /// </summary>
        public string Choice2 => cboChoice2.SelectedValue?.ToString() ?? string.Empty;

        #endregion Properties
    }
}
