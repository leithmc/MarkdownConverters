Readme
2md.exe converts an .hxs file to a directory tree of markdown files and a parallel directory tree of .json files.
2md.exe is available from https://microsoft.visualstudio.com/DefaultCollection/CPUB%20Tools/_git/CPUB.tools#path=%2FMarkdownTools%2F2md&version=GBdevelop&_a=contents

***Syntax***
2md [inputfile] [options]
inputfile: The absolute or relative path to the HxS file to convert.
-n [collection name] Overrides the default name for the top level output file and directory. Applies only if the TOC has a single top level node.
-d [destination path length] Specifies the path length of the final destination if longer than that of the directory that contains the source file. Used to prevent PathTooLong exceptions when copying the markdown content to a new location.
-q Quiet mode. Skips all user prompts except when required parameters are missing or invalid."
-? Displays the help text and then exits.

***Requirements***
Relative links are supported, but only to files in the same HxS.
You must have Pandoc.exe and hxcomp.exe installed to run 2md. 
  Pandoc installer: https://github.com/jgm/pandoc/releases 
  HxComp: Install the Visual Studio 2008 sdk (v1) from http://www.microsoft.com/en-us/download/details.aspx?id=508 
  Note: The VS2008 sdk installer requires that you have VS2008 installed. Alternatively, you can unzip the installer and run the MSI directly, but then you have to apply some hacks to get all of the components to register correctly.

***Output***
If the input file is an .hxs, 2md creates a directory named “md” in the same folder is the input file. The /md folder has four children: 
	* The top level .md file for the collection
	* A folder with the same name that contains the rest of the directory tree
	* An .hxtx file that retains the TOC order for future conversions
	* A “resources” folder which contains any images, script files, and other non-html assets included in the HxS
Every directory in the tree will have the same name as the sibling file that it corresponds to. 
2md will also create a "Meta" folder, in the same location as "md", which contains .json files with the same names as the corresponding .md files. These contain all of the file-specific metadata. Global metadata is in a file named globals.json, which lives at the root of the \Meta folder.
If there is an existing md/ folder in the same directory as the input file, it will be deleted.

Log information is written to 2md.log in the same folder as the source file.

***Remarks***
When converting an HxS file, the resulting markdown files are arranged in a directory structure to match the TOC defined in the .HxT file, which is then expanded and saved as an .hxtx file. Because the .htm files in an HxS usually have long file names to ensure uniqueness in a large, flat TOC, the markdown files will have different names than the originals. These names are derived from the RLTitle attribute in the source file.
2md automatically scales the maximum path segment length to prevent PathTooLong exceptions in the target directory.
The original html file names are stored in the .hxtx file, so if the markdown is later converted back to an .hxs via MD2HXS.exe, those original filenames will be restored.

To get the cleanest file and directory names, we recommend that you do the following:
    * Store the markdown files in a location with a short directory path
    * Use the –n switch to assign a short name to the root level file and directory

Md2HxS preserves external links and updates links to files included in the same HxS. If you have multiple doc sets that are likely to link to each other with a high frequency, you should consider rolling them into the same HxS. (Or if there is sufficient need, we will implement support for HxC collections.)

***Automation***
If you are converting multiple files, *make sure to put each input file in a separate directory* because 2md deletes any existing /md/ folder in the same folder as the input file. If you have multiple source files in the same folder, I recommend running in a loop like in the following pseudocode:
Foreach (var file in currentDir.getFiles(“*.hxs”))
	var childDir = Directory.createDirectory(file.path.trimExtension);
	File.copy(file, childDir);
	Run(2md.exe, childDir.path + file.getFileName);
