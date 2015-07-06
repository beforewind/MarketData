﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); 
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/
using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MarketData.GoogleFinance;
using MarketData.ToolBox;

namespace QuantConnect.GoogleFinanceUI
{
    /// <summary>
    /// Code behid for the form
    /// </summary>
    public partial class FormGoogleFinanceTest : Form
    {
        #region Variables

        private string downloadedData;
        private NameValueCollection appSettings;
        private string _defaultOutputDirectory;
        //private DownloadURIBuilder uriBuilder;


        #endregion

        #region Initialization

        /// <summary>
        /// Constructor for the Form
        /// </summary>
        public FormGoogleFinanceTest()
        {
            InitializeComponent();
            checkBoxDateTime.Enabled = false;
            checkBoxSplitDays.Enabled = false;
            checkBoxZipOutput.Enabled = false;
            buttonList.Enabled = true;
            buttonList.Enabled = radioButtonMinutes.Checked || radioButtonAllData.Checked;
            //System.Configuration.AppSettingsReader reader = new AppSettingsReader();
            appSettings = ConfigurationManager.AppSettings;
            for (int i = 0; i < appSettings.Count; i++)
            {
                if (appSettings.GetKey(i) == "defaultOutputDirectoryPath")
                {
                    _defaultOutputDirectory = appSettings[i];
                }

            }
        }

        /// <summary>
        /// Event handler for the OnLoad Event
        /// </summary>
        /// <param name="e">EventArgs - the args for the on load event</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            setTextBoxDataContentToDownloadData();
            updateURL();
        }

        #endregion

        #region "private methods"

        private async Task SaveFiles(string saveToFilename, string ticker)
        {
            // defaults to the executing directory
            var directory = GetSaveDirectory();
            if (directory.Length == 0)
                directory = Config.GetDefaultDownloadDirectory();
                
            string path = directory + @"\" + saveToFilename;
            FileInfo info = new FileInfo(path);
            using (StreamWriter sw = new StreamWriter(info.FullName))
            {
                sw.Write(richTextBoxData.Text);
            }
            if (checkBoxSplitDays.Checked)
            {
                // Save the data in the text box, splitting into days into the {ticker} subfolder of the saveFileDialog.OutputDirectory
                //  Notice this is a different location than saving files from a list because it does not segregate into Exchange\singleLetter\symbol
                string quotes = richTextBoxData.Text;
                string exchange = textBoxExchange.Text;
                //DirectoryInfo exchangeDirectoryInfo = ExchangeDirectoryFactory.Create(exchange, directory);
                if (directory != null)
                {
                    await SaveByDay(directory, quotes, ticker);
                }
            }


            return;
        }

