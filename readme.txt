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
It works by loading assembly in reflection-only context, and generating
MD5 hash for all types with their members, fields, properties, attributes
and other metadata.
Two hashes are calculated: one for public types/members and another one
for internal types/members if the assembly has InternalsVisibleTo attribute.
For F#, FSharpOptimizationData resource contents is also included in the
signature, since it contains information for inline functions.

- Before building the assembly, a dependency file is generated. It consists
of signatures of all references. If signature file is found alongside the
assembly, its contents is used with appropriate public/internal handling;
otherwise file timestamp is used (i.e. build dependencies are correct even
if signature files are not generated).

- CoreCompile targets are modified to depend on dependency file instead of
reference files (this is why Microsoft.*.Targets files need to be modified).
