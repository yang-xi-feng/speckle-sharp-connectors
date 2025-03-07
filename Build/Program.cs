using System.IO.Compression;
using Build;
using GlobExpressions;
using static Bullseye.Targets;
using static SimpleExec.Command;

const string CLEAN = "clean";
const string RESTORE = "restore";
const string BUILD = "build";
const string TEST = "test";
const string TEST_ONLY = "test-only";
const string FORMAT = "format";
const string ZIP = "zip";
const string VERSION = "version";
const string RESTORE_TOOLS = "restore-tools";
const string BUILD_SERVER_VERSION = "build-server-version";
const string CLEAN_LOCKS = "clean-locks";
const string CHECK_SOLUTIONS = "check-solutions";
const string DEEP_CLEAN = "deep-clean";

//need to pass arguments
/*var arguments = new List<string>();
if (args.Length > 1)
{
  arguments = args.ToList();
  args = new[] { arguments.First() };
  //arguments = arguments.Skip(1).ToList();
}*/

Target(
  CLEAN_LOCKS,
  () =>
  {
    foreach (var f in Glob.Files(".", "**/*.lock.json"))
    {
      Console.WriteLine("Found and will delete: " + f);
      File.Delete(f);
    }
    Console.WriteLine("Running restore now.");
    Run("dotnet", "restore .\\Speckle.Connectors.sln --no-cache");
  }
);

Target(
  DEEP_CLEAN,
  () =>
  {
    foreach (var f in Glob.Directories(".", "**/bin"))
    {
      if (f.StartsWith("Build"))
      {
        continue;
      }
      Console.WriteLine("Found and will delete: " + f);
      Directory.Delete(f, true);
    }
    foreach (var f in Glob.Directories(".", "**/obj"))
    {
      if (f.StartsWith("Build"))
      {
        continue;
      }
      Console.WriteLine("Found and will delete: " + f);
      Directory.Delete(f, true);
    }
    Console.WriteLine("Running restore now.");
    Run("dotnet", "restore .\\Speckle.Connectors.sln --no-cache");
  }
);

Target(
  CLEAN,
  ForEach("**/output"),
  dir =>
  {
    IEnumerable<string> GetDirectories(string d)
    {
      return Glob.Directories(".", d);
    }

    void RemoveDirectory(string d)
    {
      if (Directory.Exists(d))
      {
        Console.WriteLine(d);
        Directory.Delete(d, true);
      }
    }

    foreach (var d in GetDirectories(dir))
    {
      RemoveDirectory(d);
    }
  }
);

Target(
  VERSION,
  async () =>
  {
    var (output, _) = await ReadAsync("dotnet", "minver -v w").ConfigureAwait(false);
    output = output.Trim();
    Console.WriteLine($"Version: {output}");
    Run("echo", $"\"version={output}\" >> $GITHUB_OUTPUT");
  }
);

Target(
  RESTORE_TOOLS,
  () =>
  {
    Run("dotnet", "tool restore");
  }
);

Target(
  FORMAT,
  DependsOn(RESTORE_TOOLS),
  () =>
  {
    Run("dotnet", "csharpier --check .");
  }
);

Target(
  RESTORE,
  DependsOn(FORMAT),
  Consts.Solutions,
  s =>
  {
    Run("dotnet", $"restore {s} --locked-mode");
  }
);

Target(
  BUILD_SERVER_VERSION,
  DependsOn(RESTORE_TOOLS),
  () =>
  {
    Run("dotnet", "tool run dotnet-gitversion /output json /output buildserver");
  }
);

Target(
  BUILD,
  DependsOn(RESTORE),
  Consts.Solutions,
  s =>
  {
    var version = Environment.GetEnvironmentVariable("GitVersion_FullSemVer") ?? "3.0.0-localBuild";
    var fileVersion = Environment.GetEnvironmentVariable("GitVersion_AssemblySemFileVer") ?? "3.0.0.0";
    Console.WriteLine($"Version: {version} & {fileVersion}");
    Run(
      "dotnet",
      $"build {s} -c Release --no-restore -warnaserror -p:Version={version} -p:FileVersion={fileVersion} -v:m"
    );
  }
);

Target(CHECK_SOLUTIONS, Solutions.CompareConnectorsToLocal);

Target(
  TEST,
  DependsOn(BUILD, CHECK_SOLUTIONS),
  Glob.Files(".", "**/*.Tests.csproj"),
  file =>
  {
    Run("dotnet", $"test {file} -c Release --no-build --no-restore --verbosity=minimal");
  }
);

Target(
  TEST_ONLY,
  DependsOn(FORMAT),
  Glob.Files(".", "**/*.Tests.csproj"),
  file =>
  {
    Run("dotnet", $"restore {file} --locked-mode");
    Run(
      "dotnet",
      $"test {file} -c Release --no-restore --verbosity=minimal  /p:AltCover=true /p:AltCoverAttributeFilter=ExcludeFromCodeCoverage /p:AltCoverVerbosity=Warning"
    );
  }
);

Target(
  ZIP,
  DependsOn(TEST),
  Consts.InstallerManifests,
  x =>
  {
    var outputDir = Path.Combine(".", "output");
    var slugDir = Path.Combine(outputDir, x.HostAppSlug);

    Directory.CreateDirectory(outputDir);
    Directory.CreateDirectory(slugDir);

    foreach (var asset in x.Projects)
    {
      var fullPath = Path.Combine(".", asset.ProjectPath, "bin", "Release", asset.TargetName);
      if (!Directory.Exists(fullPath))
      {
        throw new InvalidOperationException("Could not find: " + fullPath);
      }

      var assetName = Path.GetFileName(asset.ProjectPath);
      var connectorDir = Path.Combine(slugDir, assetName);

      Directory.CreateDirectory(connectorDir);
      foreach (var directory in Directory.EnumerateDirectories(fullPath, "*", SearchOption.AllDirectories))
      {
        Directory.CreateDirectory(directory.Replace(fullPath, connectorDir));
      }

      foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
      {
        Console.WriteLine(file);
        File.Copy(file, file.Replace(fullPath, connectorDir), true);
      }
    }

    var outputPath = Path.Combine(outputDir, $"{x.HostAppSlug}.zip");
    File.Delete(outputPath);
    Console.WriteLine($"Zipping: '{slugDir}' to '{outputPath}'");
    ZipFile.CreateFromDirectory(slugDir, outputPath);
    // Directory.Delete(slugDir, true);
  }
);

Target("default", DependsOn(FORMAT, ZIP), () => Console.WriteLine("Done!"));

await RunTargetsAndExitAsync(args).ConfigureAwait(true);
