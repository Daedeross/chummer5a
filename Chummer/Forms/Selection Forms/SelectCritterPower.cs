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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.XPath;

namespace Chummer
{
    public partial class SelectCritterPower : Form
    {
        private string _strSelectedPower = string.Empty;
        private int _intSelectedRating;
        private static string _strSelectCategory = string.Empty;
        private decimal _decPowerPoints;
        private bool _blnAddAgain;

        private readonly XPathNavigator _xmlBaseCritterPowerDataNode;
        private readonly XPathNavigator _xmlMetatypeDataNode;
        private readonly Character _objCharacter;

        private readonly List<ListItem> _lstCategory = Utils.ListItemListPool.Get();

        #region Control Events

        public SelectCritterPower(Character objCharacter)
        {
            InitializeComponent();
            this.UpdateLightDarkMode();
            this.TranslateWinForm();
            _objCharacter = objCharacter ?? throw new ArgumentNullException(nameof(objCharacter));
            _xmlBaseCritterPowerDataNode = _objCharacter.LoadDataXPath("critterpowers.xml").SelectSingleNodeAndCacheExpression("/chummer");
            _xmlMetatypeDataNode = _objCharacter.GetNodeXPath();

            if (_xmlMetatypeDataNode == null || _objCharacter.MetavariantGuid == Guid.Empty) return;
            XPathNavigator xmlMetavariantNode = _xmlMetatypeDataNode.SelectSingleNode("metavariants/metavariant[id = "
                                                                                      + _objCharacter.MetavariantGuid.ToString("D", GlobalSettings.InvariantCultureInfo).CleanXPath()
                                                                                      + ']');
            if (xmlMetavariantNode != null)
                _xmlMetatypeDataNode = xmlMetavariantNode;
        }

        private async void SelectCritterPower_Load(object sender, EventArgs e)
        {
            // Populate the Category list.
            foreach (XPathNavigator objXmlCategory in await _xmlBaseCritterPowerDataNode.SelectAndCacheExpressionAsync("categories/category"))
            {
                string strInnerText = objXmlCategory.Value;
                if ((await ImprovementManager
                        .GetCachedImprovementListForValueOfAsync(_objCharacter,
                                                                 Improvement.ImprovementType.AllowCritterPowerCategory))
                    .Any(imp => strInnerText.Contains(imp.ImprovedName))
                    && (await objXmlCategory.SelectSingleNodeAndCacheExpressionAsync("@whitelist"))?.Value == bool.TrueString
                    || (await ImprovementManager
                        .GetCachedImprovementListForValueOfAsync(_objCharacter,
                                                                 Improvement.ImprovementType.LimitCritterPowerCategory))
                       .Any(imp => strInnerText.Contains(imp.ImprovedName)))
                {
                    _lstCategory.Add(new ListItem(strInnerText,
                        (await objXmlCategory.SelectSingleNodeAndCacheExpressionAsync("@translate"))?.Value ?? strInnerText));
                    continue;
                }

                if ((await ImprovementManager
                        .GetCachedImprovementListForValueOfAsync(_objCharacter,
                                                                 Improvement.ImprovementType.LimitCritterPowerCategory))
                    .Any(imp => !strInnerText.Contains(imp.ImprovedName)))
                {
                    continue;
                }
                _lstCategory.Add(new ListItem(strInnerText,
                    (await objXmlCategory.SelectSingleNodeAndCacheExpressionAsync("@translate"))?.Value ?? strInnerText));
            }

            _lstCategory.Sort(CompareListItems.CompareNames);

            if (_lstCategory.Count > 0)
            {
                _lstCategory.Insert(0, new ListItem("Show All", await LanguageManager.GetStringAsync("String_ShowAll")));
            }
            
            await cboCategory.PopulateWithListItemsAsync(_lstCategory);

            // Select the first Category in the list.
            if (string.IsNullOrEmpty(_strSelectCategory))
                cboCategory.SelectedIndex = 0;
            else if (cboCategory.Items.Contains(_strSelectCategory))
            {
                cboCategory.SelectedValue = _strSelectCategory;
            }

            if (cboCategory.SelectedIndex == -1)
                cboCategory.SelectedIndex = 0;
        }

