using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace LeetcodeSubmissionScraper
{
    
    internal class Program
    {
        const string SOLUTION_FOLDER = "Solution";

        private static void Login(string username, string password, ChromeDriver cd)
        {
            cd.Url = @"https://leetcode.com/submissions/#/1";
            cd.Navigate();
            IWebElement e = cd.FindElementById("id_login");
            e.SendKeys(username);
            Console.WriteLine("Login entered");
            e = cd.FindElementById("id_password");
            e.SendKeys(password);
            Console.WriteLine("Password entered");
            try
            {
                cd.FindElement(By.Id("initial-loading"));
                Console.WriteLine("Preloader exists");
            }
            catch (NoSuchElementException)
            {
                Console.WriteLine("Preloader deleted");
            }
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
                    Console.WriteLine("Preloader deleted");
                    return true;
                }
            });
            e = cd.FindElementById("signin_btn");
            e.Click();
        }
        
        public static void Main(string[] args)
        {
            ChromeDriver cd = new ChromeDriver(@"chromedriver_win32");
            Login("stef4nio",")g!.&2wZ/d@sd#C",cd);
            
            if (!Directory.Exists(SOLUTION_FOLDER))
            {
                Directory.CreateDirectory(SOLUTION_FOLDER);
            }
            
            var submissionsTableWait = new WebDriverWait(cd, TimeSpan.FromSeconds(10));
            submissionsTableWait.Until(driver => driver.FindElement(By.CssSelector(".table")).Displayed);
            Console.WriteLine("Table loaded");

            var navButtonsContainerWait = new WebDriverWait(cd, TimeSpan.FromSeconds(5));
            navButtonsContainerWait.Until(driver => driver.FindElement(By.CssSelector(".pager")).Displayed);
            Console.WriteLine("Navigation buttons container detected");
            
            IWebElement nextPageButton =
                cd.FindElementByCssSelector(".next");
            Console.WriteLine("\"Next\" button css class attribute: "+nextPageButton.GetAttribute("class"));
            
            IWebElement prevPageButton =
                cd.FindElementByCssSelector(".previous");
            Console.WriteLine("\"Previous\" button css class attribute: "+prevPageButton.GetAttribute("class"));
            
            ReadOnlyCollection<IWebElement> acceptedSubmissionsButtons = cd.FindElementsByClassName("text-success");
            Console.WriteLine($"Accepted submissions found: {acceptedSubmissionsButtons.Count}");
            List<string> downloadedProblems = new List<string>();
            foreach (var button in acceptedSubmissionsButtons)
            {
                string submissionUrl = button.GetAttribute("href");
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
                if (!downloadedProblems.Contains(problemName))
                {
                    downloadedProblems.Add(problemName);
                    IWebElement codeBlock = cd.FindElementByClassName("ace_content");
                    Console.WriteLine("Code block captured. Downloading...");
                    string currFileFilename = SOLUTION_FOLDER + "/" + problemName + ".txt";
                    try
                    {
                        File.WriteAllText(currFileFilename, codeBlock.GetAttribute("innerHTML"));
                        Console.WriteLine("Download successful.");
                    }
                    catch (Exception exception)
                    {
                        Console.Write("Error while writing to file. Exception: ");
                        Console.WriteLine(exception);
                    }
                }
                else
                {
                    Console.WriteLine("Already downloaded solution for: " + problemName);
                }
                Console.WriteLine("Closing tab: " + submissionUrl);
                cd.Close();
                cd.SwitchTo().Window(cd.WindowHandles[0]);
                Console.WriteLine("Closed successfully");
            }
        }
    }
}