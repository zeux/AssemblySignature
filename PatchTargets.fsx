open System.IO
open Microsoft.Win32

let patchTarget source target =
    let lines = File.ReadAllLines(source)

    // Make sure file is not patched
    if lines |> Array.exists (fun s -> s.Contains("@(ReferenceSignature)")) then
        failwithf "Error patching file %s: file is already patched" source

    // Insert AssemblySignature import
    let lines = lines |> Array.map (fun s ->
        if s = "</Project>" then "    <Import Project=\"AssemblySignature.targets\" />\r\n" + s
        else s)
    
    // Make sure patching succeeded
    if not (lines |> Array.exists (fun s -> s.Contains("AssemblySignature.targets"))) then
        failwithf "Error patching file %s: AssemblySignature patching failed" source

    // Change CoreCompile inputs to use ReferenceSignature instead of ReferencePath
    let lines = lines |> Array.mapi (fun i s ->
        if s.Contains("@(ReferencePath)") && lines.[i-3..i+1] |> Array.map (fun s -> s.Trim()) |> String.concat "" =
            "@(_CoreCompileResourceInputs);$(ApplicationIcon);$(AssemblyOriginatorKeyFile);@(ReferencePath);@(CompiledLicenseFile);" then
            s.Replace("@(ReferencePath)", "@(ReferenceSignature)")
        else s)

    // Make sure patching succeeded
    if not (lines |> Array.exists (fun s -> s.Contains("@(ReferenceSignature);"))) then
        failwithf "Error patching file %s: CoreCompile patching failed" source

    // Patch relative imports with absolute for specific cases
    let lines = lines |> Array.map (fun s ->
        if s.Contains("<UsingTask") && s.Contains("AssemblyFile=\"FSharp.Build.dll\"") then
            s.Replace("AssemblyFile=\"FSharp.Build.dll\"", "AssemblyFile=\"$(MSBuildExtensionsPath32)\\..\\Microsoft SDKs\\F#\\3.0\\Framework\\v4.0\\FSharp.Build.dll\"")
        else if s.Contains("<Import") && s.Contains("Project=\"Microsoft.Common.targets\"") then
            s.Replace("Project=\"Microsoft.Common.targets\"", "Project=\"$(MSBuildToolsPath)\\Microsoft.Common.targets\"")
        else
            s)

    // Write new file
    File.WriteAllLines(target, lines)

let patchTargetLocal source =
    patchTarget source (Path.GetFileName(source))

let HKLM = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)

// patch C# targets file
patchTargetLocal (HKLM.OpenSubKey(@"SOFTWARE\Microsoft\MSBuild\ToolsVersions\4.0").GetValue("MSBuildToolsPath") :?> string + @"\Microsoft.CSharp.Targets")

// patch F# targets file
patchTargetLocal (HKLM.OpenSubKey(@"SOFTWARE\Microsoft\FSharp\3.0\Runtime\v4.0").GetValue("") :?> string + @"\Microsoft.FSharp.Targets")