        private void cmdOK_Click(object sender, EventArgs e)
        {
            _blnAddAgain = false;
            AcceptForm();
        }

        private void cmdCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private async void trePowers_AfterSelect(object sender, TreeViewEventArgs e)
        {
            await lblPowerPoints.DoThreadSafeAsync(x => x.Visible = false);
            await lblPowerPointsLabel.DoThreadSafeAsync(x => x.Visible = false);
            string strSelectedPower = await trePowers.DoThreadSafeFuncAsync(x => x.SelectedNode.Tag?.ToString());
            if (!string.IsNullOrEmpty(strSelectedPower))
            {
                XPathNavigator objXmlPower = _xmlBaseCritterPowerDataNode.SelectSingleNode("powers/power[id = " + strSelectedPower.CleanXPath() + ']');
                if (objXmlPower != null)
                {
                    string strCategory = (await objXmlPower.SelectSingleNodeAndCacheExpressionAsync("category"))?.Value
                                         ?? string.Empty;
                    await lblCritterPowerCategory.DoThreadSafeAsync(x => x.Text = strCategory);

                    string strType = (await objXmlPower.SelectSingleNodeAndCacheExpressionAsync("type"))?.Value
                                     ?? string.Empty;
                    switch (strType)
                    {
                        case "M":
                            strType = await LanguageManager.GetStringAsync("String_SpellTypeMana");
                            break;

                        case "P":
                            strType = await LanguageManager.GetStringAsync("String_SpellTypePhysical");
                            break;
                    }
                    await lblCritterPowerType.DoThreadSafeAsync(x => x.Text = strType);

                    string strAction = objXmlPower.SelectSingleNode("action")?.Value ?? string.Empty;
                    switch (strAction)
                    {
                        case "Auto":
                            strAction = await LanguageManager.GetStringAsync("String_ActionAutomatic");
                            break;

                        case "Free":
                            strAction = await LanguageManager.GetStringAsync("String_ActionFree");
                            break;

                        case "Simple":
                            strAction = await LanguageManager.GetStringAsync("String_ActionSimple");
                            break;

                        case "Complex":
                            strAction = await LanguageManager.GetStringAsync("String_ActionComplex");
                            break;

                        case "Special":
                            strAction = await LanguageManager.GetStringAsync("String_SpellDurationSpecial");
                            break;
                    }
                    await lblCritterPowerAction.DoThreadSafeAsync(x => x.Text = strAction);

                    string strRange = objXmlPower.SelectSingleNode("range")?.Value ?? string.Empty;
                    if (!string.IsNullOrEmpty(strRange))
                    {
                        strRange = await strRange.CheapReplaceAsync("Self",
                                () => LanguageManager.GetStringAsync("String_SpellRangeSelf"))
                            .CheapReplaceAsync("Special",
                                () => LanguageManager.GetStringAsync("String_SpellDurationSpecial"))
                            .CheapReplaceAsync("LOS", () => LanguageManager.GetStringAsync("String_SpellRangeLineOfSight"))
                            .CheapReplaceAsync("LOI",
                                () => LanguageManager.GetStringAsync("String_SpellRangeLineOfInfluence"))
                            .CheapReplaceAsync("Touch", () => LanguageManager.GetStringAsync("String_SpellRangeTouchLong"))
                            .CheapReplaceAsync("T", () => LanguageManager.GetStringAsync("String_SpellRangeTouch"))
                            .CheapReplaceAsync("(A)", async () => '(' + await LanguageManager.GetStringAsync("String_SpellRangeArea") + ')')
                            .CheapReplaceAsync("MAG", () => LanguageManager.GetStringAsync("String_AttributeMAGShort"));
                    }
                    await lblCritterPowerRange.DoThreadSafeAsync(x => x.Text = strRange);

                    string strDuration = (await objXmlPower.SelectSingleNodeAndCacheExpressionAsync("duration"))?.Value ?? string.Empty;
                    switch (strDuration)
                    {
                        case "Instant":
                            strDuration = await LanguageManager.GetStringAsync("String_SpellDurationInstantLong");
                            break;

                        case "Sustained":
                            strDuration = await LanguageManager.GetStringAsync("String_SpellDurationSustained");
                            break;

                        case "Always":
                            strDuration = await LanguageManager.GetStringAsync("String_SpellDurationAlways");
                            break;

                        case "Special":
                            strDuration = await LanguageManager.GetStringAsync("String_SpellDurationSpecial");
                            break;
                    }
                    await lblCritterPowerDuration.DoThreadSafeAsync(x => x.Text = strDuration);

                    string strSource = (await objXmlPower.SelectSingleNodeAndCacheExpressionAsync("source"))?.Value ?? await LanguageManager.GetStringAsync("String_Unknown");
                    string strPage = (await objXmlPower.SelectSingleNodeAndCacheExpressionAsync("altpage"))?.Value ?? (await objXmlPower.SelectSingleNodeAndCacheExpressionAsync("page"))?.Value ?? await LanguageManager.GetStringAsync("String_Unknown");
                    SourceString objSource = await SourceString.GetSourceStringAsync(strSource, strPage, GlobalSettings.Language,
                        GlobalSettings.CultureInfo, _objCharacter);
                    await objSource.SetControlAsync(lblCritterPowerSource);

                    bool blnVisible = await objXmlPower.SelectSingleNodeAndCacheExpressionAsync("rating") != null;
                    await nudCritterPowerRating.DoThreadSafeAsync(x => { x.Visible = blnVisible; x.Enabled = blnVisible; });

                    string strKarma = (await objXmlPower.SelectSingleNodeAndCacheExpressionAsync("karma"))?.Value
                                      ?? "0";
                    await lblKarma.DoThreadSafeAsync(x => x.Text = strKarma);

                    // If the character is a Free Spirit, populate the Power Points Cost as well.
                    if (_objCharacter.Metatype == "Free Spirit")
                    {
                        XPathNavigator xmlOptionalPowerCostNode = _xmlMetatypeDataNode.SelectSingleNode("optionalpowers/power[. = " + objXmlPower.SelectSingleNode("name")?.Value.CleanXPath() + "]/@cost");
                        if (xmlOptionalPowerCostNode != null)
                        {
                            await lblPowerPoints.DoThreadSafeAsync(x =>
                            {
                                x.Text = xmlOptionalPowerCostNode.Value;
                                x.Visible = true;
                            });
                            await lblPowerPointsLabel.DoThreadSafeAsync(x => x.Visible = true);
                        }
                    }

                    await lblCritterPowerTypeLabel.DoThreadSafeAsync(x => x.Visible = !string.IsNullOrEmpty(strType));
                    await lblCritterPowerActionLabel.DoThreadSafeAsync(x => x.Visible = !string.IsNullOrEmpty(strAction));
                    await lblCritterPowerRangeLabel.DoThreadSafeAsync(x => x.Visible = !string.IsNullOrEmpty(strRange));
                    await lblCritterPowerDurationLabel.DoThreadSafeAsync(x => x.Visible = !string.IsNullOrEmpty(strDuration));
                    await lblCritterPowerSourceLabel.DoThreadSafeAsync(x => x.Visible = !string.IsNullOrEmpty(objSource.ToString()));
                    await lblKarmaLabel.DoThreadSafeAsync(x => x.Visible = !string.IsNullOrEmpty(strKarma));
                    await tlpRight.DoThreadSafeAsync(x => x.Visible = true);
                }
                else
                {
                    await tlpRight.DoThreadSafeAsync(x => x.Visible = false);
                }
            }
            else
            {
                await tlpRight.DoThreadSafeAsync(x => x.Visible = false);
            }
        }

