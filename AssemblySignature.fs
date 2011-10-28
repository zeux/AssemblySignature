// fsc AssemblySignature.fs /optimize /tailcalls /r:Microsoft.Build.Framework /r:Microsoft.Build.Utilities.v4.0 /target:library
module AssemblySignature

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Runtime.CompilerServices
open System.Security.Cryptography

open Microsoft.Build.Framework
open Microsoft.Build.Utilities

let loadAssembly references (qname: string) =
    let name = qname.Split([|','|]).[0].ToLower()

    match references |> Array.tryFind (fun r -> Path.GetFileNameWithoutExtension(r).ToLower() = name) with
    | Some path -> Assembly.ReflectionOnlyLoadFrom(path)
    | None -> Assembly.ReflectionOnlyLoad(qname)

let md5str (hash: byte array) =
    hash |> Array.map (fun b -> b.ToString("x2")) |> String.concat ""

let md5 (s: string) =
    MD5.Create().ComputeHash(s.ToCharArray() |> Array.map byte) |> md5str

let outsort (fd: TextWriter) (prefix: string) data =
    data
    |> Seq.toArray
    |> Array.map (fun x -> x.ToString())
    |> Array.sort
    |> Array.iter (fun d ->
        fd.Write(prefix)
        fd.WriteLine(d))

let paramsig (m: MethodBase) =
    m.GetParameters()
    |> Array.map (fun p ->
        (p.GetCustomAttributesData() |> Seq.map (fun a -> string a + " ") |> String.concat "") + string p)
    |> String.concat ", "

let gensig (args: Type array) =
    if args.Length > 0 then
        let constr = seq {
            for t in args do
                if t.GenericParameterAttributes <> GenericParameterAttributes.None then
                    yield string t + ": " + string t.GenericParameterAttributes
                yield! t.GetGenericParameterConstraints() |> Seq.map (fun c -> string t + ": " + string c) }
        if Seq.isEmpty constr then ""
        else " where " + (constr |> String.concat " and ")
    else ""

let membersig (m: MemberInfo) =
    match m with
    | :? EventInfo as e -> "e " + string e
    | :? FieldInfo as f -> "f " + string f
    | :? ConstructorInfo as c -> "c " + c.Name + "(" + paramsig c + ")"
    | :? MethodInfo as m -> "m " + string m.ReturnType + " " + m.Name + "(" + paramsig m + ")" + gensig (m.GetGenericArguments())
    | :? PropertyInfo as p -> "p " + string p
    | :? Type as t -> "t " + string t
    | x -> failwithf "Unknown member %O" x

type Access =
| None = 0
| Public = 1
| Internal = 2

let rec getMemberAccess (m: MemberInfo) =
    match m with
    | :? EventInfo as e -> Access.Public
    | :? FieldInfo as f ->
        match f.Attributes &&& FieldAttributes.FieldAccessMask with
        | FieldAttributes.PrivateScope -> Access.None
        | FieldAttributes.Private -> Access.None
        | FieldAttributes.FamANDAssem -> Access.Internal
        | FieldAttributes.Assembly -> Access.Internal
        | FieldAttributes.Family -> Access.Public
        | FieldAttributes.FamORAssem -> Access.Public
        | FieldAttributes.Public -> Access.Public
        | a -> failwithf "Unknown field access %O" a
    | :? MethodBase as m ->
        match m.Attributes &&& MethodAttributes.MemberAccessMask with
        | MethodAttributes.PrivateScope -> Access.None
        | MethodAttributes.Private -> Access.None
        | MethodAttributes.FamANDAssem -> Access.Internal
        | MethodAttributes.Assembly -> Access.Internal
        | MethodAttributes.Family -> Access.Public
        | MethodAttributes.FamORAssem -> Access.Public
        | MethodAttributes.Public -> Access.Public
        | a -> failwithf "Unknown method access %O" a
    | :? PropertyInfo as p ->
        let gm = p.GetGetMethod(nonPublic = true)
        let sm = p.GetSetMethod(nonPublic = true)
        max (if gm <> null then getMemberAccess gm else Access.None) (if sm <> null then getMemberAccess sm else Access.None)
    | :? Type as t ->
        match t.Attributes &&& TypeAttributes.VisibilityMask with
        | TypeAttributes.NotPublic -> Access.None
        | TypeAttributes.Public -> Access.Public
        | TypeAttributes.NestedPublic -> Access.Public
        | TypeAttributes.NestedPrivate -> Access.None
        | TypeAttributes.NestedFamily -> Access.Public
        | TypeAttributes.NestedAssembly -> Access.Internal
        | TypeAttributes.NestedFamANDAssem -> Access.Internal
        | TypeAttributes.NestedFamORAssem -> Access.Public
        | a -> failwith "Unknown type access %O" a
    | x -> failwithf "Unknown member %O" x

let isSpecialType (t: Type) =
    // F# auto-generated types for closures
    t.FullName.Contains("@")

