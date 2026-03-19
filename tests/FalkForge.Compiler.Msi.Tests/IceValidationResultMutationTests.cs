using FalkForge.Compiler.Msi.Validation;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class IceValidationResultMutationTests
{
    private static IceMessage MakeMessage(IceMessageSeverity severity, string name = "ICE01") =>
        new()
        {
            IceName = name,
            Severity = severity,
            Description = $"{severity} message from {name}"
        };

    [Fact]
    public void FromMessages_ErrorsOnly_IsNotValid()
    {
        var result = IceValidationResult.FromMessages([MakeMessage(IceMessageSeverity.Error)]);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.Failures);
        Assert.Single(result.Messages);
    }

    [Fact]
    public void FromMessages_FailuresOnly_IsNotValid()
    {
        var result = IceValidationResult.FromMessages([MakeMessage(IceMessageSeverity.Failure)]);

        Assert.False(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Single(result.Failures);
        Assert.Single(result.Messages);
    }

    [Fact]
    public void FromMessages_WarningsOnly_IsValid()
    {
        var result = IceValidationResult.FromMessages([MakeMessage(IceMessageSeverity.Warning)]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Single(result.Warnings);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void FromMessages_InformationOnly_IsValid()
    {
        var result = IceValidationResult.FromMessages([MakeMessage(IceMessageSeverity.Information)]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.Failures);
        Assert.Single(result.Messages);
    }

    [Fact]
    public void FromMessages_ErrorsAndFailures_BothMakeItInvalid()
    {
        var result = IceValidationResult.FromMessages(
        [
            MakeMessage(IceMessageSeverity.Error, "ICE01"),
            MakeMessage(IceMessageSeverity.Failure, "ICE02"),
        ]);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Single(result.Failures);
        Assert.Equal(2, result.Messages.Count);
    }

    [Fact]
    public void FromMessages_MixOfAllSeverities_CorrectCounts()
    {
        var result = IceValidationResult.FromMessages(
        [
            MakeMessage(IceMessageSeverity.Error, "ICE01"),
            MakeMessage(IceMessageSeverity.Error, "ICE02"),
            MakeMessage(IceMessageSeverity.Warning, "ICE03"),
            MakeMessage(IceMessageSeverity.Warning, "ICE04"),
            MakeMessage(IceMessageSeverity.Warning, "ICE05"),
            MakeMessage(IceMessageSeverity.Failure, "ICE06"),
            MakeMessage(IceMessageSeverity.Information, "ICE07"),
            MakeMessage(IceMessageSeverity.Information, "ICE08"),
        ]);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(3, result.Warnings.Count);
        Assert.Single(result.Failures);
        Assert.Equal(8, result.Messages.Count);
    }

    [Fact]
    public void FromMessages_EmptyList_IsValid()
    {
        var result = IceValidationResult.FromMessages([]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Messages);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Success_IsValid_True()
    {
        var result = IceValidationResult.Success();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Success_AllCollections_Empty()
    {
        var result = IceValidationResult.Success();

        Assert.Empty(result.Messages);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void IsValid_OnlyWhenBothErrorsAndFailuresAreEmpty()
    {
        // Only warnings and info -> valid
        var valid = IceValidationResult.FromMessages(
        [
            MakeMessage(IceMessageSeverity.Warning),
            MakeMessage(IceMessageSeverity.Information),
        ]);
        Assert.True(valid.IsValid);

        // One error -> invalid
        var withError = IceValidationResult.FromMessages([MakeMessage(IceMessageSeverity.Error)]);
        Assert.False(withError.IsValid);
        Assert.Empty(withError.Failures);
        Assert.Single(withError.Errors);

        // One failure -> invalid
        var withFailure = IceValidationResult.FromMessages([MakeMessage(IceMessageSeverity.Failure)]);
        Assert.False(withFailure.IsValid);
        Assert.Single(withFailure.Failures);
        Assert.Empty(withFailure.Errors);
    }

    [Fact]
    public void IceReportMessage_OptionalProperties_DefaultNull()
    {
        var msg = new IceReportMessage
        {
            IceName = "ICE01",
            Severity = "Warning",
            Description = "desc"
        };

        Assert.Null(msg.Table);
        Assert.Null(msg.Column);
        Assert.Null(msg.PrimaryKeys);
    }

    [Fact]
    public void IceReport_Properties_RoundTrip()
    {
        var report = new IceReport
        {
            IsValid = false,
            Messages = [new IceReportMessage { IceName = "ICE01", Severity = "Error", Description = "err" }],
            Summary = new IceReportSummary { Errors = 1, Warnings = 0, Failures = 0, Information = 0 }
        };

        Assert.False(report.IsValid);
        Assert.Single(report.Messages);
        Assert.Equal(1, report.Summary.Errors);
    }
}
