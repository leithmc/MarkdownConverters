using System;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;
using System.Xml.Linq;

namespace md2hxs
{
    /// <summary>
    /// A helper class that uses Pandoc.exe to convert markdown files to html files.
    /// </summary>
    public class DocConverter
    {
        string PandocPath;
        string Flavor;
        /// <summary>
        /// The constructor for the DocConverter class.
        /// </summary>
        /// <param name="pandocPath">The path to Pandoc.exe, or just the file name if pandoc.exe is the the users path environment variable.</param>
        /// <param name="strict">True to convert from markdown_strict; false to convert from markdown_github.</param>
        internal DocConverter(string pandocPath, bool strict)
        {
            PandocPath = pandocPath;
            Flavor = (strict) ? "markdown_strict" : "markdown_github";
        }

        /// <summary>
        /// Reads a markdown file and writes the content to an html file.
        /// </summary>
        /// <param name="mdFilePath">The  full path to the source markdown file.</param>
        /// <param name="outputFileName">The name of the destination html file.</param>
        /// <param name="msHelp">The MSTP metadata associated with the output html file.</param>
        /// <param name="title">The title of the output html file.</param>
        /// <returns>True if the output html file was successfully written; false otherwise.</returns>
        internal bool Convert(string mdFilePath, string outputFileName, XElement msHelp, string title)
        {
            string tmpFilePath = Content.preClean(mdFilePath);
            string body = ToHtml(tmpFilePath);
            File.Delete(tmpFilePath);
            string final = Content.postClean(msHelp, title, body, outputFileName);
            return Utils.tryWrite(string.Format("{0}\\{1}", Program.OutputDirectoryPath, outputFileName), final);
        }

        string ToHtml(string fileName)
        {
            var pd = new Process();
            pd.StartInfo.FileName = PandocPath;
            pd.StartInfo.Arguments = string.Format("-f {0} -t html {1}", Flavor, fileName);
            pd.StartInfo.UseShellExecute = false;
            pd.StartInfo.RedirectStandardOutput = true;
            pd.StartInfo.RedirectStandardError = true;
            pd.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            try
            {
                pd.Start();
                string content = pd.StandardOutput.ReadToEnd();
                string err = pd.StandardError.ReadToEnd();
                pd.WaitForExit();
                pd.Close();
                if (err != "" && !Program.QuietMode)
                {
                    Console.WriteLine("Pandoc encountered one or more errors:\r\n{0}\r\nPress ENTER to continue or CTRL+C to quit.", err);
                    Console.ReadLine();
                }
                return content;
            }
            catch (Exception e)
            {
                Utils.Die(string.Format("Could not run Pandoc on " + fileName + ". Is pandoc.exe installed", e.InnerException));
                return "";
            }
        }
    }

    /// <summary>
    /// A helper class that compiles html files into an .hxs file by using hxcomp.exe.
    /// </summary>
    internal class HxConverter
    {
        string DirPath;
        string HxCompPath;
        bool Overwrite;
        /// <summary>
        /// The constructor for the HxConverter class.
        /// </summary>
        /// <param name="dirPath">The path to the directory that contains the html files.</param>
        /// <param name="overwriteHxs">True if any existing .hxs file at the destination should be overwritten; false otherwise.</param>
        /// <param name="hxCompPath">The path to hxcomp.exe, or the filename if it is in the users PATH environment variable.</param>
        internal HxConverter(string dirPath, bool overwriteHxs, string hxCompPath = "")
        {
            this.DirPath = dirPath;
            this.Overwrite = overwriteHxs;
            this.HxCompPath = hxCompPath;
        }


