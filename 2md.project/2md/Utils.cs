﻿using System;
using System.IO;
using System.Linq;

namespace _2md
{
    static class Utils
    {
        /// <summary>
        /// Attempts to write the specified content to a file. If the attempt fails three times, writes to log.
        /// </summary>
        /// <param name="filePath">The file path to write to.</param>
        /// <param name="content">The string content to write.</param>
        /// <param name="append">Optional. True to append; false to overwrite.</param>
        /// <param name="recursed">Optional. True if this is a recursive call to try to write to a log file; false otherwise.</param>
        /// <returns>True if successfull; false otherwise.</returns>
        internal static bool TryWrite(string filePath, string content, bool append = false, bool recursed = false)
        {
            if (!CreateParentDirIfMissing(filePath)) return false;
            string err = "";
            for (int attempts = 0; attempts < 3; attempts++)
            {
                try
                {
                    if (append) File.AppendAllText(filePath, content);
                    else File.WriteAllText(filePath, content);
                    return true;
                }
                catch (PathTooLongException)
                {
                    Utils.Die("Path " + filePath + " too long.\r\nTry building the files closer to the drive root, or run 2md with the -d switch to specify a larger filepath offset.");
                }
                catch (Exception e)
                {
                    err = e.Message;
                }
            }
            Console.WriteLine("Failed to write content to {0}\r\nWriting details to {1}", filePath, Program.LogPath);
            if (recursed || !TryWrite(Program.LogPath, string.Format("Failed to write content to {0}\r\n{1}", filePath, err), true, true))
                Console.WriteLine("Failed to write log.\r\n{0}", err);
            return false;
        }

        /// <summary>
        /// Opens the log file in notepad.
        /// </summary>
        internal static void ShowLog()
        {
            var p = new System.Diagnostics.Process();
            p.StartInfo.FileName = "Notepad.exe";
            p.StartInfo.Arguments = string.Format(Program.LogPath);
            p.Start();
            return;
        }

        /// <summary>
        /// Attempts to delete and then recreate the specified directory. Exits the program on the third failed attempt.
        /// </summary>
        /// <param name="dirPath">The full path to the director to delete.</param>
        internal static void ReplaceWithClean(string dirPath)
        {
            int attempts = 0;
            if (Directory.Exists(dirPath)) try { Directory.Delete(dirPath, true); }
                catch (Exception)
                {
                    if (attempts > 2) Utils.Die("Could not delete directory " + dirPath);
                    attempts++;
                }
            for (int i = 0; !Directory.Exists(dirPath); i++) Directory.CreateDirectory(dirPath);
        }

        /// <summary>
        /// Writes the specifed message to the log, alerts the user, displays the log (unless in quiet mode), and then exits the program with code 3.
        /// </summary>
        /// <param name="logMsg">The message to write to the log file.</param>
        internal static void Die(string logMsg = "")
        {
            Console.WriteLine("FATAL ERROR. See {0} for details.", Program.LogPath);
            TryWrite(Program.LogPath, logMsg, true);
            ShowLog();
            Environment.Exit(3);
        }

        /// <summary>
        /// Writes the specified message to console and to log. If logMsg is specified, writes it to log after msg.
        /// </summary>
        /// <param name="msg">A message to write to console and to log.</param>
        /// <param name="logMsg">Additional details to add to the log entry but not write to console.</param>
        internal static void Warn(string msg, string logMsg = "")
        {
            Console.WriteLine("Warning: " + msg);
            if (logMsg != "") msg += "\r\n" + logMsg;
            TryWrite(Program.LogPath, msg, true);
        }

        static bool CreateParentDirIfMissing(string filePath)
        {
            bool success = true;
            if (Directory.Exists(Path.GetPathRoot(filePath)))
            {
                while (!Directory.Exists(Path.GetDirectoryName(filePath)))
                {
                    var parent = new DirectoryInfo(Path.GetDirectoryName(filePath));
                    while (!Directory.Exists(parent.Parent.FullName))
                        parent = parent.Parent;
                    try { parent.Create(); }
                    catch (Exception) 
                    {
                        success = false;
                        break;
                    }
                }
            }
            else success = false;
            if (!success) Warn("Invalid filepath -- " + filePath + ".\r\nParent directory could not be created.");
            return success;
        }
    }

    /// <summary>
    /// A helper class that displays file conversion status in the console.
    /// </summary>
    internal class Status
    {
        int maxPoints = 20;
        int[] refPoints;
        int current = 0;

        /// <summary>
        /// The constructor for the Status object.
        /// </summary>
        /// <param name="fileCount">The total number of files that will be converted.</param>
        internal Status(int fileCount)
        {
            refPoints = new int[maxPoints];
            for (int i = 1; i < maxPoints; i++) refPoints[i] = (i * fileCount) / (maxPoints - 1);
        }

        /// <summary>
        /// Alerts the Status object that a file has been converted. If the file corresponds to a preset reference point, a "." character is written to the console.
        /// </summary>
        internal void Update()
        {
            current++;
            if (refPoints.Contains<int>(current)) Console.Write(".");
        }
    }
}
