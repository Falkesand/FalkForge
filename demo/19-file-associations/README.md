# Demo 19: File Associations

Registers a file extension (`.demo`) with the Windows shell so double-clicking a `.demo` file opens the installed
application.

## What This Demonstrates

- Registering a custom file extension with `package.FileAssociation()`
- Setting a MIME content type and user-friendly description
- Assigning a file icon from the application executable
- Defining a shell verb (`open`) with command-line arguments

## Key API Calls

```csharp
// Register .demo file association
package.FileAssociation(".demo", fa =>
{
    fa.ContentType = "application/x-demo";
    fa.Description = "Demo Document";
    fa.IconFile = "payload/app.exe";
    fa.IconIndex = 0;

    // Shell verb — "%1" is replaced with the file path at runtime
    fa.Verb("open", "\"%1\"", verb =>
    {
        verb.Command = "Open";
    });
});
```

## How to Build

```bash
dotnet build demo/19-file-associations
```

## Notes

- The `Verb` method's second parameter is the command-line argument template. `"%1"` passes the double-clicked file path
  to the application.
- `IconIndex = 0` uses the first icon resource from the executable.
- On uninstall, MSI automatically removes the file association from the registry.
