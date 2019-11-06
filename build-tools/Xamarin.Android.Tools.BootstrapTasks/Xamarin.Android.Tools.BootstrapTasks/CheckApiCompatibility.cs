using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Xamarin.Android.Tools.BootstrapTasks
{
	public sealed class CheckApiCompatibility : Task
	{
		// This dictionary holds Api versions
		// key is the Api version
		// value is the previous Api version in relation to the key
		static readonly Dictionary<string, string> api_versions = new Dictionary<string, string> ()
		{
			{ "v4.4", "" },
			{ "v4.4.87", "v4.4" },
			{ "v5.0", "v4.4.87" },
			{ "v5.1", "v5.0" },
			{ "v6.0", "v5.1" },
			{ "v7.0", "v6.0" },
			{ "v7.1", "v7.0" },
			{ "v8.0", "v7.1" },
			{ "v8.1", "v8.0" },
			{ "v9.0", "v8.1" },
			{ "v10.0", "v9.0" },
		};

		// Path where Microsoft.DotNet.ApiCompat nuget package is located
		[Required]
		public  string         ApiCompatPath          	{ get; set; }

		// Api level just built
		[Required]
		public  string         ApiLevel            	{ get; set; }

		// The last stable api level.
		[Required]
		public  string         LastStableApiLevel	{ get; set; }

		// Path to xamarin-android-api-compatibility folder
		[Required]
		public  string         ApiCompatContractPath	{ get; set; }

		// Output Path where the assembly was just built
		[Required]
		public  string         TargetImplementationPath	{ get; set; }

		// This Build tasks validates that changes are not breaking Api
		public override bool Execute ()
		{
			Log.LogMessage (MessageImportance.High, $"CheckApiCompatibility for ApiLevel: {ApiLevel}");

			// Check to see if Api has a previous Api defined.
			if (!api_versions.TryGetValue (ApiLevel, out string previousApiLevel)) {
				Log.LogError ($"Please add ApiLevel:{ApiLevel} to the list of supported apis.");
				return !Log.HasLoggedErrors;
			}

			// Get the previous api implementation path by replacing the current api string with the previous one.
			var previousTargetImplementationPath = TargetImplementationPath.Replace (ApiLevel, previousApiLevel);

			// In case previous api is not defined or directory does not exist we can skip the check.
			var validateAgainstPreviousApi = !(string.IsNullOrWhiteSpace (previousApiLevel) || !Directory.Exists (previousTargetImplementationPath));
			if (validateAgainstPreviousApi) {

				// First we check the Api level assembly against the previous api level assembly
				// i.e.: check api breakages using "the just built V2.dll" against "the just built V1.dll"
				ValidateApiCompat (previousTargetImplementationPath);

				if (Log.HasLoggedErrors) {
					return !Log.HasLoggedErrors;
				}
			}

			// If Api level is the latest we should also compare it against the reference assembly
			// located on the external folder. (xamarin-android-api-compatibility)
			// i.e.: check apicompat using "the just built V2.dll" against V2.dll located on xamarin-android-api-compatibility repo
			if (ApiLevel == LastStableApiLevel) {

				// Check xamarin-android-api-compatibility reference directory exists
				var referenceContractPath = Path.Combine (ApiCompatContractPath, "reference");
				if (!Directory.Exists (referenceContractPath)) {
					Log.LogMessage (MessageImportance.High, $"CheckApiCompatibility Warning: Skipping reference contract check.\n{referenceContractPath} does not exist.");
					return !Log.HasLoggedErrors;
				}

				ValidateApiCompat (referenceContractPath);
			}

			return !Log.HasLoggedErrors;
		}

		// Validates Api compatibility between contract (previous version) and implmentation (current version)
		// We do that by using Microsoft.DotNet.ApiCompat.dll
		void ValidateApiCompat (string contractPath) {

			var apiCompat = Path.Combine (ApiCompatPath, "Microsoft.DotNet.ApiCompat.exe");

			string content;
			using (var genApiProcess = new Process()) {

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)){
  					genApiProcess.StartInfo.FileName = apiCompat;
					genApiProcess.StartInfo.Arguments = $"{contractPath} -i {TargetImplementationPath}";
				} else {
					genApiProcess.StartInfo.FileName = $"mono";
					genApiProcess.StartInfo.Arguments = $"{apiCompat} {contractPath} -i {TargetImplementationPath}";
				}

				genApiProcess.StartInfo.UseShellExecute = false;
				genApiProcess.StartInfo.CreateNoWindow = true;
				genApiProcess.StartInfo.RedirectStandardOutput = true;

				// Get api definition for previous Api
				genApiProcess.Start ();
				content = genApiProcess.StandardOutput.ReadToEnd();
				genApiProcess.WaitForExit ();
			}

			ValidateIssues(content);
		}

		// Validates there is no issue or issues found are acceptable
		void ValidateIssues(string content) {

			// Load issues into a dictionary
			var issuesFound = LoadIssues (content);

			// Return if no issues were found
			if (issuesFound == null || issuesFound.Count == 0) {
				return;
			}

			// Verify if there is a file with acceptable issues.
			var acceptableIssuesFile = Path.Combine (ApiCompatContractPath, $"{ApiLevel}.txt");
			if (!File.Exists (acceptableIssuesFile)) {

				// We have issues but there is not acceptable breaks file.
				// Print issues and fail build.
				Log.LogMessage (MessageImportance.High, content);
				Log.LogError ($"CheckApiCompatibility found nonacceptable Api breakages for ApiLevel: {ApiLevel}.");
				return;
			}

			// Read and Convert the acceptable issues into a dictionary
			var exceptionRulesFileContent = File.ReadAllText (acceptableIssuesFile);
			var acceptableIssues = LoadIssues (exceptionRulesFileContent);

			// Now remove all acceptable issues form the dictionary of issues found.
			if (acceptableIssues != null) {
				foreach (var item in acceptableIssues) {
					if (!issuesFound.TryGetValue (item.Key, out HashSet<string> issues)) {
						continue;
					}

					foreach (var issue in item.Value) {
						issues.Remove (issue);
					}
				}
			}

			// Any issue that still exist on issues found means it is a new issue and we should report
			var count = 0;
			foreach (var item in issuesFound) {
				if (item.Value.Count == 0) {
					continue;
				}

				Log.LogMessage (MessageImportance.High, item.Key);
				foreach (var issue in item.Value) {
					Log.LogMessage (MessageImportance.High, issue);
					count++;
				}
			}

			if (count > 0) {
				Log.LogMessage (MessageImportance.High, $"Total Issues: {count}");
				Log.LogError ($"CheckApiCompatibility found nonacceptable Api breakages for ApiLevel: {ApiLevel}.");
			}

			return;
		}

		// Converts list of issue into a dictinary
		Dictionary<string, HashSet<string>> LoadIssues (string content) {

			var issues = new Dictionary <string, HashSet<string>> ();
			HashSet<string> currentSet = null;
			var lines = content.Split ('\n');
			foreach (var line in lines) {

				if (string.IsNullOrWhiteSpace (line)) {
					continue;
				}

				// Create hashset per assembly
				if (line.StartsWith ("Compat issues with assembly", StringComparison.InvariantCultureIgnoreCase)) {
					currentSet = new HashSet<string> ();
					issues.Add (line, currentSet);
					continue;
				}

				// end of file
				if (line.StartsWith ("Total Issues:", StringComparison.InvariantCultureIgnoreCase)) {
					break;
				}

				if (currentSet == null) {
					// Hashset should never be null, unless exception file is not defining assembly line.
					Log.LogError ($"Exception file should start with: Compat issues with assembly");
					return null;
				}

				// Add rule to hashset
				currentSet.Add (line);
			}

			return issues;
		}
	}
}

