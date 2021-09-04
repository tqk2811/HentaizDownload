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
  class Program
  {
    static readonly HttpClient httpClient = new HttpClient();
    static readonly Regex regex = new Regex(@"\/\d+.m3u8", RegexOptions.Multiline);
    static readonly Regex regex_chunk = new Regex(@"^https:\/\/.+$", RegexOptions.Multiline);
    static void Main(string[] args)
    {
      Console.Write("VideoId:");
      string id = Console.ReadLine();

      string main = DownloadData($"https://vapi.hentaiz.cc/segments/{id}/main.m3u8");
      MatchCollection matches = regex.Matches(main);
      Console.WriteLine("Select Quality:");
      for(int i = 0; i < matches.Count; i++) Console.WriteLine($"{i} {matches[i].Value}");

      int index = 0;
      while (!(int.TryParse(Console.ReadLine(), out index) && index >= 0 && index < matches.Count));

      string audio = DownloadData($"https://vapi.hentaiz.cc/segments/{id}/audio.m3u8");
      string video = DownloadData($"https://vapi.hentaiz.cc/segments/{id}{matches[index].Value}");

      var videoUrls = regex_chunk.Matches(video).Select(x => x.Value);
      var audioUrls = regex_chunk.Matches(audio).Select(x => x.Value);

      Task task_video = DownloadChunk(videoUrls, $"{id}_video.m3u8");
      Task task_audio = DownloadChunk(audioUrls, $"{id}_audio.m3u8");

      Task.WaitAll(task_audio, task_video);
      Console.WriteLine("Tải hoàn tất");

      FFmpegMerge($"{id}.flv", $"{id}_video.m3u8", $"{id}_audio.m3u8");

      Console.WriteLine("Ghép file hoàn tất");
      Console.ReadLine();
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
      using FileStream fs = new FileStream(saveName, FileMode.Create, FileAccess.Write, FileShare.Read);
      foreach(var url in urls)
      {
        Console.WriteLine("Download:" + url);
        using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
        AddHeader(req);
        using HttpResponseMessage res = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        using Stream content = await res.EnsureSuccessStatusCode().Content.ReadAsStreamAsync();
        await content.CopyToAsync(fs);
      }
    }


    static void AddHeader(HttpRequestMessage req)
    {
      req.Headers.Add("Origin", "https://v.hentaiz.cc");
      req.Headers.Add("Accept-Encoding", "deflate");
      req.Headers.Add("Accept", "*/*");
      req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/93.0.4577.63 Safari/537.36");
    }

    static void FFmpegMerge(string output,string video,string audio)
    {
      string args = $"-i {video} -i {audio} -map \"0:v\" -map \"1:a\" -c:v copy -c:a copy -y {output}";
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