        private async void cboCategory_SelectedIndexChanged(object sender, EventArgs e)
        {
            await RefreshCategory();
        }

        private async ValueTask RefreshCategory()
        {
            await trePowers.DoThreadSafeAsync(x => x.Nodes.Clear());

            string strCategory = await cboCategory.DoThreadSafeFuncAsync(x => x.SelectedValue?.ToString());

            List<string> lstPowerWhitelist = new List<string>(10);

            // If the Critter is only allowed certain Powers, display only those.
            XPathNavigator xmlOptionalPowers = await _xmlMetatypeDataNode.SelectSingleNodeAndCacheExpressionAsync("optionalpowers");
            if (xmlOptionalPowers != null)
            {
                foreach (XPathNavigator xmlNode in await xmlOptionalPowers.SelectAndCacheExpressionAsync("power"))
                    lstPowerWhitelist.Add(xmlNode.Value);

                // Determine if the Critter has a physical presence Power (Materialization, Possession, or Inhabitation).
                bool blnPhysicalPresence = _objCharacter.CritterPowers.Any(x => x.Name == "Materialization" || x.Name == "Possession" || x.Name == "Inhabitation");

                // Add any Critter Powers the Critter comes with that have been manually deleted so they can be re-added.
                foreach (XPathNavigator objXmlCritterPower in await _xmlMetatypeDataNode.SelectAndCacheExpressionAsync("powers/power"))
                {
                    bool blnAddPower = true;
                    // Make sure the Critter doesn't already have the Power.
                    foreach (string strCheckPowerName in _objCharacter.CritterPowers.Select(x => x.Name))
                    {
                        if (strCheckPowerName == objXmlCritterPower.Value)
                        {
                            blnAddPower = false;
                            break;
                        }
                        if ((strCheckPowerName == "Materialization" || strCheckPowerName == "Possession" || strCheckPowerName == "Inhabitation") && blnPhysicalPresence)
                        {
                            blnAddPower = false;
                            break;
                        }
                    }

                    if (blnAddPower)
                    {
                        lstPowerWhitelist.Add(objXmlCritterPower.Value);

                        // If Manifestation is one of the Powers, also include Inhabitation and Possess if they're not already in the list.
                        if (!blnPhysicalPresence && objXmlCritterPower.Value == "Materialization")
                        {
                            bool blnFoundPossession = false;
                            bool blnFoundInhabitation = false;
                            foreach (string strCheckPower in lstPowerWhitelist)
                            {
                                switch (strCheckPower)
                                {
                                    case "Possession":
                                        blnFoundPossession = true;
                                        break;

                                    case "Inhabitation":
                                        blnFoundInhabitation = true;
                                        break;
                                }

                                if (blnFoundInhabitation && blnFoundPossession)
                                    break;
                            }
                            if (!blnFoundPossession)
                            {
                                lstPowerWhitelist.Add("Possession");
                            }
                            if (!blnFoundInhabitation)
                            {
                                lstPowerWhitelist.Add("Inhabitation");
                            }
                        }
                    }
                }
            }

            string strFilter = string.Empty;
            using (new FetchSafelyFromPool<StringBuilder>(Utils.StringBuilderPool, out StringBuilder sbdFilter))
            {
                sbdFilter.Append('(').Append(_objCharacter.Settings.BookXPath()).Append(')');
                if (!string.IsNullOrEmpty(strCategory) && strCategory != "Show All")
                {
                    sbdFilter.Append(" and (contains(category,").Append(strCategory.CleanXPath()).Append("))");
                }
                else
                {
                    bool blnHasToxic = false;
                    using (new FetchSafelyFromPool<StringBuilder>(Utils.StringBuilderPool,
                                                                  out StringBuilder sbdCategoryFilter))
                    {
                        foreach (string strItem in _lstCategory.Select(x => x.Value))
                        {
                            if (!string.IsNullOrEmpty(strItem))
                            {
                                sbdCategoryFilter.Append("(contains(category,").Append(strItem.CleanXPath())
                                                 .Append(")) or ");
                                if (strItem == "Toxic Critter Powers")
                                {
                                    sbdCategoryFilter.Append("toxic = ").Append(bool.TrueString.CleanXPath()).Append(" or ");
                                    blnHasToxic = true;
                                }
                            }
                        }

                        if (sbdCategoryFilter.Length > 0)
                        {
                            sbdCategoryFilter.Length -= 4;
                            sbdFilter.Append(" and (").Append(sbdCategoryFilter).Append(')');
                        }
                    }

                    if (!blnHasToxic)
                        sbdFilter.Append(" and (not(toxic) or toxic != ").Append(bool.TrueString.CleanXPath()).Append(')');
                }

                string strSearch = await txtSearch.DoThreadSafeFuncAsync(x => x.Text);
                if (!string.IsNullOrEmpty(strSearch))
                    sbdFilter.Append(" and ").Append(CommonFunctions.GenerateSearchXPath(strSearch));

                if (sbdFilter.Length > 0)
                    strFilter = '[' + sbdFilter.ToString() + ']';
            }

            foreach (XPathNavigator objXmlPower in _xmlBaseCritterPowerDataNode.Select("powers/power" + strFilter))
            {
                string strPowerName = (await objXmlPower.SelectSingleNodeAndCacheExpressionAsync("name"))?.Value ?? await LanguageManager.GetStringAsync("String_Unknown");
                if (!lstPowerWhitelist.Contains(strPowerName) && lstPowerWhitelist.Count != 0)
                    continue;
                if (!objXmlPower.RequirementsMet(_objCharacter, string.Empty, string.Empty)) continue;
                TreeNode objNode = new TreeNode
                {
                    Tag = (await objXmlPower.SelectSingleNodeAndCacheExpressionAsync("id"))?.Value ?? string.Empty,
                    Text = (await objXmlPower.SelectSingleNodeAndCacheExpressionAsync("translate"))?.Value ?? strPowerName
                };
                await trePowers.DoThreadSafeAsync(x => x.Nodes.Add(objNode));
            }
            await trePowers.DoThreadSafeAsync(x => x.Sort());
        }

