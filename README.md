# What this is
A little program using Roslyn to parse C# source code and strip of most code implementations so it's safe to use for (de)serialisation. This can be used in combinaison of a .dll decompiler to generate source code.

This is not perfect: You can expect a few errors due to very difficult edge cases (property getters/setters part of an interface). It's easy to fix with Visual studio with a few clicks.

# Usage

Download from Releases section or compile using Visual Studio. Use the command line to execute:
`./MonoStubGenerator.exe input_directory output_directory`

It will recursively find all .cs files, process them and write them in the corresponding folders in the output (folder hierarchy is preserved).
