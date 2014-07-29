﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Api.Episodes;
using NzbDrone.Api.REST;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Api.Mapping;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv;

namespace NzbDrone.Api.EpisodeFiles
{
    public class EpisodeModule : NzbDroneRestModuleWithSignalR<EpisodeFileResource, EpisodeFile>,
                                 IHandle<EpisodeFileAddedEvent>
    {
        private readonly IMediaFileService _mediaFileService;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly ISeriesService _seriesService;
        private readonly Logger _logger;

        public EpisodeModule(ICommandExecutor commandExecutor,
                             IMediaFileService mediaFileService,
                             IRecycleBinProvider recycleBinProvider,
                             ISeriesService seriesService,
                             Logger logger)
            : base(commandExecutor)
        {
            _mediaFileService = mediaFileService;
            _recycleBinProvider = recycleBinProvider;
            _seriesService = seriesService;
            _logger = logger;
            GetResourceById = GetEpisodeFile;
            GetResourceAll = GetEpisodeFiles;
            UpdateResource = SetQuality;
            DeleteResource = DeleteEpisodeFile;
        }

        private EpisodeFileResource GetEpisodeFile(int id)
        {
            var episodeFile = _mediaFileService.Get(id);
            var series = _seriesService.GetSeries(episodeFile.SeriesId);

            return MapToResource(series, episodeFile);
        }

        private List<EpisodeFileResource> GetEpisodeFiles()
        {
            var seriesId = (int?)Request.Query.SeriesId;

            if (seriesId == null)
            {
                throw new BadRequestException("seriesId is missing");
            }

            var series = _seriesService.GetSeries(seriesId.Value);

            return _mediaFileService.GetFilesBySeries(seriesId.Value)
                                    .Select(f => MapToResource(series, f)).ToList();
        }

        private void SetQuality(EpisodeFileResource episodeFileResource)
        {
            var episodeFile = _mediaFileService.Get(episodeFileResource.Id);
            episodeFile.Quality = episodeFileResource.Quality;
            _mediaFileService.Update(episodeFile);
        }

        private void DeleteEpisodeFile(int id)
        {
            var episodeFile = _mediaFileService.Get(id);
            var series = _seriesService.GetSeries(episodeFile.SeriesId);
            var fullPath = Path.Combine(series.Path, episodeFile.RelativePath);

            _logger.Info("Deleting episode file: {0}", fullPath);
            _recycleBinProvider.DeleteFile(fullPath);
            _mediaFileService.Delete(episodeFile);
        }

        private static EpisodeFileResource MapToResource(Core.Tv.Series series, EpisodeFile episodeFile)
        {
            var resource = episodeFile.InjectTo<EpisodeFileResource>();
            resource.Path = Path.Combine(series.Path, episodeFile.RelativePath);

            return resource;
        }

        public void Handle(EpisodeFileAddedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, message.EpisodeFile.Id);
        }
    }
}