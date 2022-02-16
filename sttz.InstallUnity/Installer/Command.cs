using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace sttz.InstallUnity
{

/// <summary>
/// Helper class to run command-line programs.
/// </summary>
public static class Command
{
    static ILogger Logger = UnityInstaller.CreateLogger("Command");

    /// <summary>
    /// Run a command asynchronously.
    /// </summary>
    /// <param name="command">Command to execute</param>
    /// <param name="arguments">Arguments to pass to the command</param>
    /// <param name="input">Input to write to the process' standard input</param>
    /// <param name="cancellation">Token to stop the command</param>
    /// <returns>A task that returns the command's exit code, standard output and standard error</returns>
    public static Task<(int exitCode, string output, string error)> Run(
        string command,
        string arguments,
        string input = null,
        CancellationToken cancellation = default
    ) {
        var startInfo = new ProcessStartInfo();
        startInfo.FileName = command;
        startInfo.Arguments =  arguments;
        return Run(startInfo, input, cancellation);
    }

    /// <summary>
    /// Run a command asynchronously.
    /// </summary>
    /// <param name="command">Command to execute</param>
    /// <param name="arguments">Arguments to pass to the command</param>
    /// <param name="onOutput">Called for every standard output line</param>
    /// <param name="onError">Called for every standard error line</param>
    /// <param name="input">Input to write to the process' standard input</param>
    /// <param name="cancellation">Token to stop the command</param>
    /// <returns>A task that returns the command's exit code</returns>
    public static Task<int> Run(
        string command,
        string arguments,
        Action<string> onOutput, 
        Action<string> onError,
        string input = null,
        CancellationToken cancellation = default
    ) {
        var startInfo = new ProcessStartInfo();
        startInfo.FileName = command;
        startInfo.Arguments =  arguments;
        return Run(startInfo, onOutput, onError, input, cancellation);
    }

    /// <summary>
    /// Same as <see cref="Run(ProcessStartInfo, Action{string}, Action{string}, string, CancellationToken)"/> but
    /// returns standard output and error as string when the process exists instead of streaming them.
    /// </summary>
    /// <param name="startInfo">Process start info</param>
    /// <param name="input">Input to write to the process' standard input</param>
    /// <param name="cancellation">Token to stop the command</param>
    /// <returns>A task that returns the command's exit code, standard output and standard error</returns>
    public async static Task<(int exitCode, string output, string error)> Run(
        ProcessStartInfo startInfo, 
        string input = null, 
        CancellationToken cancellation = default
    ) {
        var output = new StringBuilder();
        Action<string> outputReader = (string outputLine) => {
            output.AppendLine(outputLine);
        };

        var error = new StringBuilder();
        Action<string> errorReader = (string errorLine) => {
            error.AppendLine(errorLine);
        };

        var code = await Run(startInfo, outputReader, errorReader, input, cancellation);

        return (code, output.ToString(), error.ToString());
    }

    /// <summary>
    /// Run a command asynchronously.
    /// </summary>
    /// <remarks>
    /// Note that some of the startInfo configuration will be overwritten due to
    /// Process' constraints. UseShellExecute is set to false, RedirectStandardOutput
    /// and RedirectStandardError set to true. If an input is provided, 
    /// RedirectStandardInput is also set to true.
    /// </remarks>
    /// <param name="startInfo">Process start info</param>
    /// <param name="onOutput">Called for every standard output line</param>
    /// <param name="onError">Called for every standard error line</param>
    /// <param name="input">Input to write to the process' standard input</param>
    /// <param name="cancellation">Token to stop the command</param>
    /// <returns>A task that returns the command's exit code</returns>
    public static Task<int> Run(
        ProcessStartInfo startInfo, 
        Action<string> onOutput, 
        Action<string> onError,
        string input = null, 
        CancellationToken cancellation = default
    ) {
        var commandName = Path.GetFileName(startInfo.FileName);

        var command = new Process();
        command.StartInfo = startInfo;
        command.StartInfo.UseShellExecute = false;
        command.StartInfo.RedirectStandardOutput = true;
        command.StartInfo.RedirectStandardError = true;
        command.EnableRaisingEvents = true;

        if (!string.IsNullOrEmpty(input)) {
            command.StartInfo.RedirectStandardInput = true;
        }

        command.OutputDataReceived += (s, a) => {
            if (onOutput != null) {
                onOutput(a.Data);
            }
        };
        command.ErrorDataReceived += (s, a) => {
            if (onError != null) {
                onError(a.Data);
            }
        };

        var completion = new TaskCompletionSource<int>();
        command.Exited += (s, a) => {
            // Wait for stdin and stderr to flush
            // see https://github.com/dotnet/runtime/issues/18789
            while (!command.WaitForExit(10000));
            command.WaitForExit();

            Thread.Sleep(100);

            var exitCode = command.ExitCode;
            command.Close();

            Logger.LogDebug($"{command.StartInfo.FileName} exited with code {exitCode}");
            completion.SetResult(exitCode);
        };

        if (cancellation.CanBeCanceled) {
            cancellation.Register(() => {
                if (command.HasExited) return;
                Logger.LogDebug($"Terminating {command.StartInfo.FileName}");
                //command.Kill();
                command.CloseMainWindow();
            });
        }

        try {
            Logger.LogDebug($"$ {command.StartInfo.FileName} {command.StartInfo.Arguments}");
            command.Start();

            command.BeginOutputReadLine();
            command.BeginErrorReadLine();

            if (!string.IsNullOrEmpty(input)) {
                var writer = new StreamWriter(command.StandardInput.BaseStream, new System.Text.UTF8Encoding(false));
                writer.Write(input);
                writer.Close();
            }
        } catch (Exception e) {
            if (onError != null) onError("Exception running " + commandName + ": " + e.Message);
            return Task.FromResult(-1);
        }

        return completion.Task;
    }
}

}
