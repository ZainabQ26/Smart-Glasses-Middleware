Task handlers (plugins)
=======================

Overview
--------
This project discovers `ITaskHandler` implementations at startup and registers them with DI.

- During development: add a new handler source file under `src/SmartGlassesMiddleware/Services/` and rebuild — the handler will be compiled into the app and discovered automatically.
- At runtime: place a compiled plugin DLL that contains one or more `ITaskHandler` implementations into the `tasks/` folder (located at the app content root). The app will attempt to load `*.dll` files from this folder at startup and register any concrete types that implement `ITaskHandler`.

Template file
-------------
`TemplateTaskHandler.cs` in the `tasks/` folder is a source template. It will not be loaded automatically unless you either:

1. Copy the file into the project (for example into `src/SmartGlassesMiddleware/Services/`) and rebuild, or
2. Compile it into a class library DLL and drop that DLL into the `tasks/` folder.

Will the template cause issues?
------------------------------
- Leaving the `.cs` template file in the `tasks/` folder will not be loaded at runtime and is harmless.
- If you compile a plugin DLL, make sure:
  - The plugin targets the same framework (net10.0) or a compatible target.
  - The plugin references any necessary assemblies (the app's public interfaces are in the same project/assembly).
  - Each handler's `Name` property is unique (case-insensitive). Duplicate names will cause a startup error because handlers are keyed by `Name`.
  - Avoid ambiguous handler types; handlers should be concrete, public classes implementing `ITaskHandler` with a parameterless or DI-compatible constructor.

Plugin example
--------------
Create a small class library project (example):

_dotnet new classlib -n MyTaskPlugin_

Add a reference to the app project (or duplicate the `ITaskHandler` interface in a shared library) and implement `ITaskHandler`.

Example `csproj` snippet that outputs the DLL into the app `tasks` folder on build (adjust path as needed):

    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <OutputPath>..\..\tasks\</OutputPath>
    </PropertyGroup>

Build and drop
--------------
- Build the plugin: `dotnet build` (the DLL will be written to `tasks/` if you set `OutputPath`).
- Restart the app; the startup will attempt to load the DLLs in `tasks/` and register `ITaskHandler` types.

Logging and errors
------------------
- If a plugin assembly fails to load, the startup logs a warning and continues.
- If two handlers use the same `Name`, `TaskDispatcher` construction will throw; keep names unique.

Support
-------
If you'd like, I can:
- Add a buildable sample plugin project that produces a DLL into `tasks/`.
- Add a short script to compile and copy plugin DLLs into the `tasks/` folder.
