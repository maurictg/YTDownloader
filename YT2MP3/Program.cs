using CliWrap;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace YT2MP3;

class Program
{
    enum Type { INFO, WARNING, SUCCESS, ERROR, FATAL, DEBUG, LOG };
    enum DownloadType { VIDEO, PLAYLIST };
    enum MediaType { VIDEO, AUDIO, MUXED };

    static YoutubeClient yt = new YoutubeClient();

    static DownloadType? downloadType;
    static MediaType? mediaType;
    static string videoUrl = null;
    static string videoId = null;
    static string outputPath = null;
    static string mimeType = null;
    static bool showInfo = false;
    static bool skipIfPresent = false;
    private static bool parallel = false;

    static async Task Main(string[] _args)
    {
        //Check if FFMPEG is present
        await FFMPEG();

        if (_args.Length < 1)
        {
            Alert("Invalid amount of args", Type.WARNING, true, true);
            return;
        }

        await ExecuteArg(0, _args);
    }

    static async Task Start()
    {
        if (videoUrl == null && videoId == null)
        {
            Alert("No videoURL or videoID provided", Type.ERROR);
            return;
        }

        if(mediaType == null && !showInfo)
        {
            Alert("No valid media type provided. Please enter -V for video or -A for audio", Type.ERROR);
            return;
        }

        //Alert($"dtype: {downloadType}, mtype: {mediaType}, url: {videoUrl}, id: {videoId}, output: {outputPath}", Type.DEBUG, true, true);
            
        if(downloadType == DownloadType.PLAYLIST)
        {
            var playlist = await yt.Playlists.GetAsync(videoId ?? videoUrl);

            if (showInfo)
            {
                //In future update: information about every video in playlist
                ListInfo(playlist);
                return;
            }

            Console.Clear();
            Alert($"Downloading playlist \"{playlist.Title}\". This could take a moment", Type.INFO);

            var tasks = new List<Task>();
            
            //Download
            await foreach(var video in yt.Playlists.GetVideosAsync(playlist.Id))
            {
                try
                {
                    if (parallel)
                    {
                        tasks.Add(Task.Run(async() =>
                        {
                            var vid = await yt.Videos.GetAsync(video.Id);
                            await Download(vid);
                        }));
                    }
                    else
                    {
                        var vid = await yt.Videos.GetAsync(video.Id);
                        await Download(vid);
                    }
                }
                catch(Exception e)
                {
                    Alert(e.Message, Type.ERROR, showType: true);
                    //Alert(e.ToString(), Type.DEBUG);
                }
            }

            await Task.WhenAll(tasks);

        }
        else //Video
        {
            var video = await yt.Videos.GetAsync(videoId ?? videoUrl);

            if(showInfo)
            {
                VideoInfo(video);
                return;
            }

            Console.Clear();
            //Download
            try
            {
                await Download(video);
            }
            catch (Exception e)
            {
                Alert(e.Message, Type.ERROR, showType: true);
                //Alert(e.ToString(), Type.DEBUG);
            }

        }

    }

    static async Task FFMPEG()
    {
        var path = FFMPEGPATH();

        if (File.Exists(path))
            return;

        Alert("FFMPEG is not present. ", Type.WARNING, false, true);
        Alert("We'll download it for you. Have a moment... ", writeLine: false);


        try
        {
            using var httpClient = new HttpClient();
            await using var zipStream = await httpClient.GetStreamAsync(FFMPEGDownloadURL());
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);

            var entry = zip.GetEntry(FFMPEGName());
            await using var entryStream = entry.Open();
            await using var fileStream = File.Create(path);
            await entryStream.CopyToAsync(fileStream);
            Alert("DONE", Type.SUCCESS);
        }
        catch (Exception e)
        {
            Alert($"FAILED ({e.Message})", Type.ERROR);
            Alert(e.ToString(), Type.DEBUG);
            Alert("Failed to download ffmpeg. Please try it by hand", Type.FATAL, showType: true);
        }

