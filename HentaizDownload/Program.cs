using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics;
using Newtonsoft.Json;

namespace HentaizDownload
{
  static class Program
  {
    const string videoDomain = "logs.ocho.top";
    static readonly HttpClient httpClient = new HttpClient(new HttpClientHandler()
    {
      UseProxy = false,
      UseDefaultCredentials = true,
      AutomaticDecompression = System.Net.DecompressionMethods.All,
      UseCookies = false
    });
    //static readonly Regex regex_videoQuality = new Regex(@"\/\d+.m3u8", RegexOptions.Multiline);
    static readonly Regex regex_chunk = new Regex(@"^https:\/\/.+$", RegexOptions.Multiline);
    static readonly Regex regex_iframe = new Regex(@"<iframe.*?<\/iframe>");
    static readonly Regex regex_frameUrl = new Regex("(?<=src=\\\").*?(?=\\\")");
    static readonly Regex regex_frameName = new Regex("(?<=title=\\\").*?(?=\\\")");
    static readonly Regex regex_url = new Regex(@"https:\/\/.*?/(\d+x\d+)\/.*?.m3u8", RegexOptions.Multiline);
    static void Main(string[] args)
    {
      //httpClient.DefaultRequestHeaders.Add("Origin", "https://v.hentaiz.vip");
      httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
      httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
      httpClient.DefaultRequestHeaders.Add("Accept-Language", "vi,en;q=0.9,ja;q=0.8,ht;q=0.7");
      httpClient.DefaultRequestHeaders.Add("sec-fetch-dest", "empty");
      httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/95.0.4638.69 Safari/537.36");
      httpClient.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
      httpClient.DefaultRequestHeaders.Add("sec-fetch-dest", "empty");
      httpClient.DefaultRequestHeaders.Add("sec-fetch-mode", "cors");
      httpClient.DefaultRequestHeaders.Add("sec-fetch-site", "cross-site");
      httpClient.DefaultRequestHeaders.Add("sec-ch-ua", "\"Google Chrome\";v=\"95\", \"Chromium\";v=\"95\", \";Not A Brand\";v=\"99\"");
      httpClient.DefaultRequestHeaders.Add("sec-fetch-site", "cross-site");
      while (true)
      {
#if DEBUG
        //string url = "https://hentaiz.vip/onii-chan-asa-made-zutto-gyutte-shite-1/";
        //string url = "https://hentaiz.vip/onii-chan-asa-made-zutto-gyutte-shite-2/";
        //string url = "https://hentaiz.vip/onii-chan-asa-made-zutto-gyutte-shite-3/";
        //string url = "https://hentaiz.vip/onii-chan-asa-made-zutto-gyutte-shite-4/";
        string url = "https://hentaiz.vip/cafe-junkie-1/";
        //string url = "";
        //string url = "";
#else
        Console.Write("VideoUrl:");
        string url = Console.ReadLine();
#endif
        string content = GetString(url);
        Match match = regex_iframe.Match(content);
        if(!match.Success)
        {
          Console.WriteLine("Không tìm được frame");
          continue;
        }

        string iframe = match.Value;
        Match match_Url = regex_frameUrl.Match(iframe);
        Match match_name = regex_frameName.Match(iframe);
        if(!match_Url.Success || !match_name.Success)
        {
          Console.WriteLine("Không tìm được video id.");
          continue;
        }
        
        Uri urlFrame = new Uri(match_Url.Value);
        string id = urlFrame.Segments.Last();
        string name = match_name.Value;
        foreach(var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');

        /////////////////
        MasterPlayList masterPlaylistUrl = GetString<MasterPlayList>($"https://api.{urlFrame.Host}/player/{id}");
        string playlist = GetString(masterPlaylistUrl.masterPlaylistUrl);
        MatchCollection matches = regex_url.Matches(playlist);
        var videos_url = matches.Where(x => !x.Groups[1].Value.Equals("0x0")).ToList();
        var audio_url = matches.Except(videos_url).First();

        Console.WriteLine("Select Quality:");
        for (int i = 0; i < videos_url.Count; i++) Console.WriteLine($"{i} {videos_url[i].Groups[1].Value}");

        int index = 0;
        while (!(int.TryParse(Console.ReadLine(), out index) && index >= 0 && index < videos_url.Count)) ;

        string audio = GetString(audio_url.Value);
        string video = GetString(videos_url[index].Value);

        var videoUrls = regex_chunk.Matches(video).Select(x => x.Value).ToList();
        var audioUrls = regex_chunk.Matches(audio).Select(x => x.Value).ToList();

        Task task_video = DownloadChunk(videoUrls, $"{id}_video.m3u8", $"https://{urlFrame.Host}");
        Task task_audio = DownloadChunk(audioUrls, $"{id}_audio.m3u8", $"https://{urlFrame.Host}");

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


    static async Task DownloadChunk(IEnumerable<string> urls, string saveName, string origin = null)
    {
      int count = urls.Count();
      using FileStream fs = new FileStream(saveName, FileMode.Create, FileAccess.Write, FileShare.Read);
      int i = 0;
      foreach(var url in urls)
      {
        i++;
        Console.WriteLine($"Download: ({i}/{count}) {url}");
        using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(origin))
        {
          req.Headers.Add("Origin", origin);
          req.Headers.Add("Referer", origin);
        }
        using HttpResponseMessage res = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        if(res.IsSuccessStatusCode)
        {
          using Stream content = await res.Content.ReadAsStreamAsync();
          await content.CopyToAsync(fs);
        }
        else
        {
          //debug
          string content = await res.Content.ReadAsStringAsync();
          res.EnsureSuccessStatusCode();
        }
      }
    }

    static string GetString(string url)
    {
      using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
      using HttpResponseMessage res = httpClient.Send(req, HttpCompletionOption.ResponseContentRead);
      return res.EnsureSuccessStatusCode().Content.ReadAsStringAsync().Result;
    }

    static T GetString<T>(string url)
    {
      var content = GetString(url);
      return JsonConvert.DeserializeObject<T>(content);
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

  class MasterPlayList 
  {
    public string masterPlaylistUrl { get; set; }
  }
}
