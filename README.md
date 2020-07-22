# YTDownloader
CLI application to download video's and playlists from Youtube. Based on YoutubeExplode

```
Usage:
YT2MP3.exe [arguments]

Show help page
        -h or 'help'    (Shows this page)

Download type
        -v      (Download a video)
        -p      (Download a playlist)

Set video/playlist URL or ID
        -u [url]        (Specify playlist or video by URL)
        -i [id]         (Specify playlist or video by ID)

Set output folder (optional)
        -o [path]       (Specify output path [optional])

Set the media type of the stream you want to download
        -V      (Video-only stream)
        -A      (Audio-only stream)
        -M      (Muxed stream, video and audio)
        -m [mediatype]  (Set by hand, accepting: 'v', 'video', 'm', 'muxed', 'a' and 'audio')

Other
        -I      (Don't download, but only show information)

Protip
You can use multiple arguments at once, like to show info about a video by id: YT2MP3 -vIi AkRiYsTN
You can also write this as: YT2MP4 -v -I -i AkRiYsTN
```
