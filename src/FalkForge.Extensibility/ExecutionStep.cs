namespace FalkForge.Extensibility;

/// <summary>
/// A declarative unit of install-time <b>execution</b> contributed by an extension —
/// the reusable seam that turns extension configuration into a custom action that
/// actually runs during installation, rather than inert table data that only lands in
/// the MSI.
///
/// <para>
/// The MSI compiler (via <c>ExecutionStepEmitter</c>) translates each step into:
/// </para>
/// <list type="number">
///   <item><description>
///     a <b>deferred, elevated</b> (no-impersonate, runs as <c>SYSTEM</c>) custom action
///     that executes <see cref="InstallCommand"/> in the <c>InstallExecuteSequence</c>
///     between <c>InstallInitialize</c> and <c>InstallFinalize</c>;
///   </description></item>
///   <item><description>
///     an optional paired <b>rollback</b> custom action running <see cref="RollbackCommand"/>,
///     scheduled immediately <i>before</i> the install action so Windows Installer runs it
///     automatically, in reverse, if a later step fails;
///   </description></item>
///   <item><description>
///     an optional <b>uninstall</b> custom action running <see cref="UninstallCommand"/>,
///     scheduled during removal;
///   </description></item>
///   <item><description>
///     the matching <c>InstallExecuteSequence</c> rows placing each action at a valid,
///     deterministic sequence number.
///   </description></item>
/// </list>
///
/// <para><b>Passing secrets (SQL credentials, service-account passwords, …).</b>
/// Never bake a secret literal into <see cref="InstallCommand"/> — deferred command
/// lines are visible on the process command line and in verbose MSI logs. Instead set
/// <see cref="CustomActionData"/> to a <i>formatted</i> expression such as
/// <c>"[DB_PASSWORD]"</c>. The compiler emits an immediate <c>SetProperty</c> (type 51)
/// custom action, named to match the deferred action, that copies that runtime property
/// into the deferred action's <c>CustomActionData</c>. The deferred action then reads the
/// value in-process via <c>session.CustomActionData</c> / the <c>[CustomActionData]</c>
/// token. The sensitive value is supplied at run time (e.g. via
/// <c>IInstallerEngine.SetSecureProperty</c>) and is <b>never stored in the MSI</b>; the
/// extension author is responsible for listing the source property in
/// <c>MsiHiddenProperties</c> so it is also scrubbed from logs, and — for maximum secrecy —
/// for using a DLL command target that reads <c>CustomActionData</c> in-process rather than
/// interpolating it onto a command line.</para>
/// </summary>
public sealed record ExecutionStep
{
    /// <summary>
    /// Stable, unique identifier for this step. Becomes the base of the generated custom
    /// action names (<c>Id</c>, <c>Id_rb</c>, <c>Id_un</c>), so it must be a valid MSI
    /// identifier (<c>^[A-Za-z_][A-Za-z0-9_]*$</c>) short enough to leave room for those
    /// suffixes. Two steps sharing an <see cref="Id"/> fail the build loudly (duplicate
    /// primary key) rather than silently overwriting one another.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The full command line executed on install (deferred, elevated). For a bundle/MSI
    /// installed per-machine this runs as <c>SYSTEM</c>. All untrusted values interpolated
    /// into this command MUST be escaped by the contributing extension (see
    /// <see cref="CommandLine"/>) — the deferred action runs with high privilege, so an
    /// unescaped value is a command-injection / privilege-escalation vector.
    /// </summary>
    public required string InstallCommand { get; init; }

    /// <summary>Command run to undo <see cref="InstallCommand"/> if installation later fails. Optional.</summary>
    public string? RollbackCommand { get; init; }

    /// <summary>Command run when the product is being removed. Optional.</summary>
    public string? UninstallCommand { get; init; }

    /// <summary>
    /// Optional <i>formatted</i> expression (e.g. <c>"[DB_PASSWORD]"</c>) whose run-time value
    /// is passed to the deferred install action as <c>CustomActionData</c> via a generated
    /// type-51 <c>SetProperty</c> action. This is the channel for secret / late-bound values;
    /// see the type remarks. When <see langword="null"/> (the default) no data channel is
    /// created and <see cref="InstallCommand"/> is fully self-contained.
    /// <para>
    /// <b>Contract.</b> This value is intentionally <i>not</i> escaped — it is meant to carry live
    /// MSI Formatted tokens (<c>[PROPERTY]</c>), which is the whole point of the channel. Therefore
    /// pass only a controlled property/token expression here, never raw untrusted text. The channel
    /// feeds the <b>install</b> action only; a rollback or uninstall command that needs its own
    /// late-bound value is not covered by this field.
    /// </para>
    /// </summary>
    public string? CustomActionData { get; init; }

    /// <summary>
    /// MSI condition gating the install (and rollback) action. Defaults to <c>NOT Installed</c>
    /// (first-time install) when <see langword="null"/>.
    /// </summary>
    public string? InstallCondition { get; init; }

    /// <summary>
    /// MSI condition gating the uninstall action. Defaults to <c>REMOVE~="ALL"</c> (full
    /// uninstall) when <see langword="null"/>.
    /// </summary>
    public string? UninstallCondition { get; init; }

    /// <summary>
    /// When <see langword="true"/> (the default) the generated custom actions are marked
    /// no-impersonate so they run in the elevated <c>SYSTEM</c> context required to modify
    /// machine state (firewall, services, IIS, …). Set <see langword="false"/> only for work
    /// that must run as the installing user.
    /// </summary>
    public bool Elevated { get; init; } = true;
}
