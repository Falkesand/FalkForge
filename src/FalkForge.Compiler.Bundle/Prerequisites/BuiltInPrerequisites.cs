using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Prerequisites;

/// <summary>
/// Pre-configured package groups for common prerequisites.
/// Each group defines detection (via registry search) and silent install arguments.
/// The actual installer files are NOT embedded -- users must provide the source files
/// or use RemotePayload for download.
/// </summary>
public static class BuiltInPrerequisites
{
    /// <summary>
    /// .NET Framework 4.7.2 offline installer.
    /// Detection: Registry HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full, Release >= 461808.
    /// Silent args: /q /norestart.
    /// </summary>
    public static PackageGroupModel NetFx472()
    {
        return new PackageGroupBuilder()
            .Id("NetFx472")
            .ExePackage("NDP472-KB4054530-x86-x64-AllOS-ENU.exe", p => p
                .Id("NetFx472")
                .DisplayName("Microsoft .NET Framework 4.7.2")
                .Vital(true)
                .Prerequisite()
                .DetectionMode(DetectionMode.SearchOnly)
                .Property("InstallArguments", "/q /norestart")
                .SearchCondition(sc => sc.RegistryValue(
                    RegistryRoot.LocalMachine,
                    @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full",
                    "Release",
                    ">=",
                    "461808")))
            .Build();
    }

    /// <summary>
    /// Visual C++ 2015-2022 Redistributable x64.
    /// Detection: Registry HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64, Installed = 1.
    /// Silent args: /install /quiet /norestart.
    /// </summary>
    public static PackageGroupModel VCRedist14x64()
    {
        return new PackageGroupBuilder()
            .Id("VCRedist14x64")
            .ExePackage("vc_redist.x64.exe", p => p
                .Id("VCRedist14x64")
                .DisplayName("Microsoft Visual C++ 2015-2022 Redistributable (x64)")
                .Vital(true)
                .Prerequisite()
                .DetectionMode(DetectionMode.SearchOnly)
                .Property("InstallArguments", "/install /quiet /norestart")
                .SearchCondition(sc => sc.RegistryValue(
                    RegistryRoot.LocalMachine,
                    @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64",
                    "Installed",
                    "=",
                    "1")))
            .Build();
    }

    /// <summary>
    /// Microsoft ODBC Driver 17 for SQL Server.
    /// Detection: Registry HKLM\SOFTWARE\ODBC\ODBCINST.INI\ODBC Driver 17 for SQL Server, Driver exists.
    /// </summary>
    public static PackageGroupModel OdbcDriver17()
    {
        return new PackageGroupBuilder()
            .Id("OdbcDriver17")
            .MsiPackage("msodbcsql_17.msi", p => p
                .Id("OdbcDriver17")
                .DisplayName("Microsoft ODBC Driver 17 for SQL Server")
                .Vital(true)
                .Prerequisite()
                .DetectionMode(DetectionMode.SearchOnly)
                .Property("InstallArguments", "/quiet /norestart IACCEPTMSODBCSQLLICENSETERMS=YES")
                .SearchCondition(sc => sc.RegistryExists(
                    RegistryRoot.LocalMachine,
                    @"SOFTWARE\ODBC\ODBCINST.INI\ODBC Driver 17 for SQL Server",
                    "Driver")))
            .Build();
    }

    /// <summary>
    /// SQL Server 2017 Express.
    /// Detection: Registry HKLM\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL, SQLEXPRESS exists.
    /// Silent args: /IACCEPTSQLSERVERLICENSETERMS /ACTION=Install /FEATURES=SQLEngine /INSTANCENAME=SQLEXPRESS /QUIET.
    /// </summary>
    public static PackageGroupModel SqlExpress2017()
    {
        return new PackageGroupBuilder()
            .Id("SqlExpress2017")
            .ExePackage("SQLEXPR_x64_ENU.exe", p => p
                .Id("SqlExpress2017")
                .DisplayName("Microsoft SQL Server 2017 Express")
                .Vital(true)
                .Prerequisite()
                .DetectionMode(DetectionMode.SearchOnly)
                .Property("InstallArguments",
                    "/IACCEPTSQLSERVERLICENSETERMS /ACTION=Install /FEATURES=SQLEngine /INSTANCENAME=SQLEXPRESS /QUIET")
                .SearchCondition(sc => sc.RegistryExists(
                    RegistryRoot.LocalMachine,
                    @"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL",
                    "SQLEXPRESS")))
            .Build();
    }
}
