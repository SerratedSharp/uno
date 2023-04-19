﻿// Uncomment the following line to write expected files to disk
// Don't commit this line uncommented.
//#define WRITE_EXPECTED

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using Uno.UI.SourceGenerators.MetadataUpdates;
using Uno.UI.SourceGenerators.XamlGenerator;

namespace Uno.UI.SourceGenerators.Tests.Verifiers
{
	public record struct XamlFile(string FileName, string Contents);

	public class TestSetup
	{
		public TestSetup(string xamlFileName, string subFolder)
		{
			XamlFileName = xamlFileName;
			SubFolder = subFolder;
		}

		public string XamlFileName { get; }
		public string SubFolder { get; }
		public List<string> PreprocessorSymbols { get; } = new List<string>();
		public List<DiagnosticResult> ExpectedDiagnostics { get; } = new List<DiagnosticResult>();
	}

	public static partial class XamlSourceGeneratorVerifier
	{
		public static async Task AssertXamlGenerator(TestSetup testSetup, [CallerFilePath] string testFilePath = "", [CallerMemberName] string testMethodName = "")
		{
			var projectFolder = Path.GetFullPath(Path.Combine("..", "..", ".."));
			var solutionFolder = Path.GetFullPath(Path.Combine(projectFolder, "..", ".."));
			var folder = Path.GetFullPath(Path.Combine(solutionFolder, testSetup.SubFolder));
			var xaml = File.ReadAllText(Path.Combine(folder, testSetup.XamlFileName));
			var cs = File.ReadAllText(Path.Combine(folder, testSetup.XamlFileName + ".cs"));

			var test = new Test(new XamlFile(testSetup.XamlFileName, xaml), testFilePath, testMethodName)
			{
				TestState =
				{
					Sources = { cs },
				},
				PreprocessorSymbols = testSetup.PreprocessorSymbols,
			}.AddGeneratedSources();
			test.ExpectedDiagnostics.AddRange(testSetup.ExpectedDiagnostics);

			await test.RunAsync();
		}

		public class Test : CSharpSourceGeneratorVerifier<XamlCodeGenerator>.Test
		{
			private readonly string _testFilePath;
			private readonly string _testMethodName;
			private const string TestOutputFolderName = "TestOutput";

			public Test(XamlFile xamlFile, [CallerFilePath] string testFilePath = "", [CallerMemberName] string testMethodName = "")
				: this(new[] { xamlFile }, testFilePath, testMethodName)
			{
			}