        private async Task SaveByDay(string directory, string quotes, string ticker = @"")
        {
            TimeSpan ts = new TimeSpan(-15, 0, 0, 0);
            DateTime startDate = DateTime.Now - ts;
            DateTime dt = DateTime.Now;
            int day = 0;
            StringBuilder sb;
            string[] lines = quotes.Split('\n');
            sb = new StringBuilder();


            foreach (string line in lines)
            {
                try
                {
                    if (line.Length == 0)
                    {
                        await WriteStreamAsync(dt, sb.ToString(), directory, ticker);
                    }
                    else
                    {
                        string[] elements = line.Split(',');
                        if (checkBoxDateTime.Checked) // Milliseconds
                        {
                            dt = System.Convert.ToDateTime(elements[elements.Length - 1]);
                        }
                        else
                        {
                            try
                            {
                                dt = System.Convert.ToDateTime(elements[0]);
                            }
                            catch (Exception xx)
                            {
                                throw new Exception(xx.Message);
                            }
                        }
                        if (dt.Day != day)
                        {
                            if (day > 0)
                            {
                                await WriteStreamAsync(dt, sb.ToString(), directory, ticker);
                            }
                            sb = new StringBuilder();
                            day = dt.Day;
                        }
                        //string record = line.Substring(0, line.LastIndexOf(",", System.StringComparison.Ordinal));
                        sb.AppendLine(line);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception(ex.Message);
                }


            }

        }
        /// <summary>
        /// Writes a ticker symbol's downloaded data to a file
        /// </summary>
        /// <param name="tradingDate">The date for the trades</param>
        /// <param name="buffer">the csv data to be written</param>
        /// <param name="directory">the base folder to write the data to.  Generally this will be the exchange folder and single letter folder</param>
        /// <param name="ticker">the symbol to write.  If a folder for the symbols in NYSE\A\{symbol} does not exist it will be created</param>
        /// <returns>A generic task for the async call.  There is not actual result, but Async/Await does not allow voids.</returns>
        public async Task WriteStreamAsync(DateTime tradingDate, string buffer, string directory, string ticker)
        {
            if (directory.Length == 0)
                directory = Config.GetDefaultDownloadDirectory();
            if (!directory.EndsWith(@"\"))
                directory += @"\";
            if (ticker.Length == 0)
                throw new NullReferenceException("Ticker symbol is null");

            // Add the ticker symbol directory to the \exchange\singleletter\ directory and make sure it exists.
            string folder = directory + ticker + @"\";
            if (!System.IO.Directory.Exists(folder))
                System.IO.Directory.CreateDirectory(folder);

            // Create the dated filename
            string filename = FilenameDateFormatter.FormatDt(tradingDate) + "_trade.csv";

            // Create the final path for writing the stream
            string filepath = folder + filename;


            // No point if writing if there is no data in the buffer 
            //  For example, on a bank holiday
            if (buffer.Length > 0)
            {
                // Check to see if the trading day is today,
                //  and if so overwrite the current day's file.  
                // If we download during the trading day, we will get the latest day's trades
                //  up to the amount of delay Google builds into their quote mechanism.  (I think 15 minutes)
                // If the current hour is after say 4:45 GMT-5 (East Coast time) we will get all of the day's prices
                if (tradingDate.Day == DateTime.Now.Day)
                {
                    // If today is Saturday or Sunday, the equity markets are closed.  
                    if (!(DateTime.Now.DayOfWeek == DayOfWeek.Sunday || DateTime.Now.DayOfWeek == DayOfWeek.Saturday))
                    {
                        await WriteFileAsync(buffer, filepath, filename);
                    }
                }

                // If we write a file in the above if block, we do not need to write it again
                // If we have a file in the folder already for a date, we do not need to write it again
                if (!File.Exists(filename))
                {
                    await WriteFileAsync(buffer, filepath, filename);
                }
            }
        }

        private async Task WriteFileAsync(string buffer, string filepath, string filename)
        {
            if (checkBoxZipOutput.Checked)
            {
                string zippath = filepath.Replace("csv", "zip");

                Compression.Zip(zippath, filename, buffer);

                //byte[] buf = Encoding.ASCII.GetBytes(buffer);
                //using (var fs = File.Create(zippath, buf.Length))
                //{
                //    using (var s = new ZipOutputStream(fs))
                //    {
                //        s.PutNextEntry(filename);
                //        await s.WriteAsync(buf, 0, buf.Length);
                //    }
                //}
            }
            else
            {
                using (StreamWriter sw = new StreamWriter(filepath, false))
                {
                    await sw.WriteAsync(buffer);
                    await sw.FlushAsync();
                }
            }
        }


        /// <summary>
        /// Builds the output file name by check the form's check boxes
        /// </summary>
        /// <returns>A filename</returns>
        private string BuildOutputFilename()
        {
            StringBuilder sb = new StringBuilder();
            if (textBoxExchange.Text.Length > 0)
                sb.Append(textBoxExchange.Text + "_");
            sb.Append(textBoxTicker.Text + "_");
            if (checkBoxRawData.Checked)
            {
                sb.Append("RawData");
            }
            else if (radioButtonMinutes.Checked)
            {
                if (checkBoxDateTime.Checked)
                {
                    sb.Append("Milliseconds");
                }
                else
                {
                    sb.Append("DateTime");
                }
            }
            else if (radioButtonAllData.Checked)
            {
                sb.Append("AllData");
            }
            else if (radioButtonSince.Checked)
            {
                sb.Append("Since");
                DateTime dt = System.Convert.ToDateTime(dateTimePickerSinceDate.Text);
                sb.Append(FilenameDateFormatter.FormatDt(dt));
                sb.Append("-");
                sb.Append(FilenameDateFormatter.FormatDt(DateTime.Now));
            }
            if (radioButtonLastQuoute.Checked)
            {
                sb.AppendFormat("_LastQuote_{0}", FilenameDateFormatter.FormatDt(DateTime.Now));
            }

            sb.Append(".csv");
            return sb.ToString();
        }



        /// <summary>
        /// Downloads the data from Google Finance based on the url in the textBoxURL text box
        /// </summary>
        private void downloadData()
        {
            string uri = textBoxURL.Text;

            if (String.IsNullOrEmpty(uri))
                return;

            using (WebClient wClient = new WebClient())
            {
                downloadedData = wClient.DownloadString(uri);
            }

            if (checkBoxRawData.Checked)
            {
                richTextBoxData.Text = downloadedData;  // show the raw data
            }
            else
            {
                setTextBoxDataContentToDownloadData();  // process the data to show date time
            }
        }

        /// <summary>
        /// Processes the downloaded data (with the header) into DateTime format in the first column
        /// </summary>
        private void setTextBoxDataContentToDownloadData()
        {
            if (String.IsNullOrEmpty(downloadedData))
            {
                richTextBoxData.Text = string.Empty;
                return;
            }

            if (checkBoxRawData.Checked)
            {
                richTextBoxData.Text = downloadedData;
            }
            else
            {
                if (checkBoxDateTime.Checked)
                    fillTextBoxWithProcessedData("Millisecond");
                else
                    fillTextBoxWithProcessedData("DateTime");
            }
        }

        /// <summary>
        /// Fills the textBoxURL with data formated with DateTime or milliseconds in the first column
        /// </summary>
        /// <param name="dateFormat"></param>
        private void fillTextBoxWithProcessedData(string dateFormat)
        {
            // Stream from the downloaded Data
            using (MemoryStream ms = new MemoryStream(System.Text.Encoding.Default.GetBytes(downloadedData)))
            {
                DataProcessor dp = new DataProcessor();
                string errorMessage;
                string resultValue;

                //processStreamOfOneMinuteBarsToReplaceGoogleDateWithFormatted
                if (radioButtonLastQuoute.Checked)
                {
                    // Only display the last quote of the stream
                    resultValue = dp.processStreamMadeOfOneMinuteBarsToExtractMostRecentOHLCVForCurrentDay(ms, out errorMessage);
                }
                else if (radioButtonMinutes.Checked)
                {
                    // show minute data
                    resultValue = dp.ProcessStreamOfOneMinuteBarsToReplaceGoogleDateWithFormatted(ms, dateFormat, out errorMessage);
                }
                else
                {
                    // Show daily data
                    resultValue = dp.processStreamMadeOfOneDayLinesToExtractHistoricalData(ms, out errorMessage);
                }

                // Show an error message or the processed data
                if (!String.IsNullOrEmpty(errorMessage))
                {
                    richTextBoxData.Text = errorMessage;
                    ErrorFunction(errorMessage);

                }
                else
                {
                    richTextBoxData.Text = resultValue;
                }
            }
        }
        #region "Move Files during development"
        /// <summary>
        /// Moves the symbol folder up one folder to correct a problem with the WriteStream adding an extra symbol subdirectory
        /// </summary>
        /// <param name="exchangeRoot">string - the starting folder</param>
        private static void MoveFiles(string exchangeRoot)
        {

            DirectoryInfo root = new DirectoryInfo(Config.GetDefaultDownloadDirectory()); 
            if (exchangeRoot.Length > 0)
            {
                root = new DirectoryInfo(exchangeRoot);
            }
            var subdirs1 = root.GetDirectories();

            foreach (DirectoryInfo info1 in subdirs1)
            {
                System.Threading.Thread.Sleep(100);
                var subdirs2 = info1.GetDirectories();
                foreach (DirectoryInfo info2 in subdirs2)
                {
                    try
                    {
                        if (info2.Name.Contains(@"/") || info2.Name.Contains(@"\"))
                            continue;
                        var subdirs3 = info2.GetDirectories();
                        foreach (DirectoryInfo info3 in subdirs3)
                        {
                            try
                            {
                                if (info3.FullName.ToLower().Contains("ddd"))
                                    Debug.WriteLine(info3.FullName);

                                var files = info3.GetFiles();
                                foreach (var file3 in files)
                                {
                                    try
                                    {
                                        string oldname = file3.FullName;
                                        string newname = info2.FullName + @"\" + file3.Name;
                                        File.Move(oldname, newname);

                                    }
                                    catch (Exception ex)
                                    {
                                        throw new Exception(ex.Message + file3.FullName);
                                    }
                                }
                                //System.Threading.Thread.Sleep(250);
                                Directory.Delete(info3.FullName, true);
                            }
                            catch (Exception ex)
                            {
                                throw new Exception(ex.Message + info3.FullName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(ex.Message + info2.FullName);
                    }
                }
            }
        }
        #endregion
        private void CheckPriceFormat(string exchangeRoot)
        {
            const string NotEmpty = @"The directory is not empty.";
            DirectoryInfo root = new DirectoryInfo(Config.GetDefaultDownloadDirectory());
            if (exchangeRoot.Length > 0)
            {
                root = new DirectoryInfo(exchangeRoot);
            }


            var subdirs1 = root.GetDirectories();

            foreach (DirectoryInfo singleLetterInfo in subdirs1)
            {
                var singleLetterInfos = singleLetterInfo.GetDirectories();
                foreach (DirectoryInfo singleInfo in singleLetterInfos)
                {
                    try
                    {
                        Debug.WriteLine(singleInfo.FullName);
                        if (singleInfo.FullName.Contains("ALL-B"))
                            Debug.WriteLine("here");
                        var fileInfos = singleInfo.GetFiles();
                        foreach (FileInfo fi in fileInfos)
                        {
                            try
                            {
                                int secondval = ReadFirstLineSecondValue(fi.FullName);
                                if (secondval == 0)
                                {
                                    fi.Delete();
                                    continue;
                                }
                                //int secondval = ReadFirstLineSecondValue(singleInfo.Parent.FullName + @"\ALL-B.csv");
                                if (secondval < 10000)
                                {
                                    //Debug.WriteLine(fi.Name);
                                    StringBuilder sb = new StringBuilder();
                                    using (StreamReader sr = new StreamReader(fi.FullName))
                                    {
                                        while (!sr.EndOfStream)
                                        {
                                            string line = sr.ReadLine();
                                            sb.AppendLine(MultiplyBy10000(line));
                                        }
                                    }
                                    using (StreamWriter wr = new StreamWriter(fi.FullName, false))
                                    {
                                        wr.Write(sb.ToString());
                                        wr.Flush();
                                    }

                                }
                            }
                            catch (Exception ex)
                            {
                                if (ex.Message.Contains(NotEmpty))
                                {
                                    throw new Exception(ex.Message + singleInfo.FullName);
                                }
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        throw new Exception(ex.Message + singleInfo.FullName);
                    }
                }
            }
        }

        private int ReadFirstLineSecondValue(string fullName)
        {
            Decimal seconditem;
            using (StreamReader sr = new StreamReader(fullName))
            {
                string line = sr.ReadLine();
                if (line == null)
                    return 0;
                string[] arr = line.Split(',');
                seconditem = Decimal.Parse(arr[2]);

            }
            return Convert.ToInt32(seconditem);
        }


        private string MultiplyBy10000(string line)
        {
            StringBuilder sb = new StringBuilder();
            var arr = ConvertArrayToPriceTimes10000(line);
            for (int i = 0; i < arr.Length - 1; i++)
            {
                sb.Append(arr[i]);
                sb.Append(",");
            }
            sb.Append(arr[arr.Length - 1]);
            return sb.ToString();
        }

        private static string[] ConvertArrayToPriceTimes10000(string line)
        {
            string[] arr = line.Split(',');
            for (int i = 1; i < 5; i++)
            {
                if (System.Convert.ToDecimal(arr[i]) < 9999m)
                {
                    arr[i] = (Convert.ToInt32(Convert.ToDecimal(arr[i]) * 10000).ToString(CultureInfo.InvariantCulture));
                }
            }
            return arr;
        }

        #endregion

        #region Useful methods
        private void ErrorFunction(string errorMessage)
        {
            if (String.IsNullOrEmpty(errorMessage))
            {
                Debug.Fail("No error message.");
                return;
            }

            MessageBox.Show(errorMessage, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }


        #endregion

        #region Interaction with GoogleFinanceDownloader
        /// <summary>
        /// Update the url in the text box
        /// </summary>
        private void updateURL()
        {
            textBoxURL.Text = getDownloadURI();
        }
        /// <summary>
        /// Gets teh Url from the Builder
        /// </summary>
        /// <returns>string - the url calculated in the Builder</returns>
        private string getDownloadURI()
        {
            string ticker = textBoxTicker.Text;
            string exchange = textBoxExchange.Text;

            if (String.IsNullOrEmpty(ticker))
            {
                return string.Empty;
            }

            DownloadURIBuilder uriBuilder = new DownloadURIBuilder(exchange, ticker);

            if (radioButtonAllData.Checked)
            {
                return uriBuilder.GetGetPricesUrlToDownloadAllData(DateTime.Now);
            }
            else if (radioButtonLastQuoute.Checked)
            {
                return uriBuilder.GetGetPricesUrlForLastQuote();
            }
            else if (radioButtonSince.Checked)
            {
                DateTime startDate = dateTimePickerSinceDate.Value.Date,
                    endDate = DateTime.Now.Date;
                if (endDate < startDate)
                { //It's impossible to download data from the future. That's why no URL is returned in this case.
                    return string.Empty;
                }
                else
                {
                    return uriBuilder.GetGetPricesUrlForRecentData(startDate, endDate);
                }
            }
            else if (radioButtonMinutes.Checked)
            {
                return uriBuilder.GetGetPricesUrlForLastNumberOfDays(15);
            }
            else
            {
                return string.Empty;
            }
        }
        #endregion



        #region Control events
        private async void buttonSave_Click(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(richTextBoxData.Text))
            {
                ErrorFunction("No data to save!");
                return;
            }

            try
            {
                var saveToFilename = BuildOutputFilename();
                await SaveFiles(saveToFilename, textBoxTicker.Text);
            }
            catch (Exception exc)
            {
                ErrorFunction(exc.Message);
            }
        }
        private void textBoxURL_TextChanged(object sender, EventArgs e)
        {
            buttonDownload.Enabled = !String.IsNullOrEmpty(textBoxURL.Text);
        }

        /// <summary>
        /// Handles the click event on the Download button
        /// </summary>
        /// <param name="sender">Button - the Download button</param>
        /// <param name="e">EventArgs - the event args for the event, which are not used here</param>
        private void buttonDownload_Click(object sender, EventArgs e)
        {
            Cursor currentCursor = Cursor.Current;
            currentCursor = Cursors.WaitCursor;

            try
            {
                downloadData();
            }
            catch (Exception exc)
            {
                ErrorFunction(exc.Message);
            }
            finally
            {
                Cursor.Current = currentCursor;
            }
        }
        private void checkBoxRawData_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxRawData.Checked)
            {
                checkBoxDateTime.Checked = false;
                checkBoxSplitDays.Checked = false;
            }
            setTextBoxDataContentToDownloadData();
        }

        /// <summary>
        /// Handles the DateTime/Milliseconds option check box clicks
        /// </summary>
        /// <param name="sender">checkBoxDateTime - the checkbox being clicked</param>
        /// <param name="e">EventArgs - the event args for the event, which are not used here</param>
        private void checkBoxDateTime_CheckedChanged(object sender, EventArgs e)
        {
            var cb = (CheckBox)sender;
            if (cb.Checked)
                cb.Text = "Milliseconds";
            else
            {
                cb.Text = "DateTime";
            }
            downloadData();
            setTextBoxDataContentToDownloadData();

        }
        /// <summary>
        /// Handles the Value_Changed event for a uri parameter
        /// </summary>
        /// <param name="sender">object - one of the controls that affects the url</param>
        /// <param name="e">EventArgs - the event args for the event, which are not used here</param>
        private void uriParameterControl_ValueChanged(object sender, EventArgs e)
        {
            if (checkBoxRawData.Checked)
            {
                checkBoxDateTime.Checked = false;
                checkBoxSplitDays.Checked = false;
            }
            dateTimePickerSinceDate.Enabled = radioButtonSince.Checked;
            checkBoxDateTime.Enabled = radioButtonMinutes.Checked;
            checkBoxSplitDays.Enabled = radioButtonMinutes.Checked;
            buttonList.Enabled = radioButtonMinutes.Checked;
            buttonCheckDataFormat.Enabled = radioButtonMinutes.Checked;
            buttonMoveData.Enabled = radioButtonMinutes.Checked;

            updateURL();
        }

        /// <summary>
        /// Handles the event fired when the large text box's data changes
        /// </summary>
        /// <param name="sender">TextBox - the text box showing the data downloaded</param>
        /// <param name="e">EventArgs - the event args for the event, which are not used here</param>
        private void textBoxData_TextChanged(object sender, EventArgs e)
        {
            buttonSave.Enabled = !String.IsNullOrEmpty(richTextBoxData.Text);
        }
        /// <summary>
        /// Handles the event when the Minutes radion button is checked/unchecked
        /// </summary>
        /// <param name="sender">RadioButton - the button</param>
        /// <param name="e">EventArgs - the event args for the event, which are not used here</param>
        private void radioButtonMinutes_CheckedChanged(object sender, EventArgs e)
        {
            RadioButton rdo = (RadioButton)sender;
            if (rdo.Checked)
            {
                checkBoxDateTime.Enabled = true;
                checkBoxSplitDays.Enabled = true;
                checkBoxZipOutput.Enabled = true;
            }
            else
            {
                checkBoxDateTime.Checked = false;
                checkBoxSplitDays.Checked = false;
                checkBoxZipOutput.Checked = false;
            }
        }
        // From nasdaq  <a href="companies-by-industry.aspx?exchange=NYSE&amp;render=download" rel="nofollow"><b>Download this list</b></a>
        private async void buttonList_Click(object sender, EventArgs e)
        {
            string tickerListFilename = GetTickerList();
            if (tickerListFilename != string.Empty)
            {
                FileInfo tickerListInfo = new FileInfo(tickerListFilename);
                string directory = Config.GetDefaultDownloadDirectory();
                directory = GetSaveDirectory();
                if (directory.Length > 0)
                {
                    Cursor currentCursor = Cursor.Current;
                    currentCursor = Cursors.WaitCursor;
                    var caption = buttonList.Text;
                    // update ui
                    buttonList.Text = @"Downloading";
                    buttonList.Enabled = false;
                    checkBoxDateTime.Checked = true;
                    checkBoxSplitDays.Checked = true;
                    checkBoxZipOutput.Checked = true;

                    try
                    {
                        if (radioButtonMinutes.Checked)
                        {
                            // Save the data to zip files
                            MinuteDownloader minuteDownloader = new MinuteDownloader(tickerListInfo, directory)
                            {
                                FormatAsMilliseconds = true,
                                SplitDays = true,
                                ZipOutput = true
                            };
                            await minuteDownloader.DownloadDataFromListAsync();
                        }
                        if (radioButtonAllData.Checked)
                        {
                            AllDataDownloader allDataDownloader = new AllDataDownloader(tickerListInfo, directory)
                            {
                                ZipOutput = true
                            };
                            await allDataDownloader.DownloadDataFromListAsync();
                        }
                    }
                    catch (Exception exc)
                    {
                        ErrorFunction(exc.Message);
                    }
                    finally
                    {
                        Cursor.Current = currentCursor;
                        buttonList.Enabled = true;
                        buttonList.Text = caption;
                        MessageBox.Show(@"Done");
                    }
                }
            }
        }
        #endregion


        /// <summary>
        /// Checks the data format in the minute data to make sure prices are multiplied by 10000
        /// </summary>
        /// <param name="sender">The button being clicked</param>
        /// <param name="e">null</param>
        private void buttonCheckDataFormat_Click(object sender, EventArgs e)
        {
            string directory = GetSaveDirectory();
            if (directory.Length > 0)
            {
                CheckPriceFormat(directory);
            }
        }

        #region "Move files during development"
        /// <summary>
        ///  Moves the minute data symbol folder up one folder to correct a bug
        /// </summary>
        /// <param name="sender">the button being clicked</param>
        /// <param name="e">No event args</param>
        private void buttonMoveData_Click(object sender, EventArgs e)
        {
            string directory = GetSaveDirectory();
            if (directory.Length > 0)
            {
                MoveFiles(directory);
            }
        }

        /// <summary>
        /// Allow the user to select the directory to save the file to.
        /// It defaults to the ExecutingDirectory
        /// </summary>
        /// <returns>string the directory the user selected</returns>
        private string GetSaveDirectory()
        {
            string exchange = textBoxExchange.Text;
            string directory;

            if (_defaultOutputDirectory.Length == 0)
                directory = Config.GetDefaultDownloadDirectory();
            else
            {
                directory = _defaultOutputDirectory;
            }
            folderBrowserDialog1.Description = "Select Destination Folder";
            folderBrowserDialog1.SelectedPath = directory;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                var exchangeRoot = new DirectoryInfo(folderBrowserDialog1.SelectedPath);
                directory = exchangeRoot.FullName;
                return directory;
            }
            return string.Empty;
        }
        #endregion
        
        private string GetTickerList()
        {
            var directory = Config.GetDefaultDownloadDirectory();
            openFileDialog1.CheckFileExists = true;
            openFileDialog1.InitialDirectory = directory;
            openFileDialog1.Title = "Select Symbol List";

            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                return openFileDialog1.FileName;
            }
            return string.Empty;
        }
    }
}