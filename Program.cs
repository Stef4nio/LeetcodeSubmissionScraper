using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace LeetcodeSubmissionScraper
{
    
    internal class Program
    {
        const string SOLUTION_FOLDER = "Solution";

        private static string GetGoogleChromeVersion()
        {
            string googleChromeVersionQuery =
                "reg query \"HKEY_CURRENT_USER\\Software\\Google\\Chrome\\BLBeacon\" /v version";
            try
            {
                ProcessStartInfo procStartInfo =
                    new ProcessStartInfo("cmd", "/c " + googleChromeVersionQuery);
                procStartInfo.RedirectStandardOutput = true;
                procStartInfo.UseShellExecute = false;
                procStartInfo.CreateNoWindow = true;
                Process proc = new Process();
                proc.StartInfo = procStartInfo;
                proc.Start();
                string result = proc.StandardOutput.ReadToEnd();
                return result.Split(' ')[12].Trim();
            }
            catch (Exception objException)
            {
                // Log the exception
                Console.Error.WriteLine(
                    "There was an error getting your Google Chrome version. Contact the developer for resolving this issue");
                return "";
            }
        }

        private static void DownloadChromeDriver(string version)
        {
            if (version == "")
            {
                throw new Exception("Couldn't get your Google Chrome version. Please contact the developer for resolving this issue");
            }
            using (var client = new WebClient())
            {
                try
                {
                    Console.WriteLine("Attempting to download ChromeDriver version: "+version);
                    client.DownloadFile($"http://chromedriver.storage.googleapis.com/{version}/chromedriver_win32.zip",
                        "chromedriver_win32.zip");
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("Couldn't download ChromeDriver. Please contact the developer for resolving this issue");
                    throw;
                }
            }
            Console.WriteLine("Download successful. Unzipping files...");
            try
            {
                ZipFile.ExtractToDirectory("chromedriver_win32.zip","chromedriver_win32");
                File.Delete("chromedriver_win32.zip");
            }
            catch (Exception e)
            {
                Console.Error.Write("Couldn't unzip file. Please contact the developer for resolving this issue");
                Console.WriteLine(e);
                throw;
            }
            Console.WriteLine("Unzipping successful");
        }
        
        private static IWebElement GetNextButton(ChromeDriver cd)
        {
            WaitForRecaptchaBan(cd,2000);
            var navButtonsContainerWait = new WebDriverWait(cd, TimeSpan.FromSeconds(5));
            navButtonsContainerWait.Until(driver => driver.FindElement(By.CssSelector(".pager")).Displayed);
            Console.WriteLine("Navigation buttons container detected successfully");
            
            IWebElement nextPageButton =
                cd.FindElementByCssSelector(".next");
           return nextPageButton;
        }

        static (string,string) DownloadCodeFromPage(string submissionUrl, ChromeDriver cd)
        {
            cd.Navigate().GoToUrl(submissionUrl);
            var codeBlockWait = new WebDriverWait(cd, TimeSpan.FromSeconds(5));
            codeBlockWait.Until(driver => driver.FindElement(By.ClassName("ace_content")).Displayed);
            IWebElement problemLink = cd.FindElementByCssSelector("a.inline-wrap");
            string problemName = problemLink.Text;
            Console.WriteLine("Current problem name: "+problemName);
            IWebElement codeBlock = cd.FindElementByClassName("ace_content");
            Console.WriteLine("Code block captured. Downloading...");
            string codeData = codeBlock.FindElement(By.CssSelector("div.ace_layer.ace_text-layer")).GetAttribute("innerHTML");
            Console.WriteLine("Successfully downloaded code data");
            return (problemName,codeData);
        }

        static void WaitForSubmissionsTable(ChromeDriver cd, int milliSeconds)
        { 
            WaitForRecaptchaBan(cd,1000);
            Console.WriteLine("Attempt to load submissions page");
            var submissionsTableWait = new WebDriverWait(cd, TimeSpan.FromMilliseconds(milliSeconds));
            submissionsTableWait.Until(driver => driver.FindElement(By.CssSelector(".table")).Displayed);
            Console.WriteLine("Submissions page loaded. Sign in successful");
        }

        static void WaitForRecaptchaBan(ChromeDriver cd, int milliSeconds)
        {
            var captchaCrashWait = new WebDriverWait(cd, TimeSpan.FromMilliseconds(milliSeconds));
            try
            {
                captchaCrashWait.Until(driver =>
                    driver.FindElement(By.ClassName("noty_body")).Displayed);
                Thread.Sleep(3000);
                cd.Navigate().Refresh();
                WaitForSubmissionsTable(cd,60000);
            }
            catch (WebDriverTimeoutException e)
            {
                Console.WriteLine("No reload required, continuing");
            }
        }

        static void OpenNewTab(ChromeDriver cd)
        {
            Console.WriteLine("Opening a new tab");
            ((IJavaScriptExecutor)cd).ExecuteScript("window.open();");
            cd.SwitchTo().Window(cd.WindowHandles.Last());
            Console.WriteLine("Opened new tab successfully");
        }

        static void CloseCurrentTab(ChromeDriver cd)
        {
            Console.WriteLine("Closing tab: " + cd.Url);
            cd.Close();
            cd.SwitchTo().Window(cd.WindowHandles[0]);
            Console.WriteLine("Tab closed successfully");
        }
        
        static void RemoveRedunduntDataFromCode(ref string code)
        {
            code = code.Replace(
                "<div class=\"ace_line_group\" style=\"height:17px\"><div class=\"ace_line\"","");
            code = Regex.Replace(code, " style=\"height:\\d{1,4}px\">", " ");
            code = code.Replace("</div></div>", "\n");
        }
        
        //downloadType: 1 - all submissions, 2 - one for each problem
        static void DownloadSolutions(ChromeDriver cd,int downloadType)
        {
            if (!Directory.Exists(SOLUTION_FOLDER))
            {
                Directory.CreateDirectory(SOLUTION_FOLDER);
            }
            WaitForSubmissionsTable(cd, 600000);
            IWebElement nextButton = GetNextButton(cd);
            List<string> submissionsToDownload = new List<string>();
            while (!nextButton.GetAttribute("class").Contains("disabled"))
            {
                Console.WriteLine($"\"Next\" button is Enabled, continuing scan");
                Console.WriteLine("Scanning for accepted submissions of current page");
                ReadOnlyCollection<IWebElement> acceptedSubmissionsButtons = cd.FindElementsByClassName("text-success");
                Console.WriteLine($"Accepted submissions on current page: {acceptedSubmissionsButtons.Count}. Gathering Links");
                submissionsToDownload.AddRange(acceptedSubmissionsButtons.Select(button => button.GetAttribute("href")).ToList());
                cd.Navigate().GoToUrl(nextButton.FindElement(By.XPath("a")).GetAttribute("href"));
                nextButton = GetNextButton(cd);
            }

            Console.WriteLine($"\"Next\" button is Disabled, commencing saving sequence");
            Console.WriteLine($"Detected {submissionsToDownload.Count} accepted submissions");
            
            Dictionary<string,int> problemSolutionsAmount = new Dictionary<string, int>();
            OpenNewTab(cd);
            for(int i = 0;i<submissionsToDownload.Count;i++)
            {
                string submissionUrl = submissionsToDownload[i];
                
                Console.Write($"[{i+1}/{submissionsToDownload.Count}] ");
                string problemName, codeData;
                
                
                
                (problemName, codeData) = DownloadCodeFromPage(submissionsToDownload[i], cd);
                if (!problemSolutionsAmount.ContainsKey(problemName))
                {
                    problemSolutionsAmount.Add(problemName,1);
                }
                else
                {
                    problemSolutionsAmount[problemName]++;
                }

                string filename = "";
                
                if (downloadType == 1)
                {
                    filename =
                        $"{SOLUTION_FOLDER}/{problemName} v{problemSolutionsAmount[problemName]}.txt".Replace(' ', '_');
                }
                else if(problemSolutionsAmount[problemName]==1)
                {
                    filename =
                        $"{SOLUTION_FOLDER}/{problemName}.txt".Replace(' ', '_');
                }
                else
                {
                    Console.WriteLine("Solution to this problem has been already downloaded. Skipping...");
                    continue;
                }

                Console.WriteLine($"Attempt to save solution of {problemName} at:\n{filename}");
                RemoveRedunduntDataFromCode(ref codeData);
                try
                {
                    File.WriteAllText(filename, codeData);
                    Console.WriteLine($"Saving successful.");
                }
                catch (Exception exception)
                {
                    Console.Write("Error while writing to file. Exception: ");
                    Console.WriteLine(exception);
                    throw;
                }
            }

            CloseCurrentTab(cd);

        }
        
        public static void Main(string[] args)
        {
            if (!Directory.Exists("chromedriver_win32"))
            {
                Console.WriteLine("First time login detected. Downloading additional files...");
                Console.WriteLine("Getting your Google Chrome version...");
                string version = GetGoogleChromeVersion();
                Console.WriteLine("Your Google Chrome version is: "+version);
                DownloadChromeDriver(version);
            }
            Console.Write("Do you want to download ALL submissions or just ONE for each problem? Enter 1 or 2: ");
            string answer = Console.ReadLine();
            int result;
            try
            {
                result = Int32.Parse(answer);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Invalid input. Shutting down.");
                throw;
            }
            ChromeDriver cd = new ChromeDriver(@"chromedriver_win32");
            cd.Url = @"https://leetcode.com/submissions/#/1";
            cd.Navigate();
            DownloadSolutions(cd,result);
            Console.WriteLine("Scrape completed successfully!!!");
            cd.Close();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}