﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Metadata.Files;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Metadata
{
    public class MetadataService
        : IHandle<MediaCoversUpdatedEvent>,
          IHandle<EpisodeImportedEvent>,
          IHandle<SeriesRenamedEvent>
    {
        private readonly IMetadataFactory _metadataFactory;
        private readonly IMetadataFileService _metadataFileService;
        private readonly ICleanMetadataService _cleanMetadataService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IEpisodeService _episodeService;
        private readonly IDiskProvider _diskProvider;
        private readonly IHttpProvider _httpProvider;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MetadataService(IMetadataFactory metadataFactory,
                               IMetadataFileService metadataFileService,
                               ICleanMetadataService cleanMetadataService,
                               IMediaFileService mediaFileService,
                               IEpisodeService episodeService,
                               IDiskProvider diskProvider,
                               IHttpProvider httpProvider,
                               IEventAggregator eventAggregator,
                               Logger logger)
        {
            _metadataFactory = metadataFactory;
            _metadataFileService = metadataFileService;
            _cleanMetadataService = cleanMetadataService;
            _mediaFileService = mediaFileService;
            _episodeService = episodeService;
            _diskProvider = diskProvider;
            _httpProvider = httpProvider;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void Handle(MediaCoversUpdatedEvent message)
        {
            _cleanMetadataService.Clean(message.Series);

            if (!_diskProvider.FolderExists(message.Series.Path))
            {
                _logger.Info("Series folder does not exist, skipping metadata creation");
                return;
            }

            var seriesMetadataFiles = _metadataFileService.GetFilesBySeries(message.Series.Id);
            var episodeFiles = GetEpisodeFiles(message.Series.Id);

            foreach (var consumer in _metadataFactory.Enabled())
            {
                var consumerFiles = GetMetadataFilesForConsumer(consumer, seriesMetadataFiles);
                var files = new List<MetadataFile>();

                files.AddIfNotNull(ProcessSeriesMetadata(consumer, message.Series, consumerFiles));
                files.AddRange(ProcessSeriesImages(consumer, message.Series, consumerFiles));
                files.AddRange(ProcessSeasonImages(consumer, message.Series, consumerFiles));

                foreach (var episodeFile in episodeFiles)
                {
                    files.AddIfNotNull(ProcessEpisodeMetadata(consumer, message.Series, episodeFile, consumerFiles));
                    files.AddRange(ProcessEpisodeImages(consumer, message.Series, episodeFile, consumerFiles));
                }

                _eventAggregator.PublishEvent(new MetadataFilesUpdated(files));
            }
        }

        public void Handle(EpisodeImportedEvent message)
        {
            foreach (var consumer in _metadataFactory.Enabled())
            {
                var files = new List<MetadataFile>();

                files.AddIfNotNull(ProcessEpisodeMetadata(consumer, message.EpisodeInfo.Series, message.ImportedEpisode, new List<MetadataFile>()));
                files.AddRange(ProcessEpisodeImages(consumer, message.EpisodeInfo.Series, message.ImportedEpisode, new List<MetadataFile>()));

                _eventAggregator.PublishEvent(new MetadataFilesUpdated(files));
            }
        }

        public void Handle(SeriesRenamedEvent message)
        {
            var seriesMetadata = _metadataFileService.GetFilesBySeries(message.Series.Id);
            var episodeFiles = GetEpisodeFiles(message.Series.Id);

            foreach (var consumer in _metadataFactory.Enabled())
            {
                var updatedMetadataFiles = consumer.AfterRename(message.Series,
                                                                GetMetadataFilesForConsumer(consumer, seriesMetadata),
                                                                episodeFiles);

                _eventAggregator.PublishEvent(new MetadataFilesUpdated(updatedMetadataFiles));
            }
        }

        private List<EpisodeFile> GetEpisodeFiles(int seriesId)
        {
            var episodeFiles = _mediaFileService.GetFilesBySeries(seriesId);
            var episodes = _episodeService.GetEpisodeBySeries(seriesId);

            foreach (var episodeFile in episodeFiles)
            {
                var localEpisodeFile = episodeFile;
                episodeFile.Episodes = new LazyList<Episode>(episodes.Where(e => e.EpisodeFileId == localEpisodeFile.Id));
            }

            return episodeFiles;
        }

        private List<MetadataFile> GetMetadataFilesForConsumer(IMetadata consumer, List<MetadataFile> seriesMetadata)
        {
            return seriesMetadata.Where(c => c.Consumer == consumer.GetType().Name).ToList();
        }

        private MetadataFile ProcessSeriesMetadata(IMetadata consumer, Series series, List<MetadataFile> existingMetadataFiles)
        {
            var seriesMetadata = consumer.SeriesMetadata(series);

            if (seriesMetadata == null)
            {
                return null;
            }

            var hash = seriesMetadata.Contents.SHA256Hash();

            var metadata = existingMetadataFiles.SingleOrDefault(e => e.Type == MetadataType.SeriesMetadata) ??
                               new MetadataFile
                               {
                                   SeriesId = series.Id,
                                   Consumer = consumer.GetType().Name,
                                   Type = MetadataType.SeriesMetadata,
                               };

            if (hash == metadata.Hash)
            {
                return null;
            }

            _logger.Debug("Writing Series Metadata to: {0}", seriesMetadata.Path);
            _diskProvider.WriteAllText(seriesMetadata.Path, seriesMetadata.Contents);

            metadata.Hash = hash;
            metadata.RelativePath = series.Path.GetRelativePath(seriesMetadata.Path);

            return metadata;
        }

        private MetadataFile ProcessEpisodeMetadata(IMetadata consumer, Series series, EpisodeFile episodeFile, List<MetadataFile> existingMetadataFiles)
        {
            var episodeMetadata = consumer.EpisodeMetadata(series, episodeFile);

            if (episodeMetadata == null)
            {
                return null;
            }

            var fullPath = Path.Combine(series.Path, episodeMetadata.Path);

            var existingMetadata = existingMetadataFiles.SingleOrDefault(c => c.Type == MetadataType.EpisodeMetadata &&
                                                                              c.EpisodeFileId == episodeFile.Id);

            if (existingMetadata != null)
            {
                var existingFullPath = Path.Combine(series.Path, existingMetadata.RelativePath);
                if (!episodeMetadata.Path.PathEquals(existingFullPath))
                {
                    _diskProvider.MoveFile(existingFullPath, fullPath);
                    existingMetadata.RelativePath = episodeMetadata.Path;
                }
            }

            var hash = episodeMetadata.Contents.SHA256Hash();

            var metadata = existingMetadata ??
                           new MetadataFile
                           {
                               SeriesId = series.Id,
                               EpisodeFileId = episodeFile.Id,
                               Consumer = consumer.GetType().Name,
                               Type = MetadataType.EpisodeMetadata,
                               RelativePath = episodeMetadata.Path
                           };

            if (hash == metadata.Hash)
            {
                return null;
            }

            _logger.Debug("Writing Episode Metadata to: {0}", fullPath);
            _diskProvider.WriteAllText(fullPath, episodeMetadata.Contents);

            metadata.Hash = hash;

            return metadata;
        }

        private List<MetadataFile> ProcessSeriesImages(IMetadata consumer, Series series, List<MetadataFile> existingMetadataFiles)
        {
            var result = new List<MetadataFile>();

            foreach (var image in consumer.SeriesImages(series))
            {
                if (_diskProvider.FileExists(image.Path))
                {
                    _logger.Debug("Series image already exists: {0}", image.Path);
                    continue;
                }

                var relativePath = series.Path.GetRelativePath(image.Path);

                var metadata = existingMetadataFiles.SingleOrDefault(c => c.Type == MetadataType.SeriesImage &&
                                                                          c.RelativePath == relativePath) ??
                               new MetadataFile
                               {
                                   SeriesId = series.Id,
                                   Consumer = consumer.GetType().Name,
                                   Type = MetadataType.SeriesImage,
                                   RelativePath = relativePath
                               };

                _diskProvider.CopyFile(image.Url, image.Path);

                result.Add(metadata);
            }

            return result;
        }

        private List<MetadataFile> ProcessSeasonImages(IMetadata consumer, Series series, List<MetadataFile> existingMetadataFiles)
        {
            var result = new List<MetadataFile>();

            foreach (var season in series.Seasons)
            {
                foreach (var image in consumer.SeasonImages(series, season))
                {
                    if (_diskProvider.FileExists(image.Path))
                    {
                        _logger.Debug("Season image already exists: {0}", image.Path);
                        continue;
                    }

                    var relativePath = series.Path.GetRelativePath(image.Path);

                    var metadata = existingMetadataFiles.SingleOrDefault(c => c.Type == MetadataType.SeasonImage &&
                                                                            c.SeasonNumber == season.SeasonNumber &&
                                                                            c.RelativePath == relativePath) ??
                                new MetadataFile
                                {
                                    SeriesId = series.Id,
                                    SeasonNumber = season.SeasonNumber,
                                    Consumer = consumer.GetType().Name,
                                    Type = MetadataType.SeasonImage,
                                    RelativePath = relativePath
                                };

                    DownloadImage(series, image.Url, image.Path);

                    result.Add(metadata);
                }
            }

            return result;
        }

        private List<MetadataFile> ProcessEpisodeImages(IMetadata consumer, Series series, EpisodeFile episodeFile, List<MetadataFile> existingMetadataFiles)
        {
            var result = new List<MetadataFile>();

            foreach (var image in consumer.EpisodeImages(series, episodeFile))
            {
                var fullPath = Path.Combine(series.Path, image.Path);

                if (_diskProvider.FileExists(image.Path))
                {
                    _logger.Debug("Episode image already exists: {0}", image.Path);
                    continue;
                }

                var existingMetadata = existingMetadataFiles.FirstOrDefault(c => c.Type == MetadataType.EpisodeImage &&
                                                                                  c.EpisodeFileId == episodeFile.Id);

                if (existingMetadata != null)
                {
                    var existingFullPath = Path.Combine(series.Path, existingMetadata.RelativePath);
                    if (!fullPath.PathEquals(existingFullPath))
                    {
                        _diskProvider.MoveFile(fullPath, fullPath);
                        existingMetadata.RelativePath = image.Path;

                        return new List<MetadataFile>{ existingMetadata };
                    }
                }

                var metadata = existingMetadata ??
                               new MetadataFile
                               {
                                   SeriesId = series.Id,
                                   EpisodeFileId = episodeFile.Id,
                                   Consumer = consumer.GetType().Name,
                                   Type = MetadataType.EpisodeImage,
                                   RelativePath = series.Path.GetRelativePath(image.Path)
                               };

                DownloadImage(series, image.Url, image.Path);

                result.Add(metadata);
            }

            return result;
        }

        private void DownloadImage(Series series, string url, string path)
        {
            try
            {
                _httpProvider.DownloadFile(url, path);
            }
            catch (WebException e)
            {
                _logger.Warn(string.Format("Couldn't download image {0} for {1}. {2}", url, series, e.Message));
            }
            catch (Exception e)
            {
                _logger.ErrorException("Couldn't download image " + url + " for " + series, e);
            }
        }
    }
}
