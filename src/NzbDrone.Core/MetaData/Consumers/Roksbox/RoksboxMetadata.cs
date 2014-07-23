﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Metadata.Files;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Metadata.Consumers.Roksbox
{
    public class RoksboxMetadata : MetadataBase<RoksboxMetadataSettings>
    {
        private readonly IMapCoversToLocal _mediaCoverService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public RoksboxMetadata(IMapCoversToLocal mediaCoverService,
                            IDiskProvider diskProvider,
                            Logger logger)
        {
            _mediaCoverService = mediaCoverService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        private static List<string> ValidCertification = new List<string> { "G", "NC-17", "PG", "PG-13", "R", "UR", "UNRATED", "NR", "TV-Y", "TV-Y7", "TV-Y7-FV", "TV-G", "TV-PG", "TV-14", "TV-MA" };
        private static readonly Regex SeasonImagesRegex = new Regex(@"^(season (?<season>\d+))|(?<specials>specials)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public override List<MetadataFile> AfterRename(Series series, List<MetadataFile> existingMetadataFiles, List<EpisodeFile> episodeFiles)
        {
            var episodeFilesMetadata = existingMetadataFiles.Where(c => c.EpisodeFileId > 0).ToList();
            var updatedMetadataFiles = new List<MetadataFile>();

            foreach (var episodeFile in episodeFiles)
            {
                var metadataFiles = episodeFilesMetadata.Where(m => m.EpisodeFileId == episodeFile.Id).ToList();

                foreach (var metadataFile in metadataFiles)
                {
                    string newFilename;

                    if (metadataFile.Type == MetadataType.EpisodeImage)
                    {
                        newFilename = GetEpisodeImageFilename(episodeFile.RelativePath);
                    }

                    else if (metadataFile.Type == MetadataType.EpisodeMetadata)
                    {
                        newFilename = GetEpisodeMetadataFilename(episodeFile.RelativePath);
                    }

                    else
                    {
                        _logger.Trace("Unknown episode file metadata: {0}", metadataFile.RelativePath);
                        continue;
                    }

                    var existingFilename = Path.Combine(series.Path, metadataFile.RelativePath);
                    newFilename = Path.Combine(series.Path, newFilename);

                    if (!newFilename.PathEquals(existingFilename))
                    {
                        _diskProvider.MoveFile(existingFilename, newFilename);
                        metadataFile.RelativePath = series.Path.GetRelativePath(newFilename);

                        updatedMetadataFiles.Add(metadataFile);
                    }
                }
            }

            return updatedMetadataFiles;
        }

        public override MetadataFile FindMetadataFile(Series series, string path)
        {
            var filename = Path.GetFileName(path);

            if (filename == null) return null;
            var parentdir = Directory.GetParent(path);

            var metadata = new MetadataFile
                           {
                               SeriesId = series.Id,
                               Consumer = GetType().Name,
                               RelativePath = series.Path.GetRelativePath(path)
                           };

            //Series and season images are both named folder.jpg, only season ones sit in season folders
            if (Path.GetFileNameWithoutExtension(filename).Equals(parentdir.Name, StringComparison.InvariantCultureIgnoreCase))
            {
                var seasonMatch = SeasonImagesRegex.Match(parentdir.Name);

                if (seasonMatch.Success)
                {
                    metadata.Type = MetadataType.SeasonImage;

                    if (seasonMatch.Groups["specials"].Success)
                    {
                        metadata.SeasonNumber = 0;
                    }

                    else
                    {
                        metadata.SeasonNumber = Convert.ToInt32(seasonMatch.Groups["season"].Value);
                    }

                    return metadata;
                }

                else
                {
                    metadata.Type = MetadataType.SeriesImage;
                    return metadata;
                }
            }

            var parseResult = Parser.Parser.ParseTitle(filename);

            if (parseResult != null &&
                !parseResult.FullSeason)
            {
                var extension = Path.GetExtension(filename).ToLowerInvariant();

                if (extension == ".xml")
                {
                    metadata.Type = MetadataType.EpisodeMetadata;
                    return metadata;
                }

                if (extension == ".jpg")
                {
                    if (!Path.GetFileNameWithoutExtension(filename).EndsWith("-thumb"))
                    {
                        metadata.Type = MetadataType.EpisodeImage;
                        return metadata;
                    }
                }                
            }

            return null;
        }

        public override MetadataFileResult SeriesMetadata(Series series)
        {
            //Series metadata is not supported
            return null;
        }

        public override MetadataFileResult EpisodeMetadata(Series series, EpisodeFile episodeFile)
        {
            if (!Settings.EpisodeMetadata)
            {
                return null;
            }
            
            _logger.Debug("Generating Episode Metadata for: {0}", episodeFile.RelativePath);

            var xmlResult = String.Empty;
            foreach (var episode in episodeFile.Episodes.Value)
            {
                var sb = new StringBuilder();
                var xws = new XmlWriterSettings();
                xws.OmitXmlDeclaration = true;
                xws.Indent = false;

                using (var xw = XmlWriter.Create(sb, xws))
                {
                    var doc = new XDocument();

                    var details = new XElement("video");
                    details.Add(new XElement("title", String.Format("{0} - {1}x{2} - {3}", series.Title, episode.SeasonNumber, episode.EpisodeNumber, episode.Title)));
                    details.Add(new XElement("year", episode.AirDate));
                    details.Add(new XElement("genre", String.Join(" / ", series.Genres)));
                    var actors = String.Join(" , ", series.Actors.ConvertAll(c => c.Name + " - " + c.Character).GetRange(0, Math.Min(3, series.Actors.Count)));
                    details.Add(new XElement("actors", actors));
                    details.Add(new XElement("description", episode.Overview));
                    details.Add(new XElement("length", series.Runtime));
                    details.Add(new XElement("mpaa", ValidCertification.Contains(series.Certification.ToUpperInvariant()) ? series.Certification.ToUpperInvariant() : "UNRATED"));
                    doc.Add(details);
                    doc.Save(xw);

                    xmlResult += doc.ToString();
                    xmlResult += Environment.NewLine;
                }
            }

            return new MetadataFileResult(GetEpisodeMetadataFilename(episodeFile.RelativePath), xmlResult.Trim(Environment.NewLine.ToCharArray()));
        }

        public override List<ImageFileResult> SeriesImages(Series series)
        {
            var image = series.Images.SingleOrDefault(c => c.CoverType == MediaCoverTypes.Poster) ?? series.Images.FirstOrDefault();
            if (image == null)
            {
                _logger.Trace("Failed to find suitable Series image for series {0}.", series.Title);
                return null;
            }

            var source = _mediaCoverService.GetCoverPath(series.Id, image.CoverType);
            var destination = Path.Combine(series.Path, Path.GetFileName(series.Path) + Path.GetExtension(source));

            return new List<ImageFileResult>{ new ImageFileResult(destination, source) };
        }

        public override List<ImageFileResult> SeasonImages(Series series, Season season)
        {
            var seasonFolders = GetSeasonFolders(series);

            string seasonFolder;
            if (!seasonFolders.TryGetValue(season.SeasonNumber, out seasonFolder))
            {
                _logger.Trace("Failed to find season folder for series {0}, season {1}.", series.Title, season.SeasonNumber);
                return new List<ImageFileResult>();
            }

            //Roksbox only supports one season image, so first of all try for poster otherwise just use whatever is first in the collection
            var image = season.Images.SingleOrDefault(c => c.CoverType == MediaCoverTypes.Poster) ?? season.Images.FirstOrDefault();
            if (image == null)
            {
                _logger.Trace("Failed to find suitable season image for series {0}, season {1}.", series.Title, season.SeasonNumber);
                return new List<ImageFileResult>();
            }

            var filename = Path.GetFileName(seasonFolder) + ".jpg";
            var path = Path.Combine(series.Path, seasonFolder, filename);

            return new List<ImageFileResult> { new ImageFileResult(path, image.Url) };
        }

        public override List<ImageFileResult> EpisodeImages(Series series, EpisodeFile episodeFile)
        {
            var screenshot = episodeFile.Episodes.Value.First().Images.SingleOrDefault(i => i.CoverType == MediaCoverTypes.Screenshot);

            if (screenshot == null)
            {
                _logger.Trace("Episode screenshot not available");
                return new List<ImageFileResult>();
            }

            return new List<ImageFileResult> {new ImageFileResult(GetEpisodeImageFilename(episodeFile.RelativePath), screenshot.Url)};
        }

        private string GetEpisodeMetadataFilename(string episodeFilePath)
        {
            return Path.ChangeExtension(episodeFilePath, "xml");
        }

        private string GetEpisodeImageFilename(string episodeFilePath)
        {
            return Path.ChangeExtension(episodeFilePath, "jpg");
        }

        private Dictionary<Int32, String> GetSeasonFolders(Series series)
        {
            var seasonFolderMap = new Dictionary<Int32, String>();

            foreach (var folder in _diskProvider.GetDirectories(series.Path))
            {
                var directoryinfo = new DirectoryInfo(folder);
                var seasonMatch = SeasonImagesRegex.Match(directoryinfo.Name);

                if (seasonMatch.Success)
                {
                    var seasonNumber = seasonMatch.Groups["season"].Value;

                    if (seasonNumber.Contains("specials"))
                    {
                        seasonFolderMap[0] = folder;
                    }
                    else
                    {
                        int matchedSeason;
                        if (Int32.TryParse(seasonNumber, out matchedSeason))
                        {
                            seasonFolderMap[matchedSeason] = folder;
                        }
                        else
                        {
                            _logger.Debug("Failed to parse season number from {0} for series {1}.", folder, series.Title);
                        }
                    }
                }
                else
                {
                    _logger.Debug("Rejecting folder {0} for series {1}.", Path.GetDirectoryName(folder), series.Title);
                }
            }

            return seasonFolderMap;
        }
    }
}
