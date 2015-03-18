using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace md2hxs
{
    /// <summary>
    /// A helper class that handles validation requests.
    /// </summary>
    internal class ValidationHelper
    {
        /// <summary>
        /// The constructor for the ValidationHelper class.
        /// </summary>
        internal ValidationHelper(){}

        /// <summary>
        /// Validates a file path or directory path.
        /// </summary>
        /// <param name="source">The file path or directory path to validate.</param>
        /// <param name="target">A ValidationTargets enumeration value that specifies which path is being validated.</param>
        /// <param name="defaultPath">Optional. The default path to use if the current path is unspecified or invalid.</param>
        /// <returns></returns>
        internal string Validate(string source, ValidationTargets target, string defaultPath = "")
        {
            source = source.Trim().TrimEnd("\\".ToCharArray());
            switch (target)
            {
                case ValidationTargets.MdRoot:
                    return MdRoot(source);
                case ValidationTargets.HxCompPath:
                case ValidationTargets.PandocPath:
                    return ExecInPath(source);
                case ValidationTargets.OutputFilePath:
                    return OutputFilePath(source, defaultPath);
                case ValidationTargets.MetadataPath:
                    return MetadataPath(source, defaultPath);
                case ValidationTargets.HxtxPath:
                    return HxtxPath(source, defaultPath);
                default:
                    return "";
            }
        }

        private string MdRoot(string currentPath)
        {
            if (!Directory.Exists(currentPath) || Directory.GetFiles(currentPath, "*.md").Length < 1)
            {
                if (File.Exists(Program.LogPath)) File.Delete(Program.LogPath);
                Utils.Die("The specified markdown root folder at " + currentPath + " could not be found, or contains no .md files.");
            }
            string msg = string.Format("Invalid markdown file tree at {0}. The top level of the directory tree must contain exactly 1 markdown file to act as the top level TOC node.", currentPath);
            while (!Program.AllowRootless && Directory.GetFiles(currentPath, "*.md").Length > 1)
            {
                if (Program.QuietMode) Utils.Die(msg);
                Console.WriteLine(msg);
                Console.WriteLine("Specify another directory, or enter \"allow\" to allow md2hxs to create a rootless TOC, or hit ENTER to try the current folder.");
                string s = Console.ReadLine();
                if (s.ToLower() == "allow") break;
                if (s == "") currentPath = Directory.GetCurrentDirectory();
            }
            return Path.GetFullPath(currentPath);
        }

        private string OutputFilePath(string currentPath, string defaultPath)
        {
            if (currentPath == "")
            {
                if (!Program.QuietMode)
                {
                    Console.WriteLine("Specify an output file to write to, or hit ENTER to write to the default path, which is {0}.", defaultPath);
                    currentPath = Console.ReadLine();
                }
                if (currentPath == "") currentPath = defaultPath;
            }
            while ((File.Exists(currentPath) && !Program.QuietMode) || !Directory.Exists(Directory.GetParent(currentPath).FullName))
            {
                string message = (File.Exists(currentPath))
                    ? "The file {0} already exists.\r\n\tEnter a new path,\r\n\tOR enter \"o\" to overwrite,\r\n\tOR enter \"d\" to try the default path, which is {1}."
                    : "The parent directory of {0} cannot be found.\r\n\tCreate the missing directory and press ENTER,\r\n\tOR enter a new path,\r\n\tOR enter \"d\" to try the default path, which is {1}.";
                if (breakOnCurrentPath(message, currentPath, defaultPath, out currentPath) && File.Exists(currentPath)) break;
            }
            return Path.GetFullPath(currentPath);
        }

        private string HxtxPath(string currentPath, string defaultPath)
        {
            if (currentPath == "")
            {
                var set = Directory.GetFiles(Program.MdRoot, "*.hxtx");
                if (set.Length > 0)
                {
                    currentPath = set[0];
                    Console.WriteLine("TOC file found at {0}.", set[0]);
                }
                else currentPath = defaultPath;
            }
            else while (!Program.QuietMode && !File.Exists(currentPath))
            {
                string message = "The specified hxtx file {0} was not found. Enter a path to a valid .hxtx file,\r\n\tOr enter \"d\" to use the default path, which is {1},\r\n\tOr press ENTER to create a new file at this location.";
                if (breakOnCurrentPath(message, currentPath, defaultPath, out currentPath)) break;
            }
            return Path.GetFullPath(currentPath);
        }

        private string ExecInPath(string currentPath)
        {
            bool success = true;
            if (currentPath.ToLower() == "hxcomp.exe" || currentPath.ToLower() == "pandoc.exe") // Default value, make sure it's in the path.
            {
                try
                {
                    var p = new System.Diagnostics.Process();
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.FileName = currentPath;
                    p.StartInfo.Arguments = "/? -h";
                    p.StartInfo.RedirectStandardOutput = true;
                    p.Start();
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    success = (p.ExitCode == 0);
                    p.Close();
                }
                catch (Exception) { success = false; }
                if (!success)
                {
                    if (Program.QuietMode) Utils.Die(string.Format("{0} Is not in your %PATH% environment variable, or is not installed correctly.", currentPath));
                    Console.WriteLine("{0} is not in your %PATH% environment variable, or is not installed correctly. Add it to your %PATH% and press ENTER, or specify a path to {0}.", currentPath);
                    string s = Console.ReadLine();
                    currentPath = ExecInPath((s == "") ? currentPath : s);
                }
            }
            else while (!File.Exists(currentPath))
                {
                    if (Program.QuietMode) Utils.Die(string.Format("{0} cannot be found, or is not installed correctly.", currentPath));
                    Console.WriteLine("File not found: {0}.\r\n\tEnter a valid path to {1}.", currentPath, Path.GetFileName(currentPath));
                    currentPath = Console.ReadLine();
                }
            return currentPath;
        }

        private string MetadataPath(string currentPath, string defaultPath)
        {
            string mdTop = Path.GetFileNameWithoutExtension(Directory.GetFiles(Program.MdRoot, "*.md")[0]);
            bool isValid = false;
            while (!isValid)
            {
                if (Directory.Exists(currentPath))
                    // If the top level .json file matches thetop level .md file, assume the rest of the files are valid for now.
                    if (File.Exists(currentPath + "\\" + mdTop + ".json") || Program.QuietMode) isValid = true;
                    else    // Directory is there but can't find the files...
                    {
                        string message = "Path \"{0}\" invalid. The metadata root directory must contain .json files with the same names and tree structure as the markdown files."
                            + "\r\n\t Enter a valid path to the metadata root for the markdown content\r\n\tOR enter \"d\" to use the default metadata path, which is {1}"
                            + "\r\n\tOR press ENTER to stay with the current path and check for internal metadata.";
                        if (breakOnCurrentPath(message, currentPath, defaultPath, out currentPath)) break;
                    }
                else
                {
                    string message = "Directory \"{0}\" not found.\r\n\tEnter a valid path to the metadata root for the markdown content\r\n\t"
                        + "OR enter \"d\" to use the default metadata path, which is {1}\r\n\tOR press ENTER to stay with the current path and check for internal metadata.";
                    if (breakOnCurrentPath(message, currentPath, defaultPath, out currentPath)) break;
                }

                // Make sure the metadata root is not inside the markdown directory.
                string parent = Path.GetDirectoryName(currentPath.ToLower());
                while (parent != Path.GetPathRoot(currentPath.ToLower()))
                    if (parent == Program.MdRoot.ToLower()) Utils.Die("The metadata root folder must be outside the markdown root folder.");
                    else parent = Directory.GetParent(parent).FullName;
            }
            return Path.GetFullPath(currentPath);
        }

        private bool breakOnCurrentPath(string message, string currentPath, string defaultPath, out string outPath)
        {
            if (Program.QuietMode) Utils.Die(string.Format("{0} cannot be found.", currentPath));
            Console.WriteLine(message, currentPath, defaultPath);
            string s = Console.ReadLine();
            switch (s)
            {
                case "":
                case "o":
                    outPath = currentPath;
                    return true;
                case "d":
                    outPath = defaultPath;
                    break;
                default:
                    outPath = s;
                    break;
            }
            return false;
        }

        /// <summary>
        /// Verifies that each directory in the markdown directory tree has a corresponding markdown file of the same name, and that there is a single root node. Unmatched directories will not be included in the .HxS.
        /// </summary>
        internal static void ValidateDirectoryStructure()
        {
            var sourceDir = new DirectoryInfo(Program.MdRoot);
            List<string> unmatchedDirs = new List<string>();
            foreach (DirectoryInfo subDir in sourceDir.GetDirectories("*", SearchOption.AllDirectories))
                if (!File.Exists(subDir.FullName + ".md") && subDir.Name != "resources"
                    && subDir.EnumerateFiles().Where(file => file.Extension == ".md").Count() > 0)
                    unmatchedDirs.Add(subDir.FullName + "\r\n");
            if (unmatchedDirs.Count > 0)
            {
                string s = "";
                foreach (var item in unmatchedDirs) s += item + ", ";
                string err = "Error: The following directories do not not match sibling filenames.\r\n" + s.Substring(0, s.Length - 2)
                    + "Each folder in the directory tree must have a file at the same level named [directory name].md.";
                Utils.tryWrite(Program.LogPath, err, true, true);
                Console.WriteLine("Error: The following directories do not not match sibling filenames and will not be included in the HxS.\r\nSee {0} for more details.", Program.LogPath);
            }
        }
    }

    /// <summary>
    /// Lists the paths that can be validated by the ValidationHelper.Validate(string, ValidationTargets, string) method.
    /// </summary>
    internal enum ValidationTargets
    {
        MdRoot = 0,
        OutputFilePath = 1,
        HxCompPath = 2,
        PandocPath = 3,
        MetadataPath = 4,
        HxtxPath = 5
    }
}
