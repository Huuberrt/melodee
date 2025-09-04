using System.Text;
using Melodee.Common.Enums;
using Melodee.Common.Metadata.AudioTags.Interfaces;
using Melodee.Common.Metadata.AudioTags.Models;
using Melodee.Common.Utility;

namespace Melodee.Common.Metadata.AudioTags.Writers;

public class Id3v2TagWriter : ITagWriter
{
    public Task WriteTagsAsync(string filePath, IDictionary<MetaTagIdentifier, object> tags, CancellationToken cancellationToken = default)
    {
        // Use ATL to update common tag fields
        var track = new ATL.Track(filePath);

        foreach (var kv in tags)
        {
            var value = kv.Value;
            switch (kv.Key)
            {
                case MetaTagIdentifier.Title:
                    track.Title = value?.ToString();
                    break;
                case MetaTagIdentifier.Album:
                    track.Album = value?.ToString();
                    break;
                case MetaTagIdentifier.Artist:
                    track.Artist = value?.ToString();
                    break;
                case MetaTagIdentifier.AlbumArtist:
                    track.AlbumArtist = value?.ToString();
                    break;
                case MetaTagIdentifier.TrackNumber:
                    track.TrackNumber = SafeParser.ToNumber<int>(value);
                    break;
                case MetaTagIdentifier.SongTotal:
                case MetaTagIdentifier.SongNumberTotal:
                    track.TrackTotal = SafeParser.ToNumber<int>(value);
                    break;
                case MetaTagIdentifier.RecordingYear:
                case MetaTagIdentifier.RecordingDateOrYear:
                    track.Date = SafeParser.ToDateTime(value?.ToString());
                    break;
                case MetaTagIdentifier.Genre:
                    track.Genre = value?.ToString();
                    break;
                case MetaTagIdentifier.Comment:
                    track.Comment = value?.ToString();
                    break;
                default:
                    break;
            }
        }

        track.Save();
        return Task.CompletedTask;
    }

    public async Task WriteTagAsync(string filePath, MetaTagIdentifier tagId, object value, CancellationToken cancellationToken = default)
    {
        // Wrap WriteTagsAsync for a single tag
        await WriteTagsAsync(filePath, new Dictionary<MetaTagIdentifier, object> { { tagId, value } }, cancellationToken);
    }

    public Task RemoveTagAsync(string filePath, MetaTagIdentifier tagId, CancellationToken cancellationToken = default)
    {
        var track = new ATL.Track(filePath);
        switch (tagId)
        {
            case MetaTagIdentifier.Title:
                track.Title = null;
                break;
            case MetaTagIdentifier.Album:
                track.Album = null;
                break;
            case MetaTagIdentifier.Artist:
                track.Artist = null;
                break;
            case MetaTagIdentifier.AlbumArtist:
                track.AlbumArtist = null;
                break;
            case MetaTagIdentifier.Genre:
                track.Genre = null;
                break;
            case MetaTagIdentifier.Comment:
                track.Comment = null;
                break;
            default:
                break;
        }
        track.Save();
        return Task.CompletedTask;
    }

    public Task AddImageAsync(string filePath, AudioImage image, CancellationToken cancellationToken = default)
    {
        var track = new ATL.Track(filePath);
        var picType = ATL.PictureInfo.PIC_TYPE.Front;
        if (image.Type == PictureIdentifier.Back) picType = ATL.PictureInfo.PIC_TYPE.Back;
        track.EmbeddedPictures.Add(ATL.PictureInfo.fromBinaryData(image.Data.ToArray(), picType));
        track.Save();
        return Task.CompletedTask;
    }

    public Task RemoveImagesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var track = new ATL.Track(filePath);
        track.EmbeddedPictures.Clear();
        track.Save();
        return Task.CompletedTask;
    }
}
