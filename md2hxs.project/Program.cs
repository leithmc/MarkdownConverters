using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Text.RegularExpressions;

namespace md2hxs
{
    class Program
    {
        /// <summary>
        /// The path to the directory that contains the top level markdown file.
        /// </summary>
        internal static string MdRoot = "";
        /// <summary>
        /// The path to the directory that will host the html files converted from markdown.
        /// </summary>
        internal static string OutputDirectoryPath;
        /// <summary>
        /// The full path of the output .hxs file.
        /// </summary>
        internal static string OutputHxsFilePath;
        /// <summary>
        /// The full path to the log file.
        /// </summary>
        internal static string LogPath = Path.GetTempPath() + "md2hxs.log";
        /// <summary>
        /// True if all prompts should be suppressed.
        /// </summary>
        internal static bool QuietMode = false;
        internal static bool AllowRootless = false;
                
        /// <summary>
        /// A DocConverter object that converts markdown files to html.
        /// </summary>
        internal static DocConverter Doc;
        /// <summary>
        /// An HxConverter object that compiles html files into an .hxs.
        /// </summary>
        internal static HxConverter Hx;
        /// <summary>
        /// A MetaHelper object that processes MTPS metadata.
        /// </summary>
        internal static MetaHelper Meta;
        /// <summary>
        /// A TOCBuilder object that handles TOC creation.
        /// </summary>
        internal static TOCBuilder TOC;

        static void Main(string[] args)
        {
            Console.WriteLine("*** md2hxs.exe [source directory] [output file] (optional)-m [metadata root] ***");
            Console.WriteLine("Converts a directory of markdown files into an HxS.");

            processCommandLineArguments(args);

            if (!processAllFiles())
            {
                Console.WriteLine("Failed to compile content. See {0} for more information.", LogPath);
                Utils.ShowLog();
                return;
            }

            Console.WriteLine("Done. Contents of {0} compiled into {1}", MdRoot, OutputHxsFilePath);
        }

        private static void processCommandLineArguments(string[] args)
        {
            // Set temporary values for validation.
            string source = "", dest = "", metaDir = "", hxCompPath = "HxComp.exe", pandocPath = "pandoc.exe", globalJson = "", hxtPath = "", hxtxPath = "";
            bool overwriteHxs = false, strictMode = false, promptOnMissingMetadata = true;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-?":
                    case "/?":
                        usage();
                        return;
                    case "-m":
                    case "/m":
                        if (args.Length > i + 1) 
                        {
                            metaDir = args[i + 1];
                            i++;
                        }
                        break;
                    case "-hx":
                    case "/hx":
                        if (args.Length > i + 1 && args[i + 1].ToLower().EndsWith("hxcomp.exe"))                            
                        {
                            hxCompPath = args[i + 1];
                            i++;
                        }
                        break;
                    case "-pd":
                    case "/pd":
                        if (args.Length > i + 1 && args[i + 1].ToLower().EndsWith("pandoc.exe"))
                        {
                            pandocPath = args[i + 1];
                            i++;
                        }
                        break;
                    case "-global":
                    case "/global":
                        if (args.Length > i + 1) 
                        {
                            globalJson = args[i + 1];
                            i++;
                        }
                        break;
                    case "-hxtx":
                    case "/hxtx":
                        if (args.Length > i + 1)
                        {
                            hxtxPath = args[i + 1];
                            i++;
                        }
                        break;
                    case "-q":
                    case "/q":
                        QuietMode = true;
                        promptOnMissingMetadata = false;
                        break;
                    case "-strict":
                    case "/strict":
                        strictMode = true;
                        break;
                    case "-generate":
                    case "/generate":
                        promptOnMissingMetadata = false;
                        break;
                    case "-allowrootless":
                    case "/allowrootless":
                        AllowRootless = true;
                        break;
                    default:
                        if (source == "") source = args[i];
                        else if (dest == "") dest = args[i];
                        break;
                }
            }

            // Validate source directory.
            var VH = new ValidationHelper();
            MdRoot = VH.Validate(source, ValidationTargets.MdRoot);           

            // Validate output file path.
            string mdTop = Path.GetFileNameWithoutExtension(Directory.GetFiles(MdRoot, "*.md")[0]);
            string defaultOutFilePath = Directory.GetParent(MdRoot).FullName + "\\" + mdTop + ".hxs";
            string defaultMetaPath = defaultOutFilePath.Substring(0, defaultOutFilePath.LastIndexOf("\\")) + "\\meta";

            OutputHxsFilePath = VH.Validate(dest, ValidationTargets.OutputFilePath, defaultOutFilePath); // May not need these anymore.
            if (File.Exists(dest)) overwriteHxs = true;
            OutputDirectoryPath = Path.GetDirectoryName(OutputHxsFilePath) + "\\compile";
            LogPath = OutputHxsFilePath.Substring(0, OutputHxsFilePath.LastIndexOf(".")) + ".log";

            // Validate HxComp path.
            hxCompPath = VH.Validate(hxCompPath, ValidationTargets.HxCompPath);

            // Instantiate the Hxs converter.
            Hx = new HxConverter(OutputDirectoryPath, overwriteHxs, hxCompPath);

            // Validate the Pandoc path.
            pandocPath = VH.Validate(pandocPath, ValidationTargets.PandocPath);

            // Instantiate the doc converter.
            Doc = new DocConverter(pandocPath, strictMode);