			public Test(XamlFile[] xamlFiles, [CallerFilePath] string testFilePath = "", [CallerMemberName] string testMethodName = "")
			{
				_testFilePath = testFilePath;
				_testMethodName = testMethodName;

				var globalConfigBuilder = new StringBuilder("""
					is_global = true
					# For now, there is no need to customize these for each test.
					build_property.MSBuildProjectFullPath = C:\Project\Project.csproj
					build_property.RootNamespace = MyProject
					build_property.UnoForceHotReloadCodeGen = false
					""").AppendLine();

				foreach (var xamlFile in xamlFiles)
				{
					globalConfigBuilder.Append($@"[C:/Project/0/{xamlFile.FileName}]
build_metadata.AdditionalFiles.SourceItemGroup = Page
");
					TestState.AdditionalFiles.Add(($"C:/Project/0/{xamlFile.FileName}", xamlFile.Contents));
				}
				TestState.AnalyzerConfigFiles.Add(("/.globalconfig", globalConfigBuilder.ToString()));

				ReferenceAssemblies = new ReferenceAssemblies(
						"net7.0",
						new PackageIdentity(
							"Microsoft.NETCore.App.Ref",
							"7.0.0"),
						Path.Combine("ref", "net7.0"));

#if WRITE_EXPECTED
				TestBehaviors |= TestBehaviors.SkipGeneratedSourcesCheck;
#endif
			}

			public IEnumerable<string> PreprocessorSymbols { get; set; } = ImmutableArray<string>.Empty;

			protected override ParseOptions CreateParseOptions()
			{
				var options = (CSharpParseOptions)base.CreateParseOptions();
				return options.WithPreprocessorSymbols(PreprocessorSymbols);
			}

			protected override Project ApplyCompilationOptions(Project project)
			{
				var p = project.AddMetadataReferences(BuildUnoReferences());

				return base.ApplyCompilationOptions(p);
			}

			protected override async Task<Compilation> GetProjectCompilationAsync(Project project, IVerifier verifier, CancellationToken cancellationToken)
			{
				var resourceDirectory = Path.Combine(Path.GetDirectoryName(_testFilePath)!, TestOutputFolderName, _testMethodName);

				var compilation = await base.GetProjectCompilationAsync(project, verifier, cancellationToken);
				var expectedNames = new HashSet<string>();
				foreach (var tree in compilation.SyntaxTrees.Skip(project.DocumentIds.Count))
				{
					WriteTreeToDiskIfNecessary(tree, resourceDirectory);
					expectedNames.Add(Path.GetFileName(tree.FilePath));
				}

				var currentTestPrefix = $"Uno.UI.SourceGenerators.netcore.Tests.XamlCodeGeneratorTests.{TestOutputFolderName}.{_testMethodName}.";
				foreach (var name in GetType().Assembly.GetManifestResourceNames())
				{
					if (!name.StartsWith(currentTestPrefix))
					{
						continue;
					}

					if (!expectedNames.Contains(name.Substring(currentTestPrefix.Length)))
					{
						throw new InvalidOperationException($"Unexpected test resource: {name.Substring(currentTestPrefix.Length)}");
					}
				}

				return compilation;
			}

			public Test AddGeneratedSources()
			{
				var expectedPrefix = $"Uno.UI.SourceGenerators.netcore.Tests.XamlCodeGeneratorTests.{TestOutputFolderName}.{_testMethodName}.";
				foreach (var resourceName in typeof(Test).Assembly.GetManifestResourceNames().OrderBy(x => x.Contains("LocalizationResources") ? 0 : (x.Contains("GlobalStaticResources") ? 2 : 1)))
				{
					if (!resourceName.StartsWith(expectedPrefix))
					{
						continue;
					}

					using var resourceStream = GetType().Assembly.GetManifestResourceStream(resourceName);
					if (resourceStream is null)
					{
						throw new InvalidOperationException();
					}

					using var reader = new StreamReader(resourceStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
					var name = resourceName.Substring(expectedPrefix.Length);
					TestState.GeneratedSources.Add((typeof(XamlCodeGenerator), name, reader.ReadToEnd()));
				}

				return this;
			}

			[Conditional("WRITE_EXPECTED")]
			private static void WriteTreeToDiskIfNecessary(SyntaxTree tree, string resourceDirectory)
			{
				if (tree.Encoding is null)
				{
					throw new ArgumentException("Syntax tree encoding was not specified");
				}

				var name = Path.GetFileName(tree.FilePath);
				var filePath = Path.Combine(resourceDirectory, name);
				Directory.CreateDirectory(resourceDirectory);
				File.WriteAllText(filePath, tree.GetText().ToString(), tree.Encoding);
			}

			private static MetadataReference[] BuildUnoReferences()
			{
				const string configuration =
#if DEBUG
					"Debug";
#else
					"Release";
#endif

				var availableTargets = new[] {
					Path.Combine("Uno.UI.Skia", configuration, "net7.0"),
					Path.Combine("Uno.UI.Reference", configuration, "net7.0"),
				};

				var unoUIBase = Path.Combine(
					Path.GetDirectoryName(typeof(HotReloadWorkspace).Assembly.Location)!,
					"..",
					"..",
					"..",
					"..",
					"..",
					"Uno.UI",
					"bin"
					);

				var unoTarget = availableTargets
					.Select(t => Path.Combine(unoUIBase, t, "Uno.UI.dll"))
					.FirstOrDefault(File.Exists);

				if (unoTarget is null)
				{
					throw new InvalidOperationException($"Unable to find Uno.UI.dll in {string.Join(",", availableTargets)}");
				}

				return Directory.GetFiles(Path.GetDirectoryName(unoTarget)!, "*.dll")
							.Select(f => MetadataReference.CreateFromFile(Path.GetFullPath(f)))
							.ToArray();
			}

		}
	}
}
