/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Entity;
using OsEngine.Language;
using OsEngine.OsTrader.Panels.Tab.IndexEngine;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;

namespace OsEngine.OsTrader.Panels.Tab
{
    public partial class BotTabCustomIndexUi : Window
    {
        public BotTabCustomIndexUi(BotTabCustomIndex tab)
        {
            InitializeComponent();

            _tab = tab;
            _tab.SecuritiesSelectionClosedEvent += OnSecuritiesSelectionClosedEvent;

            _grid = DataGridFactory.GetDataGridView(DataGridViewSelectionMode.FullRowSelect, DataGridViewAutoSizeRowsMode.AllCells);
            _grid.CellValueChanged += GridOnCellValueChanged;
            _grid.CurrentCellDirtyStateChanged += GridOnCurrentCellDirtyStateChanged;
            HostComponents.Child = _grid;

            ComboVolatility.Items.Add(IndexVolatilityMode.Std.ToString());
            ComboVolatility.Items.Add(IndexVolatilityMode.Ewma.ToString());

            ComboStaleness.Items.Add(IndexStalenessMode.Auto.ToString());
            ComboStaleness.Items.Add(IndexStalenessMode.Bars.ToString());
            ComboStaleness.Items.Add(IndexStalenessMode.Seconds.ToString());

            ComboUpdateMode.Items.Add(IndexUpdateMode.OnEveryUpdate.ToString());
            ComboUpdateMode.Items.Add(IndexUpdateMode.OnClosedCandle.ToString());

            ReloadSettings();
            ReloadGrid();

            Title = OsLocalization.Trader.Label81;
        }

        private void ReloadGrid()
        {
            _grid.Rows.Clear();
            _grid.Columns.Clear();

            _grid.Columns.Add("Num", "#");
            _grid.Columns.Add("Unique", "Id");
            _grid.Columns.Add("Security", "Security");

            DataGridViewCheckBoxColumn enabled = new DataGridViewCheckBoxColumn
            {
                HeaderText = "Enabled",
                Width = 60
            };
            _grid.Columns.Add(enabled);

            DataGridViewCheckBoxColumn useInIndex = new DataGridViewCheckBoxColumn
            {
                HeaderText = "UseInIndex",
                Width = 80
            };
            _grid.Columns.Add(useInIndex);

            _grid.Columns.Add("IsCross", "Cross");
            _grid.Columns.Add("Direction", "UsdDir");

            for (int i = 0; i < _tab.Tabs.Count; i++)
            {
                var connector = _tab.Tabs[i];
                var component = _tab.Settings.Components.FirstOrDefault(c => c.UniqueName == connector.UniqueName);

                int row = _grid.Rows.Add();
                _grid.Rows[row].Cells[0].Value = i + 1;
                _grid.Rows[row].Cells[1].Value = connector.UniqueName;
                _grid.Rows[row].Cells[2].Value = connector.SecurityName;

                _grid.Rows[row].Cells[3].Value = component == null || component.Enabled;
                bool isCross = component != null && component.IsCross;
                _grid.Rows[row].Cells[4].Value = true;
                _grid.Rows[row].Cells[5].Value = isCross ? "Yes" : "No";
                _grid.Rows[row].Cells[6].Value = component?.UsdDirection ?? 0;
                _grid.Rows[row].Cells[4].ReadOnly = true;
            }

            _grid.Columns[0].Width = 35;
            _grid.Columns[1].Width = 120;
            _grid.Columns[2].Width = 160;
        }

        private void ReloadSettings()
        {
            ComboVolatility.SelectedItem = _tab.Settings.VolatilityMode.ToString();
            ComboStaleness.SelectedItem = _tab.Settings.StalenessMode.ToString();
            ComboUpdateMode.SelectedItem = _tab.Settings.UpdateMode.ToString();

            TextVolLookback.Text = _tab.Settings.VolLookbackBars.ToString();
            TextMaxStalenessBars.Text = _tab.Settings.MaxStalenessBars.ToString();
            TextMaxStalenessSeconds.Text = _tab.Settings.MaxStalenessSeconds.ToString();
            TextAutoStaleness.Text = _tab.Settings.AutoStalenessMultiplier.ToString(CultureInfo.InvariantCulture);
            TextMinActive.Text = _tab.Settings.MinActiveComponents.ToString();
            TextDepth.Text = _tab.Settings.CalculationDepth.ToString();
        }

