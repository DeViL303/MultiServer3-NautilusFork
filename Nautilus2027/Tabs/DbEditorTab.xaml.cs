using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Xml.Linq;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using ColorPicker;
using System.Windows.Media.Imaging;
using System.Threading;
using Newtonsoft.Json;
using Ookii.Dialogs.Wpf;
using System.Collections.Concurrent;
using System.Globalization;
using System.Xml;
using HomeTools.AFS;
using HomeTools.BARFramework;
using HomeTools.ChannelID;
using HomeTools.UnBAR;
using CustomLogger;
using System.IO.Compression;
using System.Net.Http;
using System.Net;
using Microsoft.Web.WebView2.Core;
using System.Net.NetworkInformation;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Data.SQLite;
using System.Buffers;
using CsvHelper;
using System.Collections.ObjectModel;
using HomeTools.Crypto;
using HomeTools.CDS;

namespace Nautilus2027
{
    // "DB Editor" tab.
    public partial class DbEditorTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        public DbEditorTab()
        {
            InitializeComponent();
        }

        private void Border_ODCMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Create an OpenFileDialog to allow the user to select an .odc or .xml file
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ODC files (*.odc)|*.odc|XML files (*.xml)|*.xml|All files (*.*)|*.*",
                Title = "Select an ODC or XML file"
            };

            // Show the dialog and check if the user selected a file
            if (openFileDialog.ShowDialog() == true)
            {
                string selectedFilePath = openFileDialog.FileName;

                // Simulate the drop functionality
                Main.LogDebugInfo($"File selected: {selectedFilePath}");
                ProcessDroppedFile(selectedFilePath);
            }
        }

        private void Border_SQLMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Create an OpenFileDialog to allow the user to select an .sql file
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*",
                Title = "Select an SQL file"
            };

            // Show the dialog and check if the user selected a file
            if (openFileDialog.ShowDialog() == true)
            {
                string selectedFilePath = openFileDialog.FileName;
                string fileExtension = Path.GetExtension(selectedFilePath).ToLower();

                // Ensure the selected file is a .sql file
                if (fileExtension == ".sql")
                {
                    HandleSQLFile(selectedFilePath);
                }
                else
                {
                    MessageBox.Show("Only SQL files are supported.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private int NexTIndex { get; set; }
        private Dictionary<string, Dictionary<string, string>> unsavedChanges = new Dictionary<string, Dictionary<string, string>>();

        private void BrowseSQLButton_Click(object sender, RoutedEventArgs e)
        {
            ClearUnsavedChanges();
            var initialDirectory = Main._settings.SqlOutputDirectory;

            // Create the directory if it does not exist
            if (!Directory.Exists(initialDirectory))
            {
                Directory.CreateDirectory(initialDirectory);
            }

            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                InitialDirectory = initialDirectory,
                RestoreDirectory = true,
                Filter = "All supported files (*.sql;*.hcdb;*.xml)|*.sql;*.hcdb;*.xml|SQL files (*.sql)|*.sql|HCDB files (*.hcdb)|*.hcdb|XML files (*.xml)|*.xml|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string fileExtension = Path.GetExtension(filePath).ToLower();

                switch (fileExtension)
                {
                    case ".sql":
                        HandleSQLFile(filePath);
                        break;
                    case ".hcdb":
                        HandleHCDBFile(filePath);
                        break;
                    case ".xml":
                        HandleXMLFile(filePath);
                        break;
                    default:
                        MessageBox.Show("Unsupported file type selected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        break;
                }
            }
        }

        private void Border_DragSQLEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string fileExtension = Path.GetExtension(files[0]).ToLower();
                    if (fileExtension == ".sql")
                    {
                        e.Effects = DragDropEffects.Copy;
                    }
                    else
                    {
                        e.Effects = DragDropEffects.None;
                    }
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Border_SQLDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string filePath = files[0];
                    string fileExtension = Path.GetExtension(filePath).ToLower();

                    if (fileExtension == ".sql")
                    {
                        HandleSQLFile(filePath);
                    }
                    else
                    {
                        MessageBox.Show("Only SQL files are supported for drag and drop.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            e.Handled = true;
        }


        private async void HandleSQLFile(string filePath)
        {
            Main.LogDebugInfo($"HandleSQLFile invoked with filePath: {filePath}");
            Main.sqlFilePath = filePath;  // Store the file path

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                int maxIndex = LoadMaxIndexFromSQLFile(filePath);

                // Set the NexTIndex property
                NexTIndex = maxIndex + 1;

                // Update the TextBox with NexTIndex
                txtObjectIndex.Text = NexTIndex.ToString();

                // Populate the grid with entries starting from index 0
                await PopulateGridWithEntries(0, 25); // Default count value of 25

                stopwatch.Stop();
                Main.LogDebugInfo($"SQL file processed in {stopwatch.ElapsedMilliseconds} ms. Max Index: {maxIndex}, NexTIndex: {NexTIndex}");
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error loading and parsing SQL file: {ex.Message}");
                // Suppress the error message box
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        private int LoadMaxIndexFromSQLFile(string filePath)
        {
            int maxIndex = -1;
            bool retry = true;
            int attempts = 0;

            while (retry && attempts < 2)
            {
                SQLiteConnection connection = null;
                attempts++;

                try
                {
                    string connectionString = $"Data Source={filePath};Version=3;";
                    connection = new SQLiteConnection(connectionString);
                    connection.Open();
                    Main.LogDebugInfo("SQLite connection opened successfully.");

                    string query = @"
            SELECT MAX(ObjectIndex) as MaxIndex
            FROM Objects";

                    using (SQLiteCommand command = new SQLiteCommand(query, connection))
                    {
                        object result = command.ExecuteScalar();
                        if (result != DBNull.Value && result != null)
                        {
                            maxIndex = Convert.ToInt32(result);
                        }
                    }

                    retry = false; // If successful, exit loop
                }
                catch (Exception ex)
                {
                    Main.LogDebugInfo($"Error loading max index from SQL file on attempt {attempts}: {ex.Message}");

                    if (attempts < 2)
                    {
                        Main.LogDebugInfo("Retrying...");
                    }
                    else
                    {
                        Main.LogDebugInfo($"Error loading max index from SQL file: {ex.Message}");
                    }
                }
                finally
                {
                    connection?.Close();
                }
            }

            return maxIndex;
        }

        private async void GoToIndexButton_Click(object sender, RoutedEventArgs e)
        {
            ClearUnsavedChanges();
            string input = txtGoToIndex.Text;
            if (string.IsNullOrWhiteSpace(input))
            {
                Main.LogDebugInfo("Input is empty.");
                return;
            }

            if (input.Contains('-'))
            {
                // Handle range input
                var parts = input.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0], out int startIndex) && int.TryParse(parts[1], out int endIndex))
                {
                    if (startIndex > endIndex)
                    {
                        Main.LogDebugInfo("Invalid range entered: start index is greater than end index.");
                        MessageBox.Show("Invalid range: start index is greater than end index.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    Main.LogDebugInfo($"GoToIndexButton_Click invoked with range: {startIndex}-{endIndex}");
                    await PopulateGridWithEntries(startIndex, endIndex - startIndex + 1);
                }
                else
                {
                    Main.LogDebugInfo("Invalid range entered: unable to parse start or end index.");
                    MessageBox.Show("Invalid range entered. Please enter a valid range.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (int.TryParse(input, out int startIndex))
            {
                // Handle single index input
                Main.LogDebugInfo($"GoToIndexButton_Click invoked with startIndex: {startIndex}");
                await PopulateGridWithEntries(startIndex, 25);  // Load 50 entries starting from the specified index
            }
            else
            {
                Main.LogDebugInfo("Invalid index entered.");
                MessageBox.Show("Invalid index entered. Please enter a valid index.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void HandleHCDBFile(string filePath)
        {
            // Add your code to handle HCDB files here
            MessageBox.Show($"Handling HCDB file: {filePath}", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void HandleXMLFile(string filePath)
        {
            // Add your code to handle XML files here
            MessageBox.Show($"Handling XML file: {filePath}", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private List<Dictionary<string, string>> LoadEntriesRange(string filePath, int startIndex, int count)
        {
            var entries = new List<Dictionary<string, string>>();

            try
            {
                Main.LogDebugInfo($"Loading {count} object indexes from {filePath}, starting at index {startIndex}.");
                string connectionString = $"Data Source={filePath};Version=3;";
                using (SQLiteConnection connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    Main.LogDebugInfo("SQLite connection opened successfully.");

                    // Step 1: Fetch the ObjectIndexes in the specified range
                    string fetchIndexesQuery = @"
                SELECT ObjectIndex
                FROM Objects
                WHERE ObjectIndex >= @startIndex
                ORDER BY ObjectIndex
                LIMIT @count";

                    var objectIndexes = new List<int>();
                    using (SQLiteCommand fetchIndexesCommand = new SQLiteCommand(fetchIndexesQuery, connection))
                    {
                        fetchIndexesCommand.Parameters.AddWithValue("@startIndex", startIndex);
                        fetchIndexesCommand.Parameters.AddWithValue("@count", count);

                        using (SQLiteDataReader reader = fetchIndexesCommand.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                objectIndexes.Add(reader.GetInt32(0));
                            }
                        }
                    }

                    if (objectIndexes.Count == 0)
                    {
                        return entries; // No entries to process
                    }

                    // Step 2: Fetch the Objects and their Metadata for the selected ObjectIndexes
                    string query = @"
                SELECT 
                    o.ObjectIndex, 
                    o.ObjectId, 
                    o.Version, 
                    o.Location, 
                    o.InventoryEntryType, 
                    o.ArchiveTimeStamp, 
                    o.OdcSha1Digest, 
                    o.EntitlementIndex, 
                    o.RewardIndex, 
                    o.UserLocation, 
                    o.UserDateLastUsed,
                    m.KeyName,
                    m.Value,
                    " + metaRowIdSelect + @"
                FROM Objects o
                LEFT JOIN Metadata m ON o.ObjectIndex = m.ObjectIndex
                WHERE o.ObjectIndex IN (" + string.Join(",", objectIndexes) + @")
                ORDER BY o.ObjectIndex" + (metaRowIdSelect.StartsWith("m.rowid") ? ", m.rowid" : "");

                    // a Metadata table without rowids (rare): edits then match rows by key and value, as before
                    if (metaRowIdSelect.StartsWith("m.rowid"))
                        try { using (var probe = new SQLiteCommand("SELECT rowid FROM Metadata LIMIT 1", connection)) probe.ExecuteScalar(); }
                        catch (SQLiteException)
                        {
                            Main.LogDebugInfo("DB Editor: the Metadata table has no rowid; rows are matched by key and value");
                            metaRowIdSelect = "NULL AS MetaRowId";
                            query = query.Replace("m.rowid AS MetaRowId", metaRowIdSelect).Replace(", m.rowid", "");
                        }
                    using (SQLiteCommand command = new SQLiteCommand(query, connection))
                    {
                        using (SQLiteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var entry = new Dictionary<string, string>();
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    string columnName = reader.GetName(i);
                                    object value = reader.GetValue(i);

                                    if (columnName == "OdcSha1Digest" && value != DBNull.Value)
                                    {
                                        // Convert the BLOB to a hex string
                                        byte[] blob = (byte[])value;
                                        entry[columnName] = BitConverter.ToString(blob).Replace("-", "").ToUpper();
                                    }
                                    else if ((columnName == "ArchiveTimeStamp" || columnName == "UserDateLastUsed") && value != DBNull.Value)
                                    {
                                        // Convert the timestamp to an 8-character hexadecimal string with leading zeros
                                        // stored as signed 32-bit by this tool, but older tools may have stored them unsigned
                                        entry[columnName] = Main.TimestampHex(value);
                                    }
                                    else
                                    {
                                        entry[columnName] = value.ToString();
                                    }
                                }
                                entries.Add(entry);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error loading entries from SQL file: {ex.Message}");
            }

            return entries;
        }



        private string metaRowIdSelect = "m.rowid AS MetaRowId";
        private int currentStartIndex = 0; // the first ObjectIndex asked for on the page shown
        private int currentCount = 25;      // how many objects the page shows
        private int lastShownIndex = -1;    // the last ObjectIndex on the page shown

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            ClearUnsavedChanges();
            // indexes can have gaps, so the next page starts after the last object shown
            int next = lastShownIndex >= 0 ? lastShownIndex + 1 : currentStartIndex + 25;
            PopulateGridWithEntriesSafe(next, 25);
            ScrollToTop();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ClearUnsavedChanges();
            PopulateGridWithEntriesSafe(PreviousPageStart(currentStartIndex, 25), 25);
            ScrollToTop();
        }

        /// <summary>The ObjectIndex that starts the page of <paramref name="count"/> objects before <paramref name="start"/>.</summary>
        private int PreviousPageStart(int start, int count)
        {
            try
            {
                using (var connection = new SQLiteConnection($"Data Source={Main.sqlFilePath};Version=3;"))
                {
                    connection.Open();
                    using (var command = new SQLiteCommand("SELECT MIN(ObjectIndex) FROM (SELECT ObjectIndex FROM Objects WHERE ObjectIndex < @start ORDER BY ObjectIndex DESC LIMIT @count)", connection))
                    {
                        command.Parameters.AddWithValue("@start", start);
                        command.Parameters.AddWithValue("@count", count);
                        object r = command.ExecuteScalar();
                        if (r != null && r != DBNull.Value) return Convert.ToInt32(r);
                    }
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"DB Editor: could not find the previous page: {ex.Message}");
            }
            return Math.Max(0, start - count);
        }

        private void ClearUnsavedChanges()
        {
            unsavedChanges.Clear();
            UnsavedChanges.Visibility = Visibility.Hidden;
        }


        internal async void PopulateGridWithEntriesSafe(int startIndex, int count)
        {
            Main.LogDebugInfo($"PopulateGridWithEntriesSafe invoked with startIndex: {startIndex}, count: {count}");

            try
            {
                await PopulateGridWithEntries(startIndex, count);
                ScrollToTop(); // Scroll to the top after populating entries
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error populating grid: {ex.Message}");
                MessageBox.Show($"Error populating grid: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ScrollToTop()
        {
            sqlItemsScrollViewer?.ScrollToTop();
        }
        private async Task PopulateGridWithEntries(int startIndex, int count)
        {
            Main.LogDebugInfo($"PopulateGridWithEntries invoked with startIndex: {startIndex}, count: {count}");

            var entries = LoadEntriesRange(Main.sqlFilePath, startIndex, count);

            if (entries == null || entries.Count == 0)
            {
                Main.LogDebugInfo("No entries loaded from database.");
                return;
            }
            currentStartIndex = startIndex;
            currentCount = count;
            lastShownIndex = entries.Select(x => int.TryParse(x["ObjectIndex"], out int oi) ? oi : -1).DefaultIfEmpty(-1).Max();

            // Define column widths
            var columnWidths = new Dictionary<string, GridLength>
    {
        { "ObjectIndex", new GridLength(50) },
        { "ObjectId", new GridLength(245) },
        { "Version", new GridLength(48) },
        { "Location", new GridLength(57) },
        { "InventoryEntryType", new GridLength(43) },
        { "ArchiveTimeStamp", new GridLength(72) },
        { "OdcSha1Digest", new GridLength(300) },
        { "EntitlementIndex", new GridLength(54) },
        { "RewardIndex", new GridLength(59) },
        { "UserLocation", new GridLength(55) },
        { "UserDateLastUsed", new GridLength(48) },
        { "KeyName", new GridLength(135) },
        { "Value", new GridLength(310) }
    };

            // Clear existing children and column definitions
            SQLItemsGrid.Children.Clear();
            SQLItemsGrid.ColumnDefinitions.Clear();
            SQLItemsGrid.RowDefinitions.Clear();

            // Add column definitions
            foreach (var column in columnWidths)
            {
                SQLItemsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = column.Value });
            }

            // Add row definitions for each entry
            foreach (var entry in entries)
            {
                SQLItemsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            // Sort and group metadata entries by ObjectIndex
            var sortedEntries = entries.GroupBy(e => e["ObjectIndex"])
                                       .Select(g => new
                                       {
                                           ObjectIndex = g.Key,
                                           Entries = g.OrderBy(e =>
                                           {
                                               var keyName = e["KeyName"];
                                               if (keyName.Contains("CLOTHING") || keyName.Contains("FURNITURE") || keyName.Contains("MINI_GAME"))
                                                   return 1;
                                               if (keyName.Contains("RIGS"))
                                                   return 2;
                                               if (keyName.Contains("ENTITLEMENT_ID"))
                                                   return 3;
                                               return keyName.Contains("_HEAT") ? 5 : 4; // Default order for other entries, _HEAT last
                                           }).ThenBy(e => e["KeyName"]).ToList() // Ensure consistent ordering within groups
                                       });

            // Add data incrementally
            int rowIndex = 0;
            foreach (var group in sortedEntries)
            {
                foreach (var entry in group.Entries)
                {
                    int colIndex = 0;
                    foreach (var column in columnWidths.Keys)
                    {
                        string displayValue = entry.ContainsKey(column) ? entry[column] : string.Empty;

                        // Modify the display value based on specific conditions
                        if (column == "Value")
                        {
                            if (displayValue == "00000000-00000000-00000010-00000000")
                            {
                                displayValue += " (Male)";
                            }
                            else if (displayValue == "00000000-00000000-00000010-00000001")
                            {
                                displayValue += " (Female)";
                            }
                        }

                        var textBox = new TextBox
                        {
                            Text = displayValue,
                            Margin = new Thickness(2),
                            Style = (Style)FindResource("SmallTextBoxStyle2")
                        };

                        // Make ObjectIndex and ObjectId read-only
                        if (column == "ObjectIndex" || column == "ObjectId")
                        {
                            textBox.IsReadOnly = true;
                        }

                        // Make other fields editable including Version, KeyName, Value, SHA1, and Timestamp
                        if (column == "Version" || column == "KeyName" || column == "Value" || column == "OdcSha1Digest" || column == "ArchiveTimeStamp")
                        {
                            // Store original key name and value
                            string originalKeyName = entry.ContainsKey("KeyName") ? entry["KeyName"] : string.Empty;
                            string originalValue = entry.ContainsKey("Value") ? entry["Value"] : string.Empty;

                            textBox.Tag = new { OriginalKeyName = originalKeyName, OriginalValue = originalValue };

                            // each grid row is one object + one of its metadata rows (by rowid), so changes are kept per row:
                            // keeping them per object mixed up the rows of an object and wrote one row's edit into another
                            string rowKey = entry["ObjectIndex"] + "|" + (entry.ContainsKey("MetaRowId") ? entry["MetaRowId"] : "");
                            textBox.TextChanged += (sender, e) =>
                            {
                                textBox.Foreground = Brushes.Yellow; // Change text color to yellow
                                UnsavedChanges.Visibility = Visibility.Visible;
                                if (!unsavedChanges.TryGetValue(rowKey, out var rowChanges))
                                    unsavedChanges[rowKey] = rowChanges = new Dictionary<string, string>();
                                rowChanges[column] = textBox.Text;
                            };

                            textBox.KeyDown += async (sender, e) =>
                            {
                                if (e.Key != Key.Enter) return;
                                try
                                {
                                    if (unsavedChanges.TryGetValue(rowKey, out var rowChanges) && rowChanges.Count > 0)
                                        await SaveRowChanges(entry, rowChanges);
                                }
                                catch (Exception ex)
                                {
                                    Main.LogDebugInfo($"DB Editor: could not save the row: {ex.Message}");
                                    MessageBox.Show($"Could not save the row: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                                unsavedChanges.Remove(rowKey);
                                if (unsavedChanges.Count == 0) UnsavedChanges.Visibility = Visibility.Hidden;
                                await PopulateGridWithEntries(currentStartIndex, currentCount); // Reload the same page
                            };
                        }
                        else
                        {
                            textBox.IsReadOnly = true;
                        }

                        Grid.SetRow(textBox, rowIndex);
                        Grid.SetColumn(textBox, colIndex++);
                        SQLItemsGrid.Children.Add(textBox);
                    }
                    rowIndex++;
                }

                // Update the UI
                await Task.Delay(1);  // Small delay to ensure the UI updates incrementally
            }
        }

        private static string WithoutRigNote(string value)
        {
            if (value == null) return null;
            foreach (var note in new[] { " (Male)", " (Female)" })
                if (value.EndsWith(note, StringComparison.Ordinal)) return value.Substring(0, value.Length - note.Length);
            return value;
        }

        /// <summary>Saves one grid row: object columns to Objects, KeyName / Value to that row's own Metadata row (by rowid).</summary>
        private async Task SaveRowChanges(Dictionary<string, string> entry, Dictionary<string, string> changes)
        {
            string objectIndex = entry["ObjectIndex"];
            string metaRowId = entry.ContainsKey("MetaRowId") ? entry["MetaRowId"] : "";
            foreach (var col in new[] { "Version", "OdcSha1Digest", "ArchiveTimeStamp" })
                if (changes.TryGetValue(col, out string v)) await UpdateDatabaseEntry(objectIndex, col, v.Trim());

            bool keyChanged = changes.TryGetValue("KeyName", out string newKey);
            bool valueChanged = changes.TryGetValue("Value", out string newValue);
            if (!keyChanged && !valueChanged) return;
            string key = (keyChanged ? newKey : entry.ContainsKey("KeyName") ? entry["KeyName"] : "")?.Trim() ?? "";
            string value = WithoutRigNote(valueChanged ? newValue : entry.ContainsKey("Value") ? entry["Value"] : "") ?? "";

            using (var connection = new SQLiteConnection($"Data Source={Main.sqlFilePath};Version=3;"))
            {
                await connection.OpenAsync();
                SQLiteCommand command;
                string oldKey = entry.ContainsKey("KeyName") ? entry["KeyName"] : "", oldValue = entry.ContainsKey("Value") ? entry["Value"] : "";
                if (string.IsNullOrEmpty(metaRowId) && oldKey.Length > 0)
                {
                    // no rowids in this file: match the row by its old key and value
                    command = key.Length == 0
                        ? new SQLiteCommand("DELETE FROM Metadata WHERE ObjectIndex = @objectIndex AND KeyName = @oldKey AND Value = @oldValue", connection)
                        : new SQLiteCommand("UPDATE Metadata SET KeyName = @key, Value = @value WHERE ObjectIndex = @objectIndex AND KeyName = @oldKey AND Value = @oldValue", connection);
                    command.Parameters.AddWithValue("@objectIndex", objectIndex);
                    command.Parameters.AddWithValue("@oldKey", oldKey);
                    command.Parameters.AddWithValue("@oldValue", oldValue);
                }
                else if (string.IsNullOrEmpty(metaRowId))
                {
                    // an object with no metadata yet: a key typed into its row adds one
                    if (key.Length == 0) return;
                    command = new SQLiteCommand("INSERT INTO Metadata (ObjectIndex, KeyName, Value) VALUES (@objectIndex, @key, @value)", connection);
                    command.Parameters.AddWithValue("@objectIndex", objectIndex);
                }
                else if (key.Length == 0)
                {
                    command = new SQLiteCommand("DELETE FROM Metadata WHERE rowid = @rowid AND ObjectIndex = @objectIndex", connection);
                    command.Parameters.AddWithValue("@rowid", long.Parse(metaRowId));
                    command.Parameters.AddWithValue("@objectIndex", objectIndex);
                }
                else
                {
                    command = new SQLiteCommand("UPDATE Metadata SET KeyName = @key, Value = @value WHERE rowid = @rowid AND ObjectIndex = @objectIndex", connection);
                    command.Parameters.AddWithValue("@rowid", long.Parse(metaRowId));
                    command.Parameters.AddWithValue("@objectIndex", objectIndex);
                }
                if (command.CommandText.Contains("@key")) command.Parameters.AddWithValue("@key", key);
                if (command.CommandText.Contains("@value")) command.Parameters.AddWithValue("@value", value);
                using (command) await command.ExecuteNonQueryAsync();
                Main.LogDebugInfo($"DB Editor: ObjectIndex {objectIndex}, metadata row {(metaRowId.Length > 0 ? metaRowId : "(new)")}: " +
                                  (key.Length == 0 ? "deleted" : $"{key} = {value}"));
            }
        }

        private async Task UpdateDatabaseEntry(string objectIndex, string columnName, string newValue)
        {
            try
            {
                string connectionString = $"Data Source={Main.sqlFilePath};Version=3;";
                using (SQLiteConnection connection = new SQLiteConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string updateQuery = string.Empty;
                    SQLiteCommand command = new SQLiteCommand(connection);

                    if (columnName == "ArchiveTimeStamp")
                    {
                        int timestampValue = Main.ConvertHexToInt(newValue);
                        updateQuery = $@"
                    UPDATE Objects
                    SET {columnName} = @newValue
                    WHERE ObjectIndex = @objectIndex";
                        command.Parameters.AddWithValue("@newValue", timestampValue);
                    }
                    else if (columnName == "OdcSha1Digest")
                    {
                        byte[] sha1Value = ConvertHexToByteArray(newValue);
                        updateQuery = $@"
                    UPDATE Objects
                    SET {columnName} = @newValue
                    WHERE ObjectIndex = @objectIndex";
                        command.Parameters.AddWithValue("@newValue", sha1Value);
                    }
                    else if (columnName == "Version")
                    {
                        updateQuery = $@"
                    UPDATE Objects
                    SET {columnName} = @newValue
                    WHERE ObjectIndex = @objectIndex";
                        command.Parameters.AddWithValue("@newValue", newValue);
                    }

                    command.CommandText = updateQuery;
                    command.Parameters.AddWithValue("@objectIndex", objectIndex);
                    await command.ExecuteNonQueryAsync();

                    Main.LogDebugInfo($"Database entry updated: ObjectIndex={objectIndex}, Column={columnName}, NewValue={newValue}");
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error updating database entry: {ex.Message}");
                MessageBox.Show($"Error updating database entry: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task UpdateMetadataEntry(string objectIndex, string columnName, string newValue, string originalKeyName, string originalValue)
        {
            try
            {
                string connectionString = $"Data Source={Main.sqlFilePath};Version=3;";
                using (SQLiteConnection connection = new SQLiteConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string updateQuery = $@"
                UPDATE Metadata
                SET {columnName} = @newValue
                WHERE ObjectIndex = @objectIndex AND KeyName = @originalKeyName AND Value = @originalValue";

                    SQLiteCommand command = new SQLiteCommand(updateQuery, connection);
                    command.Parameters.AddWithValue("@newValue", newValue);
                    command.Parameters.AddWithValue("@objectIndex", objectIndex);
                    command.Parameters.AddWithValue("@originalKeyName", originalKeyName);
                    command.Parameters.AddWithValue("@originalValue", originalValue);

                    await command.ExecuteNonQueryAsync();

                    Main.LogDebugInfo($"Metadata entry updated: ObjectIndex={objectIndex}, Column={columnName}, NewValue={newValue}");
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error updating metadata entry: {ex.Message}");
                MessageBox.Show($"Error updating metadata entry: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteMetadataEntry(string objectIndex, string keyName, string value)
        {
            try
            {
                string connectionString = $"Data Source={Main.sqlFilePath};Version=3;";
                using (SQLiteConnection connection = new SQLiteConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string deleteQuery = $@"
                DELETE FROM Metadata
                WHERE ObjectIndex = @objectIndex AND KeyName = @keyName AND Value = @value";

                    SQLiteCommand command = new SQLiteCommand(deleteQuery, connection);
                    command.Parameters.AddWithValue("@objectIndex", objectIndex);
                    command.Parameters.AddWithValue("@keyName", keyName);
                    command.Parameters.AddWithValue("@value", value);

                    await command.ExecuteNonQueryAsync();

                    Main.LogDebugInfo($"Metadata entry deleted: ObjectIndex={objectIndex}, KeyName={keyName}, Value={value}");
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error deleting metadata entry: {ex.Message}");
                MessageBox.Show($"Error deleting metadata entry: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            ClearUnsavedChanges();
            string searchQuery = txtSearch.Text.Trim();
            if (string.IsNullOrEmpty(searchQuery))
            {
                MessageBox.Show("Please enter a search query.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Perform the search and load results
            PerformSearchAndLoadResults(searchQuery);
        }

        private async void PerformSearchAndLoadResults(string searchQuery)
        {
            try
            {
                // Find the ObjectIndex based on the ObjectID search query
                int startIndex = GetObjectIndexByObjectId(searchQuery);

                if (startIndex == -1)
                {
                    MessageBox.Show("ObjectID not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Load the item and the next 25 items
                await PopulateGridWithEntries(startIndex, 25);
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error performing search: {ex.Message}");
                MessageBox.Show($"Error performing search: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private int GetObjectIndexByObjectId(string objectId)
        {
            int objectIndex = -1;

            try
            {
                string connectionString = $"Data Source={Main.sqlFilePath};Version=3;";
                using (SQLiteConnection connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    Main.LogDebugInfo("SQLite connection opened successfully.");

                    string query = @"
                SELECT ObjectIndex
                FROM Objects
                WHERE ObjectId = @objectId
                LIMIT 1";

                    using (SQLiteCommand command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@objectId", objectId);
                        object result = command.ExecuteScalar();
                        if (result != null)
                        {
                            objectIndex = Convert.ToInt32(result);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error retrieving ObjectIndex by ObjectId: {ex.Message}");
            }

            return objectIndex;
        }



        private async Task<List<string>> GetObjectIdsInRange(int startIndex, int endIndex)
        {
            var objectIds = new List<string>();

            try
            {
                string connectionString = $"Data Source={Main.sqlFilePath};Version=3;";
                using (SQLiteConnection connection = new SQLiteConnection(connectionString))
                {
                    await connection.OpenAsync();
                    Main.LogDebugInfo("SQLite connection opened successfully.");

                    string query = @"
                SELECT ObjectId
                FROM Objects
                WHERE ObjectIndex BETWEEN @startIndex AND @endIndex";

                    using (SQLiteCommand command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@startIndex", startIndex);
                        command.Parameters.AddWithValue("@endIndex", endIndex);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                objectIds.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error retrieving ObjectIDs in range: {ex.Message}");
            }

            return objectIds;
        }

        private async void DeleteSQLIndexButton_Click(object sender, RoutedEventArgs e)
        {
            ClearUnsavedChanges();
            string input = txtGoToIndex.Text;
            if (string.IsNullOrWhiteSpace(input))
            {
                Main.LogDebugInfo("Input is empty.");
                return;
            }

            int startIndex, endIndex;

            if (input.Contains('-'))
            {
                // Handle range input
                var parts = input.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0], out startIndex) && int.TryParse(parts[1], out int endIndexParsed))
                {
                    if (startIndex > endIndexParsed)
                    {
                        Main.LogDebugInfo("Invalid range entered: start index is greater than end index.");
                        MessageBox.Show("Invalid range: start index is greater than end index.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    endIndex = endIndexParsed;
                }
                else
                {
                    Main.LogDebugInfo("Invalid range entered: unable to parse start or end index.");
                    MessageBox.Show("Invalid range entered. Please enter a valid range.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else if (int.TryParse(input, out startIndex))
            {
                // Handle single index input
                endIndex = startIndex;
            }
            else
            {
                Main.LogDebugInfo("Invalid index entered.");
                MessageBox.Show("Invalid index entered. Please enter a valid index.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var objectIds = await GetObjectIdsInRange(startIndex, endIndex);
            if (objectIds.Count == 0)
            {
                MessageBox.Show("No entries found for the specified range.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string message = "Are you sure you want to delete the following entries?\n\n" + string.Join("\n", objectIds);
            MessageBoxResult result = MessageBox.Show(message, "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                await DeleteEntriesAndReorder(startIndex, endIndex);
            }
        }

        private async Task DeleteEntriesAndReorder(int startIndex, int endIndex)
        {
            try
            {
                string connectionString = $"Data Source={Main.sqlFilePath};Version=3;";
                using (SQLiteConnection connection = new SQLiteConnection(connectionString))
                {
                    await connection.OpenAsync();
                    Main.LogDebugInfo("SQLite connection opened successfully.");

                    using (SQLiteTransaction transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // every table that refers to objects by ObjectIndex (Objects, Metadata, and any others in the file):
                            // the rows of the deleted objects go, and the indexes after them close the gap in ALL of them.
                            // (Only Objects used to be renumbered, so every later object lost its metadata to the object before it.)
                            var tables = new List<string>();
                            using (var list = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'", connection))
                            using (var names = list.ExecuteReader())
                                while (names.Read()) tables.Add(names.GetString(0));
                            var withIndex = new List<string>();
                            foreach (var t in tables)
                            {
                                using (var info = new SQLiteCommand("PRAGMA table_info(" + QuoteName(t) + ")", connection))
                                using (var cols = info.ExecuteReader())
                                    while (cols.Read())
                                        if (string.Equals(cols["name"]?.ToString(), "ObjectIndex", StringComparison.OrdinalIgnoreCase)) { withIndex.Add(t); break; }
                            }
                            if (!withIndex.Any(t => string.Equals(t, "Objects", StringComparison.OrdinalIgnoreCase)))
                                throw new InvalidOperationException("the file has no Objects table with an ObjectIndex column");
                            int offset = endIndex - startIndex + 1;
                            foreach (var t in withIndex)
                            {
                                string q = QuoteName(t);
                                using (var del = new SQLiteCommand($"DELETE FROM {q} WHERE ObjectIndex BETWEEN @startIndex AND @endIndex", connection))
                                {
                                    del.Parameters.AddWithValue("@startIndex", startIndex);
                                    del.Parameters.AddWithValue("@endIndex", endIndex);
                                    int n = await del.ExecuteNonQueryAsync();
                                    Main.LogDebugInfo($"DB Editor: {n} row(s) deleted from {t}");
                                }
                                // two steps (up past the highest index, then down into place) so a unique ObjectIndex never
                                // clashes while the rows move down
                                long big;
                                using (var max = new SQLiteCommand($"SELECT IFNULL(MAX(ObjectIndex), 0) FROM {q}", connection))
                                    big = Convert.ToInt64(await max.ExecuteScalarAsync()) + offset + 1;
                                using (var up = new SQLiteCommand($"UPDATE {q} SET ObjectIndex = ObjectIndex + @big WHERE ObjectIndex > @endIndex", connection))
                                {
                                    up.Parameters.AddWithValue("@big", big);
                                    up.Parameters.AddWithValue("@endIndex", endIndex);
                                    await up.ExecuteNonQueryAsync();
                                }
                                using (var down = new SQLiteCommand($"UPDATE {q} SET ObjectIndex = ObjectIndex - @big - @offset WHERE ObjectIndex > @endIndex + @big", connection))
                                {
                                    down.Parameters.AddWithValue("@big", big);
                                    down.Parameters.AddWithValue("@offset", offset);
                                    down.Parameters.AddWithValue("@endIndex", endIndex);
                                    await down.ExecuteNonQueryAsync();
                                }
                            }
                            transaction.Commit();
                            Main.LogDebugInfo("Entries deleted and indexes reordered successfully.");
                            MessageBox.Show("Entries deleted and indexes reordered successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                            // Update NexTIndex and the textbox
                            int maxIndex = await GetMaxObjectIndex(connection);
                            NexTIndex = maxIndex + 1;
                            txtObjectIndex.Text = NexTIndex.ToString();
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            Main.LogDebugInfo($"Error deleting entries and reordering indexes: {ex.Message}");
                            MessageBox.Show($"Error deleting entries and reordering indexes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }

                // Refresh the grid where the deleted objects were
                await PopulateGridWithEntries(Math.Max(0, startIndex), 25);
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error performing delete and reorder: {ex.Message}");
                MessageBox.Show($"Error performing delete and reorder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private static string QuoteName(string name) => "[" + name.Replace("]", "]]") + "]";

        private async Task<int> GetMaxObjectIndex(SQLiteConnection connection)
        {
            int maxIndex = -1;

            try
            {
                string query = @"
            SELECT MAX(ObjectIndex) as MaxIndex
            FROM Objects";

                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                {
                    object result = await command.ExecuteScalarAsync();
                    if (result != DBNull.Value && result != null)
                    {
                        maxIndex = Convert.ToInt32(result);
                    }
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error retrieving max ObjectIndex: {ex.Message}");
            }

            return maxIndex;
        }

        // Method to handle key name selection changes and update value ComboBox options
        private void txtKeyName1_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateValueComboBox(txtKeyName1, txtValue1, txtCustomKeyName1, txtCustomValue1);
        private void txtKeyName2_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateValueComboBox(txtKeyName2, txtValue2, txtCustomKeyName2, txtCustomValue2);
        private void txtKeyName3_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateValueComboBox(txtKeyName3, txtValue3, txtCustomKeyName3, txtCustomValue3);
        private void txtKeyName4_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateValueComboBox(txtKeyName4, txtValue4, txtCustomKeyName4, txtCustomValue4);
        private void txtKeyName5_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateValueComboBox(txtKeyName5, txtValue5, txtCustomKeyName5, txtCustomValue5);
        private void txtKeyName6_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateValueComboBox(txtKeyName6, txtValue6, txtCustomKeyName6, txtCustomValue6);



        // Method to handle key name selection changes and update value ComboBox options
        // Method to handle key name selection changes and update value ComboBox options
        // Method to handle key name selection changes and update value ComboBox options
        private void UpdateValueComboBox(ComboBox keyComboBox, ComboBox valueComboBox, TextBox customKeyTextBox, TextBox customValueTextBox)
        {
            if (keyComboBox.SelectedItem == null) return;

            string selectedKey = (keyComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (selectedKey == "CUSTOM")
            {
                customKeyTextBox.Visibility = Visibility.Visible;
                keyComboBox.Visibility = Visibility.Collapsed;
                customValueTextBox.Visibility = Visibility.Visible;
                valueComboBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                customKeyTextBox.Visibility = Visibility.Collapsed;
                keyComboBox.Visibility = Visibility.Visible;

                valueComboBox.Visibility = Visibility.Visible;
                customValueTextBox.Visibility = Visibility.Collapsed;

                valueComboBox.Items.Clear();

                if (keyNameValues.ContainsKey(selectedKey))
                {
                    foreach (var value in keyNameValues[selectedKey])
                    {
                        valueComboBox.Items.Add(new ComboBoxItem { Content = value });
                    }

                    if (valueComboBox.Items.Count > 0)
                    {
                        valueComboBox.SelectedIndex = 0; // Automatically select the first value if there is one
                    }
                }
                valueComboBox.Items.Add(new ComboBoxItem { Content = "CUSTOM" });
            }

            // Automatic selection for other ComboBoxes
            if (keyComboBox == txtKeyName1)
            {
                ResetComboBox(txtKeyName2, txtCustomKeyName2);
                ResetComboBox(txtValue2, txtCustomValue2);
                ResetComboBox(txtKeyName3, txtCustomKeyName3);
                ResetComboBox(txtValue3, txtCustomValue3);

                if (selectedKey == "CLOTHING")
                {
                    SelectComboBoxItem(txtKeyName2, "RIGS");
                    SelectComboBoxItem(txtKeyName3, "ENTITLEMENT_ID");

                    // Uncheck and disable the HEAT checkbox, clear HEAT textboxes
                    chkBox1.IsChecked = false;
                    chkBox1.IsEnabled = false;
                    SetHeatTextBoxes(string.Empty, true);
                    ResetComboBox(txtKeyName4, txtCustomKeyName4);
                    ResetComboBox(txtValue4, txtCustomValue4);
                    ResetComboBox(txtKeyName5, txtCustomKeyName5);
                    ResetComboBox(txtValue5, txtCustomValue5);
                    ResetComboBox(txtKeyName6, txtCustomKeyName6);
                    ResetComboBox(txtValue6, txtCustomValue6);
                }
                else if (selectedKey == "FURNITURE")
                {
                    SelectComboBoxItem(txtKeyName2, "ENTITLEMENT_ID");
                    ResetComboBox(txtKeyName3, txtCustomKeyName3);
                    ResetComboBox(txtValue3, txtCustomValue3);
                    ResetComboBox(txtKeyName4, txtCustomKeyName4);
                    ResetComboBox(txtValue4, txtCustomValue4);
                    ResetComboBox(txtKeyName5, txtCustomKeyName5);
                    ResetComboBox(txtValue5, txtCustomValue5);
                    ResetComboBox(txtKeyName6, txtCustomKeyName6);
                    ResetComboBox(txtValue6, txtCustomValue6);

                    // Ensure the checkbox is enabled for FURNITURE
                    chkBox1.IsEnabled = true;
                }
                else if (selectedKey == "PORTABLE")
                {
                    SelectComboBoxItem(txtKeyName2, "ENTITLEMENT_ID");
                    ResetComboBox(txtKeyName3, txtCustomKeyName3);
                    ResetComboBox(txtValue3, txtCustomValue3);
                    ResetComboBox(txtKeyName4, txtCustomKeyName4);
                    ResetComboBox(txtValue4, txtCustomValue4);
                    ResetComboBox(txtKeyName5, txtCustomKeyName5);
                    ResetComboBox(txtValue5, txtCustomValue5);
                    ResetComboBox(txtKeyName6, txtCustomKeyName6);
                    ResetComboBox(txtValue6, txtCustomValue6);

                    // Ensure the checkbox is enabled for FURNITURE
                    chkBox1.IsEnabled = true;
                }
                else if (selectedKey == "MINIGAME")
                {
                    ResetComboBox(txtKeyName2, txtCustomKeyName2);
                    ResetComboBox(txtValue2, txtCustomValue2);
                    ResetComboBox(txtKeyName3, txtCustomKeyName3);
                    ResetComboBox(txtValue3, txtCustomValue3);
                    ResetComboBox(txtKeyName4, txtCustomKeyName4);
                    ResetComboBox(txtValue4, txtCustomValue4);
                    ResetComboBox(txtKeyName5, txtCustomKeyName5);
                    ResetComboBox(txtValue5, txtCustomValue5);
                    ResetComboBox(txtKeyName6, txtCustomKeyName6);
                    ResetComboBox(txtValue6, txtCustomValue6);

                    // Ensure the checkbox is enabled for FURNITURE
                    chkBox1.IsEnabled = true;
                }
                else if (selectedKey == "SCENE_ENTITLEMENT")
                {
                    SelectComboBoxItem(txtKeyName2, "SCENE_TYPE");
                    SelectComboBoxItem(txtKeyName3, "ENTITLEMENT_ID");
                    ResetComboBox(txtKeyName4, txtCustomKeyName4);
                    ResetComboBox(txtValue4, txtCustomValue4);
                    ResetComboBox(txtKeyName5, txtCustomKeyName5);
                    ResetComboBox(txtValue5, txtCustomValue5);
                    ResetComboBox(txtKeyName6, txtCustomKeyName6);
                    ResetComboBox(txtValue6, txtCustomValue6);

                    // Swap Value1 to a TextBox
                    customValueTextBox.Visibility = Visibility.Visible;
                    valueComboBox.Visibility = Visibility.Collapsed;
                }
                else if (selectedKey == "COMMUNICATION_ID")
                {
                    SelectComboBoxItem(txtKeyName2, "TITLE_ID");

                    // Swap Value1 and Value2 to TextBoxes
                    txtCustomValue1.Visibility = Visibility.Visible;
                    txtValue1.Visibility = Visibility.Collapsed;
                    txtCustomValue2.Visibility = Visibility.Visible;
                    txtValue2.Visibility = Visibility.Collapsed;
                    ResetComboBox(txtKeyName4, txtCustomKeyName4);
                    ResetComboBox(txtValue4, txtCustomValue4);
                    ResetComboBox(txtKeyName5, txtCustomKeyName5);
                    ResetComboBox(txtValue5, txtCustomValue5);
                    ResetComboBox(txtKeyName6, txtCustomKeyName6);
                    ResetComboBox(txtValue6, txtCustomValue6);
                }
                else
                {
                    chkBox1.IsEnabled = true;
                }
            }
        }


        private void SelectComboBoxItem(ComboBox comboBox, string itemText)
        {
            foreach (var item in comboBox.Items)
            {
                if ((item as ComboBoxItem)?.Content.ToString() == itemText)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
            // If not found, reset to default
            comboBox.SelectedIndex = -1;
        }

        private void UpdateValueComboBoxForValue(ComboBox valueComboBox, TextBox customValueTextBox)
        {
            if (valueComboBox.SelectedItem == null) return;

            string selectedValue = (valueComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (selectedValue == "CUSTOM")
            {
                customValueTextBox.Visibility = Visibility.Visible;
                valueComboBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                customValueTextBox.Visibility = Visibility.Collapsed;
                valueComboBox.Visibility = Visibility.Visible;
            }
        }

        // Event handler for selection changed on value ComboBoxes
        private void Value_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox valueComboBox = sender as ComboBox;
            if (valueComboBox != null)
            {
                if (valueComboBox == txtValue1)
                {
                    UpdateValueComboBoxForValue(txtValue1, txtCustomValue1);
                }
                else if (valueComboBox == txtValue2)
                {
                    UpdateValueComboBoxForValue(txtValue2, txtCustomValue2);
                }
                else if (valueComboBox == txtValue3)
                {
                    UpdateValueComboBoxForValue(txtValue3, txtCustomValue3);
                }
            }
        }

        // Attach Value_SelectionChanged to each value ComboBox
        internal void InitializeComboBoxEventHandlers()
        {
            txtValue1.SelectionChanged += Value_SelectionChanged;
            txtValue2.SelectionChanged += Value_SelectionChanged;
            txtValue3.SelectionChanged += Value_SelectionChanged;
        }




        private void chkBox1_Checked(object sender, RoutedEventArgs e)
        {
            SetHeatTextBoxes("1", false); // Set value to "1" and make them writable
        }

        private void chkBox1_Unchecked(object sender, RoutedEventArgs e)
        {
            SetHeatTextBoxes(string.Empty, true); // Clear the text and make them read-only
        }

        private void SetHeatTextBoxes(string text, bool isReadOnly)
        {
            txtBox1.Text = text;
            txtBox1.IsReadOnly = isReadOnly;

            txtBox2.Text = text;
            txtBox2.IsReadOnly = isReadOnly;

            txtBox4.Text = text;
            txtBox4.IsReadOnly = isReadOnly;

            txtBox5.Text = text;
            txtBox5.IsReadOnly = isReadOnly;

            txtBox6.Text = text;
            txtBox6.IsReadOnly = isReadOnly;
        }


        private void InsertMetadataEntry(SQLiteConnection connection, int objectIndex, ComboBox keyComboBox, ComboBox valueComboBox, TextBox customKeyTextBox, TextBox customValueTextBox)
        {
            string keyName = keyComboBox.Visibility == Visibility.Visible ?
                             ((ComboBoxItem)keyComboBox.SelectedItem)?.Content.ToString() : customKeyTextBox.Text;
            string value = valueComboBox.Visibility == Visibility.Visible ?
                           ((ComboBoxItem)valueComboBox.SelectedItem)?.Content.ToString() : customValueTextBox.Text;

            // Ensure keyName is not empty
            if (!string.IsNullOrEmpty(keyName))
            {
                // Trim " (Male)" or " (Female)" from the value if present
                if (!string.IsNullOrEmpty(value))
                {
                    if (value.EndsWith(" (Male)"))
                    {
                        value = value.Substring(0, value.Length - " (Male)".Length);
                    }
                    else if (value.EndsWith(" (Female)"))
                    {
                        value = value.Substring(0, value.Length - " (Female)".Length);
                    }
                }

                string insertMetadataQuery = @"
INSERT INTO Metadata (ObjectIndex, KeyName, Value)
VALUES (@objectIndex, @keyName, @value)";
                using (var insertMetadataCommand = new SQLiteCommand(insertMetadataQuery, connection))
                {
                    insertMetadataCommand.Parameters.AddWithValue("@objectIndex", objectIndex);
                    insertMetadataCommand.Parameters.AddWithValue("@keyName", keyName);
                    insertMetadataCommand.Parameters.AddWithValue("@value", value ?? string.Empty); // Use empty string if value is null
                    insertMetadataCommand.ExecuteNonQuery();
                }
            }
        }



        private async void AddNewSQLItem_Button_Click(object sender, RoutedEventArgs e)
        {
            ClearUnsavedChanges();
            try
            {
                int objectIndex = int.Parse(txtObjectIndex.Text);
                string objectId = txtObjectId.Text;
                int version = int.Parse(txtVersion.Text);
                int location = int.Parse(txtLocation.Text);
                string entryType = txtEntryType.Text == "NULL" ? null : txtEntryType.Text;
                string archiveTimeStampHex = txtArchiveTimeStampHex.Text;
                string odcSha1Digest = txtOdcSha1Digest.Text;
                int? entitlementIndex = ParseNullableInt(txtEntitlementIndex.Text);
                int? rewardIndex = ParseNullableInt(txtRewardIndex.Text);
                int userLocation = int.Parse(txtUserLocation.Text);
                string lastUsed = txtLastUsed.Text;

                // Validate SHA1 (allow it to be empty or a valid SHA1)
                if (!string.IsNullOrEmpty(odcSha1Digest) && !Regex.IsMatch(odcSha1Digest, @"^[a-fA-F0-9]{40}$"))
                {
                    MessageBox.Show("Invalid SHA1 format. It must be 40 hexadecimal characters.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Validate UUID
                if (!Regex.IsMatch(objectId, @"^[a-fA-F0-9]{8}-[a-fA-F0-9]{8}-[a-fA-F0-9]{8}-[a-fA-F0-9]{8}$"))
                {
                    MessageBox.Show("Invalid UUID format. It must be in the format 8-8-8-8 hexadecimal characters.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Validate timestamp
                if (!Regex.IsMatch(archiveTimeStampHex, @"^[a-fA-F0-9]{1,8}$"))
                {
                    MessageBox.Show("Invalid timestamp format. It must be up to 8 hexadecimal characters.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Validate last used date if not NULL
                int? lastUsedDate = null;
                if (!string.IsNullOrEmpty(lastUsed) && lastUsed != "NULL")
                {
                    if (!Regex.IsMatch(lastUsed, @"^[a-fA-F0-9]{1,8}$"))
                    {
                        MessageBox.Show("Invalid last used date format. It must be up to 8 hexadecimal characters.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    lastUsedDate = Main.ConvertHexToInt(lastUsed);
                }

                int archiveTimeStamp = Main.ConvertHexToInt(archiveTimeStampHex);
                byte[] odcSha1DigestBytes = !string.IsNullOrEmpty(odcSha1Digest) ? ConvertHexToByteArray(odcSha1Digest) : null;

                bool objectReplaced = false;

                using (var connection = new SQLiteConnection($"Data Source={Main.sqlFilePath};Version=3;"))
                {
                    await connection.OpenAsync();

                    bool objectIndexExists = await CheckObjectIndexExists(connection, objectIndex);
                    var (objectIdExists, existingObjectIndex) = await CheckObjectIdExists(connection, objectId);
                    if (objectIndexExists && objectIdExists && existingObjectIndex != objectIndex)
                    {
                        MessageBox.Show($"ObjectIndex {objectIndex} is used by another object, and ObjectId {objectId} already exists under ObjectIndex {existingObjectIndex}.\n\n" +
                                        $"Use ObjectIndex {existingObjectIndex} to replace that object, or a free ObjectIndex.", "Conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (objectIndexExists) objectIdExists = false;   // replacing by index

                    if (objectIndexExists || objectIdExists)
                    {
                        string message = objectIndexExists ?
                            $"ObjectIndex {objectIndex} already exists. Do you want to replace the existing item?" :
                            $"ObjectId {objectId} already exists under ObjectIndex {existingObjectIndex}. Do you want to replace the existing item?";

                        var result = CustomMessageBoxInserttoSQL.Show(message, "Replace", "Cancel");

                        if (result == CustomMessageBoxInserttoSQL.CustomMessageBoxResult.Cancel)
                        {
                            return; // Abort the insertion process
                        }

                        objectReplaced = true;
                        objectIndex = objectIndexExists ? objectIndex : existingObjectIndex; // Use the existing index
                    }

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // the old item goes in the same transaction, so a failed insert leaves it as it was
                            if (objectReplaced) await DeleteItemByObjectIndex(connection, objectIndex);

                            // Insert main object entry
                            string insertQuery = @"
                    INSERT INTO Objects (ObjectIndex, ObjectId, Version, Location, InventoryEntryType, ArchiveTimeStamp, OdcSha1Digest, EntitlementIndex, RewardIndex, UserLocation, UserDateLastUsed)
                    VALUES (@objectIndex, @objectId, @version, @location, @entryType, @archiveTimeStamp, @odcSha1Digest, @entitlementIndex, @rewardIndex, @userLocation, @lastUsedDate)";
                            using (var insertCommand = new SQLiteCommand(insertQuery, connection))
                            {
                                insertCommand.Parameters.AddWithValue("@objectIndex", objectIndex);
                                insertCommand.Parameters.AddWithValue("@objectId", objectId);
                                insertCommand.Parameters.AddWithValue("@version", version);
                                insertCommand.Parameters.AddWithValue("@location", location);
                                insertCommand.Parameters.AddWithValue("@entryType", (object)entryType ?? DBNull.Value);
                                insertCommand.Parameters.AddWithValue("@archiveTimeStamp", archiveTimeStamp);
                                insertCommand.Parameters.AddWithValue("@odcSha1Digest", (object)odcSha1DigestBytes ?? DBNull.Value);
                                insertCommand.Parameters.AddWithValue("@entitlementIndex", (object)entitlementIndex ?? DBNull.Value);
                                insertCommand.Parameters.AddWithValue("@rewardIndex", (object)rewardIndex ?? DBNull.Value);
                                insertCommand.Parameters.AddWithValue("@userLocation", userLocation);
                                insertCommand.Parameters.AddWithValue("@lastUsedDate", (object)lastUsedDate ?? DBNull.Value);
                                await insertCommand.ExecuteNonQueryAsync();
                            }

                            // Insert metadata entries
                            InsertMetadataEntry(connection, objectIndex, txtKeyName1, txtValue1, txtCustomKeyName1, txtCustomValue1);
                            InsertMetadataEntry(connection, objectIndex, txtKeyName2, txtValue2, txtCustomKeyName2, txtCustomValue2);
                            InsertMetadataEntry(connection, objectIndex, txtKeyName3, txtValue3, txtCustomKeyName3, txtCustomValue3);
                            InsertMetadataEntry(connection, objectIndex, txtKeyName4, txtValue4, txtCustomKeyName4, txtCustomValue4);
                            InsertMetadataEntry(connection, objectIndex, txtKeyName5, txtValue5, txtCustomKeyName5, txtCustomValue5);
                            InsertMetadataEntry(connection, objectIndex, txtKeyName6, txtValue6, txtCustomKeyName6, txtCustomValue6);

                            // Insert HEAT metadata entries if checkbox is checked
                            if (chkBox1.IsChecked == true)
                            {
                                InsertHeatMetadataEntries(connection, objectIndex);
                            }

                            transaction.Commit();
                            if (objectReplaced)
                            {
                                MessageBox.Show("Object replaced successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            else
                            {
                                MessageBox.Show("Object inserted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            Main.LogDebugInfo($"Error adding new item: {ex.Message}");
                            MessageBox.Show($"Error adding new item: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }

                    // Update NexTIndex and the textbox
                    int maxIndex = await GetMaxObjectIndex(connection);
                    NexTIndex = maxIndex + 1;
                    txtObjectIndex.Text = NexTIndex.ToString();

                    // Call the range load function to load the index number in txtObjectIndex textbox and load 25 from that point on
                    await PopulateGridWithEntries(objectIndex, 25);
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error adding new item: {ex.Message}");
                MessageBox.Show($"Error adding new item: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async Task<bool> CheckObjectIndexExists(SQLiteConnection connection, int objectIndex)
        {
            string query = "SELECT COUNT(*) FROM Objects WHERE ObjectIndex = @objectIndex";
            using (var command = new SQLiteCommand(query, connection))
            {
                command.Parameters.AddWithValue("@objectIndex", objectIndex);
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
        }

        private async Task<(bool, int)> CheckObjectIdExists(SQLiteConnection connection, string objectId)
        {
            string query = "SELECT ObjectIndex FROM Objects WHERE ObjectId = @objectId";
            using (var command = new SQLiteCommand(query, connection))
            {
                command.Parameters.AddWithValue("@objectId", objectId);
                object result = await command.ExecuteScalarAsync();
                if (result != null)
                {
                    int existingObjectIndex = Convert.ToInt32(result);
                    return (true, existingObjectIndex);
                }
                return (false, -1);
            }
        }

        private async Task DeleteItemByObjectIndex(SQLiteConnection connection, int objectIndex)
        {
            // Delete metadata entries
            string deleteMetadataQuery = "DELETE FROM Metadata WHERE ObjectIndex = @objectIndex";
            using (var deleteMetadataCommand = new SQLiteCommand(deleteMetadataQuery, connection))
            {
                deleteMetadataCommand.Parameters.AddWithValue("@objectIndex", objectIndex);
                await deleteMetadataCommand.ExecuteNonQueryAsync();
            }

            // Delete main object entry
            string deleteQuery = "DELETE FROM Objects WHERE ObjectIndex = @objectIndex";
            using (var deleteCommand = new SQLiteCommand(deleteQuery, connection))
            {
                deleteCommand.Parameters.AddWithValue("@objectIndex", objectIndex);
                await deleteCommand.ExecuteNonQueryAsync();
            }
        }




        // Method to insert HEAT metadata entries
        private void InsertHeatMetadataEntries(SQLiteConnection connection, int objectIndex)
        {
            var heatTextBoxes = new Dictionary<string, TextBox>
    {
        { "HOST_HEAT", txtBox4 },
        { "MAIN_HEAT", txtBox1 },
        { "NET_HEAT", txtBox5 },
        { "PPU_HEAT", txtBox2 },
        { "VRAM_HEAT", txtBox6 }
    };

            foreach (var heatEntry in heatTextBoxes)
            {
                if (!string.IsNullOrWhiteSpace(heatEntry.Value.Text))
                {
                    string insertMetadataQuery = @"
            INSERT INTO Metadata (ObjectIndex, KeyName, Value)
            VALUES (@objectIndex, @keyName, @value)";
                    using (var insertMetadataCommand = new SQLiteCommand(insertMetadataQuery, connection))
                    {
                        insertMetadataCommand.Parameters.AddWithValue("@objectIndex", objectIndex);
                        insertMetadataCommand.Parameters.AddWithValue("@keyName", heatEntry.Key);
                        insertMetadataCommand.Parameters.AddWithValue("@value", heatEntry.Value.Text);
                        insertMetadataCommand.ExecuteNonQuery();
                    }
                }
            }
        }




        // Dictionary to map key names to values
        private Dictionary<string, List<string>> keyNameValues = new Dictionary<string, List<string>>
{
    { "CLOTHING", new List<string> { "HAIR", "HAT", "HANDS", "TORS", "LEGS", "FEET", "OUTFITS", "GLASSES", "HEADPHONES", "JEWELBOTHEARS", "JEWELLEFTEAR", "JEWELRIGHTEAR", "FACIALHAIR", "RACE", "" } },
    { "FURNITURE", new List<string> { "CHAIR", "APPLIANCE", "TABLE", "FOOTSTOOL", "LIGHT", "ORNAMENT", "FRAME", "SOFA", "STORAGE", "CUBE", "FLOORING", "" } },
    { "PORTABLE", new List<string> { "" } },
    { "ENTITLEMENT_ID", new List<string> { "LUA_REWARD", "AUTOMATIC_REWARD", "FREE", "" } },
    { "MINI_GAME", new List<string> { "", "DARTS", "APPLIANCE", "HUBCONTENT", "XMAS_2010_SCEA", "CASUAL", "GAME", "SPACE" } },
    { "ARCADE_GAME", new List<string> { "" } },
    { "WORLD_MAP", new List<string> { "SCEA SCEE SCEJ SCEASIA", "SCEA SCEE SCEJ", "SCEA SCEE", "SCEJ SCEASIA", "SCEA", "SCEE", "SCEJ", "SCEASIA" } },
    { "SCENE_ENTITLEMENT", new List<string> { "CUSTOM" } },
    { "EMBEDDED_OBJECT", new List<string> { "" } },
    { "COMMUNICATION_ID", new List<string> { "CUSTOM" } },
    { "ACTIVE", new List<string> { "DEFAULT", "" } },
    { "TARGETABLE", new List<string> { "" } },
    { "RIGS", new List<string> { "00000000-00000000-00000010-00000000 (Male)", "00000000-00000000-00000010-00000001 (Female)" } },
    { "TITLE_ID", new List<string> { "CUSTOM" } },
    { "TAGS", new List<string> { "" } },
    { "LPID", new List<string> { "DEFAULT_HAIR", "DEFAULT_FACIALHAIR" } },
    { "SCENE_TYPE", new List<string> { "APARTMENT", "CLUBHOUSE" } }
};

        private byte[] ConvertHexToByteArray(string hex)
        {
            try
            {
                int numberChars = hex.Length;
                byte[] bytes = new byte[numberChars / 2];
                for (int i = 0; i < numberChars; i += 2)
                {
                    bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
                }
                return bytes;
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Format error converting hex to byte array: {ex.Message}");
                throw;
            }
        }

        private int? ParseNullableInt(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "NULL")
            {
                return null;
            }

            if (int.TryParse(value, out int result))
            {
                return result;
            }

            Main.LogDebugInfo($"Invalid nullable int format: {value}");
            throw new FormatException($"Invalid nullable int format: {value}");
        }


        // Method to reset all ComboBoxes and TextBoxes to their default states
        private void ResetForm()
        {
            ClearUnsavedChanges();
            // Reset TextBoxes to default values
            txtObjectId.Text = "";
            txtVersion.Text = "31";
            txtLocation.Text = "1";
            txtEntryType.Text = "NULL";
            txtArchiveTimeStampHex.Text = "FFFFFFFF";
            txtOdcSha1Digest.Text = "";
            txtEntitlementIndex.Text = "NULL";
            txtRewardIndex.Text = "NULL";
            txtUserLocation.Text = "0";
            txtLastUsed.Text = "NULL";

            // Reset ComboBoxes to default values and hide custom TextBoxes
            ResetComboBox(txtKeyName1, txtCustomKeyName1);
            ResetComboBox(txtValue1, txtCustomValue1);
            ResetComboBox(txtKeyName2, txtCustomKeyName2);
            ResetComboBox(txtValue2, txtCustomValue2);
            ResetComboBox(txtKeyName3, txtCustomKeyName3);
            ResetComboBox(txtValue3, txtCustomValue3);
            ResetComboBox(txtKeyName4, txtCustomKeyName4);
            ResetComboBox(txtValue4, txtCustomValue4);
            ResetComboBox(txtKeyName5, txtCustomKeyName5);
            ResetComboBox(txtValue5, txtCustomValue5);
            ResetComboBox(txtKeyName6, txtCustomKeyName6);
            ResetComboBox(txtValue6, txtCustomValue6);

            // Reset CheckBoxes
            chkBox1.IsChecked = false;

            // Reset additional TextBoxes
            txtBox1.Text = "";
            txtBox2.Text = "";
            txtBox4.Text = "";
            txtBox5.Text = "";
            txtBox6.Text = "";

            // Make HEAT textboxes readonly
            txtBox1.IsReadOnly = true;
            txtBox2.IsReadOnly = true;
            txtBox4.IsReadOnly = true;
            txtBox5.IsReadOnly = true;
            txtBox6.IsReadOnly = true;
        }

        // Helper method to reset a ComboBox and its corresponding custom TextBox
        private void ResetComboBox(ComboBox comboBox, TextBox customTextBox)
        {
            comboBox.SelectedIndex = -1; // Reset the ComboBox selection
            comboBox.Visibility = Visibility.Visible; // Ensure ComboBox is visible
            customTextBox.Text = ""; // Clear the custom TextBox
            customTextBox.Visibility = Visibility.Collapsed; // Hide the custom TextBox
        }

        // Event handler for the Clear button
        private void ClearSQLButton_Click(object sender, RoutedEventArgs e)
        {
            ResetForm();
        }


        private void txtValue_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private async void ExportToXMLButton_Click(object sender, RoutedEventArgs e)
        {
            ClearUnsavedChanges();
            try
            {
                List<XmlObject> xmlObjects = new List<XmlObject>();

                // Retrieve data from the SQL database
                string connectionString = $"Data Source={Main.sqlFilePath};Version=3;";
                using (SQLiteConnection connection = new SQLiteConnection(connectionString))
                {
                    await connection.OpenAsync();

                    // Query to get all objects
                    string objectQuery = @"
            SELECT 
                ObjectIndex, 
                ObjectId, 
                Version, 
                Location, 
                InventoryEntryType, 
                ArchiveTimeStamp, 
                OdcSha1Digest, 
                EntitlementIndex, 
                RewardIndex, 
                UserLocation, 
                UserDateLastUsed 
            FROM Objects";

                    using (SQLiteCommand objectCommand = new SQLiteCommand(objectQuery, connection))
                    {
                        using (var reader = await objectCommand.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var sqliteReader = (SQLiteDataReader)reader;

                                XmlObject xmlObject = new XmlObject
                                {
                                    ObjectIndex = sqliteReader["ObjectIndex"].ToString(),
                                    ObjectId = sqliteReader["ObjectId"].ToString(),
                                    Version = sqliteReader["Version"].ToString(),
                                    Location = sqliteReader["Location"].ToString(),
                                    InventoryEntryType = sqliteReader["InventoryEntryType"]?.ToString(),
                                    ArchiveTimeStamp = Main.ConvertTimestampToHex(sqliteReader["ArchiveTimeStamp"]?.ToString()),
                                    OdcSha1Digest = Main.ConvertBlobToSha1(sqliteReader["OdcSha1Digest"] as byte[]),
                                    EntitlementIndex = sqliteReader["EntitlementIndex"]?.ToString(),
                                    RewardIndex = sqliteReader["RewardIndex"]?.ToString(),
                                    UserLocation = sqliteReader["UserLocation"].ToString(),
                                    UserDateLastUsed = sqliteReader["UserDateLastUsed"]?.ToString(),
                                    Metadata = new List<XmlMetadata>()
                                };

                                // Query to get metadata for the current object
                                string metadataQuery = @"
                        SELECT ObjectIndex, KeyName, Value 
                        FROM Metadata 
                        WHERE ObjectIndex = @objectIndex";

                                using (SQLiteCommand metadataCommand = new SQLiteCommand(metadataQuery, connection))
                                {
                                    metadataCommand.Parameters.AddWithValue("@objectIndex", xmlObject.ObjectIndex);

                                    using (var metadataReader = await metadataCommand.ExecuteReaderAsync())
                                    {
                                        while (await metadataReader.ReadAsync())
                                        {
                                            var sqliteMetadataReader = (SQLiteDataReader)metadataReader;

                                            XmlMetadata metadata = new XmlMetadata
                                            {
                                                ObjectIndex = sqliteMetadataReader["ObjectIndex"].ToString(),
                                                KeyName = sqliteMetadataReader["KeyName"].ToString(),
                                                Value = sqliteMetadataReader["Value"].ToString()
                                            };

                                            xmlObject.Metadata.Add(metadata);
                                        }
                                    }
                                }

                                xmlObjects.Add(xmlObject);
                            }
                        }
                    }
                }

                // Ask user where to save the XML file
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                    DefaultExt = ".xml",
                    Title = "Save XML File"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    string selectedFilePath = saveFileDialog.FileName;
                    WriteObjectsToXML(selectedFilePath, xmlObjects);

                    // Generate the CSV file path based on the XML file path
                    Main.csvFilePath = Path.ChangeExtension(selectedFilePath, ".csv");
                    ExportToCSV(Main.csvFilePath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting to XML and CSV: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async void ExportToCSV(string csvFilePath)
        {
            try
            {
                using (var writer = new StreamWriter(csvFilePath))
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    string connectionString = $"Data Source={Main.sqlFilePath};Version=3;";
                    using (SQLiteConnection connection = new SQLiteConnection(connectionString))
                    {
                        await connection.OpenAsync();

                        string query = @"
                    SELECT 
                        o.ObjectIndex, 
                        o.ObjectId, 
                        o.Version, 
                        o.Location, 
                        o.InventoryEntryType, 
                        o.ArchiveTimeStamp, 
                        o.OdcSha1Digest, 
                        o.EntitlementIndex, 
                        o.RewardIndex, 
                        o.UserLocation, 
                        o.UserDateLastUsed, 
                        m.KeyName, 
                        m.Value
                    FROM Objects o
                    LEFT JOIN Metadata m ON o.ObjectIndex = m.ObjectIndex";

                        using (SQLiteCommand command = new SQLiteCommand(query, connection))
                        {
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                bool headerWritten = false;

                                while (await reader.ReadAsync())
                                {
                                    var sqliteReader = (SQLiteDataReader)reader;

                                    if (!headerWritten)
                                    {
                                        for (int i = 0; i < sqliteReader.FieldCount; i++)
                                        {
                                            csv.WriteField(sqliteReader.GetName(i));
                                        }
                                        csv.NextRecord();
                                        headerWritten = true;
                                    }

                                    for (int i = 0; i < sqliteReader.FieldCount; i++)
                                    {
                                        var value = sqliteReader.GetValue(i);

                                        if (value is DBNull)
                                        {
                                            csv.WriteField(string.Empty);
                                        }
                                        else if (value is byte[] blob)
                                        {
                                            csv.WriteField(BitConverter.ToString(blob).Replace("-", "").ToUpper());
                                        }
                                        else if ((value is int || value is long) && (sqliteReader.GetName(i) == "ArchiveTimeStamp" || sqliteReader.GetName(i) == "UserDateLastUsed"))
                                        {
                                            // Convert integer timestamps to hexadecimal strings (SQLite hands back Int64)
                                            csv.WriteField(unchecked((uint)Convert.ToInt64(value)).ToString("X8"));
                                        }
                                        else
                                        {
                                            csv.WriteField(value);
                                        }
                                    }
                                    csv.NextRecord();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting to CSV: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void WriteObjectsToXML(string xmlFilePath, List<XmlObject> xmlObjects)
        {
            try
            {
                using (XmlWriter writer = XmlWriter.Create(xmlFilePath, new XmlWriterSettings { Indent = true }))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("object_catalogue");

                    foreach (var obj in xmlObjects)
                    {
                        if (string.IsNullOrEmpty(obj.ObjectId))
                        {
                            continue; // Skip entries without a UUID
                        }

                        writer.WriteStartElement("object");

                        writer.WriteAttributeString("uuid", obj.ObjectId);
                        writer.WriteAttributeString("timestamp", obj.ArchiveTimeStamp.ToUpper());

                        if (!string.IsNullOrEmpty(obj.OdcSha1Digest))
                        {
                            writer.WriteAttributeString("sha1", obj.OdcSha1Digest.ToUpper());
                        }

                        if (!string.IsNullOrEmpty(obj.Version))
                        {
                            writer.WriteAttributeString("vers", obj.Version);
                        }

                        // Add metadata
                        AddMetadataToXML(writer, obj.Metadata);

                        writer.WriteEndElement(); // End "object"
                    }

                    writer.WriteEndElement(); // End "object_catalogue"
                    writer.WriteEndDocument();
                }

                CleanUpXML(xmlFilePath, xmlFilePath); // Clean up the XML and save to the final file path
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error writing XML to file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddMetadataToXML(XmlWriter writer, List<XmlMetadata> metadataList)
        {
            Dictionary<string, List<string>> metadata = new Dictionary<string, List<string>>();
            Dictionary<string, Dictionary<string, string>> specialMetadata = new Dictionary<string, Dictionary<string, string>>();
            List<XmlMetadata> heatMetadata = new List<XmlMetadata>(); // List to store heat metadata
            bool hasSpecialEntitlement = false;

            foreach (var metadataItem in metadataList)
            {
                string keyName = metadataItem.KeyName.ToLower();
                string value = metadataItem.Value;

                if (keyName == "title_id")
                {
                    if (!metadata.ContainsKey(keyName))
                    {
                        metadata[keyName] = new List<string>();
                    }
                    metadata[keyName].Add(value);
                }
                else if (keyName == "entitlement_id" || keyName == "product_id" || keyName == "category_id")
                {
                    if (keyName == "entitlement_id" && (value == "LUA_REWARD" || value == "AUTOMATIC_REWARD" || value == "FREE"))
                    {
                        hasSpecialEntitlement = true;
                        if (!specialMetadata.ContainsKey(keyName))
                        {
                            specialMetadata[keyName] = new Dictionary<string, string>();
                        }
                        specialMetadata[keyName]["SCEE"] = value;
                        specialMetadata[keyName]["SCEA"] = value;
                        specialMetadata[keyName]["SCEJ"] = value;
                        specialMetadata[keyName]["SCEAsia"] = value;
                    }
                    else
                    {
                        string territory = GetTerritory(value);
                        if (!specialMetadata.ContainsKey(keyName))
                        {
                            specialMetadata[keyName] = new Dictionary<string, string>();
                        }
                        specialMetadata[keyName][territory] = value;
                    }
                }
                else if (keyName == "host_heat" || keyName == "main_heat" || keyName == "net_heat" || keyName == "ppu_heat" || keyName == "vram_heat")
                {
                    // Add to heat metadata list
                    heatMetadata.Add(metadataItem);
                }
                else
                {
                    if (!metadata.ContainsKey(keyName))
                    {
                        metadata[keyName] = new List<string>();
                    }
                    metadata[keyName].Add(value);
                }
            }

            // Write regular metadata
            foreach (var key in metadata.Keys)
            {
                writer.WriteStartElement(key);

                if (metadata[key].Count == 0 || string.IsNullOrEmpty(metadata[key][0]))
                {
                    writer.WriteEndElement(); // Self-closing tag for empty values
                }
                else
                {
                    writer.WriteString(string.Join(" ", metadata[key])); // Space-separated values
                    writer.WriteEndElement();
                }
            }

            // Write special metadata
            WriteSpecialMetadata(writer, specialMetadata, hasSpecialEntitlement);

            // Write heat metadata last
            foreach (var heatItem in heatMetadata)
            {
                writer.WriteStartElement(heatItem.KeyName.ToLower());
                writer.WriteString(heatItem.Value);
                writer.WriteEndElement();
            }
        }

        private void WriteSpecialMetadata(XmlWriter writer, Dictionary<string, Dictionary<string, string>> specialMetadata, bool hasSpecialEntitlement)
        {
            foreach (var key in specialMetadata.Keys)
            {
                var territories = new HashSet<string> { "SCEE", "SCEA", "SCEJ", "SCEAsia" };

                foreach (var territory in specialMetadata[key].Keys)
                {
                    writer.WriteStartElement(key);
                    writer.WriteAttributeString("territory", territory);

                    if (!string.IsNullOrEmpty(specialMetadata[key][territory]))
                    {
                        writer.WriteString(specialMetadata[key][territory]);
                    }

                    writer.WriteEndElement();

                    territories.Remove(territory);
                }

                // Add missing territories
                foreach (var territory in territories)
                {
                    writer.WriteStartElement(key);
                    writer.WriteAttributeString("territory", territory);
                    writer.WriteEndElement(); // Self-closing tag for empty values
                }
            }
        }

        private string GetTerritory(string value)
        {
            if (value.StartsWith("E"))
            {
                return "SCEE";
            }
            else if (value.StartsWith("U"))
            {
                return "SCEA";
            }
            else if (value.StartsWith("J"))
            {
                return "SCEJ";
            }
            else if (value.StartsWith("H"))
            {
                return "SCEAsia";
            }
            else
            {
                return string.Empty;
            }
        }

        private void CleanUpXML(string inputXmlFilePath, string outputXmlFilePath)
        {
            XDocument doc = XDocument.Load(inputXmlFilePath);

            // Remove entries with empty territory
            doc.Descendants()
               .Where(e => e.Attribute("territory") != null && string.IsNullOrEmpty(e.Attribute("territory").Value))
               .Remove();

            // Make LPID uppercase and format clothing and furniture values
            foreach (var element in doc.Descendants())
            {
                if (element.Name.LocalName == "lpid")
                {
                    element.Name = "LPID";
                    if (!string.IsNullOrEmpty(element.Value))
                    {
                        element.Value = TransformSpecialCases(element.Value);
                    }
                }
                else if (element.Name.LocalName == "clothing" || element.Name.LocalName == "furniture")
                {
                    if (!string.IsNullOrEmpty(element.Value))
                    {
                        element.Value = TransformSpecialCases(element.Value);
                    }
                }
            }

            doc.Save(outputXmlFilePath);
        }

        private string TransformSpecialCases(string input)
        {
            switch (input.ToLower())
            {
                case "default_hair":
                    return "Default_Hair";
                case "jewelbothears":
                    return "JewelBothEars";
                case "jewelleftear":
                    return "JewelLeftEar";
                case "jewelrightear":
                    return "JewelRightEar";
                case "headphones":
                    return "HeadPhones";
                case "facialhair":
                    return "FacialHair";
                case "default_facialhair":
                    return "Default_FacialHair";
                default:
                    return char.ToUpper(input[0]) + input.Substring(1).ToLower();
            }
        }




        private void Border_DragEnter(object sender, DragEventArgs e)
        {
            Main.LogDebugInfo("Border_DragEnter invoked.");
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                Main.LogDebugInfo("FileDrop detected. Copy effect set.");
            }
            else
            {
                e.Effects = DragDropEffects.None;
                Main.LogDebugInfo("No FileDrop detected. None effect set.");
            }
        }

        private void Border_Drop(object sender, DragEventArgs e)
        {
            Main.LogDebugInfo("Border_Drop invoked.");
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string filePath = files[0];
                    Main.LogDebugInfo($"File dropped: {filePath}");
                    ProcessDroppedFile(filePath);
                }
            }
        }

        private async void ProcessDroppedFile(string filePath)
        {
            Main.LogDebugInfo($"ProcessDroppedFile invoked with filePath: {filePath}");
            try
            {
                if (!TryProcessFile(filePath))
                {
                    string baseOutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "work"); // Use static work directory
                    Main.LogDebugInfo($"Decrypting file: {filePath}");
                    bool decrypted = await DecryptODCFilesAsync(new[] { filePath }, baseOutputDirectory);
                    if (decrypted)
                    {
                        string decryptedFilePath = Path.Combine(baseOutputDirectory, Path.GetFileName(filePath));
                        Main.LogDebugInfo($"Decrypted file path: {decryptedFilePath}");
                        if (!TryProcessFile(decryptedFilePath))
                        {
                            MessageBox.Show("Failed to process the decrypted file.");
                            Main.LogDebugInfo("Failed to process the decrypted file.");
                        }
                    }
                    else
                    {
                        MessageBox.Show("Failed to decrypt the file.");
                        Main.LogDebugInfo("Failed to decrypt the file.");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error occurred while processing the file: " + ex.Message);
                Main.LogDebugInfo($"Error processing file: {ex.Message}");
            }
        }

        private bool TryProcessFile(string filePath)
        {
            Main.LogDebugInfo($"TryProcessFile invoked with filePath: {filePath}");
            try
            {
                // Load the XML document
                XDocument doc = XDocument.Load(filePath);
                Main.LogDebugInfo("XML document loaded.");

                // Extract the UUID and timestamp
                string uuid = doc.Root.Element("uuid")?.Value;
                if (string.IsNullOrEmpty(uuid))
                {
                    Main.LogDebugInfo("UUID not found in XML document.");
                    return false; // UUID not found, return false
                }

                string timestamp = doc.Root.Element("timestamp")?.Value;

                // Calculate SHA1 hash of the file
                string sha1Hash = CalculateSha1(filePath);
                Main.LogDebugInfo($"SHA1 hash calculated: {sha1Hash}");

                // Extract the image file name suffix and update the version
                string version = ExtractVersionFromImages(doc);
                Main.LogDebugInfo($"Version extracted from images: {version}");

                // Update the TextBox controls
                txtObjectId.Text = uuid;
                txtArchiveTimeStampHex.Text = string.IsNullOrWhiteSpace(timestamp) ? "FFFFFFFF" : timestamp.Trim().ToUpperInvariant();
                txtOdcSha1Digest.Text = sha1Hash;
                txtVersion.Text = version;

                Main.LogDebugInfo("File processed successfully.");
                return true; // Successfully processed the file
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error processing file: {ex.Message}");
                return false; // Error occurred, return false
            }
        }

        public async Task<bool> DecryptODCFilesAsync(string[] filePaths, string baseOutputDirectory)
        {
            Main.LogDebugInfo("DecryptODCFilesAsync invoked.");
            bool allFilesProcessed = true;
            foreach (var filePath in filePaths)
            {
                string filename = Path.GetFileName(filePath);
                try
                {
                    byte[] fileContent = await File.ReadAllBytesAsync(filePath);
                    Main.LogDebugInfo($"Read file content: {filename}");

                    BruteforceProcess proc = new BruteforceProcess(fileContent);
                    byte[] decryptedContent = proc.StartBruteForce((int)CdnMode.RETAIL);

                    if (decryptedContent != null)
                    {
                        string outputDirectory = baseOutputDirectory;
                        if (!Directory.Exists(outputDirectory))
                        {
                            Directory.CreateDirectory(outputDirectory);
                            Main.LogDebugInfo($"Created output directory: {outputDirectory}");
                        }
                        string outputPath = Path.Combine(outputDirectory, filename);
                        await File.WriteAllBytesAsync(outputPath, decryptedContent);
                        Main.LogDebugInfo($"Decrypted content written to: {outputPath}");

                        if (!IsValidDecryptedODCFile(outputPath))
                        {
                            File.Delete(outputPath);
                            Main.LogDebugInfo($"Invalid decrypted ODC file, deleted: {outputPath}");
                            allFilesProcessed = false;
                        }
                    }
                    else
                    {
                        allFilesProcessed = false;
                        Main.LogDebugInfo($"Decryption failed for file: {filename}");
                    }
                }
                catch (Exception ex)
                {
                    allFilesProcessed = false;
                    Main.LogDebugInfo($"Error decrypting file: {ex.Message}");
                }
            }

            return allFilesProcessed;
        }

        private bool IsValidDecryptedODCFile(string filePath)
        {
            Main.LogDebugInfo($"IsValidDecryptedODCFile invoked with filePath: {filePath}");
            try
            {
                XDocument doc = XDocument.Load(filePath);
                bool isValid = doc.Root.Element("uuid") != null;
                Main.LogDebugInfo($"IsValidDecryptedODCFile result: {isValid}");
                return isValid;
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error in IsValidDecryptedODCFile: {ex.Message}");
                return false;
            }
        }

        private string CalculateSha1(string filePath)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open))
            {
                using (SHA1 sha1 = SHA1.Create())
                {
                    byte[] hashBytes = sha1.ComputeHash(fs);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToUpperInvariant();
                }
            }
        }

        private string ExtractVersionFromImages(XDocument doc)
        {
            string[] imageElements = { "maker_image", "small_image", "large_image" };
            foreach (var elementName in imageElements)
            {
                string imagePath = doc.Root.Element(elementName)?.Value;
                if (!string.IsNullOrEmpty(imagePath))
                {
                    var match = Regex.Match(imagePath, @"_T(\d{3})\.png", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        // Extract the version and trim leading zeros
                        string version = match.Groups[1].Value.TrimStart('0');
                        return version;
                    }
                }
            }
            return string.Empty; // Return empty string if no version found
        }

        private async void ExportToHCDBButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.DeployHCDBFlag = false;
            ClearUnsavedChanges();
            Main.LogDebugInfo("HCDB Conversion: Process Initiated");


            if (!Directory.Exists(Main._settings.HcdbOutputDirectory))
            {
                Directory.CreateDirectory(Main._settings.HcdbOutputDirectory);
                Main.LogDebugInfo($"HCDB Conversion: Output directory created at {Main._settings.HcdbOutputDirectory}");
            }

            if (!string.IsNullOrWhiteSpace(Main.sqlFilePath))
            {
                string[] filePaths = new string[] { Main.sqlFilePath };
                string[] segsPaths = filePaths.Select(fp => fp + ".segs").ToArray(); // Assuming LZMA outputs files with .segs extension

                Main.LogDebugInfo($"HCDB Conversion: Starting conversion for {filePaths.Length} file(s)");
                bool conversionSuccess = await ConvertSqlToHcdbAsync_Unique(filePaths);

                if (conversionSuccess)
                {
                    bool encryptionSuccess = await EncryptHCDBFilesAsync_Unique(segsPaths, Main._settings.HcdbOutputDirectory); // Encrypt the .segs files
                    if (!encryptionSuccess)
                    {
                        string message = "Encryption Failed";

                        Main.LogDebugInfo($"HCDB Conversion: Result - {message}");
                    }
                }
                else
                {
                    string message = "Conversion Failed";

                    Main.LogDebugInfo($"HCDB Conversion: Result - {message}");
                }
            }
            else
            {
                Main.LogDebugInfo("HCDB Conversion: Aborted - No SQL file specified for conversion.");

            }
        }



        private async Task<bool> ConvertSqlToHcdbAsync_Unique(string[] filePaths)
        {
            bool allFilesProcessed = true;
            string lzmaPath = @"dependencies\lzma_segs.exe";

            foreach (var filePath in filePaths)
            {
                string filename = Path.GetFileName(filePath);
                string expectedOutputPath = filePath + ".segs";  // Expecting LZMA to append ".segs" to the input file's name

                try
                {
                    // Process the file with LZMA without explicitly specifying the output path
                    if (!ExecuteLzmaProcess_Unique(lzmaPath, filePath))
                    {
                        Main.LogDebugInfo($"Conversion failed for {filename}.");
                        allFilesProcessed = false;
                    }
                    else
                    {
                        Main.LogDebugInfo($"Conversion successful for {filename}, output likely written to {expectedOutputPath}.");
                    }
                }
                catch (Exception ex)
                {
                    Main.LogDebugInfo($"Error processing {filename}: {ex.Message}");
                    allFilesProcessed = false;
                }
            }

            return allFilesProcessed;
        }

        private bool ExecuteLzmaProcess_Unique(string lzmaPath, string inputFilePath)
        {
            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo.FileName = lzmaPath;
                    process.StartInfo.Arguments = $"\"{inputFilePath}\"";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();

                    // To log output or errors (optional but useful for debugging)
                    string output = process.StandardOutput.ReadToEnd();
                    string errors = process.StandardError.ReadToEnd();

                    process.WaitForExit();

                    // Check if the process exited successfully and the expected output file was created
                    if (process.ExitCode == 0 && File.Exists(inputFilePath + ".segs"))
                    {
                        Main.LogDebugInfo($"LZMA compression successful for {Path.GetFileName(inputFilePath)}");
                        return true;
                    }
                    else
                    {
                        Main.LogDebugInfo($"LZMA compression failed for {Path.GetFileName(inputFilePath)}: {errors}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"Error during LZMA compression: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> EncryptHCDBFilesAsync_Unique(string[] filePaths, string baseOutputDirectory)
        {
            bool allFilesProcessed = true;
            string segsFileSHA1 = "";

            foreach (var filePath in filePaths)
            {
                string filename = Path.GetFileName(filePath);
                string originalFilenameWithoutExtension = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filename));

                // Create the subfolder "Database_Edits" in the existing path
                string subfolderPath = Path.Combine(baseOutputDirectory, "Database_Edits");
                if (!Directory.Exists(subfolderPath))
                {
                    Directory.CreateDirectory(subfolderPath);
                }

                try
                {
                    byte[] fileContent = await File.ReadAllBytesAsync(filePath);
                    byte[]? encryptedContent = null;
                    string inputSHA1 = "";

                    using (SHA1 sha1 = SHA1.Create())
                    {
                        byte[] SHA1Data = sha1.ComputeHash(fileContent);
                        inputSHA1 = BitConverter.ToString(SHA1Data).Replace("-", ""); // Full SHA1 of input content
                        segsFileSHA1 = inputSHA1; // Store the SHA1 of the .segs file
                        Main.LogDebugInfo($"Input SHA1 for {filename}: {inputSHA1}");

                        // Encrypt the content
                        string computedSha1 = inputSHA1.Substring(0, 16);
                        encryptedContent = CDSProcess.CDSEncrypt_Decrypt(fileContent, computedSha1, (int)CdnMode.RETAIL);

                        if (encryptedContent != null)
                        {
                            // Define the 4 file names
                            string[] hcdbFilenames = new string[]
                            {
                        "Objects/ObjectCatalogue_5_SCEAsia.hcdb",
                        "Objects/ObjectCatalogue_5_SCEJ.hcdb",
                        "Objects/ObjectCatalogue_5_SCEA.hcdb",
                        "Objects/ObjectCatalogue_5_SCEE.hcdb"
                            };

                            foreach (var hcdbFilename in hcdbFilenames)
                            {
                                string outputPath = Path.Combine(subfolderPath, Path.GetFileName(hcdbFilename));
                                await File.WriteAllBytesAsync(outputPath, encryptedContent);
                                Main.LogDebugInfo($"File {hcdbFilename} encrypted and written to {outputPath}.");
                            }

                            // After successful encryption, delete the .segs file
                            File.Delete(filePath);
                            Main.LogDebugInfo($"Temporary file {filePath} deleted after successful encryption.");
                        }
                        else
                        {
                            allFilesProcessed = false;
                            Main.LogDebugInfo($"Encryption failed for file {filename}, no data written.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Main.LogDebugInfo($"Error encrypting {filename}: {ex.Message}");
                    allFilesProcessed = false;
                }
            }

            if (allFilesProcessed)
            {
                ShowSuccessMessageBox(segsFileSHA1, Path.Combine(baseOutputDirectory, "Database_Edits")); // Display the success message box with SHA-1 and open save location
            }

            return allFilesProcessed;
        }


        private void ShowSuccessMessageBox(string segsFileSHA1, string saveDirectory)
        {
            // Update the LatestHCDBSHA1textbox and set the visibility of LatestHCDBSHA1panel
            Dispatcher.Invoke(() =>
            {
                // Find the LatestHCDBSHA1textbox and set its text to the computed SHA1 hash
                var latestHCDBSHA1textbox = Main.TssTabView.LatestHCDBSHA1textbox;
                if (latestHCDBSHA1textbox != null)
                {
                    latestHCDBSHA1textbox.Text = segsFileSHA1;
                }
                else
                {
                    MessageBox.Show("LatestHCDBSHA1textbox not found!");
                }

                // Find the LatestHCDBSHA1panel and set its visibility to visible
                var latestHCDBSHA1panel = Main.TssTabView.LatestHCDBSHA1panel;
                if (latestHCDBSHA1panel != null)
                {
                    latestHCDBSHA1panel.Visibility = Visibility.Visible;
                }
                else
                {
                    MessageBox.Show("LatestHCDBSHA1panel not found!");
                }
            });

            // Create the success message box
            var messageBox = new Window
            {
                Title = "4 x HCDB Created Successfully",
                Width = 350,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = (Brush)new BrushConverter().ConvertFromString("#242424"),
                Foreground = Brushes.White,
                WindowStyle = WindowStyle.None,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#000000"),
                BorderThickness = new Thickness(1)
            };

            var stackPanel = new StackPanel
            {
                Margin = new Thickness(10)
            };

            var textBlock = new TextBlock
            {
                Text = "HCDB Creation was successful.\n\nSHA1 of the .segs file:",
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 14,
                Foreground = Brushes.White
            };

            var sha1TextBox = new TextBox
            {
                Text = segsFileSHA1,
                IsReadOnly = true,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 14,
                Width = 330,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#C0BFBF"),
                Background = (Brush)new BrushConverter().ConvertFromString("#121212"),
                BorderThickness = new Thickness(0)
            };

            var copyButton = new Button
            {
                Content = "Copy SHA1",
                Width = 100,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 11,
                Foreground = Brushes.White,
                Background = (Brush)new BrushConverter().ConvertFromString("#181818"),
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(3),
                Margin = new Thickness(0, 0, 0, 10),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#000000")
            };
            copyButton.Click += (s, e) =>
            {
                Clipboard.SetText(segsFileSHA1);
                string originalContent = copyButton.Content.ToString();
                copyButton.Content = "Copied";

                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                timer.Tick += (sender, args) =>
                {
                    copyButton.Content = originalContent;
                    timer.Stop();
                };
                timer.Start();
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 70,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 11,
                Foreground = Brushes.White,
                Background = (Brush)new BrushConverter().ConvertFromString("#181818"),
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(3),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#000000")
            };
            okButton.Click += (s, e) =>
            {
                messageBox.Close();
            };

            stackPanel.Children.Add(textBlock);
            stackPanel.Children.Add(sha1TextBox);
            stackPanel.Children.Add(copyButton);
            stackPanel.Children.Add(okButton);

            messageBox.Content = stackPanel;
            messageBox.ShowDialog();
        }

        private void BulkEditSQLButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(Main.sqlFilePath))
            {
                MessageBox.Show("Please select a SQL file first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            BulkEditorForDB bulkEditor = new BulkEditorForDB(this, Main.sqlFilePath);
            bulkEditor.ShowDialog();
        }

        // Handlers used in this tab's XAML that live elsewhere.
        private void txtObjectId_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Main.txtObjectId_MouseDoubleClick(sender, e);
    }
}
