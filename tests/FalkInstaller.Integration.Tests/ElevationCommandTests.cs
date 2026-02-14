using FalkInstaller.Engine.Elevation;
using FalkInstaller.Engine.Elevation.Commands;
using FalkInstaller.Engine.Protocol.Messages;
using Xunit;

namespace FalkInstaller.Integration.Tests;

public sealed class ElevationCommandTests
{
    [Fact]
    public void FileWriteCommand_CreatesFileWithCorrectContent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-elev-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var targetPath = Path.Combine(tempDir, "output.txt");
            var content = "Hello, FileWriteCommand integration test!"u8.ToArray();

            var executor = new ElevatedCommandExecutor([new FileWriteCommand()]);

            var payload = BuildFileWritePayload(targetPath, content);
            var message = new ElevateExecuteMessage
            {
                SequenceId = 1,
                CommandName = "FileWrite",
                CommandPayload = payload
            };

            var result = executor.Execute(message);

            Assert.True(result.Success, $"Command failed: {result.ErrorMessage}");
            Assert.True(File.Exists(targetPath), "File was not created");

            var written = File.ReadAllBytes(targetPath);
            Assert.Equal(content, written);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void FileWriteCommand_CreatesNestedDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-elev-{Guid.NewGuid():N}");
        try
        {
            // Don't create the nested dir -- FileWriteCommand should do it
            var targetPath = Path.Combine(tempDir, "sub1", "sub2", "nested.bin");
            var content = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

            var executor = new ElevatedCommandExecutor([new FileWriteCommand()]);

            var payload = BuildFileWritePayload(targetPath, content);
            var message = new ElevateExecuteMessage
            {
                SequenceId = 2,
                CommandName = "FileWrite",
                CommandPayload = payload
            };

            var result = executor.Execute(message);

            Assert.True(result.Success, $"Command failed: {result.ErrorMessage}");
            Assert.True(File.Exists(targetPath));
            Assert.Equal(content, File.ReadAllBytes(targetPath));
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void FileWriteCommand_OverwritesExistingFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-elev-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var targetPath = Path.Combine(tempDir, "existing.dat");

            // Write initial content
            File.WriteAllBytes(targetPath, [0x01, 0x02, 0x03]);

            // Now overwrite via command
            var newContent = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };
            var executor = new ElevatedCommandExecutor([new FileWriteCommand()]);

            var payload = BuildFileWritePayload(targetPath, newContent);
            var message = new ElevateExecuteMessage
            {
                SequenceId = 3,
                CommandName = "FileWrite",
                CommandPayload = payload
            };

            var result = executor.Execute(message);

            Assert.True(result.Success);
            Assert.Equal(newContent, File.ReadAllBytes(targetPath));
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void FileWriteCommand_EmptyContent_CreatesEmptyFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-elev-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var targetPath = Path.Combine(tempDir, "empty.bin");

            var executor = new ElevatedCommandExecutor([new FileWriteCommand()]);

            var payload = BuildFileWritePayload(targetPath, []);
            var message = new ElevateExecuteMessage
            {
                SequenceId = 4,
                CommandName = "FileWrite",
                CommandPayload = payload
            };

            var result = executor.Execute(message);

            Assert.True(result.Success);
            Assert.True(File.Exists(targetPath));
            Assert.Empty(File.ReadAllBytes(targetPath));
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void UnknownCommand_ReturnsFailure()
    {
        var executor = new ElevatedCommandExecutor([new FileWriteCommand()]);

        var message = new ElevateExecuteMessage
        {
            SequenceId = 5,
            CommandName = "NonExistentCommand",
            CommandPayload = []
        };

        var result = executor.Execute(message);

        Assert.False(result.Success);
        Assert.Contains("Unknown command", result.ErrorMessage);
    }

    [Fact]
    public void MultipleCommands_AllRegistered_ExecuteCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-elev-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            // Register multiple commands
            var commands = new IElevatedCommand[]
            {
                new FileWriteCommand(),
                // Add other commands as they exist -- for now just FileWrite
            };

            var executor = new ElevatedCommandExecutor(commands);

            // Execute FileWrite
            var filePath = Path.Combine(tempDir, "multi-cmd-test.txt");
            var content = "Multi-command test"u8.ToArray();
            var payload = BuildFileWritePayload(filePath, content);

            var result = executor.Execute(new ElevateExecuteMessage
            {
                SequenceId = 10,
                CommandName = "FileWrite",
                CommandPayload = payload
            });

            Assert.True(result.Success);
            Assert.Equal(10u, result.SequenceId);
            Assert.True(File.Exists(filePath));
            Assert.Equal(content, File.ReadAllBytes(filePath));
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void FileWriteCommand_LargeContent_WrittenCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-elev-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var targetPath = Path.Combine(tempDir, "large.bin");

            // 64KB of random data
            var content = new byte[65536];
            Random.Shared.NextBytes(content);

            var executor = new ElevatedCommandExecutor([new FileWriteCommand()]);

            var payload = BuildFileWritePayload(targetPath, content);
            var message = new ElevateExecuteMessage
            {
                SequenceId = 6,
                CommandName = "FileWrite",
                CommandPayload = payload
            };

            var result = executor.Execute(message);

            Assert.True(result.Success);
            Assert.Equal(content, File.ReadAllBytes(targetPath));
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static byte[] BuildFileWritePayload(string targetPath, byte[] content)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(targetPath);
        writer.Write(content.Length);
        writer.Write(content);
        return stream.ToArray();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup
        }
    }
}
