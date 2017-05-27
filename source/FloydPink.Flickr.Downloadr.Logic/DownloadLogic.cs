using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FloydPink.Flickr.Downloadr.Logic.Interfaces;
using FloydPink.Flickr.Downloadr.Model;
using FloydPink.Flickr.Downloadr.Model.Enums;
using FloydPink.Flickr.Downloadr.Repository.Extensions;

namespace FloydPink.Flickr.Downloadr.Logic
{
  public class DownloadLogic : IDownloadLogic
  {
    private static readonly Random Random = new Random((int) DateTime.Now.Ticks);
    private readonly IOriginalTagsLogic _originalTagsLogic;
    private string _currentFolder;

    public DownloadLogic(IOriginalTagsLogic originalTagsLogic)
    {
      _originalTagsLogic = originalTagsLogic;
    }

    public async Task Download(IEnumerable<Photo> photos, CancellationToken cancellationToken, IProgress<ProgressUpdate> progress,
      Preferences preferences, Photoset photoset)
    {
      await DownloadPhotos(photos, cancellationToken, progress, preferences, photoset);
    }

    private async Task DownloadPhotos(IEnumerable<Photo> photos, CancellationToken cancellationToken, IProgress<ProgressUpdate> progress,
      Preferences preferences, Photoset photoset)
    {
      var operationTest = "Downloading photos...";

      var progressUpdate = new ProgressUpdate
      {
        Cancellable = true,
        OperationText = operationTest,
        Done = 0,
        ShowPercent = true
      };
      progress.Report(progressUpdate);

      var doneCount = 0;
      var photosList = photos as IList<Photo> ?? photos.ToList();
      var totalCount = photosList.Count();

      var imageDirectory = CreateDownloadFolder(preferences.DownloadLocation, photoset);

      var downloading = false;
      foreach (var photo in photosList)
      {
        // Give the server a break
        if(doneCount % 100 == 0 && downloading && doneCount > 0) {
          // These could both be preferences
          var delay = TimeSpan.FromMinutes(0.5); 
          var interval = TimeSpan.FromSeconds(1);

          while (delay > TimeSpan.FromTicks(0)){
            progressUpdate.OperationText = $"Giving the server a break: {delay}";
            progress.Report(progressUpdate);

            await Task.Delay(interval);
            delay = delay.Subtract(interval);
          }
        }
        
        var photoUrl = photo.OriginalUrl;
        var photoExtension = "jpg";
        switch (preferences.DownloadSize)
        {
          case PhotoDownloadSize.Medium:
            photoUrl = photo.Medium800Url;
            break;
          case PhotoDownloadSize.Large:
            photoUrl = photo.Large1024Url;
            break;
          case PhotoDownloadSize.Original:
            photoUrl = photo.OriginalUrl;
            photoExtension = photo.DownloadFormat;
            break;
        }

        var photoName = preferences.TitleAsFilename ? GetSafeFilename(photo.Title) : photo.Id;
        var targetFileName = Path.Combine(imageDirectory.FullName, string.Format("{0}.{1}", photoName, photoExtension));

        // Was the photo already downloaded
        var skipFileSize = 4096; // add to preferences maybe?
        if (File.Exists(targetFileName) && new FileInfo(targetFileName).Length > skipFileSize) {
          doneCount++;
          continue;
        }

        downloading = true;

        var photoWithPreferredTags = photo;
        if (preferences.NeedOriginalTags)
        {
          photoWithPreferredTags = await _originalTagsLogic.GetOriginalTagsTask(photo);
        }
        WriteMetaDataFile(photoWithPreferredTags, targetFileName, preferences);

        var request = WebRequest.Create(photoUrl);
        var buffer = new byte[4096];
        await DownloadAndSavePhoto(targetFileName, request, buffer);

        progressUpdate.OperationText = operationTest;
        progressUpdate.Done = doneCount++;
        progressUpdate.Total = totalCount;
        progressUpdate.DownloadedPath = imageDirectory.FullName;
        progress.Report(progressUpdate);
        if (doneCount != totalCount)
        {
          cancellationToken.ThrowIfCancellationRequested();
        }
      }
    }

    private static async Task DownloadAndSavePhoto(string targetFileName, WebRequest request, byte[] buffer)
    {
      // Download file before creating the local copy (in case of exceptions)
      using (var response = await request.GetResponseAsync())
      {
        using (var stream = response.GetResponseStream())
        {
          using (var target = new FileStream(targetFileName, FileMode.Create, FileAccess.Write))
          {
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
              await target.WriteAsync(buffer, 0, read);
            }
            target.Close();
          }
        }
      }
    }

    private DirectoryInfo CreateDownloadFolder(string downloadLocation, Photoset currentPhotoset, bool addTimeStamp = false)
    {
      _currentFolder = $"flickr-downloadr";

      if(addTimeStamp)
        _currentFolder += $"{GetDownloadFolderNameForPhotoset(currentPhotoset)}-{GetSafeFilename(DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"))}";

      var imageDirectory = Directory.CreateDirectory(Path.Combine(downloadLocation, _currentFolder));

      return imageDirectory;
    }

    private string GetDownloadFolderNameForPhotoset(Photoset photoset)
    {
      return photoset.Type == PhotosetType.Album ? string.Format("-[{0}]", GetSafeFilename(photoset.Title)) : string.Empty;
    }

    private static string RandomString(int size)
    {
      // http://stackoverflow.com/a/1122519/218882
      var builder = new StringBuilder();
      for (var i = 0; i < size; i++)
      {
        var ch = Convert.ToChar(Convert.ToInt32(Math.Floor(26*Random.NextDouble() + 65), CultureInfo.InvariantCulture));
        builder.Append(ch);
      }

      return builder.ToString();
    }

    private static string GetSafeFilename(string path)
    {
      // http://stackoverflow.com/a/333297/218882
      var safeFilename = Path.GetInvalidFileNameChars()
        .Aggregate(path, (current, c) => current.Replace(c, '-'));
      return string.IsNullOrWhiteSpace(safeFilename) ? RandomString(8) : safeFilename;
    }

    private static void WriteMetaDataFile(Photo photo, string targetFileName, Preferences preferences)
    {
      var metadata = preferences.Metadata.ToDictionary(metadatum => metadatum,
        metadatum =>
          photo.GetType()
            .GetProperty(metadatum)
            .GetValue(photo, null)
            .ToString());
      if (metadata.Count > 0)
      {
        File.WriteAllText(string.Format("{0}.json", targetFileName), metadata.ToJson(), Encoding.UTF8);
      }
    }
  }
}
