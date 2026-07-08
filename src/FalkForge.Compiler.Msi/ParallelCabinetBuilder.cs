namespace FalkForge.Compiler.Msi;

public sealed class ParallelCabinetBuilder
{
    private readonly Func<CabinetWorkItem, CancellationToken, Result<CabinetBuildResult>> _buildFunc;

    public ParallelCabinetBuilder(Func<CabinetWorkItem, CancellationToken, Result<CabinetBuildResult>> buildFunc)
    {
        _buildFunc = buildFunc ?? throw new ArgumentNullException(nameof(buildFunc));
    }

    public async Task<Result<IReadOnlyList<CabinetBuildResult>>> BuildAsync(
        IReadOnlyList<CabinetWorkItem> workItems,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        if (workItems.Count == 0)
            return Result<IReadOnlyList<CabinetBuildResult>>.Success(Array.Empty<CabinetBuildResult>());

        if (workItems.Count == 1)
            return BuildSingle(workItems[0], cancellationToken);

        return await BuildParallelAsync(workItems, maxDegreeOfParallelism, cancellationToken)
            .ConfigureAwait(false);
    }

    private Result<IReadOnlyList<CabinetBuildResult>> BuildSingle(
        CabinetWorkItem workItem,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _buildFunc(workItem, cancellationToken);
            if (result.IsFailure)
                return Result<IReadOnlyList<CabinetBuildResult>>.Failure(result.Error);

            return Result<IReadOnlyList<CabinetBuildResult>>.Success(new[] { result.Value });
        }
        catch (OperationCanceledException)
        {
            return Result<IReadOnlyList<CabinetBuildResult>>.Failure(
                ErrorKind.CompilationError, "Cabinet build was cancelled.");
        }
    }

    private async Task<Result<IReadOnlyList<CabinetBuildResult>>> BuildParallelAsync(
        IReadOnlyList<CabinetWorkItem> workItems,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        var results = new CabinetBuildResult[workItems.Count];
        Error? firstError = null;
        var errorLock = new object();

        try
        {
            // Iterate indices directly to preserve order without allocating an
            // indexed projection list; the loop index maps 1:1 to workItems.
            await Parallel.ForAsync(
                0,
                workItems.Count,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                },
                (index, ct) =>
                {
                    // Short-circuit if a previous item already failed
                    lock (errorLock)
                    {
                        if (firstError is not null)
                            return ValueTask.CompletedTask;
                    }

                    var buildResult = _buildFunc(workItems[index], ct);
                    if (buildResult.IsFailure)
                    {
                        lock (errorLock)
                        {
                            firstError ??= buildResult.Error;
                        }

                        return ValueTask.CompletedTask;
                    }

                    results[index] = buildResult.Value;
                    return ValueTask.CompletedTask;
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Result<IReadOnlyList<CabinetBuildResult>>.Failure(
                ErrorKind.CompilationError, "Cabinet build was cancelled.");
        }

        if (firstError is not null)
            return Result<IReadOnlyList<CabinetBuildResult>>.Failure(firstError.Value);

        return Result<IReadOnlyList<CabinetBuildResult>>.Success(results);
    }
}