let outputTypeSignature (t: Type) (fdpub: TextWriter) (fdint: TextWriter) friend =
    let fd = if getMemberAccess t = Access.Internal then fdint else fdpub

    fd.WriteLine()
    t.GetCustomAttributesData() |> outsort fd ""
    if t.IsEnum then
        fd.WriteLine("enum " + string t + ": " + string (t.GetEnumUnderlyingType()) + " = " + (t.GetEnumNames() |> String.concat ", "))
    else
        fd.WriteLine((if t.IsValueType then "struct" else "class") + " " + string t + " <" + string t.Attributes + ">" + gensig (t.GetGenericArguments()))
        if t.BaseType <> null then fd.WriteLine("\tinherit " + string t.BaseType)
        t.GetInterfaces() |> outsort fd "\tinterface "

        let members = 
            t.GetMembers(BindingFlags.Instance ||| BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)
            |> Array.map (fun m -> getMemberAccess m, m)
            |> Array.filter (fun (a, m) -> a = Access.Public || (a = Access.Internal && friend))
            |> Array.map (fun (a, m) -> a, m, membersig m)
            |> Array.sortBy (fun (a, m, s) -> s)

        if members |> Array.exists (fun (a, m, s) -> a = Access.Internal) && fd = fdpub then
            fdint.WriteLine()
            fdint.WriteLine("type " + string t + " with")
            
        for a, m, s in members do
            let fdm = if a = Access.Internal then fdint else fd
            m.GetCustomAttributesData() |> outsort fdm "\t"
            fdm.WriteLine("\t" + s)

let outputAssemblySignature (asm: Assembly) =
    use fdasm = new StringWriter()
    use fdpub = new StringWriter()
    use fdint = new StringWriter()

    // fill common assembly info
    fdasm.WriteLine("assembly " + asm.FullName)
    asm.GetCustomAttributesData() |> outsort fdasm ""
    fdasm.WriteLine()

    asm.GetManifestResourceNames()
    |> Array.filter (fun n -> n.StartsWith("FSharpOptimizationData."))
    |> Array.iter (fun n -> fdasm.WriteLine("resource " + n + " " + (MD5.Create().ComputeHash(asm.GetManifestResourceStream(n)) |> md5str)))

    // determine if we have to query internal stuff (InternalsVisibleTo)
    let friend = asm.GetCustomAttributesData() |> Seq.exists (fun a -> a.Constructor.DeclaringType = typeof<InternalsVisibleToAttribute>)

    // get all appropriate types
    let types =
        if friend then
            asm.GetTypes() |> Array.filter (fun t -> getMemberAccess t <> Access.None)
        else
            asm.GetExportedTypes()

    // generate signature for all types
    for t in types |> Array.filter (isSpecialType >> not) |> Array.sortBy (fun t -> t.FullName) do
        outputTypeSignature t fdpub fdint friend

    string fdasm, string fdpub, string fdint

type ResolveHandlerScope(references) =
    let handler = ResolveEventHandler(fun _ args -> loadAssembly references args.Name)

    do AppDomain.CurrentDomain.add_ReflectionOnlyAssemblyResolve(handler)

    interface IDisposable with
        member this.Dispose() =
            AppDomain.CurrentDomain.remove_ReflectionOnlyAssemblyResolve(handler)

type AssemblySignatureGenerator() =
    inherit MarshalByRefObject()

    member this.Generate(input, output: string, references, log) =
        use scope = new ResolveHandlerScope(references)
        let asm = Assembly.ReflectionOnlyLoadFrom(input)

        let sasm, spub, sint = outputAssemblySignature asm

        File.WriteAllText(output, sprintf "%spublic %s\ninternal %s\n" sasm (md5 spub) (md5 sint))
        if log then
            File.WriteAllText(output + "logasm", sasm)
            File.WriteAllText(output + "logpub", spub)
            File.WriteAllText(output + "logint", sint)

type GenerateAssemblySignature() =
    inherit Task()

    let mutable input: ITaskItem = null
    let mutable output: ITaskItem = null
    let mutable references: ITaskItem[] = [||]
    let mutable log = false

    member this.Input with get () = input and set v = input <- v
    member this.Output with get () = output and set v = output <- v
    member this.References with get () = references and set v = references <- v
    member this.Log with get () = log and set v = log <- v

    override this.Execute() =
        let domain = AppDomain.CreateDomain("gasig")

        try
            let gen = domain.CreateInstanceFromAndUnwrap(typeof<AssemblySignatureGenerator>.Assembly.Location, typeof<AssemblySignatureGenerator>.FullName)
            (gen :?> AssemblySignatureGenerator).Generate(input.ItemSpec, output.ItemSpec, references |> Array.map (fun r -> r.ItemSpec), log)
            true
        finally
            AppDomain.Unload(domain)

type CopyAssemblySignature() =
    inherit Task()

    let mutable input: ITaskItem = null
    let mutable output: ITaskItem = null

    member this.Input with get () = input and set v = input <- v
    member this.Output with get () = output and set v = output <- v

    override this.Execute() =
        let same =
            try File.ReadAllText(output.ItemSpec) = File.ReadAllText(input.ItemSpec)
            with e -> false

        if not same then File.Copy(input.ItemSpec, output.ItemSpec, overwrite = true)

        true

type GetAssemblySignatureList() =
    inherit Task()

    let mutable inputs: ITaskItem[] = [||]
    let mutable signatures: ITaskItem[] = [||]

    member this.Inputs with get () = inputs and set v = inputs <- v
    [<Output>] member this.Signatures with get () = signatures and set v = signatures <- v

    override this.Execute() =
        signatures <- inputs |> Array.map (fun i -> 
            let sigf = i.ItemSpec + ".sig"
            if File.Exists(sigf) then TaskItem(sigf) :> ITaskItem else i)

        true