        private void ButtonAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_tab.ShowNewSecurityDialog())
            {
                return;
            }
        }

        private void ButtonRemove_Click(object sender, RoutedEventArgs e)
        {
            if (_grid.SelectedCells.Count == 0)
            {
                return;
            }

            int rowIndex = _grid.SelectedCells[0].RowIndex;
            _tab.DeleteSecurityTab(rowIndex);
            ReloadGrid();
        }

        private void ButtonEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_grid.SelectedCells.Count == 0)
            {
                return;
            }

            int rowIndex = _grid.SelectedCells[0].RowIndex;
            _tab.ShowConnectorDialog(rowIndex);
            ReloadGrid();
        }

        private void ButtonApply_Click(object sender, RoutedEventArgs e)
        {
            if (_grid.IsCurrentCellDirty)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }

            _grid.EndEdit();
            ApplyGridValues();
            _tab.Save();
            ReloadGrid();
        }
        
        private void OnSecuritiesSelectionClosedEvent()
        {
            ReloadGrid();
        }

        private void ButtonSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TextVolLookback.Text, out int lookback))
            {
                _tab.Settings.VolLookbackBars = lookback;
            }

            if (int.TryParse(TextMaxStalenessBars.Text, out int maxBars))
            {
                _tab.Settings.MaxStalenessBars = maxBars;
            }

            if (int.TryParse(TextMaxStalenessSeconds.Text, out int maxSeconds))
            {
                _tab.Settings.MaxStalenessSeconds = maxSeconds;
            }

            if (decimal.TryParse(TextAutoStaleness.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal multiplier))
            {
                _tab.Settings.AutoStalenessMultiplier = multiplier;
            }

            if (int.TryParse(TextMinActive.Text, out int minActive))
            {
                _tab.Settings.MinActiveComponents = minActive;
            }

            if (int.TryParse(TextDepth.Text, out int depth))
            {
                _tab.Settings.CalculationDepth = depth;
            }

            if (Enum.TryParse(ComboVolatility.SelectedItem?.ToString(), out IndexVolatilityMode volMode))
            {
                _tab.Settings.VolatilityMode = volMode;
            }

            if (Enum.TryParse(ComboStaleness.SelectedItem?.ToString(), out IndexStalenessMode stalenessMode))
            {
                _tab.Settings.StalenessMode = stalenessMode;
            }

            if (Enum.TryParse(ComboUpdateMode.SelectedItem?.ToString(), out IndexUpdateMode updateMode))
            {
                _tab.Settings.UpdateMode = updateMode;
            }

            _tab.ApplySettings();
        }

        private void GridOnCurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (_grid.IsCurrentCellDirty)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void GridOnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count)
            {
                return;
            }

            if (e.ColumnIndex != 3 && e.ColumnIndex != 4)
            {
                return;
            }

            string uniqueName = _grid.Rows[e.RowIndex].Cells[1].Value?.ToString();

            if (string.IsNullOrEmpty(uniqueName))
            {
                return;
            }

            IndexComponentSettings component = _tab.Settings.Components.FirstOrDefault(c => c.UniqueName == uniqueName);

            if (component == null)
            {
                return;
            }

            bool enabled = Convert.ToBoolean(_grid.Rows[e.RowIndex].Cells[3].Value);
            bool useInIndex = Convert.ToBoolean(_grid.Rows[e.RowIndex].Cells[4].Value);

            component.Enabled = enabled;
            component.UseInIndex = useInIndex;

            _tab.Save();
        }

        private void ApplyGridValues()
        {
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                string uniqueName = _grid.Rows[i].Cells[1].Value?.ToString();

                if (string.IsNullOrEmpty(uniqueName))
                {
                    continue;
                }

                IndexComponentSettings component = _tab.Settings.Components.FirstOrDefault(c => c.UniqueName == uniqueName);

                if (component == null)
                {
                    continue;
                }

                object enabledCell = _grid.Rows[i].Cells[3].Value;
                object useInIndexCell = _grid.Rows[i].Cells[4].Value;

                component.Enabled = enabledCell != null && Convert.ToBoolean(enabledCell);
                component.UseInIndex = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _tab.SecuritiesSelectionClosedEvent -= OnSecuritiesSelectionClosedEvent;
            base.OnClosed(e);
        }

        private readonly BotTabCustomIndex _tab;
        private readonly DataGridView _grid;
    }
}
