namespace FalkForge.Ui.Abstractions;

public sealed class PageResult
{
    public static readonly PageResult Next = new(PageResultKind.Next);
    public static readonly PageResult Previous = new(PageResultKind.Previous);
    public static readonly PageResult Finish = new(PageResultKind.Finish);
    public static readonly PageResult Cancel = new(PageResultKind.Cancel);
    public static readonly PageResult Install = new(PageResultKind.Install);
    public static readonly PageResult Uninstall = new(PageResultKind.Uninstall);
    public static readonly PageResult Repair = new(PageResultKind.Repair);

    public static PageResult Stay(string? message = null) => new(PageResultKind.Stay, message);
    public static PageResult GoTo<TPage>() where TPage : class => new(PageResultKind.GoTo, targetType: typeof(TPage));

    public PageResultKind Kind { get; }
    public string? Message { get; }
    internal Type? TargetType { get; }

    private PageResult(PageResultKind kind, string? message = null, Type? targetType = null)
    {
        Kind = kind;
        Message = message;
        TargetType = targetType;
    }
}