        if(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Alert("Hey linux user! We need to set chmod x permissions for ffmpeg... ", Type.WARNING, false, true);
            try
            {
                //Ensure permissions
                await Cli.Wrap("/bin/bash").WithArguments(new[] { "-c", $"chmod +x {path}" }).ExecuteAsync();
                Alert("DONE", Type.SUCCESS);
            } 
            catch(Exception e)
            {
                Alert($"FAILED ({e.Message})", Type.ERROR);
                Alert(e.ToString(), Type.DEBUG);
                Alert("Failed to set permissions. Please try it by hand", Type.FATAL, showType: true);
            }
        }
    }

    static string FFMPEGDownloadURL()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "https://github.com/vot/ffbinaries-prebuilt/releases/download/v4.2.1/ffmpeg-4.2.1-win-64.zip";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "https://github.com/vot/ffbinaries-prebuilt/releases/download/v4.2.1/ffmpeg-4.2.1-linux-64.zip";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "https://github.com/vot/ffbinaries-prebuilt/releases/download/v4.2.1/ffmpeg-4.2.1-osx-64.zip";

        return "";
    }

    static string FFMPEGName() => (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg");

    static string FFMPEGPATH() => Path.Combine(Environment.CurrentDirectory, FFMPEGName());
        
    static async Task Download(Video video)
    {
        var manifest = await yt.Videos.Streams.GetManifestAsync(video.Id);
        Alert($"- Downloading {((mimeType == null) ? "" : "(and converting)")} {video.Title} ({video.Duration})... ", writeLine: false);

        var _c = GetCursor();

        var streamInfo = mediaType switch
        {
            MediaType.AUDIO => manifest.GetAudioOnlyStreams().GetWithHighestBitrate(),
            MediaType.VIDEO => manifest.GetVideoOnlyStreams().GetWithHighestVideoQuality(),
            MediaType.MUXED => manifest.GetMuxedStreams().GetWithHighestVideoQuality(),
            _ => null
        };

        if(streamInfo == null)
        {
            SetCursor(_c);
            Alert($"Could not get stream information", Type.ERROR, true, true);
            return;
        }


        //Create valid filename
        var path = outputPath ?? Environment.CurrentDirectory;
        var name = $"{video.Title}.{streamInfo.Container}";
        string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
        foreach (char c in invalid) name = name.Replace(c.ToString(), "");

        path = Path.Combine(path, name);
        if (mimeType != null) path = path.Replace(streamInfo.Container.Name, mimeType);


        //Check if present
        if(skipIfPresent)
        {
            if(File.Exists(path))
            {
                Alert($"SKIPPED", Type.WARNING);
                return;
            }
        }

        //get stream
        //var stream = await yt.Videos.Streams.GetAsync(streamInfo);

        //Saving
        if (mimeType == null)
            await yt.Videos.Streams.DownloadAsync(streamInfo, path);
        else
        {
            await yt.Videos.DownloadAsync(video.Id, path, o 
                => o.SetFormat(mimeType).SetPreset(ConversionPreset.UltraFast).SetFFmpegPath(FFMPEGPATH())
            );
        }

        SetCursor(_c);
        Alert($"DONE {video.Title}", Type.SUCCESS);
    }

    static void VideoInfo(Video video)
    {
        Alert("===== Video information =====", Type.SUCCESS);
        Alert("Title: ", Type.INFO, false);
        Alert(video.Title);
        Alert("Author: ", Type.INFO, false);
        Alert(video.Author.Title);
        Alert("Duration: ", Type.INFO, false);
        Alert(video.Duration.ToString());
        Alert("Likes: ", Type.INFO, false);
        Alert(video.Engagement.LikeCount.ToString(), Type.SUCCESS);
        Alert("Dislikes: ", Type.INFO, false);
        Alert(video.Engagement.DislikeCount.ToString(), Type.ERROR);
        Alert("Views: ", Type.INFO, false);
        Alert(video.Engagement.ViewCount.ToString());
        Alert("Description:", Type.INFO);
        Alert(video.Description);
        Alert("=============================", Type.SUCCESS);
    }

    static void ListInfo(Playlist playlist)
    {
        Alert("===== Playlist information =====", Type.SUCCESS);
        Alert("Title: ", Type.INFO, false);
        Alert(playlist.Title);
        Alert("Author: ", Type.INFO, false);
        Alert(playlist.Author.Title);
        Alert("Description:", Type.INFO);
        Alert(playlist.Description);
        Alert("=============================", Type.SUCCESS);
    }

    static void Help()
    {
        Console.WriteLine("Help page is in progress :D");

        Alert("Usage: ");
        Alert("YT2MP3.exe [arguments] ", Type.SUCCESS);

        Alert("\nShow help page", Type.DEBUG);
        Alert("\t-h or 'help'", Type.INFO, false);
        Alert("\t(Shows this page)");

        Alert("\nDownload type", Type.DEBUG);
        Alert("\t-v ", Type.INFO, false);
        Alert("\t(Download a video)");
        Alert("\t-p ", Type.INFO, false);
        Alert("\t(Download a playlist)");

        Alert("\nSet video/playlist URL or ID", Type.DEBUG);
        Alert("\t-u [url]", Type.INFO, false);
        Alert("\t(Specify playlist or video by URL)");
        Alert("\t-i [id]", Type.INFO, false);
        Alert("\t\t(Specify playlist or video by ID)");

        Alert("\nSet output folder (optional)", Type.DEBUG);
        Alert("\t-o [path]", Type.INFO, false);
        Alert("\t(Specify output path [optional])");

        Alert("\nSet the media type of the stream you want to download", Type.DEBUG);
        Alert("\t-V", Type.INFO, false);
        Alert("\t(Video-only stream)");
        Alert("\t-A", Type.INFO, false);
        Alert("\t(Audio-only stream)");
        Alert("\t-M", Type.INFO, false);
        Alert("\t(Muxed stream, video and audio)");
        Alert("\t-m [mediatype]", Type.INFO, false);
        Alert("\t(Set by hand, accepting: 'v', 'video', 'm', 'muxed', 'a' and 'audio')");

        Alert("\nOther", Type.DEBUG);
        Alert("\t-I", Type.INFO, false);
        Alert("\t(Don't download, but only show information)");
        Alert("\t-t", Type.INFO, false);
        Alert("\t(Set mime type, like 'mp4', 'mp3', 'webm')");
        Alert("\t-s", Type.INFO, false);
        Alert("\t(Skip video if present in directory)");
        Alert("\t-P", Type.INFO, false);
        Alert("\t(Enable parallel downloads for faster processing)");

        Alert("\nProtip", Type.DEBUG);
        Alert("You can use multiple arguments at once, like to show info about a video by id: ", writeLine: false);
        Alert("YT2MP3 -vIi AkRiYsTN", Type.INFO);
        Alert("You can also write this as: ", writeLine: false);
        Alert("YT2MP3 -v -I -i AkRiYsTN", Type.INFO);

    }

    static async Task ExecuteArg(int i, string[] args)
    {
        var arg = args[i].Replace("--", "-");

        if(arg.StartsWith("-h") || arg.Equals("help"))
        {
            Help();
            return;
        }
            
        //Execute action, or value
        if(arg.StartsWith("-") && arg.Length > 1)
        {
            foreach(char a in arg.Remove(0, 1).ToCharArray())
            {
                switch (a)
                {
                    //1. Type download: V (video), P (playlist), I (information)
                    case 'v':
                        downloadType = DownloadType.VIDEO;
                        break;
                    case 'p':
                        downloadType = DownloadType.PLAYLIST;
                        break;
                    case 'I':
                        showInfo = true;
                        break;
                    //2. Read URL or ID
                    case 'u':
                        videoUrl = args[i + 1];
                        break;
                    case 'i':
                        videoId = args[i + 1];
                        break;
                    //3. Set output folder
                    case 'o':
                        outputPath = args[i + 1];
                        break;
                    //4. Set media type
                    case 'V': //mediatype video
                        mediaType = MediaType.VIDEO;
                        break;
                    case 'A':
                        mediaType = MediaType.AUDIO;
                        break;
                    case 'M':
                        mediaType = MediaType.MUXED;
                        break;
                    case 'm':
                        mediaType = GetType(args[i + 1]);
                        break;
                    //5. Set optional values
                    //TODO: Add container/file type, bitrate, converters etc.
                    case 't': //file type
                        mimeType = args[i + 1];
                        break;
                    case 's': //Skip if present
                        skipIfPresent = true;
                        break;
                    case 'P': //parallel mode
                        parallel = true;
                        break;
                    default:
                        Alert($"Unknown argument: --{a}", Type.WARNING, true, true);
                        break;
                }
            }
        }

        //Go to next arg thanks to recursive pattern
        if(args.Length >= i + 2)
        {
            await ExecuteArg(i + 1, args);
        }
        else
        {
            //Default: no parameters and only video url, no playlist
            if (i == 0 && args.Length == 1 && arg.Contains("?v="))
            {
                videoUrl = arg;
                downloadType = DownloadType.VIDEO;
                mediaType = MediaType.VIDEO;
            }

            try
            {
                await Start();
            }
            catch(Exception e)
            {
                Alert(e.Message, Type.ERROR, showType: true);
                //Alert(e.ToString(), Type.DEBUG);
            }

            Environment.Exit(0);
        }
    }

    static void Alert(string msg, Type type = Type.LOG, bool writeLine = true, bool showType = false, bool exitOnFatal = true)
    {
        Console.ForegroundColor = type switch
        {
            Type.SUCCESS => ConsoleColor.Green,
            Type.WARNING => ConsoleColor.Yellow,
            Type.ERROR => ConsoleColor.Red,
            Type.FATAL => ConsoleColor.Magenta,
            Type.INFO => ConsoleColor.Blue,
            Type.DEBUG => ConsoleColor.DarkGray,
            _ => ConsoleColor.White
        };

        if (showType) msg = $"[{type}] {msg}";
        Console.Write((writeLine) ? $"{msg}\r\n" : msg);
        Console.ResetColor();

        if (type == Type.FATAL && exitOnFatal)
            Environment.Exit(0);
    }

    static MediaType? GetType(string type)
    {
        return type.ToLower() switch
        {
            "v" => MediaType.VIDEO,
            "video" => MediaType.VIDEO,
            "a" => MediaType.AUDIO,
            "audio" => MediaType.AUDIO,
            "m" => MediaType.MUXED,
            "muxed" => MediaType.MUXED,
            _ => null
        };
    }

    static (int top, int left) GetCursor() => (Console.CursorTop, Console.CursorLeft);
    static void SetCursor((int top, int left) pos) => Console.SetCursorPosition(pos.left, pos.top);
}