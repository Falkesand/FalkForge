using FalkForge.Engine.Elevation.Commands;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests.Commands;

/// <summary>
/// Unit tests for <see cref="ElevatedPathPolicy"/> — the shared allowed-root containment
/// check used by elevated filesystem commands. These encode the security intent that a
/// directory that merely shares a textual prefix with an allowed root
/// (e.g. "C:\Program Files Evil" vs "C:\Program Files") must NOT be treated as contained,
/// and that the service-image root set intentionally excludes the user profile.
/// </summary>
public sealed class ElevatedPathPolicyTests
{
    [Fact]
    public void IsUnderAllowedRoot_SiblingPrefixDirectory_IsRejected()
    {
        var roots = new[] { @"C:\Program Files" };

        // "C:\Program Files Evil" shares the textual prefix "C:\Program Files" but is a
        // different directory. Without a separator boundary a naive StartsWith would wrongly
        // accept it — this is the sibling-prefix hole (FIX 3).
        Assert.False(ElevatedPathPolicy.IsUnderAllowedRoot(@"C:\Program Files Evil\payload.dll", roots));
    }

    [Fact]
    public void IsUnderAllowedRoot_GenuineChild_IsAccepted()
    {
        var roots = new[] { @"C:\Program Files" };

        Assert.True(ElevatedPathPolicy.IsUnderAllowedRoot(@"C:\Program Files\MyApp\app.exe", roots));
    }

    [Fact]
    public void IsUnderAllowedRoot_ExactRoot_IsAccepted()
    {
        var roots = new[] { @"C:\Program Files" };

        Assert.True(ElevatedPathPolicy.IsUnderAllowedRoot(@"C:\Program Files", roots));
    }

    [Fact]
    public void IsUnderAllowedRoot_MatchIsCaseInsensitive()
    {
        var roots = new[] { @"C:\Program Files" };

        Assert.True(ElevatedPathPolicy.IsUnderAllowedRoot(@"c:\program files\app\x.dll", roots));
    }

    [Fact]
    public void ServiceBinaryRoots_ExcludeUserProfile()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidate = Path.Combine(userProfile, "EvilApp", "svc.exe");

        // A SYSTEM service must never be authored under a user-writable profile directory —
        // the user could later swap the image and gain SYSTEM (FIX 2).
        Assert.False(ElevatedPathPolicy.IsUnderAllowedRoot(candidate, ElevatedPathPolicy.ServiceBinaryRoots()));
    }

    [Fact]
    public void FileWriteRoots_IncludeUserProfile()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidate = Path.Combine(userProfile, "AppData", "Local", "MyApp", "file.txt");

        // FileWrite intentionally keeps the user profile in its allowed set (unchanged by FIX 2).
        Assert.True(ElevatedPathPolicy.IsUnderAllowedRoot(candidate, ElevatedPathPolicy.FileWriteRoots()));
    }
}
