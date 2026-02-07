using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace UserContextRetrievalMcpServer.Tools;

[McpServerToolType]
public sealed class UserContextRetrievalTool
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    [McpServerTool(Name = "user_context_retrieval")]
    [Description(
        "Presents questions to the user in a new terminal window and returns their answers. " +
        "Use this tool when you encounter ambiguous requirements, need design decisions clarified, " +
        "hit unexpected issues that require user input, or when the initial prompt lacks sufficient detail. " +
        "The user will see your stated reason and all questions, and can respond to each one individually. " +
        "Each question should be clear, specific, and self-contained.")]
    public async Task<string> AskUser(
        [Description("A clear explanation of why you need the user's input right now. This is displayed prominently to the user.")]
        string reason,
        [Description("The list of specific questions to ask the user. Each should be a complete, self-contained question.")]
        string[] questions,
        CancellationToken cancellationToken = default)
    {
        if (questions is not { Length: > 0 })
        {
            return "Error: No questions provided. Please supply at least one question.";
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var tempDir = Path.Combine(Path.GetTempPath(), "UserContextRetrievalMcpServer", sessionId);
        Directory.CreateDirectory(tempDir);

        var questionsFile = Path.Combine(tempDir, "questions.json");
        var responseFile = Path.Combine(tempDir, "response.json");
        var scriptFile = Path.Combine(tempDir, "prompt.ps1");

        try
        {
            // Serialize the question payload
            var payload = new { reason, questions };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(questionsFile, json, Encoding.UTF8, cancellationToken);

            // Generate the PowerShell prompt script
            var script = GeneratePromptScript(questionsFile, responseFile);
            await File.WriteAllTextAsync(scriptFile, script, Encoding.UTF8, cancellationToken);

            // Launch a new terminal window
            var process = LaunchTerminal(scriptFile);
            if (process is null)
            {
                return "Error: Failed to launch a terminal window for user input. " +
                       "Ensure PowerShell is available on this system.";
            }

            // Wait for the user to finish responding (with timeout)
            using var timeoutCts = new CancellationTokenSource(DefaultTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                TryKill(process);
                return "The user did not respond within the 10-minute timeout period. " +
                       "You may try asking again or proceed with your best judgment.";
            }

            // Read and return the response
            if (!File.Exists(responseFile))
            {
                return "The user closed the prompt window without providing answers. " +
                       "You may try asking again or proceed with your best judgment.";
            }

            var responseJson = await File.ReadAllTextAsync(responseFile, Encoding.UTF8, cancellationToken);
            return FormatResponse(responseJson);
        }
        finally
        {
            // Clean up temp files
            TryDeleteDirectory(tempDir);
        }
    }

    private static Process? LaunchTerminal(string scriptFile)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{scriptFile}\"",
                    UseShellExecute = true
                });
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Create a bash wrapper that the terminal can execute
                var bashScript = scriptFile.Replace(".ps1", ".sh");
                File.WriteAllText(bashScript,
                    $"#!/bin/bash\npwsh -NoProfile -File '{scriptFile}'\n");
                Process.Start("chmod", $"+x \"{bashScript}\"")?.WaitForExit();
                return Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-a Terminal \"{bashScript}\"",
                    UseShellExecute = false
                });
            }

            // Linux: try common terminal emulators
            string[] terminals = ["x-terminal-emulator", "gnome-terminal", "konsole", "xterm"];
            foreach (var term in terminals)
            {
                try
                {
                    var bashScript = scriptFile.Replace(".ps1", ".sh");
                    File.WriteAllText(bashScript,
                        $"#!/bin/bash\npwsh -NoProfile -File '{scriptFile}'\n");
                    Process.Start("chmod", $"+x \"{bashScript}\"")?.WaitForExit();
                    return Process.Start(new ProcessStartInfo
                    {
                        FileName = term,
                        Arguments = $"-e \"{bashScript}\"",
                        UseShellExecute = false
                    });
                }
                catch { /* try next terminal */ }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string GeneratePromptScript(string questionsFile, string responseFile)
    {
        var qPath = questionsFile.Replace("'", "''");
        var rPath = responseFile.Replace("'", "''");

        var script = """
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            $host.UI.RawUI.WindowTitle = 'Agent Request - Awaiting Your Input'

            $data = Get-Content -Path '%%QUESTIONS_FILE%%' -Raw | ConvertFrom-Json

            Write-Host ''
            Write-Host '  ============================================================' -ForegroundColor Cyan
            Write-Host '              An AI Agent Needs Your Input                     ' -ForegroundColor Cyan
            Write-Host '  ============================================================' -ForegroundColor Cyan
            Write-Host ''
            Write-Host '  Reason:' -ForegroundColor Yellow
            Write-Host ''

            # Word-wrap the reason text
            $reason = $data.reason
            $words = $reason -split ' '
            $line = '  '
            foreach ($word in $words) {
                if (($line.Length + $word.Length + 1) -gt 62) {
                    Write-Host $line -ForegroundColor White
                    $line = '  ' + $word
                } else {
                    if ($line -eq '  ') { $line += $word } else { $line += ' ' + $word }
                }
            }
            if ($line.Trim() -ne '') { Write-Host $line -ForegroundColor White }

            Write-Host ''
            Write-Host '  ============================================================' -ForegroundColor Cyan
            Write-Host ''

            # Display all questions upfront
            $total = $data.questions.Count
            $i = 1
            foreach ($q in $data.questions) {
                Write-Host "  $i. $q" -ForegroundColor White
                $i++
            }

            Write-Host ''
            Write-Host '  ────────────────────────────────────────────────────────────' -ForegroundColor DarkGray
            Write-Host '  Enter your answers below. Press Enter after each response.' -ForegroundColor DarkGray
            Write-Host ''

            # Collect answers sequentially
            $answers = @()
            $i = 1
            foreach ($q in $data.questions) {
                Write-Host -NoNewline "  $i> " -ForegroundColor Cyan
                $answer = Read-Host
                $answers += [PSCustomObject]@{ question = $q; answer = $answer }
                $i++
            }

            $response = [PSCustomObject]@{ answers = $answers } | ConvertTo-Json -Depth 10
            Set-Content -Path '%%RESPONSE_FILE%%' -Value $response -Encoding UTF8

            Write-Host ''
            Write-Host '  Responses recorded successfully!' -ForegroundColor Green
            Write-Host '  This window will close in a few seconds...' -ForegroundColor DarkGray
            Write-Host ''
            Start-Sleep -Seconds 3
            """;

        return script
            .Replace("%%QUESTIONS_FILE%%", qPath)
            .Replace("%%RESPONSE_FILE%%", rPath);
    }

    private static string FormatResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var answers = doc.RootElement.GetProperty("answers");

            var sb = new StringBuilder();
            sb.AppendLine($"User responded to {answers.GetArrayLength()} question(s):");
            sb.AppendLine();

            var i = 1;
            foreach (var item in answers.EnumerateArray())
            {
                var question = item.GetProperty("question").GetString();
                var answer = item.GetProperty("answer").GetString();
                sb.AppendLine($"{i}. Q: {question}");
                sb.AppendLine($"   A: {answer}");
                sb.AppendLine();
                i++;
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Received a response from the user but failed to parse it: {ex.Message}" +
                   $"\n\nRaw response:\n{responseJson}";
        }
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