            // Validate meta path.
            if (metaDir == "") metaDir = defaultMetaPath;
            if (!QuietMode) metaDir = VH.Validate(metaDir, ValidationTargets.MetadataPath, defaultMetaPath);

            // Instantiate the metadata helper.
            Meta = new MetaHelper(metaDir, promptOnMissingMetadata);
            if (globalJson != "") Meta.GlobalJson = globalJson;

            // Validate the hxtx path and instantiate TOC.
            string outputFileName = Path.GetFileNameWithoutExtension(OutputHxsFilePath);
            hxtPath = string.Format("{0}\\{1}.hxt", OutputDirectoryPath, outputFileName);
            string defaultHxtxPath = MdRoot + "\\" + outputFileName + ".hxtx";
            hxtxPath = VH.Validate(hxtxPath, ValidationTargets.HxtxPath, defaultHxtxPath);
            TOC = new TOCBuilder(hxtxPath);
        }

        private static bool processAllFiles()
        {
            Utils.ReplaceWithClean(OutputDirectoryPath);
            
            // Validate directory structure.
            ValidationHelper.ValidateDirectoryStructure();

            int fileCount = Directory.GetFiles(MdRoot, "*.md", SearchOption.AllDirectories).Length;
            Status status = new Status(fileCount);
            
            // Read and update the TOC.
            Console.WriteLine("Reading and updating TOC...");
            bool hasTOC = TOC.ReadHxtx();
            TOC.FillInMissingFileEntries(hasTOC);
            TOC.CheckForDuplicateGuids();
            
            // Convert the docs.
            Console.WriteLine("Converting {0} markdown files to html", fileCount);
            foreach (var item in TOC.HxtxDoc.Descendants("HelpTOCNode"))
            {
                string mdPath = item.Attribute("MDPath").Value;
                if (File.Exists(mdPath))
                {
                    string htmlFileName = item.Attribute("Url").Value;                    
                    string title = item.Attribute("Title").Value;
                    string assetID = item.Attribute("AssetID").Value;
                    XElement msHelp = Meta.GetXml(mdPath, assetID);
                    Doc.Convert(mdPath, htmlFileName, msHelp, title);
                }
                else Utils.tryWrite(LogPath, "*ERROR* File Not Found: " + mdPath + "\r\n", true);
                status.Update();
            }  
            
            // Copy images and other assets to the output directory.
            Console.WriteLine("\r\nCopying images and other assets...");
            var allImages = Directory.GetFiles(MdRoot, "*.*", SearchOption.AllDirectories).Where(imgPath => Path.GetExtension(imgPath)!= ".md");
            foreach (string imgName in allImages)
                File.Copy(imgName, OutputDirectoryPath + "\\" + Path.GetFileName(imgName), true);
            
            // Create the HxC and any other supporting files that might be needed and compile into Hxs.
            Console.WriteLine("Creating required index files and compiling content into {0}", OutputHxsFilePath);

            return Hx.Compile();
        }

        /// <summary>
        /// True if the current piece of expected metadata is the first piece to be found missing.
        /// </summary>
        internal static bool isFirstMissingMetadata = true;
      
        private static void usage()
        {
            Console.WriteLine("*** md2hxs.exe [source directory] [output file] [options] ***");
            Console.WriteLine("Converts a directory tree of markdown files to an HxS file. The top level of the directory tree should contain exactly 1 markdown file and one child directory, which must match the first portion of the file name. ");
            Console.WriteLine("[source directory] -- The directory containing the top level markdown file.");
            Console.WriteLine("[output file]      -- Optional. The path and file name of the output HxS file. By default, the .hxs will be written to the parent of [source directory] and named for the title of the first markdown file in the tree.");
            Console.WriteLine("-----OPTIONS-----");
            Console.WriteLine("-m [metadata root] -- Specifies the root of the directory tree that contains the metadata for the doc set. By default, this is a directory named \"\\meta\" in the same location as [source directory].");
            Console.WriteLine("-global [path]     -- Specifies the location of the global metadata for the docset. The default value is [metadata root]\\global.json.");
            Console.WriteLine("-q                 -- Quiet mode. Turns off all prompts except for emergencies.");
            Console.WriteLine("-strict            -- Tells the program to interpret markdown as \"markdown_strict\". The default flavor is \"markdown_github\".");
            Console.WriteLine("-generate          -- Tells the program to generate new metadata without prompting for any files that don't already have it.");
            Console.WriteLine("-pd [path]         -- Explicitly sets the path to Pandoc.exe. By default, the program assumes that Pandoc.exe is in your %PATH% environment variable.");
            Console.WriteLine("-hx [path]         -- Explicitly sets the path to HxComp.exe. By default, the program assumes that HxComp.exe is in your %PATH% environment variable.");
            Console.WriteLine("-hxtx [path]       -- Points to an .hxtx file that that contains TOC information, or to where it should be created if there is not one. The default value is [source directory]\\[outputfilename].hxtx."); 
            Console.WriteLine("-?                 -- Displays this help message and exits the program.");
        }
    }

/*    all else: other
c#,cs,C#,[C#]: CSharp
cpp,cpp#,c,c++,C++: ManagedCPlusPlus
html: html
j#,jsharp: JSharp
js,jscript#,jscript,JScript: JScript
vb,vb#,VB,[Visual Basic],Visual Basic,Visual&#160;Basic,[Visual&#160;Basic]: VisualBasic
vb-c#: visualbasicANDcsharp
vbs: VBScript
xaml,XAML: xaml
xml: xml*/
}