        /// <summary>
        /// Compiles the html files at DirPath into an .hxs file at Program.OutputHxsFilePath by using HxComp.exe.
        /// </summary>
        /// <returns>True if hxcomp ran without generating any FATAL ERROR messages; false otherwise.</returns>
        internal bool Compile()
        {
            // Set paths.
            string fileName = Path.GetFileNameWithoutExtension(Program.OutputHxsFilePath);
            string projectFilePath = DirPath + "\\" + fileName + ".hxc";
            
            createSupportingFiles(fileName);

            // If there is an old Hxs, delete it.
            if (Overwrite) try { File.Delete(Program.OutputHxsFilePath); }
                catch (Exception) { }   // This is in case we are in overwrite mode but have nothing to delete.

            // Delete the copy of the .hxtx from Dirpath to prevent errors in the MSDN transforms.
            string hxtxPath = DirPath + "\\" + Path.GetFileName(Program.TOC.HxtxPath);
            if (File.Exists(hxtxPath)) File.Delete(hxtxPath);
            
            // Run HxComp.
            var hxc = new Process();
            hxc.StartInfo.FileName = HxCompPath;
            hxc.StartInfo.Arguments = string.Format("-p {0} -r {1} -l {2} -o{3}", projectFilePath, DirPath, Program.LogPath, Program.OutputHxsFilePath);

            hxc.StartInfo.UseShellExecute = false;
            hxc.StartInfo.RedirectStandardOutput = true;
            hxc.StartInfo.RedirectStandardError = true;
            string err = "";
            try
            {
                hxc.Start();
                err = hxc.StandardError.ReadToEnd();
                hxc.WaitForExit();
            }
            catch (Exception e)
            {
                Utils.Die(string.Format("Could not run HxComp on " + DirPath + ". Check your permissions and try again.\r\n" + err, e.InnerException));
            }

            // Move log data to log file. (HxComp overwrites instead of appending.)
            string s = File.ReadAllText(Program.LogPath);
            File.AppendAllText(Program.LogPath, s);
            File.Delete(Program.LogPath);

            return !Regex.IsMatch(err, "Fatal Error", RegexOptions.Singleline);
        }

        private bool createSupportingFiles(string fileName)
        {
            // Create HxC file.
            string hxc = string.Format("<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                    + "<!DOCTYPE HelpCollection SYSTEM \"ms-help://hx/resources/HelpCollection.DTD\">"
                    + "<HelpCollection DTDVersion=\"1.0\" LangId=\"1033\" Title=\"{0}\" Copyright=\"Microsoft Corporation.  All rights reserved.\">"
                    + "	<CompilerOptions CreateFullTextIndex=\"Yes\" CompileResult=\"Hxs\">"
                    + "  <IncludeFile File=\"{0}.HxF\"/>"
                    + " </CompilerOptions>"
                    + "	<TOCDef File=\"{0}.HxT\"/>"
                    + "	<KeywordIndexDef File=\"{0}KIndex.HxK\"/>"
                    + "	<KeywordIndexDef File=\"{0}AIndex.HxK\"/>"
                    + "	<KeywordIndexDef File=\"{0}FIndex.HxK\"/>"
                    + " <ItemMoniker Name=\"!DefaultToc\" ProgId=\"HxDs.HxHierarchy\" InitData=\"\"/>"
                    + "</HelpCollection>", fileName);

            string filePath = DirPath + "\\" + fileName;
            for (int i = 0; i < 3 && File.Exists(filePath); i++) File.Delete(filePath);
            if (!Utils.tryWrite(filePath + ".hxc", hxc)) return false;

            // Create HxF file.
            string hxf = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<!DOCTYPE HelpFileList SYSTEM \"ms-help://hx/resources/HelpFileList.DTD\">"
                + "<HelpFileList DTDVersion=\"1.0\">\r\n<File Url=\"*.*\" />\r\n</HelpFileList>";
            if (!Utils.tryWrite(filePath + ".hxf", hxf)) return false;

            // Create index files. {0} is index type, {1} is yes or no for visibility.
            string indexFile = "<?xml version=\"1.0\"?>"
                    + "<!DOCTYPE HelpIndex SYSTEM \"ms-help://hx/resources/HelpIndex.DTD\">"
                    + "<HelpIndex DTDVersion=\"1.0\" Name=\"{0}\" Title=\"{0}-Keyword Index\" Visible=\"{1}\" LangId=\"1033\" />";
            if (!Utils.tryWrite(filePath + "AIndex.hxk", string.Format(indexFile, "A", "No"))) return false;
            if (!Utils.tryWrite(filePath + "FIndex.hxk", string.Format(indexFile, "F", "No"))) return false;
            if (!Utils.tryWrite(filePath + "KIndex.hxk", string.Format(indexFile, "K", "Yes"))) return false;
            return true;
        }
    }
}
