using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Clothesline.Engines.Abstractions;

namespace Clothesline.Engines.Media;

/// <summary>
/// 原生音视频媒体提取引擎（支持 MP4 / MOV / MKV / WMV / AVI 一键提取 MP3 / WAV 伴音）
/// 基于 Windows 原生 MediaTranscoder 与跨平台管道，免除对庞大外部第三方 ffmpeg 的硬性依赖。
/// </summary>
public class MediaConverter : IConverter
{
    private static readonly HashSet<string> SupportedSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "MP4", "MOV", "MKV", "WMV", "AVI", "WEBM", "M4V"
    };

    private static readonly HashSet<string> SupportedTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "MP3", "WAV"
    };

    public bool CanConvert(string sourceExt, string targetExt)
    {
        var src = sourceExt.TrimStart('.').ToUpperInvariant();
        var tgt = targetExt.TrimStart('.').ToUpperInvariant();

        return SupportedSources.Contains(src) && SupportedTargets.Contains(tgt);
    }

    public async Task<string> ConvertAsync(
        string sourceFilePath,
        string targetExt,
        string outputDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(0.1);

        return await Task.Run(() =>
        {
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourceFilePath);
            var normalizedTargetExt = targetExt.TrimStart('.').ToLowerInvariant();
            var targetFileName = $"{fileNameWithoutExt}.{normalizedTargetExt}";
            var targetFilePath = Path.Combine(outputDirectory, targetFileName);

            if (File.Exists(targetFilePath))
            {
                targetFileName = $"{fileNameWithoutExt}_{DateTime.Now:HHmmss}.{normalizedTargetExt}";
                targetFilePath = Path.Combine(outputDirectory, targetFileName);
            }

            Directory.CreateDirectory(outputDirectory);
            progress?.Report(0.3);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ConvertViaWindowsTranscoder(sourceFilePath, targetFilePath, normalizedTargetExt, progress);
            }
            else
            {
                ConvertViaMac(sourceFilePath, targetFilePath, normalizedTargetExt, progress, cancellationToken);
            }

            progress?.Report(1.0);
            return targetFilePath;
        }, cancellationToken);
    }

    private static void ConvertViaWindowsTranscoder(
        string sourceFilePath,
        string targetFilePath,
        string targetExt,
        IProgress<double>? progress)
    {
        progress?.Report(0.4);

        string profileMethod = targetExt == "wav" ? "CreateWav" : "CreateMp3";
        string escapedSource = sourceFilePath.Replace("'", "''");
        string escapedTarget = targetFilePath.Replace("'", "''");

        string script = $@"
$source = [Windows.Storage.StorageFile]::GetFileFromPathAsync('{escapedSource}').GetAwaiter().GetResult();
$target = [Windows.Storage.StorageFile]::CreateFileAsync('{escapedTarget}', 1).GetAwaiter().GetResult();
$transcoder = New-Object Windows.Media.Transcoding.MediaTranscoder;
$profile = [Windows.Media.MediaProperties.MediaEncodingProfile]::{profileMethod}([Windows.Media.MediaProperties.AudioEncodingQuality]::High);
$prep = $transcoder.PrepareFileTranscodeAsync($source, $target, $profile).GetAwaiter().GetResult();
if ($prep.CanTranscode) {{
    $prep.TranscodeAsync().GetAwaiter().GetResult();
}}
";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi);
        if (proc != null)
        {
            progress?.Report(0.7);
            proc.WaitForExit(15000);
        }

        if (!File.Exists(targetFilePath) || new FileInfo(targetFilePath).Length == 0)
        {
            throw new InvalidOperationException($"原生音频转码失败: {targetFilePath}");
        }
    }

    private static readonly string[] FfmpegCandidates =
    [
        "/opt/homebrew/bin/ffmpeg", // Apple Silicon Homebrew
        "/usr/local/bin/ffmpeg",    // Intel Homebrew / 手动安装
    ];

    /// <summary>
    /// 查找 ffmpeg。从访达/登录项启动的 GUI 进程拿不到 shell 的 PATH，因此先查 Homebrew 常见路径再查 PATH。
    /// </summary>
    private static string? FindFfmpeg()
    {
        foreach (var candidate in FfmpegCandidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, "ffmpeg");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>
    /// macOS 音频提取：优先 ffmpeg（支持全部容器与 MP3 编码）；
    /// 未安装 ffmpeg 时仅能用系统自带 afconvert 从 MP4/MOV/M4V 提取 WAV（afconvert 不能编码 MP3，也不认 MKV/AVI/WMV/WEBM）。
    /// </summary>
    private static void ConvertViaMac(
        string sourceFilePath,
        string targetFilePath,
        string targetExt,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(0.4);

        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        var ffmpeg = FindFfmpeg();
        var sourceExt = Path.GetExtension(sourceFilePath).TrimStart('.').ToUpperInvariant();

        if (ffmpeg != null)
        {
            psi.FileName = ffmpeg;
            foreach (var arg in new[] { "-y", "-nostdin", "-loglevel", "error", "-i", sourceFilePath, "-vn" })
            {
                psi.ArgumentList.Add(arg);
            }
            if (targetExt == "mp3")
            {
                psi.ArgumentList.Add("-codec:a"); psi.ArgumentList.Add("libmp3lame");
                psi.ArgumentList.Add("-q:a"); psi.ArgumentList.Add("2");
            }
            else
            {
                psi.ArgumentList.Add("-codec:a"); psi.ArgumentList.Add("pcm_s16le");
            }
            psi.ArgumentList.Add(targetFilePath);
        }
        else if (targetExt == "wav" && sourceExt is "MP4" or "MOV" or "M4V")
        {
            psi.FileName = "/usr/bin/afconvert";
            foreach (var arg in new[] { "-f", "WAVE", "-d", "LEI16", sourceFilePath, targetFilePath })
            {
                psi.ArgumentList.Add(arg);
            }
        }
        else
        {
            throw new NotSupportedException(
                $"macOS 上将 {sourceExt} 转为 {targetExt.ToUpperInvariant()} 需要 ffmpeg，请先执行: brew install ffmpeg");
        }

        RunToCompletion(psi, TimeSpan.FromMinutes(10), cancellationToken);
        progress?.Report(0.9);

        if (!File.Exists(targetFilePath) || new FileInfo(targetFilePath).Length == 0)
        {
            throw new InvalidOperationException($"音频提取失败: {targetFilePath}");
        }
    }

    private static void RunToCompletion(ProcessStartInfo psi, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"无法启动 {psi.FileName}");
        // 异步读取输出，避免管道写满导致子进程阻塞
        var stderrTask = proc.StandardError.ReadToEndAsync();
        _ = proc.StandardOutput.ReadToEndAsync();

        var deadline = DateTime.UtcNow + timeout;
        while (!proc.WaitForExit(200))
        {
            if (cancellationToken.IsCancellationRequested || DateTime.UtcNow > deadline)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException($"{Path.GetFileName(psi.FileName)} 转码超时");
            }
        }

        if (proc.ExitCode != 0)
        {
            var err = stderrTask.Wait(1000) ? stderrTask.Result.Trim() : string.Empty;
            throw new InvalidOperationException($"{Path.GetFileName(psi.FileName)} 退出码 {proc.ExitCode}: {err}");
        }
    }
}