        private void cmdOKAdd_Click(object sender, EventArgs e)
        {
            _blnAddAgain = true;
            AcceptForm();
        }

        private async void txtSearch_TextChanged(object sender, EventArgs e)
        {
            await RefreshCategory();
        }

        #endregion Control Events

        #region Methods

        /// <summary>
        /// Accept the selected item and close the form.
        /// </summary>
        private void AcceptForm()
        {
            string strSelectedPower = trePowers.SelectedNode?.Tag.ToString();
            if (string.IsNullOrEmpty(strSelectedPower))
                return;

            XPathNavigator objXmlPower = _xmlBaseCritterPowerDataNode.SelectSingleNode("powers/power[id = " + strSelectedPower.CleanXPath() + ']');
            if (objXmlPower == null)
                return;

            if (nudCritterPowerRating.Visible)
                _intSelectedRating = nudCritterPowerRating.ValueAsInt;

            _strSelectCategory = cboCategory.SelectedValue?.ToString() ?? string.Empty;
            _strSelectedPower = strSelectedPower;

            // If the character is a Free Spirit (PC, not the Critter version), populate the Power Points Cost as well.
            if (_objCharacter.Metatype == "Free Spirit" && !_objCharacter.IsCritter)
            {
                string strName = objXmlPower.SelectSingleNode("name")?.Value;
                if (!string.IsNullOrEmpty(strName))
                {
                    XPathNavigator objXmlOptionalPowerCost = _xmlMetatypeDataNode.SelectSingleNode("optionalpowers/power[. = " + strName.CleanXPath() + "]/@cost");
                    if (objXmlOptionalPowerCost != null)
                        _decPowerPoints = Convert.ToDecimal(objXmlOptionalPowerCost.Value, GlobalSettings.InvariantCultureInfo);
                }
            }

            DialogResult = DialogResult.OK;
        }

        private async void OpenSourceFromLabel(object sender, EventArgs e)
        {
            await CommonFunctions.OpenPdfFromControl(sender);
        }

        #endregion Methods

        #region Properties

        /// <summary>
        /// Whether or not the user wants to add another item after this one.
        /// </summary>
        public bool AddAgain => _blnAddAgain;

        /// <summary>
        /// Criter Power that was selected in the dialogue.
        /// </summary>
        public string SelectedPower => _strSelectedPower;

        /// <summary>
        /// Rating for the Critter Power that was selected in the dialogue.
        /// </summary>
        public int SelectedRating => _intSelectedRating;

        /// <summary>
        /// Power Point cost for the Critter Power (only applies to Free Spirits).
        /// </summary>
        public decimal PowerPoints => _decPowerPoints;

        #endregion Properties
    }
}
