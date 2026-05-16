// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Net.Http.Headers;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Git;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

namespace CP.BuildTools;

internal static class BuildToolsRuntime
{
    internal static readonly Func<string, CancellationToken, Task<string>> DefaultDownloadStringAsync = DownloadStringAsyncCore;
    internal static readonly Func<string, IReadOnlyList<string>> DefaultRunShellCommand = RunShellCommandCore;
    internal static readonly Func<string, IReadOnlyList<string>> DefaultRunGitCommand = RunGitCommandCore;
    internal static readonly Action<string> DefaultRunWorkloadRestore = RunWorkloadRestoreCore;
    internal static readonly Func<string, bool> DefaultFileExists = File.Exists;
    internal static readonly Action<string, byte[]> DefaultWriteAllBytes = File.WriteAllBytes;
    internal static readonly Action<string, string> DefaultAppendAllText = File.AppendAllText;
    internal static readonly Func<string, string?> DefaultGetEnvironmentVariable = Environment.GetEnvironmentVariable;
    internal static readonly Action<string> DefaultWriteLine = Console.WriteLine;

    private static readonly HttpClient HttpClient = CreateDefaultHttpClient();

    internal static Func<string, CancellationToken, Task<string>> DownloadStringAsync { get; set; } = DefaultDownloadStringAsync;

    internal static Func<string, IReadOnlyList<string>> RunShellCommand { get; set; } = DefaultRunShellCommand;

    internal static Func<string, IReadOnlyList<string>> RunGitCommand { get; set; } = DefaultRunGitCommand;

    internal static Action<string> RunWorkloadRestore { get; set; } = DefaultRunWorkloadRestore;

    internal static Func<string, bool> FileExists { get; set; } = DefaultFileExists;

    internal static Action<string, byte[]> WriteAllBytes { get; set; } = DefaultWriteAllBytes;

    internal static Action<string, string> AppendAllText { get; set; } = DefaultAppendAllText;

    internal static Func<string, string?> GetEnvironmentVariable { get; set; } = DefaultGetEnvironmentVariable;

    internal static Action<string> WriteLine { get; set; } = DefaultWriteLine;

    internal static void Reset()
    {
        DownloadStringAsync = DefaultDownloadStringAsync;
        RunShellCommand = DefaultRunShellCommand;
        RunGitCommand = DefaultRunGitCommand;
        RunWorkloadRestore = DefaultRunWorkloadRestore;
        FileExists = DefaultFileExists;
        WriteAllBytes = DefaultWriteAllBytes;
        AppendAllText = DefaultAppendAllText;
        GetEnvironmentVariable = DefaultGetEnvironmentVariable;
        WriteLine = DefaultWriteLine;
    }

    private static async Task<string> DownloadStringAsyncCore(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> RunShellCommandCore(string command)
    {
        var isWindows = OperatingSystem.IsWindows();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/bash",
            Arguments = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();
        var output = new List<string>();
        while (!process.StandardOutput.EndOfStream)
        {
            output.Add(process.StandardOutput.ReadLine() ?? string.Empty);
        }

        while (!process.StandardError.EndOfStream)
        {
            output.Add(process.StandardError.ReadLine() ?? string.Empty);
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command '{command}' exited with code {process.ExitCode}.");
        }

        return output;
    }

    private static IReadOnlyList<string> RunGitCommandCore(string arguments) =>
        GitTasks.Git(arguments).Select(x => x.Text).ToArray();

    private static void RunWorkloadRestoreCore(string projectOrSolution) =>
        DotNetWorkloadRestore(settings => settings.SetProject(projectOrSolution));

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CP.Nuke.BuildTools", "1.0"));
        return client;
    }
}
