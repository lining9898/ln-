using System.Diagnostics;
using System.Text;

namespace AiKnowledgeAssistant.Infrastructure.OCR;

// Local tools only. The Windows package must include pdftoppm.exe, tesseract.exe,
// and chi_sim/eng traineddata before scanned-PDF OCR can be accepted on Windows.
public sealed class TesseractPdfPageOcr : IPdfPageOcr
{
    private readonly string renderer;
    private readonly string tesseract;
    private readonly string? tessdataDirectory;

    public TesseractPdfPageOcr(string? renderer = null, string? tesseract = null,
        string? tessdataDirectory = null)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "ocr");
        this.renderer = renderer ?? FirstExisting(Environment.GetEnvironmentVariable("AKA_PDFTOPPM"),
            Path.Combine(bundled, "pdftoppm.exe"),
            @"C:\Program Files\poppler\Library\bin\pdftoppm.exe",
            @"C:\Program Files\poppler\bin\pdftoppm.exe") ?? "pdftoppm";
        this.tesseract = tesseract ?? FirstExisting(Environment.GetEnvironmentVariable("AKA_TESSERACT"),
            Path.Combine(bundled, "tesseract.exe"),
            @"C:\Program Files\Tesseract-OCR\tesseract.exe",
            @"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe") ?? "tesseract";
        this.tessdataDirectory = tessdataDirectory ??
            FirstExistingDirectory(Environment.GetEnvironmentVariable("AKA_TESSDATA"),
                Path.Combine(bundled, "tessdata"),
                @"C:\Program Files\Tesseract-OCR\tessdata",
                @"C:\Program Files (x86)\Tesseract-OCR\tessdata");
    }

    public string Recognize(string pdfPath, int physicalPageNumber)
    {
        if (physicalPageNumber <= 0) throw new ArgumentOutOfRangeException(nameof(physicalPageNumber));
        var temporary = Path.Combine(Path.GetTempPath(), "aka-ocr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var prefix = Path.Combine(temporary, "page");
            Run(renderer, ["-f", physicalPageNumber.ToString(), "-l", physicalPageNumber.ToString(),
                "-singlefile", "-r", "250", "-png", pdfPath, prefix]);
            var imagePath = prefix + ".png";
            if (!File.Exists(imagePath)) throw new IOException("PDF 页面渲染未产生图像。");
            var arguments = new List<string> { imagePath, "stdout", "-l", "chi_sim+eng" };
            if (tessdataDirectory is not null)
            {
                arguments.Add("--tessdata-dir");
                arguments.Add(tessdataDirectory);
            }
            return Run(tesseract, arguments);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static string Run(string executable, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new IOException("无法启动本地 OCR 工具。");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(90000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("本地 OCR 工具运行超时。");
        }
        Task.WaitAll(output, error);
        if (process.ExitCode != 0)
            throw new IOException("本地 OCR 工具执行失败；请检查工具和语言包。");
        return output.Result;
    }

    private static string? FirstExisting(params string?[] paths)
    {
        foreach (var path in paths)
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
        return null;
    }

    private static string? FirstExistingDirectory(params string?[] paths)
    {
        foreach (var path in paths)
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return path;
        return null;
    }
}
