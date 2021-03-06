#addin nuget:?package=Cake.Figlet&version=1.3.1
#tool nuget:?package=vswhere&version=2.7.1
#tool nuget:?package=GitVersion.CommandLine&version=5.0.1

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var verbosityArg = Argument("verbosity", "Minimal");
var verbosity = Verbosity.Minimal;

var sln = new FilePath("./serilog-enrichers-xamarin.sln");
var outputDir = new DirectoryPath("./artifacts");

var isRunningOnAppVeyor = AppVeyor.IsRunningOnAppVeyor;
GitVersion versionInfo = null;

Setup(context =>
{    
    versionInfo = context.GitVersion(new GitVersionSettings {
        UpdateAssemblyInfo = true,
        OutputType = GitVersionOutput.Json
    });

    if (isRunningOnAppVeyor)
    {
        var buildNumber = AppVeyor.Environment.Build.Number;
        AppVeyor.UpdateBuildVersion(versionInfo.InformationalVersion
            + "-" + buildNumber);
    }

    var cakeVersion = typeof(ICakeContext).Assembly.GetName().Version.ToString();

    Information(Figlet("Enrich Xamarin"));
    Information("Building version {0}, ({1}, {2}) using version {3} of Cake.",
        versionInfo.SemVer,
        configuration,
        target,
        cakeVersion);
   
    verbosity = (Verbosity) Enum.Parse(typeof(Verbosity), verbosityArg, true);
});

Task("Clean")
    .Does(() =>
{
    CleanDirectories("./**/bin");
    CleanDirectories("./**/obj");
    CleanDirectories(outputDir.FullPath);

    EnsureDirectoryExists(outputDir);
});


FilePath msBuildPath;
Task("ResolveBuildTools")
    .WithCriteria(() => IsRunningOnWindows())
    .Does(() => 
{
    var vsWhereSettings = new VSWhereLatestSettings
    {
        IncludePrerelease = true,
        Requires = "Component.Xamarin"
    };
    
    var vsLatest = VSWhereLatest(vsWhereSettings);
    msBuildPath = (vsLatest == null)
        ? null
        : vsLatest.CombineWithFilePath("./MSBuild/Current/Bin/MSBuild.exe");

    if (msBuildPath != null)
        Information("Found MSBuild at {0}", msBuildPath.ToString());
});

Task("Restore")
    .IsDependentOn("ResolveBuildTools")
    .Does(() => 
{
    var settings = GetDefaultBuildSettings()
        .WithTarget("Restore");
    MSBuild(sln, settings);
});

Task("Build")
    .IsDependentOn("ResolveBuildTools")
    .IsDependentOn("Clean")
    .IsDependentOn("Restore")
    .Does(() =>  {

    var settings = GetDefaultBuildSettings()
        .WithProperty("Version", versionInfo.SemVer)
        .WithProperty("PackageVersion", versionInfo.SemVer)
        .WithProperty("InformationalVersion", versionInfo.InformationalVersion)
        .WithProperty("NoPackageAnalysis", "True")
        .WithTarget("Build");
	
    MSBuild(sln, settings);
});

Task("CopyPackages")
    .IsDependentOn("Build")
    .Does(() => 
{
    var nugetFiles = GetFiles("./serilog-enrichers-xamarin/bin/" + configuration + "/**/*.nupkg");
    foreach(var file in nugetFiles)
    {
        Information($"Copying artifact {file.FullPath} to {outputDir.FullPath}");
        CopyFileToDirectory(file, outputDir);
    }
});

Task("Default")
    .IsDependentOn("Build")
    .IsDependentOn("CopyPackages")
    .Does(() => 
{
});

RunTarget(target);

MSBuildSettings GetDefaultBuildSettings()
{
    var settings = new MSBuildSettings 
    {
        Configuration = configuration,
        ToolPath = msBuildPath,
        Verbosity = verbosity,
        ArgumentCustomization = args => args.Append("/m"),
        ToolVersion = MSBuildToolVersion.VS2019
    };

    // workaround for borked VS2019 image
    if (IsRunningOnWindows())
    {
        var jdkDir = EnvironmentVariable<string>("JAVA_HOME_8_X64", string.Empty);
        settings = settings.WithProperty("JavaSdkDirectory", new [] { jdkDir });
    }

    return settings;
}