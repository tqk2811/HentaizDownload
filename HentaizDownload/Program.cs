using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics;

namespace HentaizDownload
{
  static class Program
  {
    const string videoDomain = "logs.ocho.top";
    static readonly HttpClient httpClient = new HttpClient(new HttpClientHandler()
    {
      //don't know http 403 on some computer, just try
      UseProxy = false,
      UseDefaultCredentials = true,
      AutomaticDecompression = System.Net.DecompressionMethods.All,
      UseCookies = false
    });
    static readonly Regex regex_videoQuality = new Regex(@"\/\d+.m3u8", RegexOptions.Multiline);
    static readonly Regex regex_chunk = new Regex(@"^https:\/\/.+$", RegexOptions.Multiline);
    static readonly Regex regex_iframe = new Regex(@"<iframe.*?<\/iframe>");
    static readonly Regex regex_frameUrl = new Regex("(?<=src=\\\").*?(?=\\\")");
    static readonly Regex regex_frameName = new Regex("(?<=title=\\\").*?(?=\\\")");
    static void Main(string[] args)
    {
      while (true)
      {
        Console.Write("VideoUrl:");
        string url = Console.ReadLine();
        string frame = GetFrameFromUrl(url);

        if(string.IsNullOrEmpty(frame))
        {
          Console.WriteLine("Không tìm được frame");
          continue;
        }

        Match match_Url = regex_frameUrl.Match(frame);
        Match match_name = regex_frameName.Match(frame);
        if(!match_Url.Success || !match_name.Success)
        {
          Console.WriteLine("Không tìm được video id. Hoặc không sử dụng server hentaiz (có thể tải bằng idm)");
          continue;
        }

        Uri urlFrame = new Uri(match_Url.Value);
        string id = urlFrame.Segments.Last();
        string name = match_name.Value;
        foreach(var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');

        string main = DownloadData($"https://{videoDomain}/segments/{id}/main.m3u8");
        MatchCollection matches = regex_videoQuality.Matches(main);
        Console.WriteLine("Select Quality:");
        for (int i = 0; i < matches.Count; i++) Console.WriteLine($"{i} {matches[i].Value}");

        int index = 0;
        while (!(int.TryParse(Console.ReadLine(), out index) && index >= 0 && index < matches.Count)) ;

        string audio = DownloadData($"https://{videoDomain}/segments/{id}/audio.m3u8");
        string video = DownloadData($"https://{videoDomain}/segments/{id}{matches[index].Value}");

        var videoUrls = regex_chunk.Matches(video).Select(x => x.Value);
        var audioUrls = regex_chunk.Matches(audio).Select(x => x.Value);

        Task task_video = DownloadChunk(videoUrls, $"{id}_video.m3u8");
        Task task_audio = DownloadChunk(audioUrls, $"{id}_audio.m3u8");

        Task.WaitAll(task_audio, task_video);
        Console.WriteLine("Tải hoàn tất");

        FFmpegMerge($"{name}.flv", $"{id}_video.m3u8", $"{id}_audio.m3u8");

        Console.WriteLine("Ghép file hoàn tất");

        Console.Write("Tiếp tục? ([Y]/N):");
        string contin = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(contin) || contin.ToLower().StartsWith("y")) continue;
        else return;
      }
    }

    static string DownloadData(string url)
    {
      using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
      AddHeader(req);
      using HttpResponseMessage res = httpClient.Send(req, HttpCompletionOption.ResponseContentRead);
      return res.EnsureSuccessStatusCode().Content.ReadAsStringAsync().Result;
    }

    static async Task DownloadChunk(IEnumerable<string> urls, string saveName)
    {
      int count = urls.Count();
      using FileStream fs = new FileStream(saveName, FileMode.Create, FileAccess.Write, FileShare.Read);
      int i = 0;
      foreach(var url in urls)
      {
        i++;
        Console.WriteLine($"Download: ({i}/{count}) {url}");
        using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
        AddHeader(req);
        using HttpResponseMessage res = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        using Stream content = await res.EnsureSuccessStatusCode().Content.ReadAsStreamAsync();
        await content.CopyToAsync(fs);
      }
    }


    static void AddHeader(HttpRequestMessage req)
    {
      req.Headers.Add("Origin", "https://v.hentaiz.vip");
      req.Headers.Add("Accept-Encoding", "deflate");
      req.Headers.Add("Accept", "*/*");
      req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/93.0.4577.63 Safari/537.36");
    }

    static string GetFrameFromUrl(string url)
    {
      using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
      req.Headers.Add("Accept", "text/html");
      req.Headers.Add("Accept-Encoding", "gzip, deflate");
      req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/93.0.4577.63 Safari/537.36");
      using HttpResponseMessage res = httpClient.Send(req, HttpCompletionOption.ResponseContentRead);
      string content = res.EnsureSuccessStatusCode().Content.ReadAsStringAsync().Result;

      Match match = regex_iframe.Match(content);
      if(match.Success)
      {
        return match.Value;
      }
      return string.Empty;
    }




    static void FFmpegMerge(string output,string video,string audio)
    {
      string args = $"-i {video} -i {audio} -map \"0:v\" -map \"1:a\" -c:v copy -c:a copy -y \"{output}\"";
      using Process process = new Process();
      process.StartInfo.FileName = "ffmpeg.exe";
      process.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
      process.StartInfo.Arguments = args;
      process.StartInfo.UseShellExecute = false;
      process.StartInfo.RedirectStandardOutput = true;
      process.StartInfo.RedirectStandardError = true;
      process.ErrorDataReceived += Process_ErrorDataReceived;
      process.OutputDataReceived += Process_OutputDataReceived;
      process.Start();
      process.BeginOutputReadLine();
      process.BeginErrorReadLine();
      process.WaitForExit();
    }

    private static void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
    {
      Console.WriteLine(e.Data);
    }

    private static void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
      Console.WriteLine(e.Data);
    }

  }
}
