﻿using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Kbg.NppPluginNET;
using JSON_Tools.JSON_Tools;
using JSON_Tools.Utils;

namespace JSON_Tools.Forms
{
    public partial class ErrorForm : Form
    {
        /// <summary>
        /// if there are at least this many lints (completely arbitrary), warn the user 
        /// </summary>
        public const int SLOW_RELOAD_THRESHOLD = 300;
        /// <summary>
        /// there *cannot* be more than this many rows in the table, because otherwise it just gets insanely slow
        /// </summary>
        public const int LINT_MAX_ROW_COUNT = 5000;
        public string fname;
        public List<JsonLint> lints;

        public ErrorForm(string fname, List<JsonLint> lints)
        {
            InitializeComponent();
            NppFormHelper.RegisterFormIfModeless(this, false);
            Reload(fname, lints, true);
            FormStyle.ApplyStyle(this, Main.settings.use_npp_styling);
        }

        public bool SlowReloadExpected(IList<JsonLint> lints) { return lints is null || lints.Count >= SLOW_RELOAD_THRESHOLD; }

        public void Reload(string fname, List<JsonLint> lints, bool onStartup = false)
        {
            bool wasBig = SlowReloadExpected(lints);
            if (wasBig && !onStartup)
            {
                // for reasons beyond my comprehension, deleting the form and starting over is faster than refreshing when there are a lot of rows
                Npp.notepad.HideDockingForm(this);
                Close();
                Main.OpenErrorForm(fname, false);
                return;
            }
            this.fname = fname;
            this.lints = lints;
            Text = "JSON errors in current file";
            ErrorGrid.Rows.Clear();
            int interval = 1;
            int lintCount = lints is null ? 0 : lints.Count;
            // add a row that warns not all rows are shown
            if (LINT_MAX_ROW_COUNT < lintCount)
            {
                interval = lintCount / LINT_MAX_ROW_COUNT;
                var warningNotAllRowsShown = new DataGridViewRow();
                warningNotAllRowsShown.CreateCells(ErrorGrid);
                warningNotAllRowsShown.Cells[1].Value = $"Showing approximately {LINT_MAX_ROW_COUNT} of {lintCount} rows";
                ErrorGrid.Rows.Add(warningNotAllRowsShown);
            }
            int ii = 0;
            int cycler = 0;
            while (ii < lintCount)
            {
                var lint = lints[ii];
                var row = new DataGridViewRow();
                row.CreateCells(ErrorGrid);
                row.Cells[0].Value = lint.severity;
                row.Cells[1].Value = lint.message;
                row.Cells[2].Value = lint.pos;
                ErrorGrid.Rows.Add(row);
                if (interval == 1)
                    ii++;
                else
                {
                    // Do not increase by exactly interval every time.
                    // Instead change the interval so that any regular pattern in the errors
                    //     that is not relatively prime with the interval will still be exposed.
                    ii += (cycler % (2 * interval)) + 1;
                    cycler++;
                }
            }
            if (wasBig)
                Npp.notepad.ShowDockingForm(this);
        }

        private void ErrorGrid_CellEnter(object sender, DataGridViewCellEventArgs e)
        {
            int rowIndex = e.RowIndex;
            if (!IsValidRowIndex(rowIndex))
                return;
            DataGridViewRow row = ErrorGrid.Rows[rowIndex];
            HandleCellOrRowClick(row);
        }

        /// <summary>
        /// If the row corresponds to a lint, go to that lint's position.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HandleCellOrRowClick(DataGridViewRow row)
        {
            if (!(row.Cells[2].Value is int pos))
                return;
            Npp.editor.GotoPos(pos);
            this.Focus();
        }

        private void ErrorGrid_Resize(object sender, EventArgs e)
        {
            ErrorGrid.Columns[0].Width = 75;
            ErrorGrid.Columns[1].Width = ErrorGrid.Width - 218;
            ErrorGrid.Columns[2].Width = 75;
        }

        private bool IsValidRowIndex(int index)
        {
            return index >= 0 && index < ErrorGrid.RowCount;
        }

