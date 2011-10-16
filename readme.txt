AssemblySignature, by Arseny Kapoulkine (arseny.kapoulkine@gmail.com)

This is the distribution of AssemblySignature package. It is an MSBuild
extension to make .NET assemblies depend on the reference assembly signatures
(public classes/members) instead of assembly files. This reduces redundant
rebuilds - there is no need to rebuild dependent projects if only private
implementation changes.

Installation process:

- Copy AssemblySignature folder to your project so that you can redistribute
it along with project files

- Modify all csproj/fsproj files: find <Import> tag that references either
Microsoft.FSharp.Targets or Microsoft.CSharp.Targets, and change the import
path to point to the file inside AssemblySignature folder

How it works:

- After each build a signature file is generated alongside assembly file.
It works by loading assembly in reflection-only context, and printing all
public types with all attributes and members.
For F#, FSharpOptimizationData resource contents is also included in the
signature, since it contains information for inline functions.

- CoreCompile targets are modified to depend on signature file, if it is
found alongside the assembly file (this is why Microsoft.*.Targets files
need to be modified). If signature file is not found, assembly file is
used instead (i.e. build dependencies are correct even if signature files
are not generated)
