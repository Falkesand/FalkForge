using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// COM registration: in-proc server, local server, ProgId, threading model.
return Installer.Build(args, package =>
{
    package.Name = "COM Registration Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/MyComServer.dll")
        .Add("payload/MyLocalServer.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "ComDemo"));

    // --- In-process COM server (InprocServer32) ---
    // Registers a DLL-based COM class with Apartment threading model.
    package.ComClass(com => com
        .ClassId(new Guid("B5F8350B-0548-48B1-A6EE-88BD00B4A5E7"))
        .InprocServer32()
        .ThreadingModel(ComThreadingModel.Apartment)
        .ProgId("Demo.DataProcessor")
        .Description("FalkForge Demo Data Processor COM Class"));

    // --- In-process COM server with Both threading model ---
    // Free-threaded component suitable for multi-threaded callers.
    package.ComClass(com => com
        .ClassId(new Guid("A1C2D3E4-F5A6-B7C8-D9E0-1A2B3C4D5E6F"))
        .InprocServer32()
        .ThreadingModel(ComThreadingModel.Both)
        .ProgId("Demo.ThreadSafeHelper")
        .Description("Thread-safe COM helper class"));

    // --- Local (out-of-process) COM server (LocalServer32) ---
    // Registers an EXE-based COM class.
    package.ComClass(com => com
        .ClassId(new Guid("C7D8E9F0-1A2B-3C4D-5E6F-A7B8C9D0E1F2"))
        .LocalServer32()
        .ProgId("Demo.ServiceHost")
        .Description("Out-of-process COM service host"));
}, new MsiCompiler());
