##Markdown Converters
This repo contains two apps to convert between markdown and the MS Help 2.0 html format used by MSDN. By running both tools in regular builds, you can retain parity between markdown/Github content and Help2.0/MSDN content as both are updated.

###2md
2md converts an .hxs file to a directory tree of markdown files, arranged in TOC order, and a parallel directory tree of .JSON files that contain the MSDN metadata. It also creates an XMl file with an .hxtx extension to preserve the original table of contents.
File names are shortened to fit in a layered directory tree without hitting the 256 character path length limit, and relative links in the docs are rerouted to the new target file locations.

###Md2hxs
Md2hxs takes directory trees of markdown and JSON as output by 2md, converts it to HTML, and then compiles the HTML into an .hxs file. If there is new markdown, Md2hxs will generate the MSDN metadata for it.
Files are moved to a flat directory after modifying any duplicate file names. Relative links are then redirected to the new targets.

###Source Code
The source code for 2md is in the 2md.project folder. The source code for Md2hxs is in the Md2hxs.project folder. These are included mainly as portfolio samples.
From a design and code quality standpoint, Md2hxs is a better sample of my work. Writing 2md was my first exposure to markdown, and didn't have as much time available for fit and finish.