        private void ChangeSelectedRow(int oldIndex, int newIndex)
        {
            DataGridViewRow oldRow = ErrorGrid.Rows[oldIndex];
            DataGridViewRow newRow = ErrorGrid.Rows[newIndex];
            oldRow.Selected = false;
            newRow.Selected = true;
            int rowsVisible = ErrorGrid.GetCellCount(DataGridViewElementStates.Displayed) / 3;
            int firstRowToShow = newIndex - rowsVisible + 2;
            // scroll up if new row is above old; scroll down if new row is below old
            if ((firstRowToShow > ErrorGrid.FirstDisplayedScrollingRowIndex) == (newIndex > oldIndex)
                && IsValidRowIndex(firstRowToShow))
                ErrorGrid.FirstDisplayedScrollingRowIndex = firstRowToShow;
            else if (firstRowToShow < 0)
                ErrorGrid.FirstDisplayedScrollingRowIndex = 0;
            HandleCellOrRowClick(newRow); // move to location of error
        }

        /// <summary>
        /// right-clicking the grid shows a drop-down where the user can choose to refresh the form or export the lints as JSON.
        /// </summary>
        private void ErrorForm_RightClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                errorGridRightClickStrip.Show(MousePosition);
            }
        }

        private void RefreshMenuItem_Click(object sender, EventArgs e)
        {
            Npp.notepad.HideDockingForm(this);
            Main.OpenErrorForm(Npp.notepad.GetCurrentFilePath(), false);
        }

        private void ExportLintsToJsonMenuItem_Click(object sender, EventArgs e)
        {
            int lintCount = lints == null ? 0 : lints.Count;
            if (lintCount == 0)
            {
                MessageBox.Show($"No JSON syntax errors (at or below {Main.settings.logger_level} level) for {fname}",
                    "No JSON syntax errors for this file",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var lintArrChildren = new List<JNode>();
            foreach (JsonLint lint in lints)
            {
                var lintObj = lint.ToJson();
                lintArrChildren.Add(lintObj);
            }
            var lintArr = new JArray(0, lintArrChildren);
            Main.PrettyPrintJsonInNewFile(lintArr);
        }

        /// <summary>
        /// hitting enter refreshes<br></br>
        /// Hitting escape moves focus to the Notepad++ editor<br></br>
        /// hitting the first letter of any error description goes to that error description
        /// </summary>
        private void ErrorForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                // refresh error form based on current contents of current file
                e.Handled = true;
                // temporarily turn off offer_to_show_lint prompt, because the user obviously wants to see it
                bool previousOfferToShowLint = Main.settings.offer_to_show_lint;
                Main.settings.offer_to_show_lint = false;
                Main.TryParseJson(preferPreviousDocumentType:true);
                Main.settings.offer_to_show_lint = previousOfferToShowLint;
                if (Main.TryGetInfoForFile(Main.activeFname, out JsonFileInfo info)
                    && info.lints != null)
                    Reload(Main.activeFname, info.lints);
                return;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Npp.editor.GrabFocus();
            }
            var selRowIndex = ErrorGrid.SelectedCells[0].RowIndex;
            if (!IsValidRowIndex(selRowIndex))
                return;
            if (e.KeyCode == Keys.Up && selRowIndex > 0) // move up
                ChangeSelectedRow(selRowIndex, selRowIndex - 1);
            else if (e.KeyCode == Keys.Down && selRowIndex < ErrorGrid.RowCount - 1)
                ChangeSelectedRow(selRowIndex, selRowIndex + 1); // move down
            // for most keys, seek first row after current row
            // whose description start with that key's char
            else if (e.KeyValue >= ' ' && e.KeyValue <= 126)
            {
                if (ErrorGrid.SelectedCells.Count == 0)
                    return;
                char startChar = (char)e.KeyValue;
                if (!SearchForErrorStartingWithChar(startChar, selRowIndex + 1, ErrorGrid.RowCount, selRowIndex))
                {
                    // wrap around
                    SearchForErrorStartingWithChar(startChar, 0, selRowIndex, selRowIndex);
                }
            }
        }

        /// <summary>
        /// Iterates through rows between indices start (inclusive) and end (exclusive)
        /// until it finds an error whose description begins with startChar.<br></br>
        /// If a row is found, unselect initiallySelected and select the found row.<br></br>
        /// Returns true if such a row was found.
        /// </summary>
        /// <param name="startChar"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        private bool SearchForErrorStartingWithChar(char startChar, int start, int end, int initiallySelected)
        {
            for (int ii = start; ii < end; ii++)
            {
                var row = ErrorGrid.Rows[ii];
                if (!(row.Cells[1].Value is string description))
                    break;
                var descStartChar = description[0];
                if (descStartChar == startChar ||
                    ((startChar >= 'A' && startChar <= 'Z') && descStartChar == startChar + 'a' - 'A'))
                {
                    ChangeSelectedRow(initiallySelected, ii);
                    return true;
                }
            }
            return false;
        }
    }
}
