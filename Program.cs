﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
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
        
        private static void Login(string username, string password, ChromeDriver cd)
        {
            cd.Url = @"https://leetcode.com/submissions/#/1";
            cd.Navigate();
            IWebElement e = cd.FindElementById("id_login");
            e.SendKeys(username);
            Console.WriteLine("Login entered successfully");
            e = cd.FindElementById("id_password");
            e.SendKeys(password);
            Console.WriteLine("Password entered successfully");

            var signInButtonUnlockWait = new WebDriverWait(cd, TimeSpan.FromSeconds(5));
            signInButtonUnlockWait.Until(driver =>
            {
                try
                {
                    driver.FindElement(By.Id("initial-loading"));
                    return false;
                }
                catch (NoSuchElementException)
                {
                    Console.WriteLine("Preloader destroyed, ready for sign in");
                    return true;
                }
            });
            e = cd.FindElementById("signin_btn");
            Console.WriteLine("Attempting sign in");
            e.Click();
        }

        private static IWebElement GetNextButton(ChromeDriver cd)
        {
            var navButtonsContainerWait = new WebDriverWait(cd, TimeSpan.FromSeconds(5));
            navButtonsContainerWait.Until(driver => driver.FindElement(By.CssSelector(".pager")).Displayed);
            Console.WriteLine("Navigation buttons container detected successfully");
            
            IWebElement nextPageButton =
                cd.FindElementByCssSelector(".next");
           return nextPageButton;
        }

        static (string,string) DownloadCodeFromPage(string submissionUrl, ChromeDriver cd)
        {
            Console.WriteLine("Open " + submissionUrl + " in a new tab");
            ((IJavaScriptExecutor)cd).ExecuteScript("window.open();");
            cd.SwitchTo().Window(cd.WindowHandles.Last());
            cd.Navigate().GoToUrl(submissionUrl);
            Console.WriteLine("Opened new tab successfully");
            var codeBlockWait = new WebDriverWait(cd, TimeSpan.FromSeconds(5));
            codeBlockWait.Until(driver => driver.FindElement(By.ClassName("ace_content")).Displayed);
            IWebElement problemLink = cd.FindElementByCssSelector("a.inline-wrap");
            string problemName = problemLink.Text;
            Console.WriteLine("Current problem name: "+problemName);
            Thread.Sleep(500);
            IWebElement codeBlock = cd.FindElementByClassName("ace_content");
            Console.WriteLine("Code block captured. Downloading...");
            string codeData = codeBlock.FindElement(By.CssSelector("div.ace_layer.ace_text-layer")).GetAttribute("innerHTML");
            Console.WriteLine("Successfully downloaded code data");
            Console.WriteLine("Closing tab: " + submissionUrl);
            cd.Close();
            cd.SwitchTo().Window(cd.WindowHandles[0]);
            Console.WriteLine("Tab closed successfully");
            return (problemName,codeData);
        }

        static void RemoveRedunduntDataFromCode(ref string code)
        {
            code = code.Replace(
                "<div class=\"ace_line_group\" style=\"height:17px\"><div class=\"ace_line\" style=\"height:17px\">",
                "");
            code = code.Replace("</div></div>", "\n");
        }
        static void DownloadSolutions(ChromeDriver cd)
        {
            if (!Directory.Exists(SOLUTION_FOLDER))
            {
                Directory.CreateDirectory(SOLUTION_FOLDER);
            }
            
            Console.WriteLine("Attempt to load submissions page");
            var submissionsTableWait = new WebDriverWait(cd, TimeSpan.FromSeconds(10));
            submissionsTableWait.Until(driver => driver.FindElement(By.CssSelector(".table")).Displayed);
            Console.WriteLine("Submissions page loaded. Sign in successful");
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
                Thread.Sleep(1000);
            }

            Console.WriteLine($"\"Next\" button is Disabled, commencing saving sequence");
            Console.WriteLine($"Detected {submissionsToDownload.Count} accepted submissions");
            
            Dictionary<string,int> problemSolutionsAmount = new Dictionary<string, int>();
            for(int i = 0;i<submissionsToDownload.Count;i++)
            {
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
                string filename = $"{SOLUTION_FOLDER}/{problemName} v{problemSolutionsAmount[problemName]}.txt".Replace(' ','_');
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
            
        }
        
        public static void Main(string[] args)
        {
            if (!File.Exists("Credentials.txt"))
            {
                Console.WriteLine("First time login detected. Downloading additional files...");
                Console.WriteLine("Getting your Google Chrome version...");
                string version = GetGoogleChromeVersion();
                Console.WriteLine("Your Google Chrome version is: "+version);
                DownloadChromeDriver(version);
                File.WriteAllText("Credentials.txt","<username> <password>");
                Console.WriteLine("Enter your credentials to Credentials.txt that was just created and restart the program");
            }
            else
            {
                string credentials = File.ReadAllText("Credentials.txt");
                ChromeDriver cd = new ChromeDriver(@"chromedriver_win32");
                Login(credentials.Split(' ')[0],credentials.Split(' ')[1],cd);
                DownloadSolutions(cd);
                Console.WriteLine("Scrape completed successfully!!!");
                cd.Close();
            }
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}